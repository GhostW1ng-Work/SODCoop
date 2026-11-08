using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        const byte MsgVoice = 46;
        const int VoiceRate = 16000, VoiceFrame = 320, VoiceBufs = 6, VoiceOutBufs = 4, VoiceStartFrames = 3, VoiceMaxFrames = 12;

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormat { public ushort tag; public ushort channels; public uint rate; public uint bytesPerSec; public ushort blockAlign; public ushort bits; public ushort extra; }

        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, uint dev, ref WaveFormat f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WaveFormat f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        const uint WaveMapper = 0xFFFFFFFF, HdrSize = 48;
        const int HdrFlags = 24, HdrRecorded = 12, WhdrDone = 1;

        sealed class Speaker
        {
            public readonly Queue<short[]> frames = new Queue<short[]>();
            public bool playing, seqKnown;
            public ushort lastSeq;
            public volatile float gainL, gainR;
            public float heardAt;
        }

        static readonly Dictionary<int, Speaker> speakers = new Dictionary<int, Speaker>();
        static readonly ConcurrentQueue<byte[]> voiceOut = new ConcurrentQueue<byte[]>();
        static Thread micThread, spkThread;
        static volatile bool micRun, spkRun, voiceTalking;
        static float voiceTalkUntil, micIdleAt, voiceFailAt;
        static ushort voiceSeq;
        static string voiceKeyName = "";
        static KeyCode voiceKey = KeyCode.V;
        static bool voiceLogged;

        internal static KeyCode VoiceKey()
        {
            string s = Plugin.CfgVoiceKey.Value ?? "V";
            if (s != voiceKeyName) { voiceKeyName = s; KeyCode k; voiceKey = Enum.TryParse<KeyCode>(s, true, out k) ? k : KeyCode.V; }
            return voiceKey;
        }

        static readonly int[] AdpcmIndex = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
        static readonly int[] AdpcmStep = {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166,
            1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845,
            8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767 };

        static byte[] AdpcmEncode(short[] pcm)
        {
            var o = new byte[3 + (pcm.Length - 1 + 1) / 2];
            int pred = pcm[0], idx = 0;
            o[0] = (byte)pred; o[1] = (byte)(pred >> 8); o[2] = (byte)idx;
            for (int i = 1; i < pcm.Length; i++)
            {
                int step = AdpcmStep[idx], diff = pcm[i] - pred, code = 0;
                if (diff < 0) { code = 8; diff = -diff; }
                int delta = step >> 3;
                if (diff >= step) { code |= 4; diff -= step; delta += step; }
                step >>= 1;
                if (diff >= step) { code |= 2; diff -= step; delta += step; }
                step >>= 1;
                if (diff >= step) { code |= 1; delta += step; }
                pred += (code & 8) != 0 ? -delta : delta;
                pred = Math.Clamp(pred, -32768, 32767);
                idx = Math.Clamp(idx + AdpcmIndex[code], 0, 88);
                int b = 3 + (i - 1) / 2;
                if (((i - 1) & 1) == 0) o[b] = (byte)code; else o[b] |= (byte)(code << 4);
            }
            return o;
        }

        static short[] AdpcmDecode(byte[] d, int off, int len)
        {
            if (len < 3) return null;
            var o = new short[VoiceFrame];
            int pred = (short)(d[off] | (d[off + 1] << 8)), idx = Math.Clamp((int)d[off + 2], 0, 88);
            o[0] = (short)pred;
            for (int i = 1; i < VoiceFrame; i++)
            {
                int b = 3 + (i - 1) / 2;
                if (b >= len) break;
                int code = ((i - 1) & 1) == 0 ? d[off + b] & 15 : d[off + b] >> 4;
                int step = AdpcmStep[idx], delta = step >> 3;
                if ((code & 4) != 0) delta += step;
                if ((code & 2) != 0) delta += step >> 1;
                if ((code & 1) != 0) delta += step >> 2;
                pred += (code & 8) != 0 ? -delta : delta;
                pred = Math.Clamp(pred, -32768, 32767);
                idx = Math.Clamp(idx + AdpcmIndex[code], 0, 88);
                o[i] = (short)pred;
            }
            return o;
        }

        static WaveFormat VoiceFormat(int channels)
        {
            var f = new WaveFormat { tag = 1, channels = (ushort)channels, rate = VoiceRate, bits = 16 };
            f.blockAlign = (ushort)(channels * 2); f.bytesPerSec = (uint)(VoiceRate * f.blockAlign);
            return f;
        }

        static IntPtr[] AllocHeaders(int count, int bytes)
        {
            var hs = new IntPtr[count];
            for (int i = 0; i < count; i++)
            {
                hs[i] = Marshal.AllocHGlobal((int)HdrSize);
                for (int b = 0; b < HdrSize; b += 8) Marshal.WriteInt64(hs[i], b, 0);
                Marshal.WriteIntPtr(hs[i], 0, Marshal.AllocHGlobal(bytes));
                Marshal.WriteInt32(hs[i], 8, bytes);
            }
            return hs;
        }

        static void FreeHeaders(IntPtr[] hs)
        {
            foreach (var h in hs) { Marshal.FreeHGlobal(Marshal.ReadIntPtr(h, 0)); Marshal.FreeHGlobal(h); }
        }

        static void MicStart()
        {
            if (micRun) return;
            try { if (micThread != null && micThread.IsAlive) micThread.Join(300); } catch (Exception __e) { Err.Swallow(__e); }
            micRun = true;
            micThread = new Thread(MicLoop) { IsBackground = true, Name = "SodCoop mic" };
            micThread.Start();
        }

        static void MicLoop()
        {
            var fmt = VoiceFormat(1);
            IntPtr h;
            int err = waveInOpen(out h, WaveMapper, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (err != 0)
            {
                Plugin.L.LogWarning($"[VOICE] микрофон не открылся (код {err})");
                micRun = false; voiceMicFailed = true;
                return;
            }
            Plugin.L.LogInfo("[VOICE] микрофон открыт");
            var hs = AllocHeaders(VoiceBufs, VoiceFrame * 2);
            try
            {
                foreach (var hd in hs) { waveInPrepareHeader(h, hd, HdrSize); waveInAddBuffer(h, hd, HdrSize); }
                waveInStart(h);
                var pcm = new short[VoiceFrame];
                int next = 0;
                while (micRun)
                {
                    var hd = hs[next];
                    if ((Marshal.ReadInt32(hd, HdrFlags) & WhdrDone) == 0) { Thread.Sleep(2); continue; }
                    int got = Marshal.ReadInt32(hd, HdrRecorded) / 2;
                    if (voiceTalking && got == VoiceFrame)
                    {
                        Marshal.Copy(Marshal.ReadIntPtr(hd, 0), pcm, 0, VoiceFrame);
                        voiceOut.Enqueue(AdpcmEncode(pcm));
                    }
                    waveInAddBuffer(h, hd, HdrSize);
                    next = (next + 1) % hs.Length;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[VOICE] микрофон: " + e.Message); }
            finally
            {
                waveInReset(h);
                foreach (var hd in hs) waveInUnprepareHeader(h, hd, HdrSize);
                waveInClose(h);
                FreeHeaders(hs);
                micRun = false;
                Plugin.L.LogInfo("[VOICE] микрофон закрыт");
            }
        }

        static volatile bool voiceMicFailed;

        static void SpkStart()
        {
            if (spkRun) return;
            spkRun = true;
            spkThread = new Thread(SpkLoop) { IsBackground = true, Name = "SodCoop voice out" };
            spkThread.Start();
        }

        static void SpkLoop()
        {
            var fmt = VoiceFormat(2);
            IntPtr h;
            int err = waveOutOpen(out h, WaveMapper, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (err != 0) { Plugin.L.LogWarning($"[VOICE] вывод звука не открылся (код {err})"); spkRun = false; return; }
            var hs = AllocHeaders(VoiceOutBufs, VoiceFrame * 4);
            var mix = new int[VoiceFrame * 2];
            var outBuf = new short[VoiceFrame * 2];
            try
            {
                foreach (var hd in hs) { waveOutPrepareHeader(h, hd, HdrSize); Marshal.WriteInt32(hd, HdrFlags, Marshal.ReadInt32(hd, HdrFlags) | WhdrDone); }
                int next = 0;
                while (spkRun)
                {
                    var hd = hs[next];
                    if ((Marshal.ReadInt32(hd, HdrFlags) & WhdrDone) == 0) { Thread.Sleep(2); continue; }
                    Array.Clear(mix, 0, mix.Length);
                    lock (speakers)
                    {
                        foreach (var s in speakers.Values)
                        {
                            if (!s.playing && s.frames.Count >= VoiceStartFrames) s.playing = true;
                            if (!s.playing) continue;
                            if (s.frames.Count == 0) { s.playing = false; continue; }
                            var f = s.frames.Dequeue();
                            float gl = s.gainL, gr = s.gainR;
                            if (gl <= 0f && gr <= 0f) continue;
                            for (int i = 0; i < VoiceFrame; i++) { mix[i * 2] += (int)(f[i] * gl); mix[i * 2 + 1] += (int)(f[i] * gr); }
                        }
                    }
                    for (int i = 0; i < mix.Length; i++) outBuf[i] = (short)Math.Clamp(mix[i], -32768, 32767);
                    Marshal.Copy(outBuf, 0, Marshal.ReadIntPtr(hd, 0), outBuf.Length);
                    Marshal.WriteInt32(hd, HdrFlags, Marshal.ReadInt32(hd, HdrFlags) & ~WhdrDone);
                    waveOutWrite(h, hd, HdrSize);
                    next = (next + 1) % hs.Length;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[VOICE] вывод звука: " + e.Message); }
            finally
            {
                waveOutReset(h);
                foreach (var hd in hs) waveOutUnprepareHeader(h, hd, HdrSize);
                waveOutClose(h);
                FreeHeaders(hs);
                spkRun = false;
            }
        }

        static void VoiceTick()
        {
            if (!Plugin.CfgVoice.Value || manager == null) { if (micRun || spkRun) VoiceShutdown(); return; }
            float now = Time.realtimeSinceStartup;
            bool held = false;
            if (!GhostMe && !InMainMenu() && !NetMenu.Open && !cheatOpen && !inviteOpen) { try { held = KeyHeld(VoiceKey()); } catch (Exception __e) { Err.Swallow(__e); } }
            if (held) voiceTalkUntil = now + 0.15f;
            bool talk = held || now < voiceTalkUntil;
            if (talk && !micRun && !voiceMicFailed) MicStart();
            if (talk && voiceMicFailed && now >= voiceFailAt)
            {
                voiceFailAt = now + 30f;
                Toast(UiKit.T("Microphone is not available. Check that it is connected and that Windows allows apps to use it.", "Микрофон недоступен. Проверь, что он подключён и что Windows разрешает приложениям доступ к микрофону."));
            }
            if (talk != voiceTalking)
            {
                voiceTalking = talk;
                if (!voiceLogged && talk) { voiceLogged = true; Plugin.L.LogInfo($"[VOICE] говорю (клавиша {VoiceKey()})"); }
            }
            if (talk) micIdleAt = now + 10f;
            else if (micRun && now > micIdleAt) micRun = false;

            byte[] frame;
            int sent = 0;
            while (voiceOut.TryDequeue(out frame))
            {
                if (sent++ > 10) continue;
                var w = new NetDataWriter();
                w.Put(MsgVoice); w.Put(voiceSeq++); w.Put(frame);
                manager.SendToAll(w, DeliveryMethod.Unreliable);
            }
            VoiceGains(now);
        }

        static void VoiceGains(float now)
        {
            Transform lis = null;
            try { var cam = Camera.main; if (cam != null) lis = cam.transform; } catch (Exception __e) { Err.Swallow(__e); }
            float range = Mathf.Max(2f, Plugin.CfgVoiceRange.Value), vol = Mathf.Clamp(Plugin.CfgVoiceVolume.Value, 0f, 3f);
            lock (speakers)
            {
                foreach (var kv in speakers)
                {
                    var s = kv.Value;
                    var rp = RpByPid(kv.Key);
                    if (rp == null || lis == null) { s.gainL = s.gainR = 0f; continue; }
                    Vector3 sp = rp.avatar != null ? rp.avatar.transform.position + Vector3.up * 1.5f : rp.targetPos + Vector3.up * 1.5f;
                    Vector3 d = sp - lis.position;
                    float dist = d.magnitude;
                    float g = dist >= range ? 0f : Mathf.Pow(1f - dist / range, 1.5f) * vol;
                    float pan = dist > 0.5f ? Vector3.Dot(d / dist, lis.right) : 0f;
                    s.gainL = g * (1f - Mathf.Max(0f, pan) * 0.6f);
                    s.gainR = g * (1f - Mathf.Max(0f, -pan) * 0.6f);
                }
            }
        }

        static void OnVoiceMsg(RP rp, NetDataReader r)
        {
            if (rp == null || !Plugin.CfgVoice.Value) return;
            ushort seq = r.GetUShort();
            byte[] data = r.GetRemainingBytes();
            var pcm = AdpcmDecode(data, 0, data.Length);
            if (pcm == null) return;
            lock (speakers)
            {
                Speaker s;
                if (!speakers.TryGetValue(rp.pid, out s)) { s = new Speaker(); speakers[rp.pid] = s; Plugin.L.LogInfo($"[VOICE] слышу игрока {rp.pid}"); }
                if (s.seqKnown && (short)(seq - s.lastSeq) <= 0) return;
                s.seqKnown = true; s.lastSeq = seq; s.heardAt = Time.realtimeSinceStartup;
                s.frames.Enqueue(pcm);
                while (s.frames.Count > VoiceMaxFrames) s.frames.Dequeue();
            }
            SpkStart();
        }

        static void VoiceShutdown()
        {
            micRun = false; spkRun = false; voiceTalking = false; voiceMicFailed = false;
            byte[] x; while (voiceOut.TryDequeue(out x)) { }
            lock (speakers) speakers.Clear();
        }

        static void DrawVoiceHud(GUIStyle st, GUIStyle sh)
        {
            if (!Plugin.CfgVoice.Value) return;
            var lines = new List<string>();
            if (voiceTalking && micRun) lines.Add(UiKit.T("● You are talking", "● Ты говоришь"));
            float now = Time.realtimeSinceStartup;
            lock (speakers)
                foreach (var kv in speakers)
                    if (now - kv.Value.heardAt < 0.4f && (kv.Value.gainL > 0.01f || kv.Value.gainR > 0.01f))
                        lines.Add("● " + PlayerLabel(kv.Key));
            float y = 1080f - 60f - lines.Count * 28f;
            foreach (var l in lines)
            {
                GUI.Label(new Rect(21f, y + 1f, 600f, 30f), l, sh);
                GUI.Label(new Rect(20f, y, 600f, 30f), l, st); y += 28f;
            }
        }
    }
}

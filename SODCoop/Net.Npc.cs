using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        static System.Collections.Generic.Dictionary<int, Citizen> npcById;
        static float npcMapAt, npcSendAt, npcLogAt;
        static int npcRoll;
        static bool npcErrLogged, npcClientPaused, npcMissLogged;
        static int npcSentCount, npcRecvCount;

        class NpcTarget
        {
            public Citizen c;
            public Vector3 pos; public float yaw, speed;
            public byte flags, flags2, idle, arms, carryItem, combatArms, outfit = 255;
            public int apOutfit = -1, apKo = -1; public float outfitTry;
            public bool fresh = true, snap = true, wasTalking;
            public float lastLoc; public Vector3 lastLocPos;
            public float lastSpeed = -1f;
            public int ap = -1;
            public int apIdle = -1, apArms = -1, apCarry = -1, apCombat = -1;
            public float atkUntil, combatHoldUntil; public bool atkHeld; public int curCombat = -1;
            public int rev, doneRev = -1; public bool settled; public int slot;
            public float getUpCheckAt;
        }

        static int npcFrame;
        static readonly System.Collections.Generic.Dictionary<int, NpcTarget> npcTargets = new System.Collections.Generic.Dictionary<int, NpcTarget>();

        static void BuildNpcMap()
        {
            npcMapAt = Time.unscaledTime;
            if (npcById == null) npcById = new System.Collections.Generic.Dictionary<int, Citizen>();
            var list = CityData.Instance.citizenDirectory;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                try { npcById[c.humanID] = c; } catch (Exception __e) { Err.Swallow(__e); }
            }
        }

        internal static bool ForcingPlay;
        static bool invokesKept, worldAliveLogged;

        static bool KeepWorld()
        {
            return Rules.WorldNeverPauses && manager != null && RtMode != NetMode.Off && Started;
        }

        internal static bool ForcePlayBegin()
        {
            if (ForcingPlay || !KeepWorld()) return false;
            var sd = SessionData.Instance;
            if (sd == null || sd.play) return false;
            try { if (!sd.startedGame) return false; } catch (Exception) { return false; }
            sd.play = true; ForcingPlay = true;
            if (!worldAliveLogged) { worldAliveLogged = true; Plugin.L.LogInfo("[PAUSE] игра на паузе, но мир продолжает жить (мультиплеер)"); }
            return true;
        }

        internal static void ForcePlayEnd()
        {
            ForcingPlay = false;
            try { var sd = SessionData.Instance; if (sd != null) sd.play = false; } catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static bool OnGameSpeedChange(CitizenBehaviour cb)
        {
            var sd = SessionData.Instance;
            if (sd == null) return true;
            if (!sd.play)
            {
                if (!KeepWorld()) return true;
                invokesKept = true;
                worldAliveLogged = false;
                return false;
            }
            if (invokesKept) { invokesKept = false; try { cb.CancelInvoke(); } catch (Exception __e) { Err.Swallow(__e); } }
            return true;
        }

        static readonly System.Collections.Generic.HashSet<int> tickRaised = new System.Collections.Generic.HashSet<int>();
        static readonly System.Collections.Generic.List<Vector3> remoteSpots = new System.Collections.Generic.List<Vector3>();
        static float tickRaiseAt, remoteSpotsAt;
        static int tickRaiseLogN;

        static void RefreshRemoteSpots()
        {
            if (Time.unscaledTime - remoteSpotsAt < 0.5f) return;
            remoteSpotsAt = Time.unscaledTime;
            remoteSpots.Clear();
            SyncCur();
            foreach (var rp in players) if (rp.avatar != null) remoteSpots.Add(rp.targetPos);
        }

        static NewAIController.AITickRate RateNearPlayers(Human h)
        {
            RefreshRemoteSpots();
            if (remoteSpots.Count == 0) return NewAIController.AITickRate.veryLow;
            Vector3 p = h.transform.position;
            float d2 = float.MaxValue;
            foreach (var v in remoteSpots) d2 = Mathf.Min(d2, (p - v).sqrMagnitude);
            if (d2 < 15f * 15f) return NewAIController.AITickRate.veryHigh;
            if (d2 < 35f * 35f) return NewAIController.AITickRate.high;
            if (d2 < 70f * 70f) return NewAIController.AITickRate.medium;
            return NewAIController.AITickRate.veryLow;
        }

        internal static void RaiseTickNearPlayers(Human h)
        {
            if (!IsHost || manager == null || manager.ConnectedPeersCount == 0 || h == null || h.isPlayer) return;
            var ai = h.ai; if (ai == null) return;
            var want = RateNearPlayers(h);
            if (want == NewAIController.AITickRate.veryLow) return;
            if ((int)ai.desiredTickRate < (int)want) ai.SetDesiredTickRate(want, false);
            tickRaised.Add(h.humanID);
        }

        static bool applyingKey;

        internal static void OnKeyringAddress(Actor a, NewAddress ad)
        {
            if (applyingKey || a == null || ad == null || !a.isPlayer || manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return;
            int job = 0;
            var ctl = SideJobController.Instance;
            if (ctl != null && ctl.allJobsDictionary != null)
                foreach (var kv in ctl.allJobsDictionary)
                {
                    var j = kv.Value;
                    if (j == null || !j.accepted || j.state == SideJob.JobState.ended) continue;
                    if ((j.poster != null && j.poster.home != null && j.poster.home.id == ad.id) || (j.purp != null && j.purp.home != null && j.purp.home.id == ad.id)) { job = j.jobID; break; }
                }
            if (job <= 0) { Plugin.L.LogInfo($"[JOB] мне дали ключи от {ad.name} - не по заданию, другим не даю"); return; }
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)21); w.Put(ad.id); w.Put(job);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] задание {job}: мне дали ключи от {ad.name} (двери, почтовый ящик) - даю всем игрокам");
        }

        static float lookLogAt;

        internal static void BeforeLookAround(DialogPreset p, Citizen c)
        {
            if (p == null || c == null || (int)p.specialCase != 10 || IsHost || manager == null || manager.ConnectedPeersCount == 0) return;
            c.UpdateGameLocation(0f, false);
        }

        internal static void AfterLookAround(DialogPreset p, Citizen c, bool ok)
        {
            if (p == null || c == null || (int)p.specialCase != 10 || manager == null || manager.ConnectedPeersCount == 0) return;
            if (Time.unscaledTime - lookLogAt < 3f) return;
            lookLogAt = Time.unscaledTime;
            var home = c.home; var loc = c.currentGameLocation;
            bool pass = false;
            try { var gc = GameplayController.Instance; pass = home != null && gc != null && gc.guestPasses != null && gc.guestPasses.ContainsKey(home); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[DIALOG] «осмотреться» у жителя {c.humanID}: {(ok ? "можно" : "нельзя")}; дом {(home != null ? home.name : "нет")}, " +
                $"сейчас он в {(loc != null ? loc.name : "нигде")}, пропуск {(pass ? "уже есть" : "нет")}");
        }

        static int cctvCalls, cctvShots;
        static float cctvLogAt;

        internal static void OnCctvCapture(bool made) { cctvCalls++; if (made) cctvShots++; }

        static void CctvTick()
        {
            float now = Time.unscaledTime;
            if (now - cctvLogAt < 60f) return;
            cctvLogAt = now;
            int recs = 0, stored = 0, maxCap = 0; float oldest = float.MaxValue, newest = 0f;
            var cb = CitizenBehaviour.Instance;
            var list = cb != null ? cb.sceneRecorders : null;
            if (list != null)
                foreach (var r in list)
                {
                    if (r == null) continue;
                    recs++;
                    var it = r.interactable;
                    int n = it != null && it.cap != null ? it.cap.Count : 0;
                    stored += n; if (n > maxCap) maxCap = n;
                    if (r.lastCaptureAt < oldest) oldest = r.lastCaptureAt;
                    if (r.lastCaptureAt > newest) newest = r.lastCaptureAt;
                }
            var sd = SessionData.Instance;
            if (sd != null && list != null && newest > sd.gameTime + 0.05f)
            {
                float back = sd.gameTime - 1f; int fixedN = 0;
                foreach (var r in list) if (r != null && r.lastCaptureAt > sd.gameTime + 0.05f) { r.lastCaptureAt = back; fixedN++; }
                Plugin.L.LogInfo($"[CCTV] у {fixedN} камер последний снимок «из будущего» ({oldest:F2}..{newest:F2} при времени {sd.gameTime:F2}) - они ждали его и не снимали. Сбросил");
            }
            Plugin.L.LogInfo($"[CCTV] за минуту: камеры пробовали снять {cctvCalls} раз, сняли {cctvShots}; камер {recs}, кадров в памяти {stored} (у одной до {maxCap}); " +
                $"время игры {(sd != null ? sd.gameTime : 0f):F2}, последние снимки камер от {(recs > 0 ? oldest : 0f):F2} до {newest:F2}");
            cctvCalls = cctvShots = 0;
        }

        static void TickRateTick()
        {
            float now = Time.unscaledTime;
            if (now < tickRaiseAt) return;
            tickRaiseAt = now + 1f;
            int up = 0, back = 0;
            foreach (var kv in npcById)
            {
                var c = kv.Value; if (c == null) continue;
                try
                {
                    if (c.isDead || c.ai == null) continue;
                    var want = RateNearPlayers(c);
                    if (want != NewAIController.AITickRate.veryLow)
                    {
                        if ((int)c.ai.desiredTickRate < (int)want) { c.ai.SetDesiredTickRate(want, false); up++; }
                        tickRaised.Add(kv.Key);
                    }
                    else if (tickRaised.Remove(kv.Key)) { c.UpdateTickRateOnProx(); back++; }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            if ((up > 0 || back > 0) && tickRaiseLogN++ % 20 == 0) Plugin.L.LogInfo($"[NPC] хост: жителей возле других игроков ускорил {up}, вернул игре {back}, всего ускорено {tickRaised.Count}");
        }

        static void NpcTick(float dt)
        {
            if (!Plugin.CfgNpcSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
            if (npcById == null || npcById.Count == 0 || Time.unscaledTime - npcMapAt > 30f) BuildNpcMap();
            if (npcById.Count == 0) return;
            long tq = Perf.T();
            PuppetsFrame(dt);
            Perf.Add("жители: двойники", tq); tq = Perf.T();
            CorpseTick();
            Perf.Add("жители: тела", tq); tq = Perf.T();
            try { CctvTick(); } catch (Exception __e) { Err.Swallow(__e); }
            Perf.Add("жители: камеры", tq);
            if (!IsHost) { tq = Perf.T(); NpcClientTick(dt); Perf.Add("жители: движение у клиента", tq); return; }
            if (IsHost) { NpcSendTick(); try { TickRateTick(); } catch (Exception e) { if (!npcErrLogged) { npcErrLogged = true; Plugin.L.LogWarning("[NPC] частота: " + e.Message); } } }
            else NpcClientTick(dt);
        }

        static void PutNpc(NetDataWriter w, Citizen c)
        {
            Vector3 p = c.transform.position;
            w.Put(c.humanID);
            w.Put(p.x); w.Put(p.y); w.Put(p.z);
            w.Put(c.transform.eulerAngles.y);
            float sp = 0f; try { sp = c.currentNormalizedSpeed; } catch (Exception __e) { Err.Swallow(__e); }
            w.Put(sp);
            int f = 0;
            try { if (c.isDead) f |= 1; } catch (Exception __e) { Err.Swallow(__e); }
            try { if (c.ai != null && c.ai.ko) f |= 2; } catch (Exception __e) { Err.Swallow(__e); }
            try { if (c.ai != null && c.ai.inCombat) f |= 4; } catch (Exception __e) { Err.Swallow(__e); }
            try { if (c.isInBed) f |= 8; } catch (Exception __e) { Err.Swallow(__e); }
            try { if (c.isInLowBed) f |= 16; } catch (Exception __e) { Err.Swallow(__e); }
            try { if (c.isRunning) f |= 128; } catch (Exception __e) { Err.Swallow(__e); }
            int f2 = 0;
            try { if (c.isMoving) f2 |= 1; } catch (Exception __e) { Err.Swallow(__e); }
            try { if (c.ai != null && c.ai.restrained) f2 |= 2; } catch (Exception __e) { Err.Swallow(__e); }
            int idle = 0, arms = 0, carry = 0, comb = 0;
            try
            {
                var ac = c.animationController;
                if (ac != null)
                {
                    idle = (int)ac.idleAnimationState; arms = (int)ac.armsBoolAnimationState;
                    var an = ac.mainAnimator;
                    if (an != null)
                    {
                        if (an.GetBool("carryingItem")) f |= 32;
                        carry = an.GetInteger("carryItem"); comb = an.GetInteger("combatArmsOverride");
                    }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            int outfit = 255;
            try { var oc = c.outfitController; if (oc != null) outfit = (int)oc.currentOutfit; } catch (Exception __e) { Err.Swallow(__e); }
            w.Put((byte)f); w.Put((byte)f2); w.Put((byte)idle); w.Put((byte)arms); w.Put((byte)carry); w.Put((byte)comb); w.Put((byte)outfit);
        }

        sealed class NpcSent { public byte[] data = new byte[48]; public int len; public float at; }
        static readonly System.Collections.Generic.Dictionary<int, NpcSent> npcSent = new System.Collections.Generic.Dictionary<int, NpcSent>();
        static NetDataWriter npcScratch = new NetDataWriter();
        static int npcBytes, npcSkipped;

        static void NpcSendTick()
        {
            float now = Time.unscaledTime;
            if (now - npcSendAt < Mathf.Max(0.03f, Plugin.CfgNpcRate.Value)) return;
            npcSendAt = now;
            Vector3 hp = Vector3.zero; bool hasH = false;
            try { hp = Player.Instance.transform.position; hasH = true; } catch (Exception __e) { Err.Swallow(__e); }
            var cps = new List<Vector3>();
            SyncCur();
            foreach (var rp in players) if (rp.avatar != null) cps.Add(rp.targetPos);
            float rMid = Plugin.CfgNpcRange.Value; float rNear = rMid * 0.33f;
            float rNear2 = rNear * rNear, rMid2 = rMid * rMid;
            float baseIv = Mathf.Max(0.03f, Plugin.CfgNpcRate.Value);
            var all = new System.Collections.Generic.List<Citizen>(npcById.Values);
            NpcActPoll(all);
            var recs = new System.Collections.Generic.List<NpcSent>();
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || IsPuppetCit(c)) continue;
                try { if (c.removedFromWorld) continue; } catch (Exception __e) { Err.Swallow(__e); }
                float d2 = float.MaxValue;
                try
                {
                    Vector3 p = c.transform.position;
                    if (hasH) d2 = Mathf.Min(d2, (p - hp).sqrMagnitude);
                    for (int ci = 0; ci < cps.Count; ci++) d2 = Mathf.Min(d2, (p - cps[ci]).sqrMagnitude);
                }
                catch (Exception) { continue; }
                float iv, keep;
                if (d2 < rNear2) { iv = baseIv; keep = 1f; }
                else if (d2 < rMid2) { iv = baseIv * 3f; keep = 1.5f; }
                else { iv = baseIv * 8f; keep = 3f; }
                NpcSent st;
                if (!npcSent.TryGetValue(c.humanID, out st)) { st = new NpcSent(); st.at = -100f; npcSent[c.humanID] = st; }
                float age = now - st.at;
                if (age < iv) { npcSkipped++; continue; }
                npcScratch.Reset();
                try { PutNpc(npcScratch, c); } catch (Exception) { continue; }
                int len = npcScratch.Length; if (len > st.data.Length) continue;
                bool same = st.len == len;
                if (same) { var d = npcScratch.Data; for (int b = 0; b < len; b++) if (d[b] != st.data[b]) { same = false; break; } }
                if (same && age < keep) { npcSkipped++; continue; }
                Buffer.BlockCopy(npcScratch.Data, 0, st.data, 0, len); st.len = len; st.at = now;
                recs.Add(st);
            }
            int pos = 0;
            while (pos < recs.Count)
            {
                int n = Math.Min(28, recs.Count - pos);
                var w = new NetDataWriter();
                w.Put(MsgNpc); w.Put((byte)n);
                for (int i = 0; i < n; i++) { var st = recs[pos + i]; w.Put(st.data, 0, st.len); }
                manager.SendToAll(w, DeliveryMethod.Sequenced);
                npcSentCount += n; npcBytes += w.Length;
                pos += n;
            }
            if (now - npcLogAt > 15f)
            {
                npcLogAt = now;
                Plugin.L.LogInfo($"[NPC] хост: за 15 с отправлено записей: {npcSentCount} (~{npcBytes / 15 / 1024f:F1} КБ/с), пропущено как неизменных/далёких: {npcSkipped}, горожан: {all.Count}");
                npcSentCount = 0; npcBytes = 0; npcSkipped = 0;
            }
        }

        static void OnNpcMessage(NetDataReader r)
        {
            int n = r.GetByte();
            for (int i = 0; i < n; i++)
            {
                int id = r.GetInt();
                Vector3 p = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                float yaw = r.GetFloat(); float sp = r.GetFloat();
                byte f = r.GetByte(), f2 = r.GetByte(), idle = r.GetByte(), arms = r.GetByte(), carry = r.GetByte(), comb = r.GetByte(), outfit = r.GetByte();
                if (npcById == null || !npcById.ContainsKey(id)) { try { BuildNpcMap(); } catch (Exception __e) { Err.Swallow(__e); } }
                Citizen c; if (npcById == null || !npcById.TryGetValue(id, out c) || c == null)
                {
                    if (!npcMissLogged) { npcMissLogged = true; Plugin.L.LogWarning($"[NPC] горожанин {id} не найден у меня"); }
                    continue;
                }
                NpcTarget t;
                if (!npcTargets.TryGetValue(id, out t)) { t = new NpcTarget { c = c }; npcTargets[id] = t; }
                t.c = c; t.pos = p; t.yaw = yaw; t.speed = sp; t.flags = f; t.flags2 = f2; t.idle = idle; t.arms = arms; t.carryItem = carry; t.combatArms = comb; t.outfit = outfit;
                t.rev++; t.settled = false;
                npcRecvCount++;
            }
        }

        static void NpcClientTick(float dt)
        {
            CasingTick();
            if (npcTargets.Count == 0) return;
            if (!npcClientPaused)
            {
                try { Game.Instance.SetPauseAI(true); npcClientPaused = true; Plugin.L.LogInfo("[NPC] клиент: ИИ горожан остановлен, слежу за хостом"); }
                catch (Exception e) { Plugin.L.LogWarning($"[NPC] SetPauseAI: {e.Message}"); }
            }
            if (Time.unscaledTime - npcLogAt > 15f)
            {
                npcLogAt = Time.unscaledTime;
                Plugin.L.LogInfo($"[NPC] клиент: за 15 с получено записей: {npcRecvCount}, веду горожан: {npcTargets.Count}");
                npcRecvCount = 0;
            }
            npcFrame++;
            float now = Time.unscaledTime;
            float kp = 1f - Mathf.Exp(-12f * dt), kpFar = 1f - Mathf.Exp(-36f * dt);
            Vector3 eye = Vector3.zero; bool haveEye = false;
            try { var cm = Camera.main; if (cm != null) { eye = cm.transform.position; haveEye = true; } } catch (Exception __e) { Err.Swallow(__e); }
            int locBudget = 8, slotSeq = 0;
            foreach (var kv in npcTargets)
            {
                var t = kv.Value; var c = t.c;
                t.slot = slotSeq++;
                if (t.settled && t.doneRev == t.rev && ((npcFrame + t.slot) % 90) != 0 && now >= t.atkUntil && !t.atkHeld && now >= t.combatHoldUntil && t.curCombat != 1) continue;
                bool far = haveEye && (t.pos - eye).sqrMagnitude > 1600f;
                if (far && !t.snap && t.doneRev == t.rev && ((npcFrame + t.slot) % 3) != 0) continue;
                try
                {
                    if (c == null) continue;
                    if (IsPuppetId(kv.Key)) continue;
                    var tr = c.transform;
                    if (tr == null) continue;
                    bool talking = false;
                    try { talking = c.inConversation; } catch (Exception __e) { Err.Swallow(__e); }
                    if (talking)
                    {
                        if (!t.wasTalking) { t.wasTalking = true; Plugin.L.LogInfo($"[NPC] разговор с горожанином {kv.Key}: моя y={tr.position.y:F2}, у хоста y={t.pos.y:F2}; позицию не трогаю"); }
                        ApplyNpcState(t);
                        continue;
                    }
                    t.wasTalking = false;
                    if ((t.flags & 2) != 0 && !t.snap) { ApplyNpcState(t); continue; }
                    Vector3 cur = tr.position;
                    float d = Vector3.Distance(cur, t.pos);
                    var want = Quaternion.Euler(0f, t.yaw, 0f);
                    if (t.snap || d > 4f) { tr.position = t.pos; tr.rotation = want; t.snap = false; }
                    else
                    {
                        var rot = tr.rotation;
                        if (d < 0.003f && Quaternion.Angle(rot, want) < 0.3f) { tr.position = t.pos; tr.rotation = want; t.settled = true; }
                        else
                        {
                            float k = far ? kpFar : kp;
                            tr.position = Vector3.Lerp(cur, t.pos, k);
                            tr.rotation = Quaternion.Slerp(rot, want, k);
                        }
                    }
                    if (t.doneRev != t.rev || (t.outfit != 255 && t.apOutfit != t.outfit)) { ApplyNpcState(t); if (t.apOutfit == t.outfit || t.outfit == 255) t.doneRev = t.rev; }
                    ArmsLayerForAttack(t);
                    CombatSync(t);
                    if (locBudget > 0 && Time.unscaledTime - t.lastLoc > 0.5f && (t.lastLocPos - tr.position).sqrMagnitude > 0.25f)
                    {
                        t.lastLoc = Time.unscaledTime; t.lastLocPos = tr.position; locBudget--;
                        try { c.UpdateGameLocation(0f, false); } catch (Exception __e) { Err.Swallow(__e); }
                    }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
        }

        static void ArmsLayerForAttack(NpcTarget t)
        {
            if (!t.atkHeld && Time.unscaledTime >= t.atkUntil) return;
            var c = t.c; if (c == null) return;
            var ac = c.animationController; if (ac == null) return;
            var an = ac.mainAnimator; if (an == null || an.layerCount < 2) return;
            if (Time.unscaledTime < t.atkUntil)
            {
                try { an.SetLayerWeight(1, 1f); } catch (Exception __e) { Err.Swallow(__e); }
                try { ac.armsLayerDesiredWeight = 1f; } catch (Exception __e) { Err.Swallow(__e); }
                t.atkHeld = true;
            }
            else if (t.atkHeld)
            {
                t.atkHeld = false;
                if (t.arms == 0 && t.carryItem == 0)
                {
                    try { an.SetLayerWeight(1, 0f); } catch (Exception __e) { Err.Swallow(__e); }
                    try { ac.armsLayerDesiredWeight = 0f; } catch (Exception __e) { Err.Swallow(__e); }
                }
            }
        }

        static void CombatSync(NpcTarget t)
        {
            var c = t.c; if (c == null) return;
            float now = Time.unscaledTime;
            if ((t.flags & 4) != 0) t.combatHoldUntil = Math.Max(t.combatHoldUntil, now + 1.0f);
            int want = now < t.combatHoldUntil ? 1 : 0;
            if (t.curCombat == want) return;
            bool first = t.curCombat < 0;
            t.curCombat = want;
            if (first && want == 0) return;
            bool cmb = want == 1;
            try { if (c.ai != null) { c.ai.SetInCombat(cmb, true); if (cmb) c.ai.UpdateCurrentWeapon(); } } catch (Exception e) { if (!combatErrLogged) { combatErrLogged = true; Plugin.L.LogWarning($"[NPCACT] ai.SetInCombat: {e.Message}"); } }
            try { var ac = c.animationController; if (ac != null) ac.SetInCombat(cmb); } catch (Exception __e) { Err.Swallow(__e); }
        }

        static void MakeCorpseSearchable(Citizen c)
        {
            var cc = CitizenControls.Instance;
            var pr = cc != null ? cc.deadBodySearchInteractable : null;
            if (pr == null || c == null || c.outfitController == null) return;
            if (c.interactable != null && c.interactable.preset == pr) return;
            var anchor = c.outfitController.GetBodyAnchor(CitizenOutfitController.CharacterAnchor.upperTorso);
            if (anchor == null) return;
            var old = c.interactableController;
            if (old != null)
            {
                try { if (old.coll != null) UnityEngine.Object.Destroy(old.coll); } catch (Exception __e) { Err.Swallow(__e); }
                UnityEngine.Object.Destroy(old);
            }
            var it = InteractableCreator.Instance.CreateCitizenInteractable(pr, c, anchor, c.evidenceEntry);
            var ic = anchor.gameObject.AddComponent<InteractableController>();
            var mc = anchor.gameObject.AddComponent<MeshCollider>();
            if (mc != null) { mc.convex = true; mc.isTrigger = true; }
            ic.coll = mc;
            ic.Setup(it);
            if (it != null) { it.objectRef = c; c.interactable = it; c.interactableController = ic; try { it.UpdateCurrentActions(); } catch (Exception __e) { Err.Swallow(__e); } }
            Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: тело можно обыскать");
        }
    }
}

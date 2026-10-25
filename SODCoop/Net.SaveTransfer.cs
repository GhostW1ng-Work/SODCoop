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
        class XFile { public string Name = "", Dest = ""; public byte Kind; public long Size, Got; public bool Need; public FileStream Fs; }
        static bool xferMode, xferFinish; static string xferFailMsg; static string xferText = "", xferSaveName = ""; static float xferAt;
        static readonly List<XFile> xfFiles = new List<XFile>();
        static NetPeer sendPeer; static int sendStage, sendIdx; static object snapTask; static float snapAt; static List<XFile> sendFiles;
        static int snapVariant; static DateTime snapStartUtc;
        const string SyncName = "SodCoop_sync.sodb";

        internal static string SaveDir() { var d = Path.Combine(Application.persistentDataPath, "Save"); try { Directory.CreateDirectory(d); } catch (Exception __e) { Err.Swallow(__e); } return d; }
        internal static string CityDir() { var d = Path.Combine(Application.persistentDataPath, "Cities"); try { Directory.CreateDirectory(d); } catch (Exception __e) { Err.Swallow(__e); } return d; }

        internal static void StartTransfer()
        {
            xferMode = true; xferFinish = false; xferFailMsg = null; xferText = "Connecting to the host..."; xferAt = Time.realtimeSinceStartup; xferSaveName = "";
            xfFiles.Clear();
            Start();
        }

        static void XferFail(string m)
        {
            Plugin.L.LogWarning("[XFER] " + m);
            foreach (var f in xfFiles) { try { if (f.Fs != null) f.Fs.Close(); } catch (Exception __e) { Err.Swallow(__e); } f.Fs = null; }
            xferMode = false; xferFinish = false; xferText = "Transfer failed: " + m;
            Shutdown();
            RtMode = NetMode.Off;
            MenuAuto.Notice = "Could not get the host's save: " + m;
        }

        static void XferFinish()
        {
            try
            {
                foreach (var f in xfFiles)
                {
                    if (!f.Need) continue;
                    if (f.Fs != null) { f.Fs.Close(); f.Fs = null; }
                    if (f.Got != f.Size) { XferFail("file " + f.Name + " incomplete (" + f.Got + "/" + f.Size + ")"); return; }
                    if (File.Exists(f.Dest)) File.Delete(f.Dest);
                    File.Move(f.Dest + ".part", f.Dest);
                }
            }
            catch (Exception e) { XferFail("saving files: " + e.Message); return; }
            string name = xferSaveName;
            Plugin.L.LogInfo("[XFER] сохранение получено: " + name);
            xferMode = false; xferFinish = false; xferText = "";
            keepModsOnce = true;
            Shutdown();
            if (followHost)
            {
                followHost = false;
                OpenMenuForLoad();
                Toast("Loading the host's save...");
            }
            MenuAuto.LoadSave(name);
        }

        static void XferTick()
        {
            HostSendTick();
            if (xferFailMsg != null) { var m = xferFailMsg; xferFailMsg = null; XferFail(m); return; }
            if (xferFinish) { XferFinish(); return; }
            if (xferMode && Time.realtimeSinceStartup - xferAt > (followHost ? 900f : 90f)) XferFail("timed out waiting for the host (is the host in a loaded city?)");
        }

        static void ClientOnMeta(NetDataReader r)
        {
            xfFiles.Clear();
            int n = r.GetInt();
            for (int i = 0; i < n; i++)
            {
                var f = new XFile(); f.Name = Path.GetFileName(r.GetString()); f.Kind = r.GetByte(); f.Size = r.GetLong();
                xfFiles.Add(f);
            }
            var w = new NetDataWriter(); w.Put(MsgSaveWant); w.Put(n);
            foreach (var f in xfFiles)
            {
                string ext = Path.GetExtension(f.Name).ToLowerInvariant();
                bool okExt = ext == ".sodb" || ext == ".sod" || ext == ".citb" || ext == ".txt";
                f.Dest = Path.Combine(f.Kind == 0 ? SaveDir() : CityDir(), f.Name);
                bool have = f.Kind == 1 && File.Exists(f.Dest) && new FileInfo(f.Dest).Length == f.Size;
                f.Need = okExt && !have;
                if (f.Kind == 0) xferSaveName = f.Name;
                w.Put(f.Need);
                Plugin.L.LogInfo($"[XFER] файл {f.Name}: {f.Size} Б, {(f.Need ? "нужен" : "уже есть")}");
            }
            serverPeer.Send(w, DeliveryMethod.ReliableOrdered);
            xferAt = Time.realtimeSinceStartup;
            xferText = "Receiving the save...";
        }

        static void ClientOnChunk(NetDataReader r)
        {
            int idx = r.GetByte(); int n = r.GetInt();
            var buf = new byte[n]; r.GetBytes(buf, 0, n);
            if (idx < 0 || idx >= xfFiles.Count) return;
            var f = xfFiles[idx];
            if (f.Fs == null) f.Fs = new FileStream(f.Dest + ".part", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            f.Fs.Write(buf, 0, n); f.Got += n;
            xferAt = Time.realtimeSinceStartup;
            long tot = 0, got = 0; foreach (var x in xfFiles) if (x.Need) { tot += x.Size; got += x.Got; }
            xferText = "Receiving the save: " + (got / 1048576.0).ToString("F1") + " / " + (tot / 1048576.0).ToString("F1") + " MB";
        }

        static void HostFail(string m)
        {
            Plugin.L.LogWarning("[XFER] " + m);
            try { if (sendPeer != null) { var w = new NetDataWriter(); w.Put(MsgSaveFail); w.Put(m); sendPeer.Send(w, DeliveryMethod.ReliableOrdered); } } catch (Exception __e) { Err.Swallow(__e); }
            HostEndSend();
        }

        static void HostEndSend()
        {
            try { if (sendFiles != null) foreach (var f in sendFiles) { if (f.Fs != null) f.Fs.Close(); f.Fs = null; } } catch (Exception __e) { Err.Swallow(__e); }
            sendStage = 0; sendPeer = null; sendFiles = null; snapTask = null;
            while (sendQueue.Count > 0)
            {
                var next = sendQueue[0]; sendQueue.RemoveAt(0);
                if (next != null && next.ConnectionState == ConnectionState.Connected) { HostBeginSend(next); break; }
            }
        }

        static readonly List<NetPeer> sendQueue = new List<NetPeer>();
    }
}

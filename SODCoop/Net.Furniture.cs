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
        class FurnRec
        {
            public int room, angle, x, y, z, tries;
            public string preset = "", art = "", bm = "";
            public bool hasKey, hasOff;
            public Color c0, c1, c2, c3; public float grub;
            public Vector3 off;
            public System.Collections.Generic.List<int[]> covers = new System.Collections.Generic.List<int[]>();
        }

        static readonly System.Collections.Generic.List<FurnRec> furnRecs = new System.Collections.Generic.List<FurnRec>();
        static readonly System.Collections.Generic.List<FurnRec> furnPending = new System.Collections.Generic.List<FurnRec>();
        static bool inExecute; static float execAt;
        static string lastFurnKey = ""; static float lastFurnAt;
        static System.Collections.Generic.Dictionary<string, FurniturePreset> furnPresetCache;
        static System.Collections.Generic.Dictionary<string, ArtPreset> artPresetCache;

        static string FurnKey(FurnRec r) { return r.room + ":" + r.x + "," + r.y + "," + r.z + ":" + r.preset; }

        static FurniturePreset FindFurnPreset(string name)
        {
            if (furnPresetCache == null)
            {
                furnPresetCache = new System.Collections.Generic.Dictionary<string, FurniturePreset>();
                foreach (var p in Resources.FindObjectsOfTypeAll<FurniturePreset>())
                    if (p != null && !string.IsNullOrEmpty(p.name) && !furnPresetCache.ContainsKey(p.name)) furnPresetCache[p.name] = p;
                Plugin.L.LogInfo($"[FURN] пресетов мебели: {furnPresetCache.Count}");
            }
            FurniturePreset r; return furnPresetCache.TryGetValue(name ?? "", out r) ? r : null;
        }

        static ArtPreset FindArtPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (artPresetCache == null)
            {
                artPresetCache = new System.Collections.Generic.Dictionary<string, ArtPreset>();
                foreach (var p in Resources.FindObjectsOfTypeAll<ArtPreset>())
                    if (p != null && !string.IsNullOrEmpty(p.name) && !artPresetCache.ContainsKey(p.name)) artPresetCache[p.name] = p;
            }
            ArtPreset r; return artPresetCache.TryGetValue(name, out r) ? r : null;
        }

        static FurnRec MakeRec(NewRoom room, FurnitureLocation loc, PlayerApartmentController.FurniturePlacement pl)
        {
            NewNode an = null;
            try { an = loc != null ? loc.anchorNode : null; } catch (Exception __e) { Err.Swallow(__e); }
            if (an == null && pl != null) { try { an = pl.anchorNode; } catch (Exception __e) { Err.Swallow(__e); } }
            if (an == null) return null;
            if (room == null) { try { room = an.room; } catch (Exception __e) { Err.Swallow(__e); } }
            if (room == null) return null;
            var rec = new FurnRec();
            rec.room = room.roomID;
            FurniturePreset fp = null;
            try { fp = loc != null ? loc.furniture : null; } catch (Exception __e) { Err.Swallow(__e); }
            if (fp == null && pl != null) { try { fp = pl.preset; } catch (Exception __e) { Err.Swallow(__e); } }
            if (fp == null) return null;
            rec.preset = fp.name ?? "";
            var c = an.nodeCoord; rec.x = c.x; rec.y = c.y; rec.z = c.z;
            try { rec.angle = loc != null ? loc.angle : pl.angle; } catch (Exception __e) { Err.Swallow(__e); }
            Toolbox.MaterialKey key = null;
            try { key = loc != null ? loc.matKey : null; } catch (Exception __e) { Err.Swallow(__e); }
            if (key == null && pl != null) { try { key = pl.materialKey; } catch (Exception __e) { Err.Swallow(__e); } }
            if (key != null)
            {
                rec.hasKey = true;
                try { rec.bm = key.baseMaterial != null ? key.baseMaterial.name : ""; } catch (Exception __e) { Err.Swallow(__e); }
                rec.c0 = key.mainColour; rec.c1 = key.colour1; rec.c2 = key.colour2; rec.c3 = key.colour3; rec.grub = key.grubiness;
            }
            try
            {
                ArtPreset ap = loc != null ? loc.art : null;
                if (ap == null && pl != null) ap = pl.art;
                if (ap != null) rec.art = ap.name ?? "";
            }
            catch (Exception __e) { Err.Swallow(__e); }
            try { if (loc != null) { rec.off = loc.offset; rec.hasOff = true; } else if (pl != null) { rec.off = pl.offset; rec.hasOff = true; } } catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                var cov = loc != null ? loc.coversNodes : null;
                if (cov == null && pl != null) cov = pl.coversNodes;
                if (cov != null)
                    foreach (var n in cov) { if (n == null) continue; var cc = n.nodeCoord; rec.covers.Add(new[] { cc.x, cc.y, cc.z }); }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return rec;
        }

        static void PutFurn(NetDataWriter w, FurnRec r)
        {
            w.Put(r.room); w.Put(r.preset); w.Put(r.angle); w.Put(r.x); w.Put(r.y); w.Put(r.z);
            w.Put(r.hasKey);
            if (r.hasKey) { w.Put(r.bm); PutColor(w, r.c0); PutColor(w, r.c1); PutColor(w, r.c2); PutColor(w, r.c3); w.Put(r.grub); }
            w.Put(r.art ?? "");
            w.Put(r.hasOff);
            if (r.hasOff) { w.Put(r.off.x); w.Put(r.off.y); w.Put(r.off.z); }
            w.Put(r.covers.Count);
            foreach (var c in r.covers) { w.Put(c[0]); w.Put(c[1]); w.Put(c[2]); }
        }

        static FurnRec GetFurn(NetDataReader r)
        {
            var f = new FurnRec();
            f.room = r.GetInt(); f.preset = r.GetString(); f.angle = r.GetInt(); f.x = r.GetInt(); f.y = r.GetInt(); f.z = r.GetInt();
            f.hasKey = r.GetBool();
            if (f.hasKey) { f.bm = r.GetString(); f.c0 = GetColor(r); f.c1 = GetColor(r); f.c2 = GetColor(r); f.c3 = GetColor(r); f.grub = r.GetFloat(); }
            f.art = r.GetString();
            f.hasOff = r.GetBool();
            if (f.hasOff) f.off = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            int n = r.GetInt();
            for (int i = 0; i < n; i++) f.covers.Add(new[] { r.GetInt(), r.GetInt(), r.GetInt() });
            return f;
        }

        internal static void BeginExecute()
        {
            inExecute = true; execAt = Time.unscaledTime;
            try
            {
                if (applyingRemote) return;
                var pac = PlayerApartmentController.Instance;
                var p = pac != null ? pac.furnPlacement : null;
                var ex = p != null ? p.existing : null;
                if (ex != null) OnFurnRemoved(ex, "переставил");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] begin: {e.Message}"); }
        }

        internal static void EndExecute() { inExecute = false; }

        internal static void OnFurnAdded(NewRoom room, FurnitureLocation loc, PlayerApartmentController.FurniturePlacement pl)
        {
            try
            {
                if (applyingRemote) return;
                if (!inExecute || Time.unscaledTime - execAt > 5f) return;
                var rec = MakeRec(room, loc, pl);
                if (rec == null) return;
                string k = FurnKey(rec);
                if (k == lastFurnKey && Time.unscaledTime - lastFurnAt < 1f) return;
                lastFurnKey = k; lastFurnAt = Time.unscaledTime;
                furnRecs.RemoveAll(x => FurnKey(x) == k);
                furnRecs.Add(rec);
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)17); PutFurn(w, rec);
                SendDoor(w);
                Plugin.L.LogInfo($"[FURN] я поставил '{rec.preset}': комната {rec.room}, узел ({rec.x},{rec.y},{rec.z}), угол {rec.angle}, занимает узлов {rec.covers.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] add: {e.Message}"); }
        }

        internal static void OnFurnRemoved(FurnitureLocation loc, string why)
        {
            try
            {
                if (applyingRemote || loc == null) return;
                var rec = MakeRec(null, loc, null);
                if (rec == null) return;
                string k = FurnKey(rec);
                furnRecs.RemoveAll(x => FurnKey(x) == k);
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)18);
                w.Put(rec.room); w.Put(rec.x); w.Put(rec.y); w.Put(rec.z); w.Put(rec.preset);
                SendDoor(w);
                Plugin.L.LogInfo($"[FURN] я {why} '{rec.preset}': комната {rec.room}, узел ({rec.x},{rec.y},{rec.z})");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] remove: {e.Message}"); }
        }

        static NewNode FindNodeAt(NewRoom room, int x, int y, int z)
        {
            try
            {
                foreach (var n in room.nodes)
                {
                    if (n == null) continue;
                    var c = n.nodeCoord;
                    if (c.x == x && c.y == y && c.z == z) return n;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                NewNode nn;
                var map = PathFinder.Instance.nodeMap;
                if (map != null && map.TryGetValue(new Vector3(x, y, z), out nn) && nn != null) return nn;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static FurnitureLocation FindFurnLoc(NewRoom room, int x, int y, int z, string preset)
        {
            try
            {
                foreach (var f in room.individualFurniture)
                    if (FurnMatch(f, x, y, z, preset)) return f;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                foreach (var cl in room.furniture)
                {
                    if (cl == null) continue;
                    foreach (var f in cl.clusterList)
                        if (FurnMatch(f, x, y, z, preset)) return f;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static bool FurnMatch(FurnitureLocation f, int x, int y, int z, string preset)
        {
            if (f == null) return false;
            try
            {
                var an = f.anchorNode; if (an == null) return false;
                var c = an.nodeCoord;
                if (c.x != x || c.y != y || c.z != z) return false;
                return f.furniture != null && f.furniture.name == preset;
            }
            catch (Exception) { return false; }
        }

        static bool ApplyFurnAdd(FurnRec rec)
        {
            var room = RoomById(rec.room);
            if (room == null) return false;
            var node = FindNodeAt(room, rec.x, rec.y, rec.z);
            if (node == null) return false;
            var preset = FindFurnPreset(rec.preset);
            if (preset == null) { Plugin.L.LogWarning($"[FURN] нет пресета мебели '{rec.preset}'"); return true; }
            string k = FurnKey(rec);
            var existing = FindFurnLoc(room, rec.x, rec.y, rec.z, rec.preset);
            if (existing != null)
            {
                bool same = false;
                try { same = existing.angle == rec.angle; } catch (Exception __e) { Err.Swallow(__e); }
                if (same) { furnRecs.RemoveAll(x => FurnKey(x) == k); furnRecs.Add(rec); return true; }
                RemoveFurnLoc(room, existing);
            }
            var pl = new PlayerApartmentController.FurniturePlacement();
            pl.preset = preset; pl.anchorNode = node; pl.angle = rec.angle;
            if (rec.hasKey)
            {
                var key = new Toolbox.MaterialKey();
                key.baseMaterial = FindMaterial(rec.bm);
                key.mainColour = rec.c0; key.colour1 = rec.c1; key.colour2 = rec.c2; key.colour3 = rec.c3; key.grubiness = rec.grub;
                pl.materialKey = key;
            }
            var art = FindArtPreset(rec.art);
            if (art != null) pl.art = art;
            if (rec.hasOff) pl.offset = rec.off;
            var cov = new Il2CppSystem.Collections.Generic.List<NewNode>();
            foreach (var c in rec.covers)
            {
                var n = FindNodeAt(room, c[0], c[1], c[2]);
                if (n != null) cov.Add(n);
            }
            if (cov.Count == 0) cov.Add(node);
            pl.coversNodes = cov;
            applyingRemote = true; applyingDoor = true;
            try
            {
                room.AddFurnitureCustom(pl);
                furnRecs.RemoveAll(x => FurnKey(x) == k);
                furnRecs.Add(rec);
                Plugin.L.LogInfo($"[FURN] второй игрок поставил '{rec.preset}': комната {rec.room}, узел ({rec.x},{rec.y},{rec.z}), угол {rec.angle}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] поставить '{rec.preset}': {e.Message}"); }
            finally { applyingRemote = false; applyingDoor = false; }
            return true;
        }

        static void RemoveFurnLoc(NewRoom room, FurnitureLocation loc)
        {
            bool prev1 = applyingRemote, prev2 = applyingDoor;
            applyingRemote = true; applyingDoor = true;
            try
            {
                loc.Delete(true, FurnitureClusterLocation.RemoveInteractablesOption.remove);
                try { if (room.individualFurniture.Contains(loc)) room.individualFurniture.Remove(loc); } catch (Exception __e) { Err.Swallow(__e); }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] удалить: {e.Message}"); }
            finally { applyingRemote = prev1; applyingDoor = prev2; }
        }

        static bool ApplyFurnRemove(int rid, int x, int y, int z, string preset)
        {
            var room = RoomById(rid);
            if (room == null) return false;
            string k = rid + ":" + x + "," + y + "," + z + ":" + preset;
            furnRecs.RemoveAll(q => FurnKey(q) == k);
            var loc = FindFurnLoc(room, x, y, z, preset);
            if (loc == null) { Plugin.L.LogInfo($"[FURN] убрать '{preset}' (комната {rid}): у меня её уже нет"); return true; }
            RemoveFurnLoc(room, loc);
            Plugin.L.LogInfo($"[FURN] второй игрок убрал '{preset}': комната {rid}, узел ({x},{y},{z})");
            return true;
        }

        static void OnFurnMessage(byte kind, NetDataReader r)
        {
            if (kind == 17)
            {
                var rec = GetFurn(r);
                if (!ApplyFurnAdd(rec)) furnPending.Add(rec);
            }
            else if (kind == 18)
            {
                int rid = r.GetInt(); int x = r.GetInt(); int y = r.GetInt(); int z = r.GetInt(); string pn = r.GetString();
                furnPending.RemoveAll(q => q.room == rid && q.x == x && q.y == y && q.z == z && q.preset == pn);
                ApplyFurnRemove(rid, x, y, z, pn);
            }
            else if (kind == 19)
            {
                int n = r.GetInt();
                Plugin.L.LogInfo($"[FURN] получен снимок мебели: {n}");
                for (int i = 0; i < n; i++)
                {
                    var rec = GetFurn(r);
                    if (!ApplyFurnAdd(rec)) furnPending.Add(rec);
                }
            }
        }

        static void SendFurnSnapshot(NetPeer peer)
        {
            try
            {
                if (furnRecs.Count == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)19); w.Put(furnRecs.Count);
                foreach (var f in furnRecs) PutFurn(w, f);
                if (peer != null) peer.Send(w, DeliveryMethod.ReliableOrdered); else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[FURN] отправил снимок мебели: {furnRecs.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] снимок: {e.Message}"); }
        }

        static void FurnTick()
        {
            if (furnPending.Count == 0) return;
            var list = new System.Collections.Generic.List<FurnRec>(furnPending);
            foreach (var rec in list)
            {
                bool done = false;
                try { done = ApplyFurnAdd(rec); } catch (Exception __e) { Err.Swallow(__e); }
                rec.tries++;
                if (done || rec.tries > 40) furnPending.Remove(rec);
            }
        }

        static void AptTick()
        {
            try { FurnTick(); } catch (Exception __e) { Err.Swallow(__e); }
            try { MapTick(); } catch (Exception __e) { Err.Swallow(__e); }
            if (soldPending.Count == 0) return;
            var done = new System.Collections.Generic.List<int>();
            foreach (var id in soldPending) { try { if (TryRemoveSaleNote(id)) done.Add(id); } catch (Exception __e) { Err.Swallow(__e); } }
            foreach (var id in done) soldPending.Remove(id);
        }

        static void SendSoldSnapshot(NetPeer peer)
        {
            try
            {
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)15); w.Put(soldAddrs.Count);
                foreach (var id in soldAddrs)
                {
                    System.Collections.Generic.List<int[]> rems; soldRemovals.TryGetValue(id, out rems);
                    PutSale(w, id, rems);
                }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static float lastAptTick;
        static float lastTimeSend, lastTimeLog;
        static bool timeLogged, applyingWeather;

        internal static bool ClientFollowsHostWeather()
        {
            return !IsHost && !applyingWeather && Plugin.CfgTimeSync.Value && manager != null && manager.ConnectedPeersCount > 0;
        }

        static void TimeTick()
        {
            if (!IsHost || !Plugin.CfgTimeSync.Value) return;
            if (manager == null || manager.ConnectedPeersCount == 0) return;
            if (Time.unscaledTime - lastTimeSend < 5f) return;
            lastTimeSend = Time.unscaledTime;
            SendTime();
        }

        static void SendTime()
        {
            try
            {
                var sd = SessionData.Instance;
                if (sd == null) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)13);
                w.Put(sd.gameTime); w.Put(sd.leapYearCycle); w.Put((byte)(int)SharedTimeSpeed(sd));
                w.Put(sd.desiredRain); w.Put(sd.desiredWind); w.Put(sd.desiredSnow); w.Put(sd.desiredLightning); w.Put(sd.desiredFog);
                SendDoor(w);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void ApplyTime(float gt, int leap, int spd, float rain, float wind, float snow, float lit, float fog)
        {
            if (IsHost || !Plugin.CfgTimeSync.Value) return;
            try
            {
                var sd = SessionData.Instance;
                if (sd == null) return;
                float mine = sd.gameTime;
                if (!timeLogged || Time.unscaledTime - lastTimeLog > 30f)
                {
                    timeLogged = true; lastTimeLog = Time.unscaledTime;
                    Plugin.L.LogInfo($"[TIME] хост gameTime={gt:F4} у меня={mine:F4} скорость={spd}/{(int)sd.currentTimeSpeed} дождь={rain:F2} ветер={wind:F2} снег={snow:F2} молнии={lit:F2} туман={fog:F2}");
                }
                if (spd != (int)sd.currentTimeSpeed && !LocalKo())
                {
                    applyingTimeSpeed = true;
                    try { sd.SetTimeSpeed((SessionData.TimeSpeed)spd); } finally { applyingTimeSpeed = false; }
                }
                if (Mathf.Abs(mine - gt) > 0.03f) sd.SetGameTime(gt, leap);
                if (Mathf.Abs(sd.desiredRain - rain) > 0.02f || Mathf.Abs(sd.desiredWind - wind) > 0.02f || Mathf.Abs(sd.desiredSnow - snow) > 0.02f
                    || Mathf.Abs(sd.desiredLightning - lit) > 0.02f || Mathf.Abs(sd.desiredFog - fog) > 0.02f)
                {
                    applyingWeather = true;
                    try { sd.SetWeather(rain, wind, snow, lit, fog, 0.1f, false); }
                    finally { applyingWeather = false; }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[TIME] {e.Message}"); }
        }

        class PendingWin { public Vector3 wp, cp, vel; public bool fx; public int tries; }
        static readonly System.Collections.Generic.List<PendingWin> pendingWins = new System.Collections.Generic.List<PendingWin>();
        static float lastWinTick;

        internal static void OnWindowBreak(BreakableWindowController bw, Vector3 cp, Vector3 vel)
        {
            try
            {
                if (applyingRemote || bw == null || !Plugin.CfgObjSync.Value) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                if (!bw.isBroken) return;
                Vector3 wp = bw.transform.position;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)11);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                w.Put(cp.x); w.Put(cp.y); w.Put(cp.z);
                w.Put(vel.x); w.Put(vel.y); w.Put(vel.z);
                SendDoor(w);
                Plugin.L.LogInfo($"[WINDOW] я разбил окно в {wp}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static Il2CppArrayBase<BreakableWindowController> winCache; static int winCacheFrame = -1;

        static Il2CppArrayBase<BreakableWindowController> AllWindows()
        {
            if (winCache == null || winCacheFrame != Time.frameCount) { winCache = UnityEngine.Object.FindObjectsOfType<BreakableWindowController>(); winCacheFrame = Time.frameCount; }
            return winCache;
        }

        static bool TryApplyWindow(Vector3 wp, Vector3 cp, Vector3 vel, bool fx)
        {
            BreakableWindowController found = null; float best = 0.5f;
            foreach (var bw in AllWindows())
            {
                if (bw == null) continue;
                float d = Vector3.Distance(bw.transform.position, wp);
                if (d < best) { best = d; found = bw; }
            }
            if (found == null) return false;
            if (found.isBroken) return true;
            applyingRemote = true; applyingDoor = true;
            try
            {
                if (fx) found.BreakWindow(cp, vel, null, false);
                else found.SetBroken(true);
                if (!found.isBroken) found.SetBroken(true);
                Plugin.L.LogInfo($"[WINDOW] разбил окно у себя ({best:F3} м), isBroken={found.isBroken}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[WINDOW] разбить: {e.Message}"); }
            finally { applyingRemote = false; applyingDoor = false; }
            return true;
        }

        static void ApplyWindowBreak(Vector3 wp, Vector3 cp, Vector3 vel, bool fx)
        {
            bool ok = false;
            try { ok = TryApplyWindow(wp, cp, vel, fx); } catch (Exception e) { Plugin.L.LogWarning($"[WINDOW] {e.Message}"); }
            if (!ok)
            {
                if (pendingWins.Count > 400) pendingWins.RemoveAt(0);
                pendingWins.Add(new PendingWin { wp = wp, cp = cp, vel = vel, fx = fx });
            }
        }

        static void WindowTick()
        {
            if (pendingWins.Count == 0) return;
            if (Time.unscaledTime - lastWinTick < 1f) return;
            lastWinTick = Time.unscaledTime;
            for (int i = pendingWins.Count - 1; i >= 0; i--)
            {
                var p = pendingWins[i];
                bool ok = false;
                try { ok = TryApplyWindow(p.wp, p.cp, p.vel, false); } catch (Exception __e) { Err.Swallow(__e); }
                if (ok || ++p.tries > 600) pendingWins.RemoveAt(i);
            }
        }

        static void SendWindowSnapshot(NetPeer peer)
        {
            try
            {
                var list = new System.Collections.Generic.List<Vector3>();
                foreach (var bw in UnityEngine.Object.FindObjectsOfType<BreakableWindowController>())
                    if (bw != null && bw.isBroken) list.Add(bw.transform.position);
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)12); w.Put(list.Count);
                foreach (var v in list) { w.Put(v.x); w.Put(v.y); w.Put(v.z); }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[WINDOW] отправил снимок разбитых окон: {list.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[WINDOW] снимок: {e.Message}"); }
        }

        static readonly System.Collections.Generic.HashSet<string> brokenKeys = new System.Collections.Generic.HashSet<string>();

        static string BreakKey(int hash, Vector3 wp)
        {
            return hash + ":" + Mathf.RoundToInt(wp.x * 20f) + ":" + Mathf.RoundToInt(wp.y * 20f) + ":" + Mathf.RoundToInt(wp.z * 20f);
        }

        internal static void OnWorldBreak(InteractableController ic, int type, Vector3 p, Vector3 n, float m)
        {
            try
            {
                if (applyingDoor || applyingRemote || ic == null || !Plugin.CfgObjSync.Value) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                Interactable it = null;
                try { it = ic.interactable; } catch (Exception __e) { Err.Swallow(__e); }
                if (it == null) it = FindByController(ic);
                string bname = it != null ? PresetNameOf(it) : ic.gameObject.name;
                Vector3 wp = it != null ? it.wPos : ic.transform.position;
                int hash = NameHash(bname);
                if (!brokenKeys.Add(BreakKey(hash, wp))) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)10); w.Put((byte)type); w.Put(hash);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                w.Put(p.x); w.Put(p.y); w.Put(p.z); w.Put(n.x); w.Put(n.y); w.Put(n.z); w.Put(m);
                SendDoor(w);
                Plugin.L.LogInfo($"[BREAK] я разбил '{bname}' ({(type == 0 ? "Shatter" : "BreakObject")}) в {wp}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void ApplyWorldBreak(int type, int hash, Vector3 wp, Vector3 p, Vector3 n, float m)
        {
            string key = BreakKey(hash, wp);
            if (!brokenKeys.Add(key)) return;
            InteractableController found = null; float best = 0.2f;
            Action<InteractableController> consider = ic =>
            {
                if (ic == null) return;
                Interactable ci = null;
                try { ci = ic.interactable; } catch (Exception __e) { Err.Swallow(__e); }
                string nm = ci != null ? PresetNameOf(ci) : ic.gameObject.name;
                if (NameHash(nm) != hash) return;
                float d = Vector3.Distance(ci != null ? ci.wPos : ic.transform.position, wp);
                if (d < best) { best = d; found = ic; }
            };
            try
            {
                foreach (var col in Physics.OverlapSphere(wp, 1.5f, -1, QueryTriggerInteraction.Collide))
                    if (col != null) consider(col.GetComponentInParent<InteractableController>());
                if (found == null)
                    foreach (var ic in UnityEngine.Object.FindObjectsOfType<InteractableController>()) consider(ic);
            }
            catch (Exception e) { Plugin.L.LogWarning($"[BREAK] поиск: {e.Message}"); }
            if (found == null) { Plugin.L.LogWarning($"[BREAK] объект в {wp} не найден среди заспавненных (далеко от меня?)"); return; }
            applyingDoor = true; applyingRemote = true;
            try
            {
                if (type == 0) found.Shatter(p, m);
                else found.BreakObject(p, n, m, null);
                Plugin.L.LogInfo($"[BREAK] второй игрок разбил '{found.gameObject.name}' ({best:F3} м), повторяю у себя");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[BREAK] разбить: {e.Message}"); }
            finally { applyingDoor = false; applyingRemote = false; }
        }

        internal static void OnItemBroken(InteractableController ic, Vector3 p, Vector3 n, float m)
        {
            try
            {
                if (applyingRemote || ic == null || !Plugin.CfgObjSync.Value) return;
                Interactable it = null;
                try { it = ic.interactable; } catch (Exception __e) { Err.Swallow(__e); }
                if (it == null) it = FindByController(ic);
                if (it != null && !sharedLocal.Contains(it.id)) { OnWorldBreak(ic, 1, p, n, m); return; }
                if (it == null || !sharedLocal.Contains(it.id)) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                sharedLocal.Remove(it.id);
                var w = new NetDataWriter();
                w.Put(MsgItemEv); w.Put((byte)5); w.Put(GidOf(it)); w.Put(PresetNameOf(it));
                w.Put(p.x); w.Put(p.y); w.Put(p.z); w.Put(n.x); w.Put(n.y); w.Put(n.z); w.Put(m);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.Dbg($"[ITEM] предмет id={it.id} (общий id={GidOf(it)}) разбился, сообщаю второму игроку");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static void OnItemPickedUp(Interactable it)
        {
            if (!Plugin.CfgObjSync.Value) return;
            Plugin.Dbg($"[ITEM] я подобрал предмет id={it.id} (общий id={GidOf(it)})");
            SendItemEvent(1, it, false);
        }

        internal static void OnItemDropped(Interactable it)
        {
            if (!Plugin.CfgObjSync.Value) return;
            Plugin.Dbg($"[ITEM] я выбросил/поставил предмет id={it.id} (общий id={GidOf(it)})");
            SendItemEvent(2, it, true);
            pendingDrop = it;
            pendingDropUntil = Time.realtimeSinceStartup + 3f;
        }

        static System.Collections.Generic.Dictionary<int, GameObject> hiddenItems = new System.Collections.Generic.Dictionary<int, GameObject>();
        static float hiddenTickAt;

        static void HiddenTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < hiddenTickAt || hiddenItems.Count == 0) return;
            hiddenTickAt = now + 0.5f;
            try
            {
                var keys = new System.Collections.Generic.List<int>(hiddenItems.Keys);
                foreach (var k in keys)
                {
                    var it = ResolveId(k);
                    if (it == null) continue;
                    GameObject go = null;
                    try { go = it.spawnedObject; } catch (Exception __e) { Err.Swallow(__e); }
                    if (go == null) { try { if (it.controller != null) go = it.controller.gameObject; } catch (Exception __e) { Err.Swallow(__e); } }
                    if (go != null) { if (go.activeSelf) go.SetActive(false); hiddenItems[k] = go; }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void OnItemEvent(byte kind, int id, string preset, Vector3 pos, Vector3 euler, ItemState st)
        {
            try
            {
                if (kind == 1)
                {
                    var it = ResolveChecked(id, preset, 0);
                    if (it == null) return;
                    sharedLocal.Add(it.id);
                    GameObject go = null;
                    try { go = it.spawnedObject; } catch (Exception __e) { Err.Swallow(__e); }
                    if (go == null) { try { if (it.controller != null) go = it.controller.gameObject; } catch (Exception __e) { Err.Swallow(__e); } }
                    if (go != null) { go.SetActive(false); hiddenItems[id] = go; Plugin.Dbg($"[ITEM] второй игрок подобрал предмет id={id}, скрываю"); }
                }
                else if (kind == 5)
                {
                    var it = ResolveChecked(id, preset, 0);
                    hiddenItems.Remove(id);
                    if (it == null) return;
                    sharedLocal.Remove(it.id);
                    GameObject bgo = null;
                    try { bgo = it.spawnedObject; } catch (Exception __e) { Err.Swallow(__e); }
                    if (bgo != null && !bgo.activeSelf) bgo.SetActive(true);
                    applyingRemote = true;
                    try
                    {
                        bool done = false;
                        try
                        {
                            var ic = it.controller;
                            if (ic != null) { ic.BreakObject(pos, euler, st != null ? st.val : 3f, null); done = true; }
                        }
                        catch (Exception) { done = false; }
                        if (!done) it.SafeDelete(false);
                        Plugin.Dbg($"[ITEM] второй игрок разбил предмет id={id}, разбиваю свою копию (id={it.id}, разбито={done})");
                    }
                    finally { applyingRemote = false; }
                }
                else if (kind == 4)
                {
                    var it = ResolveChecked(id, preset, 0);
                    hiddenItems.Remove(id); idMap.Remove(id);
                    if (it == null) return;
                    sharedLocal.Remove(it.id);
                    applyingRemote = true;
                    try { it.SafeDelete(false); Plugin.Dbg($"[ITEM] второй игрок уничтожил/израсходовал предмет id={id}, удаляю свою копию (id={it.id})"); }
                    finally { applyingRemote = false; }
                }
                else if (kind == 2)
                {
                    GameObject hg;
                    if (hiddenItems.TryGetValue(id, out hg)) { hiddenItems.Remove(id); try { if (hg != null) hg.SetActive(true); } catch (Exception __e) { Err.Swallow(__e); } }
                    var it = ResolveChecked(id, preset, 0);
                    if (it == null)
                    {
                        var pr = FindPreset(preset);
                        if (pr == null) { Plugin.L.LogWarning($"[ITEM] пресет '{preset}' не найден, предмет id={id} не создан"); return; }
                        it = InteractableCreator.Instance.CreateWorldInteractable(pr, null, null, null, pos, euler, null, null, "");
                        if (it != null) { idMap[id] = it; gidOf[it.id] = id; sharedLocal.Add(it.id); ApplyState(it, st); Plugin.Dbg($"[ITEM] создан предмет '{preset}' (у отправителя id={id}, у меня id={it.id})"); }
                        else Plugin.L.LogWarning($"[ITEM] не удалось создать '{preset}'");
                    }
                    else
                    {
                        try { it.MoveInteractable(pos, euler, false); } catch (Exception __e) { Err.Swallow(__e); }
                        sharedLocal.Add(it.id);
                        ApplyState(it, st);
                        try
                        {
                            GameObject sgo = it.spawnedObject;
                            if (sgo == null && it.controller != null) sgo = it.controller.gameObject;
                            if (sgo != null)
                            {
                                if (!sgo.activeSelf) sgo.SetActive(true);
                                var rt = it.controller != null ? RootOf(it.controller) : sgo.transform;
                                rt.position = pos; rt.eulerAngles = euler;
                            }
                        }
                        catch (Exception __e) { Err.Swallow(__e); }
                        Plugin.Dbg($"[ITEM] второй игрок выбросил предмет id={id} (у меня id={it.id})");
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[ITEM] приём: {e.Message}"); }
        }

        static Interactable FindById(int id)
        {
            Interactable it;
            if (idCache != null && idCache.TryGetValue(id, out it) && it != null) return it;
            if (Time.realtimeSinceStartup < idCacheAt && idCache != null) return null;
            idCacheAt = Time.realtimeSinceStartup + 5f;
            try
            {
                idCache = new System.Collections.Generic.Dictionary<int, Interactable>();
                var list = CityData.Instance.interactableDirectory;
                for (int i = 0; i < list.Count; i++)
                {
                    var x = list[i];
                    if (x != null) idCache[x.id] = x;
                }
                Plugin.Dbg($"[OBJ] индекс объектов мира: {idCache.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[OBJ] индекс: {e.Message}"); }
            if (idCache.TryGetValue(id, out it)) return it;
            try { if (CityData.Instance.savableInteractableDictionary.TryGetValue(id, out it) && it != null) { idCache[id] = it; return it; } } catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static Interactable FindByController(InteractableController ic)
        {
            try
            {
                var list = CityData.Instance.interactableDirectory;
                for (int i = 0; i < list.Count; i++)
                {
                    var x = list[i];
                    if (x != null && x.controller != null && x.controller.Pointer == ic.Pointer) return x;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static bool IsBodyController(InteractableController ic)
        {
            try
            {
                var it = ic.interactable;
                if (it == null) return false;
                var cc = CitizenControls.Instance;
                if (cc != null && it.preset == cc.deadBodySearchInteractable) return true;
                return it.isActor != null || (it.objectRef != null && it.objectRef.TryCast<Human>() != null);
            }
            catch (Exception) { return false; }
        }

        static Transform RootOf(Component c)
        {
            Rigidbody rb = c.GetComponent<Rigidbody>();
            if (rb == null) rb = c.GetComponentInParent<Rigidbody>();
            return rb != null ? rb.transform : c.transform;
        }

        static void SendObject(byte flags)
        {
            try
            {
                var tr = RootOf(objTrackIc);
                var w = new NetDataWriter();
                w.Put(MsgObj);
                w.Put(objTrackId);
                w.Put(flags);
                w.Put(objTrackHash);
                Vector3 p = tr.position; Quaternion q = tr.rotation;
                w.Put(p.x); w.Put(p.y); w.Put(p.z);
                w.Put(q.x); w.Put(q.y); w.Put(q.z); w.Put(q.w);
                manager.SendToAll(w, (flags & 2) != 0 ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
            }
            catch (Exception e) { Plugin.L.LogWarning($"[OBJ] отправка: {e.Message}"); }
        }

        static void FinishObject()
        {
            if (objTrackIc != null && objTrackId >= 0) SendObject(2);
            if (objTrackIc != null) Plugin.Dbg($"[OBJ] объект id={objTrackId} отпущен, финальная поза отправлена");
            objTrackIc = null; objTrackIt = null; objTrackId = -1; objSettleUntil = 0f; objStillSince = -1f;
        }

        static void EnsureShared(Interactable it)
        {
            try
            {
                if (it == null || it.id < RuntimeIdStart || sharedLocal.Contains(it.id)) return;
                if (manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgObjSync.Value) return;
                SendItemEvent(2, it, true);
                Plugin.Dbg($"[ITEM] новый предмет '{PresetNameOf(it)}' id={it.id} (общий {GidOf(it)}): отправил второму игроку для создания");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[ITEM] EnsureShared: {e.Message}"); }
        }

        static void LocalObjects(float dt)
        {
            objSendTimer += dt;
            if (objSendTimer < 0.066f) return;
            objSendTimer = 0f;
            float now = Time.realtimeSinceStartup;
            InteractableController cur = null;
            try { cur = InteractionController.Instance.carryingObject; } catch (Exception __e) { Err.Swallow(__e); }
            if (cur == null && objTrackIc == null && pendingDrop != null)
            {
                try
                {
                    if (pendingDrop.controller != null)
                    {
                        objTrackIc = pendingDrop.controller; objTrackIt = pendingDrop; objTrackId = GidOf(pendingDrop); objTrackHash = NameHash(PresetNameOf(pendingDrop));
                        objSettleUntil = now + 8f; objStillSince = -1f; pendingDrop = null;
                    }
                    else if (now > pendingDropUntil) pendingDrop = null;
                }
                catch (Exception) { pendingDrop = null; }
            }
            if (cur != null && IsBodyController(cur)) cur = null;
            if (cur != null)
            {
                if (objTrackIc == null || objTrackIc.Pointer != cur.Pointer)
                {
                    FinishObject();
                    objTrackIc = cur;
                    try { objTrackIt = cur.interactable; } catch (Exception) { objTrackIt = null; }
                    if (objTrackIt == null) objTrackIt = FindByController(cur);
                    objTrackId = objTrackIt != null ? GidOf(objTrackIt) : -1;
                    objTrackHash = NameHash(PresetNameOf(objTrackIt));
                    string pn = "";
                    try { pn = objTrackIt != null && objTrackIt.preset != null ? objTrackIt.preset.name : ""; } catch (Exception __e) { Err.Swallow(__e); }
                    Plugin.Dbg($"[OBJ] несу объект id={objTrackId} пресет='{pn}'");
                    EnsureShared(objTrackIt);
                }
                objSettleUntil = 0f; objStillSince = -1f;
                if (objTrackId == 0 && objTrackIt != null) { objTrackId = GidOf(objTrackIt); if (objTrackId > 0) EnsureShared(objTrackIt); }
                if (objTrackId > 0) SendObject(1);
            }
            else if (objTrackIc != null)
            {
                if (objSettleUntil == 0f) { objSettleUntil = now + 8f; objStillSince = -1f; }
                if (objTrackId > 0) SendObject(0);
                bool still;
                try
                {
                    var rb = objTrackIc.GetComponent<Rigidbody>();
                    if (rb == null) rb = objTrackIc.GetComponentInParent<Rigidbody>();
                    still = rb == null || rb.IsSleeping() || rb.velocity.sqrMagnitude < 0.01f;
                }
                catch (Exception) { still = true; }
                if (still) { if (objStillSince < 0f) objStillSince = now; } else objStillSince = -1f;
                if ((objStillSince >= 0f && now - objStillSince > 0.6f) || now > objSettleUntil) FinishObject();
            }
        }

        class RObj { public Interactable it; public Transform tr; public Rigidbody rb; public Vector3 pos; public Quaternion rot; public float last; }
        static System.Collections.Generic.Dictionary<int, RObj> robjs = new System.Collections.Generic.Dictionary<int, RObj>();
        static System.Collections.Generic.HashSet<int> robjMissLogged = new System.Collections.Generic.HashSet<int>();

        static void ReleaseRemote(RObj o, bool exact)
        {
            try
            {
                if (o.tr != null && exact) { o.tr.position = o.pos; o.tr.rotation = o.rot; }
                if (o.rb != null) { o.rb.isKinematic = false; o.rb.velocity = Vector3.zero; o.rb.angularVelocity = Vector3.zero; }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void OnRemoteObject(int id, byte flags, int phash, Vector3 pos, Quaternion rot)
        {
            try
            {
                RObj o;
                if (!robjs.TryGetValue(id, out o))
                {
                    var it = ResolveChecked(id, null, phash);
                    if (it == null)
                    {
                        if (robjMissLogged.Add(id)) Plugin.L.LogWarning($"[OBJ] объект id={id} не найден у меня");
                        return;
                    }
                    Transform ctr = null;
                    try { if (it.controller != null) ctr = RootOf(it.controller); } catch (Exception __e) { Err.Swallow(__e); }
                    if (ctr == null) { try { if (it.spawnedObject != null) ctr = it.spawnedObject.transform; } catch (Exception __e) { Err.Swallow(__e); } }
                    if (ctr == null)
                    {
                        if ((flags & 2) != 0) { try { it.MoveInteractable(pos, rot.eulerAngles, false); } catch (Exception __e) { Err.Swallow(__e); } }
                        return;
                    }
                    o = new RObj { it = it, tr = ctr, rb = ctr.GetComponent<Rigidbody>() };
                    robjs[id] = o;
                    string pn = "";
                    try { pn = it.preset != null ? it.preset.name : ""; } catch (Exception __e) { Err.Swallow(__e); }
                    Plugin.Dbg($"[OBJ] второй игрок взял объект id={id} пресет='{pn}'");
                }
                o.pos = pos; o.rot = rot; o.last = Time.realtimeSinceStartup;
                if ((flags & 2) != 0)
                {
                    ReleaseRemote(o, true);
                    try { o.it.MoveInteractable(pos, rot.eulerAngles, false); } catch (Exception __e) { Err.Swallow(__e); }
                    robjs.Remove(id);
                    Plugin.Dbg($"[OBJ] объект id={id} положен/брошен вторым игроком");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[OBJ] приём: {e.Message}"); }
        }

        static void RemoteObjectsTick(float dt)
        {
            if (robjs.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            System.Collections.Generic.List<int> done = null;
            float k = 1f - Mathf.Exp(-18f * dt);
            foreach (var kv in robjs)
            {
                var o = kv.Value;
                try
                {
                    if (o.tr == null || now - o.last > 2.5f)
                    {
                        ReleaseRemote(o, false);
                        if (done == null) done = new System.Collections.Generic.List<int>();
                        done.Add(kv.Key);
                        continue;
                    }
                    if (o.rb != null && !o.rb.isKinematic) o.rb.isKinematic = true;
                    o.tr.position = Vector3.Lerp(o.tr.position, o.pos, k);
                    o.tr.rotation = Quaternion.Slerp(o.tr.rotation, o.rot, k);
                }
                catch (Exception) { if (done == null) done = new System.Collections.Generic.List<int>(); done.Add(kv.Key); }
            }
            if (done != null) foreach (var id in done) robjs.Remove(id);
        }
    }
}

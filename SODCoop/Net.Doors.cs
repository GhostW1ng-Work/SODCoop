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
        static bool applyingDoor;
        static bool doorActorFallback;
        static System.Collections.Generic.Dictionary<IntPtr, int> doorKeys = new System.Collections.Generic.Dictionary<IntPtr, int>();
        static float doorKeysAt;

        static bool IsPlayerActor(Actor a)
        {
            try { return a != null && a.isPlayer; } catch (Exception) { return false; }
        }

        static Actor DoorActor()
        {
            if (!doorActorFallback) return null;
            try { return Player.Instance; } catch (Exception) { return null; }
        }

        static int DoorKey(NewDoor d)
        {
            int k;
            if (doorKeys.TryGetValue(d.Pointer, out k)) return k;
            if (Time.realtimeSinceStartup < doorKeysAt) return -1;
            doorKeysAt = Time.realtimeSinceStartup + 2f;
            try
            {
                doorKeys.Clear();
                foreach (var kv in CityData.Instance.doorDictionary)
                    if (kv.Value != null) doorKeys[kv.Value.Pointer] = kv.Key;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] индекс дверей: {e.Message}"); }
            return doorKeys.TryGetValue(d.Pointer, out k) ? k : -1;
        }

        sealed class PendingDoor { public bool hasOpen, hasLock, hasJam, locked, jammed; public float ajar, at; }
        static readonly System.Collections.Generic.Dictionary<int, PendingDoor> doorPending = new System.Collections.Generic.Dictionary<int, PendingDoor>();
        static float doorPendingAt, doorPendingLogAt;
        static int doorPendingApplied, doorPendingDropped;

        static PendingDoor Pend(int key)
        {
            PendingDoor p;
            if (!doorPending.TryGetValue(key, out p)) { p = new PendingDoor(); doorPending[key] = p; }
            p.at = Time.unscaledTime;
            return p;
        }

        static void ApplyDoorJam(NewDoor d, bool val, bool fx)
        {
            applyingDoor = true;
            try { d.SetJammed(val, null, fx); } finally { applyingDoor = false; }
        }

        static void DoorPendingTick()
        {
            float now = Time.unscaledTime;
            if (doorPending.Count == 0 || now < doorPendingAt) return;
            doorPendingAt = now + 2f;
            var done = new System.Collections.Generic.List<int>();
            foreach (var kv in doorPending)
            {
                var p = kv.Value;
                if (now - p.at > 1800f) { done.Add(kv.Key); doorPendingDropped++; continue; }
                var d = DoorByKey(kv.Key);
                if (d == null) continue;
                try
                {
                    if (p.hasOpen && Mathf.Abs(d.ajar - p.ajar) > 0.01f) ApplyDoorOpen(d, p.ajar, true);
                    if (p.hasLock && d.isLocked != p.locked) ApplyDoorLock(d, p.locked);
                    if (p.hasJam && d.isJammed != p.jammed) ApplyDoorJam(d, p.jammed, false);
                    doorPendingApplied++;
                }
                catch (Exception e) { Err.Swallow(e); }
                done.Add(kv.Key);
            }
            foreach (int k in done) doorPending.Remove(k);
            if (now - doorPendingLogAt > 60f && (doorPendingApplied > 0 || doorPendingDropped > 0))
            {
                doorPendingLogAt = now;
                Plugin.L.LogInfo($"[DOOR] отложенные двери: применено {doorPendingApplied} после подгрузки района, ждут {doorPending.Count}, устарело {doorPendingDropped}");
                doorPendingApplied = 0; doorPendingDropped = 0;
            }
        }

        static NewDoor DoorByKey(int key)
        {
            try
            {
                NewDoor d;
                var dict = CityData.Instance.doorDictionary;
                if (dict != null && dict.TryGetValue(key, out d)) return d;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static void SendDoor(NetDataWriter w)
        {
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        internal static void OnDoorOpen(NewDoor d, float ajar, Actor actor, bool skip)
        {
            try
            {
                if (applyingDoor || !Plugin.CfgDoorSync.Value || d == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (!IsPlayerActor(actor) && !(IsHost && actor != null && Plugin.CfgNpcSync.Value)) return;
                int key = DoorKey(d);
                if (key < 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)1); w.Put(key); w.Put(ajar); w.Put(skip);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] я открыл/закрыл дверь {key}: ajar={ajar:0.00}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static void OnDoorFlag(byte kind, NewDoor d, bool val, Actor actor)
        {
            try
            {
                if (applyingDoor || !Plugin.CfgDoorSync.Value || d == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (kind == 2 && !IsPlayerActor(actor)) return;
                int key = DoorKey(d);
                if (key < 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put(kind); w.Put(key); w.Put(val);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] {(kind == 2 ? "замок" : "заклинивание")} двери {key}: {val}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static readonly System.Collections.Generic.Dictionary<long, bool> moveState = new System.Collections.Generic.Dictionary<long, bool>();

        internal static void OnLockState(Interactable it, bool val, Actor interactor)
        {
            try
            {
                if (applyingDoor || it == null || !Plugin.CfgDoorSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
                if (!IsPlayerActor(interactor) || it.id == 0) return;
                var w = new NetDataWriter();
                Vector3 wp = it.wPos;
                w.Put(MsgDoor); w.Put((byte)6); w.Put(GidOf(it)); w.Put(NameHash(PresetNameOf(it))); w.Put(val);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] я {(val ? "запер" : "отпер")} объект id={it.id} '{PresetNameOf(it)}'");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static readonly System.Collections.Generic.HashSet<string> seenSwitchNames = new System.Collections.Generic.HashSet<string>();

        static readonly System.Collections.Generic.Dictionary<int, Interactable> breakerLevers = new System.Collections.Generic.Dictionary<int, Interactable>();
        static readonly System.Collections.Generic.Dictionary<int, float> leverMineUntil = new System.Collections.Generic.Dictionary<int, float>();

        sealed class LeverWant { public bool val; public float until, fixAt; public int fixes; }
        static readonly System.Collections.Generic.Dictionary<int, LeverWant> leverWant = new System.Collections.Generic.Dictionary<int, LeverWant>();

        static readonly System.Collections.Generic.Dictionary<int, DoorMovementController> leverCtl = new System.Collections.Generic.Dictionary<int, DoorMovementController>();

        static DoorMovementController LeverCtl(Interactable lev)
        {
            DoorMovementController c;
            if (leverCtl.TryGetValue(lev.id, out c)) return c;
            float d;
            c = FindMoveCtl(NameHash(PresetNameOf(lev)), lev.wPos, out d);
            try { if (c != null && c.interactable != null && c.interactable.Pointer != lev.Pointer) c = null; } catch (Exception) { c = null; }
            leverCtl[lev.id] = c;
            Plugin.L.LogInfo($"[SWITCH] рычаг '{PresetNameOf(lev)}' id={lev.id}: {(c != null ? "двигается как дверца (" + c.GetIl2CppType().Name + "), isOpen=" + c.isOpen : "своего механизма нет")}");
            return c;
        }

        static void SetLever(Interactable lev, bool val, string why)
        {
            var c = LeverCtl(lev);
            bool b0 = lev.sw0, o0 = c != null && c.isOpen;
            applyingDoor = true;
            try
            {
                float want = val ? 1f : 0f;
                if (Mathf.Abs(lev.GetExtraStateValue() - want) > 0.01f) lev.SetExtraStateValue(want, true);
                if (lev.sw0 != val) lev.SetSwitchState(val, DoorActor(), true, true, false);
            }
            catch (Exception e) { Plugin.L.LogWarning("[SWITCH] рычаг: " + e.Message); }
            finally { applyingDoor = false; }
            float ev = -1f; try { ev = lev.GetExtraStateValue(); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[SWITCH] {why}: рычаг '{PresetNameOf(lev)}' -> {val}: sw0 {b0}->{lev.sw0}" + (c != null ? $", дверца {o0}->{c.isOpen}" : "") + $", extra={ev:F2}, кадр {Time.frameCount}");
        }

        static void HoldLever(Interactable it, bool val)
        {
            leverWant[it.id] = new LeverWant { val = val, until = Time.realtimeSinceStartup + 5f };
        }

        static void LeverTick()
        {
            if (leverWant.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            var done = new System.Collections.Generic.List<int>();
            foreach (var kv in leverWant)
            {
                var lw = kv.Value;
                if (now > lw.until || lw.fixes >= 6) { done.Add(kv.Key); continue; }
                if (lw.fixAt <= 0f || now < lw.fixAt) continue;
                lw.fixAt = 0f;
                Interactable lev;
                if (!breakerLevers.TryGetValue(kv.Key, out lev) || lev == null || lev.sw0 == lw.val) continue;
                lw.fixes++;
                SetLever(lev, lw.val, "игра вернула рычаг назад (" + lw.fixes + ")");
            }
            foreach (var id in done) leverWant.Remove(id);
            if (IsHost && done.Count > 0) alarmSnapAt = 0f;
        }

        static bool IsSecuritySwitch(Interactable it)
        {
            try
            {
                var sc = it.preset != null ? it.preset.specialCaseFlag : InteractablePreset.SpecialCase.none;
                return sc == InteractablePreset.SpecialCase.breakerSecurity || sc == InteractablePreset.SpecialCase.breakerLights || sc == InteractablePreset.SpecialCase.breakerDoors;
            }
            catch (Exception) { return false; }
        }

        static bool SwitchWorthy(Interactable it, string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("light") || n.Contains("lamp") || n.Contains("switch") || n.Contains("lantern")) return true;
            if (n.Contains("computer") || n.Contains("cruncher")) return true;
            try
            {
                var sc = it.preset != null ? it.preset.specialCaseFlag : InteractablePreset.SpecialCase.none;
                if (sc == InteractablePreset.SpecialCase.breakerSecurity || sc == InteractablePreset.SpecialCase.breakerLights || sc == InteractablePreset.SpecialCase.breakerDoors
                    || sc == InteractablePreset.SpecialCase.securityDoor || sc == InteractablePreset.SpecialCase.alarmSystem || sc == InteractablePreset.SpecialCase.otherSecuritySystem) return true;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            try { if (it.preset != null && it.preset.isLight != null) return true; } catch (Exception __e) { Err.Swallow(__e); }
            return false;
        }

        static float pressUntil;
        static float rateWindowStart; static int rateCount;
        static readonly System.Collections.Generic.Dictionary<long, bool> lastSwitchSent = new System.Collections.Generic.Dictionary<long, bool>();

        internal static void MarkPlayerPress(Actor who)
        {
            try { if (IsPlayerActor(who)) pressUntil = Time.realtimeSinceStartup + 1.5f; } catch (Exception __e) { Err.Swallow(__e); }
        }

        static readonly System.Collections.Generic.Dictionary<int, bool> lastRoomSent = new System.Collections.Generic.Dictionary<int, bool>();
        static System.Collections.Generic.List<int> roomSnapIds; static System.Collections.Generic.List<bool> roomSnapVals; static int roomSnapPos;

        internal static void OnRoomLights(NewRoom room, bool val, Actor actor)
        {
            try
            {
                if (applyingDoor || room == null || !Plugin.CfgDoorSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
                float now = Time.realtimeSinceStartup;
                if (!IsPlayerActor(actor) && now > pressUntil) return;
                int rid = room.roomID;
                if (lastRoomSent.TryGetValue(rid, out var lv) && lv == val) return;
                lastRoomSent[rid] = val;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)8); w.Put(rid); w.Put(val);
                SendDoor(w);
                Plugin.Dbg($"[LIGHT] я {(val ? "включил" : "выключил")} свет в комнате {rid}");
                AvatarLightFix(true);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static bool avatarMaskLogged;

        internal static void AvatarLightFix(bool log)
        {
            try
            {
                if (!Plugin.CfgAvatarLightFix.Value) return;
                SyncCur();
                for (int i = 0; i < players.Count; i++) FixAvatarLight(players[i] == curRp ? avatar : players[i].avatar, log);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void FixAvatarLight(GameObject av, bool log)
        {
            try
            {
                if (av == null) return;
                var rs = av.GetComponentsInChildren<Renderer>(true);
                uint first = 0; bool got = false; int changed = 0;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    uint m = r.renderingLayerMask;
                    if (!got) { first = m; got = true; }
                    if (m != 0xFFFFFFFFu) { r.renderingLayerMask = 0xFFFFFFFFu; changed++; }
                }
                if ((log || !avatarMaskLogged) && got)
                {
                    avatarMaskLogged = true;
                    Plugin.Dbg($"[LIGHT] маска света аватара: было {first:X}, исправлено рендереров {changed} из {rs.Length}");
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static NewRoom RoomById(int rid)
        {
            try
            {
                var dict = CityData.Instance.roomDictionary;
                NewRoom r;
                if (dict != null && dict.TryGetValue(rid, out r)) return r;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static bool ApplyRoomLights(int rid, bool val, bool log)
        {
            var r = RoomById(rid);
            if (r == null) { if (log) Plugin.L.LogWarning($"[LIGHT] комната {rid} не найдена"); return false; }
            lastRoomSent[rid] = val;
            bool before = r.mainLightStatus;
            applyingDoor = true;
            try
            {
                if (before != val) r.SetMainLights(val, "coop", null, false, false);
                try
                {
                    var sw = r.lightswitchInteractables;
                    if (sw != null)
                        for (int i = 0; i < sw.Count; i++)
                        {
                            var x = sw[i];
                            if (x != null && x.sw0 != val) x.SetSwitchState(val, DoorActor(), false, false, false);
                        }
                }
                catch (Exception e) { if (log) Plugin.L.LogWarning($"[LIGHT] выключатели: {e.Message}"); }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[LIGHT] SetMainLights: {e.Message}"); }
            finally { applyingDoor = false; }
            if (log) Plugin.Dbg($"[LIGHT] второй игрок {(val ? "включил" : "выключил")} свет в комнате {rid}: {before} -> {r.mainLightStatus}");
            AvatarLightFix(log);
            return true;
        }

        static void SendRoomSnapshot(NetPeer peer)
        {
            try
            {
                var dict = CityData.Instance.roomDictionary;
                if (dict == null) return;
                var ids = new System.Collections.Generic.List<int>();
                var vals = new System.Collections.Generic.List<bool>();
                foreach (var kv in dict)
                {
                    var r = kv.Value;
                    if (r == null) continue;
                    ids.Add(kv.Key); vals.Add(r.mainLightStatus);
                }
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)9); w.Put(ids.Count);
                for (int i = 0; i < ids.Count; i++) { w.Put(ids[i]); w.Put(vals[i]); }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.Dbg($"[LIGHT] отправил снимок света комнат: {ids.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[LIGHT] снимок: {e.Message}"); }
        }

        static void RoomSnapTick()
        {
            if (roomSnapIds == null) return;
            int budget = 15;
            while (budget > 0 && roomSnapPos < roomSnapIds.Count)
            {
                int i = roomSnapPos++;
                try
                {
                    var r = RoomById(roomSnapIds[i]);
                    if (r != null && r.mainLightStatus != roomSnapVals[i]) { ApplyRoomLights(roomSnapIds[i], roomSnapVals[i], false); budget--; }
                    else lastRoomSent[roomSnapIds[i]] = roomSnapVals[i];
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            if (roomSnapPos >= roomSnapIds.Count)
            {
                Plugin.Dbg($"[LIGHT] снимок света комнат применён: {roomSnapIds.Count}");
                roomSnapIds = null; roomSnapVals = null;
            }
        }

        static bool SwitchExcluded(string name)
        {
            return name.StartsWith("Door") || name.StartsWith("Drawer") || name.StartsWith("Telephone") || name == "";
        }

        internal static void OnSwitchState(Interactable it, int idx, bool val, Actor interactor)
        {
            try
            {
                if (it != null && idx == 0 && IsSecuritySwitch(it))
                {
                    breakerLevers[it.id] = it;
                    if (IsPlayerActor(interactor)) leverWant.Remove(it.id);
                    else if (!applyingDoor && interactor == null)
                    {
                        LeverWant lw;
                        if (leverWant.TryGetValue(it.id, out lw) && lw.val != val && Time.realtimeSinceStartup < lw.until)
                        {
                            lw.fixAt = Time.realtimeSinceStartup + 0.1f;
                            DoorMovementController c0; leverCtl.TryGetValue(it.id, out c0);
                            float ev0 = -1f; try { ev0 = it.GetExtraStateValue(); } catch (Exception __e) { Err.Swallow(__e); }
                            Plugin.L.LogInfo($"[SWITCH] игра ставит рычаг {val} (кадр {Time.frameCount}), extra={ev0:F2}" + (c0 != null ? $", дверца isOpen={c0.isOpen}" : ""));
                        }
                    }
                    if (!applyingDoor && IsPlayerActor(interactor)) { leverMineUntil[it.id] = Time.realtimeSinceStartup + 4f; alarmSnapAt = 0f; }
                }
                if (it != null && IsSecuritySwitch(it)) Plugin.L.LogInfo($"[SWITCH] '{PresetNameOf(it)}' id={it.id} sw{idx} -> {val}, кто: {(interactor == null ? "нет" : IsPlayerActor(interactor) ? "я" : "житель")}{(applyingDoor ? " (от другого игрока)" : "")}");
                if (applyingDoor || it == null || !Plugin.CfgDoorSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
                string name = PresetNameOf(it);
                bool placed = false;
                try { var fpar = it.furnitureParent; placed = fpar != null && fpar.userPlaced; } catch (Exception __e) { Err.Swallow(__e); }
                bool isMain = name.StartsWith("Lightswitch");
                try { if (it.preset != null && it.preset.isMainLight) isMain = true; } catch (Exception __e) { Err.Swallow(__e); }
                if (isMain && !placed)
                {
                    bool hasFurn = false;
                    try { hasFurn = it.furnitureParent != null; } catch (Exception __e) { Err.Swallow(__e); }
                    if (hasFurn && seenSwitchNames.Add("main:" + name)) Plugin.Dbg($"[SWITCH] пропущен основной свет '{name}' sw{idx} id={it.id} (мебель есть, userPlaced=false)");
                    return;
                }
                bool player = IsPlayerActor(interactor);
                bool noActor = interactor == null;
                if (!player && !noActor) return;
                float now = Time.realtimeSinceStartup;
                if (!player && now > pressUntil) return;
                if (now - rateWindowStart > 1f) { rateWindowStart = now; rateCount = 0; }
                if (++rateCount > 30) return;
                long sk = ((long)it.Pointer << 2) | (long)idx;
                if (lastSwitchSent.TryGetValue(sk, out var lastV) && lastV == val) return;
                lastSwitchSent[sk] = val;
                bool worthy = placed || (player ? (SwitchWorthy(it, name) || it.id < 0) : SwitchWorthy(it, name));
                if (SwitchExcluded(name) || !worthy)
                {
                    string k = name + "#" + idx;
                    if (seenSwitchNames.Add(k)) Plugin.Dbg($"[SWITCH] не синхронизирую '{name}' sw{idx} id={it.id}");
                    return;
                }
                var w = new NetDataWriter();
                Vector3 wp = it.wPos;
                w.Put(MsgDoor); w.Put((byte)7); w.Put(it.id); w.Put(NameHash(name)); w.Put(val);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z); w.Put((byte)idx);
                SendDoor(w);
                if (IsSecuritySwitch(it)) Plugin.L.LogInfo($"[SWITCH] отправил '{name}' sw{idx} -> {val}");
                Plugin.Dbg($"[SWITCH] я {(val ? "включил" : "выключил")} '{name}' sw{idx} id={it.id} (актёр: {(player ? "игрок" : "нет")}{(placed ? ", моя мебель" : "")})");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static Interactable FindInteractableAt(int hash, Vector3 wp, out float dist)
        {
            Interactable best = null; float bd = 0.15f;
            try
            {
                var list = CityData.Instance.interactableDirectory;
                for (int i = 0; i < list.Count; i++)
                {
                    var x = list[i];
                    if (x == null) continue;
                    float d = Vector3.Distance(x.wPos, wp);
                    if (d >= bd) continue;
                    if (NameHash(PresetNameOf(x)) != hash) continue;
                    bd = d; best = x;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[SWITCH] поиск: {e.Message}"); }
            dist = bd;
            return best;
        }

        static bool SwGet(Interactable it, int idx)
        {
            return idx == 0 ? it.sw0 : idx == 1 ? it.sw1 : idx == 2 ? it.sw2 : it.sw3;
        }

        static void ApplySwitch(int id, int hash, Vector3 wp, bool val, int idx)
        {
            float dist;
            var it = FindInteractableAt(hash, wp, out dist);
            if (it == null) { Plugin.L.LogWarning($"[SWITCH] объект id={id} в позиции {wp} не найден"); return; }
            bool before = SwGet(it, idx);
            lastSwitchSent[((long)it.Pointer << 2) | (long)idx] = val;
            if (idx == 0 && IsSecuritySwitch(it)) { breakerLevers[it.id] = it; HoldLever(it, val); }
            applyingDoor = true;
            try
            {
                if (before != val)
                {
                    Actor act = DoorActor();
                    if (idx == 0 && IsSecuritySwitch(it)) { applyingDoor = false; SetLever(it, val, "второй игрок"); }
                    else if (idx == 0) it.SetSwitchState(val, act, false, false, false);
                    else if (idx == 1) it.SetCustomState1(val, act, false, false, false);
                    else if (idx == 2) it.SetCustomState2(val, act, false, false, false);
                    else it.SetCustomState3(val, act, false, false, false);
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[SWITCH] SetState: {e.Message}"); }
            finally { applyingDoor = false; }
            if (IsSecuritySwitch(it)) Plugin.L.LogInfo($"[SWITCH] второй игрок переключил '{PresetNameOf(it)}' sw{idx}: {before} -> {SwGet(it, idx)} ({dist:F3} м)");
            Plugin.Dbg($"[SWITCH] второй игрок {(val ? "включил" : "выключил")} '{PresetNameOf(it)}' sw{idx} ({dist:F3} м), {before} -> {SwGet(it, idx)}");
        }

        static Il2CppArrayBase<DoorMovementController> moveCache; static int moveCacheFrame = -1;

        static Il2CppArrayBase<DoorMovementController> AllMoveCtls()
        {
            if (moveCache == null || moveCacheFrame != Time.frameCount) { moveCache = UnityEngine.Object.FindObjectsOfType<DoorMovementController>(); moveCacheFrame = Time.frameCount; }
            return moveCache;
        }

        static DoorMovementController FindMoveCtl(int hash, Vector3 wp, out float dist)
        {
            DoorMovementController c = null;
            float best = 0.1f;
            try
            {
                foreach (var cand in AllMoveCtls())
                {
                    if (cand == null) continue;
                    Interactable ci = null;
                    try { ci = cand.interactable; } catch (Exception __e) { Err.Swallow(__e); }
                    if (ci == null || NameHash(PresetNameOf(ci)) != hash) continue;
                    float d = Vector3.Distance(ci.wPos, wp);
                    if (d < best) { best = d; c = cand; }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] поиск: {e.Message}"); }
            dist = best;
            return c;
        }

        internal static void OnMoveDoor(DoorMovementController c, bool open, Actor interactor)
        {
            try
            {
                if (applyingDoor || !Plugin.CfgDoorSync.Value || c == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (!IsPlayerActor(interactor)) return;
                Interactable it = null;
                try { it = c.interactable; } catch (Exception __e) { Err.Swallow(__e); }
                if (it == null) return;
                long mk = (long)c.Pointer;
                if (moveState.TryGetValue(mk, out var known) && known == open) return;
                moveState[mk] = open;
                var w = new NetDataWriter();
                Vector3 wp = it.wPos;
                w.Put(MsgDoor); w.Put((byte)4); w.Put(GidOf(it)); w.Put(NameHash(PresetNameOf(it))); w.Put(open);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] я {(open ? "открыл" : "закрыл")} объект id={it.id} '{PresetNameOf(it)}'");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void ApplyDoorOpen(NewDoor d, float ajar, bool skip)
        {
            applyingDoor = true;
            try
            {
                try { d.SetOpen(ajar, DoorActor(), skip, 1f); }
                catch (Exception)
                {
                    if (doorActorFallback) throw;
                    doorActorFallback = true;
                    Plugin.L.LogWarning("[DOOR] SetOpen с пустым актёром не прошёл, использую локального игрока как актёра");
                    d.SetOpen(ajar, DoorActor(), skip, 1f);
                }
            }
            finally { applyingDoor = false; }
        }

        static void ApplyDoorLock(NewDoor d, bool val)
        {
            applyingDoor = true;
            try
            {
                try { d.SetLocked(val, DoorActor(), true); }
                catch (Exception)
                {
                    if (doorActorFallback) throw;
                    doorActorFallback = true;
                    d.SetLocked(val, DoorActor(), true);
                }
            }
            finally { applyingDoor = false; }
        }

        static void OnDoorMessage(NetDataReader r)
        {
            byte kind = r.GetByte();
            try
            {
                if (!Plugin.CfgDoorSync.Value) { if (kind == 5) {  } else return; }
                if (kind == 1)
                {
                    int key = r.GetInt(); float ajar = r.GetFloat(); bool skip = r.GetBool();
                    var d = DoorByKey(key);
                    if (d == null) { var pd = Pend(key); pd.hasOpen = true; pd.ajar = ajar; return; }
                    doorPending.Remove(key);
                    ApplyDoorOpen(d, ajar, skip);
                    if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] второй игрок открыл/закрыл дверь {key}: ajar={ajar:0.00}");
                }
                else if (kind == 2 || kind == 3)
                {
                    int key = r.GetInt(); bool val = r.GetBool();
                    var d = DoorByKey(key);
                    if (d == null) { var pd = Pend(key); if (kind == 2) { pd.hasLock = true; pd.locked = val; } else { pd.hasJam = true; pd.jammed = val; } return; }
                    if (kind == 2) ApplyDoorLock(d, val);
                    else ApplyDoorJam(d, val, val);
                    if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] второй игрок: {(kind == 2 ? "замок" : "заклинивание")} двери {key} = {val}");
                }
                else if (kind == 4)
                {
                    int id = r.GetInt(); int hash = r.GetInt(); bool open = r.GetBool();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    ApplyMove(id, hash, wp, open, false);
                }
                else if (kind == 14)
                {
                    int sid = r.GetInt();
                    var srems = GetRems(r);
                    ApplyApartmentSold(sid, true, srems);
                    if (IsHost && doorSender != null) ProfileNoteApt(doorSender, sid, srems);
                }
                else if (kind == 16)
                {
                    ApplyDecorRemote(r);
                }
                else if (kind == 15)
                {
                    int cnt = r.GetInt();
                    for (int i = 0; i < cnt; i++) { int sid = r.GetInt(); ApplyApartmentSold(sid, false, GetRems(r)); }
                    Plugin.L.LogInfo($"[APT] получен список проданных квартир: {cnt}");
                }
                else if (kind == 13)
                {
                    float gt = r.GetFloat(); int leap = r.GetInt(); int spd = r.GetByte();
                    float rain = r.GetFloat(), wind = r.GetFloat(), snow = r.GetFloat(), lit = r.GetFloat(), fog = r.GetFloat();
                    ApplyTime(gt, leap, spd, rain, wind, snow, lit, fog);
                }
                else if (kind == 11)
                {
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 cp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 vv = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    ApplyWindowBreak(wp, cp, vv, true);
                }
                else if (kind == 12)
                {
                    int cnt = r.GetInt();
                    for (int i = 0; i < cnt; i++)
                    {
                        Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                        ApplyWindowBreak(wp, wp, Vector3.zero, false);
                    }
                    Plugin.L.LogInfo($"[WINDOW] получен снимок разбитых окон: {cnt}");
                }
                else if (kind == 10)
                {
                    int type = r.GetByte(); int hash = r.GetInt();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 cp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 nn = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    float mag = r.GetFloat();
                    ApplyWorldBreak(type, hash, wp, cp, nn, mag);
                }
                else if (kind == 8)
                {
                    int rid = r.GetInt(); bool val = r.GetBool();
                    ApplyRoomLights(rid, val, true);
                }
                else if (kind == 9)
                {
                    int n = r.GetInt();
                    var ids = new System.Collections.Generic.List<int>(); var vals = new System.Collections.Generic.List<bool>();
                    for (int i = 0; i < n; i++) { ids.Add(r.GetInt()); vals.Add(r.GetBool()); }
                    if (!IsHost && Plugin.CfgDoorSnap.Value && Plugin.CfgDoorSync.Value)
                    {
                        roomSnapIds = ids; roomSnapVals = vals; roomSnapPos = 0;
                        Plugin.Dbg($"[LIGHT] получен снимок света комнат: {n}");
                    }
                }
                else if (kind == 7)
                {
                    int id = r.GetInt(); int hash = r.GetInt(); bool val = r.GetBool();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    int sidx = r.GetByte();
                    ApplySwitch(id, hash, wp, val, sidx);
                }
                else if (kind == 6)
                {
                    int id = r.GetInt(); int hash = r.GetInt(); bool val = r.GetBool();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    ApplyMove(id, hash, wp, val, true);
                }
                else if (kind >= 17 && kind <= 19) OnFurnMessage(kind, r);
                else if (kind == 5)
                {
                    int n = r.GetInt();
                    var list = new System.Collections.Generic.List<int[]>();
                    var ajars = new System.Collections.Generic.List<float>();
                    for (int i = 0; i < n; i++)
                    {
                        int key = r.GetInt(); float ajar = r.GetFloat(); byte fl = r.GetByte();
                        list.Add(new[] { key, fl }); ajars.Add(ajar);
                    }
                    if (!IsHost && Plugin.CfgDoorSnap.Value && Plugin.CfgDoorSync.Value)
                    {
                        doorSnap = list; doorSnapAjar = ajars; doorSnapPos = 0;
                        Plugin.L.LogInfo($"[DOOR] получен снимок дверей хоста: {n}");
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] приём kind={kind}: {e.Message}"); }
        }

        static System.Collections.Generic.List<int[]> doorSnap;
        static System.Collections.Generic.List<float> doorSnapAjar;
        static int doorSnapPos;

        static void SendDoorSnapshot(NetPeer peer)
        {
            try
            {
                if (!Plugin.CfgDoorSnap.Value || !Plugin.CfgDoorSync.Value) return;
                var dict = CityData.Instance.doorDictionary;
                if (dict == null) return;
                var keys = new System.Collections.Generic.List<int>();
                var ajars = new System.Collections.Generic.List<float>();
                var flags = new System.Collections.Generic.List<byte>();
                foreach (var kv in dict)
                {
                    var d = kv.Value;
                    if (d == null) continue;
                    byte fl = 0;
                    try { if (d.isLocked) fl |= 1; if (d.isJammed) fl |= 2; } catch (Exception __e) { Err.Swallow(__e); }
                    keys.Add(kv.Key); ajars.Add(d.ajar); flags.Add(fl);
                }
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)5); w.Put(keys.Count);
                for (int i = 0; i < keys.Count; i++) { w.Put(keys[i]); w.Put(ajars[i]); w.Put(flags[i]); }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[DOOR] отправил снимок дверей: {keys.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] снимок: {e.Message}"); }
        }

        class PendingMove
        {
            public int id, hash; public Vector3 wp; public bool isLock, val; public float due; public DoorMovementController c;
        }
        static readonly System.Collections.Generic.List<PendingMove> pendingMoves = new System.Collections.Generic.List<PendingMove>();

        static bool QueueMove(PendingMove m)
        {
            for (int i = 0; i < pendingMoves.Count; i++)
            {
                var o = pendingMoves[i];
                if (o.c == null && m.c == null && o.isLock == m.isLock && o.hash == m.hash && Vector3.Distance(o.wp, m.wp) < 0.1f) { pendingMoves[i] = m; return false; }
            }
            if (pendingMoves.Count > 300) pendingMoves.RemoveAt(0);
            pendingMoves.Add(m);
            return true;
        }

        static bool ApplyMove(int id, int hash, Vector3 wp, bool val, bool isLock, bool quiet = false)
        {
            float dist;
            var c = FindMoveCtl(hash, wp, out dist);
            if (c == null)
            {
                bool isNew = QueueMove(new PendingMove { id = id, hash = hash, wp = wp, isLock = isLock, val = val, due = Time.realtimeSinceStartup + (quiet ? 5f : 1f) });
                if (isNew && !quiet) Plugin.L.LogInfo($"[DOOR] объект id={id} далеко/не заспавнен, применю позже ({(isLock ? "замок" : "открыт")}={val})");
                return false;
            }
            Interactable ci = null;
            try { ci = c.interactable; } catch (Exception __e) { Err.Swallow(__e); }
            if (isLock)
            {
                if (ci == null) return false;
                applyingDoor = true;
                try { ci.SetLockedState(val, DoorActor(), false, true); }
                catch (Exception e) { Plugin.L.LogWarning($"[DOOR] SetLockedState: {e.Message}"); }
                finally { applyingDoor = false; }
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] второй игрок {(val ? "запер" : "отпер")} объект id={id} ({dist:F3} м)");
                return true;
            }
            bool before = c.isOpen;
            moveState[(long)c.Pointer] = val;
            string how = "уже в нужном состоянии";
            if (before != val)
            {
                applyingDoor = true;
                try
                {
                    how = "SetSwitchState";
                    if (ci != null) ci.SetSwitchState(val, DoorActor(), false, false, false);
                    else { how = "SetOpen"; c.SetOpen(val ? 1f : 0f, DoorActor(), false); }
                }
                catch (Exception e) { Plugin.L.LogWarning($"[DOOR] открытие: {e.Message}"); }
                finally { applyingDoor = false; }
                QueueMove(new PendingMove { id = id, hash = hash, wp = wp, isLock = false, val = val, due = Time.realtimeSinceStartup + 2f, c = c });
            }
            Plugin.L.LogInfo($"[DOOR] ящик id={id}: {dist:F3} м, isOpen {before} -> {c.isOpen} (нужно {val}), способ: {how}");
            return true;
        }

        static void MoveTick()
        {
            if (pendingMoves.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = pendingMoves.Count - 1; i >= 0; i--)
            {
                var m = pendingMoves[i];
                if (now < m.due) continue;
                pendingMoves.RemoveAt(i);
                try
                {
                    if (m.c != null)
                    {
                        if (m.c.isOpen != m.val)
                        {
                            applyingDoor = true;
                            try { if (m.val) m.c.OnOpen(DoorActor(), false); else m.c.OnClose(DoorActor(), false); }
                            finally { applyingDoor = false; }
                            Plugin.L.LogInfo($"[DOOR] ящик id={m.id}: доделал через OnOpen/OnClose, isOpen={m.c.isOpen}");
                        }
                    }
                    else ApplyMove(m.id, m.hash, m.wp, m.val, m.isLock, true);
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
        }

        static void DoorTick()
        {
            try { DoorPendingTick(); } catch (Exception e) { Err.Swallow(e); }
            MoveTick();
            RoomSnapTick();
            WindowTick();
            TimeTick();
            if (Time.unscaledTime - lastAptTick > 2f) { lastAptTick = Time.unscaledTime; AptTick(); }
            if (doorSnap == null) return;
            int budget = 60;
            while (budget-- > 0 && doorSnapPos < doorSnap.Count)
            {
                int i = doorSnapPos++;
                try
                {
                    int key = doorSnap[i][0]; byte fl = (byte)doorSnap[i][1]; float ajar = doorSnapAjar[i];
                    var d = DoorByKey(key);
                    if (d == null) { var pd = Pend(key); pd.hasOpen = true; pd.ajar = ajar; pd.hasLock = true; pd.locked = (fl & 1) != 0; pd.hasJam = true; pd.jammed = (fl & 2) != 0; continue; }
                    if (Mathf.Abs(d.ajar - ajar) > 0.01f) ApplyDoorOpen(d, ajar, true);
                    bool lk = (fl & 1) != 0;
                    if (d.isLocked != lk) ApplyDoorLock(d, lk);
                    bool jm = (fl & 2) != 0;
                    if (d.isJammed != jm) { applyingDoor = true; try { d.SetJammed(jm, null, false); } finally { applyingDoor = false; } }
                }
                catch (Exception e) { if (doorSnapPos < 5) Plugin.L.LogWarning($"[DOOR] снимок дверь #{i}: {e.Message}"); }
            }
            if (doorSnapPos >= doorSnap.Count)
            {
                Plugin.L.LogInfo($"[DOOR] снимок дверей применён: {doorSnap.Count}");
                doorSnap = null; doorSnapAjar = null;
            }
        }

        static readonly System.Collections.Generic.HashSet<int> soldAddrs = new System.Collections.Generic.HashSet<int>();
        static readonly System.Collections.Generic.HashSet<int> soldPending = new System.Collections.Generic.HashSet<int>();

        static int AddrIdOf(ResidenceController rc)
        {
            try { var a = rc != null ? rc.address : null; return a != null ? a.id : -1; } catch (Exception) { return -1; }
        }

        internal static bool IsApartmentSoldElsewhere(ResidenceController rc)
        {
            if (applyingRemote) return false;
            int id = AddrIdOf(rc);
            if (id >= 0 && soldAddrs.Contains(id))
            {
                Plugin.L.LogWarning($"[APT] квартира (адрес {id}) уже куплена вторым игроком, моя покупка блокируется");
                return true;
            }
            return false;
        }

        static bool buying;
        static readonly System.Collections.Generic.List<int[]> buyRemovals = new System.Collections.Generic.List<int[]>();
        static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int[]>> soldRemovals = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int[]>>();
        static readonly System.Collections.Generic.HashSet<int> removalsDone = new System.Collections.Generic.HashSet<int>();

        internal static void BeginBuy() { buying = true; buyRemovals.Clear(); }

        internal static void OnRoomCleared(NewRoom room, bool skipFlag, int opt)
        {
            try
            {
                if (!buying || applyingRemote || room == null) return;
                buyRemovals.Add(new[] { room.roomID, skipFlag ? 1 : 0, opt });
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void PutSale(NetDataWriter w, int id, System.Collections.Generic.List<int[]> rems)
        {
            w.Put(id);
            int n = rems != null ? rems.Count : 0;
            w.Put(n);
            for (int i = 0; i < n; i++) { w.Put(rems[i][0]); w.Put(rems[i][1] != 0); w.Put((byte)rems[i][2]); }
        }

        static System.Collections.Generic.List<int[]> GetRems(NetDataReader r)
        {
            int n = r.GetInt();
            var l = new System.Collections.Generic.List<int[]>();
            for (int i = 0; i < n; i++) { int rid = r.GetInt(); bool b = r.GetBool(); int o = r.GetByte(); l.Add(new[] { rid, b ? 1 : 0, o }); }
            return l;
        }

        internal static void OnApartmentBought(ResidenceController rc)
        {
            try
            {
                buying = false;
                if (applyingRemote) return;
                int id = AddrIdOf(rc);
                if (id < 0) return;
                if (!soldAddrs.Add(id)) return;
                var rems = new System.Collections.Generic.List<int[]>(buyRemovals);
                buyRemovals.Clear();
                soldRemovals[id] = rems; removalsDone.Add(id);
                Plugin.L.LogInfo($"[APT] я купил квартиру (адрес {id}): {rc.GetResidenceString()}, очисток комнат: {rems.Count}");
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)14); PutSale(w, id, rems);
                SendDoor(w);
            }
            catch (Exception e) { Plugin.L.LogWarning($"[APT] {e.Message}"); }
        }

        static void ApplyRemovals(int id, System.Collections.Generic.List<int[]> rems)
        {
            if (rems == null || rems.Count == 0 || !removalsDone.Add(id)) return;
            soldRemovals[id] = rems;
            applyingRemote = true; applyingDoor = true;
            try
            {
                foreach (var e in rems)
                {
                    var room = RoomById(e[0]);
                    if (room == null) { Plugin.L.LogWarning($"[APT] комната {e[0]} не найдена"); continue; }
                    int opt = e[2] == 2 ? 1 : e[2];
                    try
                    {
                        room.RemoveAllInhabitantFurniture(e[1] != 0, (FurnitureClusterLocation.RemoveInteractablesOption)opt);
                        Plugin.L.LogInfo($"[APT] очистил комнату {e[0]} (флаг={e[1]}, опция={opt})");
                    }
                    catch (Exception ex) { Plugin.L.LogWarning($"[APT] очистка комнаты {e[0]}: {ex.Message}"); }
                }
            }
            finally { applyingRemote = false; applyingDoor = false; }
        }

        static System.Collections.Generic.Dictionary<string, Material> matByName;

        static Material FindMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (matByName == null)
            {
                matByName = new System.Collections.Generic.Dictionary<string, Material>();
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && !matByName.ContainsKey(m.name)) matByName[m.name] = m;
            }
            Material res; matByName.TryGetValue(name, out res);
            return res;
        }

        static MaterialGroupPreset FindMatPreset(string name)
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<MaterialGroupPreset>())
                if (m != null && m.name == name) return m;
            return null;
        }

        static void PutColor(NetDataWriter w, Color c) { w.Put(c.r); w.Put(c.g); w.Put(c.b); w.Put(c.a); }
        static Color GetColor(NetDataReader r) { return new Color(r.GetFloat(), r.GetFloat(), r.GetFloat(), r.GetFloat()); }

        internal static void OnDecorApplied(PlayerApartmentController pac, MaterialGroupPreset.MaterialType type, MaterialGroupPreset mat, Toolbox.MaterialKey key, bool save)
        {
            try
            {
                if (!save || applyingRemote || mat == null || manager == null || manager.ConnectedPeersCount == 0) return;
                NewRoom room = pac.decoratingRoom;
                if (room == null) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)16);
                w.Put(room.roomID); w.Put((byte)(int)type); w.Put(mat.name);
                bool hk = key != null;
                w.Put(hk);
                if (hk)
                {
                    w.Put(key.baseMaterial != null ? key.baseMaterial.name : "");
                    PutColor(w, key.mainColour); PutColor(w, key.colour1); PutColor(w, key.colour2); PutColor(w, key.colour3);
                    w.Put(key.grubiness);
                }
                SendDoor(w);
                Plugin.L.LogInfo($"[DECOR] я применил отделку: комната {room.roomID}, тип {type}, материал '{mat.name}'");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DECOR] {e.Message}"); }
        }

        static void ApplyDecorRemote(NetDataReader r)
        {
            int rid = r.GetInt(); int type = r.GetByte(); string mname = r.GetString();
            Toolbox.MaterialKey key = null;
            if (r.GetBool())
            {
                string bm = r.GetString();
                key = new Toolbox.MaterialKey();
                key.baseMaterial = FindMaterial(bm);
                key.mainColour = GetColor(r); key.colour1 = GetColor(r); key.colour2 = GetColor(r); key.colour3 = GetColor(r);
                key.grubiness = r.GetFloat();
            }
            var room = RoomById(rid);
            var preset = FindMatPreset(mname);
            var pac = PlayerApartmentController.Instance;
            if (room == null || preset == null || pac == null) { Plugin.L.LogWarning($"[DECOR] не могу применить: комната={(room != null)} материал='{mname}'={(preset != null)} контроллер={(pac != null)}"); return; }
            applyingRemote = true; applyingDoor = true;
            var prevRoom = pac.decoratingRoom;
            try
            {
                pac.decoratingRoom = room;
                pac.ApplyDecor((MaterialGroupPreset.MaterialType)type, preset, key, true);
                Plugin.L.LogInfo($"[DECOR] второй игрок сменил отделку: комната {rid}, тип {type}, материал '{mname}'");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DECOR] применить: {e.Message}"); }
            finally { try { pac.decoratingRoom = prevRoom; } catch (Exception __e) { Err.Swallow(__e); } applyingRemote = false; applyingDoor = false; }
        }

        static bool TryRemoveSaleNote(int id)
        {
            var dict = CityData.Instance.addressDictionary;
            NewAddress a = null;
            if (dict == null || !dict.TryGetValue(id, out a) || a == null) return false;
            Interactable note = null;
            try { note = a.saleNote; } catch (Exception __e) { Err.Swallow(__e); }
            if (note == null) return true;
            applyingRemote = true; applyingDoor = true;
            try { note.SafeDelete(false); a.saleNote = null; Plugin.L.LogInfo($"[APT] убрал объявление о продаже (адрес {id})"); }
            catch (Exception e) { Plugin.L.LogWarning($"[APT] убрать объявление: {e.Message}"); }
            finally { applyingRemote = false; applyingDoor = false; }
            return true;
        }

        static void ApplyApartmentSold(int id, bool log, System.Collections.Generic.List<int[]> rems = null)
        {
            soldAddrs.Add(id);
            try { ApplyRemovals(id, rems); } catch (Exception e) { Plugin.L.LogWarning($"[APT] {e.Message}"); }
            bool ok = false;
            try { ok = TryRemoveSaleNote(id); } catch (Exception e) { Plugin.L.LogWarning($"[APT] {e.Message}"); }
            if (log) Plugin.L.LogInfo($"[APT] второй игрок купил квартиру (адрес {id}), найдено={ok}");
            if (!ok) soldPending.Add(id);
        }
    }
}

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
        static bool applyingAlarm;

        internal static bool AlarmAllowed()
        {
            return applyingAlarm || manager == null || RtMode != NetMode.Client;
        }

        static void PutAlarmTarget(NetDataWriter w, Human t)
        {
            if (t == null) { w.Put((byte)0); w.Put(0); return; }
            if (t.isPlayer) { w.Put((byte)1); w.Put(0); return; }
            foreach (var rp in players) if (PuppetIdOf(rp) == t.humanID) { w.Put((byte)1); w.Put(rp.pid); return; }
            w.Put((byte)2); w.Put(t.humanID);
        }

        static Human GetAlarmTarget(NetDataReader r)
        {
            byte k = r.GetByte(); int v = r.GetInt();
            if (k == 1) return v == MyPid ? Player.Instance : null;
            if (k == 2) { Human h; if (CityData.Instance.GetHuman(v, out h, false)) return h; }
            return null;
        }

        sealed class AlarmSent { public bool val; public string who = ""; public float at; public int repeats; }
        static readonly System.Collections.Generic.Dictionary<string, AlarmSent> alarmSent = new System.Collections.Generic.Dictionary<string, AlarmSent>();

        static string AlarmTargetLabel(Human t)
        {
            if (t == null) return "нет";
            try
            {
                if (t.isPlayer) return "хост";
                foreach (var rp in players) if (PuppetIdOf(rp) == t.humanID) return "игрок " + rp.pid + " (двойник)";
                return "житель " + t.humanID;
            }
            catch (Exception) { return "?"; }
        }

        static bool AlarmShouldSend(string key, bool val, Human target, out string who, out int repeats)
        {
            who = AlarmTargetLabel(target); repeats = 0;
            float now = Time.realtimeSinceStartup;
            AlarmSent s;
            if (!alarmSent.TryGetValue(key, out s)) { s = new AlarmSent(); alarmSent[key] = s; s.val = !val; }
            if (s.val == val && s.who == who && now - s.at < 5f) { s.repeats++; return false; }
            repeats = s.repeats;
            s.val = val; s.who = who; s.at = now; s.repeats = 0;
            return true;
        }

        internal static void OnBuildingAlarm(NewBuilding b, bool val, Human target, NewFloor floor)
        {
            if (!IsHost || applyingAlarm || b == null || manager == null || manager.ConnectedPeersCount == 0) return;
            string who; int rep;
            if (!AlarmShouldSend("b" + b.buildingID + (floor != null ? "_" + floor.floor : ""), val, target, out who, out rep)) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)16); w.Put((byte)1); w.Put(b.buildingID); w.Put(val);
            w.Put(floor != null ? floor.floor : int.MinValue); PutAlarmTarget(w, target);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[ALARM] тревога в здании {b.buildingID}{(floor != null ? " этаж " + floor.floor : "")}: {(val ? "включена" : "выключена")}, цель: {who}{(rep > 0 ? $", до этого продлена {rep} раз" : "")}");
        }

        internal static void OnAddressAlarm(NewAddress a, bool val, Human target)
        {
            if (!IsHost || applyingAlarm || a == null || manager == null || manager.ConnectedPeersCount == 0) return;
            string who; int rep;
            if (!AlarmShouldSend("a" + a.id, val, target, out who, out rep)) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)16); w.Put((byte)2); w.Put(a.id); w.Put(val); w.Put(0); PutAlarmTarget(w, target);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[ALARM] тревога в {a.name}: {(val ? "включена" : "выключена")}, цель: {who}{(rep > 0 ? $", до этого продлена {rep} раз" : "")}");
        }

        static bool illSentTres, illSentAction, illSentAny;

        static int illSentEsc;
        static float illTickAt, illSentAt;

        static void IllegalTick(Player p)
        {
            if (IsHost || manager == null || p == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < illTickAt) return;
            illTickAt = now + 0.25f;
            bool off = downed;
            try { off = off || p.playerKOInProgress; } catch (Exception __e) { Err.Swallow(__e); }
            bool tres = !off && p.isTrespassing;
            bool act = !off && p.illegalActionActive;
            int esc = tres ? p.trespassingEscalation : 0;
            bool arrestOnly = tres && OnlyHouseArrestTrespass(p);
            if (illSentAny && tres == illSentTres && act == illSentAction && esc == illSentEsc && arrestOnly == illSentArrest && now - illSentAt < 3f) return;
            illSentAny = true; illSentTres = tres; illSentAction = act; illSentEsc = esc; illSentArrest = arrestOnly; illSentAt = now;
            var w = new NetDataWriter(); w.Put(MsgIllegal); w.Put((byte)((tres ? 1 : 0) | (act ? 2 : 0) | (arrestOnly ? 4 : 0))); w.Put((byte)Mathf.Clamp(esc, 0, 255));
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void OnIllegalMsg(RP rp, NetDataReader r)
        {
            byte f = r.GetByte(); int esc = r.GetByte();
            bool tres = (f & 1) != 0, act = (f & 2) != 0, arrest = (f & 4) != 0;
            if (tres != rp.illTres || act != rp.illAction || arrest != rp.illArrestOnly) Plugin.L.LogInfo($"[ALARM] игрок {rp.pid}: {(tres ? "нарушает границы (эскалация " + esc + ")" + (arrest ? " только из-за домашнего ареста - реагирует полиция" : "") : "в разрешённом месте")}{(act ? ", незаконное действие" : "")}");
            rp.illTres = tres; rp.illAction = act; rp.illEsc = esc; rp.illArrestOnly = arrest;
            var c = PuppetOf(rp);
            if ((object)c != null) ApplyPuppetIllegal(c, rp);
        }

        static void ApplyPuppetIllegal(Citizen c, RP rp)
        {
            if (c == null || rp == null) return;
            bool off = rp.downed;
            bool tres = !off && rp.illTres, act = !off && rp.illAction;
            c.isTrespassing = tres; c.illegalAreaActive = tres; c.trespassingEscalation = tres ? rp.illEsc : 0; c.illegalActionActive = act;
            c.UpdateIllegalStatus();
        }

        internal static bool PuppetIllegalOverride(Actor a)
        {
            if (!IsHost || a == null || players.Count == 0) return false;
            IntPtr ptr = a.Pointer;
            for (int i = 0; i < players.Count; i++)
            {
                var c = PuppetOf(players[i]);
                if ((object)c == null || c.Pointer != ptr) continue;
                ApplyPuppetIllegal(c, players[i]);
                return true;
            }
            return false;
        }

        static void ClearPuppetHeat(RP rp, string why)
        {
            if (!IsHost || rp == null) return;
            var c = PuppetOf(rp);
            if (c == null) return;
            ApplyPuppetIllegal(c, rp);
            int alarms = 0, chasers = 0;
            foreach (var b in CityBuildings.Instance.buildingDirectory)
            {
                try { if (b != null && b.alarmTargets != null && b.alarmTargets.Contains(c)) { b.alarmTargets.Remove(c); alarms++; } } catch (Exception __e) { Err.Swallow(__e); }
            }
            foreach (var x in AlarmAddresses())
            {
                try { var a = x.a; if (a != null && a.alarmTargets != null && a.alarmTargets.Contains(c)) { a.alarmTargets.Remove(c); alarms++; } } catch (Exception __e) { Err.Swallow(__e); }
            }
            IntPtr ptr = c.Pointer;
            var list = CityData.Instance.citizenDirectory;
            for (int i = 0; list != null && i < list.Count; i++)
            {
                var h = list[i];
                if (h == null || h.Pointer == ptr) continue;
                try
                {
                    var ai = h.ai;
                    if (ai == null) continue;
                    bool chasing = (ai.persuitTarget != null && ai.persuitTarget.Pointer == ptr) || (ai.attackTarget != null && ai.attackTarget.Pointer == ptr);
                    try { if (h.seesIllegal != null && h.seesIllegal.ContainsKey(c)) { h.seesIllegal.Remove(c); chasing = true; } } catch (Exception __e) { Err.Swallow(__e); }
                    if (!chasing) continue;
                    if (ai.persuitTarget != null && ai.persuitTarget.Pointer == ptr) { ai.SetPersuit(false); ai.persuitTarget = null; }
                    if (ai.attackTarget != null && ai.attackTarget.Pointer == ptr) { ai.CancelCombat(); ai.attackTarget = null; }
                    ai.ResetInvestigate();
                    chasers++;
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            Plugin.L.LogInfo($"[ALARM] игрок {rp.pid} {why}: двойник убран из целей тревог ({alarms}), погоню прекратили {chasers}");
        }

        static NewBuilding BuildingById(int id)
        {
            var l = CityBuildings.Instance.buildingDirectory;
            if (l != null) foreach (var b in l) if (b != null && b.buildingID == id) return b;
            return null;
        }

        static void OnAlarmMsg(NetDataReader r)
        {
            byte kind = r.GetByte(); int id = r.GetInt(); bool val = r.GetBool(); int floorNum = r.GetInt();
            var target = GetAlarmTarget(r);
            applyingAlarm = true;
            try
            {
                if (kind == 1)
                {
                    var b = BuildingById(id); if (b == null) { Plugin.L.LogWarning($"[ALARM] здание {id} не найдено"); return; }
                    NewFloor f = null; if (floorNum != int.MinValue && b.floors != null) b.floors.TryGetValue(floorNum, out f);
                    b.SetAlarm(val, target, f);
                    Plugin.L.LogInfo($"[ALARM] тревога в здании {id}: {(val ? "включена" : "выключена")} (от хоста)");
                }
                else
                {
                    NewAddress a; if (!CityData.Instance.addressDictionary.TryGetValue(id, out a) || a == null) return;
                    a.SetAlarm(val, target);
                    Plugin.L.LogInfo($"[ALARM] тревога в {a.name}: {(val ? "включена" : "выключена")} (от хоста)");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[ALARM] " + e.Message); }
            finally { applyingAlarm = false; }
        }

        static NetDataWriter AlarmSnapshot(out string sig)
        {
            var bl = new System.Collections.Generic.List<int>(); var al = new System.Collections.Generic.List<int>();
            foreach (var b in CityBuildings.Instance.buildingDirectory) if (b != null && b.alarmActive) bl.Add(b.buildingID);
            foreach (var x in AlarmAddresses()) { try { if (x.a != null && x.a.alarmActive) al.Add(x.key); } catch (Exception __e) { Err.Swallow(__e); } }
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)17);
            w.Put(bl.Count); foreach (var x in bl) w.Put(x);
            w.Put(al.Count); foreach (var x in al) w.Put(x);
            var dl = new System.Collections.Generic.List<int>();
            foreach (var d in SecurityDoors()) { try { if (d.sw0) dl.Add(d.id); } catch (Exception __e) { Err.Swallow(__e); } }
            w.Put(dl.Count); foreach (var x in dl) w.Put(x);
            var ll = new System.Collections.Generic.List<int>();
            foreach (var kv in breakerLevers) { try { if (kv.Value != null && kv.Value.sw0) ll.Add(kv.Key); } catch (Exception __e) { Err.Swallow(__e); } }
            w.Put(breakerLevers.Count); foreach (var kv in breakerLevers) { w.Put(kv.Key); w.Put(ll.Contains(kv.Key)); }
            sig = string.Join(",", bl) + "|" + string.Join(",", al) + "|" + string.Join(",", dl) + "|" + string.Join(",", ll);
            return w;
        }

        static System.Collections.Generic.List<Interactable> secDoorsCache;
        static float secDoorsAt;

        static System.Collections.Generic.List<(int key, NewAddress a)> alarmAddrCache;
        static int alarmAddrCount = -1;

        static System.Collections.Generic.List<(int key, NewAddress a)> AlarmAddresses()
        {
            var dict = CityData.Instance.addressDictionary;
            int n = dict != null ? dict.Count : 0;
            if (alarmAddrCache != null && n == alarmAddrCount) return alarmAddrCache;
            var res = new System.Collections.Generic.List<(int, NewAddress)>();
            if (dict != null) foreach (var kv in dict) if (kv.Value != null) res.Add((kv.Key, kv.Value));
            alarmAddrCache = res; alarmAddrCount = n;
            Plugin.L.LogInfo($"[ALARM] адресов для тревог: {res.Count}");
            return res;
        }

        static System.Collections.Generic.List<Interactable> SecurityDoors()
        {
            if (secDoorsCache != null && (secDoorsCache.Count > 0 || Time.realtimeSinceStartup < secDoorsAt)) return secDoorsCache;
            secDoorsAt = Time.realtimeSinceStartup + 60f;
            var res = new System.Collections.Generic.List<Interactable>();
            foreach (var b in CityBuildings.Instance.buildingDirectory)
            {
                if (b == null || b.floors == null) continue;
                foreach (var kv in b.floors)
                {
                    var f = kv.Value; if (f == null || f.securityDoors == null) continue;
                    foreach (var d in f.securityDoors) if (d != null) res.Add(d);
                }
            }
            secDoorsCache = res;
            Plugin.L.LogInfo($"[ALARM] штор охраны в городе: {res.Count}");
            return res;
        }

        static bool alarmSnapLogged;

        static void SendAlarmSnapshot(NetPeer peer)
        {
            string sig;
            peer.Send(AlarmSnapshot(out sig), DeliveryMethod.ReliableOrdered);
        }

        static string alarmSig = "";
        static float alarmResendAt;
        static float alarmSnapAt;

        static void AlarmSnapshotTick()
        {
            if (Time.realtimeSinceStartup < alarmSnapAt) return;
            alarmSnapAt = Time.realtimeSinceStartup + 2f;
            string sig;
            var w = AlarmSnapshot(out sig);
            if (sig == alarmSig && Time.realtimeSinceStartup < alarmResendAt) return;
            alarmSig = sig; alarmResendAt = Time.realtimeSinceStartup + 10f;
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void OnAlarmSnapshot(NetDataReader r)
        {
            var bs = new System.Collections.Generic.HashSet<int>(); int n = r.GetInt(); for (int i = 0; i < n; i++) bs.Add(r.GetInt());
            var ads = new System.Collections.Generic.HashSet<int>(); n = r.GetInt(); for (int i = 0; i < n; i++) ads.Add(r.GetInt());
            int off = 0, on = 0, doors = 0, levers = 0;
            applyingAlarm = true;
            try
            {
                foreach (var b in CityBuildings.Instance.buildingDirectory)
                {
                    if (b == null) continue;
                    bool want = bs.Contains(b.buildingID);
                    if (b.alarmActive != want) { try { b.SetAlarm(want, null, null); if (want) on++; else off++; } catch (Exception __e) { Err.Swallow(__e); } }
                }
                foreach (var x in AlarmAddresses())
                {
                    var a = x.a; if (a == null) continue;
                    bool want = ads.Contains(x.key);
                    if (a.alarmActive != want) { try { a.SetAlarm(want, null); if (want) on++; else off++; } catch (Exception __e) { Err.Swallow(__e); } }
                }
                if (r.AvailableBytes >= 4)
                {
                    float now = Time.realtimeSinceStartup;
                    bool mineFresh = false;
                    foreach (var kv in leverMineUntil) if (kv.Value > now) { mineFresh = true; break; }
                    var ds = new System.Collections.Generic.HashSet<int>(); n = r.GetInt(); for (int i = 0; i < n; i++) ds.Add(r.GetInt());
                    var lv = new System.Collections.Generic.Dictionary<int, bool>();
                    if (r.AvailableBytes >= 4) { n = r.GetInt(); for (int i = 0; i < n && r.AvailableBytes >= 5; i++) { int id = r.GetInt(); lv[id] = r.GetBool(); } }
                    applyingDoor = true;
                    try
                    {
                        if (!mineFresh)
                        {
                            foreach (var d in SecurityDoors())
                            {
                                bool want = ds.Contains(d.id);
                                if (d.sw0 == want) continue;
                                try { d.SetSwitchState(want, null, true, true, false); doors++; } catch (Exception __e) { Err.Swallow(__e); }
                            }
                            foreach (var kv in lv)
                            {
                                Interactable lev;
                                if (!breakerLevers.TryGetValue(kv.Key, out lev) || lev == null || lev.sw0 == kv.Value) continue;
                                if (leverWant.ContainsKey(kv.Key)) continue;
                                try { applyingDoor = false; SetLever(lev, kv.Value, "снимок хоста"); levers++; HoldLever(lev, kv.Value); } catch (Exception __e) { Err.Swallow(__e); }
                                applyingDoor = true;
                                try { lastSwitchSent[((long)lev.Pointer << 2)] = kv.Value; } catch (Exception __e) { Err.Swallow(__e); }
                            }
                        }
                    }
                    finally { applyingDoor = false; }
                }
            }
            finally { applyingAlarm = false; }
            if (on + off + doors + levers > 0 || !alarmSnapLogged) { alarmSnapLogged = true; Plugin.L.LogInfo($"[ALARM] тревоги как у хоста: включил {on}, снял {off}, штор передвинул {doors}, рычагов {levers}"); }
        }
    }
}

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
        static bool murderErrLogged, applyingMurderCase;

        static bool MurderLocKey(NewGameLocation loc, out byte kind, out int id)
        {
            kind = 0; id = 0;
            var ad = loc.TryCast<NewAddress>(); var st = loc.TryCast<StreetController>();
            if (ad != null) { kind = 1; id = ad.id; } else if (st != null) { kind = 2; id = st.streetID; }
            return kind != 0;
        }

        static readonly System.Collections.Generic.HashSet<int> murderCaseSeen = new System.Collections.Generic.HashSet<int>();

        static string MurderCaseName()
        {
            try { var mc = MurderController.Instance; return mc != null && mc.currentActiveCase != null ? (mc.currentActiveCase.name ?? "") : ""; } catch (Exception) { return ""; }
        }

        static bool MurderCaseOnBoard()
        {
            try
            {
                var mc = MurderController.Instance; var cp = CasePanelController.Instance;
                if (mc == null || cp == null || mc.currentActiveCase == null || cp.activeCases == null) return false;
                foreach (var c in cp.activeCases) if (c != null && c.Pointer == mc.currentActiveCase.Pointer) return true;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return false;
        }

        static int CurrentMurderId()
        {
            try { var mc = MurderController.Instance; var m = mc != null ? mc.GetCurrentMurder() : null; return m != null ? m.murderID : -1; } catch (Exception) { return -1; }
        }

        internal static void OnMurderCaseNotify(NewGameLocation loc) { SendMurderCase(loc); }

        internal static void OnVictimDiscovered()
        {
            NewGameLocation loc = null;
            try { var mc = MurderController.Instance; var m = mc != null ? mc.GetCurrentMurder() : null; if (m != null) loc = m.location; } catch (Exception __e) { Err.Swallow(__e); }
            if (loc != null) SendMurderCase(loc); else { int mid = CurrentMurderId(); if (mid >= 0) murderCaseSeen.Add(mid); }
        }

        static void SendMurderCase(NewGameLocation loc)
        {
            int mid = CurrentMurderId();
            bool first = murderCaseSeen.Add(mid);
            if (applyingMurderCase || !first || loc == null || manager == null || manager.ConnectedPeersCount == 0) return;
            byte kind; int id;
            if (!MurderLocKey(loc, out kind, out id)) { Plugin.L.LogWarning("[MURDER] новое дело: место не адрес и не улица"); return; }
            var w = new NetDataWriter(); w.Put(MsgMurder); w.Put((byte)3); w.Put(kind); w.Put(id); w.Put(false); w.Put(MurderCaseName());
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[MURDER] новое дело об убийстве {mid} ({loc.name}) - сообщаю другим");
        }
        static float murderPollAt;
        static readonly System.Collections.Generic.Dictionary<int, string> murderSentSig = new System.Collections.Generic.Dictionary<int, string>();

        internal static bool BlockLocalMurders()
        {
            return manager != null && RtMode == NetMode.Client;
        }

        static string MurderSig(MurderController.Murder m, bool active)
        {
            return active + "|" + (int)m.state + "|" + m.murdererID + "|" + m.victimID + "|" + m.victimSiteID + "|" + m.weaponID + "|" + m.ammoID + "|" + m.callingCardID + "|" +
                (m.graffitiIDs != null ? m.graffitiIDs.Count : 0) + "|" + m.killTime + "|" + (int)m.ransomPhase + "|" + m.monkierPre + m.monkierPost + "|" + m.ransomSiteID + "|" + m.meetRestaurantID;
        }

        static System.Collections.Generic.List<(MurderController.Murder m, bool active)> AllMurders()
        {
            var res = new System.Collections.Generic.List<(MurderController.Murder, bool)>();
            var mc = MurderController.Instance;
            if (mc == null) return res;
            if (mc.activeMurders != null) foreach (var m in mc.activeMurders) if (m != null) res.Add((m, true));
            if (mc.inactiveMurders != null) foreach (var m in mc.inactiveMurders) if (m != null) res.Add((m, false));
            return res;
        }

        static void MurderHostTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < murderPollAt) return;
            murderPollAt = now + 2f;
            var ids = new System.Collections.Generic.List<int>();
            foreach (var x in AllMurders())
            {
                ids.Add(x.m.murderID);
                string sig = MurderSig(x.m, x.active), old;
                if (murderSentSig.TryGetValue(x.m.murderID, out old) && old == sig) continue;
                if (SendMurder(x.m, x.active, null)) murderSentSig[x.m.murderID] = sig;
            }
        }

        static System.Collections.Generic.List<Interactable> MurderItems(MurderController.Murder m)
        {
            var res = new System.Collections.Generic.List<Interactable>();
            var ids = new System.Collections.Generic.HashSet<int>();
            System.Action<Interactable> add = it => { try { if (it != null && it.inInventory == null && ids.Add(it.id)) res.Add(it); } catch (Exception __e) { Err.Swallow(__e); } };
            try { if (m.activeMurderItems != null) foreach (var kv in m.activeMurderItems) add(kv.Value); } catch (Exception __e) { Err.Swallow(__e); }
            try { add(m.weapon); add(m.ammo); add(m.callingCard); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (m.graffiti != null) foreach (var g in m.graffiti) add(g); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (m.graffitiIDs != null) foreach (var gid in m.graffitiIDs) { Interactable g; if (CityData.Instance.savableInteractableDictionary.TryGetValue(gid, out g)) add(g); } } catch (Exception __e) { Err.Swallow(__e); }
            return res;
        }

        static bool SendMurder(MurderController.Murder m, bool active, NetPeer to)
        {
            try
            {
                var data = new StateSaveData();
                data.murders = new Il2CppSystem.Collections.Generic.List<MurderController.Murder>();
                data.murders.Add(m);
                data.interactables = new Il2CppSystem.Collections.Generic.List<Interactable>();
                var items = MurderItems(m);
                foreach (var it in items) data.interactables.Add(it);
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
                var w = new NetDataWriter(); w.Put(MsgMurder); w.Put((byte)1); w.Put(active); w.Put(m.murderID); w.PutBytesWithLength(bytes);
                if (to != null) to.Send(w, DeliveryMethod.ReliableOrdered); else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[MURDER] убийство {m.murderID} ({m.state}, {(active ? "идёт" : "прошлое")}, убийца {m.murdererID}, жертва {m.victimID}) отправлено: предметов {items.Count}, {bytes.Length} байт");
                return true;
            }
            catch (Exception e) { if (!murderErrLogged) { murderErrLogged = true; Plugin.L.LogWarning("[MURDER] отправка: " + e); } return false; }
        }

        static void SendMurderSnapshot(NetPeer peer)
        {
            var ids = new System.Collections.Generic.List<int>();
            foreach (var x in AllMurders())
            {
                ids.Add(x.m.murderID);
                if (SendMurder(x.m, x.active, peer)) murderSentSig[x.m.murderID] = MurderSig(x.m, x.active);
            }
            var w = new NetDataWriter(); w.Put(MsgMurder); w.Put((byte)2); w.Put(ids.Count);
            foreach (var id in ids) w.Put(id);
            peer.Send(w, DeliveryMethod.ReliableOrdered);
            try
            {
                var mc = MurderController.Instance;
                var cm = mc != null ? mc.GetCurrentMurder() : null;
                if (mc != null && mc.currentActiveCase != null && cm != null && cm.location != null)
                {
                    byte kind; int lid;
                    if (MurderLocKey(cm.location, out kind, out lid))
                    {
                        var wc = new NetDataWriter(); wc.Put(MsgMurder); wc.Put((byte)3); wc.Put(kind); wc.Put(lid); wc.Put(true); wc.Put(MurderCaseName());
                        peer.Send(wc, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[MURDER] дело в снимке: " + e.Message); }
        }

        static void OnMurderMsg(NetDataReader r)
        {
            if (!WorldReady()) { worldMsgQueue.Add((true, null, r.GetRemainingBytes())); return; }
            byte op = r.GetByte();
            if (IsHost && op != 3) return;
            if (op == 1)
            {
                bool active = r.GetBool(); int id = r.GetInt(); var bytes = r.GetBytesWithLength();
                ImportMurder(id, active, bytes);
            }
            else if (op == 3)
            {
                byte kind = r.GetByte(); int locId = r.GetInt(); bool snap = r.GetBool(); string caseName = r.AvailableBytes > 0 ? r.GetString() : "";
                if (snap && MurderCaseOnBoard()) return;
                if (murderCaseSeen.Contains(CurrentMurderId()) && MurderCaseOnBoard()) { Plugin.L.LogInfo("[MURDER] новое дело от другого игрока - у меня оно уже есть"); return; }
                NewGameLocation loc = null;
                try
                {
                    if (kind == 1) { NewAddress ad; if (CityData.Instance.addressDictionary.TryGetValue(locId, out ad)) loc = ad; }
                    else if (kind == 2) { foreach (var st in CityData.Instance.streetDirectory) if (st != null && st.streetID == locId) { loc = st; break; } }
                }
                catch (Exception __e) { Err.Swallow(__e); }
                if (loc == null) { Plugin.L.LogWarning($"[MURDER] новое дело: место {kind}/{locId} у меня не найдено"); return; }
                applyingMurderCase = true;
                try { GameplayController.Instance.NewMurderCaseNotify(loc); Plugin.L.LogInfo($"[MURDER] новое дело об убийстве: {loc.name}"); }
                catch (Exception e) { Plugin.L.LogWarning("[MURDER] новое дело: " + e.Message); }
                try
                {
                    if (!MurderCaseOnBoard())
                    {
                        var mc1 = MurderController.Instance; var cp1 = CasePanelController.Instance;
                        Plugin.L.LogInfo($"[MURDER] дела на доске нет (у убийцы дело: {(mc1 != null && mc1.currentActiveCase != null ? mc1.currentActiveCase.name : "нет")}) - завожу сам");
                        var nc = cp1.CreateNewCase(Case.CaseType.murder, (Case.CaseStatus)0, true, string.IsNullOrEmpty(caseName) ? "Murder" : caseName);
                        if (nc != null) mc1.AssignActiveCase(nc);
                        Plugin.L.LogInfo($"[MURDER] дело заведено: {(nc != null ? nc.name : "не вышло")}, на доске: {MurderCaseOnBoard()}");
                    }
                }
                catch (Exception e) { Plugin.L.LogWarning("[MURDER] завести дело: " + e.Message); }
                finally { applyingMurderCase = false; }
            }
            else if (op == 2)
            {
                int n = r.GetInt(); var alive = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < n; i++) alive.Add(r.GetInt());
                var mc = MurderController.Instance; if (mc == null) return;
                int gone = 0;
                foreach (var lst in new[] { mc.activeMurders, mc.inactiveMurders })
                {
                    if (lst == null) continue;
                    for (int i = lst.Count - 1; i >= 0; i--) if (lst[i] != null && !alive.Contains(lst[i].murderID)) { lst.RemoveAt(i); gone++; }
                }
                Plugin.L.LogInfo($"[MURDER] убийств у хоста: {n}; убрано у меня лишних (из старого сейва): {gone}");
            }
        }

        static System.Reflection.PropertyInfo[] murderProps;

        static void CopyMurderFields(MurderController.Murder from, MurderController.Murder to)
        {
            if (murderProps == null)
            {
                var l = new System.Collections.Generic.List<System.Reflection.PropertyInfo>();
                foreach (var pr in typeof(MurderController.Murder).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (!pr.CanRead || !pr.CanWrite) continue;
                    var t = pr.PropertyType;
                    if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Vector3Int) || t == typeof(Il2CppSystem.Collections.Generic.List<int>)) l.Add(pr);
                }
                murderProps = l.ToArray();
            }
            foreach (var pr in murderProps) { try { pr.SetValue(to, pr.GetValue(from)); } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static void ImportMurder(int id, bool active, byte[] bytes)
        {
            var obj = JsonUtility.FromJson(System.Text.Encoding.UTF8.GetString(bytes), Il2CppInterop.Runtime.Il2CppType.Of<StateSaveData>());
            var data = obj != null ? obj.TryCast<StateSaveData>() : null;
            if (data == null || data.murders == null || data.murders.Count == 0) { Plugin.L.LogWarning($"[MURDER] убийство {id}: не разобрано"); return; }
            int newItems, updItems;
            ImportItems(data.interactables, "[MURDER]", "убийства " + id, out newItems, out updItems);
            var nm = data.murders[0];
            var mc = MurderController.Instance;
            if (mc == null || nm == null) return;
            if (mc.activeMurders == null) mc.activeMurders = new Il2CppSystem.Collections.Generic.List<MurderController.Murder>();
            if (mc.inactiveMurders == null) mc.inactiveMurders = new Il2CppSystem.Collections.Generic.List<MurderController.Murder>();
            MurderController.Murder cur = null; bool curActive = false;
            foreach (var x in AllMurders()) if (x.m.murderID == id) { cur = x.m; curActive = x.active; break; }
            bool isNew = cur == null;
            if (isNew) cur = nm;
            else CopyMurderFields(nm, cur);
            if (isNew || curActive != active)
            {
                mc.activeMurders.Remove(cur); mc.inactiveMurders.Remove(cur);
                (active ? mc.activeMurders : mc.inactiveMurders).Add(cur);
            }
            try { cur.LoadSerializedData(); } catch (Exception e) { Plugin.L.LogWarning($"[MURDER] убийство {id}: {e.Message}"); }
            try
            {
                if (active)
                {
                    if (cur.murderer != null) mc.currentMurderer = cur.murderer;
                    if (cur.victim != null) mc.currentVictim = cur.victim;
                    if (cur.preset != null) mc.murderPreset = cur.preset;
                    if (cur.mo != null) mc.chosenMO = cur.mo;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[MURDER] убийство {id} {(isNew ? "получено" : "обновлено")} ({cur.state}, {(active ? "идёт" : "прошлое")}, убийца {cur.murdererID}, жертва {cur.victimID}): новых предметов {newItems}, обновлено {updItems}");
        }
    }
}

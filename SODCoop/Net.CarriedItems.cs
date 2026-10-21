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
        static System.Collections.Generic.Dictionary<int, Interactable> idCache;
        static float idCacheAt;
        static InteractableController objTrackIc;
        static Interactable objTrackIt;
        static int objTrackId = -1;
        static float objSendTimer, objSettleUntil, objStillSince = -1f;
        static Interactable pendingDrop;
        static int objTrackHash;
        static float pendingDropUntil;
        static System.Collections.Generic.Dictionary<int, Interactable> idMap = new System.Collections.Generic.Dictionary<int, Interactable>();
        static System.Collections.Generic.Dictionary<string, InteractablePreset> presetCache;
        static System.Collections.Generic.Dictionary<int, int> gidOf = new System.Collections.Generic.Dictionary<int, int>();
        static int GidOf(Interactable it)
        {
            if (it == null) return -1;
            int g;
            if (gidOf.TryGetValue(it.id, out g)) return g;
            if (it.id >= RuntimeIdStart && (it.id & SideMask) == 0) return it.id | SideOf(MyPid);
            return it.id;
        }
        const int RuntimeIdStart = 10000000;
        const int SideMask = 7 << 28;
        static int SideOf(int pid) { return (pid + 1) << 28; }

        static string PresetNameOf(Interactable it)
        {
            try { return it != null && it.preset != null ? (it.preset.name ?? "") : ""; } catch (Exception) { return ""; }
        }

        static int NameHash(string s)
        {
            unchecked { int h = 17; foreach (char c in (s ?? "")) h = h * 31 + c; return h; }
        }

        static Interactable ResolveChecked(int id, string preset, int presetHash)
        {
            var it = ResolveId(id);
            if (it == null) return null;
            string pn = PresetNameOf(it);
            bool ok = preset != null ? pn == preset : NameHash(pn) == presetHash;
            if (!ok)
            {
                Plugin.L.LogWarning($"[ITEM] id={id}: у меня это '{pn}', а у отправителя '{preset ?? presetHash.ToString()}' (id не совпали) — игнорирую");
                return null;
            }
            return it;
        }

        static Interactable ResolveId(int id)
        {
            var r = ResolveIdRaw(id);
            if (r != null && rewardLocal.Contains(r.id)) { Plugin.Dbg($"[ITEM] {id}: это моя награда за задание - чужие действия с ней не применяю"); return null; }
            return r;
        }

        static readonly System.Collections.Generic.HashSet<int> rewardLocal = new System.Collections.Generic.HashSet<int>();

        internal static bool IsRewardObjective(string entry) { return entry != null && entry.StartsWith("Pick up your reward"); }

        internal static void NoteRewardItem(Objective.ObjectiveTrigger t)
        {
            if (t == null || t.interactableID == 0 || t.interactableID == -1) return;
            if (rewardLocal.Add(t.interactableID)) Plugin.L.LogInfo($"[JOB] награда за задание {t.jobID}: предмет {t.interactableID} - только мой");
        }

        internal static void OnRewarded(SideJob j, int idBefore)
        {
            int n = 0;
            for (int id = idBefore; id < Interactable.worldAssignID && id - idBefore < 50; id++)
            {
                var it = FindById(id);
                if (it == null) { try { CityData.Instance.savableInteractableDictionary.TryGetValue(id, out it); } catch (Exception __e) { Err.Swallow(__e); } }
                if (it != null && rewardLocal.Add(it.id)) n++;
            }
            Plugin.L.LogInfo($"[JOB] задание {(j != null ? j.jobID : 0)}: моя игра выдала награду, новых предметов {n} - они только мои");
        }

        static Interactable ResolveIdRaw(int id)
        {
            Interactable it;
            if (idMap.TryGetValue(id, out it) && it != null) return it;
            int side = id & SideMask;
            if (side != 0)
            {
                if (side != SideOf(MyPid)) return null;
                return FindById(id & ~SideMask);
            }
            return FindById(id);
        }

        static InteractablePreset FindPreset(string name)
        {
            if (presetCache == null)
            {
                presetCache = new System.Collections.Generic.Dictionary<string, InteractablePreset>();
                try
                {
                    foreach (var p in Resources.FindObjectsOfTypeAll<InteractablePreset>())
                        if (p != null && !string.IsNullOrEmpty(p.name) && !presetCache.ContainsKey(p.name)) presetCache[p.name] = p;
                    Plugin.Dbg($"[ITEM] пресетов предметов: {presetCache.Count}");
                }
                catch (Exception e) { Plugin.L.LogWarning($"[ITEM] пресеты: {e.Message}"); }
            }
            InteractablePreset r;
            return presetCache.TryGetValue(name ?? "", out r) ? r : null;
        }

        static void SendItemEvent(byte kind, Interactable it, bool withPose)
        {
            try
            {
                if (profileApplying || profRestoring || manager == null || manager.ConnectedPeersCount == 0 || it == null) return;
                var w = new NetDataWriter();
                w.Put(MsgItemEv);
                w.Put(kind);
                w.Put(GidOf(it));
                w.Put(PresetNameOf(it));
                sharedLocal.Add(it.id);
                if (withPose)
                {
                    Vector3 p = it.wPos; Vector3 e = it.wEuler;
                    try { if (it.controller != null) { var tr = RootOf(it.controller); p = tr.position; e = tr.eulerAngles; } } catch (Exception __e) { Err.Swallow(__e); }
                    w.Put(p.x); w.Put(p.y); w.Put(p.z);
                    w.Put(e.x); w.Put(e.y); w.Put(e.z);
                    WriteState(w, it);
                }
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception ex) { Plugin.L.LogWarning($"[ITEM] отправка: {ex.Message}"); }
        }

        static System.Collections.Generic.HashSet<int> sharedLocal = new System.Collections.Generic.HashSet<int>();
        static bool applyingRemote;

        class PassedVal { public int type; public float value; public string str; }
        class ItemState { public float val, cs; public System.Collections.Generic.List<PassedVal> pv = new System.Collections.Generic.List<PassedVal>(); }

        static void WriteState(NetDataWriter w, Interactable it)
        {
            float v = 0f, c = 0f; int n = 0;
            try { v = it.val; c = it.cs; } catch (Exception __e) { Err.Swallow(__e); }
            var list = new System.Collections.Generic.List<PassedVal>();
            try
            {
                var pv = it.pv;
                if (pv != null)
                    for (int i = 0; i < pv.Count && i < 30; i++)
                    {
                        var x = pv[i];
                        if (x == null) continue;
                        list.Add(new PassedVal { type = (int)x.varType, value = x.value, str = x.str ?? "" });
                    }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            n = list.Count;
            w.Put(v); w.Put(c); w.Put((byte)n);
            foreach (var x in list) { w.Put(x.type); w.Put(x.value); w.Put(x.str); }
        }

        static ItemState ReadState(NetDataReader r)
        {
            var st = new ItemState();
            st.val = r.GetFloat(); st.cs = r.GetFloat();
            int n = r.GetByte();
            for (int i = 0; i < n; i++) st.pv.Add(new PassedVal { type = r.GetInt(), value = r.GetFloat(), str = r.GetString() });
            return st;
        }

        static void ApplyState(Interactable it, ItemState st)
        {
            if (it == null || st == null) return;
            try { it.val = st.val; it.cs = st.cs; } catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                var pv = it.pv;
                if (pv == null)
                {
                    pv = new Il2CppSystem.Collections.Generic.List<Interactable.Passed>();
                    it.pv = pv;
                }
                foreach (var x in st.pv)
                {
                    bool found = false;
                    for (int i = 0; i < pv.Count; i++)
                    {
                        var e = pv[i];
                        if (e != null && (int)e.varType == x.type) { e.value = x.value; if (!string.IsNullOrEmpty(x.str)) e.str = x.str; found = true; break; }
                    }
                    if (!found) pv.Add(new Interactable.Passed((Interactable.PassedVarType)x.type, x.value, string.IsNullOrEmpty(x.str) ? null : x.str));
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[ITEM] состояние: {e.Message}"); }
        }

        internal static void OnItemDeleting(Interactable it)
        {
            try
            {
                if (applyingRemote || profileApplying || profRestoring || it == null || !Plugin.CfgObjSync.Value) return;
                if (!sharedLocal.Remove(it.id)) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgItemEv); w.Put((byte)4); w.Put(GidOf(it)); w.Put(PresetNameOf(it));
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.Dbg($"[ITEM] предмет id={it.id} (общий id={GidOf(it)}) удалён, сообщаю второму игроку");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }
    }
}

using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        const byte MsgWatch = 42;
        static bool illSentArrest;
        static float watchSendAt, watchCleanAt, hostWatchAt = -10f;
        static readonly HashSet<int> hostWatch = new HashSet<int>();
        static int watchCleaned, watchLogged;

        static bool OnlyHouseArrestTrespass(Player p)
        {
            var mc = ModifiersController.Instance;
            if (mc == null || !mc.houseArrestModifierEnabled) return false;
            bool normal = true;
            mc.houseArrestModifierEnabled = false;
            try { int e; normal = p.IsTrespassing(p.currentRoom, out e, true); }
            catch (Exception) { normal = true; }
            finally { mc.houseArrestModifierEnabled = true; }
            return !normal;
        }

        static RP ArrestOnlyPuppetRp(Actor target)
        {
            if (!IsHost || target == null || players.Count == 0) return null;
            IntPtr ptr = target.Pointer;
            for (int i = 0; i < players.Count; i++)
            {
                var rp = players[i];
                var c = PuppetOf(rp);
                if ((object)c == null || c.Pointer != ptr) continue;
                return rp.illArrestOnly && !rp.illAction ? rp : null;
            }
            return null;
        }

        internal static bool OnlyPoliceReacts(Actor observer, Actor target)
        {
            if (observer == null || target == null) return false;
            try
            {
                if (observer.isEnforcer) return false;
                return ArrestOnlyPuppetRp(target) != null;
            }
            catch (Exception) { return false; }
        }

        static void WatchTick()
        {
            float now = Time.realtimeSinceStartup;
            if (manager == null || manager.ConnectedPeersCount == 0) return;
            if (IsHost) { if (now >= watchSendAt) { watchSendAt = now + 1f; HostSendWatchers(); } return; }
            if (now >= watchCleanAt) { watchCleanAt = now + 0.5f; ClientCleanWatchers(now); }
        }

        static void HostSendWatchers()
        {
            var list = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            if (list == null) return;
            foreach (var rp in players)
            {
                if (rp.peer == null) continue;
                var c = PuppetOf(rp);
                var ids = new List<int>();
                if ((object)c != null && c != null)
                {
                    IntPtr ptr = c.Pointer;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var h = list[i]; if (h == null || h.Pointer == ptr) continue;
                        bool w = false;
                        try { w = h.seesIllegal != null && h.seesIllegal.ContainsKey(c); } catch (Exception __e) { Err.Swallow(__e); }
                        if (!w) { try { var ai = h.ai; w = ai != null && ((ai.persuitTarget != null && ai.persuitTarget.Pointer == ptr) || (ai.attackTarget != null && ai.attackTarget.Pointer == ptr)); } catch (Exception __e) { Err.Swallow(__e); } }
                        if (w) ids.Add(h.humanID);
                    }
                }
                var wr = new NetDataWriter(); wr.Put(MsgWatch); wr.Put(ids.Count); foreach (var id in ids) wr.Put(id);
                try { rp.peer.Send(wr, DeliveryMethod.Sequenced); } catch (Exception __e) { Err.Swallow(__e); }
            }
        }

        static void OnWatchMsg(NetDataReader r)
        {
            int n = r.GetInt();
            hostWatch.Clear();
            for (int i = 0; i < n; i++) hostWatch.Add(r.GetInt());
            hostWatchAt = Time.realtimeSinceStartup;
        }

        static void ClientCleanWatchers(float now)
        {
            if (now - hostWatchAt > 3f) return;
            var p = Player.Instance;
            var list = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            if (p == null || list == null) return;
            IntPtr pp = p.Pointer;
            int removed = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i]; if (h == null) continue;
                if (hostWatch.Contains(h.humanID)) continue;
                try
                {
                    if (h.seesIllegal != null && h.seesIllegal.ContainsKey(p))
                    {
                        try { h.RemoveFromSeesIllegal(p, 1f); } catch (Exception __e) { Err.Swallow(__e); }
                        if (h.seesIllegal.ContainsKey(p)) h.seesIllegal.Remove(p);
                        removed++;
                    }
                }
                catch (Exception __e) { Err.Swallow(__e); }
                try
                {
                    var ai = h.ai;
                    if (ai != null && ai.persuitTarget != null && ai.persuitTarget.Pointer == pp) { ai.CancelPersue(); removed++; }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            try
            {
                if (p.persuedBy != null && p.persuedBy.Count > 0)
                {
                    var drop = new List<Actor>();
                    foreach (var a in p.persuedBy) { var h = a != null ? a.TryCast<Human>() : null; if (a == null || h == null || !hostWatch.Contains(h.humanID)) drop.Add(a); }
                    foreach (var a in drop) { p.persuedBy.Remove(a); removed++; }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                if (p.witnessesToIllegalActivity != null && p.witnessesToIllegalActivity.Count > 0)
                {
                    var drop = new List<Actor>();
                    foreach (var a in p.witnessesToIllegalActivity) { var h = a != null ? a.TryCast<Human>() : null; if (a == null || h == null || !hostWatch.Contains(h.humanID)) drop.Add(a); }
                    foreach (var a in drop) { p.witnessesToIllegalActivity.Remove(a); removed++; }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            if (removed > 0)
            {
                watchCleaned += removed;
                try { p.UpdateIllegalStatus(); } catch (Exception __e) { Err.Swallow(__e); }
                if (watchLogged < 20) { watchLogged++; Plugin.L.LogInfo($"[WANTED] у хоста меня видят/гонятся {hostWatch.Count} жителей - убрал лишних свидетелей и погонь: {removed}"); }
            }
        }
    }
}

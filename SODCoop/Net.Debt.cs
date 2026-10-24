using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        const byte MsgDebt = 44;
        const int ConvoDebt = (int)InteractionController.ConversationType.loanSharkVisit;
        static readonly Dictionary<int, List<int>> lateDebts = new Dictionary<int, List<int>>();
        static readonly Dictionary<long, float> debtNextAt = new Dictionary<long, float>();
        static float debtSendAt, debtResendAt, debtHostAt;
        static string debtSent = "";

        static void DebtTick()
        {
            if (manager == null || manager.ConnectedPeersCount == 0) { if (lateDebts.Count > 0) lateDebts.Clear(); debtSent = ""; return; }
            float now = Time.realtimeSinceStartup;
            if (IsHost) { if (now >= debtHostAt) { debtHostAt = now + 3f; HostSendCollectors(now); } return; }
            if (now < debtSendAt) return;
            debtSendAt = now + 5f;
            var late = new List<int>();
            var gc = GameplayController.Instance; var sd = SessionData.Instance;
            if (!GhostMe && gc != null && gc.debt != null && sd != null)
                foreach (var d in gc.debt) if (d != null && sd.gameTime > d.nextPaymentDueBy && !late.Contains(d.companyID)) late.Add(d.companyID);
            string key = string.Join(",", late);
            if (key == debtSent && now < debtResendAt) return;
            if (key != debtSent) Plugin.L.LogInfo(late.Count > 0 ? $"[DEBT] платёж просрочен (компании {key}) - сообщаю хосту" : "[DEBT] просроченных платежей нет");
            debtSent = key; debtResendAt = now + 30f;
            var w = new NetDataWriter(); w.Put(MsgDebt); w.Put(late.Count); foreach (var c in late) w.Put(c);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void OnDebtMsg(RP rp, NetDataReader r)
        {
            if (!IsHost || rp == null) return;
            int n = r.GetInt();
            var list = new List<int>();
            for (int i = 0; i < n; i++) list.Add(r.GetInt());
            List<int> old;
            bool had = lateDebts.TryGetValue(rp.pid, out old) && old.Count > 0;
            if (list.Count > 0 && !had) Plugin.L.LogInfo($"[DEBT] игрок {rp.pid} просрочил платёж ростовщику - пошлю коллектора");
            if (list.Count == 0 && had) Plugin.L.LogInfo($"[DEBT] у игрока {rp.pid} больше нет просроченных платежей");
            lateDebts[rp.pid] = list;
        }

        static bool CollectorBusy(int pid)
        {
            foreach (var f in fameWalks) if (f.pid == pid && f.convo == ConvoDebt) return true;
            foreach (var kv in talkPending) { int cv; if (kv.Value == pid && talkConvo.TryGetValue(kv.Key, out cv) && cv == ConvoDebt) return true; }
            return false;
        }

        static void HostSendCollectors(float now)
        {
            var dir = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            var rc = RoutineControls.Instance;
            if (dir == null || rc == null || rc.fameAndFortune == null) return;
            foreach (var rp in players)
            {
                List<int> late;
                if (rp.peer == null || !lateDebts.TryGetValue(rp.pid, out late) || late.Count == 0) continue;
                if (IsGhostRp(rp) || rp.downed || CollectorBusy(rp.pid)) continue;
                var c = PuppetOf(rp);
                if ((object)c == null || c == null || c.interactable == null) continue;
                foreach (var cid in late)
                {
                    long key = ((long)cid << 8) | (uint)rp.pid;
                    float at;
                    if (debtNextAt.TryGetValue(key, out at) && now < at) continue;
                    debtNextAt[key] = now + 60f;
                    Human best = null; float bd = float.MaxValue;
                    for (int i = 0; i < dir.Count; i++)
                    {
                        var h = dir[i];
                        try
                        {
                            if (h == null || h.isDead || h.isStunned || h.isAsleep || h.inConversation || h.ai == null || h.ai.currentGoal == null) continue;
                            if (h.job == null || h.job.employer == null || h.job.employer.companyID != cid) continue;
                            float d = Vector3.Distance(h.transform.position, c.transform.position);
                            if (d < bd) { bd = d; best = h; }
                        }
                        catch (Exception __e) { Err.Swallow(__e); }
                    }
                    if (best == null) { Plugin.L.LogInfo($"[DEBT] для игрока {rp.pid}: у компании {cid} нет свободных сотрудников, попробую позже"); continue; }
                    bool ok = false;
                    try { ok = best.ai.currentGoal.TryInsertInteractableAction(c.interactable, FamePresetForPuppet(rc.fameAndFortune), 9, c.currentNode, true); } catch (Exception e) { Plugin.L.LogWarning("[DEBT] " + e.Message); }
                    if (!ok) { Plugin.L.LogInfo($"[DEBT] коллектор {best.humanID} не смог пойти к игроку {rp.pid}"); continue; }
                    debtNextAt[key] = now + 240f;
                    fameWalks.Add(new FameWalk { h = best, pid = rp.pid, until = now + 240f, convo = ConvoDebt });
                    Plugin.L.LogInfo($"[DEBT] коллектор {best.humanID} ({best.citizenName}) идёт к игроку {rp.pid} ({PlayerLabel(rp.pid)}) за долгом, до него {bd:F0} м");
                    break;
                }
            }
        }
    }
}

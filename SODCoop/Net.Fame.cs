using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        const byte MsgTalk = 43;
        static readonly Dictionary<long, float> fameRolled = new Dictionary<long, float>();
        static readonly Dictionary<int, int> talkPending = new Dictionary<int, int>();
        static readonly Dictionary<int, int> talkConvo = new Dictionary<int, int>();
        static bool debtUnpaidOnReturn;
        static bool applyingTalk;
        static AIActionPreset fameToPuppet;
        static float fameTickAt;
        sealed class FameWalk { public Human h; public int pid; public float until; public int convo = (int)InteractionController.ConversationType.fameAndFortune; }
        static readonly List<FameWalk> fameWalks = new List<FameWalk>();

        static AIActionPreset FamePresetForPuppet(AIActionPreset orig)
        {
            if (fameToPuppet != null) return fameToPuppet;
            var p = UnityEngine.Object.Instantiate(orig);
            p.name = orig.name + "_SodCoopPuppet";
            Plugin.L.LogInfo($"[FAME] действие фаната '{orig.name}': место {orig.actionLocation} - для двойника ставлю {AIActionPreset.ActionLocation.interactable}");
            p.actionLocation = AIActionPreset.ActionLocation.interactable;
            fameToPuppet = p;
            return p;
        }

        static void FameTick()
        {
            if (!IsHost || fameWalks.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            if (now < fameTickAt) return;
            fameTickAt = now + 0.5f;
            for (int i = fameWalks.Count - 1; i >= 0; i--)
            {
                var f = fameWalks[i];
                var rp = RpByPid(f.pid);
                var c = rp != null ? PuppetOf(rp) : null;
                if (f.h == null || rp == null || (object)c == null || c == null || now > f.until || f.h.isDead || f.h.isStunned)
                {
                    if (now > f.until) Plugin.L.LogInfo($"[FAME] горожанин {(f.h != null ? f.h.humanID : -1)} так и не дошёл до игрока {f.pid}");
                    fameWalks.RemoveAt(i); continue;
                }
                float d = Vector3.Distance(f.h.transform.position, c.transform.position);
                if (d > 2.5f) continue;
                fameWalks.RemoveAt(i);
                try { var act = f.h.ai.currentAction; if (act != null && act.preset == fameToPuppet) act.Remove(0f); } catch (Exception __e) { Err.Swallow(__e); }
                SendTalk(f.h, rp, f.convo, "дошёл до игрока");
            }
        }

        static void SendTalk(Human h, RP rp, int convoType, string why)
        {
            talkPending[h.humanID] = rp.pid;
            talkConvo[h.humanID] = convoType;
            for (int i = fameWalks.Count - 1; i >= 0; i--) if (fameWalks[i].h == h) fameWalks.RemoveAt(i);
            var w = new NetDataWriter(); w.Put(MsgTalk); w.Put((byte)1); w.Put(h.humanID); w.Put(convoType);
            try { rp.peer.Send(w, DeliveryMethod.ReliableOrdered); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[FAME] горожанин {h.humanID} ({h.citizenName}) заговорил с игроком {rp.pid} (разговор {convoType}, {why}) - открываю разговор у него");
        }

        static RP PuppetRpOfActor(Actor a)
        {
            if (a == null) return null;
            IntPtr ptr = a.Pointer;
            for (int i = 0; i < players.Count; i++)
            {
                var c = PuppetOf(players[i]);
                if ((object)c != null && c.Pointer == ptr) return players[i];
            }
            return null;
        }

        static RP PuppetRpOfInteractable(Interactable it)
        {
            if (it == null) return null;
            IntPtr ptr = it.Pointer;
            for (int i = 0; i < players.Count; i++)
            {
                var c = PuppetOf(players[i]);
                if ((object)c == null || c == null) continue;
                try { if (c.interactable != null && c.interactable.Pointer == ptr) return players[i]; } catch (Exception __e) { Err.Swallow(__e); }
            }
            return null;
        }

        internal static void OnNpcNewTrackTarget(NewAIController ai)
        {
            if (!IsHost || ai == null || manager == null || manager.ConnectedPeersCount == 0 || players.Count == 0) return;
            var mc = ModifiersController.Instance;
            if (mc == null || !mc.fameAndFortuneModifierEnabled) return;
            var h = ai.human;
            if (h == null || h.isDead || h.isStunned || h.inConversation || ai.currentGoal == null) return;
            var tt = ai.trackedTargets;
            if (tt == null) return;
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < tt.Count; i++)
            {
                var t = tt[i]; if (t == null) continue;
                var rp = PuppetRpOfActor(t.actor);
                if (rp == null || IsGhostRp(rp) || rp.illTres || rp.downed) continue;
                long key = ((long)h.humanID << 8) | (uint)rp.pid;
                float at;
                if (fameRolled.TryGetValue(key, out at) && now - at < 600f) continue;
                fameRolled[key] = now;
                if (UnityEngine.Random.value > mc.fameAndFortuneChance) continue;
                var c = PuppetOf(rp);
                var rc = RoutineControls.Instance;
                if ((object)c == null || c == null || c.interactable == null || rc == null || rc.fameAndFortune == null) continue;
                bool ok = false;
                try { ok = ai.currentGoal.TryInsertInteractableAction(c.interactable, FamePresetForPuppet(rc.fameAndFortune), 9, c.currentNode, true); } catch (Exception e) { Plugin.L.LogWarning("[FAME] " + e.Message); }
                if (ok)
                {
                    fameWalks.Add(new FameWalk { h = h, pid = rp.pid, until = now + 90f });
                    Plugin.L.LogInfo($"[FAME] горожанин {h.humanID} ({h.citizenName}) узнал игрока {rp.pid} ({PlayerLabel(rp.pid)}) и идёт к нему");
                }
                else Plugin.L.LogInfo($"[FAME] горожанин {h.humanID} узнал игрока {rp.pid}, но действие не встало в очередь");
                return;
            }
        }

        internal static bool OnNpcTalkTo(NewAIController ai, int convoType)
        {
            if (applyingTalk || !IsHost || ai == null || manager == null || manager.ConnectedPeersCount == 0) return true;
            RP rp = null;
            try { var act = ai.currentAction; if (act != null) rp = PuppetRpOfInteractable(act.passedInteractable); } catch (Exception __e) { Err.Swallow(__e); }
            var h = ai.human;
            if (h == null) return true;
            foreach (var f in fameWalks) if (f.h == h) { if (rp == null) rp = RpByPid(f.pid); convoType = f.convo; break; }
            if (rp == null || rp.peer == null) return true;
            SendTalk(h, rp, convoType, "игра");
            return false;
        }

        internal static void OnClientTalkReturnPre(NewAIController ai)
        {
            debtUnpaidOnReturn = false;
            if (IsHost || ai == null || ai.human == null) return;
            try { var ic = InteractionController.Instance; debtUnpaidOnReturn = ic != null && ic.debtCollector != null && ic.debtCollector.Pointer == ai.human.Pointer; } catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static void OnClientTalkReturn(NewAIController ai)
        {
            if (IsHost || applyingTalk || ai == null || manager == null || manager.ConnectedPeersCount == 0) return;
            var h = ai.human; if (h == null) return;
            var w = new NetDataWriter(); w.Put(MsgTalk); w.Put((byte)2); w.Put(h.humanID); w.Put(debtUnpaidOnReturn ? 1 : 0);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static int clientTalkHid = -1;
        static float clientTalkSentAt;
        sealed class TalkHold { public int pid; public float until; }
        static readonly Dictionary<int, TalkHold> talkHolds = new Dictionary<int, TalkHold>();

        static void ClientTalkTick()
        {
            if (IsHost || manager == null || manager.ConnectedPeersCount == 0) { clientTalkHid = -1; return; }
            int hid = -1;
            try
            {
                var ic = InteractionController.Instance;
                var it = ic != null && ic.dialogMode ? ic.talkingTo : null;
                var h = it != null && it.isActor != null ? it.isActor.TryCast<Human>() : null;
                if (h != null && !h.isPlayer && !IsPuppetId(h.humanID)) hid = h.humanID;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            float now = Time.unscaledTime;
            if (hid == clientTalkHid && (hid < 0 || now - clientTalkSentAt < 10f)) return;
            if (clientTalkHid >= 0 && hid != clientTalkHid) SendTalkHold(clientTalkHid, false);
            if (hid >= 0) SendTalkHold(hid, true);
            if (hid != clientTalkHid) Plugin.L.LogInfo(hid >= 0 ? $"[TALK] говорю с горожанином {hid} - прошу хоста остановить его" : $"[TALK] разговор с горожанином {clientTalkHid} закончен");
            clientTalkHid = hid; clientTalkSentAt = now;
        }

        static void SendTalkHold(int hid, bool on)
        {
            var w = new NetDataWriter(); w.Put(MsgTalk); w.Put((byte)3); w.Put(hid); w.Put(on ? 1 : 0);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void HostTalkHold(RP rp, Human h, bool on)
        {
            int hid = h.humanID;
            var c = PuppetOf(rp);
            if (on)
            {
                bool was = talkHolds.ContainsKey(hid);
                talkHolds[hid] = new TalkHold { pid = rp.pid, until = Time.unscaledTime + 25f };
                try { if ((object)c != null && c != null && c.interactable != null) h.SetInteracting(c.interactable); } catch (Exception e) { Plugin.L.LogWarning("[TALK] остановить горожанина: " + e.Message); }
                try { if ((object)c != null && c != null) h.ai.SetFacingPosition(c.transform.position); } catch (Exception __e) { Err.Swallow(__e); }
                if (!was) Plugin.L.LogInfo($"[TALK] игрок {rp.pid} говорит с горожанином {hid} ({h.citizenName}) - держу его на месте");
            }
            else
            {
                TalkHold th;
                if (!talkHolds.TryGetValue(hid, out th) || th.pid != rp.pid) return;
                ReleaseTalkHold(hid, h, "разговор закончен");
            }
        }

        static void ReleaseTalkHold(int hid, Human h, string why)
        {
            talkHolds.Remove(hid);
            try { if (h != null) h.SetInteracting(null); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[TALK] горожанин {hid} отпущен: {why}");
        }

        static void FaceLocalPlayer(Transform tr, float dt)
        {
            var p = Player.Instance; if (p == null) return;
            Vector3 d = p.transform.position - tr.position; d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return;
            var want = Quaternion.LookRotation(d.normalized, Vector3.up);
            tr.rotation = Quaternion.RotateTowards(tr.rotation, want, 360f * dt);
        }

        static float talkFaceAt;

        static void HostTalkHoldTick()
        {
            if (talkHolds.Count == 0) return;
            float now = Time.unscaledTime;
            if (now >= talkFaceAt)
            {
                talkFaceAt = now + 0.5f;
                foreach (var kv in talkHolds)
                {
                    try
                    {
                        var rp = RpByPid(kv.Value.pid); var c = rp != null ? PuppetOf(rp) : null;
                        Human h = null; CityData.Instance.GetHuman(kv.Key, out h, false);
                        if (h == null || (object)c == null || c == null) continue;
                        h.ai.SetFacingPosition(c.transform.position);
                        Vector3 d = c.transform.position - h.transform.position; d.y = 0f;
                        if (d.sqrMagnitude > 0.0001f) h.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
            }
            List<int> done = null;
            foreach (var kv in talkHolds)
            {
                bool gone = RpByPid(kv.Value.pid) == null;
                if (gone || now > kv.Value.until) (done ??= new List<int>()).Add(kv.Key);
            }
            if (done == null) return;
            foreach (int hid in done)
            {
                Human h = null;
                try { CityData.Instance.GetHuman(hid, out h, false); } catch (Exception __e) { Err.Swallow(__e); }
                ReleaseTalkHold(hid, h, "игрок ушёл или не отвечает");
            }
        }

        static void OnTalkMsg(RP rp, NetDataReader r)
        {
            byte op = r.GetByte(); int hid = r.GetInt(); int convo = r.GetInt();
            Human h = null;
            try { CityData.Instance.GetHuman(hid, out h, false); } catch (Exception __e) { Err.Swallow(__e); }
            if (h == null || h.ai == null) { Plugin.L.LogWarning($"[FAME] горожанин {hid} не найден"); return; }
            if (op == 3)
            {
                if (IsHost && rp != null) HostTalkHold(rp, h, convo != 0);
                return;
            }
            if (op == 1 && !IsHost)
            {
                applyingTalk = true;
                try
                {
                    var p = Player.Instance;
                    if (convo == ConvoDebt && p != null && ActionController.Instance != null) ActionController.Instance.DebtCollection(p.interactable, p.currentNode, h);
                    else h.ai.TalkTo((InteractionController.ConversationType)convo);
                    Plugin.L.LogInfo($"[FAME] со мной заговорил горожанин {hid} ({h.citizenName}), разговор {convo}");
                }
                catch (Exception e) { Plugin.L.LogWarning("[FAME] разговор: " + e.Message); }
                finally { applyingTalk = false; }
            }
            else if (op == 2 && IsHost && rp != null)
            {
                int pid;
                if (!talkPending.TryGetValue(hid, out pid) || pid != rp.pid) return;
                talkPending.Remove(hid);
                int was; talkConvo.TryGetValue(hid, out was); talkConvo.Remove(hid);
                var mc = ModifiersController.Instance;
                var c = PuppetOf(rp);
                if (was == ConvoDebt)
                {
                    if ((object)c == null || c == null) return;
                    if (convo != 0)
                    {
                        try { h.ai.SetPersue(c, false, 2, true, 1f); Plugin.L.LogInfo($"[DEBT] игрок {rp.pid} не заплатил - коллектор {hid} ({h.citizenName}) нападает"); }
                        catch (Exception e) { Plugin.L.LogWarning("[DEBT] нападение: " + e.Message); }
                    }
                    else Plugin.L.LogInfo($"[DEBT] игрок {rp.pid} заплатил коллектору {hid}");
                    return;
                }
                if (mc == null || (object)c == null || c == null) return;
                if (UnityEngine.Random.value <= mc.fameAndFortuneCombatChance)
                {
                    try { h.ai.SetPersue(c, false, 2, true, 1f); Plugin.L.LogInfo($"[FAME] горожанин {hid} ({h.citizenName}) после разговора нападает на игрока {rp.pid}"); }
                    catch (Exception e) { Plugin.L.LogWarning("[FAME] нападение: " + e.Message); }
                }
                else Plugin.L.LogInfo($"[FAME] разговор горожанина {hid} с игроком {rp.pid} закончился мирно");
            }
        }
    }
}

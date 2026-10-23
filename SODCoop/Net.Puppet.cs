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
        static Citizen puppet;
        static int puppetId = -1;
        static float puppetAt, puppetAiAt, puppetLocAt, puppetIdAt, puppetHideAt;
        static bool puppetErrLogged, puppetCrouch;
        static Renderer[] puppetRends;
        static Collider[] puppetCols;
        static int puppetHideId = -1;
        static float puppetClientHideAt;
        static readonly System.Collections.Generic.Dictionary<int, float> puppetSeenLog = new System.Collections.Generic.Dictionary<int, float>();

        static Citizen PuppetOf(RP rp) { return rp == curRp ? puppet : rp.puppet; }
        static int PuppetIdOf(RP rp) { return rp == curRp ? puppetId : rp.puppetId; }

        static bool IsPuppetCit(Citizen c)
        {
            if (c == null) return false;
            for (int i = 0; i < players.Count; i++)
            {
                if (IsHost) { if (PuppetOf(players[i]) == c) return true; }
                else if (PuppetIdOf(players[i]) >= 0 && PuppetIdOf(players[i]) == c.humanID) return true;
            }
            return false;
        }

        static bool IsPuppetId(int humanId)
        {
            for (int i = 0; i < players.Count; i++) { int id = PuppetIdOf(players[i]); if (id >= 0 && id == humanId) return true; }
            return false;
        }

        static bool IsPuppetActor(Actor a)
        {
            var c = AsCitizen(a);
            return c != null && IsPuppetCit(c);
        }

        static Citizen AsCitizen(Il2CppSystem.Object a)
        {
            if (a == null) return null;
            var c = a as Citizen;
            if (c != null) return c;
            try { return a.TryCast<Citizen>(); } catch (Exception) { return null; }
        }

        static RP OwnerOfPuppet(Citizen c)
        {
            if (c == null) return null;
            for (int i = 0; i < players.Count; i++)
            {
                if (IsHost) { if (PuppetOf(players[i]) == c) return players[i]; }
                else if (PuppetIdOf(players[i]) >= 0 && PuppetIdOf(players[i]) == c.humanID) return players[i];
            }
            return null;
        }

        static Citizen PickPuppet()
        {
            int want = Plugin.CfgPuppetId.Value;
            Citizen best = null; int bestScore = int.MinValue;
            if (want >= 0)
            {
                foreach (var c in npcById.Values) if (c != null && c.humanID == want && !IsPuppetCit(c)) return c;
            }
            foreach (var c in npcById.Values)
            {
                if (c == null) continue;
                try
                {
                    if (IsPuppetCit(c)) continue;
                    if (c.removedFromWorld || c.isDead || c.isEnforcer || c.isPlayer || c.isAsleep) continue;
                    int sc = 0;
                    if (c.isHomeless) sc += 100;
                    if (c.job == null) sc += 10;
                    if (c.partner == null) sc += 5;
                    if (c.home == null) sc += 20;
                    sc -= c.humanID / 1000;
                    if (sc > bestScore) { bestScore = sc; best = c; }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            return best;
        }

        static void CachePuppetParts(Citizen c)
        {
            puppetRends = null; puppetCols = null;
            try
            {
                var rs = c.gameObject.GetComponentsInChildren<Renderer>(true);
                var l = new System.Collections.Generic.List<Renderer>();
                for (int i = 0; i < rs.Length; i++) if (rs[i] != null) l.Add(rs[i]);
                puppetRends = l.ToArray();
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static readonly System.Collections.Generic.HashSet<int> activePuppets = new System.Collections.Generic.HashSet<int>();
        static int puppetVisFixes;

        internal static void OnHumanSetVisible(Human h, bool vis)
        {
            if (!vis || h == null || activePuppets.Count == 0 || !activePuppets.Contains(h.humanID)) return;
            int off = 0;
            var rs = h.gameObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                try { var r = rs[i]; if (r != null && r.enabled) { r.enabled = false; NoteHidden(h.humanID, r); off++; } } catch (Exception __e) { Err.Swallow(__e); }
            }
            if (off > 0 && puppetVisFixes++ % 50 == 0) Plugin.Dbg($"[PUPPET] двойник {h.humanID}: SetVisible включил {off} рендереров - сразу спрятал (раз {puppetVisFixes})");
        }

        static float puppetHideLog;
        static void HidePuppet()
        {
            var c = puppet; if (c == null) return;
            int on = 0;
            try
            {
                var rs = c.gameObject.GetComponentsInChildren<Renderer>(true);
                puppetRends = rs;
                for (int i = 0; i < rs.Length; i++)
                {
                    try { var r = rs[i]; if (r != null && r.enabled) { r.enabled = false; NoteHidden(c.humanID, r); on++; } } catch (Exception __e) { Err.Swallow(__e); }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            if (on > 0 && Plugin.CfgPuppetLog.Value && Time.unscaledTime - puppetHideLog > 3f)
            {
                puppetHideLog = Time.unscaledTime;
                Plugin.L.LogInfo($"[PUPPET] игра снова включила {on} рендереров двойника, выключил (сидит клиент={remoteSitting})");
            }
        }

        static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Renderer>> puppetHiddenRends = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Renderer>>();
        static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Collider>> puppetHiddenCols = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Collider>>();

        static void NoteHidden(int id, Renderer r)
        {
            System.Collections.Generic.List<Renderer> l;
            if (!puppetHiddenRends.TryGetValue(id, out l)) { l = new System.Collections.Generic.List<Renderer>(); puppetHiddenRends[id] = l; }
            foreach (var x in l) if (x != null && x.Pointer == r.Pointer) return;
            l.Add(r);
        }

        static void NoteHidden(int id, Collider c)
        {
            System.Collections.Generic.List<Collider> l;
            if (!puppetHiddenCols.TryGetValue(id, out l)) { l = new System.Collections.Generic.List<Collider>(); puppetHiddenCols[id] = l; }
            foreach (var x in l) if (x != null && x.Pointer == c.Pointer) return;
            l.Add(c);
        }

        static void UnhidePuppet(int id)
        {
            System.Collections.Generic.List<Renderer> rs; System.Collections.Generic.List<Collider> cs;
            int n = 0;
            if (puppetHiddenRends.TryGetValue(id, out rs)) { foreach (var r in rs) { try { if (r != null) { r.enabled = true; n++; } } catch (Exception __e) { Err.Swallow(__e); } } puppetHiddenRends.Remove(id); }
            if (puppetHiddenCols.TryGetValue(id, out cs)) { foreach (var c in cs) { try { if (c != null) c.enabled = true; } catch (Exception __e) { Err.Swallow(__e); } } puppetHiddenCols.Remove(id); }
            if (n > 0) Plugin.L.LogInfo($"[PUPPET] житель {id} снова видим (частей: {n})");
        }

        static NewNode PuppetReleaseSpot(Citizen c)
        {
            try { var h = c.home; if (h != null) { var n = c.FindSafeTeleport(h, false, true); if (n != null) return n; } } catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                var sd = CityData.Instance.streetDirectory;
                var me = Player.Instance != null ? Player.Instance.transform.position : Vector3.zero;
                for (int k = 0; k < 15 && sd != null && sd.Count > 0; k++)
                {
                    var s = sd[UnityEngine.Random.Range(0, sd.Count)];
                    if (s == null) continue;
                    var n = c.FindSafeTeleport(s, false, true);
                    if (n == null) continue;
                    bool near = Vector3.Distance(n.position, me) < 40f;
                    foreach (var o in players) if (Vector3.Distance(n.position, o.targetPos) < 40f) near = true;
                    if (!near || k == 14) return n;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static void ReleasePuppet(string why)
        {
            var c = puppet; puppet = null; puppetId = -1;
            if (c == null) return;
            activePuppets.Remove(c.humanID);
            PuppetSeat(c, puppetSeatId, false); puppetSeatId = -1;
            string where = "на месте";
            try
            {
                var n = PuppetReleaseSpot(c);
                if (n != null) { c.Teleport(n, null, true, false, true); where = c.home != null ? "домой" : "на улицу"; }
            }
            catch (Exception e) { Plugin.L.LogWarning("[PUPPET] увести двойника: " + e.Message); }
            UnhidePuppet(c.humanID);
            try { if (Plugin.CfgPuppetNoAI.Value && c.ai != null) c.ai.EnableAI(true); } catch (Exception __e) { Err.Swallow(__e); }
            puppetRends = null;
            Plugin.L.LogInfo($"[PUPPET] двойник {c.humanID} ({c.citizenName}) возвращён в город {where}: {why}");
        }

        static int lookBlockLog;
        internal static void FilterPuppetLook(InteractionController ic)
        {
            if (manager == null || players.Count == 0 || ic == null) return;
            var cur = ic.currentLookingAtInteractable;
            if (cur == null) return;
            var it = cur.interactable;
            if (it == null || it.isActor == null) return;
            var h = it.isActor.TryCast<Human>();
            if (h == null || h.isPlayer || !IsPuppetId(h.humanID)) return;
            ic.currentLookingAtInteractable = null;
            ic.lookingAtInteractable = false;
            try { ic.UpdateInteractionText(); } catch (Exception __e) { Err.Swallow(__e); }
            if (lookBlockLog++ < 3) Plugin.L.LogInfo($"[PUPPET] смотрю на двойника {h.humanID}: взаимодействие с ним скрыто");
        }

        static void PuppetTick(float dt)
        {
            bool want = Plugin.CfgPuppet.Value && manager != null && manager.ConnectedPeersCount > 0 && avatar != null;
            if (!want)
            {
                if (puppet != null) ReleasePuppet("клиент не подключён");
                return;
            }
            if (puppet == null)
            {
                if (Time.unscaledTime - puppetAt < 2f) return;
                puppetAt = Time.unscaledTime;
                var c = PickPuppet();
                if (c == null) { Plugin.L.LogWarning("[PUPPET] не нашёл подходящего горожанина для двойника"); return; }
                puppet = c; puppetId = c.humanID; activePuppets.Add(c.humanID);
                CachePuppetParts(c);
                try { if (Plugin.CfgPuppetNoAI.Value && c.ai != null) c.ai.EnableAI(false); } catch (Exception e) { Plugin.L.LogWarning($"[PUPPET] EnableAI: {e.Message}"); }
                try { c.SetVisible(true, false); } catch (Exception __e) { Err.Swallow(__e); }
                Plugin.L.LogInfo($"[PUPPET] двойником второго игрока стал горожанин {c.humanID} ({c.citizenName}), бездомный={c.isHomeless}. Он невидим и стоит там, где стоит клиент");
            }
            var p = puppet;
            if (p == null) return;
            var tr = p.transform;
            Vector3 pos = targetPos;
            tr.position = pos;
            tr.rotation = Quaternion.Euler(0f, targetYaw - 180f, 0f);
            float now = Time.unscaledTime;
            if (now - puppetLocAt > 0.2f)
            {
                puppetLocAt = now;
                try { p.UpdateGameLocation(0f, false); } catch (Exception __e) { Err.Swallow(__e); }
            }
            if (now - puppetHideAt > 0.2f) { puppetHideAt = now; HidePuppet(); try { if (curRp != null) ApplyPuppetIllegal(p, curRp); } catch (Exception __e) { Err.Swallow(__e); } }
            if (now - puppetAiAt > 5f)
            {
                puppetAiAt = now;
                try { if (Plugin.CfgPuppetNoAI.Value && p.ai != null) p.ai.EnableAI(false); } catch (Exception __e) { Err.Swallow(__e); }
                try { if (!p.visible) p.SetVisible(true, false); } catch (Exception __e) { Err.Swallow(__e); }
                try { p.UpdateLightLevel(); } catch (Exception __e) { Err.Swallow(__e); }
            }
            try { if (puppetCrouch != remoteCrouched) { puppetCrouch = remoteCrouched; p.SetCrouched(remoteCrouched, true); } } catch (Exception __e) { Err.Swallow(__e); }
            int wantSeat = remoteSitting ? remoteSeatId : -1;
            if (wantSeat != puppetSeatId) { PuppetSeat(p, puppetSeatId, false); PuppetSeat(p, wantSeat, true); puppetSeatId = wantSeat; }
        }

        static void PuppetSeat(Citizen c, int seatId, bool take)
        {
            if (c == null || seatId < 0) return;
            try
            {
                var it = FindById(seatId);
                var up = it != null ? it.usagePoint : null;
                if (up == null) return;
                if (take) { bool ok = up.TrySetUser(Interactable.UsePointSlot.defaultSlot, c, "SodCoop: player sits here"); Plugin.Dbg($"[SIT] место {seatId} отмечено занятым двойником {c.humanID}: {ok}"); }
                else up.RemoveUserFromAllSlots(c);
            }
            catch (Exception e) { Plugin.L.LogWarning("[SIT] отметить место: " + e.Message); }
        }

        static float puppetTblAt;
        static void PuppetTableTick()
        {
            float now = Time.unscaledTime;
            if (now - puppetTblAt < 3f || manager == null) return;
            puppetTblAt = now;
            var ids = new List<int[]>();
            foreach (var rp in players) { int id = PuppetIdOf(rp); if (id >= 0) ids.Add(new[] { rp.pid, id }); }
            if (ids.Count == 0) return;
            var w = new NetDataWriter(); w.Put(MsgPuppet); w.Put((byte)ids.Count);
            foreach (var pr in ids) { w.Put((byte)pr[0]); w.Put(pr[1]); }
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void OnPuppetTable(NetDataReader r)
        {
            int n = r.GetByte();
            for (int i = 0; i < n; i++)
            {
                int pid = r.GetByte(); int npc = r.GetInt();
                RP t = RpByPid(pid == MyPid ? 0 : pid);
                if (t == null) continue;
                Enter(t); OnPuppetId(npc); Leave();
            }
        }

        static void PuppetsFrame(float dt)
        {
            for (int i = 0; i < players.Count; i++)
            {
                Enter(players[i]);
                try
                {
                    if (IsHost) PuppetTick(dt);
                    else { PuppetClientTick(); PuppetClientFollow(); }
                }
                catch (Exception e) { if (!puppetErrLogged) { puppetErrLogged = true; Plugin.L.LogWarning($"[PUPPET] {e}"); } }
            }
            Leave();
            if (IsHost) { try { PuppetTableTick(); } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static void OnPuppetId(int id)
        {
            if (id == puppetId) return;
            if (puppetId >= 0) UnhidePuppet(puppetId);
            puppetId = id; puppetHideId = -1;
        }

        static void PuppetClientFollow()
        {
            if (!Rules.Pvp || puppetId < 0 || avatar == null || npcById == null) return;
            Citizen c; if (!npcById.TryGetValue(puppetId, out c) || c == null) return;
            try
            {
                var tr = c.transform; var at = avatar.transform;
                tr.position = at.position;
                tr.rotation = Quaternion.Euler(0f, at.eulerAngles.y, 0f);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void PuppetClientTick()
        {
            if (puppetId < 0 || npcById == null) return;
            if (puppetHideId == puppetId && Time.unscaledTime - puppetClientHideAt < 0.2f) return;
            puppetClientHideAt = Time.unscaledTime;
            Citizen c; if (!npcById.TryGetValue(puppetId, out c) || c == null) return;
            try
            {
                var rs = c.gameObject.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++) { try { if (rs[i] != null && rs[i].enabled) { rs[i].enabled = false; NoteHidden(c.humanID, rs[i]); } } catch (Exception __e) { Err.Swallow(__e); } }
                if (!Rules.Pvp)
                {
                    var cs = c.gameObject.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < cs.Length; i++) { try { if (cs[i] != null && cs[i].enabled) { cs[i].enabled = false; NoteHidden(c.humanID, cs[i]); } } catch (Exception __e) { Err.Swallow(__e); } }
                }
                if (puppetHideId != puppetId) Plugin.L.LogInfo($"[PUPPET] у меня горожанин {puppetId} ({c.citizenName}) - двойник другого игрока: спрятан");
                puppetHideId = puppetId;
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static bool OnPuppetDamaged(int victimPid, float amount, Actor fromWho, Vector3 pos, Vector3 dir, SpatterPatternPreset fs, SpatterPatternPreset bs, int erase, bool alert, bool forceRag, float dur, float shock, bool kill, bool recoil, float ragMp)
        {
            try
            {
                if (manager == null || manager.ConnectedPeersCount == 0) return true;
                if (IsHost && !Rules.Pvp && fromWho != null && fromWho.isPlayer) return true;
                int aid = -1;
                try { var h = fromWho as Human; if (h != null && !h.isPlayer) aid = h.humanID; } catch (Exception __e) { Err.Swallow(__e); }
                var w = new NetDataWriter();
                w.Put(MsgPuppetHurt); w.Put((byte)victimPid);
                w.Put(aid); w.Put(amount);
                w.Put(pos.x); w.Put(pos.y); w.Put(pos.z); w.Put(dir.x); w.Put(dir.y); w.Put(dir.z);
                w.Put(fs != null ? (fs.name ?? "") : ""); w.Put(bs != null ? (bs.name ?? "") : "");
                w.Put(erase);
                int fl = (alert ? 1 : 0) | (forceRag ? 2 : 0) | (kill ? 4 : 0) | (recoil ? 8 : 0);
                w.Put((byte)fl); w.Put(dur); w.Put(shock); w.Put(ragMp);
                if (IsHost)
                {
                    var vr = RpByPid(victimPid);
                    if (vr == null || vr.peer == null) return true;
                    vr.peer.Send(w, DeliveryMethod.ReliableOrdered);
                }
                else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                if (Plugin.CfgPuppetLog.Value) Plugin.L.LogInfo($"[PUPPET] по игроку {victimPid} ударили (урон {amount:F2}, от {(aid >= 0 ? aid.ToString() : "игрока")}), переслал ему");
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[PUPPET] {e.Message}"); return false; }
        }

        static void OnPuppetHurt(NetDataReader r)
        {
            int aid = r.GetInt(); float amount = r.GetFloat();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            Vector3 dir = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            string fsn = r.GetString(), bsn = r.GetString();
            int erase = r.GetInt(); int fl = r.GetByte();
            float dur = r.GetFloat(), shock = r.GetFloat(), ragMp = r.GetFloat();
            Actor from = null;
            if (aid >= 0 && npcById != null) { Citizen a; if (npcById.TryGetValue(aid, out a)) from = a; }
            var me = Player.Instance;
            if (me == null) return;
            try
            {
                me.RecieveDamage(amount, from, pos, dir, FindSpatter(fsn), FindSpatter(bsn), (SpatterSimulation.EraseMode)erase,
                    (fl & 1) != 0, (fl & 2) != 0, dur, shock, (fl & 4) != 0, (fl & 8) != 0, ragMp);
                Plugin.L.LogInfo($"[PUPPET] меня ударил горожанин {aid}: урон {amount:F2}, здоровье {me.currentHealth:F2}");
                PlayHitSound(from, pos);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"[PUPPET] урон с атакующим не прошёл ({e.Message}), пробую без него");
                try { me.RecieveDamage(amount, null, pos, dir, FindSpatter(fsn), FindSpatter(bsn), (SpatterSimulation.EraseMode)erase, (fl & 1) != 0, (fl & 2) != 0, dur, shock, (fl & 4) != 0, (fl & 8) != 0, ragMp); }
                catch (Exception e2) { Plugin.L.LogWarning($"[PUPPET] {e2.Message}"); }
            }
        }

        static int aggroLog;
        static readonly System.Collections.Generic.Dictionary<int, float> aggroSeen = new System.Collections.Generic.Dictionary<int, float>();
        static void AggroNote(string what, NewAIController ai, Actor target)
        {
            try
            {
                var tc = target as Citizen; if (tc == null) return;
                var h = ai.human; if (h == null) return;
                int key = (what + h.humanID + ":" + tc.humanID).GetHashCode();
                float last; if (aggroSeen.TryGetValue(key, out last) && Time.unscaledTime - last < 20f) return;
                aggroSeen[key] = Time.unscaledTime;
                if (aggroLog++ >= 80) return;
                string st = "";
                try { st = $"бой={ai.inCombat} атака={ai.attackActive} оружие={(ai.currentWeaponPreset != null ? ai.currentWeaponPreset.name : "нет")}"; } catch (Exception __e) { Err.Swallow(__e); }
                string tst = ""; try { tst = $"цель: ko={tc.ai != null && tc.ai.ko} бой={tc.ai != null && tc.ai.inCombat}"; } catch (Exception __e) { Err.Swallow(__e); }
                string pd = ""; try { pd = $"; двойник в {Vector3.Distance(tc.transform.position, h.transform.position):F1} м от него, цель в {Vector3.Distance(tc.transform.position, h.transform.position):F1} м"; } catch (Exception __e) { Err.Swallow(__e); }
                Plugin.Dbg($"[AGGRO] горожанин {h.humanID} ({h.citizenName}) {what} ДРУГОГО жителя {tc.humanID} ({tc.citizenName}); {st}; {tst}{pd}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static void PuppetNote(string what, NewAIController ai, Actor target)
        {
            try
            {
                if (!IsHost || players.Count == 0 || target == null || !Plugin.CfgPuppetLog.Value || ai == null) return;
                if (!IsPuppetActor(target)) { if (what != "заметил") AggroNote(what, ai, target); return; }
                var h = ai.human;
                int id = h != null ? h.humanID : -1;
                float last; string key = what + id;
                int k = key.GetHashCode();
                if (puppetSeenLog.TryGetValue(k, out last) && Time.unscaledTime - last < 20f) return;
                puppetSeenLog[k] = Time.unscaledTime;
                Plugin.L.LogInfo($"[PUPPET] горожанин {id} ({(h != null ? h.citizenName : "?")}): {what} второго игрока");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }
    }
}

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
        sealed class CorpseState { public Vector3 last; public bool has; public float quietUntil, resendAt, loggedAt, appliedLogAt; public byte[] pending; }
        static readonly System.Collections.Generic.Dictionary<int, CorpseState> corpses = new System.Collections.Generic.Dictionary<int, CorpseState>();
        static float corpseTickAt;
        static bool corpseErrLogged;

        static void CorpseTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < corpseTickAt || npcById == null) return;
            corpseTickAt = now + 0.1f;
            Vector3 me = Vector3.zero; bool haveMe = false;
            try { if (Player.Instance != null) { me = Player.Instance.transform.position; haveMe = true; } } catch (Exception __e) { Err.Swallow(__e); }
            foreach (var kv in npcById)
            {
                var c = kv.Value;
                if (c == null || IsPuppetId(kv.Key)) continue;
                try
                {
                    bool ko = false; try { ko = c.ai != null && c.ai.ko; } catch (Exception __e) { Err.Swallow(__e); }
                    if (!c.isDead && !ko) { corpses.Remove(kv.Key); continue; }
                    CorpseState s;
                    if (!corpses.TryGetValue(kv.Key, out s)) { s = new CorpseState { quietUntil = now + 3f }; corpses[kv.Key] = s; }
                    var ac = c.animationController;
                    var rbs = ac != null ? ac.createdRBs : null;
                    if (rbs == null || rbs.Count == 0) continue;
                    if (s.pending != null) { var pd = s.pending; s.pending = null; ApplyCorpse(c, s, pd); continue; }
                    var torso = ac.upperTorsoRB != null ? ac.upperTorsoRB : rbs[0];
                    if (torso == null) continue;
                    Vector3 cur = torso.position;
                    if (!s.has || now < s.quietUntil) { s.last = cur; s.has = true; continue; }
                    bool moved = (cur - s.last).sqrMagnitude > 0.0025f && haveMe && (cur - me).sqrMagnitude < 9f;
                    bool resend = IsHost && now >= s.resendAt;
                    if (!moved && !resend) { if ((cur - s.last).sqrMagnitude > 0.0025f) s.last = cur; continue; }
                    s.last = cur; s.resendAt = now + 10f;
                    if (moved && now - s.loggedAt > 5f) { s.loggedAt = now; Plugin.L.LogInfo($"[CORPSE] тело {kv.Key}: я его двигаю - рассылаю позу ({rbs.Count} костей)"); }
                    var w = new NetDataWriter(); w.Put(MsgCorpse); w.Put(kv.Key); w.Put(rbs.Count);
                    foreach (var rb in rbs)
                    {
                        Vector3 p = rb != null ? rb.position : Vector3.zero; Quaternion q = rb != null ? rb.rotation : Quaternion.identity;
                        w.Put(rb != null ? NameHash(rb.name) : 0);
                        w.Put(p.x); w.Put(p.y); w.Put(p.z); w.Put(q.x); w.Put(q.y); w.Put(q.z); w.Put(q.w);
                    }
                    manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                }
                catch (Exception e) { if (!corpseErrLogged) { corpseErrLogged = true; Plugin.L.LogWarning("[CORPSE] " + e.Message); } }
            }
        }

        static void OnCorpseMsg(NetDataReader r)
        {
            int id = r.GetInt();
            byte[] data = r.GetRemainingBytes();
            CorpseState s;
            if (!corpses.TryGetValue(id, out s)) { s = new CorpseState(); corpses[id] = s; }
            if (npcById == null || npcById.Count == 0) BuildNpcMap();
            Citizen c;
            var rbs = npcById != null && npcById.TryGetValue(id, out c) && c != null && c.animationController != null ? c.animationController.createdRBs : null;
            if (rbs == null || rbs.Count == 0) { s.pending = data; return; }
            s.pending = null;
            ApplyCorpse(npcById[id], s, data);
        }

        static void ApplyCorpse(Citizen c, CorpseState s, byte[] data)
        {
            var rbs = c.animationController.createdRBs;
            var byName = new System.Collections.Generic.Dictionary<int, Rigidbody>();
            foreach (var rb in rbs) if (rb != null) byName[NameHash(rb.name)] = rb;
            var r = new NetDataReader(data);
            int n = r.GetInt();
            for (int i = 0; i < n; i++)
            {
                int h = r.GetInt();
                Vector3 p = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                Quaternion q = new Quaternion(r.GetFloat(), r.GetFloat(), r.GetFloat(), r.GetFloat());
                Rigidbody rb;
                if (!byName.TryGetValue(h, out rb) || rb == null) continue;
                rb.position = p; rb.rotation = q; rb.transform.SetPositionAndRotation(p, q);
                if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            float now = Time.realtimeSinceStartup;
            s.quietUntil = now + 1.5f; s.has = false;
            if (now - s.appliedLogAt > 5f) { s.appliedLogAt = now; Plugin.L.LogInfo($"[CORPSE] тело {c.humanID}: поза от другого игрока"); }
            try { c.UpdateGameLocation(0f, false); } catch (Exception __e) { Err.Swallow(__e); }
        }

        static int npcGetUpLogs;

        static void NpcGetUp(NpcTarget t, string why)
        {
            var c = t.c; if (c == null) return;
            var ai = c.ai; var ac = c.animationController;
            try { if (ai != null) { if (ai.ko) ai.SetKO(false, Vector3.zero, Vector3.zero, true, 0f); ai.ko = false; ai.isRagdoll = false; } } catch (Exception __e) { Err.Swallow(__e); }
            try { if (ac != null) { ac.SetRagdoll(false, false); ac.SetPauseAnimation(false); } } catch (Exception __e) { Err.Swallow(__e); }
            t.snap = true; t.settled = false;
            if (npcGetUpLogs < 30) { npcGetUpLogs++; Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: поднял из лежачего положения ({why})"); }
        }

        static void ApplyNpcState(NpcTarget t)
        {
            var c = t.c;
            var ac = c.animationController;
            if (ac == null) return;
            bool dead = (t.flags & 1) != 0;
            bool inBed = (t.flags & 8) != 0, low = (t.flags & 16) != 0;
            int mask = (t.flags & (1 | 2 | 4 | 8 | 16 | 32 | 128)) | ((t.flags2 & 3) << 8);
            if (mask != t.ap)
            {
                int old = t.ap; t.ap = mask;
                if (old < 0 || ((old ^ mask) & 1) != 0)
                {
                    if (dead)
                    {
                        try { ac.SetDead(true); c.isDead = true; Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: мёртв (по данным хоста)"); } catch (Exception __e) { Err.Swallow(__e); }
                        try { MakeCorpseSearchable(c); } catch (Exception e) { Plugin.L.LogWarning($"[NPC] горожанин {c.humanID}: обыск тела: {e.Message}"); }
                    }
                    else if (old >= 0)
                    {
                        try { ac.SetDead(false); c.isDead = false; Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: больше не мёртв (по данным хоста), снимаю позу смерти"); } catch (Exception __e) { Err.Swallow(__e); }
                    }
                }
                if (old < 0 || ((old ^ mask) & (8 | 16)) != 0)
                {
                    try { c.isInBed = inBed; c.isInLowBed = low; ac.SetInBed(inBed, low, false, true); } catch (Exception __e) { Err.Swallow(__e); }
                }
                bool koNow = (t.flags & 2) != 0;
                if (dead) t.apKo = koNow ? 1 : 0;
                else if (t.apKo != (koNow ? 1 : 0))
                {
                    bool first = t.apKo < 0; t.apKo = koNow ? 1 : 0;
                    if (!(first && !koNow))
                    {
                        try { if (c.ai != null) c.ai.SetKO(koNow, Vector3.zero, Vector3.zero, true, 0f); Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: {(koNow ? "оглушён" : "пришёл в себя")} (по данным хоста)"); }
                        catch (Exception __e) { Err.Swallow(__e); }
                        if (!koNow) NpcGetUp(t, "по данным хоста");
                    }
                }
                if (old < 0 || ((old ^ mask) & 32) != 0) { try { ac.SetCarryingItem((t.flags & 32) != 0); } catch (Exception __e) { Err.Swallow(__e); } }
                if (old < 0 || ((old ^ (mask)) & (1 << 8)) != 0) { try { c.isMoving = (t.flags2 & 1) != 0; } catch (Exception __e) { Err.Swallow(__e); } }
                if (old < 0 || ((old ^ mask) & 128) != 0) { try { c.isRunning = (t.flags & 128) != 0; } catch (Exception __e) { Err.Swallow(__e); } }
                if (old < 0 || ((old ^ (mask)) & (2 << 8)) != 0)
                {
                    bool rs = (t.flags2 & 2) != 0;
                    applyingRestrain = true;
                    try { if (c.ai != null && (rs || c.ai.restrained)) c.ai.SetRestrained(rs, 600f); else ac.SetRestrained(rs); }
                    catch (Exception) { try { ac.SetRestrained(rs); } catch (Exception __e) { Err.Swallow(__e); } }
                    finally { applyingRestrain = false; }
                }
            }
            if (!dead && (t.flags & 2) == 0 && Time.unscaledTime - t.getUpCheckAt > 1f)
            {
                t.getUpCheckAt = Time.unscaledTime;
                bool lying = false;
                try { var ai = c.ai; lying = ai != null && (ai.ko || ai.isRagdoll); } catch (Exception __e) { Err.Swallow(__e); }
                if (lying) NpcGetUp(t, "лежал, хотя у хоста стоит");
            }
            if (t.outfit != 255 && t.apOutfit != t.outfit && Time.unscaledTime - t.outfitTry > 1f)
            {
                t.outfitTry = Time.unscaledTime;
                try
                {
                    var oc = c.outfitController;
                    if (oc != null)
                    {
                        bool deadNow = (t.flags & 1) != 0;
                        if ((int)oc.currentOutfit != t.outfit || (deadNow && (int)oc.loadedOutfit != t.outfit))
                        {
                            oc.SetCurrentOutfit((ClothesPreset.OutfitCategory)t.outfit, deadNow, deadNow, false);
                            if (deadNow) Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: одежда тела как у хоста ({(ClothesPreset.OutfitCategory)t.outfit})");
                        }
                        t.apOutfit = t.outfit;
                    }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            if (t.apIdle != t.idle) { t.apIdle = t.idle; try { ac.SetIdleAnimationState((CitizenAnimationController.IdleAnimationState)t.idle); } catch (Exception __e) { Err.Swallow(__e); } }
            if (t.apArms != t.arms) { t.apArms = t.arms; try { ac.SetArmsBoolState((CitizenAnimationController.ArmsBoolSate)t.arms); } catch (Exception __e) { Err.Swallow(__e); } }
            if (t.apCarry != t.carryItem) { t.apCarry = t.carryItem; try { ac.SetCarryItemType(t.carryItem); } catch (Exception __e) { Err.Swallow(__e); } }
            if (t.apCombat != t.combatArms) { t.apCombat = t.combatArms; try { ac.SetCombatArmsOverride(t.combatArms); } catch (Exception __e) { Err.Swallow(__e); } }
            if (Mathf.Abs(t.speed - t.lastSpeed) > 0.01f)
            {
                t.lastSpeed = t.speed;
                try { c.currentNormalizedSpeed = t.speed; c.desiredNormalizedSpeed = t.speed; ac.UpdateMovementSpeed(); } catch (Exception __e) { Err.Swallow(__e); }
            }
        }
    }
}

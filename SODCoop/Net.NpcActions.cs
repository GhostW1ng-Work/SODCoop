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
        static int npcActLogged;
        static readonly System.Collections.Generic.Dictionary<int, float> actLastAt = new System.Collections.Generic.Dictionary<int, float>();
        static readonly int[] actLogCnt = new int[16], actLogCnt2 = new int[16];
        class AtkState { public bool atk, thr; public int shots; }
        static readonly System.Collections.Generic.Dictionary<int, AtkState> atkStates = new System.Collections.Generic.Dictionary<int, AtkState>();

        static void NpcActPoll(System.Collections.Generic.List<Citizen> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || IsPuppetCit(c)) continue;
                try
                {
                    var ai = c.ai; if (ai == null) continue;
                    AtkState st;
                    if (!atkStates.TryGetValue(c.humanID, out st)) { st = new AtkState(); atkStates[c.humanID] = st; }
                    bool atk = ai.attackActive, thr = ai.throwActive; int shots = ai.revolverShots;
                    if (atk && !st.atk)
                    {
                        string wn = ""; try { var wp = ai.currentWeaponPreset; if (wp != null) wn = wp.name; } catch (Exception __e) { Err.Swallow(__e); }
                        if (actLogCnt[0]++ < 8)
                        {
                            string lw = ""; try { var an = c.animationController.mainAnimator; for (int li = 0; li < an.layerCount; li++) lw += $" L{li}={an.GetLayerWeight(li):F1}"; lw += $" desired={c.animationController.armsLayerDesiredWeight:F1}"; } catch (Exception __e) { Err.Swallow(__e); }
                            Plugin.Dbg($"[NPCACT] хост: горожанин {c.humanID} начал атаку, оружие='{wn}', выстрелов={shots}, веса слоёв:{lw}");
                        }
                        NpcAct(c, 1, 0f, false, Vector3.zero, 0);
                    }
                    else if (shots != st.shots && shots > 0) NpcAct(c, 1, 0f, false, Vector3.zero, 0);
                    if (thr && !st.thr) NpcAct(c, 2, 0f, false, Vector3.zero, 0);
                    st.atk = atk; st.thr = thr; st.shots = shots;
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
        }
        static readonly System.Collections.Generic.Dictionary<int, int> npcHeldState = new System.Collections.Generic.Dictionary<int, int>();

        internal static void NpcAct(Citizen c, byte kind, float f, bool b, Vector3 pos, int state)
        {
            try
            {
                if (!IsHost || applyingRemote || !Plugin.CfgNpcSync.Value || c == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (kind <= 5)
                {
                    int key = c.humanID * 16 + kind; float la;
                    if (actLastAt.TryGetValue(key, out la) && Time.unscaledTime - la < 0.2f) return;
                    actLastAt[key] = Time.unscaledTime;
                }
                if (kind == 11) npcHeldState.Remove(c.humanID);
                if (kind == 10)
                {
                    int old; if (npcHeldState.TryGetValue(c.humanID, out old) && old == state) return;
                    npcHeldState[c.humanID] = state;
                }
                var w = new NetDataWriter();
                w.Put(MsgNpcAct); w.Put(c.humanID); w.Put(kind); w.Put(f); w.Put(b);
                w.Put(pos.x); w.Put(pos.y); w.Put(pos.z); w.Put(state);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                if (kind < 10 && actLogCnt[kind & 15]++ < 8) Plugin.Dbg($"[NPCACT] хост: горожанин {c.humanID} действие {kind} (f={f:F2})");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static void NpcActAc(CitizenAnimationController ac, byte kind, float f, bool b, Vector3 pos)
        {
            try { var c = ac != null ? ac.cit as Citizen : null; if (c != null) NpcAct(c, kind, f, b, pos, 0); } catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static void NpcActAi(NewAIController ai, byte kind, int state)
        {
            try { var c = ai != null ? ai.human as Citizen : null; if (c != null) NpcAct(c, kind, 0f, false, Vector3.zero, state); } catch (Exception __e) { Err.Swallow(__e); }
        }

        static bool npcEnumLogged, combatErrLogged;
        static void OnNpcAct(NetDataReader r)
        {
            int id = r.GetInt(); byte kind = r.GetByte(); float f = r.GetFloat(); bool b = r.GetBool();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat()); int state = r.GetInt();
            if (npcById == null || !npcById.ContainsKey(id)) { try { BuildNpcMap(); } catch (Exception __e) { Err.Swallow(__e); } }
            Citizen c; if (npcById == null || !npcById.TryGetValue(id, out c) || c == null) return;
            if (kind < 10 && actLogCnt2[kind & 15]++ < 8) Plugin.Dbg($"[NPCACT] клиент: горожанин {id} действие {kind} (f={f:F2})");
            if (!npcEnumLogged)
            {
                npcEnumLogged = true;
                try { Plugin.Dbg("[NPCACT] ActionStateFlag: " + string.Join(", ", Enum.GetNames(typeof(AIActionPreset.ActionStateFlag)))); } catch (Exception __e) { Err.Swallow(__e); }
            }
            applyingRemote = true;
            try
            {
                var ac = c.animationController; var ai = c.ai;
                switch (kind)
                {
                    case 1:
                        if (ac != null)
                        {
                            NpcTarget nt; npcTargets.TryGetValue(id, out nt);
                            var an = ac.mainAnimator;
                            if (nt != null) nt.combatHoldUntil = Time.unscaledTime + 2.5f;
                            if (an != null && nt != null) { try { an.SetInteger("combatArmsOverride", nt.combatArms); } catch (Exception __e) { Err.Swallow(__e); } }
                            ac.AttackTrigger();
                            if (nt != null) nt.atkUntil = Time.unscaledTime + 1.2f;
                            if (actLogCnt2[8]++ < 6)
                            {
                                string d = "";
                                try
                                {
                                    if (an != null)
                                    {
                                        d = $"inCombat={an.GetBool("inCombat")} cao={an.GetInteger("combatArmsOverride")} arms={an.GetInteger("ArmsState")} слоёв={an.layerCount} enabled={an.enabled} speed={an.speed}";
                                        for (int li = 0; li < an.layerCount && li < 4; li++) d += $" L{li}:w={an.GetLayerWeight(li):F1}/{an.GetCurrentAnimatorClipInfo(li).Length}";
                                    }
                                }
                                catch (Exception __e) { Err.Swallow(__e); }
                                string held = "";
                                try
                                {
                                    var hi = ai != null ? ai.spawnedRightItem : null;
                                    if (hi == null) held = "нет";
                                    else
                                    {
                                        var rs = hi.GetComponentsInChildren<Renderer>(true); int on = 0; for (int ri = 0; ri < rs.Length; ri++) if (rs[ri] != null && rs[ri].enabled) on++;
                                        held = $"{hi.name} активен={hi.activeInHierarchy} рендереров={on}/{rs.Length} в {Vector3.Distance(hi.transform.position, c.transform.position):F2} м от жителя";
                                    }
                                }
                                catch (Exception __e) { Err.Swallow(__e); }
                                Plugin.Dbg($"[NPCACT] клиент: аниматор {id}: {d}; предмет в правой руке: {held}");
                            }
                        }
                        break;
                    case 2: if (ac != null) ac.ThrowTrigger(); break;
                    case 3: if (ac != null) ac.AbortAttackTrigger(); break;
                    case 4: if (ac != null) ac.BlockTrigger(f, b); break;
                    case 5: if (ac != null) ac.TakeDamageRecoil(pos); break;
                    case 10: if (ai != null) ai.UpdateHeldItems((AIActionPreset.ActionStateFlag)state); break;
                    case 11: if (ai != null) ai.DespawnRightItem(); break;
                    case 12: if (ai != null) ai.DespawnLeftItem(); break;
                    case 13: if (ai != null) ai.UpdateCurrentWeapon(); break;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[NPCACT] действие {kind} у {id}: {e.Message}"); }
            finally { applyingRemote = false; }
        }

        static readonly System.Text.RegularExpressions.Regex combatSoundRx = new System.Text.RegularExpressions.Regex("gun|shot|shoot|fire|pistol|revolver|rifle|bullet|punch|hit|kick|swing|draw(?!er)|holster|reload|impact|knife|sword|melee|block|click|cock|brass|casing|weapon|attack", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Collections.Generic.HashSet<string> soundLogged = new System.Collections.Generic.HashSet<string>();
        static readonly System.Collections.Generic.Dictionary<string, AudioEvent> audioByName = new System.Collections.Generic.Dictionary<string, AudioEvent>();

        internal static void OnWorldSound(AudioEvent ev, Actor who, Vector3 pos)
        {
            try
            {
                if (applyingRemote || ev == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (who != null && who.isPlayer) { OnPlayerSound(ev, pos); return; }
                if (!IsHost || !Plugin.CfgNpcSync.Value) return;
                var c = who as Citizen;
                if (c == null || IsPuppetCit(c)) return;
                string name = ev.name; if (string.IsNullOrEmpty(name)) return;
                bool fight = false;
                try { var ai = c.ai; fight = ai != null && (ai.inCombat || ai.attackActive || ai.ko); } catch (Exception __e) { Err.Swallow(__e); }
                bool match = combatSoundRx.IsMatch(name);
                if (!match && !fight) return;
                if (soundLogged.Count < 60 && soundLogged.Add(name)) Plugin.Dbg($"[SOUND] хост: '{name}' от горожанина {c.humanID} (бой={fight}, по имени={match})");
                var w = new NetDataWriter();
                w.Put(MsgSound); w.Put(c.humanID); w.Put(name); w.Put(pos.x); w.Put(pos.y); w.Put(pos.z);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }
    }
}

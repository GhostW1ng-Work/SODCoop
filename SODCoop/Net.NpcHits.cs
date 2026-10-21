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
        static int lookDepth; static float lookAt; static int lookLogged;
        static UnityEngine.Random.State lookSaved;

        internal static void LookSeedBegin(Human h)
        {
            try
            {
                if (!Plugin.CfgSeedLook.Value || h == null) return;
                if (lookDepth > 0 && Time.realtimeSinceStartup - lookAt > 2f) lookDepth = 0;
                if (lookDepth++ > 0) return;
                lookAt = Time.realtimeSinceStartup;
                lookSaved = UnityEngine.Random.state;
                int sd;
                unchecked { sd = NameHash(h.seed ?? "") * 31 + h.humanID * 7919 + 12345; }
                UnityEngine.Random.InitState(sd);
                if (lookLogged < 6) { lookLogged++; Plugin.Dbg($"[LOOK] внешность горожанина {h.humanID} привязана к его seed ({sd})"); }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static void LookSeedEnd()
        {
            try
            {
                if (lookDepth <= 0) return;
                if (--lookDepth == 0) UnityEngine.Random.state = lookSaved;
            }
            catch (Exception) { lookDepth = 0; }
        }

        static System.Collections.Generic.Dictionary<string, SpatterPatternPreset> spatterCache;
        static SpatterPatternPreset FindSpatter(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (spatterCache == null)
            {
                spatterCache = new System.Collections.Generic.Dictionary<string, SpatterPatternPreset>();
                foreach (var p in Resources.FindObjectsOfTypeAll<SpatterPatternPreset>())
                    if (p != null && !string.IsNullOrEmpty(p.name) && !spatterCache.ContainsKey(p.name)) spatterCache[p.name] = p;
            }
            SpatterPatternPreset r; return spatterCache.TryGetValue(name, out r) ? r : null;
        }

        static int ffLog;
        internal static bool OnClientHitsNpc(Citizen c, float amount, Actor fromWho, Vector3 pos, Vector3 dir, SpatterPatternPreset fs, SpatterPatternPreset bs, int erase, bool alert, bool forceRag, float dur, float shock, bool kill, bool recoil, float ragMp)
        {
            try
            {
                if (IsHost && !applyingRemote && players.Count > 0 && c != null)
                {
                    var vo = OwnerOfPuppet(c);
                    if (vo != null) return !OnPuppetDamaged(vo.pid, amount, fromWho, pos, dir, fs, bs, erase, alert, forceRag, dur, shock, kill, recoil, ragMp);
                }
                if (IsHost && !applyingRemote && players.Count > 0 && c != null && !IsPuppetCit(c) && Plugin.CfgNoFriendlyFire.Value)
                {
                    var att = AsCitizen(fromWho);
                    if (att != null && att != c && !IsPuppetCit(att) && att.ai != null)
                    {
                        bool atPuppet = false;
                        try { atPuppet = IsPuppetActor(att.ai.persuitTarget) || IsPuppetActor(att.ai.attackTarget); } catch (Exception __e) { Err.Swallow(__e); }
                        if (atPuppet)
                        {
                            if (ffLog++ < 12) Plugin.Dbg($"[FF] удар/выстрел горожанина {att.humanID} по случайному горожанину {c.humanID} (цель - второй игрок) отменён");
                            return false;
                        }
                    }
                }
                if (!IsHost && !applyingRemote && c != null && fromWho != null && fromWho.isPlayer)
                {
                    var vo = OwnerOfPuppet(c);
                    if (vo != null)
                    {
                        if (Plugin.CfgPvp.Value) OnPuppetDamaged(vo.pid, amount, fromWho, pos, dir, fs, bs, erase, alert, forceRag, dur, shock, kill, recoil, ragMp);
                        return false;
                    }
                }
                if (IsHost || applyingRemote || !Plugin.CfgNpcSync.Value || manager == null || manager.ConnectedPeersCount == 0) return true;
                if (c == null || fromWho == null || !fromWho.isPlayer) return true;
                var w = new NetDataWriter();
                w.Put(MsgNpcHit);
                w.Put(c.humanID); w.Put(amount);
                w.Put(pos.x); w.Put(pos.y); w.Put(pos.z); w.Put(dir.x); w.Put(dir.y); w.Put(dir.z);
                w.Put(fs != null ? (fs.name ?? "") : ""); w.Put(bs != null ? (bs.name ?? "") : "");
                w.Put(erase);
                int fl = (alert ? 1 : 0) | (forceRag ? 2 : 0) | (kill ? 4 : 0) | (recoil ? 8 : 0);
                w.Put((byte)fl); w.Put(dur); w.Put(shock); w.Put(ragMp);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                try { c.animationController.TakeDamageRecoil(pos); } catch (Exception __e) { Err.Swallow(__e); }
                Plugin.Dbg($"[HIT] я ударил горожанина {c.humanID}: урон {amount:F2}, отправил хосту");
                return false;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HIT] {e.Message}"); return true; }
        }

        static void OnNpcHit(NetDataReader r)
        {
            int id = r.GetInt(); float amount = r.GetFloat();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            Vector3 dir = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            string fsn = r.GetString(), bsn = r.GetString();
            int erase = r.GetInt(); int fl = r.GetByte();
            float dur = r.GetFloat(), shock = r.GetFloat(), ragMp = r.GetFloat();
            if (npcById == null || !npcById.ContainsKey(id)) { try { BuildNpcMap(); } catch (Exception __e) { Err.Swallow(__e); } }
            Citizen c; if (npcById == null || !npcById.TryGetValue(id, out c) || c == null) { Plugin.L.LogWarning($"[HIT] горожанин {id} не найден у хоста"); return; }
            Actor attacker = null;
            if (Plugin.CfgPuppet.Value && puppet != null && puppet != c) attacker = puppet;
            else if (Plugin.CfgNpcHitHostAttacker.Value) { try { attacker = Player.Instance; } catch (Exception __e) { Err.Swallow(__e); } }
            applyingRemote = true;
            try
            {
                c.RecieveDamage(amount, attacker, pos, dir, FindSpatter(fsn), FindSpatter(bsn), (SpatterSimulation.EraseMode)erase,
                    (fl & 1) != 0, (fl & 2) != 0, dur, shock, (fl & 4) != 0, (fl & 8) != 0, ragMp);
                Plugin.Dbg($"[HIT] второй игрок ударил горожанина {id}: урон {amount:F2}, здоровье теперь {c.currentHealth:F2}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HIT] применить удар: {e.Message}"); }
            finally { applyingRemote = false; }
        }
    }
}

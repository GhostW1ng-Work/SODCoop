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
        static readonly System.Text.RegularExpressions.Regex playerSoundRx = new System.Text.RegularExpressions.Regex("gun|shot|shoot|fire|pistol|revolver|rifle|bullet|punch|kick|swing|reload|knife|sword|melee|block|brass|casing|weapon|attack|equip|holster|cock|hammer|slash|stab|bat|crowbar|whoosh", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Collections.Generic.HashSet<string> fireEventNames = new System.Collections.Generic.HashSet<string>();
        static System.Collections.Generic.Dictionary<string, MurderWeaponPreset> weaponByName, weaponByPrefab;
        static int wpnLog;
        static MurderWeaponPreset lastShotWp; static float lastShotAt;

        static void BuildWeaponMaps()
        {
            if (weaponByName != null) return;
            weaponByName = new System.Collections.Generic.Dictionary<string, MurderWeaponPreset>();
            weaponByPrefab = new System.Collections.Generic.Dictionary<string, MurderWeaponPreset>();
            try
            {
                foreach (var wp in Resources.FindObjectsOfTypeAll<MurderWeaponPreset>())
                {
                    if (wp == null) continue;
                    weaponByName[wp.name] = wp;
                    try { if (wp.fireEvent != null) fireEventNames.Add(wp.fireEvent.name); } catch (Exception __e) { Err.Swallow(__e); }
                    try { if (wp.itemRightOverride != null) weaponByPrefab[wp.itemRightOverride.name] = wp; } catch (Exception __e) { Err.Swallow(__e); }
                    try { if (wp.itemLeftOverride != null) weaponByPrefab[wp.itemLeftOverride.name] = wp; } catch (Exception __e) { Err.Swallow(__e); }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[WEAPON] список оружия: " + e.Message); }
            Plugin.L.LogInfo("[WEAPON] видов оружия: " + weaponByName.Count + ", моделей в руках: " + weaponByPrefab.Count);
        }

        static MurderWeaponPreset FindWeapon(string name)
        {
            BuildWeaponMaps();
            MurderWeaponPreset w; weaponByName.TryGetValue(name ?? "", out w); return w;
        }

        static Citizen RemotePuppet()
        {
            if (IsHost) return puppet;
            Citizen c = null; if (puppetId >= 0 && npcById != null) npcById.TryGetValue(puppetId, out c);
            return c;
        }

        static string DetectHeldWeapon(out string names)
        {
            names = "";
            try
            {
                var fp = FirstPersonItemController.Instance;
                if (fp == null || fp.forceHolstered) return "";
                BuildWeaponMaps();
                var sb = new StringBuilder();
                MurderWeaponPreset wp;
                foreach (var pr in new[] { fp.rightPrefabReference, fp.leftPrefabReference })
                {
                    if (pr == null) continue;
                    string k = pr.name.Replace("(Clone)", "").Trim();
                    sb.Append("ref:" + k + ";");
                    if (weaponByPrefab.TryGetValue(k, out wp)) { names = sb.ToString(); return wp.name; }
                }
                for (int side = 0; side < 2; side++)
                {
                    Transform par = side == 0 ? fp.rightHandObjectParent : fp.leftHandObjectParent;
                    if (par == null) continue;
                    for (int i = 0; i < par.childCount; i++)
                    {
                        string k = par.GetChild(i).name.Replace("(Clone)", "").Trim();
                        sb.Append(k + ";");
                        if (weaponByPrefab.TryGetValue(k, out wp)) { names = sb.ToString(); return wp.name; }
                    }
                }
                names = sb.ToString();
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return "";
        }

        static string weaponSent = null; static float weaponResendAt; static string remoteWeaponName = ""; static int remoteWeaponCao = -1;

        static void SendWeaponIfChanged()
        {
            string names; string wn = DetectHeldWeapon(out names);
            bool changed = wn != weaponSent;
            if (!changed && Time.realtimeSinceStartup < weaponResendAt) return;
            weaponSent = wn; weaponResendAt = Time.realtimeSinceStartup + 5f;
            if (changed) Plugin.L.LogInfo("[WEAPON] у меня в руках оружие: '" + wn + "' (предмет '" + curItem + "', модели в руках: " + names + ")");
            var w = new NetDataWriter(); w.Put(MsgWeapon); w.Put(wn);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void OnWeaponMessage(NetDataReader r)
        {
            string prevWeapon = remoteWeaponName;
            remoteWeaponName = r.GetString(); remoteWeaponCao = -1;
            var wp = remoteWeaponName != "" ? FindWeapon(remoteWeaponName) : null;
            if (wp != null)
            {
                switch (wp.type)
                {
                    case MurderWeaponPreset.WeaponType.poison: case MurderWeaponPreset.WeaponType.strangulation: case MurderWeaponPreset.WeaponType.fists: remoteWeaponCao = -1; break;
                    default: remoteWeaponCao = 4; break;
                }
            }
            if (prevWeapon == remoteWeaponName) return;
            remoteHeldDirty = true;
            Plugin.L.LogInfo("[WEAPON] у второго игрока в руках оружие: '" + remoteWeaponName + "' тип=" + (wp != null ? "" + wp.type : "-") + " поза=" + remoteWeaponCao);
        }

        static void OnPlayerSound(AudioEvent ev, Vector3 pos)
        {
            string name = ev.name; if (string.IsNullOrEmpty(name)) return;
            bool match = playerSoundRx.IsMatch(name);
            if (soundLogged.Count < 120 && soundLogged.Add("me:" + name)) Plugin.Dbg($"[SOUND] мой звук '{name}' (отправляю={match})");
            if (!match) return;
            var w = new NetDataWriter();
            w.Put(MsgSound); w.Put(-1); w.Put(name); w.Put(pos.x); w.Put(pos.y); w.Put(pos.z);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        internal static void OnShoot(Actor who, Vector3 muzzle, Vector3 aim, float range, float acc, float dmg, MurderWeaponPreset wp, bool eject, Vector3 ejectPt)
        {
            try
            {
                if (applyingRemote || who == null || !who.isPlayer || manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgShot); w.Put(wp != null ? (wp.name ?? "") : "");
                w.Put(muzzle.x); w.Put(muzzle.y); w.Put(muzzle.z); w.Put(aim.x); w.Put(aim.y); w.Put(aim.z);
                w.Put(range); w.Put(eject); w.Put(ejectPt.x); w.Put(ejectPt.y); w.Put(ejectPt.z);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                if (wpnLog++ < 8) Plugin.L.LogInfo($"[WEAPON] мой выстрел: {(wp != null ? wp.name : "?")}, урон {dmg:F2}, дальность {range:F1}, гильза={eject}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static bool IsUnder(Transform t, Transform root)
        {
            for (int i = 0; t != null && i < 40; i++) { if (t == root) return true; t = t.parent; }
            return false;
        }

        static void SpawnCasing(MurderWeaponPreset wp, Vector3 pos, Vector3 right)
        {
            try
            {
                if (wp.shellCasing == null) return;
                applyingRemote = true;
                Interactable it;
                try { it = InteractableCreator.Instance.CreateWorldInteractable(wp.shellCasing, null, null, null, pos, new Vector3(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), 0f), null, null, ""); }
                finally { applyingRemote = false; }
                if (it == null) return;
                GameObject go = it.spawnedObject;
                if (go == null && it.controller != null) go = it.controller.gameObject;
                if (go != null)
                {
                    var rb = go.GetComponentInChildren<Rigidbody>();
                    if (rb != null) { rb.isKinematic = false; rb.velocity = right * UnityEngine.Random.Range(1.2f, 2.2f) + Vector3.up * UnityEngine.Random.Range(1.0f, 2.0f); rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f; }
                }
                casings.Add(new KeyValuePair<Interactable, float>(it, Time.unscaledTime + 20f));
            }
            catch (Exception e) { if (brassLog++ < 4) Plugin.L.LogWarning("[FX] гильза игрока: " + e.Message); }
        }

        static void OnShotMessage(NetDataReader r)
        {
            string wn = r.GetString();
            Vector3 muzzle = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            Vector3 aim = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            float range = r.GetFloat(); bool eject = r.GetBool();
            Vector3 ep = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            try
            {
                var wp = FindWeapon(wn);
                var pc = RemotePuppet();
                lastShotWp = wp; lastShotAt = Time.realtimeSinceStartup;
                Vector3 dir = aim - muzzle; if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward; dir.Normalize();
                if (wp == null) { if (wpnLog++ < 8) Plugin.L.LogWarning("[WEAPON] выстрел из неизвестного оружия '" + wn + "'"); return; }
                if (wp.fireEvent != null)
                {
                    applyingRemote = true;
                    try { AudioController.Instance.PlayWorldOneShot(wp.fireEvent, pc, pc != null ? pc.currentNode : null, muzzle); }
                    finally { applyingRemote = false; }
                }
                if (wp.muzzleFlash != null)
                {
                    GameObject fx = UnityEngine.Object.Instantiate(wp.muzzleFlash);
                    fx.SetActive(true);
                    fx.transform.position = muzzle; fx.transform.rotation = Quaternion.LookRotation(dir);
                    foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { try { ps.Play(true); } catch (Exception __e) { Err.Swallow(__e); } }
                    foreach (var l in fx.GetComponentsInChildren<Light>(true)) { try { l.enabled = true; } catch (Exception __e) { Err.Swallow(__e); } }
                    UnityEngine.Object.Destroy(fx, 0.35f);
                }
                if (eject) SpawnCasing(wp, ep, Vector3.Cross(Vector3.up, dir).normalized);
                float maxd = range > 1f ? range : 60f;
                try { if (wp.maximumBulletRange > 1f) maxd = Mathf.Min(maxd, wp.maximumBulletRange); } catch (Exception __e) { Err.Swallow(__e); }
                var all = Physics.RaycastAll(muzzle, dir, maxd, ~0, QueryTriggerInteraction.Ignore);
                var list = new System.Collections.Generic.List<RaycastHit>();
                foreach (var h in all) list.Add(h);
                list.Sort((a, b) => a.distance.CompareTo(b.distance));
                var me = Player.Instance;
                foreach (var h in list)
                {
                    var col = h.collider; if (col == null) continue;
                    var ct = col.transform;
                    if ((avatar != null && IsUnder(ct, avatar.transform)) || (pc != null && IsUnder(ct, pc.transform)) || (me != null && IsUnder(ct, me.transform))) continue;
                    applyingRemote = true;
                    try { Toolbox.Instance.CreateBulletSurfaceContactFX(wp, h); }
                    catch (Exception e) { if (wpnLog++ < 8) Plugin.L.LogWarning("[WEAPON] след пули: " + e.Message); }
                    finally { applyingRemote = false; }
                    break;
                }
                if (wpnLog++ < 8) Plugin.L.LogInfo($"[WEAPON] выстрел второго игрока: {wp.name}, вспышка={(wp.muzzleFlash != null)}, звук={(wp.fireEvent != null ? wp.fireEvent.name : "нет")}, гильза={eject}");
            }
            catch (Exception e) { if (wpnLog++ < 8) Plugin.L.LogWarning("[WEAPON] выстрел второго игрока: " + e.Message); }
        }

        static void OnSoundMessage(NetDataReader r)
        {
            int id = r.GetInt(); string name = r.GetString();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            if (id >= 0 && (IsHost || !Plugin.CfgNpcSync.Value)) return;
            BuildWeaponMaps();
            if (id < 0 && fireEventNames.Contains(name)) return;
            try
            {
                Citizen c = null;
                if (id < 0) c = RemotePuppet(); else if (npcById != null) npcById.TryGetValue(id, out c);
                AudioEvent ev;
                if (!audioByName.TryGetValue(name, out ev) || ev == null)
                {
                    ev = null;
                    foreach (var e in Resources.FindObjectsOfTypeAll<AudioEvent>()) { if (e != null && e.name == name) { ev = e; break; } }
                    audioByName[name] = ev;
                }
                if (ev == null) { if (soundLogged.Add("miss:" + name)) Plugin.L.LogWarning($"[SOUND] у меня нет звука '{name}'"); return; }
                if (soundLogged.Count < 60 && soundLogged.Add("play:" + name)) Plugin.Dbg($"[SOUND] клиент: играю '{name}'");
                applyingRemote = true;
                try { AudioController.Instance.PlayWorldOneShot(ev, c, c != null ? c.currentNode : null, pos); }
                finally { applyingRemote = false; }
                MuzzleFx(c, name, pos);
            }
            catch (Exception e) { if (soundLogged.Add("err")) Plugin.L.LogWarning($"[SOUND] {e.Message}"); }
        }

        static AudioEvent FindAudio(string name)
        {
            AudioEvent ev;
            if (audioByName.TryGetValue(name, out ev) && ev != null) return ev;
            ev = null;
            foreach (var e in Resources.FindObjectsOfTypeAll<AudioEvent>()) { if (e != null && e.name == name) { ev = e; break; } }
            audioByName[name] = ev;
            return ev;
        }

        static int brassLog;
        static readonly System.Collections.Generic.List<KeyValuePair<Interactable, float>> casings = new System.Collections.Generic.List<KeyValuePair<Interactable, float>>();

        static void EjectCasing(Citizen c, MurderWeaponPreset wp, GameObject hi)
        {
            try
            {
                if (wp.ejectBrassSetting != MurderWeaponPreset.EjectBrass.onFire || wp.shellCasing == null) return;
                Vector3 pos = hi != null ? hi.transform.TransformPoint(wp.brassEjectOffset) : c.transform.position + Vector3.up * 1.3f;
                Vector3 right = hi != null ? hi.transform.right : c.transform.right;
                Vector3 up = Vector3.up;
                applyingRemote = true;
                Interactable it;
                try { it = InteractableCreator.Instance.CreateWorldInteractable(wp.shellCasing, null, null, null, pos, new Vector3(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), 0f), null, null, ""); }
                finally { applyingRemote = false; }
                if (it == null) { if (brassLog++ < 3) Plugin.L.LogWarning("[FX] гильза не создана"); return; }
                GameObject go = it.spawnedObject;
                if (go == null && it.controller != null) go = it.controller.gameObject;
                if (go != null)
                {
                    var rb = go.GetComponentInChildren<Rigidbody>();
                    if (rb != null) { rb.isKinematic = false; rb.velocity = right * UnityEngine.Random.Range(1.2f, 2.2f) + up * UnityEngine.Random.Range(1.0f, 2.0f); rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f; }
                    else if (brassLog < 3) Plugin.Dbg("[FX] у гильзы нет Rigidbody");
                }
                casings.Add(new KeyValuePair<Interactable, float>(it, Time.unscaledTime + 20f));
                if (brassLog++ < 4) Plugin.Dbg($"[FX] клиент: гильза '{wp.shellCasing.name}' у {c.humanID}");
            }
            catch (Exception e) { if (brassLog++ < 4) Plugin.L.LogWarning("[FX] гильза: " + e.Message); }
        }

        static void CasingTick()
        {
            if (casings.Count == 0) return;
            for (int i = casings.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < casings[i].Value) continue;
                var it = casings[i].Key; casings.RemoveAt(i);
                applyingRemote = true;
                try { if (it != null) it.SafeDelete(false); } catch (Exception __e) { Err.Swallow(__e); } finally { applyingRemote = false; }
            }
        }

        static int hitSndLog;
        static void PlayHitSound(Actor from, Vector3 pos)
        {
            try
            {
                var cit = from as Citizen;
                AudioEvent ev = null;
                if (cit == null && lastShotWp != null && Time.realtimeSinceStartup - lastShotAt < 0.8f)
                {
                    ev = lastShotWp.impactEventPlayer; if (ev == null) ev = lastShotWp.impactEventBody;
                }
                if (ev == null && cit != null && cit.ai != null)
                {
                    var wp = cit.ai.currentWeaponPreset;
                    if (wp != null) { ev = wp.impactEventPlayer; if (ev == null) ev = wp.impactEventBody; }
                }
                if (ev == null) ev = FindAudio("punchHitFlesh");
                if (ev == null) return;
                applyingRemote = true;
                try { AudioController.Instance.PlayWorldOneShot(ev, cit, cit != null ? cit.currentNode : null, pos); }
                finally { applyingRemote = false; }
                if (hitSndLog++ < 5) Plugin.Dbg($"[SOUND] клиент: звук попадания '{ev.name}'");
            }
            catch (Exception e) { if (hitSndLog++ < 5) Plugin.L.LogWarning("[SOUND] попадание: " + e.Message); }
        }

        static int muzzleLog;
        static void MuzzleFx(Citizen c, string soundName, Vector3 pos)
        {
            try
            {
                if (c == null || c.ai == null) return;
                var wp = c.ai.currentWeaponPreset;
                if (wp == null || wp.fireEvent == null || wp.fireEvent.name != soundName) return;
                var flash = wp.muzzleFlash;
                var hi = c.ai.spawnedRightItem;
                if (flash == null) { if (muzzleLog++ < 3) Plugin.Dbg($"[FX] у оружия {wp.name} нет muzzleFlash"); return; }
                GameObject fx = UnityEngine.Object.Instantiate(flash);
                fx.SetActive(true);
                if (hi != null)
                {
                    fx.transform.SetParent(hi.transform, false);
                    fx.transform.localPosition = wp.muzzleOffset;
                    fx.transform.localRotation = Quaternion.identity;
                }
                else fx.transform.position = pos + Vector3.up * 1.4f;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { try { ps.Play(true); } catch (Exception __e) { Err.Swallow(__e); } }
                foreach (var l in fx.GetComponentsInChildren<Light>(true)) { try { l.enabled = true; } catch (Exception __e) { Err.Swallow(__e); } }
                UnityEngine.Object.Destroy(fx, 0.35f);
                EjectCasing(c, wp, hi);
                if (muzzleLog++ < 6) Plugin.Dbg($"[FX] клиент: вспышка '{flash.name}' у {c.humanID}, оружие {wp.name}, предмет={(hi != null ? hi.name : "нет")}, смещение={wp.muzzleOffset}");
            }
            catch (Exception e) { if (muzzleLog++ < 6) Plugin.L.LogWarning("[FX] " + e.Message); }
        }
    }
}

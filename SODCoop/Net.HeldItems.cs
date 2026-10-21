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
        class HeldPart { public string mesh; public string[] mats; public byte side; public float u = 1f; public float[] chain = new float[0]; }
        static System.Collections.Generic.List<HeldPart> remoteHeld = new System.Collections.Generic.List<HeldPart>();
        static bool remoteHeldDirty;
        static string remoteHeldSig = "";
        static string heldFor = "";
        static GameObject heldL, heldR;
        static float heldScanAt, heldResendAt;
        static string heldSentSig = "";
        static float meshMissAt;
        static float heldU = 1f;
        static string handNameR = "", handNameL = "";
        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Mesh>> meshCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Mesh>>();
        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Material>> matCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Material>>();

        static string CleanName(string n)
        {
            if (n == null) return "";
            n = n.Replace(" (Instance)", "").Replace("(Clone)", "").Trim();
            return n;
        }

        static void ScanHeld(System.Collections.Generic.List<HeldPart> res)
        {
            try
            {
                var fp = FirstPersonItemController.Instance;
                if (fp == null || fp.forceHolstered) return;
                string nm = curItem ?? "";
                if (nm == "" || nm == "nothing" || nm.IndexOf("fist", StringComparison.OrdinalIgnoreCase) >= 0) return;
                for (byte side = 0; side < 2; side++)
                {
                    Transform par = side == 0 ? fp.rightHandObjectParent : fp.leftHandObjectParent;
                    if (par == null) continue;
                    Vector3 pls = par.lossyScale;
                    foreach (var mf in par.GetComponentsInChildren<MeshFilter>(false))
                    {
                        if (mf == null || mf.sharedMesh == null || string.IsNullOrEmpty(mf.sharedMesh.name)) continue;
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mr == null || !mr.enabled) continue;
                        var tr = mf.transform;
                        var sm = mr.sharedMaterials;
                        var names = new string[sm.Length];
                        for (int i = 0; i < sm.Length; i++) names[i] = sm[i] != null ? CleanName(sm[i].name) + "\t" + TexName(sm[i]) : "\t";
                        var nodes = new System.Collections.Generic.List<Transform>();
                        Transform t = tr;
                        while (t != null && t != par && nodes.Count < 10) { nodes.Add(t); t = t.parent; }
                        if (t != par) continue;
                        nodes.Reverse();
                        var chain = new float[nodes.Count * 10];
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var n = nodes[i];
                            var lp = n.localPosition; var lr = n.localRotation; var lsc = n.localScale;
                            int o = i * 10;
                            chain[o] = lp.x; chain[o + 1] = lp.y; chain[o + 2] = lp.z;
                            chain[o + 3] = lr.x; chain[o + 4] = lr.y; chain[o + 5] = lr.z; chain[o + 6] = lr.w;
                            chain[o + 7] = lsc.x; chain[o + 8] = lsc.y; chain[o + 9] = lsc.z;
                        }
                        var part = new HeldPart();
                        part.mesh = mf.sharedMesh.name + "\t" + mf.sharedMesh.vertexCount;
                        part.mats = names;
                        part.side = side;
                        float u = Mathf.Pow(Mathf.Abs(pls.x * pls.y * pls.z), 1f / 3f);
                        if (u > 0f && !float.IsNaN(u) && !float.IsInfinity(u) && chain.Length >= 10)
                        {
                            chain[0] *= u; chain[1] *= u; chain[2] *= u;
                            chain[7] *= u; chain[8] *= u; chain[9] *= u;
                        }
                        part.u = 1f;
                        part.chain = chain;
                        res.Add(part);
                        if (res.Count >= 12) return;
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HELD] скан: {e.Message}"); }
        }

        static string HeldSig(System.Collections.Generic.List<HeldPart> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                sb.Append(p.mesh).Append('|').Append(p.side).Append('|').Append(string.Join(",", p.mats)).Append(';');
            }
            return sb.ToString();
        }

        static void SendHeldIfChanged()
        {
            if (Time.realtimeSinceStartup < heldScanAt) return;
            try { var fpc0 = FirstPersonItemController.Instance; if (fpc0 != null && fpc0.isConsuming) return; } catch (Exception __e) { Err.Swallow(__e); }
            heldScanAt = Time.realtimeSinceStartup + 0.5f;
            var parts = new System.Collections.Generic.List<HeldPart>();
            ScanHeld(parts);
            SendWeaponIfChanged();
            string hpn = parts.Count > 0 ? HeldPresetName() : "";
            string sig = HeldSig(parts) + "#" + hpn;
            if (sig == heldSentSig && Time.realtimeSinceStartup < heldResendAt) return;
            if (sig != heldSentSig)
                Plugin.Dbg($"[HELD] у меня в руках: {curItem}, частей={parts.Count} {(parts.Count > 0 ? "первая: меш=" + parts[0].mesh + " материал=" + string.Join("/", parts[0].mats) + " масштаб узла0=" + (parts[0].chain.Length >= 10 ? parts[0].chain[7].ToString("G4") : "-") + " узлов=" + (parts[0].chain.Length / 10) : "")}");
            heldSentSig = sig;
            heldResendAt = Time.realtimeSinceStartup + 5f;
            var w = new NetDataWriter();
            w.Put(MsgHeld);
            w.Put(parts.Count);
            foreach (var p in parts)
            {
                w.Put(p.mesh ?? "");
                w.Put(p.side);
                w.Put(p.mats.Length);
                foreach (var m in p.mats) w.Put(m ?? "");
                w.Put(p.u);
                w.Put(p.chain.Length / 10);
                foreach (var f in p.chain) w.Put(f);
            }
            w.Put(hpn);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static string TexName(Material m)
        {
            try
            {
                Texture t = null;
                if (m.HasProperty("_BaseColorMap")) t = m.GetTexture("_BaseColorMap");
                if (t == null && m.HasProperty("_MainTex")) t = m.GetTexture("_MainTex");
                if (t == null && m.HasProperty("_UnlitColorMap")) t = m.GetTexture("_UnlitColorMap");
                return t != null ? t.name : "";
            }
            catch (Exception) { return ""; }
        }

        static Mesh PickMesh(string name, int verts)
        {
            System.Collections.Generic.List<Mesh> l;
            if (!meshCache.TryGetValue(name, out l) || l.Count == 0) return null;
            foreach (var m in l) if (m != null && m.vertexCount == verts) return m;
            Plugin.L.LogWarning($"[HELD] меш '{name}': вариантов={l.Count}, с {verts} вершинами нет, беру первый");
            return l[0];
        }

        static Material PickMat(string name, string tex)
        {
            System.Collections.Generic.List<Material> l;
            if (!matCache.TryGetValue(name, out l) || l.Count == 0) return null;
            foreach (var m in l) if (m != null && TexName(m) == tex) return m;
            Plugin.L.LogWarning($"[HELD] материал '{name}': вариантов={l.Count}, с текстурой '{tex}' нет, беру первый");
            return l[0];
        }

        static void RebuildCaches()
        {
            try
            {
                meshCache.Clear(); matCache.Clear();
                foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                {
                    if (m == null || string.IsNullOrEmpty(m.name)) continue;
                    System.Collections.Generic.List<Mesh> l;
                    if (!meshCache.TryGetValue(m.name, out l)) { l = new System.Collections.Generic.List<Mesh>(); meshCache[m.name] = l; }
                    l.Add(m);
                }
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || string.IsNullOrEmpty(m.name)) continue;
                    string cn = CleanName(m.name);
                    System.Collections.Generic.List<Material> l;
                    if (!matCache.TryGetValue(cn, out l)) { l = new System.Collections.Generic.List<Material>(); matCache[cn] = l; }
                    l.Add(m);
                }
                Plugin.Dbg($"[HELD] кэш: мешей={meshCache.Count} материалов={matCache.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HELD] кэш: {e.Message}"); }
        }

        static void ClearHeld()
        {
            if (heldL != null) UnityEngine.Object.Destroy(heldL);
            if (heldR != null) UnityEngine.Object.Destroy(heldR);
            heldL = heldR = null; heldWeaponMode = false; heldPresetMode = false;
        }

        static GameObject Container(Transform bone)
        {
            var go = new GameObject("HeldItem");
            go.layer = bone.gameObject.layer;
            go.transform.SetParent(bone, false);
            return go;
        }

        static bool heldWeaponMode;
        static string remoteHeldPreset = "";
        static bool heldPresetMode;
        static string anchorNameR, anchorNameL;
        static System.Collections.Generic.Dictionary<string, InteractablePreset> weaponItemPreset;

        static void SetLayerRec(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRec(t.GetChild(i), layer);
        }

        static Transform FindHandDesc(Transform t, int depth)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                string n = c.name.ToLowerInvariant();
                if (n.Contains("hand") && !n.Contains("finger") && !n.Contains("thumb") && !n.Contains("index") && !n.Contains("pinky")) return c;
            }
            if (depth > 0)
                for (int i = 0; i < t.childCount; i++) { var r = FindHandDesc(t.GetChild(i), depth - 1); if (r != null) return r; }
            return null;
        }

        static void DumpTree(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(t.name).Append('(').Append(t.localPosition.ToString("F2")).Append(')');
            if (depth <= 0 || t.childCount == 0) return;
            sb.Append('[');
            for (int i = 0; i < t.childCount && i < 6; i++) { if (i > 0) sb.Append(", "); DumpTree(t.GetChild(i), depth - 1, sb); }
            sb.Append(']');
        }

        static bool handLogged;

        static Transform FindHandBone(bool left)
        {
            string hn = left ? handNameL : handNameR;
            Transform t = hn != "" ? FindDeep(avatarModel, hn) : null;
            Transform arm = FindDeep(avatarModel, left ? "LowerArmLeft" : "LowerArmRight");
            if (t == null) t = arm;
            if (t == null) return null;
            string ln = t.name.ToLowerInvariant();
            if (!ln.Contains("hand"))
            {
                var h = FindHandDesc(t, 3);
                if (h == null && t.childCount > 0) h = t.GetChild(0);
                if (h != null) t = h;
            }
            if (!handLogged)
            {
                handLogged = true;
                var sb = new StringBuilder();
                if (arm != null) DumpTree(arm, 3, sb);
                Plugin.L.LogInfo($"[WEAPON] кость руки: '{t.name}' (сустав NPC '{hn}'); дерево предплечья: {sb}");
            }
            return t;
        }

        static bool BuildWeaponModel()
        {
            try
            {
                var wp = FindWeapon(remoteWeaponName);
                if (wp == null) return false;
                GameObject pf = null; Vector3 lp = Vector3.zero, le = Vector3.zero; bool left = false; string src = "";
                if (wp.itemRightOverride != null) { pf = wp.itemRightOverride; lp = wp.itemRightLocalPos; le = wp.itemRightLocalEuler; src = "itemRightOverride"; }
                else if (wp.itemLeftOverride != null) { pf = wp.itemLeftOverride; lp = wp.itemLeftLocalPos; le = wp.itemLeftLocalEuler; left = true; src = "itemLeftOverride"; }
                else
                {
                    if (weaponItemPreset == null)
                    {
                        weaponItemPreset = new System.Collections.Generic.Dictionary<string, InteractablePreset>();
                        foreach (var ip in Resources.FindObjectsOfTypeAll<InteractablePreset>())
                            if (ip != null && ip.weapon != null && ip.prefab != null && !weaponItemPreset.ContainsKey(ip.weapon.name)) weaponItemPreset[ip.weapon.name] = ip;
                    }
                    InteractablePreset ipr;
                    if (weaponItemPreset.TryGetValue(wp.name, out ipr)) { pf = ipr.prefab; src = "модель предмета " + ipr.name; }
                }
                if (pf == null) { Plugin.L.LogWarning("[WEAPON] нет модели для '" + wp.name + "', показываю как есть"); return false; }
                Transform bone = FindHandBone(left);
                if (bone == null) return false;
                var cont = Container(bone);
                if (left) heldL = cont; else heldR = cont;
                var inst = UnityEngine.Object.Instantiate<GameObject>(pf);
                inst.name = "WeaponModel";
                inst.transform.SetParent(cont.transform, false);
                inst.transform.localPosition = lp;
                inst.transform.localRotation = Quaternion.Euler(le);
                inst.transform.localScale = Vector3.one;
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
                foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.Destroy(rb);
                SetLayerRec(cont.transform, bone.gameObject.layer);
                inst.SetActive(true);
                heldU = 1f;
                heldWeaponMode = true;
                Plugin.L.LogInfo($"[WEAPON] модель '{wp.name}' в руке аватара: источник {src}, кость {bone.name}, локальная поза {lp} / {le}, масштаб кости {bone.lossyScale}");
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning("[WEAPON] модель в руке: " + e.Message); ClearHeld(); return false; }
        }

        static string HeldPresetName()
        {
            try
            {
                var bs = BioScreenController.Instance;
                var sl = bs != null ? bs.selectedSlot : null;
                var it = sl != null ? sl.GetInteractable() : null;
                return it != null ? PresetNameOf(it) : "";
            }
            catch (Exception) { return ""; }
        }

        static Transform HeldAnchor(bool left)
        {
            if (anchorNameR == null)
            {
                try
                {
                    var list = CityData.Instance.citizenDirectory;
                    for (int i = 0; list != null && i < list.Count; i++)
                    {
                        var oc = list[i] != null ? list[i].outfitController : null;
                        if (oc == null) continue;
                        var r = oc.GetBodyAnchor(CitizenOutfitController.CharacterAnchor.HandRight);
                        var l = oc.GetBodyAnchor(CitizenOutfitController.CharacterAnchor.HandLeft);
                        if (r == null || l == null) continue;
                        anchorNameR = r.name; anchorNameL = l.name;
                        Plugin.L.LogInfo($"[HELD] кости для предметов в руке у горожан: '{anchorNameR}' / '{anchorNameL}'");
                        break;
                    }
                }
                catch (Exception e) { Plugin.L.LogWarning("[HELD] анкер руки: " + e.Message); }
                if (anchorNameR == null) { anchorNameR = ""; anchorNameL = ""; }
            }
            string nm = left ? anchorNameL : anchorNameR;
            Transform t = nm != "" ? FindDeep(avatarModel, nm) : null;
            return t ?? FindHandBone(left);
        }

        static bool BuildHeldFromPreset()
        {
            var pr = FindPreset(remoteHeldPreset);
            if (pr == null || pr.prefab == null) return false;
            string hand = (Plugin.CfgHeldHand.Value ?? "Auto").Trim().ToLowerInvariant();
            bool left = hand == "left" || (hand == "auto" && remoteHeld.Count > 0 && remoteHeld[0].side == 1);
            var anchor = HeldAnchor(left);
            if (anchor == null) return false;
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(pr.prefab, anchor); }
            catch (Exception e) { Plugin.L.LogWarning($"[HELD] префаб '{remoteHeldPreset}': {e.Message}"); return false; }
            if (go == null) return false;
            go.name = "HeldItem";
            go.transform.localPosition = pr.aiHeldObjectPosition;
            go.transform.localEulerAngles = pr.aiHeldObjectRotation;
            try { foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c); } catch (Exception __e) { Err.Swallow(__e); }
            try { foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.Destroy(rb); } catch (Exception __e) { Err.Swallow(__e); }
            try { foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(mb); } catch (Exception __e) { Err.Swallow(__e); }
            try { int layer = anchor.gameObject.layer; foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; } catch (Exception __e) { Err.Swallow(__e); }
            if (left) heldL = go; else heldR = go;
            heldPresetMode = true;
            Plugin.Dbg($"[HELD] предмет второго игрока '{remoteHeldPreset}' собран из префаба в кости '{anchor.name}' (смещение {pr.aiHeldObjectPosition}, поворот {pr.aiHeldObjectRotation})");
            return true;
        }

        static void BuildHeld()
        {
            ClearHeld();
            if (avatarModel == null) return;
            if (remoteWeaponName != "" && BuildWeaponModel()) return;
            if (remoteHeld.Count == 0) return;
            if (remoteHeldPreset != "" && BuildHeldFromPreset()) return;
            if (meshCache.Count == 0) RebuildCaches();
            var bl = FindHandBone(true);
            var br = FindHandBone(false);
            string hand = (Plugin.CfgHeldHand.Value ?? "Auto").Trim().ToLowerInvariant();
            int made = 0;
            foreach (var p in remoteHeld)
            {
                string mname = p.mesh; int verts = -1;
                int tab = p.mesh.IndexOf('\t');
                if (tab >= 0) { mname = p.mesh.Substring(0, tab); int.TryParse(p.mesh.Substring(tab + 1), out verts); }
                Mesh mesh = PickMesh(mname, verts);
                if (mesh == null && Time.realtimeSinceStartup > meshMissAt)
                {
                    meshMissAt = Time.realtimeSinceStartup + 5f;
                    RebuildCaches();
                    mesh = PickMesh(mname, verts);
                }
                if (mesh == null) { Plugin.L.LogWarning($"[HELD] меш '{mname}' не найден"); continue; }
                bool left = hand == "left" || (hand == "auto" && p.side == 1);
                Transform bone = left ? bl : br;
                if (bone == null) continue;
                GameObject cont = left ? heldL : heldR;
                if (cont == null) { cont = Container(bone); if (left) heldL = cont; else heldR = cont; }
                heldU = 1f;
                Transform parent = cont.transform;
                GameObject go = null;
                for (int ci = 0; ci + 9 < p.chain.Length; ci += 10)
                {
                    go = new GameObject("n");
                    go.layer = cont.layer;
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = new Vector3(p.chain[ci], p.chain[ci + 1], p.chain[ci + 2]);
                    go.transform.localRotation = new Quaternion(p.chain[ci + 3], p.chain[ci + 4], p.chain[ci + 5], p.chain[ci + 6]);
                    go.transform.localScale = new Vector3(p.chain[ci + 7], p.chain[ci + 8], p.chain[ci + 9]);
                    parent = go.transform;
                }
                if (go == null) continue;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[p.mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    string mn = p.mats[i] ?? ""; string tn = "";
                    int mt = mn.IndexOf('\t');
                    if (mt >= 0) { tn = mn.Substring(mt + 1); mn = mn.Substring(0, mt); }
                    Material m = PickMat(CleanName(mn), tn);
                    if (m == null) Plugin.L.LogWarning($"[HELD] материал '{mn}' не найден");
                    mats[i] = m;
                }
                mr.sharedMaterials = new Il2CppReferenceArray<Material>(mats);
                made++;
            }
            Plugin.Dbg($"[HELD] у второго игрока собрано частей: {made} из {remoteHeld.Count}");
        }

        static void UpdateHeld()
        {
            if (avatarModel == null) return;
            if (remoteHeldDirty || (heldL == null && heldR == null && (remoteHeld.Count > 0 || remoteWeaponName != "") && Plugin.CfgHeldShow.Value && Time.realtimeSinceStartup > meshMissAt))
            {
                remoteHeldDirty = false;
                meshMissAt = Math.Max(meshMissAt, Time.realtimeSinceStartup + 2f);
                if (Plugin.CfgHeldShow.Value) BuildHeld(); else ClearHeld();
            }
            if (heldL == null && heldR == null) return;
            if (heldPresetMode) return;
            if (heldWeaponMode)
            {
                ApplyHeld(heldR ?? heldL, ParseV3(Plugin.CfgWpnPos.Value, Vector3.zero), Quaternion.Euler(ParseV3(Plugin.CfgWpnRot.Value, Vector3.zero)), Plugin.CfgWpnScale.Value);
                return;
            }
            Vector3 pos = ParseV3(Plugin.CfgHeldPos.Value, Vector3.zero);
            Quaternion rot = Quaternion.Euler(ParseV3(Plugin.CfgHeldRot.Value, Vector3.zero));
            float sc = Plugin.CfgHeldScale.Value;
            ApplyHeld(heldL, pos, rot, sc);
            ApplyHeld(heldR, pos, rot, sc);
        }

        static void ApplyHeld(GameObject go, Vector3 pos, Quaternion rot, float sc)
        {
            if (go == null) return;
            var t = go.transform;
            Vector3 bs = t.parent != null ? t.parent.lossyScale : Vector3.one;
            Vector3 inv = new Vector3(bs.x != 0f ? 1f / bs.x : 1f, bs.y != 0f ? 1f / bs.y : 1f, bs.z != 0f ? 1f / bs.z : 1f);
            t.localPosition = Vector3.Scale(pos, inv);
            t.localRotation = rot;
            t.localScale = inv * (sc * heldU);
        }

        static Vector3 ParseV3(string str, Vector3 def)
        {
            try
            {
                var p = (str ?? "").Split(',');
                if (p.Length != 3) return def;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                return new Vector3(float.Parse(p[0].Trim(), ci), float.Parse(p[1].Trim(), ci), float.Parse(p[2].Trim(), ci));
            }
            catch (Exception) { return def; }
        }
    }
}

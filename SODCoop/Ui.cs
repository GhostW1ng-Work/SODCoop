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
    public class Appearance
    {
        public int Gender;
        public int Build = 1;
        public int Eyes;
        public int Skin;
        public int HairCol;
        public string Head = "";
        public string Hair = "";
        public string Top = "";
        public string Bottom = "";
        public string Shoes = "";
        public string Hat = "";
        public string Glasses = "";
        public int TopCol = 3;
        public int BottomCol = 5;
        public int ShoesCol = 0;
        public string FirstName = "";
        public string SurName = "";

        internal string FullName()
        {
            return ((FirstName ?? "").Trim() + " " + (SurName ?? "").Trim()).Trim();
        }
    }

    internal static class CharEditor
    {
        internal static readonly Color[] Skins =
        {
            new Color(0.97f, 0.89f, 0.87f, 1f), new Color(0.93f, 0.80f, 0.68f, 1f), new Color(0.85f, 0.69f, 0.52f, 1f),
            new Color(0.70f, 0.52f, 0.37f, 1f), new Color(0.55f, 0.38f, 0.26f, 1f), new Color(0.38f, 0.26f, 0.18f, 1f),
            new Color(0.25f, 0.17f, 0.12f, 1f)
        };
        internal static readonly string[] HairColNames = { "black", "brown", "blonde", "ginger", "red", "blue", "green", "purple", "pink", "grey", "white" };
        internal static readonly Color[] HairCols =
        {
            new Color(0.05f, 0.04f, 0.04f, 1f), new Color(0.30f, 0.18f, 0.10f, 1f), new Color(0.85f, 0.72f, 0.40f, 1f),
            new Color(0.70f, 0.30f, 0.12f, 1f), new Color(0.61f, 0.09f, 0.09f, 1f), new Color(0.15f, 0.25f, 0.75f, 1f),
            new Color(0.15f, 0.60f, 0.25f, 1f), new Color(0.45f, 0.20f, 0.65f, 1f), new Color(0.90f, 0.45f, 0.65f, 1f),
            new Color(0.55f, 0.55f, 0.55f, 1f), new Color(0.92f, 0.92f, 0.92f, 1f)
        };
        internal static readonly Color[] Cloth =
        {
            new Color(0.10f, 0.10f, 0.10f, 1f), new Color(0.95f, 0.95f, 0.95f, 1f), new Color(0.50f, 0.50f, 0.50f, 1f),
            new Color(0.20f, 0.30f, 0.60f, 1f), new Color(0.65f, 0.15f, 0.15f, 1f), new Color(0.20f, 0.45f, 0.25f, 1f),
            new Color(0.80f, 0.65f, 0.25f, 1f), new Color(0.45f, 0.28f, 0.15f, 1f), new Color(0.80f, 0.55f, 0.65f, 1f),
            new Color(0.35f, 0.20f, 0.50f, 1f), new Color(0.85f, 0.45f, 0.15f, 1f), new Color(0.75f, 0.70f, 0.60f, 1f)
        };
        static readonly string[] GenderNames = { "Male", "Female", "Non-binary" };
        static readonly string[] BuildNames = { "Skinny", "Average", "Overweight", "Muscular" };
        static readonly string[] EyeNames = { "Blue", "Brown", "Green", "Grey" };

        internal static bool Open;
        static Appearance cur = new Appearance();
        static string status = "";
        static GameObject donor, camGo, lightGo, menuCanvas;
        static Camera cam;
        static RenderTexture rt;
        static float yaw = 180f, zoom = 3.2f, camY;
        static bool dirty, poolsDirty = true, dumped; static int frameDue; static bool fillNaked = true;
        static float applyAt;
        static readonly Vector3 FarPos = new Vector3(7000f, 7000f, 7000f);

        static readonly List<ClothesPreset> heads = new List<ClothesPreset>(), hairs = new List<ClothesPreset>(), tops = new List<ClothesPreset>(),
            bottoms = new List<ClothesPreset>(), shoes = new List<ClothesPreset>(), hats = new List<ClothesPreset>(), glasses = new List<ClothesPreset>();
        static readonly List<ClothesPreset> nakedAll = new List<ClothesPreset>();

        static string SavePath { get { return Path.Combine(BepInEx.Paths.ConfigPath, "SodCoop_character.json"); } }

        internal static Appearance Load()
        {
            try { if (File.Exists(SavePath)) { var a = JsonSerializer.Deserialize<Appearance>(File.ReadAllText(SavePath), new JsonSerializerOptions { IncludeFields = true }); if (a != null) return a; } }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] не прочитал " + SavePath + ": " + e.Message); }
            return null;
        }

        static TMPro.TMP_InputField inFirst, inSur;

        static void HarvestName()
        {
            if (inFirst != null) cur.FirstName = (inFirst.text ?? "").Trim();
            if (inSur != null) cur.SurName = (inSur.text ?? "").Trim();
        }

        static Appearance mine;
        static bool mineLoaded;

        internal static Appearance Mine
        {
            get { if (!mineLoaded) { mineLoaded = true; mine = Load(); } return mine; }
        }

        static void Save()
        {
            HarvestName();
            mine = JsonSerializer.Deserialize<Appearance>(JsonSerializer.Serialize(cur, new JsonSerializerOptions { IncludeFields = true }), new JsonSerializerOptions { IncludeFields = true }); mineLoaded = true;
            try { File.WriteAllText(SavePath, JsonSerializer.Serialize(cur, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true })); status = "Saved"; Plugin.L.LogInfo("[CHAR] сохранено: " + SavePath); }
            catch (Exception e) { status = "Save failed: " + e.Message; Plugin.L.LogWarning("[CHAR] " + e.Message); }
        }

        static bool Has<T>(Il2CppSystem.Collections.Generic.List<T> l, T v) where T : struct
        {
            if (l == null) return false;
            for (int i = 0; i < l.Count; i++) if (EqualityComparer<T>.Default.Equals(l[i], v)) return true;
            return false;
        }

        static bool Covers(ClothesPreset c, CitizenOutfitController.CharacterAnchor a) { return Has(c.covers, a); }
        static bool Undressed(ClothesPreset c) { return Has(c.outfitCategories, ClothesPreset.OutfitCategory.undressed); }

        static void BuildPools()
        {
            poolsDirty = false;
            heads.Clear(); hairs.Clear(); tops.Clear(); bottoms.Clear(); shoes.Clear(); hats.Clear(); glasses.Clear(); nakedAll.Clear();
            var all = Toolbox.Instance.allClothes;
            var g = (Human.Gender)cur.Gender; var b = (Descriptors.BuildType)cur.Build;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i]; if (c == null) continue;
                if (!Has(c.suitableForGenders, g) || !Has(c.suitableForBuilds, b)) continue;
                var A = CitizenOutfitController.CharacterAnchor.Head;
                if (c.isHead && Covers(c, A)) { heads.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.Hair)) { hairs.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.Hat)) { hats.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.Glasses)) { glasses.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.beard)) continue;
                if (Undressed(c)) { nakedAll.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.upperTorso)) tops.Add(c);
                else if (Covers(c, CitizenOutfitController.CharacterAnchor.UpperLegRight) || Covers(c, CitizenOutfitController.CharacterAnchor.UpperLegLeft) || Covers(c, CitizenOutfitController.CharacterAnchor.lowerTorso)) bottoms.Add(c);
                else if (Covers(c, CitizenOutfitController.CharacterAnchor.RightFoot) || Covers(c, CitizenOutfitController.CharacterAnchor.LeftFoot)) shoes.Add(c);
            }
            Comparison<ClothesPreset> cmp = (x, y) => string.CompareOrdinal(x.name, y.name);
            heads.Sort(cmp); hairs.Sort(cmp); tops.Sort(cmp); bottoms.Sort(cmp); shoes.Sort(cmp); hats.Sort(cmp); glasses.Sort(cmp);
            Fix(ref cur.Head, heads, false); Fix(ref cur.Hair, hairs, true); Fix(ref cur.Top, tops, true); Fix(ref cur.Bottom, bottoms, true);
            Fix(ref cur.Shoes, shoes, true); Fix(ref cur.Hat, hats, true); Fix(ref cur.Glasses, glasses, true);
        }

        static void Fix(ref string v, List<ClothesPreset> pool, bool allowNone)
        {
            if (string.IsNullOrEmpty(v) && allowNone) return;
            for (int i = 0; i < pool.Count; i++) if (pool[i].name == v) return;
            v = (allowNone || pool.Count == 0) ? "" : pool[0].name;
        }

        static ClothesPreset Find(List<ClothesPreset> pool, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < pool.Count; i++) if (pool[i].name == name) return pool[i];
            return null;
        }

        static void DestroyDonor()
        {
            try { if (donor != null) UnityEngine.Object.Destroy(donor); } catch (Exception __e) { Err.Swallow(__e); }
            donor = null;
        }

        static bool CreateDonor()
        {
            try
            {
                var prefab = PrefabControls.Instance.citizen;
                if (prefab == null) { status = "No citizen prefab"; return false; }
                var holder = new GameObject("SodCoop_DonorHolder"); holder.SetActive(false);
                var go = UnityEngine.Object.Instantiate<GameObject>(prefab, holder.transform);
                go.name = "SodCoop_Donor";
                rebound = false; scaled.Clear();
                go.transform.SetParent(null, false);
                go.transform.position = FarPos;
                go.transform.rotation = Quaternion.identity;
                UnityEngine.Object.Destroy(holder);
                var hum = go.GetComponent<Citizen>();
                if (hum != null)
                {
                    try { hum.gender = (Human.Gender)cur.Gender; } catch (Exception __e) { Err.Swallow(__e); }
                    try { if (hum.descriptors == null) hum.descriptors = new Descriptors(hum); } catch (Exception e) { Plugin.L.LogWarning("[CHAR] descriptors: " + e.Message); }
                }
                var mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < mbs.Length; i++)
                {
                    var m = mbs[i]; if (m == null) continue;
                    if (m.TryCast<CitizenOutfitController>() != null) continue;
                    try { m.enabled = false; } catch (Exception __e) { Err.Swallow(__e); }
                }
                go.SetActive(true);
                donor = go;
                Plugin.L.LogInfo("[CHAR] донор создан из префаба, citizen=" + (hum != null));
                return true;
            }
            catch (Exception e) { status = "Donor failed: " + e.Message; Plugin.L.LogWarning("[CHAR] донор: " + e); return false; }
        }

        static Color Src(ClothesPreset.ClothingColourSource s, Color own, Color skin, Color hair, Color dflt)
        {
            switch (s)
            {
                case ClothesPreset.ClothingColourSource.skin: return skin;
                case ClothesPreset.ClothingColourSource.hair: return hair;
                case ClothesPreset.ClothingColourSource.white: return Color.white;
                case ClothesPreset.ClothingColourSource.garment:
                case ClothesPreset.ClothingColourSource.underneathColour1:
                case ClothesPreset.ClothingColourSource.underneathColour2:
                case ClothesPreset.ClothingColourSource.underneathColour3: return own;
                default: return dflt;
            }
        }

        static CitizenOutfitController.OutfitClothes Make(ClothesPreset p, Color own, Color skin, Color hair)
        {
            var oc = new CitizenOutfitController.OutfitClothes();
            oc.clothes = p.name;
            var clear = new Color(0f, 0f, 0f, 0f);
            oc.baseColor = Src(p.baseColourSource, own, skin, hair, Color.white);
            oc.color1 = Src(p.colour1Source, own, skin, hair, clear);
            oc.color2 = Src(p.colour2Source, own, skin, hair, clear);
            oc.color3 = Src(p.colour3Source, own, skin, hair, clear);
            return oc;
        }

        static readonly CitizenOutfitController.CharacterAnchor[] BodyAnchors =
        {
            CitizenOutfitController.CharacterAnchor.lowerTorso, CitizenOutfitController.CharacterAnchor.upperTorso, CitizenOutfitController.CharacterAnchor.Midriff,
            CitizenOutfitController.CharacterAnchor.UpperArmRight, CitizenOutfitController.CharacterAnchor.UpperArmLeft,
            CitizenOutfitController.CharacterAnchor.LowerArmRight, CitizenOutfitController.CharacterAnchor.LowerArmLeft,
            CitizenOutfitController.CharacterAnchor.HandRight, CitizenOutfitController.CharacterAnchor.HandLeft,
            CitizenOutfitController.CharacterAnchor.UpperLegRight, CitizenOutfitController.CharacterAnchor.UpperLegLeft,
            CitizenOutfitController.CharacterAnchor.LowerLegRight, CitizenOutfitController.CharacterAnchor.LowerLegLeft,
            CitizenOutfitController.CharacterAnchor.RightFoot, CitizenOutfitController.CharacterAnchor.LeftFoot
        };

        static CitizenOutfitController.Outfit BuildOutfit()
        {
            Color skin = Skins[Mathf.Clamp(cur.Skin, 0, Skins.Length - 1)];
            Color hair = HairCols[Mathf.Clamp(cur.HairCol, 0, HairCols.Length - 1)];
            var chosen = new List<KeyValuePair<ClothesPreset, Color>>();
            Action<ClothesPreset, Color> add = (p, c) => { if (p != null) chosen.Add(new KeyValuePair<ClothesPreset, Color>(p, c)); };
            add(Find(heads, cur.Head), Color.white);
            add(Find(hairs, cur.Hair), hair);
            add(Find(tops, cur.Top), Cloth[cur.TopCol % Cloth.Length]);
            add(Find(bottoms, cur.Bottom), Cloth[cur.BottomCol % Cloth.Length]);
            add(Find(shoes, cur.Shoes), Cloth[cur.ShoesCol % Cloth.Length]);
            add(Find(hats, cur.Hat), Cloth[cur.TopCol % Cloth.Length]);
            add(Find(glasses, cur.Glasses), Color.white);

            var covered = new HashSet<CitizenOutfitController.CharacterAnchor>();
            foreach (var kv in chosen) for (int i = 0; i < kv.Key.covers.Count; i++) covered.Add(kv.Key.covers[i]);
            if (fillNaked) foreach (var a in BodyAnchors)
            {
                if (covered.Contains(a)) continue;
                ClothesPreset best = null;
                for (int i = 0; i < nakedAll.Count; i++)
                {
                    var n = nakedAll[i]; if (!Covers(n, a)) continue;
                    if (n.name.Contains("Prosthetic")) continue;
                    if (best == null) { best = n; continue; }
                    int sn = n.covers.Count * 10 + (n.name.Contains("Naked") ? 0 : 1), sb = best.covers.Count * 10 + (best.name.Contains("Naked") ? 0 : 1);
                    if (sn < sb || (sn == sb && string.CompareOrdinal(n.name, best.name) < 0)) best = n;
                }
                if (best == null) continue;
                add(best, Color.white);
                for (int i = 0; i < best.covers.Count; i++) covered.Add(best.covers[i]);
            }

            var outfit = new CitizenOutfitController.Outfit();
            outfit.category = ClothesPreset.OutfitCategory.casual;
            var list = new Il2CppSystem.Collections.Generic.List<CitizenOutfitController.OutfitClothes>();
            foreach (var kv in chosen) list.Add(Make(kv.Key, kv.Value, skin, hair));
            outfit.clothes = list;
            return outfit;
        }

        static void ApplyToDonor()
        {
            dirty = false;
            if (donor == null && !CreateDonor()) return;
            try
            {
                var hum = donor.GetComponent<Citizen>();
                Color skin = Skins[Mathf.Clamp(cur.Skin, 0, Skins.Length - 1)];
                Color hair = HairCols[Mathf.Clamp(cur.HairCol, 0, HairCols.Length - 1)];
                if (hum != null)
                {
                    hum.gender = (Human.Gender)cur.Gender;
                    var d = hum.descriptors;
                    if (d != null)
                    {
                        d.build = (Descriptors.BuildType)cur.Build;
                        d.skinColour = skin; d.hairColour = hair;
                        d.hairColourCategory = (Descriptors.HairColour)Mathf.Clamp(cur.HairCol, 0, HairColNames.Length - 1);
                        d.eyeColour = (Descriptors.EyeColour)cur.Eyes;
                        d.hairType = string.IsNullOrEmpty(cur.Hair) ? Descriptors.HairStyle.bald : Descriptors.HairStyle.shortHair;
                    }
                    try { hum.SetPhysicalModelParams(); } catch (Exception __e) { Err.Swallow(__e); }
                }
                var oc = donor.GetComponentInChildren<CitizenOutfitController>(true);
                if (oc == null) { status = "No outfit controller"; return; }
                try { oc.enabled = true; } catch (Exception __e) { Err.Swallow(__e); }
                var outfit = BuildOutfit();
                try { oc.RemoveCurrentOutfit(); } catch (Exception __e) { Err.Swallow(__e); }
                oc.outfits.Clear();
                oc.outfits.Add(outfit);
                oc.SetCurrentOutfit(ClothesPreset.OutfitCategory.casual, true, true, false);
                try { oc.LoadExpression(); } catch (Exception __e) { Err.Swallow(__e); }
                FixLights();
                var rs = donor.GetComponentsInChildren<Renderer>(true);
                int on = 0; for (int i = 0; i < rs.Length; i++) if (rs[i] != null && rs[i].enabled) on++;
                frameDue = 2;
                status = "Parts: " + outfit.clothes.Count + ", renderers: " + on + "/" + rs.Length;
                Plugin.L.LogInfo("[CHAR] наряд применён: " + status);
            }
            catch (Exception e) { status = "Apply failed: " + e.Message; Plugin.L.LogWarning("[CHAR] наряд: " + e); }
        }

        static bool LodPart(Transform t)
        {
            for (int d = 0; d < 20 && t != null; d++) { string n = t.name; if (n.Contains("LODS") || n.StartsWith("Level0")) return true; t = t.parent; }
            return false;
        }

        static Vector3 frameCenter = new Vector3(0f, 0.95f, 0f); static float frameH = 1.8f;

        static void Dedupe()
        {
            try
            {
                var seen = new HashSet<string>();
                var rs = donor.GetComponentsInChildren<Renderer>(false);
                int removed = 0;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null || !r.enabled || LodPart(r.transform) || r.name == "DistantLOD") continue;
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    var p = r.transform.position - donor.transform.position;
                    string key = mf.sharedMesh.GetInstanceID() + "|" + Mathf.Round(p.x * 500f) + "|" + Mathf.Round(p.y * 500f) + "|" + Mathf.Round(p.z * 500f) + "|" + Mathf.Sign(r.transform.lossyScale.x);
                    if (!seen.Add(key)) { r.enabled = false; lodList.Add(r); removed++; }
                }
                if (removed > 0) Plugin.Dbg("[CHAR] убрано дублей: " + removed);
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] Dedupe: " + e.Message); }
        }

        static readonly HashSet<int> scaled = new HashSet<int>();
        static void Offset()
        {
            try
            {
                var rs = donor.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null || !r.enabled || LodPart(r.transform)) continue;
                    string n = r.name;
                    if (!n.Contains("Naked")) continue;
                    int id = r.gameObject.GetInstanceID(); if (scaled.Contains(id)) continue;
                    float k = 0f;
                    if (n.Contains("LowerTorso")) k = 1.03f;
                    else if (n.Contains("LowerArm") || n.Contains("LowerLeg")) k = 1.025f;
                    else if (n.Contains("Hand") || n.Contains("Foot")) k = 1.03f;
                    if (k > 0f) { scaled.Add(id); r.transform.localScale = r.transform.localScale * k; }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void Frame()
        {
            Dedupe(); Offset();
            try
            {
                var rs = donor.GetComponentsInChildren<Renderer>(false);
                bool any = false; Bounds b = new Bounds();
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null || !r.enabled || LodPart(r.transform)) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!dumped)
                {
                    dumped = true;
                    var all = donor.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < all.Length && i < 60; i++)
                    {
                        var r = all[i]; if (r == null) continue;
                        string mesh = "-";
                        try { var smr = r.TryCast<SkinnedMeshRenderer>(); if (smr != null) mesh = "SMR mesh=" + (smr.sharedMesh != null ? smr.sharedMesh.name + "/" + smr.sharedMesh.vertexCount : "null") + " root=" + (smr.rootBone != null) + " lb=" + smr.localBounds.size; else { var mf = r.GetComponent<MeshFilter>(); mesh = "MR mesh=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + "/" + mf.sharedMesh.vertexCount : "null"); } } catch (Exception __e) { Err.Swallow(__e); }
                        Plugin.L.LogInfo("[CHAR-R] " + r.name + " en=" + r.enabled + " go=" + r.gameObject.activeInHierarchy + " lod=" + LodPart(r.transform) + " mats=" + r.sharedMaterials.Length + " " + mesh + " pos=" + (r.transform.position - donor.transform.position));
                    }
                }
                if (!dumpedB)
                {
                    dumpedB = true;
                    try
                    {
                        var an = donor.GetComponentInChildren<Animator>(true);
                        Plugin.L.LogInfo("[CHAR-A] animator=" + (an != null) + (an == null ? "" : " ctrl=" + (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "null") + " avatar=" + (an.avatar != null ? an.avatar.name + " human=" + an.avatar.isHuman + " valid=" + an.avatar.isValid : "null") + " init=" + an.isInitialized + " hier=" + an.hasTransformHierarchy + " layers=" + an.layerCount + " enabled=" + an.enabled + " go=" + an.gameObject.name + " speed=" + an.speed));
                        var trs = donor.GetComponentsInChildren<Transform>(true);
                        Plugin.L.LogInfo("[CHAR-A] всего transform=" + trs.Length);
                        for (int i = 0; i < trs.Length && i < 70; i++)
                        {
                            var t = trs[i]; if (t == null) continue;
                            int d = 0; var p = t; while (p != null && p != donor.transform && d < 30) { p = p.parent; d++; }
                            Plugin.L.LogInfo("[CHAR-T] " + new string(' ', d) + t.name + " lp=" + t.localPosition + " wp=" + (t.position - donor.transform.position) + " ls=" + t.localScale);
                        }
                    }
                    catch (Exception e) { Plugin.L.LogWarning("[CHAR-A] " + e.Message); }
                }
                if (!any || b.size.y < 0.3f) { Plugin.L.LogWarning("[CHAR] нет видимых рендереров/нулевые границы"); return; }
                frameCenter = new Vector3(0f, 1.0f, 0f); frameH = 2.0f;
                Plugin.L.LogInfo("[CHAR] донор pos=" + donor.transform.position + " bounds.center=" + b.center + " size=" + b.size + " scale=" + donor.transform.lossyScale);
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] Frame: " + e.Message); }
        }

        static void FixLights()
        {
            if (donor == null) return;
            try { var m = donor.transform.Find("Model"); if (m != null) m.localScale = Vector3.one; } catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                var tr = donor.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < tr.Length; i++) if (tr[i] != null) tr[i].gameObject.layer = 0;
                var lgs = donor.GetComponentsInChildren<LODGroup>(true);
                for (int i = 0; i < lgs.Length; i++) if (lgs[i] != null) lgs[i].enabled = false;
                var rr = donor.GetComponentsInChildren<Renderer>(true);
                lodList.Clear();
                for (int i = 0; i < rr.Length; i++) if (rr[i] != null && (LodPart(rr[i].transform) || rr[i].name == "DistantLOD")) { rr[i].enabled = false; lodList.Add(rr[i]); }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                var sk = donor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < sk.Length; i++) if (sk[i] != null) sk[i].updateWhenOffscreen = true;
                var an = donor.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < an.Length; i++) if (an[i] != null) an[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            var rs = donor.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) { try { if (rs[i] != null) rs[i].renderingLayerMask = 0xFFFFFFFFu; } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static void EnsureCamera()
        {
            if (cam != null) return;
            try
            {
                rt = new RenderTexture(640, 960, 24, RenderTextureFormat.ARGB32);
                rt.Create();
                camGo = new GameObject("SodCoop_PreviewCam");
                camGo.transform.position = FarPos + new Vector3(0f, 1f, 3f);
                cam = camGo.AddComponent<Camera>();
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.07f, 0.12f, 1f);
                cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
                cam.cullingMask = -1; cam.allowHDR = true;
                try
                {
                    var hd = camGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                    hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
                    hd.backgroundColorHDR = cam.backgroundColor;
                    hd.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
                }
                catch (Exception e) { Plugin.L.LogWarning("[CHAR] HD-камера: " + e.Message); }
                lightGo = new GameObject("SodCoop_PreviewLight");
                lightGo.transform.position = FarPos + new Vector3(2f, 3f, 4f);
                lightGo.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 3f;
                try { var hl = lightGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); hl.intensity = 2600f; hl.lightUnit = UnityEngine.Rendering.HighDefinition.LightUnit.Lux; }
                catch (Exception e) { Plugin.L.LogWarning("[CHAR] HD-свет: " + e.Message); }
                var fill = new GameObject("SodCoop_PreviewFill");
                fill.transform.SetParent(lightGo.transform, false);
                fill.transform.rotation = Quaternion.Euler(10f, 20f, 0f);
                var l2 = fill.AddComponent<Light>(); l2.type = LightType.Directional; l2.intensity = 1f;
                try { var h2 = fill.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); h2.intensity = 1000f; h2.lightUnit = UnityEngine.Rendering.HighDefinition.LightUnit.Lux; }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            catch (Exception e) { status = "Camera failed: " + e.Message; Plugin.L.LogWarning("[CHAR] камера: " + e); }
        }

        static void UpdateCamera()
        {
            if (cam == null || donor == null) return;
            donor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var look = donor.transform.position + frameCenter + new Vector3(0f, camY, 0f);
            camGo.transform.position = look + new Vector3(0f, 0.1f, zoom * (frameH / 1.8f));
            camGo.transform.LookAt(look);
        }

        internal static void OpenEditor()
        {
            if (Open) return;
            cur = Load() ?? new Appearance();
            poolsDirty = true; Open = true; status = "";
            if (UiKit.Init()) UiOpen();
            else { try { menuCanvas = GameObject.Find("MenuCanvas"); if (menuCanvas != null) menuCanvas.SetActive(false); } catch (Exception __e) { Err.Swallow(__e); } }
            BuildPools(); EnsureCamera(); dirty = true; applyAt = 0f;
        }

        internal static void CloseEditor()
        {
            Open = false;
            UiClose();
            try { if (menuCanvas != null) menuCanvas.SetActive(true); } catch (Exception __e) { Err.Swallow(__e); }
            menuCanvas = null;
            DestroyDonor();
            try { if (camGo != null) UnityEngine.Object.Destroy(camGo); if (lightGo != null) UnityEngine.Object.Destroy(lightGo); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); } } catch (Exception __e) { Err.Swallow(__e); }
            camGo = null; lightGo = null; cam = null; rt = null;
        }

        internal static void Tick()
        {
            if (!Open) return;
            if (poolsDirty) BuildPools();
            if (dirty && Time.realtimeSinceStartup >= applyAt) { applyAt = Time.realtimeSinceStartup + 0.15f; ApplyToDonor(); }
            PoseDonor();
            for (int i = 0; i < lodList.Count; i++) { try { if (lodList[i] != null && lodList[i].enabled) lodList[i].enabled = false; } catch (Exception __e) { Err.Swallow(__e); } }
            if (frameDue > 0 && --frameDue == 0) { Frame(); try { var oc = donor.GetComponentInChildren<CitizenOutfitController>(true); if (oc != null) oc.enabled = false; } catch (Exception __e) { Err.Swallow(__e); } }
            UpdateCamera();
        }

        static void PoseDonor()
        {
            if (donor == null) return;
            try
            {
                var an = donor.GetComponent<Animator>();
                if (an == null) an = donor.GetComponentInChildren<Animator>(true);
                if (an == null) return;
                if (an.transform.localScale.x < 0.5f) an.transform.localScale = Vector3.one;
                if (an.enabled) { an.enabled = false; an.fireEvents = false; an.applyRootMotion = false; }
                for (int li = 1; li < an.layerCount; li++) an.SetLayerWeight(li, 0f);
                an.SetBool("armsLock", false); an.SetBool("carryingItem", false); an.SetBool("restrained", false);
                an.SetInteger("carryItem", 0); an.SetInteger("ArmsState", 0);
                if (!rebound) { rebound = true; try { an.Rebind(); } catch (Exception __e) { Err.Swallow(__e); } }
                an.Update(0.02f);
            }
            catch (Exception e) { if (!poseWarn) { poseWarn = true; Plugin.L.LogWarning("[CHAR] PoseDonor: " + e.Message); } }
        }
        static readonly List<Renderer> lodList = new List<Renderer>();
        static bool poseWarn, dumpedB, rebound;

        static bool remoteBusy; static int remoteFrames;
        internal static bool RemoteBusy { get { return remoteBusy; } }

        internal static bool RemoteBegin(Appearance a)
        {
            if (Open || remoteBusy || a == null) return false;
            try
            {
                cur = a; BuildPools();
                DestroyDonor();
                if (!CreateDonor()) return false;
                ApplyToDonor();
                if (donor == null) return false;
                remoteBusy = true; remoteFrames = 0;
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] RemoteBegin: " + e.Message); DestroyDonor(); return false; }
        }

        internal static void RemoteAbort() { remoteBusy = false; DestroyDonor(); }

        internal static int RemoteStep(out Transform model, out List<Renderer> lod)
        {
            model = null; lod = null;
            if (!remoteBusy || donor == null) { remoteBusy = false; return -1; }
            try
            {
                PoseDonor();
                for (int i = 0; i < lodList.Count; i++) { try { if (lodList[i] != null && lodList[i].enabled) lodList[i].enabled = false; } catch (Exception __e) { Err.Swallow(__e); } }
                if (++remoteFrames < 3) return 0;
                Dedupe(); Offset();
                try { var oc = donor.GetComponentInChildren<CitizenOutfitController>(true); if (oc != null) oc.enabled = false; } catch (Exception __e) { Err.Swallow(__e); }
                var rr = donor.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rr.Length; i++)
                {
                    var r = rr[i]; if (r == null) continue;
                    if ((LodPart(r.transform) || r.name == "DistantLOD") && !lodList.Contains(r)) { r.enabled = false; lodList.Add(r); }
                }
                PoseDonor();
                var an = donor.GetComponentInChildren<Animator>(true);
                if (an == null || an.transform == donor.transform) { Plugin.L.LogWarning("[CHAR] у донора нет отдельной «Model» с аниматором"); RemoteAbort(); return -1; }
                model = an.transform;
                lod = new List<Renderer>(lodList);
                model.SetParent(null, true);
                var old = donor; donor = null; remoteBusy = false;
                UnityEngine.Object.Destroy(old);
                return 1;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] RemoteStep: " + e); RemoteAbort(); return -1; }
        }

        internal static string ToJson(Appearance a)
        {
            try { return JsonSerializer.Serialize(a, new JsonSerializerOptions { IncludeFields = true }); } catch (Exception) { return ""; }
        }

        internal static Appearance FromJson(string j)
        {
            try { if (string.IsNullOrEmpty(j)) return null; return JsonSerializer.Deserialize<Appearance>(j, new JsonSerializerOptions { IncludeFields = true }); }
            catch (Exception) { return null; }
        }

        static UiKit.Win win;
        static bool uiDirty;
        static RectTransform previewRt;
        static TMPro.TextMeshProUGUI uiStatus;
        static int dragBtn = -1; static Vector3 lastMouse;

        static bool UiOn { get { return win != null && win.Alive; } }

        static void UiOpen()
        {
            if (!UiKit.Init()) return;
            if (!UiOn) win = UiKit.MakeWindow("SodCoop CharacterPanel", UiKit.T("Character", "Персонаж"), 1800f, 980f);
            UiKit.Show(win, true);
            uiDirty = true;
        }

        static void UiClose() { if (UiOn) UiKit.Show(win, false); previewRt = null; dragBtn = -1; }

        static string[] Names(string[] en, string[] r) { return UiKit.Ru ? r : en; }

        internal static void UiTick()
        {
            if (!Open || !UiOn) return;
            if (UiKit.MainPanelShown) { CloseEditor(); return; }
            if (uiDirty) { uiDirty = false; Rebuild(); }
            string shownStatus = status != null && !status.StartsWith("Parts:") ? status : "";
            if (uiStatus != null && uiStatus.text != shownStatus) uiStatus.text = shownStatus;
            if (previewRt == null) return;
            Vector3 mp = UnityEngine.Input.mousePosition;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(previewRt, new Vector2(mp.x, mp.y), null);
            if (over)
            {
                float sc = UnityEngine.Input.mouseScrollDelta.y;
                if (sc != 0f) zoom = Mathf.Clamp(zoom - sc * 0.15f, 0.5f, 6f);
                if (UnityEngine.Input.GetMouseButtonDown(0)) dragBtn = 0;
                else if (UnityEngine.Input.GetMouseButtonDown(2)) dragBtn = 2;
            }
            if (dragBtn >= 0 && !UnityEngine.Input.GetMouseButton(dragBtn)) dragBtn = -1;
            if (dragBtn >= 0)
            {
                var d = mp - lastMouse;
                if (dragBtn == 2) camY = Mathf.Clamp(camY + d.y * 0.004f * zoom, -0.9f, 0.9f);
                else yaw += d.x * 0.6f;
            }
            lastMouse = mp;
        }

        static void Changed(bool pools) { if (pools) poolsDirty = true; dirty = true; uiDirty = true; }

        static void Rebuild()
        {
            HarvestName();
            var c = win.Content; UiKit.Clear(c); UiKit.Clear(win.Buttons);
            float W = c.rect.width, H = c.rect.height;
            if (W < 100f) { W = 1672f; H = 724f; }
            if (poolsDirty) BuildPools();
            float y = 6f, rh = 46f;
            Action<string> label = s => UiKit.Label(c, s, 0f, y, 200f, 40f, 21f, TMPro.TextAlignmentOptions.MidlineRight);
            var genders = Names(GenderNames, new[] { "Мужчина", "Женщина", "Небинарный" });
            var builds = Names(BuildNames, new[] { "Худое", "Среднее", "Полное", "Мускулистое" });
            var eyes = Names(EyeNames, new[] { "Голубые", "Карие", "Зелёные", "Серые" });
            label(UiKit.T("Gender", "Пол")); ChoiceRow(c, y, genders, cur.Gender, v => { cur.Gender = v; Changed(true); }); y += rh;
            label(UiKit.T("Build", "Телосложение")); ChoiceRow(c, y, builds, cur.Build, v => { cur.Build = v; Changed(true); }); y += rh;
            label(UiKit.T("Eyes", "Глаза")); ChoiceRow(c, y, eyes, cur.Eyes, v => { cur.Eyes = v; Changed(false); }); y += rh;
            label(UiKit.T("Skin tone", "Кожа")); SwatchRow(c, y, Skins, cur.Skin, v => { cur.Skin = v; Changed(true); }); y += rh;
            label(UiKit.T("Hair colour", "Цвет волос")); SwatchRow(c, y, HairCols, cur.HairCol, v => { cur.HairCol = v; Changed(true); }); y += rh;
            label(UiKit.T("Face", "Лицо")); PickRow(c, y, heads, cur.Head, v => cur.Head = v, false); y += rh;
            label(UiKit.T("Hair", "Причёска")); PickRow(c, y, hairs, cur.Hair, v => cur.Hair = v, true); y += rh;
            label(UiKit.T("Top", "Верх")); PickRow(c, y, tops, cur.Top, v => cur.Top = v, true); y += rh;
            label(UiKit.T("Top colour", "Цвет верха")); SwatchRow(c, y, Cloth, cur.TopCol, v => { cur.TopCol = v; Changed(true); }); y += rh;
            label(UiKit.T("Bottom", "Низ")); PickRow(c, y, bottoms, cur.Bottom, v => cur.Bottom = v, true); y += rh;
            label(UiKit.T("Bottom colour", "Цвет низа")); SwatchRow(c, y, Cloth, cur.BottomCol, v => { cur.BottomCol = v; Changed(true); }); y += rh;
            label(UiKit.T("Shoes", "Обувь")); PickRow(c, y, shoes, cur.Shoes, v => cur.Shoes = v, true); y += rh;
            label(UiKit.T("Shoes colour", "Цвет обуви")); SwatchRow(c, y, Cloth, cur.ShoesCol, v => { cur.ShoesCol = v; Changed(true); }); y += rh;
            label(UiKit.T("Hat", "Шляпа")); PickRow(c, y, hats, cur.Hat, v => cur.Hat = v, true); y += rh;
            label(UiKit.T("Glasses", "Очки")); PickRow(c, y, glasses, cur.Glasses, v => cur.Glasses = v, true); y += rh;

            float ph = H - 12f, pw = ph * 640f / 960f, px = 820f;
            var ri = UiKit.Raw(c, px, 6f, pw, ph, rt);
            previewRt = ri.GetComponent<RectTransform>();

            float rx = px + pw + 30f, rw = W - rx - 20f;
            uiStatus = UiKit.Label(c, "", rx, 10f, rw, 60f, 22f, TMPro.TextAlignmentOptions.TopLeft, UiKit.HeadColor);
            UiKit.Label(c, UiKit.T("Left mouse: rotate\nMiddle mouse: up / down\nWheel: zoom", "ЛКМ - вращать\nСКМ - вверх / вниз\nКолесо - приблизить"), rx, 90f, rw, 110f, 20f, TMPro.TextAlignmentOptions.TopLeft);
            float ny = 230f;
            UiKit.Label(c, UiKit.T("Name in multiplayer", "Имя в мультиплеере"), rx, ny, rw, 36f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); ny += 42f;
            UiKit.Label(c, UiKit.T("First name", "Имя"), rx, ny, rw, 32f, 20f); ny += 34f;
            inFirst = UiKit.Input(c, rx, ny, rw, 46f, cur.FirstName ?? "", 24, false); ny += 56f;
            UiKit.Label(c, UiKit.T("Surname", "Фамилия"), rx, ny, rw, 32f, 20f); ny += 34f;
            inSur = UiKit.Input(c, rx, ny, rw, 46f, cur.SurName ?? "", 24, false); ny += 56f;
            UiKit.Label(c, UiKit.T("Empty = the name from the save. Press Save.", "Пусто - имя из сейва. Нажми «Сохранить»."), rx, ny, rw, 60f, 18f, TMPro.TextAlignmentOptions.TopLeft);

            UiKit.BarButton(win, UiKit.T("Back", "Назад"), 260f, () => CloseEditor());
            UiKit.BarButton(win, UiKit.T("Random", "Случайно"), 260f, () => { Randomize(); uiDirty = true; });
            UiKit.BarButton(win, UiKit.T("Save", "Сохранить"), 260f, () => { Save(); uiDirty = true; });
        }

        const float RowX = 212f, RowW = 580f;

        static void ChoiceRow(Transform c, float y, string[] names, int v, Action<int> set)
        {
            float gap = 6f, bw = (RowW - gap * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                var b = UiKit.Button(c, names[i], RowX + i * (bw + gap), y, bw, 40f, () => { if (k != v) set(k); }, 20f);
                UiKit.SetSelected(b, i == v);
            }
        }

        static void SwatchRow(Transform c, float y, Color[] cols, int v, Action<int> set)
        {
            float gap = 4f, bw = (RowW - gap * (cols.Length - 1)) / cols.Length;
            for (int i = 0; i < cols.Length; i++)
            {
                int k = i;
                UiKit.Swatch(c, RowX + i * (bw + gap), y, bw, 40f, cols[i], i == v, () => { if (k != v) set(k); });
            }
        }

        static void PickRow(Transform c, float y, List<ClothesPreset> pool, string curName, Action<string> set, bool allowNone)
        {
            int idx = -1; for (int i = 0; i < pool.Count; i++) if (pool[i].name == curName) { idx = i; break; }
            string shown = idx >= 0 ? pool[idx].name : (allowNone ? UiKit.T("(none)", "(нет)") : "-");
            int count = pool.Count + (allowNone ? 1 : 0);
            int pos = allowNone ? idx + 1 : Mathf.Max(0, idx);
            Action<int> go = np =>
            {
                if (count <= 0) return;
                np = (np % count + count) % count; if (np == pos) return;
                int pi = allowNone ? np - 1 : np;
                set(pi < 0 ? "" : pool[pi].name); Changed(false);
            };
            UiKit.Button(c, "<", RowX, y, 52f, 40f, () => go(pos - 1), 22f);
            UiKit.Label(c, shown, RowX + 62f, y, RowW - 124f, 40f, 20f, TMPro.TextAlignmentOptions.Midline, Color.white);
            UiKit.Button(c, ">", RowX + RowW - 52f, y, 52f, 40f, () => go(pos + 1), 22f);
        }

        static GUIStyle btnStyle, labelStyle, titleStyle;

        static void Styles()
        {
            if (btnStyle != null) return;
            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            labelStyle.normal.textColor = new Color(0.95f, 0.85f, 0.9f, 1f);
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.95f, 0.3f, 0.35f, 1f);
        }

        static bool MenuVisible()
        {
            try
            {
                var mm = MainMenuController.Instance;
                return mm != null && mm.mainMenuActive;
            }
            catch (Exception) { return false; }
        }

        internal static void OnGui()
        {
            try
            {
                Styles();
                float s = Screen.height / 1080f;
                var old = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
                if (Open && UiOn) { GUI.matrix = old; return; }
                if (!Open)
                {
                    if (MenuVisible() && !NetMenu.Open && !UiKit.MainButtons && GUI.Button(new Rect(278f, 940f, 297f, 48f), "CHARACTER CREATE", btnStyle)) OpenEditor();
                    GUI.matrix = old; return;
                }
                float W = Screen.width / s, H = Screen.height / s;
                GUI.color = new Color(0f, 0f, 0f, 1f);
                GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(40f, 20f, 800f, 50f), "CHARACTER CREATE", titleStyle);

                float x = 40f, y = 90f, w = 520f;
                bool ch = false;
                ch |= Choice(ref y, x, w, "Gender", GenderNames, ref cur.Gender, true);
                ch |= Choice(ref y, x, w, "Build", BuildNames, ref cur.Build, true);
                ch |= Choice(ref y, x, w, "Eyes", EyeNames, ref cur.Eyes, false);
                ch |= Swatches(ref y, x, w, "Skin tone", Skins, ref cur.Skin);
                ch |= Swatches(ref y, x, w, "Hair colour", HairCols, ref cur.HairCol);
                bool pool = false;
                pool |= Pick(ref y, x, w, "Face", heads, v => cur.Head = v, cur.Head, false);
                pool |= Pick(ref y, x, w, "Hair", hairs, v => cur.Hair = v, cur.Hair, true);
                pool |= Pick(ref y, x, w, "Top", tops, v => cur.Top = v, cur.Top, true);
                ch |= Swatches(ref y, x, w, "Top colour", Cloth, ref cur.TopCol);
                pool |= Pick(ref y, x, w, "Bottom", bottoms, v => cur.Bottom = v, cur.Bottom, true);
                ch |= Swatches(ref y, x, w, "Bottom colour", Cloth, ref cur.BottomCol);
                pool |= Pick(ref y, x, w, "Shoes", shoes, v => cur.Shoes = v, cur.Shoes, true);
                ch |= Swatches(ref y, x, w, "Shoes colour", Cloth, ref cur.ShoesCol);
                pool |= Pick(ref y, x, w, "Hat", hats, v => cur.Hat = v, cur.Hat, true);
                pool |= Pick(ref y, x, w, "Glasses", glasses, v => cur.Glasses = v, cur.Glasses, true);
                if (ch) { poolsDirty = true; }
                if (ch || pool) dirty = true;

                var pr = new Rect(620f, 90f, 640f, 900f);
                if (rt != null) GUI.DrawTexture(pr, rt, ScaleMode.ScaleToFit, false);
                var e = Event.current;
                if (e != null && pr.Contains(e.mousePosition))
                {
                    if (e.type == EventType.MouseDrag)
                    {
                        if (e.button == 2) camY = Mathf.Clamp(camY - e.delta.y * 0.004f * zoom, -0.9f, 0.9f);
                        else yaw += e.delta.x * 0.6f;
                        e.Use();
                    }
                    else if (e.type == EventType.ScrollWheel) { zoom = Mathf.Clamp(zoom + e.delta.y * 0.15f, 0.5f, 6f); e.Use(); }
                }
                GUI.Label(new Rect(620f, 995f, 640f, 30f), "LMB drag: rotate   MMB drag: up/down   Wheel: zoom", labelStyle);

                if (GUI.Button(new Rect(1320f, 880f, 240f, 56f), "SAVE", btnStyle)) Save();
                if (GUI.Button(new Rect(1320f, 950f, 240f, 56f), "CLOSE", btnStyle)) CloseEditor();
                if (GUI.Button(new Rect(1320f, 810f, 240f, 56f), "RANDOM", btnStyle)) Randomize();
                GUI.Label(new Rect(1320f, 90f, 560f, 120f), status, labelStyle);
                GUI.matrix = old;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] GUI: " + e.Message); }
        }

        static void Randomize()
        {
            var r = new System.Random();
            cur.Gender = r.Next(0, 3); cur.Build = r.Next(0, 4); cur.Eyes = r.Next(0, 4);
            cur.Skin = r.Next(0, Skins.Length); cur.HairCol = r.Next(0, HairCols.Length);
            cur.TopCol = r.Next(0, Cloth.Length); cur.BottomCol = r.Next(0, Cloth.Length); cur.ShoesCol = r.Next(0, Cloth.Length);
            BuildPools();
            Func<List<ClothesPreset>, string> any = l => l.Count == 0 ? "" : l[r.Next(0, l.Count)].name;
            cur.Head = any(heads); cur.Hair = any(hairs); cur.Top = any(tops); cur.Bottom = any(bottoms); cur.Shoes = any(shoes);
            cur.Hat = ""; cur.Glasses = "";
            dirty = true;
        }

        static bool Choice(ref float y, float x, float w, string label, string[] names, ref int v, bool unused)
        {
            GUI.Label(new Rect(x, y, 150f, 34f), label, labelStyle);
            float bw = (w - 160f) / names.Length; bool ch = false;
            for (int i = 0; i < names.Length; i++)
            {
                GUI.color = i == v ? new Color(1f, 0.45f, 0.5f, 1f) : Color.white;
                if (GUI.Button(new Rect(x + 160f + i * bw, y, bw - 4f, 34f), names[i], btnStyle)) { if (v != i) { v = i; ch = true; } }
            }
            GUI.color = Color.white; y += 42f; return ch;
        }

        static bool Swatches(ref float y, float x, float w, string label, Color[] cols, ref int v)
        {
            GUI.Label(new Rect(x, y, 150f, 30f), label, labelStyle);
            float bw = (w - 160f) / cols.Length; bool ch = false;
            for (int i = 0; i < cols.Length; i++)
            {
                var rc = new Rect(x + 160f + i * bw, y, bw - 3f, 28f);
                GUI.color = cols[i];
                GUI.DrawTexture(rc, Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (i == v) GUI.Box(rc, "");
                if (GUI.Button(rc, GUIContent.none, GUIStyle.none)) { if (v != i) { v = i; ch = true; } }
            }
            y += 36f; return ch;
        }

        static bool Pick(ref float y, float x, float w, string label, List<ClothesPreset> pool, Action<string> set, string curName, bool allowNone)
        {
            GUI.Label(new Rect(x, y, 150f, 34f), label, labelStyle);
            int idx = -1; for (int i = 0; i < pool.Count; i++) if (pool[i].name == curName) { idx = i; break; }
            string shown = idx >= 0 ? pool[idx].name : (allowNone ? "(none)" : "-");
            bool ch = false;
            int count = pool.Count + (allowNone ? 1 : 0);
            if (count > 0)
            {
                int pos = allowNone ? idx + 1 : Mathf.Max(0, idx);
                int np = pos;
                if (GUI.Button(new Rect(x + 160f, y, 40f, 34f), "<", btnStyle)) np = (pos + count - 1) % count;
                if (GUI.Button(new Rect(x + w - 40f, y, 40f, 34f), ">", btnStyle)) np = (pos + 1) % count;
                if (np != pos)
                {
                    int pi = allowNone ? np - 1 : np;
                    set(pi < 0 ? "" : pool[pi].name); ch = true;
                }
            }
            GUI.Label(new Rect(x + 206f, y, w - 250f, 34f), shown, labelStyle);
            y += 40f; return ch;
        }
    }

    internal static class Lan
    {
        const int DiscPort = 47777;
        const string Q = "SODCOOP?1", A = "SODCOOP!1";
        internal class Server
        {
            public string Name = "", Seed = "", Build = "", Ip = "", Version = ""; public bool Pw; public int Players, Max = 2, Port; public float Seen;
            public bool Compatible { get { return Version == Plugin.ModVersion; } }
        }
        internal static readonly List<Server> Servers = new List<Server>();
        static UdpClient hostSock, scanSock;
        static float nextScan;

        internal static void StartAdvertise()
        {
            StopAdvertise();
            try
            {
                var u = new UdpClient(AddressFamily.InterNetwork);
                u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                u.Client.Bind(new IPEndPoint(IPAddress.Any, DiscPort));
                u.Client.Blocking = false;
                try { u.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception __e) { Err.Swallow(__e); }
                hostSock = u;
                Plugin.L.LogInfo("[LAN] Сервер виден в локальной сети (UDP " + DiscPort + ")");
            }
            catch (Exception e) { Plugin.L.LogWarning("[LAN] Не удалось открыть порт поиска " + DiscPort + ": " + e.Message); hostSock = null; }
        }

        internal static void StopAdvertise()
        {
            try { if (hostSock != null) hostSock.Close(); } catch (Exception __e) { Err.Swallow(__e); }
            hostSock = null;
        }

        internal static void CloseScan()
        {
            try { if (scanSock != null) scanSock.Close(); } catch (Exception __e) { Err.Swallow(__e); }
            scanSock = null;
        }

        internal static void Scan()
        {
            try
            {
                if (scanSock == null)
                {
                    scanSock = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                    scanSock.EnableBroadcast = true;
                    scanSock.Client.Blocking = false;
                    try { scanSock.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception __e) { Err.Swallow(__e); }
                }
                var data = Encoding.UTF8.GetBytes(Q);
                var targets = new HashSet<string> { "255.255.255.255", "127.0.0.1" };
                try
                {
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                            var ip = ua.Address.GetAddressBytes(); var mk = ua.IPv4Mask.GetAddressBytes();
                            var bc = new byte[4]; for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mk[i]);
                            targets.Add(new IPAddress(bc).ToString());
                        }
                    }
                }
                catch (Exception __e) { Err.Swallow(__e); }
                foreach (var t in targets)
                {
                    try { scanSock.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(t), DiscPort)); } catch (Exception __e) { Err.Swallow(__e); }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[LAN] Scan: " + e.Message); }
        }

        internal static void Tick()
        {
            try { SteamLink.Tick(NetMenu.Open); } catch (Exception e) { Plugin.L.LogWarning("[STEAM] " + e.Message); }
            try
            {
                if (hostSock != null)
                {
                    int guard = 0;
                    while (hostSock.Client.Available > 0 && guard++ < 20)
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        var d = hostSock.Receive(ref ep);
                        if (Encoding.UTF8.GetString(d) != Q) continue;
                        using (var ms = new MemoryStream())
                        {
                            using (var bw = new BinaryWriter(ms))
                            {
                                bw.Write(A); bw.Write(Net.RtName ?? ""); bw.Write(!string.IsNullOrEmpty(Net.RtPassword));
                                bw.Write(Net.PlayerCount); bw.Write(Net.MaxPlayersPublic); bw.Write(Net.RtPort); bw.Write(Net.SeedInfo()); bw.Write(Net.BuildInfo());
                                bw.Write(Plugin.ModVersion);
                            }
                            var rb = ms.ToArray();
                            hostSock.Send(rb, rb.Length, ep);
                        }
                    }
                }
                if (scanSock != null)
                {
                    int guard = 0;
                    while (scanSock.Client.Available > 0 && guard++ < 50)
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        var d = scanSock.Receive(ref ep);
                        try
                        {
                            using (var br = new BinaryReader(new MemoryStream(d)))
                            {
                                if (br.ReadString() != A) continue;
                                var sv = new Server();
                                sv.Name = br.ReadString(); sv.Pw = br.ReadBoolean(); sv.Players = br.ReadInt32(); sv.Max = br.ReadInt32();
                                sv.Port = br.ReadInt32(); sv.Seed = br.ReadString(); sv.Build = br.ReadString();
                                sv.Version = br.BaseStream.Position < br.BaseStream.Length ? br.ReadString() : "";
                                sv.Ip = ep.Address.ToString(); sv.Seen = Time.realtimeSinceStartup;
                                int idx = Servers.FindIndex(x => x.Ip == sv.Ip && x.Port == sv.Port);
                                if (idx >= 0) Servers[idx] = sv; else Servers.Add(sv);
                            }
                        }
                        catch (Exception __e) { Err.Swallow(__e); }
                    }
                    Servers.RemoveAll(x => Time.realtimeSinceStartup - x.Seen > 8f);
                }
                if (NetMenu.Open && Time.realtimeSinceStartup >= nextScan) { nextScan = Time.realtimeSinceStartup + 2.5f; Scan(); }
                if (!NetMenu.Open && scanSock != null) { CloseScan(); Servers.Clear(); }
            }
            catch (SocketException) { }
            catch (Exception e) { Plugin.L.LogWarning("[LAN] Tick: " + e.Message); }
        }
    }

    internal static class MenuAuto
    {
        internal static string Notice = "";
        static string lastNotice = ""; static float noticeAt;

        internal static List<string> SaveNames()
        {
            var l = new List<KeyValuePair<DateTime, string>>();
            try
            {
                foreach (var f in Directory.GetFiles(Net.SaveDir()))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext != ".sod" && ext != ".sodb") || Path.GetFileName(f).StartsWith("SodCoop_sync")) continue;
                    l.Add(new KeyValuePair<DateTime, string>(File.GetLastWriteTimeUtc(f), Path.GetFileName(f)));
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            l.Sort((x, y) => y.Key.CompareTo(x.Key));
            var r = new List<string>(); foreach (var kv in l) r.Add(kv.Value);
            return r;
        }

        static string autoPath = "", autoName = "";
        static int autoStage;
        static float autoAt;

        internal static void LoadSave(string fileName)
        {
            string path = string.IsNullOrEmpty(fileName) ? "" : Path.Combine(Net.SaveDir(), fileName);
            if (path == "" || !File.Exists(path)) { ManualLoad(fileName); return; }
            autoPath = path; autoName = Path.GetFileNameWithoutExtension(fileName);
            autoStage = 1; autoAt = Time.realtimeSinceStartup + 0.5f;
            Notice = "Loading '" + autoName + "'...";
            Plugin.L.LogInfo("[MENU] автозагрузка: " + fileName);
        }

        static void ManualLoad(string fileName)
        {
            autoStage = 0;
            Notice = (fileName ?? "") == ""
                ? "HOST: choose a save with Load Game and press Start. The server starts when the city has loaded."
                : "Save received. Press Load Game, pick '" + Path.GetFileNameWithoutExtension(fileName) + "' and press Start. You connect automatically when the city has loaded.";
            Plugin.L.LogInfo("[MENU] ручная загрузка: " + (fileName ?? "(хост)"));
        }

        static void AutoLoadTick(float now)
        {
            if (autoStage == 0 || now < autoAt) return;
            var mm = MainMenuController.Instance; var rs = RestartSafeController.Instance;
            if (autoStage == 1)
            {
                if (mm == null || rs == null || !mm.mainMenuActive)
                {
                    if (now > autoAt + 30f) { Plugin.L.LogWarning("[MENU] автозагрузка: главное меню так и не открылось"); ManualLoad(Path.GetFileName(autoPath)); }
                    return;
                }
                try
                {
                    rs.newGameLoadCity = false; rs.generateNew = false;
                    rs.loadSaveGame = true;
                    rs.saveStateFileInfo = new Il2CppSystem.IO.FileInfo(autoPath);
                    mm.SetMenuComponent(MainMenuController.Component.loadingCity);
                    autoStage = 2; autoAt = now + 20f;
                    Plugin.L.LogInfo("[MENU] автозагрузка запущена: " + autoPath);
                }
                catch (Exception e) { Plugin.L.LogWarning("[MENU] автозагрузка не удалась: " + e.Message); ManualLoad(Path.GetFileName(autoPath)); }
                return;
            }
            autoStage = 0;
            bool started = false;
            try { started = mm == null || !mm.mainMenuActive || (mm.currentComponent != null && mm.currentComponent.component == MainMenuController.Component.loadingCity); } catch (Exception __e) { Err.Swallow(__e); }
            if (started) { if (Notice.StartsWith("Loading '")) Notice = ""; }
            else { Plugin.L.LogWarning("[MENU] автозагрузка: загрузка не началась"); ManualLoad(Path.GetFileName(autoPath)); }
        }

        static int newStage;
        static float newAt, newUntil;

        internal static void NewGame()
        {
            autoStage = 0;
            newStage = 1; newAt = Time.realtimeSinceStartup + 0.3f; newUntil = newAt + 30f;
            Notice = "HOST: pick a city or generate a new one. The server starts when the city has loaded.";
            Plugin.L.LogInfo("[MENU] новая игра: открываю выбор города в песочнице");
        }

        static void NewGameTick(float now)
        {
            if (newStage == 0 || now < newAt) return;
            var mm = MainMenuController.Instance;
            if (mm == null || !mm.mainMenuActive)
            {
                if (now > newUntil) { newStage = 0; Notice = "HOST: start a New Game. The server starts when the city has loaded."; Plugin.L.LogWarning("[MENU] новая игра: главное меню так и не открылось"); }
                return;
            }
            newStage = 0;
            try { mm.NewGameTypeButton(true); Plugin.L.LogInfo("[MENU] новая игра: открыт выбор города (песочница)"); }
            catch (Exception e) { Notice = "HOST: start a New Game. The server starts when the city has loaded."; Plugin.L.LogWarning("[MENU] новая игра: " + e.Message); }
        }

        internal static void Tick()
        {
            try { AutoLoadTick(Time.realtimeSinceStartup); } catch (Exception e) { autoStage = 0; Plugin.L.LogWarning("[MENU] автозагрузка: " + e.Message); }
            try { NewGameTick(Time.realtimeSinceStartup); } catch (Exception e) { newStage = 0; Plugin.L.LogWarning("[MENU] новая игра: " + e.Message); }
            if (Notice != lastNotice) { lastNotice = Notice; noticeAt = Time.realtimeSinceStartup; }
            if (Notice != "" && Time.realtimeSinceStartup - noticeAt > 120f) { Notice = ""; lastNotice = ""; }
        }
    }

    internal static class UiKit
    {
        internal static bool Ok;
        static Transform menuRoot, mainPanel;
        static GameObject btnTpl, panelTpl;
        static TMPro.TMP_FontAsset font; static Material fontMat;
        static Sprite frame2;
        static bool ru;
        static float initAt, statusAt;
        static GameObject mpBtn, chBtn;
        static TMPro.TextMeshProUGUI statusLabel;
        internal static readonly Color BtnColor = new Color(0.251f, 0.212f, 0.529f, 1f);
        internal static readonly Color SelColor = new Color(0.62f, 0.18f, 0.30f, 1f);
        internal static readonly Color TextColor = new Color(0.835f, 0.816f, 0.906f, 1f);
        internal static readonly Color HeadColor = new Color(1f, 0.643f, 0.718f, 1f);

        internal static string T(string en, string r) { return ru ? r : en; }
        static TMPro.TextMeshProUGUI settingsText;
        static bool IsRu(string s) { foreach (char ch in s ?? "") if (ch >= 'Ѐ' && ch <= 'ӿ') return true; return false; }

        static void LanguageCheck()
        {
            if (settingsText == null) return;
            bool r = IsRu(settingsText.text);
            if (r == ru) return;
            ru = r;
            SetText(mpBtn, T("Multiplayer", "Мультиплеер")); SetText(chBtn, T("Character", "Персонаж"));
            Plugin.L.LogInfo("[UI] язык меню: " + (ru ? "русский" : "английский"));
        }

        static void SetText(GameObject b, string text)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<TMPro.TextMeshProUGUI>(true); if (t != null) t.text = text;
        }
        internal static bool Ru { get { return ru; } }
        internal static bool MainButtons { get { return Ok && mpBtn != null && chBtn != null; } }

        internal static bool Init()
        {
            if (Ok && btnTpl != null && panelTpl != null && menuRoot != null && mainPanel != null) return true;
            Ok = false; mpBtn = null; chBtn = null; statusLabel = null;
            float now = Time.realtimeSinceStartup;
            if (now < initAt) return false;
            initAt = now + 1f;
            try
            {
                var mc = GameObject.Find("MenuCanvas");
                if (mc != null) menuRoot = mc.transform.Find("MainMenu");
                else
                {
                    var mmc = MainMenuController.Instance;
                    menuRoot = mmc != null && mmc.mainMenuContainer != null ? mmc.mainMenuContainer.transform : null;
                }
                if (menuRoot == null) return false;
                mainPanel = menuRoot.Find("MainMenuPanel");
                var settings = mainPanel != null ? mainPanel.Find("Settings") : null;
                var sgp = menuRoot.Find("SaveGamePanel");
                if (settings == null || sgp == null || sgp.Find("Components") == null || sgp.Find("ButtonArea") == null || sgp.Find("Header/PanelTitle") == null) return false;
                btnTpl = settings.gameObject; panelTpl = sgp.gameObject;
                var t = settings.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (t == null) return false;
                font = t.font; fontMat = t.fontSharedMaterial;
                var ci = sgp.Find("Components").GetComponent<UnityEngine.UI.Image>(); frame2 = ci != null ? ci.sprite : null;
                settingsText = t;
                ru = IsRu(t.text);
                Ok = true;
                Plugin.L.LogInfo("[UI] шаблоны меню найдены, язык: " + (ru ? "русский" : "английский"));
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning("[UI] шаблоны меню: " + e.Message); return false; }
        }

        internal static void Tick()
        {
            bool vis = false;
            try { var mm = MainMenuController.Instance; vis = mm != null && mm.mainMenuActive; } catch (Exception __e) { Err.Swallow(__e); }
            try { Net.InviteUiTick(); } catch (Exception e) { Plugin.L.LogWarning("[UI] приглашение: " + e.Message); }
            if (!vis && !NetMenu.Open && !CharEditor.Open && !Net.CheatMenuOpen) return;
            if (!Init()) return;
            if (mpBtn == null || chBtn == null) AddMainButtons();
            JuiceTick();
            float now = Time.realtimeSinceStartup;
            if (now >= statusAt)
            {
                statusAt = now + 0.5f;
                LanguageCheck();
                FitPanel();
                Canvas.ForceUpdateCanvases();
                RefreshJuicePos(mainPanel);
                if (NetMenu.Open || CharEditor.Open) RefreshJuicePos(menuRoot);
                if (statusLabel != null) { string st = Net.StatusLine(); if (statusLabel.text != st) statusLabel.text = st; }
            }
            try { Updater.Start(); Updater.Tick(); } catch (Exception e) { Plugin.L.LogWarning("[UPDATE] " + e.Message); }
            try { MessageTick(); } catch (Exception e) { Plugin.L.LogWarning("[UI] сообщение: " + e.Message); }
            try { Net.CheatMenuUiTick(); } catch (Exception e) { Plugin.L.LogWarning("[UI] читы: " + e.Message); }
            try { Net.ModLeakCheck(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] " + e.Message); }
            try { NetMenu.UiTick(); } catch (Exception e) { Plugin.L.LogWarning("[UI] мультиплеер: " + e.Message); }
            try { CharEditor.UiTick(); } catch (Exception e) { Plugin.L.LogWarning("[UI] персонаж: " + e.Message); }
        }

        static void AddMainButtons()
        {
            try
            {
                if (mpBtn != null) UnityEngine.Object.Destroy(mpBtn);
                if (chBtn != null) UnityEngine.Object.Destroy(chBtn);
                var load = mainPanel.Find("Load Game");
                int idx = load != null ? load.GetSiblingIndex() + 1 : btnTpl.transform.GetSiblingIndex();
                mpBtn = CloneButton(btnTpl, mainPanel, T("Multiplayer", "Мультиплеер"), () => NetMenu.OpenMenu());
                mpBtn.transform.SetSiblingIndex(idx);
                chBtn = CloneButton(btnTpl, mainPanel, T("Character", "Персонаж"), () => CharEditor.OpenEditor());
                chBtn.transform.SetSiblingIndex(idx + 1);
                var go = new GameObject("SodCoop Status"); go.AddComponent<RectTransform>();
                go.transform.SetParent(mainPanel, false);
                go.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 0f); rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -54f); rt.sizeDelta = new Vector2(1400f, 34f);
                statusLabel = MakeText(go, "", 20f, TMPro.TextAlignmentOptions.TopLeft, TextColor);
                FitPanel();
                Plugin.L.LogInfo("[UI] кнопки мультиплеера и персонажа добавлены в главное меню");
            }
            catch (Exception e) { Plugin.L.LogWarning("[UI] кнопки главного меню: " + e.Message); }
        }

        static void FitPanel()
        {
            try
            {
                var rt = mainPanel.GetComponent<RectTransform>(); var vl = mainPanel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                if (rt == null || vl == null) return;
                float need = vl.padding.top + vl.padding.bottom; int n = 0;
                for (int i = 0; i < mainPanel.childCount; i++)
                {
                    var c = mainPanel.GetChild(i); if (!c.gameObject.activeSelf) continue;
                    var le = c.GetComponent<UnityEngine.UI.LayoutElement>(); if (le != null && le.ignoreLayout) continue;
                    var crt = c.GetComponent<RectTransform>(); if (crt == null) continue;
                    need += crt.sizeDelta.y; n++;
                }
                if (n > 1) need += vl.spacing * (n - 1);
                if (need > rt.sizeDelta.y + 0.5f) rt.sizeDelta = new Vector2(rt.sizeDelta.x, need + 2f);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static readonly List<GameObject> juiceFix = new List<GameObject>();

        static void JuiceTick()
        {
            if (juiceFix.Count == 0) return;
            Canvas.ForceUpdateCanvases();
            foreach (var go in juiceFix) FixJuice(go);
            juiceFix.Clear();
        }

        static void FixJuice(GameObject go)
        {
            if (go == null) return;
            var j = go.GetComponent<JuiceController>();
            if (j == null || j.elements == null) return;
            foreach (var e in j.elements)
            {
                if (e == null) continue;
                try
                {
                    if (e.transformElement != null) { e.originalLocalPos = e.transformElement.localPosition; e.originalLocalRot = e.transformElement.localEulerAngles; e.originalLocalScale = e.transformElement.localScale; }
                    if (e.imageElement != null) e.originalColour = e.imageElement.color;
                    e.getNormalTransformAtStart = false; e.getNormalColourAtStart = false;
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
        }

        static void RefreshJuicePos(Transform root)
        {
            if (root == null || !root.gameObject.activeInHierarchy) return;
            foreach (var j in root.GetComponentsInChildren<JuiceController>(false))
            {
                if (j == null || j.elements == null) continue;
                foreach (var e in j.elements)
                {
                    if (e == null || e.transformElement == null) continue;
                    try { e.originalLocalPos = e.transformElement.localPosition; e.getNormalTransformAtStart = false; } catch (Exception __e) { Err.Swallow(__e); }
                }
            }
        }

        internal static void NudgeAll()
        {
            if (menuRoot == null) return;
            int n = 0;
            foreach (var j in menuRoot.GetComponentsInChildren<JuiceController>(false))
                if (j != null && j.GetComponent<ButtonController>() != null) { try { j.Nudge(); n++; } catch (Exception __e) { Err.Swallow(__e); } }
            Plugin.L.LogInfo("[UI] встряхнул кнопок: " + n);
        }

        static string msgTitle, msgText;
        static (string, Action)[] msgButtons;
        static Win msgWin;

        static Func<string> msgLive, msgLivePending;
        static bool msgPending;
        static int msgRestores;
        static float msgRestoreAt;
        static TMPro.TextMeshProUGUI msgLabel;

        internal static void ShowMessage(string title, string text, params (string, Action)[] buttons) { msgTitle = title; msgText = text; msgButtons = buttons; msgLivePending = null; }

        internal static void ShowProgress(string title, Func<string> live, params (string, Action)[] buttons)
        {
            msgTitle = title; msgText = live() ?? ""; msgButtons = buttons; msgLivePending = live;
        }

        static void MessageTick()
        {
            if (msgWin != null && msgWin.Alive && msgWin.Root.activeSelf)
            {
                if (MainPanelShown) { Show(msgWin, false); msgRestoreAt = Time.unscaledTime + 1f; Plugin.L.LogInfo("[UI] меню игры закрыло сообщение, покажу снова"); return; }
                if (msgText == null)
                {
                    if (msgLive != null && msgLabel != null) { string t = msgLive() ?? ""; if (msgLabel.text != t) msgLabel.text = t; }
                    return;
                }
            }
            if (msgText == null && msgPending && msgRestores < 5 && msgWin != null && msgWin.Alive && MainPanelShown && Time.unscaledTime > msgRestoreAt
                && !NetMenu.Open && !CharEditor.Open && !Net.CheatMenuOpen)
            {
                msgRestores++;
                Show(msgWin, true);
                return;
            }
            if (msgText == null || NetMenu.Open || CharEditor.Open || Net.CheatMenuOpen) return;
            if (msgWin == null || !msgWin.Alive) msgWin = MakeWindow("SodCoop MessagePanel", "", 1000f, 480f);
            msgWin.Title.text = msgTitle ?? "";
            Clear(msgWin.Content); Clear(msgWin.Buttons);
            var c = msgWin.Content; float W = c.rect.width > 100f ? c.rect.width : 872f;
            msgLabel = Label(c, msgText, 30f, 30f, W - 60f, 150f, 24f, TMPro.TextAlignmentOptions.Center, Color.white);
            msgLive = msgLivePending; msgLivePending = null;
            var btns = msgButtons != null && msgButtons.Length > 0 ? msgButtons : new[] { ("OK", (Action)null) };
            foreach (var bt in btns)
            {
                var act = bt.Item2;
                BarButton(msgWin, bt.Item1, btns.Length > 2 ? 240f : 260f, () => { msgPending = false; Show(msgWin, false); act?.Invoke(); });
            }
            msgText = null; msgButtons = null;
            msgPending = true; msgRestores = 0;
            Show(msgWin, true);
        }

        internal static GameObject CloneButton(GameObject tpl, Transform parent, string label, Action click)
        {
            var go = UnityEngine.Object.Instantiate(tpl, parent, false);
            go.name = "SodCoop " + label; go.SetActive(true);
            var bc = go.GetComponent<ButtonController>();
            if (bc != null) { bc.useAutomaticText = false; bc.textReference = ""; bc.menuMouseoverReference = ""; bc.interactable = true; bc.baseColour = BtnColor; }
            var img = go.GetComponent<UnityEngine.UI.Image>(); if (img != null) img.color = BtnColor;
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var ch = go.transform.GetChild(i);
                if (ch.name.StartsWith("ButtonAdditionalHighlight")) UnityEngine.Object.Destroy(ch.gameObject);
            }
            var tmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (tmp != null) { tmp.text = label; var c = tmp.color; c.a = 1f; tmp.color = c; }
            var b = go.GetComponent<UnityEngine.UI.Button>();
            if (b != null)
            {
                int lastFrame = -1;
                b.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                b.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    if (Time.frameCount == lastFrame) return;
                    lastFrame = Time.frameCount;
                    try { click(); } catch (Exception e) { Plugin.L.LogWarning("[UI] кнопка '" + label + "': " + e); }
                }));
                b.interactable = true;
            }
            juiceFix.Add(go);
            return go;
        }

        internal sealed class Win
        {
            internal GameObject Root; internal RectTransform Content; internal Transform Buttons; internal TMPro.TextMeshProUGUI Title; internal bool Overlay;
            internal bool Alive { get { return Root != null && Content != null && Buttons != null; } }
        }

        static GameObject overlay;

        static Transform Overlay()
        {
            if (overlay != null) return overlay.transform;
            overlay = new GameObject("SodCoop Overlay");
            UnityEngine.Object.DontDestroyOnLoad(overlay);
            var cv = overlay.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 5000;
            var cs = overlay.AddComponent<UnityEngine.UI.CanvasScaler>();
            cs.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920f, 1080f); cs.matchWidthOrHeight = 0f;
            overlay.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return overlay.transform;
        }

        internal static Win MakeWindow(string name, string title, float w, float h, bool inGame = false)
        {
            var go = UnityEngine.Object.Instantiate(panelTpl, inGame ? Overlay() : menuRoot, false);
            go.name = name; go.SetActive(false);
            var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(0f, inGame ? 0f : -20f);
            if (inGame) { rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); }
            var win = new Win { Root = go, Overlay = inGame };
            var pt = go.transform.Find("Header/PanelTitle");
            var auto = pt.GetComponent<MenuAutoTextController>(); if (auto != null) UnityEngine.Object.DestroyImmediate(auto);
            win.Title = pt.GetComponent<TMPro.TextMeshProUGUI>(); win.Title.text = title;
            var comps = go.transform.Find("Components");
            Clear(comps);
            win.Content = comps.GetComponent<RectTransform>();
            win.Buttons = go.transform.Find("ButtonArea");
            Clear(win.Buttons);
            return win;
        }

        internal static void Show(Win w, bool on)
        {
            if (w == null || w.Root == null) return;
            w.Root.SetActive(on);
            if (!w.Overlay && mainPanel != null) mainPanel.gameObject.SetActive(!on);
        }

        internal static bool MainPanelShown { get { return mainPanel != null && mainPanel.gameObject.activeSelf; } }

        internal static void Clear(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        static RectTransform Place(GameObject go, Transform parent, float x, float y, float w, float h)
        {
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>(); if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static void Stretch(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        static TMPro.TextMeshProUGUI MakeText(GameObject go, string text, float size, TMPro.TextAlignmentOptions al, Color col)
        {
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            t.font = font; t.fontSharedMaterial = fontMat; t.fontSize = size; t.color = col; t.alignment = al;
            t.overflowMode = TMPro.TextOverflowModes.Ellipsis; t.raycastTarget = false; t.text = text ?? "";
            return t;
        }

        internal static TMPro.TextMeshProUGUI Label(Transform p, string text, float x, float y, float w, float h, float size = 22f, TMPro.TextAlignmentOptions al = TMPro.TextAlignmentOptions.MidlineLeft, Color? col = null)
        {
            var go = new GameObject("Label"); go.AddComponent<RectTransform>();
            Place(go, p, x, y, w, h);
            return MakeText(go, text, size, al, col ?? TextColor);
        }

        internal static GameObject Button(Transform p, string label, float x, float y, float w, float h, Action click, float size = 22f)
        {
            var go = CloneButton(btnTpl, p, label, click);
            var le = go.GetComponent<UnityEngine.UI.LayoutElement>(); if (le != null) le.ignoreLayout = true;
            Place(go, p, x, y, w, h);
            var tmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.enableAutoSizing = false; tmp.fontSize = size;
                tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                var trt = tmp.GetComponent<RectTransform>();
                if (trt != null) { trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(4f, 0f); trt.offsetMax = new Vector2(-4f, 0f); }
            }
            return go;
        }

        internal static GameObject BarButton(Win w, string label, float width, Action click, bool enabled = true)
        {
            var go = CloneButton(btnTpl, w.Buttons, label, click);
            var le = go.GetComponent<UnityEngine.UI.LayoutElement>(); if (le == null) le = go.AddComponent<UnityEngine.UI.LayoutElement>();
            le.ignoreLayout = false; le.preferredWidth = width; le.minWidth = width;
            var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(width, 52f);
            if (!enabled) SetEnabled(go, false);
            return go;
        }

        internal static void SetSelected(GameObject btn, bool sel)
        {
            if (btn == null) return;
            var c = sel ? SelColor : BtnColor;
            var img = btn.GetComponent<UnityEngine.UI.Image>(); if (img != null) img.color = c;
            var bc = btn.GetComponent<ButtonController>(); if (bc != null) bc.baseColour = c;
            if (!juiceFix.Contains(btn)) juiceFix.Add(btn);
        }

        internal static void SetEnabled(GameObject btn, bool on)
        {
            if (btn == null) return;
            var b = btn.GetComponent<UnityEngine.UI.Button>(); if (b != null) b.interactable = on;
            var bc = btn.GetComponent<ButtonController>(); if (bc != null) bc.interactable = on;
            var tmp = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (tmp != null) { var c = tmp.color; c.a = on ? 1f : 0.25f; tmp.color = c; }
        }

        internal static TMPro.TMP_InputField Input(Transform p, float x, float y, float w, float h, string value, int max, bool password)
        {
            var go = new GameObject("Input"); go.SetActive(false); go.AddComponent<RectTransform>();
            Place(go, p, x, y, w, h);
            var bg = go.AddComponent<UnityEngine.UI.Image>(); bg.sprite = frame2; bg.type = UnityEngine.UI.Image.Type.Sliced; bg.color = new Color(0.10f, 0.08f, 0.22f, 0.95f);
            var tb = btnTpl.transform.Find("Border"); var tbi = tb != null ? tb.GetComponent<UnityEngine.UI.Image>() : null;
            if (tbi != null)
            {
                var bo = new GameObject("Border"); bo.AddComponent<RectTransform>(); bo.transform.SetParent(go.transform, false);
                Stretch(bo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
                var bi = bo.AddComponent<UnityEngine.UI.Image>(); bi.sprite = tbi.sprite; bi.type = tbi.type; bi.material = tbi.material; bi.color = tbi.color; bi.raycastTarget = false;
            }
            var area = new GameObject("Text Area"); area.AddComponent<RectTransform>(); area.transform.SetParent(go.transform, false);
            var art = area.GetComponent<RectTransform>(); Stretch(art, 12f, 4f, 12f, 4f);
            area.AddComponent<UnityEngine.UI.RectMask2D>();
            var tgo = new GameObject("Text"); tgo.AddComponent<RectTransform>(); tgo.transform.SetParent(area.transform, false);
            Stretch(tgo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            var txt = MakeText(tgo, "", 22f, TMPro.TextAlignmentOptions.MidlineLeft, Color.white);
            txt.overflowMode = TMPro.TextOverflowModes.Overflow;
            var f = go.AddComponent<TMPro.TMP_InputField>();
            f.targetGraphic = bg; f.textViewport = art; f.textComponent = txt;
            f.characterLimit = max; f.richText = false;
            if (password) { f.contentType = TMPro.TMP_InputField.ContentType.Password; f.inputType = TMPro.TMP_InputField.InputType.Password; }
            f.customCaretColor = true; f.caretColor = Color.white; f.selectionColor = new Color(0.858f, 0.312f, 0.384f, 0.5f);
            go.SetActive(true);
            f.text = value ?? "";
            return f;
        }

        internal static GameObject Swatch(Transform p, float x, float y, float w, float h, Color c, bool sel, Action click)
        {
            var go = Button(p, "", x, y, w, h, click);
            var tone = new GameObject("Tone"); tone.AddComponent<RectTransform>(); tone.transform.SetParent(go.transform, false);
            Stretch(tone.GetComponent<RectTransform>(), 7f, 7f, 7f, 7f);
            var img = tone.AddComponent<UnityEngine.UI.Image>(); img.color = c; img.raycastTarget = false;
            SetSelected(go, sel);
            return go;
        }

        internal static UnityEngine.UI.RawImage Raw(Transform p, float x, float y, float w, float h, Texture tex)
        {
            var go = new GameObject("Preview"); go.AddComponent<RectTransform>();
            Place(go, p, x, y, w, h);
            var ri = go.AddComponent<UnityEngine.UI.RawImage>(); ri.texture = tex; ri.raycastTarget = true;
            return ri;
        }
    }

    internal static class NetMenu
    {
        internal static bool Open;
        static int page;
        static GameObject menuCanvas;
        static string fName = "", fPass = "", fPort = "9050", fAddr = "127.0.0.1", fAPort = "9050", fAPass = "";
        static string selIp = ""; static int selPort; static string msg = "";
        static GUIStyle btn, lab, title, field, rowBtn;

        static UiKit.Win win;
        static string builtSig = "";
        static TMPro.TextMeshProUGUI uiStatus, uiMsg;
        static TMPro.TMP_InputField inName, inPass, inPort, inAddr, inAPort, inAPass;
        static float uiAt;

        static bool UiOn { get { return win != null && win.Alive; } }

        static void UiOpen()
        {
            if (!UiKit.Init()) return;
            if (!UiOn) win = UiKit.MakeWindow("SodCoop MultiplayerPanel", "", 1500f, 860f);
            builtSig = "";
            UiKit.Show(win, true);
        }

        static void UiClose() { if (UiOn) UiKit.Show(win, false); }

        static void Harvest()
        {
            if (inName != null) fName = inName.text; if (inPass != null) fPass = inPass.text; if (inPort != null) fPort = inPort.text;
            if (inAddr != null) fAddr = inAddr.text; if (inAPort != null) fAPort = inAPort.text; if (inAPass != null) fAPass = inAPass.text;
            float fv; int iv;
            if (inRDown != null && ParseF(inRDown.text, out fv)) Plugin.CfgDownedSeconds.Value = Mathf.Clamp(fv, 1f, 600f);
            if (inRHold != null && ParseF(inRHold.text, out fv)) Plugin.CfgReviveHoldSeconds.Value = Mathf.Clamp(fv, 0.5f, 60f);
            if (inRHealth != null && ParseF(inRHealth.text, out fv)) Plugin.CfgReviveHealth.Value = Mathf.Clamp(fv / 100f, 0.05f, 1f);
            if (inRHosp != null && ParseF(inRHosp.text, out fv)) Plugin.CfgHospitalSeconds.Value = Mathf.Clamp(fv, 0f, 3600f);
            if (inRMoney != null && int.TryParse(inRMoney.text, out iv)) Plugin.CfgNewPlayerMoney.Value = Math.Max(-1, iv);
            if (inRPicks != null && int.TryParse(inRPicks.text, out iv)) Plugin.CfgNewPlayerLockpicks.Value = Math.Max(-1, iv);
        }

        static string Sig()
        {
            var sb = new StringBuilder();
            sb.Append(page).Append('|').Append(selIp).Append(':').Append(selPort).Append('|').Append(hostSel).Append('|').Append(hostTop).Append('|').Append(hostSaves.Count);
            if (page == 0 || page == 3) foreach (var s in Lan.Servers) sb.Append('|').Append(s.Ip).Append(':').Append(s.Port).Append(s.Name).Append(s.Players).Append(s.Seed).Append(s.Version);
            return sb.ToString();
        }

        internal static void UiTick()
        {
            if (!Open || !UiOn) return;
            if (UiKit.MainPanelShown) { CloseMenu(); return; }
            string sig = Sig();
            if (sig != builtSig) { Harvest(); Rebuild(); builtSig = sig; }
            if (Time.realtimeSinceStartup >= uiAt)
            {
                uiAt = Time.realtimeSinceStartup + 0.3f;
                string st = Net.StatusLine();
                if (uiStatus != null) uiStatus.text = st != "" ? UiKit.T("Now: ", "Сейчас: ") + st : "";
                if (uiMsg != null) uiMsg.text = msg ?? "";
            }
        }

        static void Rebuild()
        {
            var c = win.Content; UiKit.Clear(c); UiKit.Clear(win.Buttons);
            inName = inPass = inPort = inAddr = inAPort = inAPass = null;
            inRDown = inRHold = inRHealth = inRHosp = inRMoney = inRPicks = null;
            win.Title.text = page == 5 ? UiKit.T("Game modifiers", "Модификаторы игры") : page == 4 ? UiKit.T("Server rules", "Правила сервера") : page == 1 ? UiKit.T("Host a game", "Создать игру") : page == 2 ? UiKit.T("Direct connect", "Подключение по адресу") : page == 3 ? UiKit.T("Password", "Пароль") : UiKit.T("Multiplayer", "Мультиплеер");
            float W = c.rect.width, H = c.rect.height;
            if (W < 100f) { W = 1372f; H = 604f; }
            if (page == 0) UiList(c, W);
            else if (page == 1) UiHost(c, W);
            else if (page == 2) UiDirect(c);
            else if (page == 3) UiPass(c);
            else if (page == 5) UiMods(c, W);
            else UiRules(c, W);
            uiMsg = UiKit.Label(c, msg ?? "", 24f, H - 84f, W - 48f, 36f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor);
            uiStatus = UiKit.Label(c, "", 24f, H - 48f, W - 48f, 36f, 20f);
            uiAt = 0f;
        }

        static void UiList(Transform c, float W)
        {
            float y = 18f;
            UiKit.Label(c, UiKit.T("Server", "Сервер"), 36f, y, 500f, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor);
            UiKit.Label(c, UiKit.T("Players", "Игроки"), 560f, y, 160f, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor);
            UiKit.Label(c, UiKit.T("Address", "Адрес"), 740f, y, 300f, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor);
            UiKit.Label(c, UiKit.T("City seed", "Сид города"), 1060f, y, 300f, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor);
            y += 44f;
            var list = Lan.Servers;
            if (list.Count == 0)
                UiKit.Label(c, UiKit.T("No servers found yet. The host must have loaded a city. Use Direct connect to type an address.",
                    "Серверов пока нет. Хост должен загрузить город. Можно подключиться по адресу."), 36f, y + 6f, W - 72f, 40f, 22f);
            for (int i = 0; i < list.Count && i < 8; i++)
            {
                var sv = list[i];
                string ip = sv.Ip; int port = sv.Port;
                var row = UiKit.Button(c, "", 24f, y, W - 48f, 50f, () => { selIp = ip; selPort = port; });
                UiKit.SetSelected(row, sv.Ip == selIp && sv.Port == selPort);
                string nm = (sv.Pw ? "[" + UiKit.T("password", "пароль") + "] " : "") + sv.Name;
                if (!sv.Compatible) nm += "  (" + UiKit.T("mod ", "мод ") + (sv.Version != "" ? sv.Version : UiKit.T("older than 0.12.0", "старее 0.12.0")) + UiKit.T(", incompatible)", ", несовместим)");
                UiKit.Label(row.transform, nm, 12f, 0f, 510f, 50f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, sv.Compatible ? Color.white : UiKit.HeadColor);
                UiKit.Label(row.transform, sv.Players + " / " + sv.Max, 536f, 0f, 160f, 50f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, Color.white);
                UiKit.Label(row.transform, sv.Port == 0 ? "Steam" : sv.Ip + ":" + sv.Port, 716f, 0f, 300f, 50f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, Color.white);
                UiKit.Label(row.transform, sv.Seed, 1036f, 0f, W - 1100f, 50f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, Color.white);
                y += 56f;
            }
            Lan.Server sel = null;
            foreach (var x in list) if (x.Ip == selIp && x.Port == selPort) sel = x;
            UiKit.BarButton(win, UiKit.T("Host", "Создать"), 160f, () => { page = 1; msg = ""; hostSaves = MenuAuto.SaveNames(); hostSel = 0; hostTop = 0; });
            UiKit.BarButton(win, UiKit.T("Connect", "Подключиться"), 200f, () => ConnectTo(sel), sel != null && sel.Compatible);
            UiKit.BarButton(win, UiKit.T("Direct", "По адресу"), 170f, () => { page = 2; msg = ""; });
            if (SteamLink.HostingViaSteam) UiKit.BarButton(win, UiKit.T("Invite (Steam)", "Пригласить (Steam)"), 240f, () => { CloseMenu(); SteamLink.InviteFriends(); });
            UiKit.BarButton(win, UiKit.T("Refresh", "Обновить"), 160f, () => { Lan.Servers.Clear(); Lan.Scan(); builtSig = ""; });
            UiKit.BarButton(win, UiKit.T("Disable", "Выключить"), 170f, () => { Net.ApplyOff(); msg = UiKit.T("Multiplayer disabled.", "Мультиплеер выключен."); });
            UiKit.BarButton(win, UiKit.T("Collect logs", "Собрать логи"), 190f, () => { CloseMenu(); LogPack.Collect(); });
            UiKit.BarButton(win, UiKit.T("Back", "Назад"), 140f, () => CloseMenu());
        }

        internal static void DevPage(int p) { if (p == 1) { hostSaves = MenuAuto.SaveNames(); hostSel = 0; hostTop = 0; } page = p; }

        static TMPro.TMP_InputField inRDown, inRHold, inRHealth, inRHosp, inRMoney, inRPicks;

        static bool ParseF(string s, out float v)
        {
            return float.TryParse((s ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        static void RuleToggle(Transform c, float x, ref float y, float w, string label, BepInEx.Configuration.ConfigEntry<bool> e)
        {
            bool on = e.Value;
            var b = UiKit.Button(c, label + ": " + (on ? UiKit.T("on", "вкл") : UiKit.T("off", "выкл")), x, y, w, 46f, () => { Harvest(); e.Value = !e.Value; builtSig = ""; }, 20f);
            UiKit.SetSelected(b, on);
            y += 52f;
        }

        static TMPro.TMP_InputField RuleInput(Transform c, float x, ref float y, float w, string label, string value)
        {
            UiKit.Label(c, label, x, y, w - 140f, 44f, 20f);
            var f = UiKit.Input(c, x + w - 130f, y, 130f, 44f, value, 6, false);
            y += 50f;
            return f;
        }

        static void UiRules(Transform c, float W)
        {
            float x = 36f, y = 16f, bw = 600f;
            UiKit.Label(c, UiKit.T("Players and game", "Игроки и игра"), x, y, bw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); y += 40f;
            UiKit.Label(c, UiKit.T("Max players", "Игроков максимум"), x, y, 300f, 46f, 20f);
            int mp = Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4);
            UiKit.Button(c, "<", x + bw - 182f, y, 52f, 46f, () => { Harvest(); Plugin.CfgMaxPlayers.Value = Mathf.Clamp(mp - 1, 2, 4); builtSig = ""; });
            UiKit.Label(c, mp.ToString(), x + bw - 124f, y, 66f, 46f, 24f, TMPro.TextAlignmentOptions.Midline, Color.white);
            UiKit.Button(c, ">", x + bw - 52f, y, 52f, 46f, () => { Harvest(); Plugin.CfgMaxPlayers.Value = Mathf.Clamp(mp + 1, 2, 4); builtSig = ""; });
            y += 52f;
            RuleToggle(c, x, ref y, bw, UiKit.T("Players can hurt each other", "Игроки могут ранить друг друга"), Plugin.CfgPvp);
            RuleToggle(c, x, ref y, bw, UiKit.T("The world never pauses", "Мир не встаёт на паузу"), Plugin.CfgWorldNeverPauses);
            RuleToggle(c, x, ref y, bw, UiKit.T("Players may use cheats", "Игрокам разрешены читы"), Plugin.CfgClientCheats);
            y += 6f;
            UiKit.Label(c, UiKit.T("Job and case reward", "Награда за задания и дела"), x, y, bw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); y += 40f;
            bool only = Plugin.CfgRewardOnlyHandIn.Value;
            var b1 = UiKit.Button(c, UiKit.T("Everyone", "Всем игрокам"), x, y, (bw - 10f) / 2f, 46f, () => { Harvest(); Plugin.CfgRewardOnlyHandIn.Value = false; builtSig = ""; }, 20f);
            var b2 = UiKit.Button(c, UiKit.T("Who hands in", "Только сдавшему"), x + (bw - 10f) / 2f + 10f, y, (bw - 10f) / 2f, 46f, () => { Harvest(); Plugin.CfgRewardOnlyHandIn.Value = true; builtSig = ""; }, 20f);
            UiKit.SetSelected(b1, !only); UiKit.SetSelected(b2, only);

            float rx = 700f, ry = 16f, rw = W - rx - 36f;
            UiKit.Label(c, UiKit.T("Downed and hospital", "Ранение и больница"), rx, ry, rw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); ry += 40f;
            RuleToggle(c, rx, ref ry, rw, UiKit.T("A downed player can be revived", "Упавшего можно поднять"), Plugin.CfgDowned);
            inRDown = RuleInput(c, rx, ref ry, rw, UiKit.T("Wait for help, s", "Ждать помощи, с"), Plugin.CfgDownedSeconds.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            inRHold = RuleInput(c, rx, ref ry, rw, UiKit.T("Hold the key to revive, s", "Держать клавишу для подъёма, с"), Plugin.CfgReviveHoldSeconds.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            inRHealth = RuleInput(c, rx, ref ry, rw, UiKit.T("Health after revive, %", "Здоровье после подъёма, %"), (Plugin.CfgReviveHealth.Value * 100f).ToString("0"));
            inRHosp = RuleInput(c, rx, ref ry, rw, UiKit.T("Time in hospital, s", "Время в больнице, с"), Plugin.CfgHospitalSeconds.Value.ToString("0"));
            ry += 6f;
            UiKit.Label(c, UiKit.T("New player", "Новый игрок"), rx, ry, rw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); ry += 40f;
            inRMoney = RuleInput(c, rx, ref ry, rw, UiKit.T("Starting money", "Стартовые деньги"), Plugin.CfgNewPlayerMoney.Value.ToString());
            inRPicks = RuleInput(c, rx, ref ry, rw, UiKit.T("Starting lockpicks", "Стартовые отмычки"), Plugin.CfgNewPlayerLockpicks.Value.ToString());
            RuleToggle(c, rx, ref ry, rw, UiKit.T("Start with an empty inventory", "Начинать с пустым инвентарём"), Plugin.CfgNewPlayerClean);

            UiKit.BarButton(win, UiKit.T("Back", "Назад"), 260f, () => { Harvest(); page = 1; msg = ""; });
        }

        static readonly string[][] ModNames =
        {
            new[] { "mod_filmNoir", "Film Noir", "Нуар" },
            new[] { "mod_shortSighted", "Short-Sighted", "Близорукость" },
            new[] { "mod_gamblingDebt", "Gambling Debt", "Долг за азартные игры" },
            new[] { "mod_houseArrest", "House Arrest", "Домашний арест" },
            new[] { "mod_fameAndFortune", "Fame & Fortune", "Слава и богатство" },
            new[] { "mod_snailNemesis", "Snail Nemesis", "Улитка-немезида" },
            new[] { "mod_ratDetective", "Rat Detective", "Крысиный детектив" },
            new[] { "mod_ironman", "Ironman", "Железный человек" },
            new[] { "permaDeath", "Permadeath", "Перманентная смерть" },
        };

        static void UiMods(Transform c, float W)
        {
            float x = 36f, y = 16f, bw = (W - 108f) / 2f;
            UiKit.Label(c, UiKit.T("These modifiers are set in the game settings when the server starts, for a new game and for a loaded save. Every player gets the host's modifiers.",
                "Эти модификаторы ставятся в настройки игры при запуске сервера - и для новой игры, и для загрузки сейва. У всех игроков будут модификаторы хоста."), x, y, W - 72f, 70f, 20f, TMPro.TextAlignmentOptions.TopLeft); y += 80f;
            float y0 = y;
            for (int i = 0; i < ModNames.Length; i++)
            {
                string id = ModNames[i][0];
                bool on = Net.HostModPick(id);
                float cx = i < 5 ? x : x + bw + 36f;
                if (i == 5) y = y0;
                var b = UiKit.Button(c, UiKit.T(ModNames[i][1], ModNames[i][2]) + ": " + (on ? UiKit.T("on", "вкл") : UiKit.T("off", "выкл")), cx, y, bw, 46f, () => { Harvest(); Net.SetHostModPick(id, !on); builtSig = ""; }, 20f);
                UiKit.SetSelected(b, on);
                y += 52f;
            }
            UiKit.BarButton(win, UiKit.T("Back", "Назад"), 260f, () => { Harvest(); page = 1; msg = ""; });
        }

        internal static void JoinSteam(Lan.Server sv)
        {
            if (!sv.Compatible)
            {
                Net.Toast(UiKit.T($"Can't join: the host has mod version {sv.Version}, you have {Plugin.ModVersion}.", $"Не получится подключиться: у хоста мод {sv.Version}, у тебя {Plugin.ModVersion}."));
                return;
            }
            Net.ShowMainMenu();
            Plugin.L.LogInfo($"[STEAM] подключаюсь по приглашению к {sv.Ip}{(sv.Pw ? ", нужен пароль" : "")}");
            if (sv.Pw)
            {
                OpenMenu();
                Lan.Servers.RemoveAll(x => x.Ip == sv.Ip); Lan.Servers.Add(sv);
                selIp = sv.Ip; selPort = sv.Port; fAPass = ""; page = 3; msg = ""; builtSig = "";
                return;
            }
            Net.ApplyClient(sv.Ip, sv.Port, ""); Net.StartTransfer();
            if (Open) Done("");
        }

        static void ConnectTo(Lan.Server sv)
        {
            if (sv == null) return;
            if (sv.Pw) { fAPass = ""; page = 3; msg = ""; }
            else { Net.ApplyClient(sv.Ip, sv.Port, ""); Net.StartTransfer(); Done(""); }
        }

        static void UiHost(Transform c, float W)
        {
            float x = 36f, y = 16f;
            UiKit.Label(c, UiKit.T("Server name", "Название сервера"), x, y, 560f, 32f); y += 34f;
            inName = UiKit.Input(c, x, y, 560f, 46f, fName, 32, false); y += 62f;
            UiKit.Label(c, UiKit.T("Password (empty = no password)", "Пароль (пусто = без пароля)"), x, y, 560f, 32f); y += 34f;
            inPass = UiKit.Input(c, x, y, 560f, 46f, fPass, 32, true); y += 62f;
            UiKit.Label(c, UiKit.T("Port", "Порт"), x, y, 200f, 32f);
            UiKit.Label(c, UiKit.T("Players connect via", "Как подключаться"), x + 220f, y, 380f, 32f); y += 34f;
            inPort = UiKit.Input(c, x, y, 200f, 46f, fPort, 5, false);
            bool steamOk = SteamLink.Ready, viaSteam = steamOk && Plugin.CfgSteam.Value;
            var bSteam = UiKit.Button(c, "Steam", x + 220f, y, 185f, 46f, () =>
            {
                Harvest();
                if (!SteamLink.Ready) { msg = UiKit.T("Steam is not running: start the game through Steam.", "Steam не запущен: запусти игру через Steam."); return; }
                Plugin.CfgSteam.Value = true; builtSig = "";
            }, 20f);
            var bLan = UiKit.Button(c, UiKit.T("LAN / IP", "LAN / IP"), x + 415f, y, 185f, 46f, () => { Harvest(); Plugin.CfgSteam.Value = false; builtSig = ""; }, 20f);
            UiKit.SetSelected(bSteam, viaSteam); UiKit.SetSelected(bLan, !viaSteam);
            y += 62f;
            UiKit.Label(c, UiKit.T("Max players: ", "Игроков максимум: ") + Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4) + UiKit.T(". Rules: ", ". Правила: ") + Rules.Summary(), x, y, 600f, 64f, 18f, TMPro.TextAlignmentOptions.TopLeft); y += 66f;
            UiKit.Label(c, UiKit.T("Load save starts the selected save at once. New game: set it up yourself. The server starts when the city has loaded.",
                "«Загрузить сейв» сразу запускает выбранный сейв. «Новая игра» - настрой её сам. Сервер запустится, когда город загрузится."), x, y, 600f, 90f, 20f, TMPro.TextAlignmentOptions.TopLeft);

            float lx = 680f, ly = 16f; const int per = 8;
            UiKit.Label(c, hostSaves.Count > 0 ? UiKit.T("Save to load", "Какой сейв загрузить") : UiKit.T("No saves found", "Сейвов нет"), lx, ly, W - lx - 36f, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); ly += 40f;
            hostTop = Mathf.Clamp(hostTop, 0, Mathf.Max(0, hostSaves.Count - per));
            for (int i = hostTop; i < hostSaves.Count && i < hostTop + per; i++)
            {
                int k = i;
                var row = UiKit.Button(c, Path.GetFileNameWithoutExtension(hostSaves[i]), lx, ly, W - lx - 36f, 46f, () => hostSel = k);
                var t = row.GetComponentInChildren<TMPro.TextMeshProUGUI>(true); if (t != null) t.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                UiKit.SetSelected(row, i == hostSel);
                ly += 52f;
            }
            if (hostSaves.Count > per)
            {
                UiKit.Button(c, UiKit.T("Up", "Выше"), lx, ly, 160f, 44f, () => hostTop -= per);
                UiKit.Button(c, UiKit.T("Down", "Ниже"), lx + 172f, ly, 160f, 44f, () => hostTop += per);
            }
            UiKit.BarButton(win, UiKit.T("Server rules...", "Правила сервера..."), 260f, () => { Harvest(); page = 4; msg = ""; });
            UiKit.BarButton(win, UiKit.T("Modifiers...", "Модификаторы..."), 250f, () => { Harvest(); page = 5; msg = ""; });
            UiKit.BarButton(win, UiKit.T("Host: load save", "Загрузить сейв"), 270f, () => { Harvest(); StartHost(false); }, hostSaves.Count > 0);
            UiKit.BarButton(win, UiKit.T("Host: new game", "Новая игра"), 250f, () => { Harvest(); StartHost(true); });
            UiKit.BarButton(win, UiKit.T("Back", "Назад"), 200f, () => { Harvest(); page = 0; msg = ""; });
        }

        static void UiDirect(Transform c)
        {
            float x = 36f, y = 16f;
            UiKit.Label(c, UiKit.T("Host IP address", "IP-адрес хоста"), x, y, 560f, 32f); y += 34f;
            inAddr = UiKit.Input(c, x, y, 460f, 46f, fAddr, 40, false); y += 62f;
            UiKit.Label(c, UiKit.T("Port", "Порт"), x, y, 560f, 32f); y += 34f;
            inAPort = UiKit.Input(c, x, y, 200f, 46f, fAPort, 5, false); y += 62f;
            UiKit.Label(c, UiKit.T("Password (if the server has one)", "Пароль (если есть)"), x, y, 560f, 32f); y += 34f;
            inAPass = UiKit.Input(c, x, y, 560f, 46f, fAPass, 32, true);
            UiKit.BarButton(win, UiKit.T("Connect", "Подключиться"), 300f, () =>
            {
                Harvest(); int p;
                if (string.IsNullOrWhiteSpace(fAddr)) msg = UiKit.T("Enter the host IP address.", "Введи IP-адрес хоста.");
                else if (!Port(fAPort, out p)) msg = UiKit.T("Port must be a number from 1024 to 65535.", "Порт - число от 1024 до 65535.");
                else { Net.ApplyClient(fAddr.Trim(), p, fAPass ?? ""); Net.StartTransfer(); Done(""); }
            });
            UiKit.BarButton(win, UiKit.T("Back", "Назад"), 240f, () => { Harvest(); page = 0; msg = ""; });
        }

        static void UiPass(Transform c)
        {
            Lan.Server sv = null;
            foreach (var x in Lan.Servers) if (x.Ip == selIp && x.Port == selPort) sv = x;
            float px = 36f, y = 16f;
            UiKit.Label(c, sv != null ? UiKit.T("Server: ", "Сервер: ") + sv.Name + (sv.Port == 0 ? "" : "  (" + sv.Ip + ":" + sv.Port + ")") : UiKit.T("Server is gone", "Сервер пропал"), px, y, 900f, 32f); y += 50f;
            UiKit.Label(c, UiKit.T("Password", "Пароль"), px, y, 560f, 32f); y += 34f;
            inAPass = UiKit.Input(c, px, y, 560f, 46f, fAPass, 32, true);
            UiKit.BarButton(win, UiKit.T("Connect", "Подключиться"), 300f, () =>
            {
                Harvest();
                if (sv == null) return;
                Net.ApplyClient(sv.Ip, sv.Port, fAPass ?? ""); Net.StartTransfer(); Done("");
            }, sv != null);
            UiKit.BarButton(win, UiKit.T("Back", "Назад"), 240f, () => { Harvest(); page = 0; msg = ""; });
        }

        static void Styles()
        {
            if (btn != null) return;
            btn = new GUIStyle(GUI.skin.button) { fontSize = 20 };
            lab = new GUIStyle(GUI.skin.label) { fontSize = 20 };
            lab.normal.textColor = new Color(0.95f, 0.85f, 0.9f, 1f);
            title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.95f, 0.3f, 0.35f, 1f);
            field = new GUIStyle(GUI.skin.textField) { fontSize = 22 };
            rowBtn = new GUIStyle(GUI.skin.button) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
        }

        static bool MenuVisible()
        {
            try { var mm = MainMenuController.Instance; return mm != null && mm.mainMenuActive; } catch (Exception) { return false; }
        }

        internal static void OpenMenu()
        {
            Net.EnsureRt(); MenuAuto.Notice = "";
            Open = true; page = 0; msg = ""; selIp = ""; selPort = 0;
            fName = Net.RtName; fPass = Net.RtPassword; fPort = "" + Net.RtPort; fAddr = Net.RtAddress; fAPort = "" + Net.RtPort; fAPass = "";
            if (UiKit.Init()) UiOpen();
            else { try { menuCanvas = GameObject.Find("MenuCanvas"); if (menuCanvas != null) menuCanvas.SetActive(false); } catch (Exception __e) { Err.Swallow(__e); } }
            Lan.Servers.Clear(); Lan.Scan();
        }

        internal static void CloseMenu()
        {
            Open = false;
            UiClose();
            try { if (menuCanvas != null) menuCanvas.SetActive(true); } catch (Exception __e) { Err.Swallow(__e); }
            menuCanvas = null;
        }

        static bool Port(string t, out int p) { return int.TryParse(t, out p) && p >= 1024 && p <= 65535; }

        internal static void OnGui()
        {
            try
            {
                if (CharEditor.Open) return;
                Styles();
                float s = Screen.height / 1080f;
                var old = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
                if (Open && UiOn) { GUI.matrix = old; return; }
                if (!Open)
                {
                    if (MenuVisible() && !UiKit.MainButtons)
                    {
                        if (GUI.Button(new Rect(585f, 940f, 297f, 48f), "MULTIPLAYER", btn)) OpenMenu();
                        string st = Net.StatusLine();
                        if (st != "") GUI.Label(new Rect(278f, 994f, 1400f, 34f), st, lab);
                    }
                    GUI.matrix = old; return;
                }
                float W = Screen.width / s, H = Screen.height / s;
                GUI.color = Color.black; GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(new Rect(40f, 20f, 900f, 50f), page == 1 ? "HOST A GAME" : page == 2 ? "DIRECT CONNECT" : page == 3 ? "PASSWORD" : "MULTIPLAYER", title);
                if (page == 0) Page0(W, H);
                else if (page == 1) PageHost();
                else if (page == 2) PageDirect();
                else PagePass();
                string cur = Net.StatusLine();
                if (cur != "") GUI.Label(new Rect(40f, H - 100f, W - 80f, 34f), "Now: " + cur, lab);
                if (msg != "") GUI.Label(new Rect(40f, H - 140f, W - 80f, 34f), msg, lab);
                GUI.matrix = old;
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void Page0(float W, float H)
        {
            GUI.enabled = true;
            GUI.Button(new Rect(40f, 90f, 140f, 40f), "LAN", btn);
            GUI.enabled = false; GUI.Button(new Rect(190f, 90f, 160f, 40f), "INTERNET", btn); GUI.enabled = true;

            float y = 150f;
            GUI.Label(new Rect(50f, y, 420f, 30f), "SERVER", lab);
            GUI.Label(new Rect(480f, y, 160f, 30f), "PLAYERS", lab);
            GUI.Label(new Rect(640f, y, 300f, 30f), "ADDRESS", lab);
            GUI.Label(new Rect(950f, y, 400f, 30f), "CITY SEED", lab);
            y += 36f;
            var list = Lan.Servers;
            if (list.Count == 0) GUI.Label(new Rect(50f, y, 1200f, 34f), "No servers found yet. The host must have loaded a city. Use DIRECT to type an address.", lab);
            for (int i = 0; i < list.Count && i < 12; i++)
            {
                var sv = list[i];
                bool sel = sv.Ip == selIp && sv.Port == selPort;
                GUI.color = sel ? new Color(1f, 0.55f, 0.55f, 1f) : Color.white;
                if (GUI.Button(new Rect(40f, y, 1340f, 40f), "", rowBtn)) { selIp = sv.Ip; selPort = sv.Port; }
                GUI.color = Color.white;
                GUI.Label(new Rect(50f, y + 4f, 420f, 32f), (sv.Pw ? "[lock] " : "") + sv.Name, lab);
                GUI.Label(new Rect(480f, y + 4f, 160f, 32f), sv.Players + " / " + sv.Max, lab);
                GUI.Label(new Rect(640f, y + 4f, 300f, 32f), sv.Ip + ":" + sv.Port, lab);
                GUI.Label(new Rect(950f, y + 4f, 420f, 32f), sv.Seed, lab);
                y += 44f;
            }

            float by = H - 70f;
            if (GUI.Button(new Rect(40f, by, 200f, 50f), "HOST", btn)) { page = 1; msg = ""; hostSaves = MenuAuto.SaveNames(); hostSel = 0; hostTop = 0; }
            Server_Connect(new Rect(250f, by, 200f, 50f));
            if (GUI.Button(new Rect(460f, by, 200f, 50f), "DIRECT", btn)) { page = 2; msg = ""; }
            if (GUI.Button(new Rect(670f, by, 200f, 50f), "REFRESH", btn)) { Lan.Servers.Clear(); Lan.Scan(); }
            if (GUI.Button(new Rect(880f, by, 240f, 50f), "DISABLE MULTIPLAYER", btn)) { Net.ApplyOff(); msg = "Multiplayer disabled."; }
            if (GUI.Button(new Rect(W - 240f, by, 200f, 50f), "BACK", btn)) CloseMenu();
        }

        static void Server_Connect(Rect r)
        {
            Lan.Server sv = null;
            foreach (var x in Lan.Servers) if (x.Ip == selIp && x.Port == selPort) sv = x;
            GUI.enabled = sv != null;
            if (GUI.Button(r, "CONNECT", btn) && sv != null)
            {
                if (sv.Pw) { fAPass = ""; page = 3; msg = ""; }
                else { Net.ApplyClient(sv.Ip, sv.Port, ""); Net.StartTransfer(); Done(""); }
            }
            GUI.enabled = true;
        }

        static void Done(string m) { msg = m; CloseMenu(); }

        static List<string> hostSaves = new List<string>(); static int hostSel;

        static void PageHost()
        {
            float x = 40f, y = 110f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Server name", lab); y += 32f;
            fName = GUI.TextField(new Rect(x, y, 600f, 40f), fName ?? "", 32, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Password (empty = no password)", lab); y += 32f;
            fPass = GUI.PasswordField(new Rect(x, y, 600f, 40f), fPass ?? "", '*', 32, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Port", lab); y += 32f;
            fPort = GUI.TextField(new Rect(x, y, 200f, 40f), fPort ?? "", 5, field); y += 56f;
            GUI.Label(new Rect(x, y, 800f, 30f), "Max players: " + Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4) + " (Network/MaxPlayers in the config, 2..4)", lab);
            y += 50f;
            GUI.Label(new Rect(x, y, 1100f, 30f), "LOAD SAVE loads the selected save at once. NEW GAME: set up the new game yourself. The server starts when the city has loaded.", lab); y += 60f;
            GUI.enabled = hostSaves.Count > 0;
            if (GUI.Button(new Rect(x, y, 300f, 50f), "HOST: LOAD SAVE", btn)) StartHost(false);
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + 320f, y, 260f, 50f), "HOST NEW GAME", btn)) StartHost(true);
            if (GUI.Button(new Rect(x + 600f, y, 200f, 50f), "BACK", btn)) { page = 0; msg = ""; }

            float lx = 720f, ly = 110f; const int per = 10;
            GUI.Label(new Rect(lx, ly, 700f, 30f), hostSaves.Count > 0 ? "Save to load" : "No saves found", lab); ly += 36f;
            hostTop = Mathf.Clamp(hostTop, 0, Mathf.Max(0, hostSaves.Count - per));
            for (int i = hostTop; i < hostSaves.Count && i < hostTop + per; i++)
            {
                if (GUI.Button(new Rect(lx, ly, 700f, 40f), (i == hostSel ? "> " : "   ") + Path.GetFileNameWithoutExtension(hostSaves[i]), rowBtn)) hostSel = i;
                ly += 44f;
            }
            if (hostSaves.Count > per)
            {
                if (GUI.Button(new Rect(lx, ly, 120f, 40f), "UP", btn)) hostTop -= per;
                if (GUI.Button(new Rect(lx + 130f, ly, 120f, 40f), "DOWN", btn)) hostTop += per;
            }
        }

        static int hostTop;

        static void StartHost(bool newGame)
        {
            int p;
            if (string.IsNullOrWhiteSpace(fName)) { msg = "Enter a server name."; return; }
            if (!Port(fPort, out p)) { msg = "Port must be a number from 1024 to 65535."; return; }
            Net.ApplyHost(fName.Trim(), fPass ?? "", p);
            Net.ApplyHostModPicks();
            CloseMenu();
            if (newGame) MenuAuto.NewGame();
            else MenuAuto.LoadSave(hostSel >= 0 && hostSel < hostSaves.Count ? hostSaves[hostSel] : "");
            msg = "";
        }

        static void PageDirect()
        {
            float x = 40f, y = 110f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Host IP address", lab); y += 32f;
            fAddr = GUI.TextField(new Rect(x, y, 400f, 40f), fAddr ?? "", 40, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Port", lab); y += 32f;
            fAPort = GUI.TextField(new Rect(x, y, 200f, 40f), fAPort ?? "", 5, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Password (if the server has one)", lab); y += 32f;
            fAPass = GUI.PasswordField(new Rect(x, y, 600f, 40f), fAPass ?? "", '*', 32, field); y += 70f;
            if (GUI.Button(new Rect(x, y, 260f, 50f), "CONNECT", btn))
            {
                int p;
                if (string.IsNullOrWhiteSpace(fAddr)) msg = "Enter the host IP address.";
                else if (!Port(fAPort, out p)) msg = "Port must be a number from 1024 to 65535.";
                else { Net.ApplyClient(fAddr.Trim(), p, fAPass ?? ""); Net.StartTransfer(); Done(""); }
            }
            if (GUI.Button(new Rect(x + 280f, y, 200f, 50f), "BACK", btn)) { page = 0; msg = ""; }
        }

        static void PagePass()
        {
            Lan.Server sv = null;
            foreach (var x in Lan.Servers) if (x.Ip == selIp && x.Port == selPort) sv = x;
            float px = 40f, y = 110f;
            GUI.Label(new Rect(px, y, 900f, 30f), sv != null ? "Server: " + sv.Name + "  (" + sv.Ip + ":" + sv.Port + ")" : "Server is gone", lab); y += 50f;
            GUI.Label(new Rect(px, y, 600f, 30f), "Password", lab); y += 32f;
            fAPass = GUI.PasswordField(new Rect(px, y, 600f, 40f), fAPass ?? "", '*', 32, field); y += 70f;
            GUI.enabled = sv != null;
            if (GUI.Button(new Rect(px, y, 260f, 50f), "CONNECT", btn) && sv != null)
            {
                Net.ApplyClient(sv.Ip, sv.Port, fAPass ?? "");
                Net.StartTransfer();
                Done("");
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(px + 280f, y, 200f, 50f), "BACK", btn)) { page = 0; msg = ""; }
        }
    }

    public class SodCoopDriver : MonoBehaviour
    {
        public SodCoopDriver(IntPtr ptr) : base(ptr) { }
        float nextLog; bool alive; KeyCode key = KeyCode.F4; bool keyParsed;
        public void OnGUI() { CharEditor.OnGui(); NetMenu.OnGui(); Net.DrawHud(); }
        public void Update()
        {
            try
            {
                CrashLog.Beat();
                Perf.Frame();
                try { UiDev.Tick(); } catch (Exception e) { Plugin.L.LogWarning("[UI] " + e.Message); }
                long tu = Perf.T();
                try { UiKit.Tick(); } catch (Exception e) { Plugin.L.LogWarning("[UI] " + e.Message); }
                CharEditor.Tick();
                Perf.Add("интерфейс", tu);
                Lan.Tick();
                Net.Poll();
                MenuAuto.Tick();
                try { Net.AutoTestTick(); } catch (Exception e) { Plugin.L.LogWarning("[AUTOTEST] " + e); }
                if (Net.Started && Net.KeyDown(KeyCode.End)) Net.LeaveSession();
                if (!alive) { alive = true; Plugin.L.LogInfo("[DRV] Update работает"); }
                if (!keyParsed)
                {
                    keyParsed = true;
                    KeyCode k; key = Enum.TryParse<KeyCode>(Plugin.CfgProbeKey.Value ?? "", true, out k) ? k : KeyCode.None;
                    Plugin.L.LogInfo("[DRV] клавиша разведки: " + key);
                }
                if (key != KeyCode.None && Net.KeyDown(key))
                {
                    Plugin.L.LogInfo("[DRV] клавиша нажата, запускаю разведку");
                    var mm = MainMenuController.Instance;
                    bool menu = false;
                    try { menu = mm != null && mm.mainMenuActive; } catch (Exception __e) { Err.Swallow(__e); }
                    Probe.Run(menu ? "главное меню" : "игра");
                }
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup > nextLog) { nextLog = Time.realtimeSinceStartup + 30f; Plugin.L.LogWarning("[DRV] " + e); }
            }
        }
    }
}

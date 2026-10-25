using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        const byte MsgPermaDead = 40;
        const byte MsgSnail = 41;

        internal static readonly string[] ModIds = { "mod_filmNoir", "mod_shortSighted", "mod_gamblingDebt", "mod_houseArrest", "mod_fameAndFortune", "mod_snailNemesis", "mod_ratDetective", "mod_ironman", "permaDeath" };
        static readonly Dictionary<string, int> hostMods = new Dictionary<string, int>();
        static readonly Dictionary<string, int> modOrig = new Dictionary<string, int>();
        static bool keepModsOnce;
        static readonly Dictionary<int, GameObject> remoteSnails = new Dictionary<int, GameObject>();
        static readonly Dictionary<int, Vector3> remoteSnailPos = new Dictionary<int, Vector3>();
        static readonly Dictionary<int, Quaternion> remoteSnailRot = new Dictionary<int, Quaternion>();
        static float snailSendAt, modCheckAt, pauseCheckAt;
        static bool permaFieldSaved, permaFieldOrig;
        static bool permaOut, ratScanDone;

        static int LocalSetting(string id)
        {
            try { var pp = PlayerPrefsController.Instance; return pp != null ? pp.GetSettingInt(id) : 0; } catch (Exception) { return 0; }
        }

        internal static bool ModOn(string id)
        {
            int v;
            if (ClientOfHost && hostMods.TryGetValue(id, out v)) return v != 0;
            return LocalSetting(id) != 0;
        }

        internal static bool HostPermaDeath { get { return ClientOfHost ? ModOn("permaDeath") : LocalSetting("permaDeath") != 0; } }

        internal static string ModifiersSignature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var id in ModIds) sb.Append(LocalSetting(id));
            return sb.ToString();
        }

        internal static void WriteModifiers(NetDataWriter w)
        {
            w.Put(ModIds.Length);
            foreach (var id in ModIds) { w.Put(id); w.Put(LocalSetting(id)); }
        }

        internal static void ReadModifiers(NetDataReader r)
        {
            int n = r.GetInt();
            var got = new Dictionary<string, int>();
            for (int i = 0; i < n; i++) { string id = r.GetString(); int v = r.GetInt(); got[id] = v; }
            hostMods.Clear();
            foreach (var kv in got) hostMods[kv.Key] = kv.Value;
            ApplyHostModifiers();
        }

        static Dictionary<string, int> HostModPicks()
        {
            var d = new Dictionary<string, int>();
            foreach (var part in (Plugin.CfgModifiers.Value ?? "").Split(';'))
            {
                int eq = part.IndexOf('='); int v;
                if (eq > 0 && int.TryParse(part.Substring(eq + 1), out v)) d[part.Substring(0, eq).Trim()] = v;
            }
            return d;
        }

        internal static bool HostModPick(string id)
        {
            int v;
            return HostModPicks().TryGetValue(id, out v) ? v != 0 : LocalSetting(id) != 0;
        }

        internal static void SetHostModPick(string id, bool on)
        {
            var d = HostModPicks();
            foreach (var m in ModIds) if (!d.ContainsKey(m)) d[m] = LocalSetting(m) != 0 ? 1 : 0;
            d[id] = on ? 1 : 0;
            var parts = new List<string>();
            foreach (var m in ModIds) parts.Add(m + "=" + d[m]);
            Plugin.CfgModifiers.Value = string.Join(";", parts);
        }

        internal static void ApplyHostModPicks()
        {
            var d = HostModPicks();
            if (d.Count == 0) return;
            var pp = PlayerPrefsController.Instance;
            if (pp == null || pp.gameSettingControls == null) { Plugin.L.LogWarning("[MODS] настройки игры ещё не готовы - модификаторы сервера не применены"); return; }
            modLeakChecked = true;
            try { if (System.IO.File.Exists(ModRestorePath)) System.IO.File.Delete(ModRestorePath); } catch (Exception __e) { Err.Swallow(__e); }
            var changed = new List<string>(); var on = new List<string>();
            foreach (var id in ModIds)
            {
                int want;
                if (!d.TryGetValue(id, out want)) continue;
                if (want != 0) on.Add(id);
                if (LocalSetting(id) == want) continue;
                foreach (var s in pp.gameSettingControls)
                {
                    if (s == null || s.identifier != id) continue;
                    s.intValue = want;
                    PlayerPrefs.SetInt(id, want);
                    try { pp.OnToggleChanged(id, false, null); } catch (Exception e) { Plugin.L.LogWarning($"[MODS] {id}: {e.Message}"); }
                    changed.Add(id + "=" + want);
                    break;
                }
            }
            PlayerPrefs.Save();
            Plugin.L.LogInfo($"[MODS] модификаторы сервера: {(on.Count > 0 ? string.Join(", ", on) : "нет")}; изменил в настройках игры: {(changed.Count > 0 ? string.Join(", ", changed) : "ничего")}");
        }

        static string ModRestorePath { get { return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "SodCoop_modrestore.txt"); } }
        static bool modLeakChecked;

        static void WriteModRestore()
        {
            try
            {
                if (modOrig.Count == 0) { if (System.IO.File.Exists(ModRestorePath)) System.IO.File.Delete(ModRestorePath); return; }
                var lines = new List<string>();
                foreach (var kv in modOrig) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(ModRestorePath, lines);
            }
            catch (Exception e) { Plugin.L.LogWarning("[MODS] файл возврата модификаторов: " + e.Message); }
        }

        internal static void ModLeakCheck()
        {
            if (modLeakChecked || manager != null) return;
            var pp = PlayerPrefsController.Instance;
            if (pp == null || pp.gameSettingControls == null || pp.gameSettingControls.Count == 0) return;
            modLeakChecked = true;
            if (!System.IO.File.Exists(ModRestorePath)) return;
            var back = new List<string>();
            try
            {
                foreach (var line in System.IO.File.ReadAllLines(ModRestorePath))
                {
                    int eq = line.IndexOf('='); int v;
                    if (eq <= 0 || !int.TryParse(line.Substring(eq + 1), out v)) continue;
                    string id = line.Substring(0, eq);
                    foreach (var s in pp.gameSettingControls)
                    {
                        if (s == null || s.identifier != id) continue;
                        s.intValue = v;
                        PlayerPrefs.SetInt(id, v);
                        try { pp.OnToggleChanged(id, false, null); } catch (Exception __e) { Err.Swallow(__e); }
                        back.Add(id + "=" + v);
                        break;
                    }
                }
                PlayerPrefs.Save();
                System.IO.File.Delete(ModRestorePath);
            }
            catch (Exception e) { Plugin.L.LogWarning("[MODS] возврат модификаторов после прошлой игры: " + e.Message); }
            Plugin.L.LogInfo("[MODS] прошлая игра закончилась с модификаторами хоста - вернул свои: " + string.Join(", ", back));
        }

        static bool SetSetting(string id, int v)
        {
            var pp = PlayerPrefsController.Instance;
            if (pp == null || pp.gameSettingControls == null) return false;
            PlayerPrefsController.GameSetting gs = null;
            foreach (var s in pp.gameSettingControls) if (s != null && s.identifier == id) { gs = s; break; }
            if (gs == null) return false;
            int stored = PlayerPrefs.GetInt(id, gs.intValue);
            gs.intValue = v;
            pp.OnToggleChanged(id, false, null);
            int keep = modOrig.ContainsKey(id) ? modOrig[id] : stored;
            PlayerPrefs.SetInt(id, keep);
            return true;
        }

        static void ApplyHostModifiers()
        {
            var changed = new List<string>();
            foreach (var id in ModIds)
            {
                int want;
                if (!hostMods.TryGetValue(id, out want)) continue;
                if (id == "permaDeath")
                {
                    try { var g = Game.Instance; if (g != null && g.permaDeath) { if (!permaFieldSaved) { permaFieldSaved = true; permaFieldOrig = true; } g.permaDeath = false; changed.Add("permaDeath(игра)=0"); } } catch (Exception __e) { Err.Swallow(__e); }
                    continue;
                }
                int cur = LocalSetting(id);
                if (cur == want) continue;
                if (!modOrig.ContainsKey(id)) { modOrig[id] = cur; WriteModRestore(); }
                try { if (SetSetting(id, want)) changed.Add(id + "=" + want); else Plugin.L.LogWarning($"[MODS] настройка '{id}' не найдена"); }
                catch (Exception e) { Plugin.L.LogWarning($"[MODS] {id}: {e.Message}"); }
            }
            var on = new List<string>();
            foreach (var kv in hostMods) if (kv.Value != 0) on.Add(kv.Key);
            Plugin.L.LogInfo($"[MODS] модификаторы хоста: {(on.Count > 0 ? string.Join(", ", on) : "нет")}; изменил у себя: {(changed.Count > 0 ? string.Join(", ", changed) : "ничего")}");
            SyncLocalSnail();
            try { var mc = ModifiersController.Instance; if (mc != null && mc.shortSightedModifierEnabled) mc.ShortSightedGlassesCheck(); } catch (Exception __e) { Err.Swallow(__e); }
        }

        static void SyncLocalSnail()
        {
            var mc = ModifiersController.Instance;
            if (mc == null) return;
            try
            {
                bool has = mc.currentSnail != null;
                if (mc.snailNemesisModifierEnabled && !has && !GhostMe && Player.Instance != null && CityData.Instance != null)
                {
                    mc.SpawnNewSnail(null);
                    Plugin.L.LogInfo($"[MODS] моя улитка возмездия создана: {(mc.currentSnail != null ? "да" : "нет")}");
                }
                else if (!mc.snailNemesisModifierEnabled && has)
                {
                    mc.RemoveCurrentSnail();
                    Plugin.L.LogInfo("[MODS] моя улитка убрана (модификатор выключен)");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[MODS] улитка: " + e.Message); }
        }

        internal static void ModifiersRestore()
        {
            hostMods.Clear();
            foreach (var id in new List<int>(remoteSnails.Keys)) RemoveRemoteSnail(id);
            if (keepModsOnce)
            {
                keepModsOnce = false;
                if (modOrig.Count > 0) Plugin.L.LogInfo("[MODS] загружаю сейв хоста - модификаторы хоста пока оставляю включёнными");
                return;
            }
            if (permaFieldSaved) { permaFieldSaved = false; try { var g = Game.Instance; if (g != null) g.permaDeath = permaFieldOrig; } catch (Exception __e) { Err.Swallow(__e); } }
            if (modOrig.Count == 0) return;
            var back = new List<string>();
            var orig = new Dictionary<string, int>(modOrig);
            foreach (var kv in orig)
            {
                try { if (SetSetting(kv.Key, kv.Value)) back.Add(kv.Key + "=" + kv.Value); } catch (Exception e) { Plugin.L.LogWarning($"[MODS] вернуть {kv.Key}: {e.Message}"); }
            }
            modOrig.Clear();
            WriteModRestore();
            SyncLocalSnail();
            Plugin.L.LogInfo("[MODS] вернул свои модификаторы: " + string.Join(", ", back));
        }

        static void ModifiersTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= pauseCheckAt) { pauseCheckAt = now + 2f; try { ApplyPause(); } catch (Exception __e) { Err.Swallow(__e); } }
            if (manager == null || manager.ConnectedPeersCount == 0) return;
            if (now >= modCheckAt)
            {
                modCheckAt = now + 2f;
                if (ClientOfHost && hostMods.Count > 0)
                {
                    bool off = false;
                    foreach (var kv in hostMods) { if (kv.Key == "permaDeath") continue; if (LocalSetting(kv.Key) != kv.Value) { off = true; break; } }
                    try { var g = Game.Instance; if (g != null && g.permaDeath) off = true; } catch (Exception __e) { Err.Swallow(__e); }
                    if (off) ApplyHostModifiers();
                    else SyncLocalSnail();
                }
                if (!ratScanDone && ModOn("mod_ratDetective")) { ratScanDone = true; try { RatModelScan(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] поиск модели крысы: " + e.Message); } }
            }
            if (now >= snailSendAt)
            {
                snailSendAt = now + 0.2f;
                var mc = ModifiersController.Instance;
                var s = mc != null ? mc.currentSnail : null;
                var w = new NetDataWriter(); w.Put(MsgSnail);
                if (s != null && mc.snailNemesisModifierEnabled)
                {
                    var t = s.transform;
                    w.Put(true); w.Put(t.position.x); w.Put(t.position.y); w.Put(t.position.z);
                    var q = t.rotation; w.Put(q.x); w.Put(q.y); w.Put(q.z); w.Put(q.w);
                }
                else w.Put(false);
                manager.SendToAll(w, DeliveryMethod.Sequenced);
            }
            foreach (var kv in remoteSnails)
            {
                var go = kv.Value; if (go == null) continue;
                Vector3 p; Quaternion q;
                if (!remoteSnailPos.TryGetValue(kv.Key, out p) || !remoteSnailRot.TryGetValue(kv.Key, out q)) continue;
                var t = go.transform;
                t.position = Vector3.Lerp(t.position, p, Mathf.Clamp01(Time.deltaTime * 8f));
                t.rotation = Quaternion.Slerp(t.rotation, q, Mathf.Clamp01(Time.deltaTime * 8f));
            }
        }

        static void OnSnailMsg(RP rp, NetDataReader r)
        {
            if (rp == null) return;
            bool has = r.GetBool();
            if (!has) { RemoveRemoteSnail(rp.pid); return; }
            var p = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            var q = new Quaternion(r.GetFloat(), r.GetFloat(), r.GetFloat(), r.GetFloat());
            remoteSnailPos[rp.pid] = p; remoteSnailRot[rp.pid] = q;
            GameObject go;
            if (remoteSnails.TryGetValue(rp.pid, out go) && go != null) return;
            var mc = ModifiersController.Instance;
            if (mc == null || mc.snailPrefab == null) return;
            try
            {
                go = UnityEngine.Object.Instantiate(mc.snailPrefab, p, q);
                go.name = "SodCoop_Snail_" + rp.pid;
                foreach (var c in go.GetComponentsInChildren<SnailController>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                remoteSnails[rp.pid] = go;
                Plugin.L.LogInfo($"[MODS] улитка игрока {rp.pid} ({PlayerLabel(rp.pid)}) показана");
            }
            catch (Exception e) { Plugin.L.LogWarning("[MODS] показать чужую улитку: " + e.Message); }
        }

        static void RemoveRemoteSnail(int pid)
        {
            GameObject go;
            if (remoteSnails.TryGetValue(pid, out go))
            {
                try { if (go != null) UnityEngine.Object.Destroy(go); } catch (Exception __e) { Err.Swallow(__e); }
                remoteSnails.Remove(pid);
                Plugin.L.LogInfo($"[MODS] улитка игрока {pid} убрана");
            }
            remoteSnailPos.Remove(pid); remoteSnailRot.Remove(pid);
        }

        static void RatModelScan()
        {
            var names = new List<string>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                string n = go.name ?? "";
                if (n.IndexOf("rat", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Rat", StringComparison.Ordinal) < 0 && !n.StartsWith("rat", StringComparison.OrdinalIgnoreCase)) continue;
                if (names.Count < 40 && !names.Contains(n)) names.Add(n);
            }
            Plugin.L.LogInfo("[MODS] объекты с 'Rat' в имени: " + (names.Count > 0 ? string.Join(", ", names) : "нет"));
        }

        internal static bool GhostMe;
        static readonly HashSet<int> ghostPids = new HashSet<int>();
        static float permaShowAt = -1f, permaReloadAt = -1f;

        static bool PermaOn { get { return ClientOfHost ? ModOn("permaDeath") : LocalSetting("permaDeath") != 0; } }
        static bool IronOn { get { return ModOn("mod_ironman"); } }

        internal static bool PermaDeathOut()
        {
            if (manager == null || !PermaOn) return false;
            if (permaOut) return true;
            permaOut = true;
            if (IsHost)
            {
                if (IronOn)
                {
                    Plugin.L.LogInfo("[MODS] я хост и погиб (перманентная смерть + железный человек): игра окончена у всех");
                    SendPerma(3);
                    return false;
                }
                Plugin.L.LogInfo("[MODS] я хост и погиб (перманентная смерть): загружаю последний сейв");
                Toast(UiKit.T("You died. Loading the last save for everyone...", "Ты погиб. Загружаю последнее сохранение для всех..."));
                SendPerma(1);
                permaReloadAt = Time.realtimeSinceStartup + 3f;
                return true;
            }
            if (IronOn)
            {
                Plugin.L.LogInfo("[MODS] я погиб (перманентная смерть + железный человек): становлюсь призраком");
                SendPerma(2);
                BecomeGhost();
                return true;
            }
            Plugin.L.LogInfo("[MODS] я погиб (перманентная смерть): хост загрузит последний сейв");
            Toast(UiKit.T("You died. The host loads the last save...", "Ты погиб. Хост загружает последнее сохранение..."));
            SendPerma(1);
            return true;
        }

        static readonly HashSet<int> ghostSkinned = new HashSet<int>();
        static Material ghostMat;

        static Material GhostMaterial()
        {
            if (ghostMat != null) return ghostMat;
            var sh = Shader.Find("HDRP/Unlit");
            if (sh == null) return null;
            var m = new Material(sh);
            var col = new Color(0.75f, 0.85f, 1f, 0.3f);
            m.SetColor("_UnlitColor", col);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 0f);
            m.SetFloat("_SrcBlend", 1f);
            m.SetFloat("_DstBlend", 10f);
            m.SetFloat("_AlphaSrcBlend", 1f);
            m.SetFloat("_AlphaDstBlend", 10f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_TransparentZWrite", 0f);
            m.SetFloat("_ZTestDepthEqualForOpaque", 4f);
            m.SetFloat("_CullMode", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_BLENDMODE_ALPHA");
            m.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
            m.DisableKeyword("_BLENDMODE_ADD");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            ghostMat = m;
            return m;
        }

        static void GhostSkin(GameObject go)
        {
            ghostSkinned.Add(go.GetInstanceID());
            var m = GhostMaterial();
            int nr = 0, nc = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || m == null) continue;
                var arr = r.sharedMaterials;
                int n = arr != null ? arr.Length : 1;
                var mats = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(n);
                for (int i = 0; i < n; i++) mats[i] = m;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                nr++;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) { if (c != null && c.enabled) { c.enabled = false; nc++; } }
            Plugin.L.LogInfo($"[MODS] аватар призрака сделан полупрозрачным: частей {nr}, коллизий выключено {nc}{(m == null ? " (шейдер не найден)" : "")}");
        }

        internal static bool GhostBlocksPickup() { return GhostMe && !profRestoring; }

        internal static bool GhostBlocks(Actor who)
        {
            if (!GhostMe) return false;
            try { return who != null && who.isPlayer; } catch (Exception) { return false; }
        }

        static void SendPerma(byte kind)
        {
            try { var w = new NetDataWriter(); w.Put(MsgPermaDead); w.Put(kind); manager.SendToAll(w, DeliveryMethod.ReliableOrdered); manager.PollEvents(); }
            catch (Exception e) { Plugin.L.LogWarning("[MODS] сообщить о смерти: " + e.Message); }
        }

        static void BecomeGhost()
        {
            if (GhostMe) return;
            var p = Player.Instance;
            try { if (p != null) { p.ResetHealthToMaximum(); } } catch (Exception __e) { Err.Swallow(__e); }
            try { var fp = FirstPersonItemController.Instance; if (fp != null) fp.ForceHolster(); } catch (Exception __e) { Err.Swallow(__e); }
            try { var bs = BioScreenController.Instance; if (bs != null) bs.SetInventoryOpen(false, true, true); } catch (Exception __e) { Err.Swallow(__e); }
            GhostMe = true;
            try { var g = Game.Instance; if (g != null) { g.invisiblePlayer = true; if (g.freeCam) g.SetFreeCamMode(false); } } catch (Exception e) { Plugin.L.LogWarning("[MODS] режим призрака: " + e.Message); }
            try { SendBodyFx(3, Vector3.zero); } catch (Exception __e) { Err.Swallow(__e); }
            ghostCareAt = 0f;
            GhostCare();
            Toast(UiKit.T("You died for good. You are a ghost now: walk around and watch the others.", "Ты погиб навсегда. Теперь ты призрак: ходи и наблюдай за остальными."));
        }

        static float ghostCareAt;
        static readonly string[] GhostGoodStats = { "nourishment", "hydration", "alertness", "energy", "hygiene", "heat" };

        static void GhostCare()
        {
            float now = Time.realtimeSinceStartup;
            if (now < ghostCareAt) return;
            ghostCareAt = now + 0.5f;
            var p = Player.Instance;
            if (p != null)
            {
                try { if (p.currentHealth < p.maximumHealth) p.SetHealth(p.maximumHealth); } catch (Exception __e) { Err.Swallow(__e); }
                foreach (var n in BadStatNames) { try { if (GetStat(p, n) != 0f) SetStat(p, n, 0f); } catch (Exception __e) { Err.Swallow(__e); } }
                foreach (var n in GhostGoodStats) { try { if (GetStat(p, n) < 1f) SetStat(p, n, 1f); } catch (Exception __e) { Err.Swallow(__e); } }
            }
            try
            {
                var mc = ModifiersController.Instance;
                if (mc != null && mc.currentSnail != null) { mc.RemoveCurrentSnail(); Plugin.L.LogInfo("[MODS] я призрак: моя улитка убрана"); }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void EndGhost()
        {
            if (!GhostMe) return;
            GhostMe = false;
            try { var g = Game.Instance; if (g != null) { if (g.freeCam) g.SetFreeCamMode(false); g.invisiblePlayer = false; } } catch (Exception __e) { Err.Swallow(__e); }
        }

        static void GhostTick()
        {
            if (GhostMe)
            {
                try { var g = Game.Instance; if (g != null) { if (g.freeCam) g.SetFreeCamMode(false); g.invisiblePlayer = true; } } catch (Exception __e) { Err.Swallow(__e); }
                GhostCare();
            }
            foreach (var pid in ghostPids)
            {
                var rp = RpByPid(pid); if (rp == null) continue;
                Enter(rp);
                try
                {
                    if (avatar != null && !ghostSkinned.Contains(avatar.GetInstanceID())) GhostSkin(avatar);
                    if (puppet != null) ReleasePuppet("игрок стал призраком");
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            Leave();
        }

        static bool IsGhostRp(RP rp) { return rp != null && ghostPids.Contains(rp.pid); }

        static void PermaTick()
        {
            float now = Time.realtimeSinceStartup;
            GhostTick();
            if (permaReloadAt >= 0f && now >= permaReloadAt)
            {
                permaReloadAt = -1f;
                HostReloadLastSave();
            }
            if (permaShowAt >= 0f && now >= permaShowAt)
            {
                permaShowAt = -1f;
                try { Shutdown(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] отключение: " + e.Message); }
                try { var ic = InterfaceController.Instance; if (ic != null) ic.ExecuteGameOverDisplay(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] экран конца игры: " + e.Message); }
            }
        }

        static void HostReloadLastSave()
        {
            string best = null; DateTime bt = DateTime.MinValue;
            try
            {
                foreach (var f in System.IO.Directory.GetFiles(SaveDir()))
                {
                    string fn = System.IO.Path.GetFileName(f);
                    if (fn.StartsWith("SodCoop_sync", StringComparison.OrdinalIgnoreCase)) continue;
                    string ext = System.IO.Path.GetExtension(fn).ToLowerInvariant();
                    if (ext != ".sod" && ext != ".sodb") continue;
                    var t = System.IO.File.GetLastWriteTimeUtc(f);
                    if (t > bt) { bt = t; best = fn; }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[MODS] поиск сейва: " + e.Message); }
            permaOut = false;
            if (best == null)
            {
                Plugin.L.LogWarning("[MODS] сохранений нет - загружать нечего");
                Toast(UiKit.T("No save to load.", "Нет сохранения для загрузки."));
                return;
            }
            Plugin.L.LogInfo("[MODS] загружаю последний сейв: " + best);
            OpenMenuForLoad();
            MenuAuto.LoadSave(best);
        }

        static void OnPermaDeadMsg(RP rp, NetDataReader r)
        {
            byte kind = r.AvailableBytes > 0 ? r.GetByte() : (byte)2;
            int pid = rp != null ? rp.pid : 0;
            string nm = PlayerLabel(pid);
            if (kind == 1)
            {
                Plugin.L.LogInfo($"[MODS] игрок {pid} ({nm}) погиб (перманентная смерть): загружаем последний сейв");
                Toast(UiKit.T(nm + " died. Loading the last save...", nm + " погиб. Загружаем последнее сохранение..."));
                if (IsHost && permaReloadAt < 0f) permaReloadAt = Time.realtimeSinceStartup + 3f;
            }
            else if (kind == 2)
            {
                if (rp == null) return;
                ghostPids.Add(pid);
                if (IsHost && !string.IsNullOrEmpty(rp.profileId))
                {
                    ProfileData pd;
                    if (!profiles.TryGetValue(rp.profileId, out pd) || pd == null) { pd = new ProfileData(); pd.id = rp.profileId; profiles[rp.profileId] = pd; }
                    if (!pd.dead) { pd.dead = true; profDirty = true; try { ProfilesWrite(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] запись профилей: " + e.Message); } }
                }
                Plugin.L.LogInfo($"[MODS] игрок {pid} ({nm}) погиб навсегда и стал призраком");
                Toast(UiKit.T(nm + " died for good and is now a ghost.", nm + " погиб навсегда и стал призраком."));
            }
            else if (kind == 3 && !IsHost)
            {
                Plugin.L.LogInfo("[MODS] хост погиб (железный человек): игра окончена");
                Toast(UiKit.T("The host died. Game over.", "Хост погиб. Игра окончена."));
                permaShowAt = Time.realtimeSinceStartup + 2f;
            }
        }

        static void GhostOnProfile(ProfileData pd)
        {
            if (pd == null || !pd.dead || !PermaOn || !IronOn) return;
            Plugin.L.LogInfo("[MODS] я погиб в этом мире раньше: снова призрак");
            SendPerma(2);
            BecomeGhost();
        }

        static void PermaOnShutdown()
        {
            EndGhost();
            ghostPids.Clear();
            ghostSkinned.Clear();
            permaOut = false; permaReloadAt = -1f;
        }

        static Company GamblingCompany()
        {
            var cd = CityData.Instance;
            if (cd == null || cd.companyDirectory == null) return null;
            Company loan = null;
            foreach (var c in cd.companyDirectory)
            {
                if (c == null || c.preset == null) continue;
                string n = c.preset.name ?? "";
                if (n == "LoanShark") return c;
                if (loan == null && n.IndexOf("Loan", StringComparison.OrdinalIgnoreCase) >= 0) loan = c;
            }
            return loan;
        }

        static void CaptureDebts(ProfileData pd)
        {
            var gc = GameplayController.Instance;
            if (gc == null || gc.debt == null) return;
            pd.debtsSet = true;
            foreach (var d in gc.debt)
            {
                if (d == null) continue;
                pd.debts.Add(new ProfDebt { company = d.companyID, debt = d.debt, payments = d.payments, missed = d.missedPayments, due = d.nextPaymentDueBy, check = d.dueCheck });
            }
        }

        static void RestoreDebts(List<ProfDebt> debts, bool fresh, ProfileData moneyTarget)
        {
            var gc = GameplayController.Instance;
            if (gc == null) return;
            if (gc.debt == null) gc.debt = new Il2CppSystem.Collections.Generic.List<GameplayController.LoanDebt>();
            int had = gc.debt.Count;
            gc.debt.Clear();
            if (debts != null)
                foreach (var d in debts)
                    gc.debt.Add(new GameplayController.LoanDebt { companyID = d.company, debt = d.debt, payments = d.payments, missedPayments = d.missed, nextPaymentDueBy = d.due, dueCheck = d.check });
            string extra = "";
            if (fresh && ModOn("mod_gamblingDebt"))
            {
                var c = GamblingCompany();
                if (c != null) { gc.AddNewDebt(c, 10000, 0, 1000); if (moneyTarget != null && moneyTarget.money >= 0) moneyTarget.money += 10000; extra = $", долг за азартные игры {c.name} 10000, деньги +10000"; }
                else extra = ", компания для долга не найдена";
            }
            Plugin.L.LogInfo($"[MODS] долги: были от сейва хоста {had}, мои {(debts != null ? debts.Count : 0)}{extra}");
        }

        static List<ProfItem> NewPlayerKit()
        {
            var kit = new List<ProfItem>();
            if (ModOn("mod_shortSighted")) kit.Add(new ProfItem { preset = "Glasses", slot = 0 });
            return kit;
        }
    }
}

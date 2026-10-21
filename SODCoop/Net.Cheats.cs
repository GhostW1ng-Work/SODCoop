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
        static bool cheatKeysBroken;
        static bool lastPause;
        static bool moneyDumped;

        internal static void CheatKeys()
        {
            if (cheatKeysBroken || !Plugin.CfgCheatKeys.Value) return;
            try
            {
                KeyCode mk;
                string ks = (Plugin.CfgCheatMenuKey.Value ?? "").Trim();
                if (ks != "" && Enum.TryParse<KeyCode>(ks, true, out mk) && KeyDown(mk)) ToggleCheatMenu();
            }
            catch (Exception e)
            {
                cheatKeysBroken = true;
                Plugin.L.LogWarning($"[CHEAT] клавиша меню читов недоступна ({e.GetType().Name}); используйте значения в конфиге");
            }
        }

        static System.Reflection.MethodInfo keyDownMethod;

        internal static bool KeyDown(KeyCode k)
        {
            if (keyDownMethod == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("UnityEngine.Input");
                        if (t == null) continue;
                        keyDownMethod = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                        if (keyDownMethod != null) break;
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
                if (keyDownMethod == null) throw new InvalidOperationException("Input.GetKeyDown не найден");
            }
            return (bool)keyDownMethod.Invoke(null, new object[] { k });
        }

        internal static void CheatsSecond()
        {
            try
            {
                if (Plugin.CfgAddMoney.Value != 0)
                {
                    int a = Plugin.CfgAddMoney.Value;
                    Plugin.CfgAddMoney.Value = 0;
                    GiveMoney(a);
                    Plugin.Cfg.Save();
                }
                if (Plugin.CfgAddSocial.Value != 0)
                {
                    int a = Plugin.CfgAddSocial.Value;
                    Plugin.CfgAddSocial.Value = 0;
                    GiveSocial(a);
                    Plugin.Cfg.Save();
                }
                if (Plugin.CfgSpawnNow.Value)
                {
                    Plugin.CfgSpawnNow.Value = false;
                    SpawnItem(Plugin.CfgItemPreset.Value);
                    Plugin.Cfg.Save();
                }
                string q = Plugin.CfgSearchPreset.Value;
                if (!string.IsNullOrEmpty(q))
                {
                    Plugin.CfgSearchPreset.Value = "";
                    SearchPresets(q);
                    Plugin.Cfg.Save();
                }
                ApplyPause();
                AvatarLightFix(false);
                BreakTrace.MaybeInstall();
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] {e.Message}"); }
        }

        static void ApplyPause()
        {
            bool v = Plugin.CfgPauseNpc.Value;
            if (v == lastPause) return;
            lastPause = v;
            try { Game.Instance.SetPauseAI(v); Plugin.L.LogInfo($"[CHEAT] ИИ NPC {(v ? "остановлен" : "включён")}"); }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] SetPauseAI: {e.Message}"); }
        }

        static readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        static Type FindType(string name)
        {
            Type found;
            if (typeCache.TryGetValue(name, out found)) return found;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception __e) { Err.Swallow(__e); continue; }
                foreach (var t in types)
                    if (t != null && t.Name == name && t.Namespace == null) { found = t; break; }
                if (found != null) break;
            }
            if (found != null) typeCache[name] = found;
            return found;
        }

        static void GiveMoney(int amount)
        {
            try
            {
                var t = FindType("GameplayController");
                if (t == null) { Plugin.L.LogWarning("[CHEAT] класс GameplayController не найден"); return; }
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
                if (!moneyDumped)
                {
                    moneyDumped = true;
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var m in t.GetMembers(flags))
                        if (m.Name.IndexOf("oney", StringComparison.OrdinalIgnoreCase) >= 0 && !m.Name.StartsWith("NativeFieldInfoPtr") && !m.Name.StartsWith("NativeMethodInfoPtr")) names.Add(m.MemberType + " " + m.Name);
                    Plugin.L.LogInfo("[CHEAT] члены GameplayController про деньги: " + string.Join(", ", names.ToArray()));
                }
                object inst = null;
                var ip = t.GetProperty("Instance", flags);
                if (ip != null) inst = ip.GetValue(null, null);
                if (inst == null) { Plugin.L.LogWarning("[CHEAT] GameplayController.Instance пуст"); return; }
                foreach (var m in t.GetMethods(flags))
                {
                    if (m.Name != "AddMoney") continue;
                    var ps = m.GetParameters();
                    var args = new object[ps.Length];
                    bool first = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt == typeof(int)) args[i] = amount;
                        else if (pt == typeof(float)) args[i] = (float)amount;
                        else if (pt == typeof(bool)) args[i] = first ? true : false;
                        else if (pt == typeof(string)) args[i] = "cheat";
                        else args[i] = null;
                        if (pt == typeof(bool)) first = false;
                    }
                    m.Invoke(inst, args);
                    Plugin.L.LogInfo($"[CHEAT] добавлено денег: {amount} (AddMoney)");
                    return;
                }
                var mp = t.GetProperty("money", flags);
                if (mp != null && mp.PropertyType == typeof(int))
                {
                    mp.SetValue(inst, (int)mp.GetValue(inst, null) + amount, null);
                    Plugin.L.LogInfo($"[CHEAT] добавлено денег: {amount} (поле money)");
                    return;
                }
                Plugin.L.LogWarning("[CHEAT] не нашёл AddMoney / money у GameplayController, смотрите список членов в логе выше");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] деньги: {(e.InnerException != null ? e.InnerException.Message : e.Message)}"); }
        }

        static bool socialDumped;

        static void GiveSocial(int amount)
        {
            try
            {
                var t = FindType("GameplayController");
                if (t == null) { Plugin.L.LogWarning("[CHEAT] класс GameplayController не найден"); return; }
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
                if (!socialDumped)
                {
                    socialDumped = true;
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var m in t.GetMembers(flags))
                        if (m.Name.IndexOf("ocial", StringComparison.OrdinalIgnoreCase) >= 0 && !m.Name.StartsWith("NativeFieldInfoPtr") && !m.Name.StartsWith("NativeMethodInfoPtr")) names.Add(m.MemberType + " " + m.Name);
                    Plugin.L.LogInfo("[CHEAT] члены GameplayController про соцрейтинг: " + string.Join(", ", names.ToArray()));
                }
                object inst = null;
                var ip = t.GetProperty("Instance", flags);
                if (ip != null) inst = ip.GetValue(null, null);
                if (inst == null) { Plugin.L.LogWarning("[CHEAT] GameplayController.Instance пуст"); return; }
                foreach (var m in t.GetMethods(flags))
                {
                    if (!m.Name.StartsWith("Add") || m.Name.IndexOf("ocial", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ps = m.GetParameters();
                    var args = new object[ps.Length];
                    bool ok = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt == typeof(int)) args[i] = amount;
                        else if (pt == typeof(float)) args[i] = (float)amount;
                        else if (pt == typeof(bool)) args[i] = false;
                        else if (pt == typeof(string)) args[i] = "cheat";
                        else if (!pt.IsValueType) args[i] = null;
                        else ok = false;
                    }
                    if (!ok) continue;
                    m.Invoke(inst, args);
                    Plugin.L.LogInfo($"[CHEAT] соцрейтинг +{amount} ({m.Name})");
                    return;
                }
                foreach (var pr in t.GetProperties(flags))
                {
                    if (pr.Name.IndexOf("ocialcredit", StringComparison.OrdinalIgnoreCase) < 0 || !pr.CanRead || !pr.CanWrite) continue;
                    if (pr.PropertyType == typeof(int)) { pr.SetValue(inst, (int)pr.GetValue(inst, null) + amount, null); Plugin.L.LogInfo($"[CHEAT] соцрейтинг +{amount} (поле {pr.Name})"); return; }
                    if (pr.PropertyType == typeof(float)) { pr.SetValue(inst, (float)pr.GetValue(inst, null) + amount, null); Plugin.L.LogInfo($"[CHEAT] соцрейтинг +{amount} (поле {pr.Name})"); return; }
                }
                Plugin.L.LogWarning("[CHEAT] не нашёл метод/поле соцрейтинга, смотрите список членов в логе выше");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] соцрейтинг: {(e.InnerException != null ? e.InnerException.Message : e.Message)}"); }
        }

        static void SpawnItem(string preset)
        {
            try
            {
                var pr = FindPreset(preset);
                if (pr == null) { Plugin.L.LogWarning($"[CHEAT] пресет '{preset}' не найден (используйте Cheats.SearchPreset)"); return; }
                var cam = Camera.main;
                Vector3 pos = cam != null ? cam.transform.position + cam.transform.forward * 1.0f : Player.Instance.transform.position + Vector3.up;
                var it = InteractableCreator.Instance.CreateWorldInteractable(pr, null, null, null, pos, Vector3.zero, null, null, "");
                Plugin.L.LogInfo(it != null ? $"[CHEAT] создан предмет '{preset}' (id={it.id})" : $"[CHEAT] не удалось создать '{preset}'");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] предмет: {e.Message}"); }
        }

        static System.Collections.Generic.List<InteractablePreset> weaponPresets;
        static int weaponIdx;

        static void SpawnNextWeapon()
        {
            try
            {
                if (weaponPresets == null)
                {
                    weaponPresets = new System.Collections.Generic.List<InteractablePreset>();
                    foreach (var p in Resources.FindObjectsOfTypeAll<InteractablePreset>())
                        if (p != null && p.weapon != null && !string.IsNullOrEmpty(p.name)) weaponPresets.Add(p);
                    weaponPresets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var p in weaponPresets) names.Add(p.name + "(" + p.weapon.type + ")");
                    Plugin.L.LogInfo($"[CHEAT] оружие в игре ({weaponPresets.Count}): " + string.Join(", ", names.ToArray()));
                }
                if (weaponPresets.Count == 0) { Plugin.L.LogWarning("[CHEAT] оружейных пресетов не найдено"); return; }
                var w = weaponPresets[weaponIdx % weaponPresets.Count];
                weaponIdx++;
                var cam = Camera.main;
                Vector3 pos = cam != null ? cam.transform.position + cam.transform.forward * 1.0f : Player.Instance.transform.position + Vector3.up;
                var it = InteractableCreator.Instance.CreateWorldInteractable(w, null, null, null, pos, Vector3.zero, null, null, "");
                Plugin.L.LogInfo($"[CHEAT] оружие '{w.name}' ({w.weapon.type}) создано перед вами: {(it != null ? "ok" : "ошибка")}");
                Net.Toast("Weapon spawned: " + w.name + " - pick it up (F12 = next)");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] оружие: {e.Message}"); }
        }

        static void SearchPresets(string q)
        {
            FindPreset("");
            var found = new System.Collections.Generic.List<string>();
            if (presetCache != null)
                foreach (var k in presetCache.Keys)
                    if (k.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) found.Add(k);
            found.Sort();
            Plugin.L.LogInfo($"[CHEAT] пресеты по '{q}' ({found.Count}): " + string.Join(", ", found.ToArray()));
        }
    }
}

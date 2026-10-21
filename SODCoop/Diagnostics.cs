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
    public static class BreakTrace
    {
        static readonly System.Collections.Generic.Dictionary<string, int> counts = new System.Collections.Generic.Dictionary<string, int>();

        static bool tried; static float firstSeen;

        public static void MaybeInstall()
        {
            if (tried || !Plugin.CfgTraceBreak.Value) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (firstSeen == 0f) { firstSeen = now; return; }
            if (now - firstSeen < 25f) return;
            tried = true;
            Install();
        }

        static void Install()
        {
            try
            {
                var asm = typeof(Player).Assembly;
                var rx = new System.Text.RegularExpressions.Regex(Plugin.CfgTypeRegex.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                int n = 0;
                foreach (var t in asm.GetTypes())
                {
                    try
                    {
                        if (t.Namespace != null || t.IsGenericType || t.Name.Contains("<") || t.Name.Contains("__")) continue;
                        if (!rx.IsMatch(t.Name)) continue;
                        var ms = new System.Collections.Generic.List<string>();
                        foreach (var m in t.GetMethods(flags))
                        {
                            string mn = m.Name;
                            if (mn.StartsWith("get_") || mn.StartsWith("set_") || mn.StartsWith("add_") || mn.StartsWith("remove_") || mn.StartsWith("NativeMethodInfoPtr")) continue;
                            if (mn == "Equals" || mn == "GetHashCode" || mn == "ToString" || mn == "GetType" || mn == "Finalize" || mn == "MemberwiseClone") continue;
                            ms.Add(mn);
                        }
                        string bt = t.BaseType != null ? t.BaseType.Name : "";
                        var ps = new System.Collections.Generic.List<string>();
                        foreach (var p in t.GetProperties(flags)) ps.Add(p.Name);
                        Plugin.Dbg($"[TYPES] {t.Name} : {bt} | методы: {string.Join(", ", ms.ToArray())} | поля: {string.Join(", ", ps.ToArray())}");
                        n++;
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
                Plugin.Dbg($"[TYPES] найдено классов: {n}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[TYPES] {e.Message}"); }
        }

        public static void Prefix(System.Reflection.MethodBase __originalMethod)
        {
            try
            {
                string key = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
                int c; counts.TryGetValue(key, out c);
                if (c >= 6) return;
                counts[key] = c + 1;
                Plugin.Dbg($"[TRACE] вызван {key}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch]
    public static class AptTrace
    {
        static readonly string[] playerNames = { "BuyNewResidence", "ExecutePlacement", "ApplyDecor", "RevertDecor", "SellFurniture", "MoveFurnitureToStorage", "RemoveFromStorage", "MoveItemToStorage", "RemoveItemFromStorage", "SellItem", "PlaceIndividualCluster", "SetDecoratingMode" };
        static readonly string[] roomNames = { "AddFurnitureCustom" };

        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var m in typeof(PlayerApartmentController).GetMethods(flags))
                if (System.Array.IndexOf(playerNames, m.Name) >= 0) yield return m;
            foreach (var m in typeof(NewRoom).GetMethods(flags))
                if (System.Array.IndexOf(roomNames, m.Name) >= 0) yield return m;
        }

        static string Show(object o)
        {
            if (o == null) return "null";
            try
            {
                var t = o.GetType();
                if (t.IsPrimitive || t.IsEnum || o is string || o is Vector3 || o is Vector3Int) return o.ToString();
                var sb = new System.Text.StringBuilder(t.Name);
                foreach (var pn in new[] { "name", "roomID", "id", "presetName", "preset", "angle", "anchorNode", "nodeCoord", "worldPos", "wPos" })
                {
                    var p = t.GetProperty(pn);
                    if (p == null || p.GetIndexParameters().Length > 0) continue;
                    object v = null;
                    try { v = p.GetValue(o, null); } catch (Exception) { continue; }
                    if (v == null) continue;
                    string sv = (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum || v is Vector3 || v is Vector3Int) ? v.ToString() : v.GetType().Name;
                    sb.Append(' ').Append(pn).Append('=').Append(sv);
                }
                return sb.ToString();
            }
            catch (Exception) { return o.GetType().Name; }
        }

        [HarmonyPostfix]
        public static void Postfix(System.Reflection.MethodBase __originalMethod, object __instance, object[] __args)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("[APTTRACE] ").Append(__originalMethod.DeclaringType.Name).Append('.').Append(__originalMethod.Name).Append('(');
                if (__args != null) for (int i = 0; i < __args.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Show(__args[i])); }
                sb.Append(')');
                if (__originalMethod.DeclaringType == typeof(NewRoom)) sb.Append(" | room: ").Append(Show(__instance));
                if (__originalMethod.DeclaringType == typeof(PlayerApartmentController))
                {
                    foreach (var fn in new[] { "furnPlacement", "decoratingRoom", "furnPlacementRoom", "decoratingType", "decoratingMaterial" })
                    {
                        var p = __instance.GetType().GetProperty(fn);
                        if (p == null) continue;
                        object v = null; try { v = p.GetValue(__instance, null); } catch (Exception __e) { Err.Swallow(__e); }
                        sb.Append(" | ").Append(fn).Append('=').Append(Show(v));
                    }
                }
                Plugin.Dbg(sb.ToString());
            }
            catch (Exception e) { Plugin.L.LogWarning($"[APTTRACE] {e.Message}"); }
        }
    }

    internal static class Dumper
    {
        internal static void Run()
        {
            try
            {
                var city = CityData.Instance;
                bool first = false, moving = false, running = false;

                for (int i = 0; i < city.citizenDirectory.Count; i++)
                {
                    var c = city.citizenDirectory[i];
                    if (c == null) continue;

                    if (!first)
                    {
                        first = true;
                        DumpAnimator("первый NPC", c, true);
                    }
                    if (!moving && c.isMoving && !c.isRunning)
                    {
                        moving = true;
                        DumpAnimator("идущий NPC", c, false);
                    }
                    if (!running && c.isRunning)
                    {
                        running = true;
                        DumpAnimator("бегущий NPC", c, false);
                    }
                    if (first && moving && running) break;
                }

                if (!moving) Plugin.Dbg("[ANIM] идущих NPC на момент дампа не нашлось");
                if (!running) Plugin.Dbg("[ANIM] бегущих NPC на момент дампа не нашлось");
                Plugin.Dbg("[ANIM] ===== END =====");
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"[ANIM] {e}");
            }
        }

        static void DumpAnimator(string label, Citizen c, bool full)
        {
            try
            {
                var ac = c.animationController;
                if (ac == null || ac.mainAnimator == null)
                {
                    Plugin.Dbg($"[ANIM] {label}: у {c.citizenName} нет аниматора");
                    return;
                }

                var a = ac.mainAnimator;
                string ctrl = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null";
                Plugin.Dbg($"[ANIM] ===== {label}: {c.citizenName} moving={c.isMoving} running={c.isRunning} " +
                                 $"crouched={c.isCrouched} controller={ctrl} layers={a.layerCount} speed={a.speed} =====");

                var ps = a.parameters;
                var line = new StringBuilder();
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    string val;
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            val = a.GetFloat(p.name).ToString("0.###");
                            break;
                        case AnimatorControllerParameterType.Int:
                            val = a.GetInteger(p.name).ToString();
                            break;
                        case AnimatorControllerParameterType.Bool:
                            val = a.GetBool(p.name).ToString();
                            break;
                        default:
                            val = "(trigger)";
                            break;
                    }

                    if (full)
                        Plugin.Dbg($"[ANIM]   {p.name} : {p.type} = {val}");
                    else
                        line.Append(p.name).Append('=').Append(val).Append("; ");
                }

                if (!full) Plugin.Dbg("[ANIM]   " + line);

                if (full)
                {
                    for (int i = 0; i < a.layerCount; i++)
                        Plugin.Dbg($"[ANIM]   слой {i}: {a.GetLayerName(i)} вес={a.GetLayerWeight(i):0.##}");

                    var rc = a.runtimeAnimatorController;
                    if (rc != null)
                    {
                        var clips = rc.animationClips;
                        var sb = new StringBuilder();
                        for (int i = 0; i < clips.Length; i++)
                            if (clips[i] != null) sb.Append(clips[i].name).Append(" | ");
                        Plugin.Dbg($"[ANIM] клипов={clips.Length}: {sb}");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"[ANIM] {label}: {e}");
            }
        }
    }

    internal static class Perf
    {
        sealed class Acc { public double ms, max; public int n; }
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        static readonly Dictionary<string, Acc> parts = new Dictionary<string, Acc>();
        static int frames, slow; static double frameMs, worstMs; static float reportAt = 60f;

        internal static long T() { return clock.ElapsedTicks; }

        static double nested; static int depth;

        internal static void Add(string part, long t0) { AddMs(part, (clock.ElapsedTicks - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency); }

        static void AddMs(string part, double ms)
        {
            if (depth > 0) nested += ms;
            Acc a; if (!parts.TryGetValue(part, out a)) { a = new Acc(); parts[part] = a; }
            a.ms += ms; a.n++; if (ms > a.max) a.max = ms;
        }

        internal static double Enter() { depth++; return nested; }
        internal static void Leave(string part, long t0, double nested0)
        {
            depth--;
            double ms = (clock.ElapsedTicks - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency - (nested - nested0);
            AddMs(part, ms < 0 ? 0 : ms);
        }

        internal static void Frame()
        {
            float dt = Time.unscaledDeltaTime * 1000f;
            frames++; frameMs += dt; if (dt > worstMs) worstMs = dt; if (dt > 50f) slow++;
            float now = Time.realtimeSinceStartup;
            if (now < reportAt) return;
            reportAt = now + 60f;
            bool inWorld = false; try { inWorld = Player.Instance != null && !Net.MenuOpen(); } catch (Exception) { }
            if (inWorld && frames > 0)
            {
                var sb = new StringBuilder();
                double total = 0; foreach (var a in parts.Values) total += a.ms;
                sb.Append($"[PERF] за минуту: кадров {frames} ({frames / 60f:F0} к/с), средний кадр {frameMs / frames:F1} мс, худший {worstMs:F0} мс, дольше 50 мс: {slow}; мод: {total / frames:F2} мс/кадр");
                var list = new List<KeyValuePair<string, Acc>>(parts);
                list.Sort((x, y) => y.Value.ms.CompareTo(x.Value.ms));
                int k = 0;
                foreach (var kv in list)
                {
                    if (k++ >= 8 || kv.Value.ms < 1.0) break;
                    sb.Append($"; {kv.Key} {kv.Value.ms / frames:F2} мс/кадр (худший раз {kv.Value.max:F1} мс)");
                }
                Plugin.L.LogInfo(sb.ToString());
                string errs = Err.Summary();
                if (errs != "") Plugin.L.LogInfo("[PERF] пойманные ошибки за минуту: " + errs);
            }
            parts.Clear(); frames = 0; slow = 0; frameMs = 0; worstMs = 0;
            Err.ResetCounts();
        }
    }

    internal static class Err
    {
        sealed class Site { public int count, total; public string name; }
        static readonly Dictionary<string, Site> sites = new Dictionary<string, Site>();
        static readonly object sync = new object();

        internal static void Swallow(Exception e, [System.Runtime.CompilerServices.CallerMemberName] string member = "",
            [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            try
            {
                string key = Path.GetFileName(file) + ":" + line;
                bool first = false;
                lock (sync)
                {
                    Site s; if (!sites.TryGetValue(key, out s)) { s = new Site { name = member + " (" + key + ")" }; sites[key] = s; first = true; }
                    s.count++; s.total++;
                }
                if (first)
                {
                    string msg = e != null ? e.GetType().Name + ": " + e.Message : "";
                    if (msg.Length > 300) msg = msg.Substring(0, 300) + "...";
                    Plugin.L.LogWarning($"[ERR] {member} ({key}): {msg}");
                }
            }
            catch (Exception) { }
        }

        internal static string Summary()
        {
            lock (sync)
            {
                var list = new List<Site>();
                foreach (var s in sites.Values) if (s.count > 0) list.Add(s);
                if (list.Count == 0) return "";
                list.Sort((a, b) => b.count.CompareTo(a.count));
                var sb = new StringBuilder();
                for (int i = 0; i < list.Count && i < 6; i++) sb.Append(i > 0 ? "; " : "").Append(list[i].name).Append(" x").Append(list[i].count);
                return sb.ToString();
            }
        }

        internal static void ResetCounts() { lock (sync) { foreach (var s in sites.Values) s.count = 0; } }
    }

    internal sealed class CrashLog : ILogListener
    {
        static StreamWriter file;
        static string lastMsg; static int repeats;
        static long beat = Environment.TickCount64;
        static readonly object sync = new object();

        public LogLevel LogLevelFilter => LogLevel.Error | LogLevel.Fatal | LogLevel.Warning;

        internal static void Beat() { beat = Environment.TickCount64; }

        internal static void Start()
        {
            try
            {
                string path = Path.Combine(Paths.BepInExRootPath, "UnityErrors.log");
                file = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
                file.WriteLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} запуск, {Paths.GameRootPath}");
                BepInEx.Logging.Logger.Listeners.Add(new CrashLog());
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Write("FATAL", "домен", e.ExceptionObject?.ToString() ?? "");
                var t = new System.Threading.Thread(Watch) { IsBackground = true, Name = "SODCoop hang watch" };
                t.Start();
                Plugin.L.LogInfo("[CRASH] ошибки Unity пишу в " + path + ", сторож зависаний запущен");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CRASH] журнал ошибок: " + e.Message); }
        }

        static void Write(string level, string src, string msg)
        {
            lock (sync)
            {
                try
                {
                    if (file == null || file.BaseStream.Length > 20 * 1024 * 1024) return;
                    if (msg == lastMsg) { repeats++; return; }
                    if (repeats > 0) file.WriteLine($"    (повторилось ещё {repeats} раз)");
                    lastMsg = msg; repeats = 0;
                    file.WriteLine($"[{DateTime.Now:HH:mm:ss}] {level} {src}: {msg}");
                }
                catch (Exception) { }
            }
        }

        public void LogEvent(object sender, LogEventArgs e)
        {
            string src = e.Source != null ? e.Source.SourceName : "";
            bool unity = src.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0;
            if (unity && (e.Level & (LogLevel.Error | LogLevel.Fatal)) == 0) return;
            if (!unity && src != "SodCoop") return;
            Write(e.Level.ToString(), src, e.Data != null ? e.Data.ToString() : "");
        }

        public void Dispose() { }

        static void Watch()
        {
            long reported = 0, since = 0;
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
                long b = beat, frozen = Environment.TickCount64 - b;
                if (frozen < 8000)
                {
                    if (reported > 0) { Plugin.L.LogWarning($"[HANG] главный поток ожил: стоял {(b - since) / 1000} с"); reported = 0; }
                    continue;
                }
                if (reported == 0) since = b;
                if (reported == 0 || frozen - reported >= 15000)
                {
                    reported = frozen;
                    Plugin.L.LogWarning($"[HANG] главный поток игры не отвечает уже {frozen / 1000} с (сеть: {(Net.Started ? "в игре" : "нет")})");
                }
            }
        }
    }

    internal static class UiDev
    {
        static bool probed;
        static float cmdAt;

        static string PathOf(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 10 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (!probed)
            {
                var mm = MainMenuController.Instance;
                bool vis = false; try { vis = mm != null && mm.mainMenuActive && GameObject.Find("MenuCanvas") != null; } catch (Exception __e) { Err.Swallow(__e); }
                if (vis && now > 8f) { probed = true; try { Probe(); } catch (Exception e) { Plugin.L.LogWarning("[UI] разведка: " + e); } }
            }
            if (!Plugin.CfgDevCommands.Value || now < cmdAt) return;
            cmdAt = now + 0.5f;
            string f = Path.Combine(Paths.BepInExRootPath, "sodcoop_cmd.txt");
            if (!File.Exists(f)) return;
            string[] lines; try { lines = File.ReadAllLines(f); File.Delete(f); } catch (Exception) { return; }
            foreach (var raw in lines)
            {
                var l = (raw ?? "").Trim(); if (l == "") continue;
                Plugin.L.LogInfo("[UI] команда: " + l);
                var parts = l.Split(new[] { ' ' }, 2);
                switch (parts[0])
                {
                    case "shot": ScreenCapture.CaptureScreenshot(Path.Combine(Paths.BepInExRootPath, (parts.Length > 1 ? parts[1] : "shot") + ".png")); break;
                    case "mp": NetMenu.OpenMenu(); break;
                    case "char": CharEditor.OpenEditor(); break;
                    case "close": NetMenu.CloseMenu(); if (CharEditor.Open) CharEditor.CloseEditor(); break;
                    case "probe": Probe(); break;
                    case "nudge": UiKit.NudgeAll(); break;
                    case "page": { int n; if (parts.Length > 1 && int.TryParse(parts[1], out n)) NetMenu.DevPage(n); } break;
                    case "click":
                        {
                            var mp = GameObject.Find("MenuCanvas/MainMenu/MainMenuPanel/" + (parts.Length > 1 ? parts[1] : ""));
                            var b = mp != null ? mp.GetComponent<UnityEngine.UI.Button>() : null;
                            if (b != null) b.onClick.Invoke(); else Plugin.L.LogWarning("[UI] нет кнопки " + (parts.Length > 1 ? parts[1] : ""));
                        }
                        break;
                    case "comp": { int n; if (parts.Length > 1 && int.TryParse(parts[1], out n)) MainMenuController.Instance.SetMenuComponent((MainMenuController.Component)n); } break;
                }
            }
        }

        static void Line(StringBuilder sb, Transform t, int depth)
        {
            RectTransform rt = null; try { rt = t.TryCast<RectTransform>(); } catch (Exception __e) { Err.Swallow(__e); }
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
            if (rt != null) sb.Append($" pos={rt.anchoredPosition} size={rt.sizeDelta} amin={rt.anchorMin} amax={rt.anchorMax} piv={rt.pivot} scale={rt.localScale}");
            var comps = t.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                string tn = c.GetIl2CppType().Name;
                if (tn == "RectTransform" || tn == "Transform" || tn == "CanvasRenderer") continue;
                sb.Append(" [").Append(tn);
                try
                {
                    var img = c.TryCast<UnityEngine.UI.Image>();
                    if (img != null)
                    {
                        var sp = img.sprite;
                        sb.Append($" sprite={(sp != null ? sp.name : "null")}");
                        if (sp != null) sb.Append($" tex={(sp.texture != null ? sp.texture.name + " " + sp.texture.width + "x" + sp.texture.height : "null")} rect={sp.rect} border={sp.border}");
                        sb.Append($" col={img.color} type={img.type} mat={(img.material != null ? img.material.name : "null")}");
                    }
                    var tmp = c.TryCast<TMPro.TextMeshProUGUI>();
                    if (tmp != null)
                    {
                        var fa = tmp.font;
                        sb.Append($" font={(fa != null ? fa.name : "null")} src={(fa != null && fa.sourceFontFile != null ? fa.sourceFontFile.name : "null")} size={tmp.fontSize} col={tmp.color} style={tmp.fontStyle} align={tmp.alignment} cs={tmp.characterSpacing} text='{(tmp.text ?? "").Replace("\n", " ")}'");
                        try { var fm = tmp.fontSharedMaterial; if (fm != null) sb.Append($" fmat={fm.name}"); } catch (Exception __e) { Err.Swallow(__e); }
                    }
                    var vl = c.TryCast<UnityEngine.UI.VerticalLayoutGroup>();
                    if (vl != null) sb.Append($" spacing={vl.spacing} pad={vl.padding} ctrlH={vl.childControlHeight} forceH={vl.childForceExpandHeight}");
                    var hl = c.TryCast<UnityEngine.UI.HorizontalLayoutGroup>();
                    if (hl != null) sb.Append($" spacing={hl.spacing}");
                    var le = c.TryCast<UnityEngine.UI.LayoutElement>();
                    if (le != null) sb.Append($" pref={le.preferredWidth}x{le.preferredHeight} min={le.minWidth}x{le.minHeight} ignore={le.ignoreLayout}");
                    var bc = c.TryCast<ButtonController>();
                    if (bc != null) sb.Append($" auto={bc.useAutomaticText} dict={bc.textDictionary}/{bc.textReference} base={bc.baseColour}");
                }
                catch (Exception e) { sb.Append(" !" + e.Message); }
                sb.Append("]");
            }
            sb.AppendLine();
        }

        static void Tree(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            if (t == null) return;
            Line(sb, t, depth);
            if (depth >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++) Tree(sb, t.GetChild(i), depth + 1, maxDepth);
        }

        internal static void Probe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== UI PROBE " + DateTime.Now + " screen " + Screen.width + "x" + Screen.height);
            var root = GameObject.Find("MenuCanvas");
            if (root != null)
            {
                var cv = root.GetComponent<Canvas>(); var cs = root.GetComponent<UnityEngine.UI.CanvasScaler>();
                sb.AppendLine($"MenuCanvas canvas scale={(cv != null ? cv.scaleFactor : 0f)} mode={(cv != null ? cv.renderMode.ToString() : "")} scaler={(cs != null ? cs.uiScaleMode + " ref=" + cs.referenceResolution + " match=" + cs.matchWidthOrHeight : "none")}");
                var mm = root.transform.Find("MainMenu");
                if (mm != null)
                {
                    for (int i = 0; i < mm.childCount; i++)
                    {
                        var ch = mm.GetChild(i);
                        int depth = ch.name == "MainMenuPanel" || ch.name == "SaveGamePanel" || ch.name == "SettingsPanel" ? 3 : ch.name == "CharacterSetupPanel" || ch.name == "GenerateCityPanel" ? 6 : 1;
                        Tree(sb, ch, 1, depth);
                    }
                }
            }
            try
            {
                var pm = PopupMessageController.Instance;
                if (pm != null) { sb.AppendLine("=== Popup"); Tree(sb, pm.transform, 0, 5); }
            }
            catch (Exception e) { sb.AppendLine("popup: " + e.Message); }
            try
            {
                sb.AppendLine("=== Fonts");
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>()) if (f != null) sb.AppendLine("Font " + f.name + " dynamic=" + f.dynamic);
                foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) if (f != null) sb.AppendLine("TMP " + f.name + " src=" + (f.sourceFontFile != null ? f.sourceFontFile.name : "null") + " atlas=" + (f.atlasTexture != null ? f.atlasTexture.width + "x" + f.atlasTexture.height : "null"));
            }
            catch (Exception e) { sb.AppendLine("fonts: " + e.Message); }
            string path = Path.Combine(Paths.BepInExRootPath, "SodCoop_ui_probe.txt");
            File.WriteAllText(path, sb.ToString());
            Plugin.L.LogInfo("[UI] разведка меню записана: " + path);
        }
    }

    internal static class Probe
    {
        static void ListDir(StringBuilder sb, string dir, int depth)
        {
            try
            {
                if (!System.IO.Directory.Exists(dir) || depth > 2) return;
                sb.AppendLine("[DIR] " + dir);
                int n = 0;
                foreach (var f in System.IO.Directory.GetFiles(dir)) { if (n++ > 40) break; var fi = new System.IO.FileInfo(f); sb.AppendLine("[FILE]   " + fi.Name + " " + fi.Length + " B " + fi.LastWriteTime); }
                foreach (var d in System.IO.Directory.GetDirectories(dir)) ListDir(sb, d, depth + 1);
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void DumpType(StringBuilder sb, System.Reflection.Assembly asm, string name, bool brief)
        {
            try
            {
                Type t = null;
                foreach (var x in asm.GetTypes()) if (x.Name == name) { t = x; break; }
                if (t == null) { sb.AppendLine("[API] нет типа " + name); return; }
                sb.AppendLine("[API] === " + t.FullName + " : " + (t.BaseType != null ? t.BaseType.Name : ""));
                var f = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var p in t.GetProperties(f)) sb.AppendLine("[API]   prop " + p.PropertyType.Name + " " + p.Name + (p.GetMethod != null && p.GetMethod.IsStatic ? " (static)" : ""));
                if (brief) return;
                foreach (var m in t.GetMethods(f))
                {
                    if (m.IsSpecialName || m.Name.StartsWith("_") || m.Name.StartsWith("NativeMethod")) continue;
                    var ps = m.GetParameters(); var sig = new List<string>();
                    foreach (var q in ps) sig.Add(q.ParameterType.Name + " " + q.Name);
                    sb.AppendLine("[API]   " + (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", sig.ToArray()) + ")");
                }
            }
            catch (Exception e) { sb.AppendLine("[API] " + name + ": " + e.Message); }
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 8 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static string Join<T>(Il2CppSystem.Collections.Generic.List<T> l)
        {
            if (l == null) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); sb.Append(l[i].ToString()); }
            return sb.ToString();
        }

        internal static void Run(string where)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== PROBE " + where + " ===");
            try
            {
                var mm = MainMenuController.Instance;
                if (mm != null)
                {
                    sb.AppendLine("[MENU] mainMenuActive=" + mm.mainMenuActive + " container=" + (mm.mainMenuContainer != null ? PathOf(mm.mainMenuContainer) : "null"));
                    try { sb.AppendLine("[MENU] components=" + mm.components.Count); } catch (Exception __e) { Err.Swallow(__e); }
                    var mroot = GameObject.Find("MenuCanvas");
                    var bs = (mroot != null ? mroot.transform : mm.transform).GetComponentsInChildren<ButtonController>(true);
                    for (int i = 0; i < bs.Length && i < 400; i++)
                    {
                        var b = bs[i]; if (b == null) continue;
                        string txt = ""; try { txt = b.text != null ? b.text.text : ""; } catch (Exception __e) { Err.Swallow(__e); }
                        string dict = ""; try { dict = b.textDictionary + "/" + b.textReference + " auto=" + b.useAutomaticText; } catch (Exception __e) { Err.Swallow(__e); }
                        var rt = b.transform as RectTransform;
                        string rs = rt != null ? ("pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta + " anchors=" + rt.anchorMin + "-" + rt.anchorMax) : "";
                        string ev = "";
                        try
                        {
                            var ub = b.button;
                            if (ub != null)
                            {
                                int n = ub.onClick.GetPersistentEventCount();
                                for (int k = 0; k < n; k++)
                                {
                                    var tg = ub.onClick.GetPersistentTarget(k);
                                    ev += " [" + (tg != null ? tg.GetType().Name + ":" + tg.name : "null") + "." + ub.onClick.GetPersistentMethodName(k) + "]";
                                }
                                if (n == 0) ev = " (нет постоянных обработчиков)";
                            }
                            else ev = " (нет Button)";
                        }
                        catch (Exception e) { ev = " (onClick: " + e.Message + ")"; }
                        sb.AppendLine("[BTN] " + PathOf(b.transform) + " | text='" + txt + "' | active=" + b.gameObject.activeInHierarchy + " | onClick=" + ev);
                    }
                    try
                    {
                        var cs = mm.components;
                        for (int i = 0; i < cs.Count; i++) { var c = cs[i]; if (c != null) sb.AppendLine("[COMP] " + c.component + " rect=" + (c.rect != null ? PathOf(c.rect) : "null")); }
                    }
                    catch (Exception e) { sb.AppendLine("[COMP] ошибка: " + e.Message); }
                }
                else sb.AppendLine("[MENU] MainMenuController.Instance = null");
            }
            catch (Exception e) { sb.AppendLine("[MENU] ошибка: " + e); }

            try
            {
                var all = Toolbox.Instance.allClothes;
                sb.AppendLine("[CLOTHES] allClothes=" + (all != null ? all.Count : -1));
                for (int i = 0; false && all != null && i < all.Count; i++)
                {
                    var c = all[i]; if (c == null) continue;
                    sb.AppendLine("[CL] " + c.name + " | cat=" + Join(c.outfitCategories) + " | gender=" + Join(c.suitableForGenders) + " | build=" + Join(c.suitableForBuilds) + " | covers=" + Join(c.covers) + " | head=" + c.isHead + " | models=" + (c.models != null ? c.models.Count : -1) + " | tags=" + Join(c.tags) + " | hair=" + Join(c.suitableForHairstyle) + " | chance=" + c.baseChance);
                }
            }
            catch (Exception e) { sb.AppendLine("[CLOTHES] ошибка: " + e); }

            try
            {
                var go = PrefabControls.Instance.citizen;
                sb.AppendLine("[PREFAB] citizen=" + (go != null ? go.name : "null"));
                if (go != null)
                {
                    var comps = go.GetComponents<Component>();
                    for (int i = 0; i < comps.Length; i++) sb.AppendLine("[PREFAB]   root: " + comps[i]);
                    sb.AppendLine("[PREFAB] outfitCtrl in children=" + (go.GetComponentInChildren<CitizenOutfitController>(true) != null) + " animator=" + (go.GetComponentInChildren<Animator>(true) != null) + " citizen=" + (go.GetComponent<Citizen>() != null));
                    var h = new GameObject("SodCoop_ProbeHolder"); h.SetActive(false);
                    try
                    {
                        var inst = UnityEngine.Object.Instantiate<GameObject>(go, h.transform);
                        var oc = inst.GetComponentInChildren<CitizenOutfitController>(true);
                        sb.AppendLine("[PREFAB] инстанс создан без города: outfitCtrl=" + (oc != null) + (oc != null ? (" human=" + (oc.human != null) + " anchors=" + oc.anchorConfig.Count) : "") + " renderers=" + inst.GetComponentsInChildren<Renderer>(true).Length);
                        var anim = inst.GetComponentInChildren<Animator>(true);
                        if (anim != null) sb.AppendLine("[PREFAB] animator controller=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null"));
                    }
                    catch (Exception e) { sb.AppendLine("[PREFAB] инстанс не удался: " + e.Message); }
                    UnityEngine.Object.Destroy(h);
                }
            }
            catch (Exception e) { sb.AppendLine("[PREFAB] ошибка: " + e); }

            try
            {
                var city = CityData.Instance;
                if (city != null && city.citizenDirectory != null && city.citizenDirectory.Count > 0)
                {
                    Citizen cit = null;
                    for (int i = 0; i < city.citizenDirectory.Count; i++) { var c = city.citizenDirectory[i]; if (c != null && c.outfitController != null && c.descriptors != null) { cit = c; break; } }
                    if (cit != null)
                    {
                        var d = cit.descriptors;
                        sb.AppendLine("[CIT] " + cit.citizenName + " gender=" + cit.gender + " genderScale=" + cit.genderScale + " build=" + d.build + " height=" + d.height + " heightCM=" + d.heightCM + " skin=" + d.skinColour + " hairStyle=" + d.hairType + " hairCat=" + d.hairColourCategory + " hairCol=" + d.hairColour + " eyes=" + d.eyeColour + " footwear=" + d.footwear + " facial=" + (d.facialFeatures != null ? d.facialFeatures.Count : -1));
                        var oc = cit.outfitController;
                        sb.AppendLine("[CIT] currentOutfit=" + oc.currentOutfit + " loaded=" + oc.loadedOutfit + " outfits=" + oc.outfits.Count + " hair=" + (oc.currentHair != null ? oc.currentHair.name : "-") + " hat=" + (oc.currentHat != null ? oc.currentHat.name : "-"));
                        for (int i = 0; i < oc.outfits.Count; i++)
                        {
                            var o = oc.outfits[i]; if (o == null) continue;
                            var line = new StringBuilder("[OUTFIT] " + o.category + ": ");
                            for (int k = 0; k < o.clothes.Count; k++) { var oc2 = o.clothes[k]; line.Append(oc2.clothes).Append(" base=").Append(oc2.baseColor).Append(" c1=").Append(oc2.color1).Append("; "); }
                            sb.AppendLine(line.ToString());
                        }
                    }
                }
                else sb.AppendLine("[CIT] города нет (это меню) - горожан не смотрю");
            }
            catch (Exception e) { sb.AppendLine("[CIT] ошибка: " + e); }

            try
            {
                var pl = Player.Instance;
                if (pl != null) sb.AppendLine("[PLAYER] gender=" + pl.gender + " descriptors=" + (pl.descriptors != null) + " outfitController=" + (pl.outfitController != null));
            }
            catch (Exception __e) { Err.Swallow(__e); }

            try
            {
                var asm = typeof(MainMenuController).Assembly;
                var names = new List<string>();
                foreach (var t in asm.GetTypes())
                {
                    string n = t.Name;
                    if (n.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("CityConstruct", StringComparison.OrdinalIgnoreCase) >= 0)
                        if (!n.StartsWith("<") && !n.Contains("__")) names.Add(n);
                }
                sb.AppendLine("[API] типы с Save/Load: " + string.Join(", ", names.ToArray()));
                string[] want = { "SaveStateController", "SaveGameEntryController", "CityConstructor", "Game", "StateSaveData", "CitySaveData", "SessionData" };
                foreach (var w in want) DumpType(sb, asm, w, w == "SessionData");
            }
            catch (Exception e) { sb.AppendLine("[API] ошибка: " + e.Message); }

            try
            {
                sb.AppendLine("[PATH] persistentDataPath=" + Application.persistentDataPath + " dataPath=" + Application.dataPath);
                ListDir(sb, Application.persistentDataPath, 0);
                try { ListDir(sb, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? "", "Save"), 0); } catch (Exception __e) { Err.Swallow(__e); }
            }
            catch (Exception e) { sb.AppendLine("[PATH] ошибка: " + e.Message); }

            try
            {
                var mm2 = MainMenuController.Instance;
                if (mm2 != null)
                {
                    var l1 = mm2.spawnedLoadGames; var l2 = mm2.spawnedSaveGames;
                    sb.AppendLine("[SAVES] load=" + (l1 != null ? l1.Count : -1) + " save=" + (l2 != null ? l2.Count : -1) + " selected=" + (mm2.selectedSave != null ? mm2.selectedSave.name : "null"));
                    for (int i = 0; l1 != null && i < l1.Count && i < 20; i++) { var e1 = l1[i]; if (e1 != null) sb.AppendLine("[SAVES]   load[" + i + "] " + e1.name + " " + e1.GetType().Name); }
                    try { var sec = mm2.selectedSave ?? (l1 != null && l1.Count > 0 ? l1[0] : null); if (sec != null) DumpType(sb, typeof(MainMenuController).Assembly, "SaveGameEntryController", false); } catch (Exception __e) { Err.Swallow(__e); }
                }
            }
            catch (Exception e) { sb.AppendLine("[SAVES] ошибка: " + e.Message); }

            try
            {
                string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "SodCoop_probe_" + (where.StartsWith("гл") ? "menu" : "game") + ".txt");
                System.IO.File.WriteAllText(path, sb.ToString());
                Plugin.L.LogInfo("[PROBE] записано: " + path);
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROBE] запись: " + e.Message); }
        }
    }
}

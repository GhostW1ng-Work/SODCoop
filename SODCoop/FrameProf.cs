using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodCoop
{
    internal static class FrameProf
    {
        sealed class Stat { public string Name; public long Ticks, Calls, Max; }

        static readonly string[] Targets =
        {
            "Player.Update", "MurderController.Update", "InterfaceController.Update", "SessionData.Update", "InteractionController.Update",
            "InteractableController.Update", "MapController.Update", "CitizenBehaviour.Update", "CitizenBehaviour.LateUpdate",
            "LightCullingController.Update", "GeometryCullingController.Update", "MeshPoolingController.Update", "ObjectPoolingController.LateUpdate",
            "CameraController.LateUpdate", "SecuritySystem.Update", "SpeechBubbleController.Update", "SpeechController.Update",
            "FirstPersonItemController.Update", "FirstPersonItemController.LateUpdate", "RainParticleController.Update", "RainSheetController.Update",
            "PrecipitationParticleSystemController.Update", "LightController.Update", "PlayerInteresectingGeometryJobcast.Update", "StatusController.Update",
            "TelephoneController.Update", "NeonSignController.Update", "AnimatedTextureController.Update", "GameMessageController.Update",
            "RagdollPositionUpdater.Update", "PopupMessageController.Update", "LaserGridController.Update", "ReactionIndicatorController.Update",
            "SelectionIconController.Update", "SoundIndicatorController.Update", "MusicController.Update", "CasePanelController.Update"
        };

        static readonly Dictionary<MethodBase, Stat> stats = new Dictionary<MethodBase, Stat>();
        static bool on;
        static float windowAt, frameSum, frameMax;
        static int frames, gcLast, minute;

        internal static void Install(Harmony h)
        {
            if (!Plugin.CfgFrameProf.Value) return;
            var pre = new HarmonyMethod(typeof(FrameProf).GetMethod(nameof(Pre), BindingFlags.Static | BindingFlags.Public));
            var post = new HarmonyMethod(typeof(FrameProf).GetMethod(nameof(Post), BindingFlags.Static | BindingFlags.Public));
            int ok = 0, bad = 0;
            var asm = typeof(Player).Assembly;
            foreach (var entry in Targets)
            {
                int dot = entry.IndexOf('.');
                string typeName = entry.Substring(0, dot), name = entry.Substring(dot + 1);
                var t = asm.GetType(typeName);
                if (t == null) { bad++; continue; }
                MethodInfo m = null;
                try { m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null); } catch (Exception __e) { Err.Swallow(__e); }
                if (m == null) { bad++; continue; }
                try
                {
                    stats[m] = new Stat { Name = entry };
                    h.Patch(m, pre, post);
                    ok++;
                }
                catch (Exception e) { bad++; stats.Remove(m); Plugin.Dbg($"[FPROF] {entry}: {e.Message}"); }
            }
            on = true;
            Plugin.L.LogInfo($"[FPROF] профайлер кадра включён: методов {ok}, не удалось {bad}");
        }

        public static void Pre(out long __state) { __state = Stopwatch.GetTimestamp(); }

        public static void Post(MethodBase __originalMethod, long __state)
        {
            long dt = Stopwatch.GetTimestamp() - __state;
            Stat s;
            if (!stats.TryGetValue(__originalMethod, out s)) return;
            s.Ticks += dt; s.Calls++;
            if (dt > s.Max) s.Max = dt;
        }

        internal static void Tick()
        {
            if (!on) return;
            float dt = Time.unscaledDeltaTime;
            frames++; frameSum += dt; if (dt > frameMax) frameMax = dt;
            float now = Time.realtimeSinceStartup;
            if (windowAt == 0f) { windowAt = now + 60f; return; }
            if (now < windowAt) return;
            windowAt = now + 60f;
            minute++;
            double toMs = 1000.0 / Stopwatch.Frequency;
            int gc = 0;
            try { gc = Il2CppSystem.GC.CollectionCount(0); } catch (Exception __e) { Err.Swallow(__e); }
            double total = stats.Values.Sum(x => x.Ticks) * toMs / Math.Max(frames, 1);
            var top = stats.Values.OrderByDescending(x => x.Ticks).Take(14).Where(x => x.Ticks > 0)
                .Select(x => $"{x.Name} {x.Ticks * toMs / Math.Max(frames, 1):F2} (x{(double)x.Calls / Math.Max(frames, 1):F0}, макс {x.Max * toMs:F0})");
            double frame = frameSum * 1000.0 / Math.Max(frames, 1);
            Plugin.L.LogInfo($"[FPROF] мин {minute}: кадр {frame:F1} мс, макс {frameMax * 1000f:F0}; Update игры {total:F1} мс, вне их {frame - total:F1} мс; сборок мусора за минуту {gc - gcLast}; топ мс/кадр: {string.Join("; ", top)}");
            gcLast = gc; frames = 0; frameSum = 0f; frameMax = 0f;
            foreach (var s in stats.Values) { s.Ticks = 0; s.Calls = 0; s.Max = 0; }
        }
    }
}

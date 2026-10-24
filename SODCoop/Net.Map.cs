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
        static Transform mapTracked;
        static Sprite mapSprite;
        static float lastMapTry; static bool mapFailLogged;

        static Sprite MakeMapSprite()
        {
            if (mapSprite != null) return mapSprite;
            int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    Color col = new Color(0f, 0f, 0f, 0f);
                    if (d <= 15.5f) col = d >= 12f ? new Color(0.25f, 0.25f, 0.25f, 1f) : new Color(1f, 1f, 1f, 1f);
                    px[y * n + x] = col;
                }
            tex.SetPixels(px); tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            mapSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return mapSprite;
        }

        static readonly Color[] PlayerColors =
        {
            new Color(1f, 0.4f, 0.8f, 1f),
            new Color(0.2f, 1f, 0.3f, 1f),
            new Color(0.25f, 0.55f, 1f, 1f),
            new Color(1f, 0.6f, 0.15f, 1f),
            new Color(1f, 0.95f, 0.25f, 1f),
            new Color(1f, 0.3f, 0.3f, 1f),
            new Color(0.3f, 1f, 0.95f, 1f),
            new Color(1f, 1f, 1f, 1f),
        };

        internal static Color PlayerColor(int pid) { return PlayerColors[((pid % PlayerColors.Length) + PlayerColors.Length) % PlayerColors.Length]; }

        static readonly Dictionary<IntPtr, int> mapButtonPid = new Dictionary<IntPtr, int>();

        internal static bool OnMapMarkerPressed(ButtonController b)
        {
            int pid;
            if (b == null || !mapButtonPid.TryGetValue(b.Pointer, out pid)) return false;
            Toast(PlayerLabel(pid));
            return true;
        }

        static void MapLabel(MapController mc, Transform t, int pid)
        {
            Il2CppSystem.Collections.Generic.List<RectTransform> list;
            if (mc.dynamicTrackedObjects == null || !mc.dynamicTrackedObjects.TryGetValue(t, out list) || list == null) return;
            string name = PlayerLabel(pid);
            for (int i = 0; i < list.Count; i++)
            {
                var rt = list[i]; if (rt == null) continue;
                var b = rt.GetComponent<ButtonController>();
                if (b == null) b = rt.GetComponentInChildren<ButtonController>();
                if (b == null) continue;
                mapButtonPid[b.Pointer] = pid;
                var tip = b.tooltip;
                if (tip == null || tip.mainText == name) continue;
                tip.useMainDictionaryEntry = false;
                tip.mainText = name;
                tip.tooltipEnabled = true;
            }
        }

        static readonly Dictionary<IntPtr, Color> selfMarkOrig = new Dictionary<IntPtr, Color>();

        static int selfTintLogged = -1;

        static void MapTintSelf(bool on)
        {
            var mc = MapController.Instance;
            if (mc == null) return;
            var imgs = new List<UnityEngine.UI.Image>();
            int keys = 0;
            var pl = Player.Instance;
            if (pl != null && mc.dynamicTrackedObjects != null)
            {
                var pt = pl.transform;
                foreach (var kv in mc.dynamicTrackedObjects)
                {
                    var t = kv.Key;
                    if (t == null || kv.Value == null) continue;
                    if (t != pt && !t.IsChildOf(pt)) continue;
                    keys++;
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        var rt = kv.Value[i]; if (rt == null) continue;
                        foreach (var img in rt.GetComponentsInChildren<UnityEngine.UI.Image>(true)) if (img != null) imgs.Add(img);
                    }
                }
            }
            if (on && selfTintLogged != imgs.Count)
            {
                selfTintLogged = imgs.Count;
                Plugin.L.LogInfo($"[MAP] моя метка на карте: объектов {keys}, картинок {imgs.Count}");
            }
            var col = PlayerColor(MyPid);
            foreach (var img in imgs)
            {
                Color orig;
                if (!selfMarkOrig.TryGetValue(img.Pointer, out orig)) { orig = img.color; selfMarkOrig[img.Pointer] = orig; }
                var want = on ? new Color(col.r, col.g, col.b, orig.a) : orig;
                if (img.color != want) img.color = want;
            }
            if (!on) selfMarkOrig.Clear();
        }

        internal static void MapSessionEnded()
        {
            try { MapTintSelf(false); } catch (Exception __e) { Err.Swallow(__e); }
            mapButtonPid.Clear();
            selfTintLogged = -1;
        }

        static void MapUntrack()
        {
            var objs = new List<GameObject>();
            try
            {
                var mc = MapController.Instance;
                if ((object)mapTracked != null && mc != null)
                {
                    Il2CppSystem.Collections.Generic.List<RectTransform> list;
                    if (mc.dynamicTrackedObjects != null && mc.dynamicTrackedObjects.TryGetValue(mapTracked, out list) && list != null)
                        for (int i = 0; i < list.Count; i++) if (list[i] != null) objs.Add(list[i].gameObject);
                    mc.RemoveTrackedObject(mapTracked);
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            foreach (var go in objs)
            {
                try
                {
                    var b = go.GetComponent<ButtonController>();
                    if (b != null) mapButtonPid.Remove(b.Pointer);
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            mapTracked = null;
        }

        static void MapTick()
        {
            if (Time.unscaledTime - lastMapTry < 1f) return;
            lastMapTry = Time.unscaledTime;
            try { MapTintSelf(Plugin.CfgMapIcon.Value); } catch (Exception __e) { Err.Swallow(__e); }
            for (int i = 0; i < players.Count; i++)
            {
                Enter(players[i]);
                try { MapTickOne(); } catch (Exception __e) { Err.Swallow(__e); }
            }
            Leave();
        }

        static void MapTickOne()
        {
            if (!Plugin.CfgMapIcon.Value) { if ((object)mapTracked != null) MapUntrack(); return; }
            if (avatar == null) { if ((object)mapTracked != null) MapUntrack(); return; }
            var mc = MapController.Instance;
            if (mc == null) return;
            int pid = curRp != null ? curRp.pid : 0;
            if (mapTracked != null && mapTracked == avatar.transform) { MapLabel(mc, mapTracked, pid); return; }
            if ((object)mapTracked != null) MapUntrack();
            try
            {
                float sz = Mathf.Max(6f, Plugin.CfgMapIconSize.Value);
                mc.AddNewTrackedObject(avatar.transform, MakeMapSprite(), new Vector2(sz, sz), PlayerColor(pid), true, null);
                mapTracked = avatar.transform;
                MapLabel(mc, mapTracked, pid);
                Plugin.L.LogInfo($"[MAP] метка игрока {pid} ({PlayerLabel(pid)}) добавлена на карту");
            }
            catch (Exception e)
            {
                if (!mapFailLogged) { mapFailLogged = true; Plugin.L.LogWarning($"[MAP] не удалось добавить иконку: {e.Message}"); }
            }
        }
    }
}

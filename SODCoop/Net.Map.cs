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

        static void MapUntrack()
        {
            try
            {
                if (mapTracked != null && MapController.Instance != null) MapController.Instance.RemoveTrackedObject(mapTracked);
            }
            catch (Exception __e) { Err.Swallow(__e); }
            mapTracked = null;
        }

        static void MapTick()
        {
            if (Time.unscaledTime - lastMapTry < 1f) return;
            lastMapTry = Time.unscaledTime;
            for (int i = 0; i < players.Count; i++)
            {
                Enter(players[i]);
                try { MapTickOne(); } catch (Exception __e) { Err.Swallow(__e); }
            }
            Leave();
        }

        static void MapTickOne()
        {
            if (!Plugin.CfgMapIcon.Value) { if (mapTracked != null) MapUntrack(); return; }
            if (avatar == null) { if (mapTracked != null) MapUntrack(); return; }
            var mc = MapController.Instance;
            if (mc == null) return;
            if (mapTracked != null && mapTracked == avatar.transform) return;
            if (mapTracked != null) MapUntrack();
            try
            {
                float sz = Mathf.Max(6f, Plugin.CfgMapIconSize.Value);
                mc.AddNewTrackedObject(avatar.transform, MakeMapSprite(), new Vector2(sz, sz), new Color(0.2f, 1f, 0.3f, 1f), true, null);
                mapTracked = avatar.transform;
                Plugin.L.LogInfo("[MAP] иконка второго игрока добавлена на карту");
            }
            catch (Exception e)
            {
                if (!mapFailLogged) { mapFailLogged = true; Plugin.L.LogWarning($"[MAP] не удалось добавить иконку: {e.Message}"); }
            }
        }
    }
}

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
        static bool applyingKnock;

        internal static void OnLocalKnock(NewDoor door, Actor who, int knocks, float urgency)
        {
            if (applyingKnock || manager == null || manager.ConnectedPeersCount == 0 || door == null || who == null) return;
            var me = Player.Instance;
            if (me == null || who.Pointer != me.Pointer) return;
            int key = DoorKey(door);
            if (key < 0) return;
            var w = new NetDataWriter(); w.Put(MsgKnock); w.Put(key); w.Put(knocks); w.Put(urgency);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo(knocks < 0 ? $"[KNOCK] выбиваю дверь {key}, сообщаю другим" : $"[KNOCK] стучу в дверь {key} ({knocks} раз), сообщаю другим");
        }

        static void OnKnockMsg(RP rp, NetDataReader r)
        {
            int key = r.GetInt(), knocks = r.GetInt(); float urgency = r.GetFloat();
            var door = DoorByKey(key);
            if (door == null) { Plugin.Dbg($"[KNOCK] дверь {key} ещё не подгружена, стук пропущен"); return; }
            Actor who = null;
            try
            {
                if (IsHost) who = puppet;
                else { Citizen c; if (puppetId >= 0 && npcById != null && npcById.TryGetValue(puppetId, out c)) who = c; }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            if (who == null) who = Player.Instance;
            if (who == null) return;
            if (knocks < 0)
            {
                var ev = door.preset != null ? door.preset.doorBargeContact : null;
                var wall = door.wall;
                if (ev != null && wall != null)
                {
                    applyingRemote = true;
                    try { AudioController.Instance.PlayWorldOneShot(ev, who, wall.node, wall.position); }
                    catch (Exception e) { Plugin.L.LogWarning("[KNOCK] звук удара: " + e.Message); }
                    finally { applyingRemote = false; }
                }
                Plugin.L.LogInfo($"[KNOCK] игрок {rp.pid} выбивает дверь {key}");
                return;
            }
            var gc = GameplayController.Instance;
            if (gc == null) return;
            applyingKnock = true;
            try { gc.KnockOnDoor(door, who, knocks, urgency); }
            catch (Exception e) { Plugin.L.LogWarning("[KNOCK] " + e.Message); }
            finally { applyingKnock = false; }
            Plugin.L.LogInfo($"[KNOCK] игрок {rp.pid} стучит в дверь {key} ({knocks} раз)");
        }
    }
}

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
        static int atStage; static float atAt, atInGameAt, atCaseAt; static bool atCaseDone;

        static void AtLog(string m) { Plugin.L.LogInfo("[AUTOTEST] " + m); }

        internal static void AutoTestTick()
        {
            string mode = (Plugin.CfgAutoTest.Value ?? "").Trim().ToLowerInvariant();
            if (mode != "host" && mode != "client") return;
            float now = Time.realtimeSinceStartup;
            Application.runInBackground = true;
            if (now < atAt) return;
            bool menu = InMainMenu();
            if (atStage == 0)
            {
                if (!menu) { atAt = now + 1f; return; }
                atAt = now + 5f; atStage = 1; AtLog("главное меню открыто, режим " + mode);
                return;
            }
            if (atStage == 1)
            {
                if (mode == "host")
                {
                    var saves = MenuAuto.SaveNames();
                    if (saves.Count == 0) { AtLog("нет сейвов"); atStage = 99; return; }
                    ApplyHost("autotest", "", Plugin.CfgPort.Value);
                    MenuAuto.LoadSave(saves[0]);
                    AtLog("хост: гружу " + saves[0]);
                }
                else
                {
                    ApplyClient("127.0.0.1", Plugin.CfgPort.Value, "");
                    StartTransfer();
                    AtLog("клиент: подключаюсь");
                }
                atStage = 2; atAt = now + 5f;
                return;
            }
            bool inGame = !menu && Player.Instance != null && manager != null && manager.ConnectedPeersCount > 0;
            if (atStage == 2)
            {
                if (mode == "client" && !inGame && now > atInGameAt + 0f && serverPeer == null && !xferMode && manager == null && RtMode == NetMode.Off) { atStage = 1; atAt = now + 20f; AtLog("клиент: хост ещё не готов, повторю"); return; }
                if (!inGame) { atAt = now + 1f; return; }
                atStage = 3; atInGameAt = now; atAt = now + 40f; AtLog("в игре, второй игрок на связи - жду 40 с");
                return;
            }
            if (atStage >= 3 && atStage <= 5) { try { var sd = SessionData.Instance; if (sd != null && !sd.play) sd.ResumeGame(); } catch (Exception __e) { Err.Swallow(__e); } }
            if (mode == "host" && atStage >= 3 && atStage <= 5 && !atCaseDone && now >= atCaseAt)
            {
                atCaseAt = now + 10f;
                try
                {
                    var mc = MurderController.Instance; var cm = mc != null ? mc.GetCurrentMurder() : null;
                    if (cm != null && cm.location != null)
                    {
                        atCaseDone = true;
                        AtLog($"убийство {cm.murderID} ({cm.state}) место {cm.location.name}, дело у меня: {(mc.currentActiveCase != null)} - завожу дело");
                        GameplayController.Instance.NewMurderCaseNotify(cm.location);
                    }
                }
                catch (Exception e) { atCaseDone = true; AtLog("дело об убийстве: " + e.Message); }
            }
            if (atStage == 3)
            {
                if (mode == "client")
                {
                    foreach (var part in (Plugin.CfgAutoTestJobs.Value ?? "").Split(','))
                    {
                        int id; if (!int.TryParse(part.Trim(), out id)) continue;
                        var j = JobById(id);
                        if (j == null) { AtLog($"задание {id}: нет"); continue; }
                        AtLog($"задание {id} ({(j.preset != null ? j.preset.name : "?")}, {j.state}, взято {j.accepted}): беру");
                        try { if (!j.accepted) j.AcceptJob(); } catch (Exception e) { AtLog($"задание {id}: взять - {e.Message}"); }
                    }
                }
                atStage = 4; atAt = now + 25f;
                return;
            }
            if (atStage == 4)
            {
                if (mode == "client")
                {
                    foreach (var part in (Plugin.CfgAutoTestJobs.Value ?? "").Split(','))
                    {
                        int id; if (!int.TryParse(part.Trim(), out id)) continue;
                        var j = JobById(id); var c = j != null ? j.thisCase : null;
                        if (c == null) { AtLog($"задание {id}: дела нет"); continue; }
                        var call = new System.Collections.Generic.List<Objective>();
                        if (c.currentActiveObjectives != null) foreach (var o in c.currentActiveObjectives) if (o != null && !o.isComplete && o.queueElement != null && o.queueElement.entryRef == "Call") call.Add(o);
                        foreach (var o in call) { try { o.Complete(); } catch (Exception e) { AtLog("цель: " + e.Message); } }
                        AtLog($"задание {id}: засчитал звонок ({call.Count} целей)");
                    }
                }
                atStage = 5; atAt = now + 150f;
                return;
            }
            if (atStage == 5)
            {
                var ctl = SideJobController.Instance;
                if (ctl != null && ctl.allJobsDictionary != null)
                    foreach (var kv in ctl.allJobsDictionary)
                    {
                        var j = kv.Value; if (j == null || !j.accepted) continue;
                        var sb = new System.Text.StringBuilder();
                        foreach (var x in JobChoices(j)) sb.Append(x.k).Append('=').Append(x.a).Append(x.b != 0 || x.c != 0 ? "," + x.b + "," + x.c : "").Append(' ');
                        sb.Append("| цели: ");
                        var c = j.thisCase;
                        if (c != null && c.currentActiveObjectives != null) foreach (var o in c.currentActiveObjectives) if (o != null) sb.Append('[').Append(o.queueElement != null ? o.queueElement.entryRef : "?").Append(": ").Append(o.name).Append(o.isComplete ? " (выполнена)" : "").Append("] ");
                        AtLog($"ИТОГ задание {j.jobID} этап {j.phase}: выбор {sb}");
                    }
                try
                {
                    var mc = MurderController.Instance; var cm = mc != null ? mc.GetCurrentMurder() : null;
                    AtLog($"ИТОГ убийство: {(cm != null ? cm.murderID + " " + cm.state + " убийца " + cm.murdererID + " жертва " + cm.victimID : "нет")}, дело у меня: {(mc != null && mc.currentActiveCase != null ? mc.currentActiveCase.name : "нет")}");
                }
                catch (Exception e) { AtLog("убийство: " + e.Message); }
                atStage = 6; atAt = now + (mode == "host" ? 15f : 5f);
                return;
            }
            if (atStage == 6) { AtLog("готово, выхожу"); atStage = 99; Application.Quit(); }
        }
    }
}

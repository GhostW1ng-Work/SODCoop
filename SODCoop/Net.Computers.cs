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
        static bool applyingComputer;
        static System.Collections.Generic.Dictionary<string, CruncherAppPreset> appByName;

        internal static void OnComputer(ComputerController cc)
        {
            if (applyingComputer || cc == null || !cc.playerControlled || manager == null || manager.ConnectedPeersCount == 0) return;
            var it = cc.ic != null ? cc.ic.interactable : null;
            if (it == null) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)14); w.Put(it.id);
            w.Put(cc.currentApp != null ? cc.currentApp.name : "");
            var li = cc.loggedInAs;
            w.Put(li != null && !li.isPlayer ? li.humanID : -1);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.Dbg($"[PC] компьютер {it.id}: программа '{(cc.currentApp != null ? cc.currentApp.name : "")}', вход: {(li != null ? li.humanID.ToString() : "нет")}");
        }

        static void OnComputerMsg(NetDataReader r, int pid)
        {
            int id = r.GetInt(); string app = r.GetString(); int login = r.GetInt();
            var it = FindById(id);
            if (it == null) { try { CityData.Instance.savableInteractableDictionary.TryGetValue(id, out it); } catch (Exception __e) { Err.Swallow(__e); } }
            var cc = it != null && it.controller != null ? it.controller.computer : null;
            if (cc == null) return;
            if (appByName == null)
            {
                appByName = new System.Collections.Generic.Dictionary<string, CruncherAppPreset>();
                foreach (var a in Resources.FindObjectsOfTypeAll<CruncherAppPreset>()) if (a != null && !appByName.ContainsKey(a.name)) appByName[a.name] = a;
            }
            applyingComputer = true;
            try
            {
                Human h = null; if (login >= 0) CityData.Instance.GetHuman(login, out h, false);
                if (h != null && (cc.loggedInAs == null || cc.loggedInAs.humanID != login)) cc.SetLoggedIn(h);
                CruncherAppPreset ap;
                if (app != "" && appByName.TryGetValue(app, out ap) && (cc.currentApp == null || cc.currentApp.name != app)) cc.SetComputerApp(ap, true);
                Plugin.Dbg($"[PC] компьютер {id}: у игрока {pid} программа '{app}'");
            }
            catch (Exception e) { Plugin.L.LogWarning("[PC] " + e.Message); }
            finally { applyingComputer = false; }
        }

        static bool applyingPage;

        internal static void OnPage(EvidenceMultiPage ev, int page)
        {
            if (applyingPage || ev == null || manager == null || manager.ConnectedPeersCount == 0) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)15); w.Put(ev.evID ?? ""); w.Put(EvidenceSig(ev)); w.Put(page);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void OnPageMsg(NetDataReader r)
        {
            string id = r.GetString(); string sig = r.GetString(); int page = r.GetInt();
            Evidence ev = null;
            try { Toolbox.Instance.TryGetEvidence(id, out ev); } catch (Exception __e) { Err.Swallow(__e); }
            if (sig != "" && (ev == null || EvidenceSig(ev) != sig)) ev = FindEvidenceBySig(sig);
            var mp = ev != null ? ev.TryCast<EvidenceMultiPage>() : null;
            if (mp == null || mp.page == page) return;
            applyingPage = true;
            try { mp.SetPage(page, false); } catch (Exception e) { Plugin.L.LogWarning("[PAGE] " + e.Message); }
            finally { applyingPage = false; }
        }

        static System.Collections.Generic.List<string> caseAnswers;
        static float caseAnswersAt;
        static GUIStyle caseStyle, caseShadow;

        static void ToggleCaseAnswers()
        {
            if (caseAnswers != null) { caseAnswers = null; return; }
            caseAnswers = BuildCaseAnswers(); caseAnswersAt = Time.unscaledTime;
            Plugin.L.LogInfo("[CHEAT] ответы по делам:\n  " + string.Join("\n  ", caseAnswers.ToArray()));
        }

        static System.Collections.Generic.List<string> BuildCaseAnswers()
        {
            var lines = new System.Collections.Generic.List<string>();
            var ctl = SideJobController.Instance;
            if (ctl != null && ctl.allJobsDictionary != null)
                foreach (var kv in ctl.allJobsDictionary)
                {
                    var j = kv.Value;
                    if (j == null || !j.accepted || j.state == SideJob.JobState.ended) continue;
                    lines.Add($"JOB {j.jobID} ({j.presetStr})");
                    if (j.poster != null) lines.Add("   client: " + WhoWhere(j.poster));
                    if (j.purp != null) lines.Add("   CULPRIT: " + WhoWhere(j.purp));
                    if (j.activeJobItems != null)
                        foreach (var it in j.activeJobItems)
                            if (it.Value != null) lines.Add($"   {it.Key}: {ItemName(it.Value)} - {ItemWhere(it.Value)}");
                }
            try
            {
                var mc = MurderController.Instance;
                if (mc != null && mc.activeMurders != null)
                    foreach (var m in mc.activeMurders)
                    {
                        if (m == null) continue;
                        lines.Add("MURDER");
                        if (m.murderer != null) lines.Add("   MURDERER: " + WhoWhere(m.murderer));
                        if (m.victim != null) lines.Add("   victim: " + WhoWhere(m.victim));
                    }
                else if (mc != null && mc.currentMurderer != null) lines.Add("MURDERER: " + WhoWhere(mc.currentMurderer));
            }
            catch (Exception e) { lines.Add("murder: " + e.Message); }
            if (lines.Count == 0) lines.Add("No taken jobs and no active murder");
            return lines;
        }

        static string WhoWhere(Human h)
        {
            string name = h.citizenName;
            string home = h.home != null ? h.home.name : "homeless";
            string now = h.currentGameLocation != null ? h.currentGameLocation.name : "?";
            string state = h.isDead ? ", DEAD" : "";
            return $"{name} (home {home}; now in {now}{state})";
        }

        static string ItemName(Interactable it)
        {
            try { var n = it.GetName(); if (!string.IsNullOrEmpty(n)) return n; } catch (Exception __e) { Err.Swallow(__e); }
            return PresetNameOf(it);
        }

        static string ItemWhere(Interactable it)
        {
            try { var a = it.inInventory; if (a != null) { var h = a.TryCast<Human>(); return h != null ? "carried by " + (h.isPlayer ? "a player" : h.citizenName) : "in someone's inventory"; } } catch (Exception __e) { Err.Swallow(__e); }
            var n = it.node;
            string where = "?";
            if (n != null)
            {
                string loc = n.gameLocation != null ? n.gameLocation.name : "";
                string room = n.room != null ? n.room.name : "";
                int fl = 0; try { if (n.room != null && n.room.floor != null) fl = n.room.floor.floor; } catch (Exception __e) { Err.Swallow(__e); }
                where = loc + (room != "" && room != loc ? ", " + room : "") + $", floor {fl}";
            }
            try { var me = Player.Instance; if (me != null) where += $", {Vector3.Distance(me.transform.position, it.GetWorldPosition(true)):F0} m from you"; } catch (Exception __e) { Err.Swallow(__e); }
            return where;
        }

        static void DrawCaseAnswers()
        {
            if (caseAnswers == null) return;
            if (!CheatsActive) { caseAnswers = null; return; }
            if (Time.unscaledTime - caseAnswersAt > 2f) { caseAnswersAt = Time.unscaledTime; try { caseAnswers = BuildCaseAnswers(); } catch (Exception __e) { Err.Swallow(__e); } }
            if (caseStyle == null)
            {
                caseStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperLeft };
                caseStyle.normal.textColor = new Color(1f, 0.85f, 0.4f, 1f);
                caseShadow = new GUIStyle(caseStyle); caseShadow.normal.textColor = new Color(0f, 0f, 0f, 0.95f);
            }
            float sc = Screen.height / 1080f;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            float y = 180f;
            foreach (var l in caseAnswers)
            {
                GUI.Label(new Rect(21f, y + 1f, 1300f, 26f), l, caseShadow);
                GUI.Label(new Rect(20f, y, 1300f, 26f), l, caseStyle); y += 24f;
            }
            GUI.matrix = old;
        }

        internal static void RevealAllCitizens()
        {
            var dir = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            if (dir == null) return;
            bool client = manager != null && RtMode == NetMode.Client && manager.ConnectedPeersCount > 0;
            var keys = new System.Collections.Generic.List<Evidence.DataKey>();
            foreach (Evidence.DataKey k in Enum.GetValues(typeof(Evidence.DataKey)))
                if (k != Evidence.DataKey.name && k != Evidence.DataKey.code && k != Evidence.DataKey.blueprints && k != Evidence.DataKey.location && k != Evidence.DataKey.purpose) keys.Add(k);
            int n = 0, letters = 0;
            foreach (var c in dir)
            {
                if (c == null || c.isPlayer) continue;
                try
                {
                    if (c.fingerprintLoop < 0)
                    {
                        if (!client) AssignPrintLetter(c);
                        else if (printAsked.Add(c.humanID)) { var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)13); w.Put(c.humanID); manager.SendToAll(w, DeliveryMethod.ReliableOrdered); }
                        letters++;
                    }
                    var ev = c.evidenceEntry;
                    if (ev == null) continue;
                    foreach (var k in keys) { try { ev.MergeDataKeys(Evidence.DataKey.name, k); } catch (Exception __e) { Err.Swallow(__e); } }
                    n++;
                }
                catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] житель {c.humanID}: {e.Message}"); }
            }
            Toast($"All citizens revealed: {n} files, {letters} new fingerprint letters.");
            Plugin.L.LogInfo($"[CHEAT] все жители раскрыты: досье {n}, новых букв отпечатков {letters}");
        }
    }
}

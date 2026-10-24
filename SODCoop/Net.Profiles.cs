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
        static RP NewRp(int pid)
        {
            var rp = new RP(); rp.pid = pid; rp.label = "P" + pid;
            players.Add(rp);
            return rp;
        }

        static int FreePid()
        {
            for (int i = 1; i < 8; i++) if (RpByPid(i) == null) return i;
            return 7;
        }

        public sealed class ProfPv { public int type; public float value; public string str = ""; }
        public sealed class ProfItem
        {
            public string preset = ""; public int slot;
            public bool hasState; public float val, cs;
            public System.Collections.Generic.List<ProfPv> pv = new System.Collections.Generic.List<ProfPv>();
        }
        public sealed class ProfApt { public int id; public System.Collections.Generic.List<int[]> rems = new System.Collections.Generic.List<int[]>(); }
        public sealed class ProfCode { public int type, id; public System.Collections.Generic.List<int> digits = new System.Collections.Generic.List<int>(); }
        public sealed class ProfDisk { public string upgrade = ""; public int state, list, level, objId, uninstallCost; }
        public sealed class ProfileData
        {
            public string id = ""; public string saved = ""; public bool valid;
            public int version;
            public int money; public int social;
            public float hp = -1f, maxHp = -1f, nerve = -1f, maxNerve = -1f;
            public System.Collections.Generic.Dictionary<string, float> stats = new System.Collections.Generic.Dictionary<string, float>();
            public System.Collections.Generic.List<ProfItem> items = new System.Collections.Generic.List<ProfItem>();
            public System.Collections.Generic.List<ProfApt> apts = new System.Collections.Generic.List<ProfApt>();
            public int residence = -1;
            public bool hasPos; public float px, py, pz; public int nx, ny, nz;
            public int lockpicks = -1;
            public System.Collections.Generic.List<int> doorKeys = new System.Collections.Generic.List<int>();
            public System.Collections.Generic.List<int> itemKeys = new System.Collections.Generic.List<int>();
            public System.Collections.Generic.List<ProfCode> codes = new System.Collections.Generic.List<ProfCode>();
            public System.Collections.Generic.List<int> knowsPw = new System.Collections.Generic.List<int>();
            public System.Collections.Generic.List<ProfDisk> disks = new System.Collections.Generic.List<ProfDisk>();
            public bool hasBoards;
            public System.Collections.Generic.List<ProfBoard> boards = new System.Collections.Generic.List<ProfBoard>();
        }
        public sealed class ProfPin { public string id = "", n = "", sig = "", item = "", pose = ""; public int cam; public System.Collections.Generic.List<int> dk = new System.Collections.Generic.List<int>(); public float x, y; public bool co, m; public int color; }
        public sealed class ProfBoard { public string name = ""; public int type, jobRef; public System.Collections.Generic.List<ProfPin> pins = new System.Collections.Generic.List<ProfPin>(); }

        static bool profileApplying, helloOkFlag, profDirty, profErrLogged;
        static int profState;
        static float profReadyAt, profReqAt, profSendAt, profWriteAt;
        static string profLastSent = "", profFile = "", myProfileId;
        static RP doorSender;
        static readonly System.Collections.Generic.Dictionary<string, ProfileData> profiles = new System.Collections.Generic.Dictionary<string, ProfileData>();
        static readonly JsonSerializerOptions ProfJson = new JsonSerializerOptions { IncludeFields = true };

        static readonly string[] StatNames = { "nourishment", "hydration", "alertness", "energy", "hygiene", "heat", "drunk", "sick", "headache", "wet", "brokenLeg", "bruised", "blackEye", "blackedOut", "numb", "poisoned", "bleeding", "wellRested", "starchAddiction", "syncDiskInstall", "blinded" };
        static readonly string[] BadStatNames = { "drunk", "sick", "headache", "wet", "brokenLeg", "bruised", "blackEye", "blackedOut", "numb", "poisoned", "bleeding", "starchAddiction", "syncDiskInstall", "blinded" };

        static float GetStat(Player p, string n)
        {
            switch (n)
            {
                case "nourishment": return p.nourishment;
                case "hydration": return p.hydration;
                case "alertness": return p.alertness;
                case "energy": return p.energy;
                case "hygiene": return p.hygiene;
                case "heat": return p.heat;
                case "drunk": return p.drunk;
                case "sick": return p.sick;
                case "headache": return p.headache;
                case "wet": return p.wet;
                case "brokenLeg": return p.brokenLeg;
                case "bruised": return p.bruised;
                case "blackEye": return p.blackEye;
                case "blackedOut": return p.blackedOut;
                case "numb": return p.numb;
                case "poisoned": return p.poisoned;
                case "bleeding": return p.bleeding;
                case "wellRested": return p.wellRested;
                case "starchAddiction": return p.starchAddiction;
                case "syncDiskInstall": return p.syncDiskInstall;
                case "blinded": return p.blinded;
            }
            return 0f;
        }

        static void SetStat(Player p, string n, float v)
        {
            switch (n)
            {
                case "nourishment": p.nourishment = v; break;
                case "hydration": p.hydration = v; break;
                case "alertness": p.alertness = v; break;
                case "energy": p.energy = v; break;
                case "hygiene": p.hygiene = v; break;
                case "heat": p.heat = v; break;
                case "drunk": p.drunk = v; break;
                case "sick": p.sick = v; break;
                case "headache": p.headache = v; break;
                case "wet": p.wet = v; break;
                case "brokenLeg": p.brokenLeg = v; break;
                case "bruised": p.bruised = v; break;
                case "blackEye": p.blackEye = v; break;
                case "blackedOut": p.blackedOut = v; break;
                case "numb": p.numb = v; break;
                case "poisoned": p.poisoned = v; break;
                case "bleeding": p.bleeding = v; break;
                case "wellRested": p.wellRested = v; break;
                case "starchAddiction": p.starchAddiction = v; break;
                case "syncDiskInstall": p.syncDiskInstall = v; break;
                case "blinded": p.blinded = v; break;
            }
        }

        static string MyProfileId()
        {
            if (myProfileId != null) return myProfileId;
            try
            {
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "SodCoop_playerid.txt");
                string v = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
                if (v.Length < 8) { v = Guid.NewGuid().ToString("N"); File.WriteAllText(path, v); Plugin.L.LogInfo("[PROFILE] создан постоянный номер игрока: " + v); }
                myProfileId = v;
            }
            catch (Exception e) { myProfileId = Guid.NewGuid().ToString("N"); Plugin.L.LogWarning("[PROFILE] не смог сохранить номер игрока: " + e.Message); }
            return myProfileId;
        }

        const System.Reflection.BindingFlags GcFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;

        static object GcInst(out Type t)
        {
            t = FindType("GameplayController");
            if (t == null) return null;
            var ip = t.GetProperty("Instance", GcFlags);
            return ip != null ? ip.GetValue(null, null) : null;
        }

        static double GcGet(string prop)
        {
            try
            {
                Type t; var inst = GcInst(out t);
                if (inst == null) return 0;
                var pr = t.GetProperty(prop, GcFlags);
                if (pr == null) return 0;
                return Convert.ToDouble(pr.GetValue(inst, null));
            }
            catch (Exception) { return 0; }
        }

        static bool GcSet(string prop, string method, double v)
        {
            try
            {
                Type t; var inst = GcInst(out t);
                if (inst == null) return false;
                if (method != null)
                {
                    foreach (var m in t.GetMethods(GcFlags))
                    {
                        if (m.Name != method) continue;
                        var ps = m.GetParameters();
                        if (ps.Length < 1) continue;
                        var args = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                        {
                            var pt = ps[i].ParameterType;
                            if (i == 0 && pt == typeof(int)) args[i] = (int)Math.Round(v);
                            else if (i == 0 && pt == typeof(float)) args[i] = (float)v;
                            else if (pt == typeof(bool)) args[i] = false;
                            else if (pt == typeof(int)) args[i] = 0;
                            else if (pt == typeof(float)) args[i] = 0f;
                            else args[i] = null;
                        }
                        m.Invoke(inst, args);
                        return true;
                    }
                }
                var pr = t.GetProperty(prop, GcFlags);
                if (pr != null && pr.CanWrite)
                {
                    if (pr.PropertyType == typeof(int)) pr.SetValue(inst, (int)Math.Round(v), null);
                    else if (pr.PropertyType == typeof(float)) pr.SetValue(inst, (float)v, null);
                    else return false;
                    return true;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] " + prop + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
            return false;
        }

        static ProfileData CaptureProfile()
        {
            var p = Player.Instance;
            var fp = FirstPersonItemController.Instance;
            if (p == null || fp == null) return null;
            if (p.isDead) return null;
            var pd = new ProfileData();
            pd.valid = true; pd.id = MyProfileId();
            pd.money = (int)Math.Round(GcGet("money"));
            pd.social = (int)Math.Round(GcGet("socialCredit"));
            pd.hp = p.currentHealth; pd.maxHp = p.maximumHealth; pd.nerve = p.currentNerve; pd.maxNerve = p.maxNerve;
            foreach (var n in StatNames) pd.stats[n] = (float)Math.Round(GetStat(p, n), 3);
            var slots = fp.slots;
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var sl = slots[i];
                    if (sl == null || sl.isStatic != FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) continue;
                    Interactable it = null;
                    try { it = sl.GetInteractable(); } catch (Exception __e) { Err.Swallow(__e); }
                    if (it == null) continue;
                    string pn = PresetNameOf(it);
                    if (pn == "") continue;
                    var pi2 = new ProfItem(); pi2.preset = pn; pi2.slot = sl.index;
                    try
                    {
                        pi2.val = it.val; pi2.cs = it.cs; pi2.hasState = true;
                        var pv = it.pv;
                        if (pv != null)
                            for (int k = 0; k < pv.Count && k < 30; k++)
                            {
                                var x = pv[k];
                                if (x != null) pi2.pv.Add(new ProfPv { type = (int)x.varType, value = x.value, str = x.str ?? "" });
                            }
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                    pd.items.Add(pi2);
                }
            }
            pd.version = 2;
            try { CaptureExtra(p, pd); } catch (Exception e) { if (!profErrLogged) { profErrLogged = true; Plugin.L.LogWarning("[PROFILE] сбор профиля: " + e.Message); } }
            return pd;
        }

        static System.Collections.Generic.List<ProfBoard> boardsPending;
        static float boardsUntil, boardsAt, boardsStart;
        static readonly System.Collections.Generic.Dictionary<string, string> pinItemJson = new System.Collections.Generic.Dictionary<string, string>();

        static void CaptureBoards(ProfileData pd)
        {
            pd.hasBoards = true;
            if (boardsPending != null) { pd.boards = boardsPending; return; }
            var cp = CasePanelController.Instance;
            if (cp == null || cp.activeCases == null) return;
            foreach (var c in cp.activeCases)
            {
                if (c == null || c.caseStatus == Case.CaseStatus.submitted || c.caseStatus == Case.CaseStatus.archived || c.caseElements == null) continue;
                var b = new ProfBoard { name = c.name ?? "", type = (int)c.caseType, jobRef = c.jobReference };
                foreach (var el in c.caseElements)
                {
                    if (el == null || string.IsNullOrEmpty(el.id)) continue;
                    var pin = new ProfPin { id = el.id, n = el.n ?? "", x = el.v.x, y = el.v.y, co = el.co, m = el.m, color = (int)el.color };
                    if (el.dk != null) for (int i = 0; i < el.dk.Count; i++) pin.dk.Add((int)el.dk[i]);
                    try
                    {
                        Evidence ev = null;
                        if (Toolbox.Instance.TryGetEvidence(el.id, out ev) && ev != null)
                        {
                            pin.sig = EvidenceSig(ev);
                            string js;
                            if (!pinItemJson.TryGetValue(el.id, out js))
                            {
                                js = EvidenceItemJson(ev, el.id) ?? "";
                                if (js.Length > 20000) js = "";
                                pinItemJson[el.id] = js;
                            }
                            pin.item = js;
                            pin.cam = CaptureCameraId(ev, false);
                            pin.pose = CapturePose(ev, false);
                        }
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                    b.pins.Add(pin);
                }
                if (b.pins.Count > 0) pd.boards.Add(b);
            }
        }

        static Evidence FindPinEvidence(ProfPin pin)
        {
            Evidence ev = null;
            bool found = false;
            try { found = Toolbox.Instance.TryGetEvidence(pin.id, out ev) && ev != null; } catch (Exception __e) { Err.Swallow(__e); }
            if (pin.sig != "" && (!found || EvidenceSig(ev) != pin.sig)) { ev = FindEvidenceBySig(pin.sig); found = ev != null; }
            if (!found && pin.item != "") { ev = ImportSharedItem(System.Text.Encoding.UTF8.GetBytes(pin.item), pin.id, pin.cam, pin.pose ?? ""); found = ev != null; }
            return found ? ev : null;
        }

        static void BoardsTick(float now)
        {
            if (boardsPending == null || now < boardsAt) return;
            boardsAt = now + 3f;
            var cp = CasePanelController.Instance;
            if (cp == null || cp.activeCases == null) return;
            for (int bi = boardsPending.Count - 1; bi >= 0; bi--)
            {
                var b = boardsPending[bi];
                Case c = null;
                try
                {
                    if (b.jobRef > 0) c = FindCase(b.jobRef);
                    else if (b.type == (int)Case.CaseType.murder) c = FindCase(0);
                    else
                    {
                        foreach (var x in cp.activeCases) if (x != null && (int)x.caseType == b.type && (x.name ?? "") == b.name) { c = x; break; }
                        if (c == null && now > boardsStart + 15f) c = cp.CreateNewCase((Case.CaseType)b.type, Case.CaseStatus.closable, true, b.name);
                    }
                }
                catch (Exception e) { Plugin.L.LogWarning("[PROFILE] доска '" + b.name + "': " + e.Message); }
                if (c == null) continue;
                boardsPending.RemoveAt(bi);
                int ok = 0, had = 0, miss = 0;
                var have = new System.Collections.Generic.HashSet<string>();
                try { if (c.caseElements != null) foreach (var el in c.caseElements) if (el != null && el.id != null) have.Add(el.id); } catch (Exception __e) { Err.Swallow(__e); }
                foreach (var pin in b.pins)
                {
                    try
                    {
                        if (have.Contains(pin.id)) { had++; continue; }
                        var ev = FindPinEvidence(pin);
                        if (ev == null) { miss++; continue; }
                        if (have.Contains(ev.evID)) { had++; continue; }
                        var keys = new Il2CppSystem.Collections.Generic.List<Evidence.DataKey>();
                        foreach (int k in pin.dk) keys.Add((Evidence.DataKey)k);
                        if (keys.Count == 0) keys.Add(Evidence.DataKey.name);
                        var pos = new Vector2(pin.x, pin.y);
                        cp.PinToCasePanel(c, ev, keys, false, pos, false);
                        have.Add(ev.evID);
                        ok++;
                        foreach (var el in c.caseElements)
                        {
                            if (el == null || el.id != ev.evID) continue;
                            el.v = pos; el.co = pin.co; el.m = pin.m;
                            try { el.SetColour((InterfaceControls.EvidenceColours)pin.color); } catch (Exception __e) { Err.Swallow(__e); }
                            try { if (el.pinnedController != null) el.pinnedController.SetPostion(pos); } catch (Exception __e) { Err.Swallow(__e); }
                            break;
                        }
                    }
                    catch (Exception e) { miss++; Plugin.L.LogWarning($"[PROFILE] улика '{pin.n}' на доске '{b.name}': {e.Message}"); }
                }
                Plugin.L.LogInfo($"[PROFILE] доска '{c.name}': вернул улик {ok}, уже были {had}, не нашёл {miss}");
            }
            if (boardsPending.Count == 0 || now > boardsUntil)
            {
                foreach (var b in boardsPending) Plugin.L.LogWarning($"[PROFILE] дело '{b.name}' (задание {b.jobRef}) так и не появилось - его доска не восстановлена");
                boardsPending = null;
            }
        }

        static void CaptureExtra(Player p, ProfileData pd)
        {
            try { CaptureBoards(pd); } catch (Exception e) { if (!profErrLogged) { profErrLogged = true; Plugin.L.LogWarning("[PROFILE] доски дел: " + e.Message); } }
            var pos = p.transform.position;
            pd.px = pos.x; pd.py = pos.y; pd.pz = pos.z;
            try { var n = p.currentNode; if (n != null) { var c = n.nodeCoord; pd.nx = (int)c.x; pd.ny = (int)c.y; pd.nz = (int)c.z; pd.hasPos = true; } } catch (Exception __e) { Err.Swallow(__e); }
            pd.residence = AddrIdOf(p.residence);
            pd.lockpicks = (int)Math.Round(GcGet("lockPicks"));
            try { var kr = p.keyring; if (kr != null) for (int i = 0; i < kr.Count; i++) { var d = kr[i]; if (d == null) continue; int k = DoorKey(d); if (k >= 0) pd.doorKeys.Add(k); } } catch (Exception __e) { Err.Swallow(__e); }
            try { var ki = p.playerKeyringInt; if (ki != null) for (int i = 0; i < ki.Count; i++) { var it = ki[i]; if (it != null) pd.itemKeys.Add(it.id); } } catch (Exception __e) { Err.Swallow(__e); }
            var gc = GameplayController.Instance;
            if (gc != null)
            {
                try
                {
                    var ac = gc.acquiredPasscodes;
                    if (ac != null)
                        for (int i = 0; i < ac.Count; i++)
                        {
                            var c = ac[i]; if (c == null) continue;
                            var pc = new ProfCode { type = (int)c.type, id = c.id };
                            var dg = c.digits; if (dg != null) for (int j = 0; j < dg.Count; j++) pc.digits.Add(dg[j]);
                            pd.codes.Add(pc);
                        }
                }
                catch (Exception __e) { Err.Swallow(__e); }
                try { var kp = gc.playerKnowsPasswords; if (kp != null) for (int i = 0; i < kp.Count; i++) pd.knowsPw.Add(kp[i]); } catch (Exception __e) { Err.Swallow(__e); }
            }
            try
            {
                var uc = UpgradesController.Instance;
                var ups = uc != null ? uc.upgrades : null;
                if (ups != null)
                    for (int i = 0; i < ups.Count; i++)
                    {
                        var u = ups[i]; if (u == null) continue;
                        pd.disks.Add(new ProfDisk { upgrade = u.upgrade ?? "", state = (int)u.state, list = u.list, level = u.level, objId = u.objId, uninstallCost = u.uninstallCost });
                    }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void ClearInventory()
        {
            var fp = FirstPersonItemController.Instance;
            if (fp == null || fp.slots == null) return;
            var list = new System.Collections.Generic.List<FirstPersonItemController.InventorySlot>();
            for (int i = 0; i < fp.slots.Count; i++)
            {
                var sl = fp.slots[i];
                if (sl != null && sl.isStatic == FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) list.Add(sl);
            }
            int n = 0;
            foreach (var sl in list)
            {
                try { if (sl.GetInteractable() == null) continue; fp.EmptySlot(sl, false, true, false, false); n++; }
                catch (Exception e) { Plugin.L.LogWarning("[PROFILE] очистка слота: " + e.Message); }
            }
            Plugin.L.LogInfo($"[PROFILE] инвентарь очищен, предметов убрано: {n}");
        }

        static bool profRestoring;
        static float profVerifyAt, profVerifyEnd, pendDeadline;
        static bool pendClearDone;
        static double prevMoney, prevSocial;
        static ProfileData profTarget;
        static System.Collections.Generic.List<ProfItem> pendingItems;
        static Interactable pendingIt;
        static int pendingTries, pendOk, pendBad;
        static float pendingAt;

        static void RestoreInventory(System.Collections.Generic.List<ProfItem> items)
        {
            pendingIt = null; pendOk = 0; pendBad = 0; pendClearDone = false;
            pendingItems = new System.Collections.Generic.List<ProfItem>(items ?? new System.Collections.Generic.List<ProfItem>());
            pendingItems.Sort((a2, b2) => a2.slot.CompareTo(b2.slot));
            profRestoring = true; pendingAt = Time.realtimeSinceStartup + 0.5f; pendDeadline = Time.realtimeSinceStartup + 90f;
            Plugin.L.LogInfo($"[PROFILE] жду инвентарь игры, чтобы вернуть предметы: {pendingItems.Count}");
        }

        static void FinishRestore()
        {
            Plugin.L.LogInfo($"[PROFILE] инвентарь восстановлен: {pendOk} из {pendOk + pendBad}" + (pendBad > 0 ? $", не удалось: {pendBad}" : ""));
            pendingItems = null; pendingIt = null; profRestoring = false;
        }

        static void PendingItemsTick(float now)
        {
            if (pendingItems == null || now < pendingAt) return;
            var fp = FirstPersonItemController.Instance; var pl = Player.Instance;
            if (fp == null || pl == null) return;
            try
            {
                int nsl = 0;
                try { for (int i = 0; i < fp.slots.Count; i++) { var sl0 = fp.slots[i]; if (sl0 != null && sl0.isStatic == FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) nsl++; } } catch (Exception __e) { Err.Swallow(__e); }
                if (nsl == 0)
                {
                    if (now > pendDeadline) { Plugin.L.LogWarning("[PROFILE] инвентарь игры так и не появился, предметы не возвращены"); pendingItems = null; pendingIt = null; profRestoring = false; }
                    else pendingAt = now + 0.5f;
                    return;
                }
                if (!pendClearDone) { pendClearDone = true; ClearInventory(); }
                if (pendingIt == null)
                {
                    if (pendingItems.Count == 0) { FinishRestore(); return; }
                    var pi2 = pendingItems[0];
                    var pr = FindPreset(pi2.preset);
                    if (pr == null) { Plugin.L.LogWarning($"[PROFILE] пресет предмета '{pi2.preset}' не найден"); pendingItems.RemoveAt(0); pendBad++; pendingAt = now + 0.05f; return; }
                    pendingIt = InteractableCreator.Instance.CreateWorldInteractable(pr, null, null, null, pl.transform.position + Vector3.up, Vector3.zero, null, null, "");
                    pendingTries = 0; pendingAt = now + 0.4f;
                    if (pendingIt == null) { Plugin.L.LogWarning($"[PROFILE] не смог создать предмет '{pi2.preset}'"); pendingItems.RemoveAt(0); pendBad++; }
                    else if (pi2.hasState)
                    {
                        var st = new ItemState(); st.val = pi2.val; st.cs = pi2.cs;
                        foreach (var x in pi2.pv) st.pv.Add(new PassedVal { type = x.type, value = x.value, str = x.str });
                        ApplyState(pendingIt, st);
                    }
                    return;
                }
                pendingTries++;
                bool done = false;
                try { done = fp.PickUpItem(pendingIt, false, false, false, false, false); } catch (Exception e) { if (pendingTries == 1) Plugin.L.LogWarning("[PROFILE] PickUpItem: " + e.Message); }
                if (!done) { try { done = fp.PickUpItem(pendingIt, false, true, false, false, false); } catch (Exception __e) { Err.Swallow(__e); } }
                if (!done) { try { done = pendingIt.PickUpTarget(pl, false); } catch (Exception e) { if (pendingTries == 1) Plugin.L.LogWarning("[PROFILE] PickUpTarget: " + e.Message); } }
                if (done)
                {
                    pendOk++; pendingItems.RemoveAt(0); pendingIt = null; pendingAt = now + 0.2f;
                    return;
                }
                if (pendingTries == 1)
                {
                    int ns = 0, empty = 0;
                    try { for (int i = 0; i < fp.slots.Count; i++) { var sl = fp.slots[i]; if (sl != null && sl.isStatic == FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) { ns++; if (sl.GetInteractable() == null) empty++; } } } catch (Exception __e) { Err.Swallow(__e); }
                    bool spawned = false; try { spawned = pendingIt.spawnedObject != null; } catch (Exception __e) { Err.Swallow(__e); }
                    bool avail = false; try { avail = fp.IsSlotAvailable(); } catch (Exception __e) { Err.Swallow(__e); }
                    Plugin.L.LogWarning($"[PROFILE] подбор '{PresetNameOf(pendingIt)}' не прошёл: слотов {ns}, пустых {empty}, inventorySlots={fp.inventorySlots}, IsSlotAvailable={avail}, предмет в мире={spawned}");
                }
                if (pendingTries >= 6)
                {
                    Plugin.L.LogWarning($"[PROFILE] предмет '{PresetNameOf(pendingIt)}' так и не удалось положить в инвентарь");
                    try { pendingIt.SafeDelete(false); } catch (Exception __e) { Err.Swallow(__e); }
                    pendingItems.RemoveAt(0); pendBad++; pendingIt = null; pendingAt = now + 0.2f;
                }
                else pendingAt = now + 0.5f;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[PROFILE] возврат предметов: " + e.Message);
                pendingItems = null; pendingIt = null; profRestoring = false;
            }
        }

        static void RestoreResidence(System.Collections.Generic.List<ProfApt> apts, int residence = -1)
        {
            if (apts == null || apts.Count == 0) return;
            var pl = Player.Instance;
            var dict = CityData.Instance.addressDictionary;
            if (pl == null || dict == null) return;
            int home = apts[apts.Count - 1].id;
            foreach (var a in apts) if (a.id == residence) home = residence;
            var order = new System.Collections.Generic.List<int>();
            foreach (var a in apts) if (a.id != home && !order.Contains(a.id)) order.Add(a.id);
            order.Add(home);
            int n = 0;
            foreach (int id in order)
            {
                NewAddress ad = null;
                if (!dict.TryGetValue(id, out ad) || ad == null) { Plugin.L.LogWarning($"[PROFILE] квартира (адрес {id}) не найдена"); continue; }
                var rc = ad.residence;
                if (rc == null) { Plugin.L.LogWarning($"[PROFILE] у адреса {id} нет жилья"); continue; }
                soldAddrs.Add(id);
                if (AddrIdOf(pl.residence) != id) pl.SetResidence(rc, false);
                try { pl.AddToKeyring(ad, false); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] ключи от квартиры: " + e.Message); }
                n++;
            }
            Plugin.L.LogInfo($"[PROFILE] квартир возвращено: {n} из {apts.Count}, жильё игрока = {AddrIdOf(pl.residence)}");
        }

        static void ApplyProfile(ProfileData pd)
        {
            prevMoney = GcGet("money"); prevSocial = GcGet("socialCredit");
            profileApplying = true;
            try
            {
                var p = Player.Instance;
                try { GcSet("money", "SetMoney", pd.money); GcSet("socialCredit", "SetSocialCredit", pd.social); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] деньги/рейтинг: " + e.Message); }
                if (p != null)
                {
                    try
                    {
                        if (pd.maxHp > 0f) p.SetMaxHealth(pd.maxHp, false);
                        if (pd.hp > 0f) p.SetHealth(pd.hp);
                        if (pd.maxNerve > 0f) p.SetMaxNerve(pd.maxNerve, false);
                        if (pd.nerve >= 0f) p.SetNerve(pd.nerve);
                    }
                    catch (Exception e) { Plugin.L.LogWarning("[PROFILE] здоровье: " + e.Message); }
                    try { foreach (var kv in pd.stats) SetStat(p, kv.Key, kv.Value); }
                    catch (Exception e) { Plugin.L.LogWarning("[PROFILE] статусы: " + e.Message); }
                }
                try { RestoreInventory(pd.items); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] инвентарь: " + e.Message); }
                try { RestoreResidence(pd.apts, pd.residence); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] квартира: " + e.Message); }
                if (pd.version >= 2 && p != null) RestoreExtra(p, pd);
            }
            finally { profileApplying = false; }
        }

        static void RestoreExtra(Player p, ProfileData pd)
        {
            if (pd.lockpicks >= 0) { try { GcSet("lockPicks", "SetLockpicks", pd.lockpicks); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] отмычки: " + e.Message); } }
            try { RestoreKeys(p, pd); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] ключи: " + e.Message); }
            try { RestoreCodes(pd); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] коды: " + e.Message); }
            try { RestoreDisks(pd.disks); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] синк-диски: " + e.Message); }
            try { RestorePosition(p, pd); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] позиция: " + e.Message); }
            if (pd.hasBoards && pd.boards.Count > 0)
            {
                boardsPending = new System.Collections.Generic.List<ProfBoard>(pd.boards);
                boardsStart = Time.realtimeSinceStartup; boardsAt = boardsStart + 3f; boardsUntil = boardsStart + 180f;
                int np = 0; foreach (var b in pd.boards) np += b.pins.Count;
                Plugin.L.LogInfo($"[PROFILE] досок дел в профиле: {pd.boards.Count}, улик {np} - верну, когда дела появятся");
            }
        }

        static void RestoreKeys(Player p, ProfileData pd)
        {
            var kr = p.keyring;
            if (kr != null)
            {
                kr.Clear();
                foreach (int k in pd.doorKeys) { var d = DoorByKey(k); if (d != null && !kr.Contains(d)) kr.Add(d); }
            }
            var ki = p.playerKeyringInt;
            if (ki != null) ki.Clear();
            int ok = 0;
            foreach (int id in pd.itemKeys) { var it = FindById(id); if (it != null) { p.AddToKeyring(it, false); ok++; } }
            foreach (var a in pd.apts) { NewAddress ad; if (CityData.Instance.addressDictionary.TryGetValue(a.id, out ad) && ad != null) p.AddToKeyring(ad, false); }
            Plugin.L.LogInfo($"[PROFILE] ключи: дверей {(kr != null ? kr.Count : 0)} из {pd.doorKeys.Count}, предметов {ok} из {pd.itemKeys.Count}");
        }

        static void RestoreCodes(ProfileData pd)
        {
            var gc = GameplayController.Instance;
            if (gc == null) return;
            var ac = gc.acquiredPasscodes;
            if (ac != null)
            {
                ac.Clear();
                var city = CityData.Instance;
                foreach (var c in pd.codes)
                {
                    GameplayController.Passcode code = null;
                    try
                    {
                        switch ((GameplayController.PasscodeType)c.type)
                        {
                            case GameplayController.PasscodeType.address: { NewAddress a; if (city.addressDictionary.TryGetValue(c.id, out a) && a != null) code = a.passcode; break; }
                            case GameplayController.PasscodeType.room: { NewRoom r; if (city.roomDictionary.TryGetValue(c.id, out r) && r != null) code = r.passcode; break; }
                            case GameplayController.PasscodeType.citizen: { Human h; if (city.citizenDictionary.TryGetValue(c.id, out h) && h != null) code = h.passcode; break; }
                            case GameplayController.PasscodeType.interactable: { var it = FindById(c.id); if (it != null) code = it.passcode; break; }
                        }
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                    if (code == null)
                    {
                        code = new GameplayController.Passcode((GameplayController.PasscodeType)c.type);
                        code.id = c.id;
                        code.digits = new Il2CppSystem.Collections.Generic.List<int>();
                        foreach (int d in c.digits) code.digits.Add(d);
                    }
                    if (!ac.Contains(code)) ac.Add(code);
                }
            }
            var kp = gc.playerKnowsPasswords;
            if (kp != null) { kp.Clear(); foreach (int a in pd.knowsPw) kp.Add(a); }
            Plugin.L.LogInfo($"[PROFILE] коды: {pd.codes.Count}, известных паролей адресов: {pd.knowsPw.Count}");
        }

        static void RestoreDisks(System.Collections.Generic.List<ProfDisk> disks)
        {
            var uc = UpgradesController.Instance;
            if (uc == null || uc.upgrades == null) return;
            uc.upgrades.Clear();
            foreach (var d in disks ?? new System.Collections.Generic.List<ProfDisk>())
            {
                var u = new UpgradesController.Upgrades();
                u.upgrade = d.upgrade; u.state = (UpgradesController.SyncDiskState)d.state; u.list = d.list; u.level = d.level; u.objId = d.objId; u.uninstallCost = d.uninstallCost;
                uc.upgrades.Add(u);
            }
            try { uc.UpdateActivation(); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] UpdateActivation: " + e.Message); }
            try { var ue = UpgradeEffectController.Instance; if (ue != null) ue.OnSyncDiskChange(true); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] OnSyncDiskChange: " + e.Message); }
            try { uc.UpdateUpgrades(); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] UpdateUpgrades: " + e.Message); }
            Plugin.L.LogInfo($"[PROFILE] синк-дисков: {uc.upgrades.Count}");
        }

        static void RestorePosition(Player p, ProfileData pd)
        {
            if (!pd.hasPos) return;
            NewNode node = null;
            var map = PathFinder.Instance != null ? PathFinder.Instance.nodeMap : null;
            if (map == null || !map.TryGetValue(new Vector3(pd.nx, pd.ny, pd.nz), out node) || node == null)
            {
                Plugin.L.LogWarning($"[PROFILE] точка выхода ({pd.nx}, {pd.ny}, {pd.nz}) не найдена, игрок остаётся где есть");
                return;
            }
            p.Teleport(node, null, true, false, true);
            Plugin.L.LogInfo($"[PROFILE] игрок перенесён туда, где вышел: ({pd.px:F1}, {pd.py:F1}, {pd.pz:F1})");
        }

        static void NewPlayerStart()
        {
            prevMoney = GcGet("money"); prevSocial = GcGet("socialCredit");
            profileApplying = true;
            try
            {
                var p = Player.Instance;
                try { RestoreInventory(null); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] инвентарь: " + e.Message); }
                if (Rules.NewPlayerMoney >= 0) GcSet("money", "SetMoney", Rules.NewPlayerMoney);
                if (Rules.NewPlayerLockpicks >= 0) { try { GcSet("lockPicks", "SetLockpicks", Rules.NewPlayerLockpicks); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] отмычки: " + e.Message); } }
                GcSet("socialCredit", "SetSocialCredit", 0);
                if (p != null)
                {
                    try { p.ResetHealthToMaximum(); p.ResetNerveToMaximum(); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] здоровье: " + e.Message); }
                    try { foreach (var n in BadStatNames) SetStat(p, n, 0f); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] статусы: " + e.Message); }
                }
                try { RestoreDisks(null); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] синк-диски: " + e.Message); }
            }
            finally { profileApplying = false; }
        }

        static void SendProfile(bool force)
        {
            if (profState != 2 || profRestoring || profTarget != null || serverPeer == null || serverPeer.ConnectionState != ConnectionState.Connected) return;
            var pd = CaptureProfile();
            if (pd == null) return;
            string js = JsonSerializer.Serialize(pd, ProfJson);
            bool urgent = profUrgent; profUrgent = false;
            if (!force && js == profLastSent) return;
            profLastSent = js;
            var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)1); w.Put(js);
            w.Put(urgent || force);
            serverPeer.Send(w, DeliveryMethod.ReliableOrdered);
            if (urgent) Plugin.Dbg("[PROFILE] профиль отправлен сразу после: " + profUrgentWhy);
        }

        static bool profUrgent;
        static string profUrgentWhy = "";

        internal static void ProfileSoon(string why)
        {
            if (manager == null || IsHost || profState != 2 || profileApplying || profRestoring || applyingRemote) return;
            float t = Time.realtimeSinceStartup + 0.5f;
            if (profSendAt > t) profSendAt = t;
            profUrgent = true; profUrgentWhy = why;
        }

        static void VerifyProfile()
        {
            var t = profTarget;
            if (t == null) return;
            float now = Time.realtimeSinceStartup;
            if (now >= profVerifyEnd) { profTarget = null; Plugin.L.LogInfo($"[PROFILE] проверка закончена: деньги {GcGet("money"):F0}, рейтинг {GcGet("socialCredit"):F0}"); return; }
            profVerifyAt = now + 2f;
            profileApplying = true;
            try
            {
                double m = GcGet("money"), sc = GcGet("socialCredit");
                if (t.money >= 0 && Math.Abs(m - t.money) >= 1 && Math.Abs(m - prevMoney) < 1) { Plugin.L.LogWarning($"[PROFILE] игра вернула деньги ({m:F0}), ставлю {t.money}"); GcSet("money", "SetMoney", t.money); }
                if (Math.Abs(sc - t.social) >= 1 && Math.Abs(sc - prevSocial) < 1) { Plugin.L.LogWarning($"[PROFILE] игра вернула рейтинг ({sc:F0}), ставлю {t.social}"); GcSet("socialCredit", "SetSocialCredit", t.social); }
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] проверка: " + e.Message); }
            finally { profileApplying = false; }
        }

        static void ProfileClientData(NetDataReader r)
        {
            bool found = r.GetBool(); string js = r.GetString();
            if (profState == 2) return;
            ProfileData pd = null;
            try { if (found && js != "") pd = JsonSerializer.Deserialize<ProfileData>(js, ProfJson); }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] ответ хоста не разобран: " + e.Message); }
            try
            {
                if (pd != null && pd.valid)
                {
                    ApplyProfile(pd);
                    profTarget = pd; profVerifyAt = Time.realtimeSinceStartup + 2f; profVerifyEnd = Time.realtimeSinceStartup + 25f;
                    try { var pp = Player.Instance; Plugin.L.LogInfo($"[PROFILE] после применения: деньги {GcGet("money"):F0} (в профиле {pd.money}), рейтинг {GcGet("socialCredit"):F0} (в профиле {pd.social}), здоровье {(pp != null ? pp.currentHealth : -1f):F2} (в профиле {pd.hp:F2}), нерв {(pp != null ? pp.currentNerve : -1f):F2} (в профиле {pd.nerve:F2})"); } catch (Exception __e) { Err.Swallow(__e); }
                    Plugin.L.LogInfo($"[PROFILE] профиль загружен: деньги {pd.money}, рейтинг {pd.social}, здоровье {pd.hp:F1}/{pd.maxHp:F1}, предметов {pd.items.Count}, квартир {pd.apts.Count}");
                    Toast("Your saved profile was loaded.");
                }
                else
                {
                    if (Rules.NewPlayerClean)
                    {
                        NewPlayerStart();
                        var nt = new ProfileData(); nt.money = Rules.NewPlayerMoney; nt.social = 0; nt.valid = true; nt.lockpicks = Rules.NewPlayerLockpicks;
                        profTarget = nt; profVerifyAt = Time.realtimeSinceStartup + 2f; profVerifyEnd = Time.realtimeSinceStartup + 25f;
                    }
                    if (pd != null && pd.apts.Count > 0) { profileApplying = true; try { RestoreResidence(pd.apts, pd.residence); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] квартира: " + e.Message); } finally { profileApplying = false; } }
                    Plugin.L.LogInfo("[PROFILE] профиля ещё нет, это новый игрок" + (Rules.NewPlayerClean ? ": чистый старт" : ""));
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] применить профиль: " + e.Message); }
            profState = 2; profSendAt = Time.realtimeSinceStartup + 2f;
        }

        static string ProfFilePath()
        {
            string seed = "";
            try { seed = "" + CityData.Instance.seed; } catch (Exception __e) { Err.Swallow(__e); }
            var sb = new StringBuilder();
            foreach (char ch in seed) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return Path.Combine(SaveDir(), "SodCoop_profiles_" + (sb.Length > 0 ? sb.ToString() : "city") + ".json");
        }

        static int worldGen, profWorldGen = -1;
        static string snapPath = "";
        static float snapUntil;

        internal static void OnWorldLoading() { worldGen++; gooseDone.Clear(); }

        static string SnapPathFor(string savePath)
        {
            return Path.Combine(Path.GetDirectoryName(savePath) ?? SaveDir(), Path.GetFileNameWithoutExtension(savePath) + ".sodcoop.json");
        }

        static bool SaveOlderThanProfiles(string save, string prof)
        {
            try
            {
                if (!File.Exists(save) || !File.Exists(prof)) return false;
                var ts = File.GetLastWriteTimeUtc(save); var tp = File.GetLastWriteTimeUtc(prof);
                Plugin.L.LogInfo($"[PROFILE] сейв {Path.GetFileName(save)} от {ts.ToLocalTime():dd.MM HH:mm}, последние профили от {tp.ToLocalTime():dd.MM HH:mm}");
                return ts < tp.AddMinutes(-1);
            }
            catch (Exception) { return false; }
        }

        static string LoadedSavePath()
        {
            try { var r = RestartSafeController.Instance; if (r != null && r.loadSaveGame && r.saveStateFileInfo != null) return r.saveStateFileInfo.FullName ?? ""; } catch (Exception __e) { Err.Swallow(__e); }
            return "";
        }

        static bool ReadProfilesFile(string path)
        {
            foreach (var f in new[] { path, path + ".bak" })
            {
                if (!File.Exists(f)) continue;
                try
                {
                    var d = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, ProfileData>>(File.ReadAllText(f), ProfJson);
                    if (d == null) continue;
                    foreach (var kv in d) if (kv.Value != null) profiles[kv.Key] = kv.Value;
                    if (f != path) Plugin.L.LogWarning("[PROFILE] " + Path.GetFileName(path) + " повреждён, взял резервную копию");
                    return true;
                }
                catch (Exception e) { Plugin.L.LogWarning("[PROFILE] не прочитал " + f + ": " + e.Message); }
            }
            return false;
        }

        static void WriteJsonSafe(string path, string json)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }

        static void ProfilesHostInit()
        {
            profDirty = false; profFile = "";
            if (!Plugin.CfgProfiles.Value) { profiles.Clear(); return; }
            profFile = ProfFilePath();
            string src;
            if (worldGen != profWorldGen)
            {
                profWorldGen = worldGen; profiles.Clear(); soldAddrs.Clear();
                string save = LoadedSavePath();
                string snap = save != "" ? SnapPathFor(save) : "";
                if (snap != "" && ReadProfilesFile(snap)) { src = "снимок сейва " + Path.GetFileName(save); profDirty = true; }
                else if (save == "" && worldGen > 0) { src = "новая игра, профилей нет"; profDirty = true; }
                else if (save != "" && SaveOlderThanProfiles(save, profFile))
                {
                    src = "у сейва " + Path.GetFileName(save) + " нет снимка, а последние профили новее сейва - игроки начнут заново";
                    profDirty = true;
                }
                else { ReadProfilesFile(profFile); src = save != "" ? "у сейва " + Path.GetFileName(save) + " нет снимка, взял последние профили города (они не новее сейва)" : "последние профили города"; }
            }
            else src = "профили этой сессии";
            int n = 0;
            foreach (var kv in profiles)
                foreach (var a in kv.Value.apts) { try { ApplyApartmentSold(a.id, false, a.rems); n++; } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] квартира " + a.id + ": " + e.Message); } }
            Plugin.L.LogInfo($"[PROFILE] профилей игроков: {profiles.Count} ({src}), проданных квартир восстановлено: {n}");
        }

        static void ProfilesWrite()
        {
            if (profFile == "") return;
            try { WriteJsonSafe(profFile, JsonSerializer.Serialize(profiles, ProfJson)); profDirty = false; Plugin.Dbg("[PROFILE] файл профилей записан"); }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] не смог записать " + profFile + ": " + e.Message); }
        }

        static void SnapshotWrite()
        {
            if (snapPath == "") return;
            try { WriteJsonSafe(snapPath, JsonSerializer.Serialize(profiles, ProfJson)); Plugin.Dbg("[PROFILE] снимок профилей записан: " + snapPath); }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] не смог записать снимок " + snapPath + ": " + e.Message); }
        }

        internal static void OnGameSaving(string path)
        {
            if (manager == null || !IsHost || !Plugin.CfgProfiles.Value || string.IsNullOrEmpty(path)) return;
            if (Path.GetFileName(path).StartsWith("SodCoop_")) return;
            snapPath = SnapPathFor(path); snapUntil = Time.realtimeSinceStartup + 6f;
            SnapshotWrite();
            var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)4);
            foreach (var o in players) if (o.peer != null) { try { o.peer.Send(w, DeliveryMethod.ReliableOrdered); } catch (Exception __e) { Err.Swallow(__e); } }
            Plugin.L.LogInfo($"[PROFILE] игра сохранена: профили игроков ({profiles.Count}) записаны в {Path.GetFileName(snapPath)}");
        }

        static void ProfileNoteApt(RP rp, int id, System.Collections.Generic.List<int[]> rems)
        {
            if (!Plugin.CfgProfiles.Value || rp == null || string.IsNullOrEmpty(rp.profileId)) return;
            ProfileData pd;
            if (!profiles.TryGetValue(rp.profileId, out pd)) { pd = new ProfileData(); pd.id = rp.profileId; profiles[rp.profileId] = pd; }
            pd.apts.RemoveAll(a => a.id == id);
            var pa = new ProfApt(); pa.id = id; pa.rems = rems ?? new System.Collections.Generic.List<int[]>();
            pd.apts.Add(pa);
            profDirty = true;
            Plugin.L.LogInfo($"[PROFILE] квартира (адрес {id}) записана игроку {rp.pid}");
        }

        static void ProfileHostMsg(RP rp, byte op, NetDataReader r)
        {
            if (!Plugin.CfgProfiles.Value || string.IsNullOrEmpty(rp.profileId)) return;
            string id = rp.profileId;
            if (op == 1)
            {
                ProfileData pd = null;
                string js = r.GetString();
                bool urgent = r.AvailableBytes > 0 && r.GetBool();
                try { pd = JsonSerializer.Deserialize<ProfileData>(js, ProfJson); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] профиль игрока " + rp.pid + " не разобран: " + e.Message); }
                if (pd == null || !pd.valid) return;
                if (urgent) profWriteAt = Mathf.Min(profWriteAt, Time.realtimeSinceStartup + 1f);
                ProfileData old; profiles.TryGetValue(id, out old);
                pd.id = id; pd.saved = DateTime.UtcNow.ToString("u");
                pd.apts = old != null ? old.apts : new System.Collections.Generic.List<ProfApt>();
                profiles[id] = pd; profDirty = true;
                if (snapPath != "" && Time.realtimeSinceStartup < snapUntil) SnapshotWrite();
            }
            else if (op == 2)
            {
                ProfileData pd; bool found = profiles.TryGetValue(id, out pd);
                var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)3); w.Put(found); w.Put(found ? JsonSerializer.Serialize(pd, ProfJson) : "");
                if (rp.peer != null) rp.peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[PROFILE] игрок {rp.pid} запросил профиль: " + (found && pd.valid ? "найден" : "новый игрок") + (found && pd.apts.Count > 0 ? $", квартир {pd.apts.Count}" : ""));
            }
        }

        static void ProfileTick(float dt)
        {
            if (!Plugin.CfgProfiles.Value || manager == null) return;
            float now = Time.realtimeSinceStartup;
            if (IsHost)
            {
                if (profDirty && now >= profWriteAt) { profWriteAt = now + 10f; ProfilesWrite(); }
                return;
            }
            if (xferMode || !helloOkFlag || serverPeer == null || serverPeer.ConnectionState != ConnectionState.Connected) return;
            if (Player.Instance == null || FirstPersonItemController.Instance == null) return;
            PendingItemsTick(now);
            if (profState < 2)
            {
                if (now < profReadyAt || now < profReqAt) return;
                bool started = false;
                try { started = SessionData.Instance != null && SessionData.Instance.startedGame; } catch (Exception __e) { Err.Swallow(__e); }
                if (!started && now < profReadyAt + 26f) return;

                profReqAt = now + 5f; profState = 1;
                var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)2);
                serverPeer.Send(w, DeliveryMethod.ReliableOrdered);
                return;
            }
            if (profTarget != null && now >= profVerifyAt) VerifyProfile();
            try { BoardsTick(now); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] доски дел: " + e.Message); boardsPending = null; }
            if (now >= profSendAt) { profSendAt = now + Mathf.Max(3f, Plugin.CfgProfileInterval.Value); SendProfile(false); }
        }

        static void ProfileOnShutdown()
        {
            if (IsHost) { if (profDirty) ProfilesWrite(); }
            else if (profState == 2 && manager != null)
            {
                SendProfile(true);
                System.Threading.Thread.Sleep(60);
            }
            profState = 0; profLastSent = ""; helloOkFlag = false; profReqAt = 0f; pendingItems = null; pendingIt = null; profRestoring = false; profTarget = null;
        }
    }
}

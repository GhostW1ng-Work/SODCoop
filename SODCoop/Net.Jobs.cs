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
        static bool applyingJob, jobErrLogged;
        static float jobPollAt, jobShareKeyAt;
        static readonly System.Collections.Generic.Dictionary<int, string> jobSentSig = new System.Collections.Generic.Dictionary<int, string>();
        static readonly System.Collections.Generic.Dictionary<int, float> jobSeenAt = new System.Collections.Generic.Dictionary<int, float>();
        static readonly System.Collections.Generic.HashSet<int> jobAcceptSent = new System.Collections.Generic.HashSet<int>();
        static KeyCode shareKey = KeyCode.None; static string shareKeyName = "";

        internal static bool BlockLocalJobCreation()
        {
            return Plugin.CfgJobSync.Value && manager != null && RtMode == NetMode.Client;
        }

        static bool jobLoopLogged;
        static float clientLoopAt, jobChoiceAt, printAt;

        internal static void ClientJobWorldLoop()
        {
            var ctl = SideJobController.Instance;
            if (ctl == null || ctl.allJobsDictionary == null) return;
            var list = new System.Collections.Generic.List<SideJob>();
            foreach (var kv in ctl.allJobsDictionary) if (kv.Value != null && kv.Value.accepted && kv.Value.state != SideJob.JobState.ended) list.Add(kv.Value);
            foreach (var j in list)
            {
                try { j.GameWorldLoop(); }
                catch (Exception e) { Plugin.L.LogWarning($"[JOB] задание {j.jobID}: {e.Message}"); }
            }
            if (!jobLoopLogged && list.Count > 0) { jobLoopLogged = true; Plugin.L.LogInfo($"[JOB] прокручиваю свои взятые задания: {list.Count}"); }
        }

        static SideJob JobById(int id)
        {
            try { var c = SideJobController.Instance; SideJob j; if (c != null && c.allJobsDictionary != null && c.allJobsDictionary.TryGetValue(id, out j)) return j; } catch (Exception __e) { Err.Swallow(__e); }
            return null;
        }

        static readonly System.Collections.Generic.Dictionary<int, int> jobPhoneCount = new System.Collections.Generic.Dictionary<int, int>();

        static string JobSig(SideJob j)
        {
            int ph; jobPhoneCount.TryGetValue(j.jobID, out ph);
            int dl = 0; try { dl = j.dialog != null ? j.dialog.Count : 0; } catch (Exception __e) { Err.Swallow(__e); }
            return (int)j.state + "|" + j.postID + "|" + j.phase + "|" + j.failed + "|" + j.knowHandInLocation + "|" + ph + "|" + dl;
        }

        static void CountJobPhones()
        {
            jobPhoneCount.Clear();
            try
            {
                var tc = TelephoneController.Instance;
                if (tc == null || tc.fakeTelephoneDictionary == null) return;
                foreach (var kv in tc.fakeTelephoneDictionary)
                {
                    if (kv.Value == null || kv.Value.job <= 0) continue;
                    int n; jobPhoneCount.TryGetValue(kv.Value.job, out n); jobPhoneCount[kv.Value.job] = n + 1;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void JobHostTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < jobPollAt) return;
            jobPollAt = now + 2f;
            var ctl = SideJobController.Instance;
            if (ctl == null || ctl.allJobsDictionary == null) return;
            if (jobItemIndex == null) { jobPollAt = now + 0.5f; return; }
            var alive = new System.Collections.Generic.HashSet<int>();
            CountJobPhones();
            int sent = 0;
            foreach (var kv in ctl.allJobsDictionary)
            {
                var j = kv.Value; if (j == null) continue;
                alive.Add(j.jobID);
                float seen;
                if (!jobSeenAt.TryGetValue(j.jobID, out seen)) { jobSeenAt[j.jobID] = now; continue; }
                if (now - seen < 1.5f) continue;
                string sig = JobSig(j), old;
                if (jobSentSig.TryGetValue(j.jobID, out old) && old == sig) continue;
                if (sent >= 6) continue;
                if (SendJob(j, null)) { jobSentSig[j.jobID] = sig; sent++; }
            }
            foreach (var id in new System.Collections.Generic.List<int>(jobSentSig.Keys))
            {
                if (alive.Contains(id)) continue;
                jobSentSig.Remove(id); jobSeenAt.Remove(id);
                var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)2); w.Put(id);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[JOB] задание {id} убрано у хоста, сообщаю клиентам");
            }
        }

        static System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Interactable>> jobItemIndex, jobItemBuild;
        static int jobIndexPos, jobIndexScanned;
        static float jobIndexFullAt;

        static void JobIndexStep()
        {
            var list = CityData.Instance != null ? CityData.Instance.interactableDirectory : null;
            if (list == null) return;
            int count = list.Count;
            if (jobItemBuild == null && jobItemIndex != null)
            {
                if (count < jobIndexScanned) jobIndexFullAt = 0f;
                else if (count > jobIndexScanned)
                {
                    int to = Math.Min(count, jobIndexScanned + 1500);
                    IndexRange(list, jobIndexScanned, to, jobItemIndex);
                    jobIndexScanned = to;
                }
                if (Time.realtimeSinceStartup < jobIndexFullAt) return;
            }
            if (jobItemBuild == null) { jobItemBuild = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Interactable>>(); jobIndexPos = 0; }
            int end = Math.Min(count, jobIndexPos + 1500);
            IndexRange(list, jobIndexPos, end, jobItemBuild);
            jobIndexPos = end;
            if (jobIndexPos >= count) { jobItemIndex = jobItemBuild; jobItemBuild = null; jobIndexScanned = count; jobIndexFullAt = Time.realtimeSinceStartup + 30f; }
        }

        static void IndexRange(Il2CppSystem.Collections.Generic.List<Interactable> list, int from, int end, System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Interactable>> into)
        {
            for (int i = from; i < end; i++)
            {
                try
                {
                    var it = list[i]; if (it == null) continue;
                    var pv = it.pv; if (pv == null || pv.Count == 0) continue;
                    for (int k = 0; k < pv.Count; k++)
                    {
                        var x = pv[k];
                        if (x == null || x.varType != Interactable.PassedVarType.jobID) continue;
                        int jid = Mathf.RoundToInt(x.value);
                        System.Collections.Generic.List<Interactable> l;
                        if (!into.TryGetValue(jid, out l)) { l = new System.Collections.Generic.List<Interactable>(); into[jid] = l; }
                        if (!l.Contains(it)) l.Add(it);
                        break;
                    }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
        }

        static System.Collections.Generic.List<Interactable> JobItems(SideJob j)
        {
            var res = new System.Collections.Generic.List<Interactable>();
            var ids = new System.Collections.Generic.HashSet<int>();
            System.Action<Interactable> add = it => { try { if (it != null && it.inInventory == null && !rewardLocal.Contains(it.id) && res.Count < 60 && ids.Add(it.id)) res.Add(it); } catch (Exception __e) { Err.Swallow(__e); } };
            System.Collections.Generic.List<Interactable> idx;
            if (jobItemIndex != null && jobItemIndex.TryGetValue(j.jobID, out idx)) foreach (var it in idx) add(it);
            try { if (j.activeJobItems != null) foreach (var kv in j.activeJobItems) add(kv.Value); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (j.postID > 0 && !ids.Contains(j.postID)) { Interactable p; if (CityData.Instance.savableInteractableDictionary.TryGetValue(j.postID, out p) && p != null && p.inInventory == null) { ids.Add(p.id); res.Add(p); } } } catch (Exception __e) { Err.Swallow(__e); }
            return res;
        }

        static bool SendJob(SideJob j, NetPeer to)
        {
            try
            {
                var data = new StateSaveData();
                data.basicJobs = new Il2CppSystem.Collections.Generic.List<SideJob>();
                data.affairJobs = new Il2CppSystem.Collections.Generic.List<SideJobAffair>();
                data.sabotageJobs = new Il2CppSystem.Collections.Generic.List<SideJobSabotage>();
                data.stolenItemJobs = new Il2CppSystem.Collections.Generic.List<SideJobStolenItem>();
                data.missingPersonJobs = new Il2CppSystem.Collections.Generic.List<SideJobMissingPerson>();
                data.revengeJobs = new Il2CppSystem.Collections.Generic.List<SideJobRevenge>();
                data.briefcaseJobs = new Il2CppSystem.Collections.Generic.List<SideJobStealBriefcase>();
                data.interactables = new Il2CppSystem.Collections.Generic.List<Interactable>();
                var a = j.TryCast<SideJobAffair>(); var sb = j.TryCast<SideJobSabotage>(); var si = j.TryCast<SideJobStolenItem>();
                var mp = j.TryCast<SideJobMissingPerson>(); var rv = j.TryCast<SideJobRevenge>(); var bc = j.TryCast<SideJobStealBriefcase>();
                if (a != null) data.affairJobs.Add(a);
                else if (sb != null) data.sabotageJobs.Add(sb);
                else if (si != null) data.stolenItemJobs.Add(si);
                else if (mp != null) data.missingPersonJobs.Add(mp);
                else if (rv != null) data.revengeJobs.Add(rv);
                else if (bc != null) data.briefcaseJobs.Add(bc);
                else data.basicJobs.Add(j);
                var items = JobItems(j);
                foreach (var it in items) data.interactables.Add(it);
                int phones = AddFakeNumbers(data, j.jobID);
                string json = JsonUtility.ToJson(data);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)1); w.Put(j.jobID); w.PutBytesWithLength(bytes);
                if (to != null) to.Send(w, DeliveryMethod.ReliableOrdered); else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[JOB] задание {j.jobID} ({(j.preset != null ? j.preset.name : "?")}, {j.state}) отправлено: предметов {items.Count}, номеров телефона {phones}, {bytes.Length} байт");
                return true;
            }
            catch (Exception e) { if (!jobErrLogged) { jobErrLogged = true; Plugin.L.LogWarning("[JOB] отправка задания: " + e); } return false; }
        }

        static int AddFakeNumbers(StateSaveData data, int job)
        {
            int n = 0;
            try
            {
                var tc = TelephoneController.Instance;
                if (tc == null || tc.fakeTelephoneDictionary == null) return 0;
                if (data.fakeTelephone == null) data.fakeTelephone = new Il2CppSystem.Collections.Generic.List<StateSaveData.FakeTelephone>();
                foreach (var kv in tc.fakeTelephoneDictionary)
                {
                    var src = kv.Value;
                    if (src == null || (job >= 0 && src.job != job)) continue;
                    var ft = new StateSaveData.FakeTelephone(); ft.number = kv.Key; ft.source = src;
                    data.fakeTelephone.Add(ft); n++;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] номера телефонов: " + e.Message); }
            return n;
        }

        internal static void LogPhoneCall(int from, int to, Human caller, Human receiver, TelephoneController.CallSource src, TelephoneController.PhoneCall call)
        {
            if (manager == null) return;
            bool fromMe = caller != null && caller.isPlayer;
            if (!fromMe && (receiver == null || !receiver.isPlayer)) return;
            bool fake = false; TelephoneController.CallSource fs = null;
            try { var tc = TelephoneController.Instance; fake = tc != null && tc.fakeTelephoneDictionary != null && tc.fakeTelephoneDictionary.TryGetValue(to, out fs); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[PHONE] {(fromMe ? "я звоню" : "мне звонят")}: {from} -> {to}, кому: {(receiver != null ? receiver.humanID + " " + receiver.citizenName : "никому")}, " +
                $"источник: {(src != null ? src.callType + " задание " + src.job + " диалог '" + src.dialog + "'" : "нет")}, номер задания у меня: {(fake ? "да (задание " + fs.job + ")" : "нет")}, звонок создан: {call != null}" +
                JobStateText(src != null ? src.job : 0));
        }

        static string JobStateText(int jobId)
        {
            var j = jobId > 0 ? JobById(jobId) : null;
            if (j == null) return "";
            string cs = ""; try { if (j.thisCase != null) cs = j.thisCase.caseStatus.ToString(); } catch (Exception __e) { Err.Swallow(__e); }
            return $"; задание: этап {j.phase}, состояние {j.state}, дело {cs}, адрес сдачи известен {j.knowHandInLocation}";
        }

        static void SendJobSnapshot(NetPeer peer)
        {
            if (!Plugin.CfgJobSync.Value) return;
            var ctl = SideJobController.Instance;
            if (ctl == null || ctl.allJobsDictionary == null) return;
            var ids = new System.Collections.Generic.List<int>();
            CountJobPhones();
            foreach (var kv in ctl.allJobsDictionary)
            {
                var j = kv.Value; if (j == null) continue;
                ids.Add(j.jobID);
                if (SendJob(j, peer)) { jobSentSig[j.jobID] = JobSig(j); if (!jobSeenAt.ContainsKey(j.jobID)) jobSeenAt[j.jobID] = Time.realtimeSinceStartup - 10f; }
            }
            try
            {
                var pd = new StateSaveData();
                int n = AddFakeNumbers(pd, -1);
                if (n > 0)
                {
                    var b = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(pd));
                    var wp = new NetDataWriter(); wp.Put(MsgJob); wp.Put((byte)1); wp.Put(0); wp.PutBytesWithLength(b);
                    peer.Send(wp, DeliveryMethod.ReliableOrdered);
                    Plugin.L.LogInfo($"[JOB] номера телефонов заданий отправлены: {n}");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] номера телефонов: " + e.Message); }
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)6); w.Put(ids.Count);
            foreach (var id in ids) w.Put(id);
            peer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        static void ImportJob(int jobId, byte[] bytes)
        {
            var obj = JsonUtility.FromJson(System.Text.Encoding.UTF8.GetString(bytes), Il2CppInterop.Runtime.Il2CppType.Of<StateSaveData>());
            var data = obj != null ? obj.TryCast<StateSaveData>() : null;
            if (data == null) { Plugin.L.LogWarning($"[JOB] задание {jobId}: не разобрано"); return; }
            int newItems, updItems;
            ImportItems(data.interactables, "[JOB]", "задания " + jobId, out newItems, out updItems);
            try { DropMyJobDupes(JobById(jobId), data.interactables); } catch (Exception e) { Plugin.L.LogWarning($"[JOB] дубли задания {jobId}: {e.Message}"); }
            if (data.fakeTelephone != null)
            {
                int added = 0;
                var tc = TelephoneController.Instance;
                foreach (var ft in data.fakeTelephone)
                {
                    if (ft == null || tc == null) continue;
                    try
                    {
                        TelephoneController.CallSource old = null;
                        if (tc.fakeTelephoneDictionary == null || !tc.fakeTelephoneDictionary.TryGetValue(ft.number, out old) || old == null) { tc.AddFakeNumber(ft.number, ft.source); added++; }
                        else if (ft.source != null && ft.source.job > 0 && (old.dialog ?? "") != (ft.source.dialog ?? ""))
                        {
                            tc.RemoveFakeNumber(ft.number); tc.AddFakeNumber(ft.number, ft.source);
                            Plugin.L.LogInfo($"[JOB] номер {ft.number} задания {ft.source.job}: разговор '{old.dialog}' -> '{ft.source.dialog}', как у хоста");
                        }
                    }
                    catch (Exception e) { Plugin.L.LogWarning($"[JOB] номер {ft.number}: {e.Message}"); }
                }
                if (added > 0) Plugin.L.LogInfo($"[JOB] номеров телефона добавлено: {added}" + (jobId > 0 ? $" (задание {jobId})" : ""));
            }
            var jobs = new System.Collections.Generic.List<SideJob>();
            if (data.basicJobs != null) foreach (var x in data.basicJobs) jobs.Add(x);
            if (data.affairJobs != null) foreach (var x in data.affairJobs) jobs.Add(x);
            if (data.sabotageJobs != null) foreach (var x in data.sabotageJobs) jobs.Add(x);
            if (data.stolenItemJobs != null) foreach (var x in data.stolenItemJobs) jobs.Add(x);
            if (data.missingPersonJobs != null) foreach (var x in data.missingPersonJobs) jobs.Add(x);
            if (data.revengeJobs != null) foreach (var x in data.revengeJobs) jobs.Add(x);
            if (data.briefcaseJobs != null) foreach (var x in data.briefcaseJobs) jobs.Add(x);
            foreach (var nj in jobs)
            {
                if (nj == null) continue;
                var cur = JobById(nj.jobID);
                if (cur != null) { UpdateJob(cur, nj); continue; }
                bool wasAccepted = nj.accepted;
                nj.accepted = false; nj.caseID = -1;
                bool knew = nj.knowHandInLocation; string infoMsg = nj.jobInfoDialogMsg;
                nj.knowHandInLocation = false;
                if (wasAccepted) { nj.phase = 0; nj.phaseChange = -1; }
                SaveStateController.Instance.LoadJob(nj);
                try { var ctl = SideJobController.Instance; if (ctl != null && ctl.allJobsDictionary != null && !ctl.allJobsDictionary.ContainsKey(nj.jobID)) ctl.allJobsDictionary.Add(nj.jobID, nj); } catch (Exception __e) { Err.Swallow(__e); }
                Plugin.L.LogInfo($"[JOB] задание {nj.jobID} получено от хоста ({nj.state}): новых предметов {newItems}, обновлено {updItems}");
                if (wasAccepted && nj.state != SideJob.JobState.ended) AcceptJobHere(nj, "его взяли до моего подключения");
                if (knew) AcquireJobInfoHere(nj, infoMsg, "его выслушали до моего подключения");
            }
        }

        static void DropMyJobDupes(SideJob j, Il2CppSystem.Collections.Generic.List<Interactable> items)
        {
            if (IsHost || j == null || items == null || j.activeJobItems == null) return;
            var host = new System.Collections.Generic.Dictionary<string, Interactable>();
            var hostPtr = new System.Collections.Generic.HashSet<IntPtr>();
            var hostIds = new System.Collections.Generic.HashSet<int>();
            foreach (var it in items)
            {
                if (it == null || !HasJobId(it, j.jobID)) continue;
                Interactable loc;
                if (!idMap.TryGetValue(it.id | SideOf(0), out loc) || loc == null) loc = FindById(it.id);
                if (loc == null) continue;
                hostPtr.Add(loc.Pointer); hostIds.Add(loc.id); hostIds.Add(it.id);
                host[it.p ?? ""] = loc;
            }
            if (host.Count == 0) return;
            var drop = new System.Collections.Generic.List<(JobPreset.JobTag tag, Interactable mine, Interactable theirs)>();
            foreach (var kv in j.activeJobItems)
            {
                var mine = kv.Value; if (mine == null || hostPtr.Contains(mine.Pointer) || hostIds.Contains(mine.id) || rewardLocal.Contains(mine.id)) continue;
                Interactable theirs;
                if (!host.TryGetValue(PresetNameOf(mine), out theirs) || theirs.id == mine.id || theirs.Pointer == mine.Pointer) continue;
                if (mine.id < RuntimeIdStart) continue;
                try { if (mine.inInventory != null) continue; } catch (Exception __e) { Err.Swallow(__e); }
                drop.Add((kv.Key, mine, theirs));
            }
            foreach (var d in drop)
            {
                j.activeJobItems[d.tag] = d.theirs;
                applyingRemote = true;
                try { d.mine.SafeDelete(false); } catch (Exception __e) { Err.Swallow(__e); } finally { applyingRemote = false; }
                Plugin.L.LogInfo($"[JOB] задание {j.jobID}: мой '{PresetNameOf(d.mine)}' (id {d.mine.id}) убрал - есть такой же от хоста (id {d.theirs.id})");
            }
        }

        static bool HasJobId(Interactable it, int jobId)
        {
            var pv = it.pv; if (pv == null) return false;
            for (int k = 0; k < pv.Count; k++) { var x = pv[k]; if (x != null && x.varType == Interactable.PassedVarType.jobID && Mathf.RoundToInt(x.value) == jobId) return true; }
            return false;
        }

        static int localItemId = 1900000000;
        static int FreeLocalItemId()
        {
            var dict = CityData.Instance.savableInteractableDictionary;
            while (dict != null && dict.ContainsKey(localItemId)) localItemId++;
            return localItemId++;
        }

        static void ImportItems(Il2CppSystem.Collections.Generic.List<Interactable> items, string tag, string what, out int newItems, out int updItems)
        {
            newItems = 0; updItems = 0;
            if (items == null) return;
            var dict = CityData.Instance.savableInteractableDictionary;
            var cc = CityConstructor.Instance;
            Il2CppSystem.Collections.Generic.Dictionary<int, FurnitureLocation> oldRef = null;
            bool swapped = false;
            try { if (cc != null) { oldRef = cc.loadingFurnitureReference; cc.loadingFurnitureReference = FurnitureRef(items); swapped = true; } }
            catch (Exception e) { Plugin.L.LogWarning(tag + " таблица мебели: " + e.Message); }
            try
            {
                foreach (var it in items)
                {
                    if (it == null) continue;
                    try
                    {
                        Interactable ex;
                        if (dict != null && dict.TryGetValue(it.id, out ex) && ex != null)
                        {
                            if (PresetNameOf(ex) != (it.p ?? ""))
                            {
                                int nid = FreeLocalItemId();
                                dict.Remove(it.id); ex.id = nid; dict[nid] = ex;
                                Plugin.L.LogInfo($"{tag} предмет {it.id} ({what}): мой '{PresetNameOf(ex)}' перенёс на номер {nid}, ставлю '{it.p}' хоста");
                                it.wasLoadedFromSave = true; it.MainSetupStart(); it.OnLoad(); newItems++;
                                MapHostItem(it, it.id);
                                continue;
                            }
                            ex.CopyState(it); updItems++;
                            MapHostItem(ex, ex.id);
                        }
                        else { it.wasLoadedFromSave = true; it.MainSetupStart(); it.OnLoad(); newItems++; MapHostItem(it, it.id); }
                    }
                    catch (Exception e) { Plugin.L.LogWarning($"{tag} предмет {it.id} ({what}): {e.Message}"); }
                }
            }
            finally { if (swapped) try { cc.loadingFurnitureReference = oldRef; } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static void MapHostItem(Interactable local, int hostId)
        {
            if (IsHost || local == null || hostId < RuntimeIdStart || (hostId & SideMask) != 0) return;
            int g = hostId | SideOf(0);
            idMap[g] = local; gidOf[local.id] = g;
        }

        static Il2CppSystem.Collections.Generic.Dictionary<int, FurnitureLocation> furnRef;

        static Il2CppSystem.Collections.Generic.Dictionary<int, FurnitureLocation> FurnitureRef(Il2CppSystem.Collections.Generic.List<Interactable> items)
        {
            bool need = furnRef == null;
            if (!need) foreach (var it in items) if (it != null && it.fp > -1 && !furnRef.ContainsKey(it.fp)) { need = true; break; }
            if (!need) return furnRef;
            var d = new Il2CppSystem.Collections.Generic.Dictionary<int, FurnitureLocation>();
            var rooms = CityData.Instance.roomDirectory;
            for (int i = 0; rooms != null && i < rooms.Count; i++)
            {
                var r = rooms[i]; if (r == null || r.individualFurniture == null) continue;
                foreach (var f in r.individualFurniture) if (f != null && !d.ContainsKey(f.id)) d.Add(f.id, f);
            }
            furnRef = d;
            Plugin.L.LogInfo($"[JOB] таблица мебели для загрузки предметов: {d.Count}");
            return d;
        }

        static System.Collections.Generic.List<(byte k, double a, double b, double c)> JobChoices(SideJob j)
        {
            var l = new System.Collections.Generic.List<(byte, double, double, double)>();
            if (j == null) return l;
            if (j.meetingPoint != 0) { l.Add((1, j.meetingPoint, FurnIdOf(j.chosenMeetingPoint), 0)); l.Add((2, j.meetingConsumableIndex, 0, 0)); }
            if (j.secretLocationFurniture != 0) l.Add((3, j.secretLocationFurniture, 0, 0));
            if (j.secretLocationNode != Vector3Int.zero) l.Add((4, j.secretLocationNode.x, j.secretLocationNode.y, j.secretLocationNode.z));
            if (j.gooseChasePhone != 0) { l.Add((5, j.gooseChasePhone, FurnIdOf(j.chosenGooseChasePhone), 0)); l.Add((6, j.gooseChaseFromPhone, 0, 0)); }
            if (j.gooseChaseCallTime != 0f) l.Add((7, j.gooseChaseCallTime, 0, 0));
            var sb = j.TryCast<SideJobSabotage>();
            if (sb != null) { if (sb.chosenAddress != null) l.Add((8, sb.chosenAddress.id, 0, 0)); if (sb.callTime != 0f) l.Add((9, sb.callTime, 0, 0)); }
            var si = j.TryCast<SideJobStolenItem>();
            if (si != null) { if (si.theftTime != 0f) l.Add((10, si.theftTime, 0, 0)); if (si.theftTimeFrom != 0f) l.Add((11, si.theftTimeFrom, 0, 0)); if (si.theftTimeTo != 0f) l.Add((12, si.theftTimeTo, 0, 0)); if (si.stolenItemRoom != 0) l.Add((13, si.stolenItemRoom, 0, 0)); }
            var bc = j.TryCast<SideJobStealBriefcase>();
            if (bc != null) { if (bc.carrier != 0) l.Add((14, bc.carrier, 0, 0)); if (bc.meetNodeLocation != Vector3Int.zero) l.Add((15, bc.meetNodeLocation.x, bc.meetNodeLocation.y, bc.meetNodeLocation.z)); }
            return l;
        }

        static int FurnIdOf(Interactable it)
        {
            try { return it != null && it.furnitureParent != null ? it.furnitureParent.id : 0; } catch (Exception) { return 0; }
        }

        static Interactable FindItemOrIntegrated(int id, int furnId)
        {
            var it = FindById(id);
            if (it != null) return it;
            try { if (CityData.Instance.savableInteractableDictionary.TryGetValue(id, out it) && it != null) return it; } catch (Exception __e) { Err.Swallow(__e); }
            if (furnId == 0) return null;
            try
            {
                var map = FurnitureRef(new Il2CppSystem.Collections.Generic.List<Interactable>());
                FurnitureLocation f;
                if (map == null || !map.TryGetValue(furnId, out f) || f == null) { Plugin.L.LogWarning($"[JOB] мебель {furnId} у меня не найдена"); return null; }
                if (!f.createdInteractables) f.CreateInteractables();
                if (f.integratedInteractables != null) foreach (var x in f.integratedInteractables) if (x != null && x.id == id) return x;
                Plugin.L.LogWarning($"[JOB] в мебели {furnId} нет предмета {id} (встроенных: {(f.integratedInteractables != null ? f.integratedInteractables.Count : 0)})");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] мебель {furnId}: {e.Message}"); }
            return null;
        }

        static void PutJobChoices(NetDataWriter w, SideJob j)
        {
            var l = JobChoices(j);
            w.Put((byte)l.Count);
            foreach (var x in l) { w.Put(x.k); w.Put(x.a); w.Put(x.b); w.Put(x.c); }
        }

        static void ApplyJobChoices(NetDataReader r, SideJob j, int pid)
        {
            bool force = !IsHost && pid == 0;
            int n = r.GetByte();
            var got = new System.Collections.Generic.Dictionary<byte, (double a, double b, double c)>();
            for (int i = 0; i < n; i++) { byte key = r.GetByte(); got[key] = (r.GetDouble(), r.GetDouble(), r.GetDouble()); }
            if (j == null || n == 0) return;
            var applied = new System.Collections.Generic.List<string>();
            (double a, double b, double c) v;
            try
            {
                if (got.TryGetValue(1, out v) && (force ? j.meetingPoint != (int)v.a : j.meetingPoint == 0))
                {
                    var mp = FindItemOrIntegrated((int)v.a, (int)v.b);
                    if (mp != null)
                    {
                        j.meetingPoint = (int)v.a; j.chosenMeetingPoint = mp;
                        (double a, double b, double c) c2; if (got.TryGetValue(2, out c2)) j.meetingConsumableIndex = (int)c2.a;
                        applied.Add("место встречи " + mp.GetName());
                    }
                    else Plugin.L.LogWarning($"[JOB] задание {j.jobID}: место встречи {(int)v.a} у меня не найдено");
                }
                if (got.TryGetValue(3, out v) && (force ? j.secretLocationFurniture != (int)v.a : j.secretLocationFurniture == 0)) { j.secretLocationFurniture = (int)v.a; applied.Add("мебель тайника"); }
                if (got.TryGetValue(4, out v) && (force || j.secretLocationNode == Vector3Int.zero) && j.secretLocationNode != new Vector3Int((int)v.a, (int)v.b, (int)v.c)) { j.secretLocationNode = new Vector3Int((int)v.a, (int)v.b, (int)v.c); applied.Add("тайник"); }
                if (got.TryGetValue(5, out v) && (force ? j.gooseChasePhone != (int)v.a : j.gooseChasePhone == 0))
                {
                    var ph = FindItemOrIntegrated((int)v.a, (int)v.b);
                    j.gooseChasePhone = (int)v.a; if (ph != null) j.chosenGooseChasePhone = ph;
                    (double a, double b, double c) c6; if (got.TryGetValue(6, out c6)) j.gooseChaseFromPhone = (int)c6.a;
                    applied.Add("таксофон " + (ph != null ? ph.GetName() : "?"));
                }
                if (got.TryGetValue(7, out v) && (force ? j.gooseChaseCallTime != v.a : j.gooseChaseCallTime == 0f)) { j.gooseChaseCallTime = (float)v.a; applied.Add("время звонка"); }
                var sb = j.TryCast<SideJobSabotage>();
                if (sb != null)
                {
                    NewAddress ad;
                    if (got.TryGetValue(8, out v) && (force ? (sb.chosenAddress == null || sb.chosenAddress.id != (int)v.a) : sb.chosenAddress == null) && CityData.Instance.addressDictionary.TryGetValue((int)v.a, out ad)) { sb.chosenAddress = ad; applied.Add("адрес"); }
                    if (got.TryGetValue(9, out v) && (force ? sb.callTime != v.a : sb.callTime == 0f)) { sb.callTime = (float)v.a; applied.Add("время звонка"); }
                }
                var si = j.TryCast<SideJobStolenItem>();
                if (si != null)
                {
                    if (got.TryGetValue(10, out v) && (force || si.theftTime == 0f)) si.theftTime = (float)v.a;
                    if (got.TryGetValue(11, out v) && (force || si.theftTimeFrom == 0f)) si.theftTimeFrom = (float)v.a;
                    if (got.TryGetValue(12, out v) && (force || si.theftTimeTo == 0f)) si.theftTimeTo = (float)v.a;
                    if (got.TryGetValue(13, out v) && (force || si.stolenItemRoom == 0)) si.stolenItemRoom = (int)v.a;
                }
                var bc = j.TryCast<SideJobStealBriefcase>();
                if (bc != null)
                {
                    if (got.TryGetValue(14, out v) && (force ? bc.carrier != (int)v.a : bc.carrier == 0)) { bc.carrier = (int)v.a; applied.Add("носитель кейса"); }
                    if (got.TryGetValue(15, out v) && (force || bc.meetNodeLocation == Vector3Int.zero) && bc.meetNodeLocation != new Vector3Int((int)v.a, (int)v.b, (int)v.c)) { bc.meetNodeLocation = new Vector3Int((int)v.a, (int)v.b, (int)v.c); applied.Add("место встречи"); }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] выбор задания {j.jobID}: {e.Message}"); }
            if (applied.Count > 0) Plugin.L.LogInfo($"[JOB] задание {j.jobID}: от игрока {pid} - " + string.Join(", ", applied));
        }

        static readonly System.Collections.Generic.Dictionary<int, string> jobChoiceSent = new System.Collections.Generic.Dictionary<int, string>();

        static void JobChoicesTick()
        {
            var ctl = SideJobController.Instance;
            if (ctl == null || ctl.allJobsDictionary == null) return;
            foreach (var kv in ctl.allJobsDictionary)
            {
                var j = kv.Value; if (j == null || !j.accepted || j.state == SideJob.JobState.ended) continue;
                var l = JobChoices(j);
                if (l.Count == 0) continue;
                var sig = string.Join(";", l);
                string old; if (jobChoiceSent.TryGetValue(j.jobID, out old) && old == sig) continue;
                jobChoiceSent[j.jobID] = sig;
                var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)11); w.Put(j.jobID); PutJobChoices(w, j);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
        }

        internal static bool EnsureJobPost(SideJob j)
        {
            try
            {
                if (j.post != null) return true;
                Interactable p = null;
                var dict = CityData.Instance.savableInteractableDictionary;
                bool has = j.postID > 0 && dict != null && dict.TryGetValue(j.postID, out p) && p != null;
                if (has && PresetNameOf(p) == "JobNote") { j.post = p; Plugin.L.LogInfo($"[JOB] задание {j.jobID}: ссылка на объявление {j.postID} восстановлена"); return true; }
                Plugin.L.LogWarning($"[JOB] задание {j.jobID}: нет объявления (postID {j.postID}, под этим номером у меня {(has ? "'" + PresetNameOf(p) + "'" : "ничего")}) - взять не могу, игра упала бы");
                Toast($"Job {j.jobID}: its note is missing here, cannot take it.");
                return false;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] объявление задания {j.jobID}: {e.Message}"); return true; }
        }
    }
}

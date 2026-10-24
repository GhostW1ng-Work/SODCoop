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
        static readonly System.Collections.Generic.Dictionary<int, int> printSent = new System.Collections.Generic.Dictionary<int, int>();
        static readonly System.Collections.Generic.HashSet<int> printAsked = new System.Collections.Generic.HashSet<int>();
        static readonly System.Collections.Generic.Dictionary<int, int> printFromHost = new System.Collections.Generic.Dictionary<int, int>();

        static void AssignPrintLetter(Human h)
        {
            var gc = GameplayController.Instance;
            if (gc == null || h == null) return;
            h.fingerprintLoop = gc.printsLetterLoop;
            gc.printsLetterLoop = gc.printsLetterLoop + 1;
            Plugin.L.LogInfo($"[PRINTS] житель {h.humanID}: отпечатки нашёл другой игрок, буква №{h.fingerprintLoop}");
        }

        static void ApplyPrintLetter(int hid, int loop)
        {
            printFromHost[hid] = loop;
            Human h;
            if (!CityData.Instance.GetHuman(hid, out h, false) || h == null) return;
            if (h.fingerprintLoop != loop)
            {
                foreach (var c in CityData.Instance.citizenDirectory) if (c != null && c.humanID != hid && c.fingerprintLoop == loop && !printFromHost.ContainsKey(c.humanID)) { c.fingerprintLoop = -1; printAsked.Remove(c.humanID); }
                h.fingerprintLoop = loop;
            }
            var gc = GameplayController.Instance;
            if (gc != null && gc.printsLetterLoop <= loop) gc.printsLetterLoop = loop + 1;
        }

        static void PrintLettersTick()
        {
            var dir = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            if (dir == null) return;
            if (IsHost)
            {
                var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)12);
                var fresh = new System.Collections.Generic.List<(int, int)>();
                foreach (var c in dir)
                {
                    if (c == null || c.fingerprintLoop < 0) continue;
                    int old; if (printSent.TryGetValue(c.humanID, out old) && old == c.fingerprintLoop) continue;
                    printSent[c.humanID] = c.fingerprintLoop; fresh.Add((c.humanID, c.fingerprintLoop));
                }
                if (fresh.Count == 0) return;
                w.Put(fresh.Count); foreach (var x in fresh) { w.Put(x.Item1); w.Put(x.Item2); }
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                foreach (var c in dir)
                {
                    if (c == null || c.fingerprintLoop < 0 || printFromHost.ContainsKey(c.humanID) || !printAsked.Add(c.humanID)) continue;
                    var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)13); w.Put(c.humanID);
                    manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        static void SendPrintSnapshot(NetPeer peer)
        {
            var dir = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            if (dir == null) return;
            var l = new System.Collections.Generic.List<(int, int)>();
            foreach (var c in dir) if (c != null && c.fingerprintLoop >= 0) l.Add((c.humanID, c.fingerprintLoop));
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)12); w.Put(l.Count);
            foreach (var x in l) { w.Put(x.Item1); w.Put(x.Item2); }
            peer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        static void EnsureAcceptSent(int jobId)
        {
            var j = JobById(jobId);
            if (j != null && j.accepted && !jobAcceptSent.Contains(jobId)) OnJobAccepted(j);
        }

        internal static void OnObjectiveAdded(Case c, object[] a)
        {
            if (applyingJob || c == null || RtMode == NetMode.Off || !Plugin.CfgJobSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
            int jobId = c.jobReference;
            if (jobId <= 0) return;
            EnsureAcceptSent(jobId);
            var t = a[1] as Objective.ObjectiveTrigger;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)10); w.Put(jobId);
            PutJobChoices(w, JobById(jobId));
            w.Put((string)a[0] ?? "");
            w.Put(t != null);
            if (t != null)
            {
                w.Put((int)t.triggerType); w.Put(t.forceProgressAmount); w.Put(t.progressAdd); w.Put(t.name ?? ""); w.Put(t.hightlightAction ?? ""); w.Put(t.orTrigger);
                w.Put(t.roomID); w.Put(t.interactableID); w.Put(t.evidenceID ?? ""); w.Put(t.nodeCoord.x); w.Put(t.nodeCoord.y); w.Put(t.nodeCoord.z);
                w.Put(t.doorID); w.Put(t.addressID); w.Put(t.streetID); w.Put(t.jobID); w.Put(t.position.x); w.Put(t.position.y); w.Put(t.position.z);
            }
            var pp = (Vector3)a[3];
            w.Put((bool)a[2]); w.Put(pp.x); w.Put(pp.y); w.Put(pp.z); w.Put(Convert.ToInt32(a[4])); w.Put(Convert.ToInt32(a[5])); w.Put((float)a[6]);
            w.Put((bool)a[7]); w.Put((string)a[8] ?? ""); w.Put((bool)a[9]); w.Put((bool)a[10]); w.Put((bool)a[12]); w.Put((bool)a[14]);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] задание {jobId}: новая цель '{a[0]}', сообщаю другим");
        }

        internal static bool HasObjective(Case c, string entry, bool done)
        {
            if (c == null || string.IsNullOrEmpty(entry)) return false;
            foreach (var lst in new[] { c.currentActiveObjectives, c.inactiveCurrentObjectives, c.waitForObjectives, c.endedObjectives })
            {
                if (lst == null) continue;
                foreach (var o in lst)
                {
                    if (o == null || o.queueElement == null || o.queueElement.entryRef != entry) continue;
                    if (o.isComplete) { if (done) return true; }
                    else if (!o.isCancelled) return true;
                }
            }
            return false;
        }

        static void AddObjectiveHere(RP rp, NetDataReader r)
        {
            int jobId = r.GetInt();
            int pid = rp != null ? rp.pid : 0;
            var j = JobById(jobId);
            ApplyJobChoices(r, j, pid);
            string entry = r.GetString();
            Objective.ObjectiveTrigger t = null;
            if (r.GetBool())
            {
                var type = (Objective.ObjectiveTriggerType)r.GetInt(); bool force = r.GetBool(); float add = r.GetFloat(); string name = r.GetString(); string hl = r.GetString(); bool or = r.GetBool();
                int room = r.GetInt(), inter = r.GetInt(); string ev = r.GetString(); var node = new Vector3Int(r.GetInt(), r.GetInt(), r.GetInt());
                int door = r.GetInt(), addr = r.GetInt(), street = r.GetInt(), tj = r.GetInt(); var pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                t = new Objective.ObjectiveTrigger(type, name, force, add, null, null, null, null, null, null, j, hl, or, pos);
                t.roomID = room; t.interactableID = inter; t.evidenceID = ev; t.nodeCoord = node; t.doorID = door; t.addressID = addr; t.streetID = street; t.jobID = tj;
            }
            bool usePointer = r.GetBool(); var pp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            var icon = (InterfaceControls.Icon)r.GetInt(); var act = (Objective.OnCompleteAction)r.GetInt(); float delay = r.GetFloat();
            bool removePrev = r.GetBool(); string chapter = r.GetString(); bool silent = r.GetBool(); bool crouch = r.GetBool(); bool bottom = r.GetBool(); bool parsing = r.GetBool();
            var c = j != null ? j.thisCase : null;
            if (c == null) { Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{entry}' от игрока {pid} - у меня задание не взято"); return; }
            bool haveOpen = false; try { haveOpen = HasObjective(c, entry, false); } catch (Exception __e) { Err.Swallow(__e); }
            if (!haveOpen && !MirrorJobs)
            {
                pendingObjectives.RemoveAll(x => x.job == jobId && x.entry == entry);
                pendingObjectives.Add((jobId, entry, Time.realtimeSinceStartup + 4f, () => ApplyRemoteObjective(j, c, pid, entry, t, usePointer, pp, icon, act, delay, removePrev, chapter, silent, crouch, bottom, parsing)));
                Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{entry}' от игрока {pid} - жду, не поставит ли её моя игра");
                return;
            }
            ApplyRemoteObjective(j, c, pid, entry, t, usePointer, pp, icon, act, delay, removePrev, chapter, silent, crouch, bottom, parsing);
        }

        static readonly System.Collections.Generic.HashSet<int> hudNoted = new System.Collections.Generic.HashSet<int>();
        static float hudAt;
        static string hudLast = "";

        internal static void NoteJobObjective(Case c)
        {
            if (c != null && c.jobReference > 0 && RtMode != NetMode.Off) hudNoted.Add(c.jobReference);
        }

        internal static void OnObjectiveCancel(Objective o)
        {
            if (o == null || o.isCancelled || o.isComplete || RtMode == NetMode.Off) return;
            var c = o.thisCase;
            if (c == null || c.jobReference <= 0) return;
            Plugin.L.LogInfo($"[HUD] задание {c.jobReference}: цель '{o.name}' отменена{(applyingJob ? " (при передаче от другого игрока)" : " игрой")}");
        }

        static int OpenObjectives(Case c, System.Text.StringBuilder names)
        {
            int n = 0;
            if (c == null) return 0;
            foreach (var lst in new[] { c.currentActiveObjectives, c.inactiveCurrentObjectives })
            {
                if (lst == null) continue;
                foreach (var o in lst)
                    if (o != null && !o.isComplete && !o.isCancelled) { n++; if (names != null) names.Append(names.Length > 0 ? ", " : "").Append(o.name); }
            }
            return n;
        }

        static void ObjectiveHudTick()
        {
            if (Time.realtimeSinceStartup < hudAt) return;
            hudAt = Time.realtimeSinceStartup + 1f;
            var cp = CasePanelController.Instance;
            if (cp == null || cp.activeCases == null) return;
            if (hudNoted.Count > 0)
            {
                var ids = new System.Collections.Generic.List<int>(hudNoted); hudNoted.Clear();
                foreach (int id in ids)
                {
                    var j = JobById(id); var c = j != null ? j.thisCase : null;
                    if (c == null || !j.accepted || j.state == SideJob.JobState.ended || cp.activeCase == c || OpenObjectives(c, null) == 0) continue;
                    bool listed = false; foreach (var x in cp.activeCases) if (x != null && x.Pointer == c.Pointer) { listed = true; break; }
                    if (!listed) continue;
                    string was = cp.activeCase != null ? cp.activeCase.name : "нет";
                    cp.SetActiveCase(c);
                    Plugin.L.LogInfo($"[HUD] задание {id}: новая цель, а активным было дело '{was}' - делаю активным дело задания '{c.name}', чтобы цели были на экране");
                }
            }
            var sb = new System.Text.StringBuilder();
            var ac = cp.activeCase;
            sb.Append("активное дело '").Append(ac != null ? ac.name : "нет").Append("' (задание ").Append(ac != null ? ac.jobReference : 0).Append(")");
            var ctl = SideJobController.Instance;
            if (ctl != null && ctl.allJobsDictionary != null)
                foreach (var kv in ctl.allJobsDictionary)
                {
                    var j = kv.Value;
                    if (j == null || !j.accepted || j.state == SideJob.JobState.ended || j.thisCase == null) continue;
                    var names = new System.Text.StringBuilder();
                    int n = OpenObjectives(j.thisCase, names);
                    sb.Append("; задание ").Append(j.jobID).Append(" этап ").Append(j.phase).Append(": целей ").Append(n).Append(" [").Append(names).Append("]");
                }
            string st = sb.ToString();
            if (st != hudLast) { hudLast = st; Plugin.L.LogInfo("[HUD] " + st); }
        }

        static readonly System.Collections.Generic.List<(int job, string entry, float at, Action apply)> pendingObjectives = new System.Collections.Generic.List<(int, string, float, Action)>();

        static void PendingObjectivesTick(int flushJob = 0)
        {
            if (pendingObjectives.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            var due = pendingObjectives.FindAll(x => x.at <= now || x.job == flushJob);
            if (due.Count == 0) return;
            pendingObjectives.RemoveAll(x => x.at <= now || x.job == flushJob);
            foreach (var x in due)
            {
                try { x.apply(); } catch (Exception e) { Plugin.L.LogWarning($"[JOB] цель '{x.entry}' задания {x.job}: {e.Message}"); }
            }
        }

        static void ApplyRemoteObjective(SideJob j, Case c, int pid, string entry, Objective.ObjectiveTrigger t, bool usePointer, Vector3 pp, InterfaceControls.Icon icon,
            Objective.OnCompleteAction act, float delay, bool removePrev, string chapter, bool silent, bool crouch, bool bottom, bool parsing)
        {
            int jobId = j.jobID;
            if (j.thisCase != c || j.state == SideJob.JobState.ended) return;
            bool mineOpen = false; try { mineOpen = HasObjective(c, entry, false); } catch (Exception __e) { Err.Swallow(__e); }
            if (mineOpen && (IsHost || pid != 0)) { Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{entry}' от игрока {pid} - моя игра поставила свою, оставляю её"); return; }
            bool doneHere = false; try { doneHere = HasObjective(c, entry, true) && !mineOpen; } catch (Exception __e) { Err.Swallow(__e); }
            if (doneHere) { Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{entry}' от игрока {pid} - у меня уже выполнена, заново не ставлю"); return; }
            if (t != null && t.interactableID != 0 && t.interactableID != -1 && !IsHost && pid == 0)
            {
                Interactable tgt = null;
                if (!idMap.TryGetValue(t.interactableID | SideOf(0), out tgt) || tgt == null) tgt = FindById(t.interactableID);
                if (tgt != null) t.interactableID = tgt.id;
                else
                {
                    bool haveMine = false;
                    foreach (var lst in new[] { c.currentActiveObjectives, c.inactiveCurrentObjectives })
                        if (lst != null) foreach (var o in lst) if (o != null && !o.isComplete && o.queueElement != null && o.queueElement.entryRef == entry) haveMine = true;
                    if (haveMine) { Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{entry}' хоста указывает на его предмет {t.interactableID}, которого у меня нет - оставляю свою"); return; }
                }
            }
            applyingJob = true;
            try
            {
                if (t != null) t.SetupNonSerialized();
                if (!IsHost && pid == 0)
                {
                    int removed = 0;
                    foreach (var lst in new[] { c.currentActiveObjectives, c.inactiveCurrentObjectives })
                    {
                        if (lst == null) continue;
                        var mine = new System.Collections.Generic.List<Objective>();
                        foreach (var o in lst) if (o != null && !o.isComplete && o.queueElement != null && o.queueElement.entryRef == entry) mine.Add(o);
                        foreach (var o in mine) { try { o.Remove(); removed++; } catch (Exception __e) { Err.Swallow(__e); } }
                    }
                    if (removed > 0) Plugin.L.LogInfo($"[JOB] задание {jobId}: моя цель '{entry}' заменена хостовой");
                }
                CallAddObjective(c, entry, t, usePointer, pp, icon, act, delay, removePrev, chapter, silent, crouch, j, bottom, false, parsing);
                Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{entry}' от игрока {pid} передана игре (дубль игра не поставит)");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] цель '{entry}' задания {jobId}: {e.Message}"); }
            finally { applyingJob = false; }
        }

        static System.Reflection.MethodInfo addObjMethod;
        static bool addObjLooked;

        internal static System.Reflection.MethodInfo AddObjectiveMethod()
        {
            if (addObjLooked) return addObjMethod;
            addObjLooked = true;
            foreach (var m in typeof(Case).GetMethods())
            {
                if (m.Name != "AddObjective") continue;
                var ps = m.GetParameters();
                if (ps.Length < 2 || ps[1].ParameterType != typeof(Objective.ObjectiveTrigger)) continue;
                if (addObjMethod == null || ps.Length > addObjMethod.GetParameters().Length) addObjMethod = m;
            }
            if (addObjMethod == null) Plugin.L.LogWarning("[JOB] Case.AddObjective не найден - цели заданий не будут синхронизироваться");
            else
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var p in addObjMethod.GetParameters()) names.Add(p.Name);
                Plugin.L.LogInfo($"[JOB] Case.AddObjective: параметров {names.Count} ({string.Join(", ", names)})");
            }
            return addObjMethod;
        }

        static void CallAddObjective(Case c, string entry, Objective.ObjectiveTrigger t, bool usePointer, Vector3 pp, InterfaceControls.Icon icon,
            Objective.OnCompleteAction act, float delay, bool removePrev, string chapter, bool silent, bool crouch, SideJob j, bool bottom, bool ignoreDup, bool parsing)
        {
            var m = AddObjectiveMethod();
            if (m == null) throw new MissingMethodException("Case.AddObjective");
            var known = new System.Collections.Generic.Dictionary<string, object>
            {
                ["entryRef"] = entry, ["trigger"] = t, ["usePointer"] = usePointer, ["pointerPosition"] = pp, ["useIcon"] = icon,
                ["onCompleteAction"] = act, ["delay"] = delay, ["removePrevious"] = removePrev, ["chapterString"] = chapter, ["isSilent"] = silent,
                ["allowCrouchPromt"] = crouch, ["jobRef"] = j, ["forceBottomOfList"] = bottom, ["ignoreDuplicates"] = ignoreDup, ["useParsing"] = parsing
            };
            var ordered = new object[] { entry, t, usePointer, pp, icon, act, delay, removePrev, chapter, silent, crouch, j, bottom, ignoreDup, parsing };
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                object v;
                if (known.TryGetValue(ps[i].Name ?? "", out v)) { args[i] = v; continue; }
                if (ps.Length == ordered.Length) { args[i] = ordered[i]; continue; }
                var pt = ps[i].ParameterType;
                if (ps[i].HasDefaultValue && ps[i].DefaultValue != null && !(ps[i].DefaultValue is DBNull)) args[i] = ps[i].DefaultValue;
                else args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            m.Invoke(c, args);
        }

        internal static void OnObjectiveCompleted(Objective o)
        {
            if (applyingJob || o == null || RtMode == NetMode.Off || !Plugin.CfgJobSync.Value) return;
            var c = o.thisCase;
            int jobId = c != null ? c.jobReference : 0;
            if (jobId <= 0 || string.IsNullOrEmpty(o.name)) return;
            if (o.queueElement != null && IsRewardObjective(o.queueElement.entryRef)) return;
            EnsureAcceptSent(jobId);
            Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{o.name}' выполнена у меня, сообщаю другим");
            if (manager == null || manager.ConnectedPeersCount == 0) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)9); w.Put(jobId); w.Put(o.name);
            PutJobChoices(w, JobById(jobId));
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static readonly System.Collections.Generic.List<(int job, string name, int pid, float until)> pendingCompletes = new System.Collections.Generic.List<(int, string, int, float)>();

        static void PendingCompletesTick()
        {
            if (pendingCompletes.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = pendingCompletes.Count - 1; i >= 0; i--)
            {
                var x = pendingCompletes[i];
                var j = JobById(x.job);
                if (j == null || j.state == SideJob.JobState.ended) { pendingCompletes.RemoveAt(i); continue; }
                if (CompleteObjectiveHere(x.job, x.name, x.pid, true)) { pendingCompletes.RemoveAt(i); continue; }
                if (now > x.until)
                {
                    pendingCompletes.RemoveAt(i);
                    Plugin.L.LogWarning($"[JOB] задание {x.job}: цель '{x.name}' игрока {x.pid} так и не появилась у меня за 15 с");
                }
            }
        }

        static bool CompleteObjectiveHere(int jobId, string name, int pid, bool retry = false)
        {
            var j = JobById(jobId);
            var c = j != null ? j.thisCase : null;
            if (c == null)
            {
                if (retry) return false;
                pendingCompletes.Add((jobId, name, pid, Time.realtimeSinceStartup + 15f));
                Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{name}' игрока {pid} - у меня задание не взято, жду");
                return false;
            }
            var found = new System.Collections.Generic.List<Objective>();
            foreach (var lst in new[] { c.currentActiveObjectives, c.inactiveCurrentObjectives })
                if (lst != null) foreach (var o in lst) if (o != null && !o.isComplete && !o.isCancelled && o.name == name) found.Add(o);
            if (found.Count == 0 && pendingObjectives.Exists(x => x.job == jobId))
            {
                PendingObjectivesTick(jobId);
                foreach (var lst in new[] { c.currentActiveObjectives, c.inactiveCurrentObjectives })
                    if (lst != null) foreach (var o in lst) if (o != null && !o.isComplete && !o.isCancelled && o.name == name) found.Add(o);
            }
            if (found.Count == 0)
            {
                bool doneAlready = false;
                foreach (var lst in new[] { c.currentActiveObjectives, c.inactiveCurrentObjectives, c.endedObjectives })
                    if (lst != null) foreach (var o in lst) if (o != null && o.isComplete && o.name == name) doneAlready = true;
                if (doneAlready) return true;
                if (!retry)
                {
                    pendingCompletes.Add((jobId, name, pid, Time.realtimeSinceStartup + 15f));
                    Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{name}' игрока {pid} - у меня её ещё нет, выполню, когда появится");
                }
                return false;
            }
            applyingJob = true;
            try { foreach (var o in found) o.Complete(); }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] цель {jobId}: {e.Message}"); }
            finally { applyingJob = false; }
            Plugin.L.LogInfo($"[JOB] задание {jobId}: цель '{name}' выполнена (её выполнил игрок {pid}){(retry ? " - дождался её у себя" : "")}");
            return true;
        }

        internal static void OnJobInfoAcquired(SideJob j, string msg)
        {
            Plugin.L.LogInfo($"[JOB] задание {j.jobID}: заказчик выслушан" + (applyingJob ? " (от другого игрока)" : ", сообщаю другим"));
            if (applyingJob || manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)7); w.Put(j.jobID); w.Put(msg ?? "");
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static readonly System.Collections.Generic.Dictionary<int, (string msg, int pid, float at)> pendingJobInfo = new System.Collections.Generic.Dictionary<int, (string, int, float)>();

        static void QueueJobInfo(int jobId, string msg, int pid)
        {
            if (!pendingJobInfo.ContainsKey(jobId)) Plugin.L.LogInfo($"[JOB] задание {jobId}: сведения от игрока {pid} - дам, если сам не выслушаю заказчика");
            pendingJobInfo[jobId] = (msg, pid, Time.realtimeSinceStartup + (PlayerOnPhone() ? 10f : 1.5f));
        }

        static bool PlayerOnPhone()
        {
            try
            {
                var tc = TelephoneController.Instance;
                if (tc != null && tc.activeCalls != null)
                    foreach (var c in tc.activeCalls) if (c != null && ((c.callerNS != null && c.callerNS.isPlayer) || (c.recevierNS != null && c.recevierNS.isPlayer))) return true;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return false;
        }

        static void PendingJobInfoTick()
        {
            if (pendingJobInfo.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (var id in new System.Collections.Generic.List<int>(pendingJobInfo.Keys))
            {
                var p = pendingJobInfo[id];
                if (now < p.at) continue;
                var j = JobById(id);
                if (j == null || j.knowHandInLocation) { pendingJobInfo.Remove(id); continue; }
                if (PlayerOnPhone()) { pendingJobInfo[id] = (p.msg, p.pid, now + 3f); continue; }
                if (!IsHost && j.accepted)
                {
                    try { j.GameWorldLoop(); } catch (Exception __e) { Err.Swallow(__e); }
                    if (j.knowHandInLocation) { pendingJobInfo.Remove(id); Plugin.L.LogInfo($"[JOB] задание {id}: заказчика выслушал сам, чужие сведения не нужны"); continue; }
                }
                pendingJobInfo.Remove(id);
                AcquireJobInfoHere(j, p.msg, p.pid > 0 ? "player " + p.pid + " called the client" : "хост выслушал заказчика");
                if (j.knowHandInLocation) Toast($"{PlayerLabel(p.pid)} called the client: the job details are on your case board.");
            }
        }

        static void AcquireJobInfoHere(SideJob j, string msg, string why)
        {
            if (j == null || j.knowHandInLocation) return;
            if (!j.accepted || j.thisCase == null) { Plugin.L.LogWarning($"[JOB] задание {j.jobID}: заказчика выслушали ({why}), но у меня оно не взято"); return; }
            applyingJob = true;
            try { j.OnAcquireJobInfo(msg ?? ""); Plugin.L.LogInfo($"[JOB] задание {j.jobID}: улики заказчика выданы ({why})"); }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] улики заказчика {j.jobID}: {e}"); }
            finally { applyingJob = false; }
        }

        static void UpdateJob(SideJob cur, SideJob nj)
        {
            if (nj.postID != cur.postID)
            {
                RemoveOwnPost(cur, nj.postID);
                cur.postID = nj.postID;
                try { Interactable p; if (nj.postID > 0 && CityData.Instance.savableInteractableDictionary.TryGetValue(nj.postID, out p)) cur.post = p; } catch (Exception __e) { Err.Swallow(__e); }
            }
            if (nj.knowHandInLocation && !cur.knowHandInLocation) QueueJobInfo(cur.jobID, nj.jobInfoDialogMsg, 0);
            if (MirrorJobs && cur.accepted && cur.state != SideJob.JobState.ended)
            {
                if (cur.phase != nj.phase) Plugin.L.LogInfo($"[JOB] задание {cur.jobID}: этап {cur.phase} -> {nj.phase}, как у хоста");
                cur.phase = nj.phase; cur.phaseChange = nj.phase;
            }
            else if (!IsHost && nj.knowHandInLocation) CatchUpPhase(cur, nj.phase, "хоста");
            try { SyncJobDialogs(cur, nj); } catch (Exception e) { Plugin.L.LogWarning($"[JOB] фразы задания {cur.jobID}: {e.Message}"); }
            if (nj.state != cur.state)
            {
                applyingJob = true;
                try
                {
                    if (nj.state == SideJob.JobState.ended) cur.End();
                    else cur.SetJobState(nj.state, true);
                }
                catch (Exception e) { Plugin.L.LogWarning($"[JOB] состояние задания {cur.jobID}: {e.Message}"); }
                finally { applyingJob = false; }
                Plugin.L.LogInfo($"[JOB] задание {cur.jobID}: {nj.state}");
            }
        }

        static void SyncJobDialogs(SideJob cur, SideJob nj)
        {
            if (IsHost || cur == null || nj == null || nj.dialog == null || cur.state == SideJob.JobState.ended) return;
            foreach (var d in nj.dialog) AddJobDialogHere(cur, d, "как у хоста");
        }

        static void AddJobDialogHere(SideJob cur, SideJob.AddedDialog d, string why)
        {
            if (cur == null || d == null || string.IsNullOrEmpty(d.dialogRef) || cur.state == SideJob.JobState.ended) return;
            if (cur.dialog != null)
                foreach (var m in cur.dialog) if (m != null && m.humanID == d.humanID && m.dialogRef == d.dialogRef && m.roomID == d.roomID) return;
            var h = d.GetHuman(); var pr = d.GetDialog();
            if (h == null || pr == null) { Plugin.L.LogWarning($"[JOB] задание {cur.jobID}: фраза '{d.dialogRef}' жителю {d.humanID} - не нашёл {(h == null ? "жителя" : "фразу")}"); return; }
            NewRoom room = null; try { room = d.GetRoom(); } catch (Exception __e) { Err.Swallow(__e); }
            applyingJob = true;
            try { cur.AddDialogOption(h, d.key, pr, room); }
            finally { applyingJob = false; }
            Plugin.L.LogInfo($"[JOB] задание {cur.jobID}: фраза '{d.dialogRef}' жителю {d.humanID} - {why}");
        }

        internal static void OnJobDialogAdded(SideJob j, Human person, Evidence.DataKey key, DialogPreset pr, NewRoom room)
        {
            if (IsHost || applyingJob || j == null || person == null || pr == null || manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)23); w.Put(j.jobID); w.Put(person.humanID); w.Put(pr.name ?? ""); w.Put(room != null ? room.roomID : -1); w.Put((int)key);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] задание {j.jobID}: моя игра дала фразу '{pr.name}' жителю {person.humanID} - сообщаю хосту");
        }

        static void CatchUpPhase(SideJob j, int phase, string whose)
        {
            if (j == null || !j.accepted || j.state == SideJob.JobState.ended || !j.knowHandInLocation) return;
            if (phase <= j.phase) return;
            Plugin.L.LogInfo($"[JOB] задание {j.jobID}: этап у {whose} {phase}, у меня {j.phase} - догоняю");
            j.phase = phase;
        }

        static readonly System.Collections.Generic.Dictionary<int, (int phase, float at)> phaseSent = new System.Collections.Generic.Dictionary<int, (int, float)>();
        static float phaseTickAt;

        static void JobPhaseReportTick()
        {
            if (IsHost || MirrorJobs || Time.realtimeSinceStartup < phaseTickAt) return;
            phaseTickAt = Time.realtimeSinceStartup + 1f;
            var ctl = SideJobController.Instance;
            if (ctl == null || ctl.allJobsDictionary == null) return;
            foreach (var kv in ctl.allJobsDictionary)
            {
                var j = kv.Value;
                if (j == null || !j.accepted || j.state == SideJob.JobState.ended || !j.knowHandInLocation) continue;
                (int phase, float at) last;
                bool known = phaseSent.TryGetValue(j.jobID, out last);
                if (known && last.phase == j.phase && Time.realtimeSinceStartup - last.at < 5f) continue;
                phaseSent[j.jobID] = (j.phase, Time.realtimeSinceStartup);
                var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)22); w.Put(j.jobID); w.Put(j.phase);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
        }

        static void AcceptJobHere(SideJob j, string why)
        {
            if (j == null || j.accepted) return;
            applyingJob = true;
            try { j.AcceptJob(); Toast("Job taken (" + why + "): it is on your case board."); Plugin.L.LogInfo($"[JOB] задание {j.jobID} взято у меня: {why}"); }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] взять задание {j.jobID}: {e.Message}"); }
            finally { applyingJob = false; }
            RemoveJobPostHere(j);
        }

        static void RemoveOwnPost(SideJob cur, int hostPostId)
        {
            try
            {
                int id = cur.postID;
                if (id <= 0 || id == hostPostId) return;
                var ctl = SideJobController.Instance;
                if (ctl != null && ctl.allJobsDictionary != null)
                    foreach (var kv in ctl.allJobsDictionary) if (kv.Value != null && kv.Value.jobID != cur.jobID && kv.Value.postID == id) return;
                Interactable post = null;
                CityData.Instance.savableInteractableDictionary.TryGetValue(id, out post);
                if (post == null || post.inInventory != null) return;
                applyingRemote = true;
                try { post.SafeDelete(false); } finally { applyingRemote = false; }
                if (cur.post != null && cur.post.Pointer == post.Pointer) cur.post = null;
                Plugin.L.LogInfo($"[JOB] задание {cur.jobID}: моё объявление {id} убрано (у хоста {(hostPostId > 0 ? hostPostId.ToString() : "не вывешено")})");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] своё объявление {cur.jobID}: {e.Message}"); }
        }

        static void RemoveJobPostHere(SideJob j)
        {
            try
            {
                Interactable post = j.post;
                if (post == null && j.postID > 0) CityData.Instance.savableInteractableDictionary.TryGetValue(j.postID, out post);
                if (post == null || post.inInventory != null) return;
                applyingRemote = true;
                try { post.SafeDelete(false); } finally { applyingRemote = false; }
                Plugin.L.LogInfo($"[JOB] объявление задания {j.jobID} убрано с доски (его взял другой игрок)");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] убрать объявление {j.jobID}: {e.Message}"); }
        }

        internal static void OnJobAccepted(SideJob j)
        {
            if (applyingJob || j == null || manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return;
            if (!jobAcceptSent.Add(j.jobID)) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)3); w.Put(j.jobID);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] я взял задание {j.jobID}, сообщаю другим");
        }

        internal static void OnCaseResolved(Case c, double moneyBefore)
        {
            if (applyingJob || c == null || manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return;
            int jobId = c.jobReference;
            bool murder = c.caseType == Case.CaseType.murder;
            if (jobId <= 0 && !murder) return;
            int got = (int)Math.Round(GcGet("money") - moneyBefore);
            if (got <= 0 && jobId > 0) { var jj = JobById(jobId); if (jj != null && jj.reward > 0) got = jj.reward; }
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)4); w.Put(murder ? -1 : jobId); w.Put(Math.Max(0, got));
            PutCaseAnswers(w, c);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] я сдал {(murder ? "дело об убийстве '" + c.name + "'" : "задание " + jobId)}, награда {got} - всем игрокам");
        }

        static void PutCaseAnswers(NetDataWriter w, Case c)
        {
            var qs = c.resolveQuestions;
            int n = qs != null ? qs.Count : 0;
            w.Put(n);
            for (int i = 0; i < n; i++)
            {
                var q = qs[i];
                w.Put(q != null ? q.name ?? "" : ""); w.Put(q != null ? q.input ?? "" : ""); w.Put(q != null ? q.inputtedEvidence ?? "" : "");
                w.Put(q != null && q.isValid); w.Put(q != null && q.isCorrect); w.Put(q != null ? q.progress : 0f);
                w.Put(q != null ? q.reward : 0); w.Put(q != null ? q.penalty : 0);
            }
            var sd = c.suspectsDetained;
            w.Put(sd != null ? sd.Count : 0);
            if (sd != null) foreach (var x in sd) w.Put(x);
        }

        sealed class CaseAnswer { public string name, input, ev; public bool valid, correct; public float progress; public int reward, penalty; }

        static Case FindCase(int jobId)
        {
            var cp = CasePanelController.Instance;
            if (cp == null || cp.activeCases == null) return null;
            foreach (var c in cp.activeCases)
            {
                if (c == null || c.caseStatus == Case.CaseStatus.submitted || c.caseStatus == Case.CaseStatus.archived) continue;
                if (jobId > 0 ? c.jobReference == jobId : c.caseType == Case.CaseType.murder) return c;
            }
            return null;
        }

        static bool MyJobCaseHandedIn(int jobId, out Case.CaseStatus st)
        {
            st = Case.CaseStatus.handInNotCollected;
            var cp = CasePanelController.Instance;
            if (cp == null || cp.activeCases == null) return false;
            foreach (var c in cp.activeCases)
            {
                if (c == null || c.jobReference != jobId) continue;
                st = c.caseStatus;
                if (st == Case.CaseStatus.submitted || st == Case.CaseStatus.closable || st == Case.CaseStatus.archived) return true;
            }
            return false;
        }

        static bool ResolveLikeOther(int jobId, NetDataReader r)
        {
            if (r.AvailableBytes < 4) return false;
            var ans = new System.Collections.Generic.List<CaseAnswer>();
            int n = r.GetInt();
            for (int i = 0; i < n; i++)
                ans.Add(new CaseAnswer { name = r.GetString(), input = r.GetString(), ev = r.GetString(), valid = r.GetBool(), correct = r.GetBool(), progress = r.GetFloat(), reward = r.GetInt(), penalty = r.GetInt() });
            var det = new System.Collections.Generic.List<int>();
            int nd = r.GetInt(); for (int i = 0; i < nd; i++) det.Add(r.GetInt());
            var c = FindCase(jobId);
            if (c == null) return false;
            var qs = c.resolveQuestions;
            for (int i = 0; qs != null && i < qs.Count; i++)
            {
                var q = qs[i]; if (q == null) continue;
                CaseAnswer a = i < ans.Count && ans[i].name == (q.name ?? "") ? ans[i] : null;
                if (a == null) foreach (var x in ans) if (x.name == (q.name ?? "")) { a = x; break; }
                if (a == null) continue;
                q.input = a.input; q.inputtedEvidence = a.ev; q.isValid = a.valid; q.isCorrect = a.correct; q.progress = a.progress; q.reward = a.reward; q.penalty = a.penalty;
            }
            if (c.suspectsDetained == null) c.suspectsDetained = new Il2CppSystem.Collections.Generic.List<int>();
            foreach (var x in det) if (!c.suspectsDetained.Contains(x)) c.suspectsDetained.Add(x);
            applyingJob = true;
            var rsw = System.Diagnostics.Stopwatch.StartNew();
            try { c.Resolve(); Plugin.L.LogInfo($"[LAG] Case.Resolve: {rsw.ElapsedMilliseconds} мс"); }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] сдать дело '{c.name}': {e.Message}"); }
            finally { applyingJob = false; }
            Plugin.L.LogInfo($"[JOB] дело '{c.name}' сдано у меня с ответами другого игрока (вопросов {ans.Count}, задержанных {det.Count})");
            return true;
        }

        internal static void OnCaseStatus(Case c, Case.CaseStatus st)
        {
            if (c != null) Plugin.L.LogInfo($"[JOB] дело '{c.name}' ({c.caseType}, задание {c.jobReference}): статус {st}{(applyingJob ? " (от другого игрока)" : "")}");
            if (c != null && !applyingJob && st == Case.CaseStatus.submitted) localSubmitted.Add(CaseKey(c));
            if (applyingJob || c == null || st != Case.CaseStatus.handInCollected || manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return;
            int jobId = c.jobReference;
            if (jobId <= 0 && c.caseType != Case.CaseType.murder) return;
            if (c.caseType == Case.CaseType.murder) jobId = -1;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)18); w.Put(jobId);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] я взял бланк дела '{c.name}' - всем игрокам");
        }

        static void OnCaseStatusMsg(RP rp, NetDataReader r)
        {
            int jobId = r.GetInt();
            var c = FindCase(jobId);
            if (c == null) { Plugin.L.LogWarning($"[JOB] бланк от игрока {(rp != null ? rp.pid : 0)}: у меня нет дела {(jobId > 0 ? "задания " + jobId : "об убийстве")}"); return; }
            if (c.caseStatus == Case.CaseStatus.handInCollected) return;
            applyingJob = true;
            try { c.SetStatus(Case.CaseStatus.handInCollected, false); try { CasePanelController.Instance.UpdateCloseCaseButton(); } catch (Exception __e) { Err.Swallow(__e); } }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] бланк: " + e.Message); }
            finally { applyingJob = false; }
            Toast($"{PlayerLabel(rp != null ? rp.pid : 0)} took the hand-in form for '{c.name}': you have it too.");
            Plugin.L.LogInfo($"[JOB] бланк дела '{c.name}' взят другим игроком - у меня тоже");
        }

        static bool applyingRestrain;

        internal static void OnRestrained(NewAIController ai, bool val, float duration)
        {
            if (applyingRestrain || IsHost || ai == null || manager == null || manager.ConnectedPeersCount == 0 || RtMode != NetMode.Client) return;
            var h = ai.human; if (h == null) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)19); w.Put(h.humanID); w.Put(val); w.Put(duration);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[NPC] горожанин {h.humanID}: я {(val ? "надел" : "снял")} наручники - сообщаю хосту");
        }

        static readonly System.Collections.Generic.HashSet<int> gooseDone = new System.Collections.Generic.HashSet<int>();

        internal static bool OnGooseChaseSuccess(SideJob j)
        {
            if (j == null || applyingJob || manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return true;
            if (IsHost)
            {
                if (!gooseDone.Add(j.jobID)) { Plugin.L.LogInfo($"[JOB] задание {j.jobID}: ответил на звонок, но кейс уже создан по ответу другого игрока"); return false; }
                Plugin.L.LogInfo($"[JOB] задание {j.jobID}: я ответил на звонок - создаю кейс с данными");
                return true;
            }
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)20); w.Put(j.jobID);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] задание {j.jobID}: я ответил на звонок - хост тоже создаст кейс, лишний у меня уберётся");
            return true;
        }

        static void OnRestrainMsg(NetDataReader r)
        {
            int id = r.GetInt(); bool val = r.GetBool(); float dur = r.GetFloat();
            Human h; if (!CityData.Instance.GetHuman(id, out h, false) || h == null || h.ai == null) return;
            if (h.ai.restrained == val) return;
            applyingRestrain = true;
            try { h.ai.SetRestrained(val, dur); Plugin.L.LogInfo($"[NPC] горожанин {id}: наручники {(val ? "надеты" : "сняты")} другим игроком"); }
            catch (Exception e) { Plugin.L.LogWarning("[NPC] наручники: " + e.Message); }
            finally { applyingRestrain = false; }
        }

        static readonly System.Collections.Generic.List<(bool murder, RP rp, byte[] data)> worldMsgQueue = new System.Collections.Generic.List<(bool, RP, byte[])>();

        static bool WorldReady()
        {
            if (IsHost) return true;
            try { return !xferMode && !InMainMenu() && Player.Instance != null && CityData.Instance != null && SideJobController.Instance != null && MurderController.Instance != null; } catch (Exception) { return false; }
        }

        static void WorldMsgQueueTick()
        {
            if (worldMsgQueue.Count == 0 || !WorldReady()) return;
            var list = new System.Collections.Generic.List<(bool murder, RP rp, byte[] data)>(worldMsgQueue);
            worldMsgQueue.Clear();
            Plugin.L.LogInfo($"[JOB] город загружен - разбираю отложенные сообщения: {list.Count}");
            foreach (var x in list)
            {
                try
                {
                    var rd = new NetDataReader(x.data);
                    if (x.murder) OnMurderMsg(rd); else OnJobMsg(x.rp, rd);
                }
                catch (Exception e) { Plugin.L.LogWarning("[JOB] отложенное сообщение: " + e.Message); }
            }
        }

        static void OnJobMsg(RP rp, NetDataReader r)
        {
            if (!WorldReady()) { worldMsgQueue.Add((false, rp, r.GetRemainingBytes())); return; }
            byte op = r.GetByte();
            if (!Plugin.CfgJobSync.Value) return;
            if (op == 1 && !IsHost)
            {
                int id = r.GetInt(); var bytes = r.GetBytesWithLength();
                try { ImportJob(id, bytes); } catch (Exception e) { Plugin.L.LogWarning($"[JOB] загрузка задания {id}: {e}"); }
            }
            else if (op == 2 && !IsHost)
            {
                int id = r.GetInt(); var j = JobById(id);
                if (j != null && j.state != SideJob.JobState.ended)
                {
                    applyingJob = true;
                    try { j.End(); } catch (Exception e) { Plugin.L.LogWarning($"[JOB] закончить задание {id}: {e.Message}"); }
                    finally { applyingJob = false; }
                }
            }
            else if (op == 3)
            {
                int id = r.GetInt(); var j = JobById(id);
                if (j == null) { Plugin.L.LogWarning($"[JOB] задание {id} (взял игрок {(rp != null ? rp.pid : -1)}) у меня не найдено"); return; }
                jobAcceptSent.Add(id);
                AcceptJobHere(j, "player " + (rp != null ? rp.pid : 0) + " took it");
            }
            else if (op == 18) { OnCaseStatusMsg(rp, r); }
            else if (op == 19 && IsHost) { OnRestrainMsg(r); }
            else if (op == 22 && IsHost)
            {
                int id = r.GetInt(), phase = r.GetInt();
                CatchUpPhase(JobById(id), phase, "игрока " + (rp != null ? rp.pid : 0));
            }
            else if (op == 23 && IsHost)
            {
                int id = r.GetInt(); var j = JobById(id);
                var d = new SideJob.AddedDialog();
                d.humanID = r.GetInt(); d.dialogRef = r.GetString(); d.roomID = r.GetInt(); d.key = (Evidence.DataKey)r.GetInt();
                if (j != null) AddJobDialogHere(j, d, "от игрока " + (rp != null ? rp.pid : 0));
            }
            else if (op == 24 && !IsHost)
            {
                OnJobCallMsg(r);
            }
            else if (op == 26 && !IsHost)
            {
                OnJobGuestPassMsg(r);
            }
            else if (op == 21)
            {
                int adId = r.GetInt(), job = r.GetInt();
                NewAddress ad = null;
                CityData.Instance.addressDictionary.TryGetValue(adId, out ad);
                var me = Player.Instance;
                if (ad == null || me == null) { Plugin.L.LogWarning($"[JOB] ключи от адреса {adId} (задание {job}): адрес не найден"); return; }
                applyingKey = true;
                try { me.AddToKeyring(ad, true); }
                finally { applyingKey = false; }
                Plugin.L.LogInfo($"[JOB] задание {job}: игрок {(rp != null ? rp.pid : 0)} получил ключи от {ad.name} - у меня они теперь тоже");
            }
            else if (op == 20 && IsHost)
            {
                int id = r.GetInt(); var j = JobById(id);
                if (j == null || !j.accepted) { Plugin.L.LogWarning($"[JOB] задание {id}: игрок {(rp != null ? rp.pid : 0)} ответил на звонок, но у меня задание не взято"); return; }
                if (!gooseDone.Add(id)) { Plugin.L.LogInfo($"[JOB] задание {id}: игрок {(rp != null ? rp.pid : 0)} ответил на звонок - кейс уже создан"); return; }
                applyingJob = true;
                try { j.OnGooseChaseSuccess(); Plugin.L.LogInfo($"[JOB] задание {id}: игрок {(rp != null ? rp.pid : 0)} ответил на звонок - создал кейс с данными, он уйдёт всем"); }
                catch (Exception e) { Plugin.L.LogWarning($"[JOB] кейс задания {id}: {e.Message}"); }
                finally { applyingJob = false; }
            }
            else if (op == 4)
            {
                int id = r.GetInt(), money = r.GetInt();
                Case.CaseStatus myStatus;
                if (id > 0 && MyJobCaseHandedIn(id, out myStatus))
                {
                    Plugin.L.LogInfo($"[JOB] задание {id} сдано игроком {(rp != null ? rp.pid : 0)}, у меня оно уже сдано само ({myStatus}) - награду второй раз не даю");
                    return;
                }
                bool resolved = false;
                try { resolved = ResolveLikeOther(id, r); } catch (Exception e) { Plugin.L.LogWarning("[JOB] ответы дела: " + e.Message); }
                if (resolved || id <= 0)
                {
                    if (!resolved && id <= 0)
                    {
                        applyingJob = true;
                        try { if (money > 0 && !RewardRuleActive) GameplayController.Instance.AddMoney(money, true, "case reward"); } catch (Exception e) { Plugin.L.LogWarning("[JOB] награда: " + e.Message); }
                        finally { applyingJob = false; }
                    }
                    Toast($"{PlayerLabel(rp != null ? rp.pid : 0)} solved the case" + (money > 0 ? $": you get the reward too." : "."));
                    Plugin.L.LogInfo($"[JOB] дело {(id > 0 ? "задания " + id : "об убийстве")} сдано игроком {(rp != null ? rp.pid : 0)}, награда {money}, у меня сдано своё дело: {resolved}");
                    return;
                }
                var j = JobById(id);
                bool mine = j != null && j.accepted;
                if (mine && money > 0 && !RewardRuleActive)
                {
                    applyingJob = true;
                    try { GameplayController.Instance.AddMoney(money, true, "case reward"); } catch (Exception e) { Plugin.L.LogWarning("[JOB] награда: " + e.Message); }
                    finally { applyingJob = false; }
                }
                if (j != null && j.state != SideJob.JobState.ended)
                {
                    applyingJob = true;
                    try { j.End(); } catch (Exception e) { Plugin.L.LogWarning($"[JOB] закончить задание {id}: {e.Message}"); }
                    finally { applyingJob = false; }
                }
                Toast($"{PlayerLabel(rp != null ? rp.pid : 0)} completed the job" + (mine && money > 0 ? $": you get {money} too." : "."));
                Plugin.L.LogInfo($"[JOB] задание {id} сдано игроком {(rp != null ? rp.pid : 0)}, награда {money}, мне начислено: {mine && money > 0}");
            }
            else if (op == 5)
            {
                OnEvidenceShared(rp, r);
            }
            else if (op == 14) { OnComputerMsg(r, rp != null ? rp.pid : 0); }
            else if (op == 15) { OnPageMsg(r); }
            else if (op == 16 && !IsHost) { OnAlarmMsg(r); }
            else if (op == 17 && !IsHost) { OnAlarmSnapshot(r); }
            else if (op == 12 && !IsHost)
            {
                int n = r.GetInt();
                for (int i = 0; i < n; i++) ApplyPrintLetter(r.GetInt(), r.GetInt());
            }
            else if (op == 13 && IsHost)
            {
                int hid = r.GetInt();
                Human h; if (CityData.Instance.GetHuman(hid, out h, false) && h != null && h.fingerprintLoop < 0) AssignPrintLetter(h);
            }
            else if (op == 11)
            {
                int id = r.GetInt();
                ApplyJobChoices(r, JobById(id), rp != null ? rp.pid : 0);
            }
            else if (op == 10)
            {
                AddObjectiveHere(rp, r);
            }
            else if (op == 9)
            {
                int id = r.GetInt(); string name = r.GetString();
                ApplyJobChoices(r, JobById(id), rp != null ? rp.pid : 0);
                CompleteObjectiveHere(id, name, rp != null ? rp.pid : 0);
            }
            else if (op == 7)
            {
                int id = r.GetInt(); string msg = r.GetString();
                var j = JobById(id);
                if (j == null) { Plugin.L.LogWarning($"[JOB] задание {id} (заказчика выслушал игрок {(rp != null ? rp.pid : -1)}) у меня не найдено"); return; }
                if (j.knowHandInLocation) return;
                QueueJobInfo(j.jobID, msg, rp != null ? rp.pid : 0);
            }
            else if (op == 6 && !IsHost)
            {
                int n = r.GetInt(); var alive = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < n; i++) alive.Add(r.GetInt());
                var ctl = SideJobController.Instance; if (ctl == null || ctl.allJobsDictionary == null) return;
                var gone = new System.Collections.Generic.List<SideJob>();
                foreach (var kv in ctl.allJobsDictionary) if (kv.Value != null && !alive.Contains(kv.Value.jobID) && kv.Value.state != SideJob.JobState.ended) gone.Add(kv.Value);
                foreach (var j in gone)
                {
                    applyingJob = true;
                    try { j.End(); } catch (Exception __e) { Err.Swallow(__e); }
                    finally { applyingJob = false; }
                }
                Plugin.L.LogInfo($"[JOB] заданий у хоста: {n}; закончено у меня лишних (из старого сейва): {gone.Count}");
            }
        }

        static KeyCode ShareKey()
        {
            string s = Plugin.CfgShareEvidenceKey.Value ?? "F6";
            if (s != shareKeyName) { shareKeyName = s; KeyCode k; shareKey = Enum.TryParse<KeyCode>(s, true, out k) ? k : KeyCode.F6; }
            return shareKey;
        }

        static void EvidenceShareTick()
        {
            if (manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgJobSync.Value) return;
            if (string.IsNullOrWhiteSpace(Plugin.CfgShareEvidenceKey.Value)) return;
            bool down = false;
            try { down = KeyDown(ShareKey()); } catch (Exception __e) { Err.Swallow(__e); }
            if (!down || Time.realtimeSinceStartup < jobShareKeyAt) return;
            jobShareKeyAt = Time.realtimeSinceStartup + 0.5f;
            var cp0 = CasePanelController.Instance;
            SharePinned(cp0 != null ? cp0.selectedPinned : null);
        }

        const string ShareCmd = "SodCoopShare";

        internal static void AddShareMenuItem(PinnedItemController pin)
        {
            if (pin == null || !Plugin.CfgJobSync.Value || RtMode == NetMode.Off) return;
            var cm = pin.contextMenu;
            if (cm == null) return;
            if (cm.menuButtons == null) cm.menuButtons = new Il2CppSystem.Collections.Generic.List<ContextMenuController.ContextMenuButtonSetup>();
            foreach (var b in cm.menuButtons) if (b != null && b.commandString == ShareCmd) return;
            var s = new ContextMenuController.ContextMenuButtonSetup();
            s.commandString = ShareCmd; s.useText = true; s.overrideText = "Share with players";
            cm.menuButtons.Add(s);
        }

        internal static bool HandleShareCommand(ContextMenuController cm, ContextButtonController button)
        {
            var st = button != null ? button.setup : null;
            if (st == null || st.commandString != ShareCmd) return false;
            PinnedItemController pin = null;
            try { pin = cm != null && cm.commandObject != null ? cm.commandObject.TryCast<PinnedItemController>() : null; } catch (Exception __e) { Err.Swallow(__e); }
            if (pin == null) { try { var cp = CasePanelController.Instance; if (cp != null) pin = cp.selectedPinned; } catch (Exception __e) { Err.Swallow(__e); } }
            Plugin.L.LogInfo($"[JOB] нажато «Share with players»: улика {(pin != null && pin.caseElement != null ? pin.caseElement.id : "не найдена")}");
            SharePinned(pin);
            try { if (cm != null) cm.ForceClose(); } catch (Exception __e) { Err.Swallow(__e); }
            return true;
        }

        static void SharePinned(PinnedItemController sel)
        {
            if (manager == null || manager.ConnectedPeersCount == 0) { Toast("No other players to share with."); return; }
            var cp = CasePanelController.Instance;
            var el = sel != null ? sel.caseElement : null;
            if (el == null || string.IsNullOrEmpty(el.id)) { Toast("Select an item on your case board to share it."); return; }
            int jobRef = 0;
            try { var ac = cp.activeCase; if (ac != null) jobRef = ac.jobReference; } catch (Exception __e) { Err.Swallow(__e); }
            var ks = new System.Collections.Generic.List<int>();
            if (el.dk != null) for (int i = 0; i < el.dk.Count; i++) ks.Add((int)el.dk[i]);
            Evidence sev = null;
            try { Toolbox.Instance.TryGetEvidence(el.id, out sev); } catch (Exception __e) { Err.Swallow(__e); }
            SendEvidence(sev, el.id, ks, jobRef, false);
            string nm = el.n ?? el.id;
            Toast("Shared with the other players: " + nm);
            Plugin.L.LogInfo($"[JOB] улика '{el.id}' отправлена другим игрокам (дело задания {jobRef})");
        }

        static void SendEvidence(Evidence sev, string evId, System.Collections.Generic.List<int> keys, int jobRef, bool auto)
        {
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)5); w.Put(evId); w.Put(jobRef);
            w.Put(keys.Count);
            foreach (var k in keys) w.Put(k);
            w.Put(EvidenceSig(sev));
            byte[] ib = null;
            try
            {
                string js = EvidenceItemJson(sev, evId);
                if (js != null) ib = System.Text.Encoding.UTF8.GetBytes(js);
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] предмет улики: " + e.Message); }
            w.Put(ib != null); if (ib != null) w.PutBytesWithLength(ib);
            w.Put(auto);
            w.Put(CaptureCameraId(sev));
            w.Put(CapturePose(sev));
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        internal static string EvidenceItemJson(Evidence sev, string evId)
        {
            var sit = sev != null ? sev.interactable : null;
            if (sit == null) return null;
            var d = new StateSaveData(); d.interactables = new Il2CppSystem.Collections.Generic.List<Interactable>(); d.interactables.Add(sit);
            var es = sev.TryCast<EvidenceSurveillance>();
            var oldSCap = sit.sCap;
            bool addCap = es != null && es.savedCapture != null && (sit.sCap == null || sit.sCap.Count == 0) && (sit.cap == null || sit.cap.Count == 0);
            if (addCap)
            {
                sit.sCap = new Il2CppSystem.Collections.Generic.List<SceneRecorder.SceneCapture>();
                sit.sCap.Add(es.savedCapture);
            }
            try { return JsonUtility.ToJson(d); }
            finally
            {
                if (addCap) { sit.sCap = oldSCap; Plugin.L.LogInfo($"[JOB] улика '{evId}': кадр камеры {es.savedCapture.id} хранится только в улике - кладу его в копию предмета"); }
            }
        }

        static string EvidenceSig(Evidence ev)
        {
            if (ev == null) return "";
            try
            {
                var it = ev.interactable;
                string kind = it != null ? PresetNameOf(it) : (ev.preset != null ? ev.preset.name : "");
                return kind + "|" + (ev.belongsTo != null ? ev.belongsTo.humanID : -1) + "|" + (ev.writer != null ? ev.writer.humanID : -1) + "|" + (ev.reciever != null ? ev.reciever.humanID : -1) + CaptureSig(ev);
            }
            catch (Exception) { return ""; }
        }

        static SceneRecorder.SceneCapture EvidenceCapture(Evidence ev)
        {
            if (ev == null) return null;
            var es = ev.TryCast<EvidenceSurveillance>();
            var it = ev.interactable;
            SceneRecorder.SceneCapture cap = es != null ? es.savedCapture : null;
            if (cap == null && it != null && it.sCap != null && it.sCap.Count > 0) cap = it.sCap[0];
            if (cap == null && it != null && it.cap != null && it.cap.Count > 0) cap = it.cap[0];
            return cap;
        }

        static string CaptureSig(Evidence ev)
        {
            try
            {
                var cap = EvidenceCapture(ev);
                if (cap == null) return "";
                return "|" + cap.t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception) { return ""; }
        }

        internal static int CaptureCameraId(Evidence ev, bool log = true)
        {
            try
            {
                var cap = EvidenceCapture(ev);
                if (cap == null) return 0;
                var rec = cap.recorder;
                string how = "в кадре";
                if (rec == null || rec.interactable == null)
                {
                    how = "через игру по номеру кадра";
                    var g = Toolbox.Instance.GetSceneCaptureFromID(cap.id);
                    rec = g != null ? g.recorder : null;
                }
                int id = rec != null && rec.interactable != null ? rec.interactable.id : 0;
                if (log) Plugin.L.LogInfo($"[JOB] кадр {cap.id} (время {cap.t:F3}): камера {(id != 0 ? id + ", найдена " + how : "не найдена")}");
                return id;
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] камера кадра: " + e.Message); return 0; }
        }

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        internal static string CapturePose(Evidence ev, bool log = true)
        {
            try
            {
                var cap = EvidenceCapture(ev);
                if (cap == null) return "";
                int nd = cap.drp != null ? cap.drp.Count : 0;
                var p = cap.GetCaptureWorldPosition(); var q = cap.GetCaptureWorldRotation();
                if (log) Plugin.L.LogInfo($"[JOB] кадр {cap.id}: точек камеры {nd}, камера {(cap.recorder != null ? "есть" : "нет")}, позиция {p}, поворот {q}");
                if (p == Vector3.zero) return "";
                return string.Join(",", new[] { p.x, p.y, p.z, q.x, q.y, q.z }.Select(f => f.ToString("R", Inv)));
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] позиция кадра: " + e.Message); return ""; }
        }

        static void ApplyCapturePose(SceneRecorder.SceneCapture cap, string pose, string evId)
        {
            if (cap == null) return;
            int nd = cap.drp != null ? cap.drp.Count : 0;
            string before = "?";
            try { before = cap.GetCaptureWorldPosition().ToString(); } catch (Exception __e) { Err.Swallow(__e); }
            if (nd == 0 && !string.IsNullOrEmpty(pose))
            {
                try
                {
                    var f = pose.Split(',').Select(x => float.Parse(x, Inv)).ToArray();
                    var d = new SceneRecorder.DynamicRecordPosition();
                    d.pos = new Vector3(f[0], f[1], f[2]); d.rot = new Vector3(f[3], f[4], f[5]);
                    if (cap.drp == null) cap.drp = new Il2CppSystem.Collections.Generic.List<SceneRecorder.DynamicRecordPosition>();
                    cap.drp.Add(d);
                }
                catch (Exception e) { Plugin.L.LogWarning($"[JOB] улика '{evId}': позиция кадра: {e.Message}"); }
            }
            string after = "?";
            try { after = cap.GetCaptureWorldPosition().ToString(); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[JOB] улика '{evId}': кадр {cap.id}, точек камеры {nd}, позиция была {before}, стала {after} (прислана: {(pose != "" ? pose : "нет")})");
        }

        static void LinkCaptureCamera(Interactable it, Evidence ev, int camId, string evId)
        {
            SceneRecorder rec = null;
            string src = "видеокамера " + camId;
            try { var cam = camId != 0 ? FindById(camId) : null; rec = cam != null ? cam.sceneRecorder : null; } catch (Exception __e) { Err.Swallow(__e); }
            if (rec == null) { src = "мой фотоаппарат"; try { rec = Player.Instance != null ? Player.Instance.sceneRecorder : null; } catch (Exception __e) { Err.Swallow(__e); } }
            int n = 0;
            var all = new System.Collections.Generic.List<SceneRecorder.SceneCapture>();
            try
            {
                var es = ev != null ? ev.TryCast<EvidenceSurveillance>() : null;
                if (es != null && es.savedCapture != null) all.Add(es.savedCapture);
                if (it.sCap != null) for (int i = 0; i < it.sCap.Count; i++) if (it.sCap[i] != null) all.Add(it.sCap[i]);
                if (it.cap != null) for (int i = 0; i < it.cap.Count; i++) if (it.cap[i] != null) all.Add(it.cap[i]);
                if (rec != null) foreach (var c in all) if (c.recorder == null) { c.recorder = rec; n++; }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] улика '{evId}': камера кадра: {e.Message}"); }
            Plugin.L.LogInfo($"[JOB] улика '{evId}': {(rec != null ? "привязал к кадрам (" + n + ") устройство: " + src : "не нашёл устройство для кадра, фото будет пустым")}");
        }

        static Evidence FindEvidenceBySig(string sig)
        {
            try
            {
                var list = CityData.Instance.interactableDirectory;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    var it = list[i]; if (it == null || it.evidence == null) continue;
                    if (EvidenceSig(it.evidence) == sig) return it.evidence;
                }
                foreach (var kv in CityData.Instance.savableInteractableDictionary)
                {
                    var it = kv.Value; if (it == null || it.evidence == null) continue;
                    if (EvidenceSig(it.evidence) == sig) return it.evidence;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] поиск улики: " + e.Message); }
            return null;
        }

        internal static Evidence ImportSharedItem(byte[] bytes, string evId, int camId, string pose)
        {
            try
            {
                var obj = JsonUtility.FromJson(System.Text.Encoding.UTF8.GetString(bytes), Il2CppInterop.Runtime.Il2CppType.Of<StateSaveData>());
                var data = obj != null ? obj.TryCast<StateSaveData>() : null;
                var it = data != null && data.interactables != null && data.interactables.Count > 0 ? data.interactables[0] : null;
                if (it == null) return null;
                it.id = FreeLocalItemId();
                it.inv = 0;
                int n, u;
                ImportItems(data.interactables, "[JOB]", "улики " + evId, out n, out u);
                try { it.RemoveFromPlacement(); } catch (Exception __e) { Err.Swallow(__e); }
                EnsureItemEvidence(it, evId);
                LinkCaptureCamera(it, it.evidence, camId, evId);
                if (it.sCap != null && it.sCap.Count > 0) ApplyCapturePose(it.sCap[0], pose, evId);
                Plugin.L.LogInfo($"[JOB] улика '{evId}': у меня её не было, создал копию предмета '{it.p}' (номер {it.id}, улика {(it.evidence != null ? it.evidence.evID : "нет")})");
                return it.evidence;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] копия улики '{evId}': {e.Message}"); return null; }
        }

        static void EnsureItemEvidence(Interactable it, string evId)
        {
            int sc = it.sCap != null ? it.sCap.Count : 0, cc = it.cap != null ? it.cap.Count : 0;
            if (it.evidence == null)
            {
                try
                {
                    it.evidence = Toolbox.Instance.GetOrCreateEvidenceForInteractable(it.preset, "I" + it.id, it, it.belongsTo, it.writer, it.reciever, it.jobParent, null, it.preset != null ? it.preset.retailItem : null, it.pv);
                    Plugin.L.LogInfo($"[JOB] улика '{evId}': игра не создала улику для копии, создал сам: {(it.evidence != null ? it.evidence.evID : "не вышло")}");
                }
                catch (Exception e) { Plugin.L.LogWarning($"[JOB] улика '{evId}': создать улику для копии: {e.Message}"); }
            }
            try
            {
                var es = it.evidence != null ? it.evidence.TryCast<EvidenceSurveillance>() : null;
                var cap = sc > 0 ? it.sCap[0] : (cc > 0 ? it.cap[0] : null);
                if (es != null && (cap != null || es.savedCapture == null))
                {
                    var was = es.savedCapture;
                    if (cap != null) { es.savedCapture = cap; es.captureID = cap.id; }
                    Plugin.L.LogInfo($"[JOB] улика '{evId}': снимок камеры {(cap != null ? "подставлен присланный (кадр " + cap.id + ")" : "не найден")}, у игры был {(was != null ? "кадр " + was.id + " (время " + was.t.ToString("F3", Inv) + ")" : "нет")}, кадров в предмете {sc}/{cc}");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[JOB] улика '{evId}': снимок: {e.Message}"); }
        }

        static void OnEvidenceShared(RP rp, NetDataReader r)
        {
            string evId = r.GetString(); int jobRef = r.GetInt(); int nk = r.GetInt();
            var keys = new Il2CppSystem.Collections.Generic.List<Evidence.DataKey>();
            for (int i = 0; i < nk; i++) keys.Add((Evidence.DataKey)r.GetInt());
            string sig = r.AvailableBytes > 0 ? r.GetString() : "";
            byte[] itemBytes = r.AvailableBytes > 0 && r.GetBool() ? r.GetBytesWithLength() : null;
            bool auto = r.AvailableBytes > 0 && r.GetBool();
            int camId = r.AvailableBytes >= 4 ? r.GetInt() : 0;
            string pose = r.AvailableBytes > 0 ? r.GetString() : "";
            int pid = rp != null ? rp.pid : 0;
            Evidence ev = null;
            bool found = false;
            try { found = Toolbox.Instance.TryGetEvidence(evId, out ev) && ev != null; } catch (Exception __e) { Err.Swallow(__e); }
            if (sig != "" && (!found || EvidenceSig(ev) != sig))
            {
                var alt = FindEvidenceBySig(sig);
                Plugin.L.LogInfo($"[JOB] улика '{evId}' от игрока {pid}: у меня под этим номером {(found ? "'" + EvidenceSig(ev) + "'" : "ничего")}, ищу '{sig}' - {(alt != null ? "нашёл " + alt.evID : "не нашёл")}");
                ev = alt; found = alt != null;
            }
            if (!found && itemBytes != null)
            {
                ev = ImportSharedItem(itemBytes, evId, camId, pose);
                found = ev != null;
            }
            if (!found) { Toast(UiKit.T($"{PlayerLabel(pid)} shared evidence you do not have yet.", $"{PlayerLabel(pid)} поделился уликой, которой у тебя ещё нет.")); Plugin.L.LogWarning($"[JOB] улика '{evId}' от игрока {pid} у меня не найдена"); return; }
            var cp = CasePanelController.Instance;
            if (cp == null) return;
            Case target = null;
            try
            {
                if (jobRef > 0 && cp.activeCases != null) foreach (var c in cp.activeCases) if (c != null && c.jobReference == jobRef) { target = c; break; }
                if (target == null) target = cp.activeCase;
                if (target == null) target = cp.CreateNewCase(Case.CaseType.custom, Case.CaseStatus.closable, true, "From player " + pid);
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] дело для улики: " + e.Message); }
            if (target == null) return;
            try
            {
                if (keys.Count == 0) keys.Add(Evidence.DataKey.name);
                if (auto && target.caseElements != null) foreach (var ce in target.caseElements) if (ce != null && ce.id == evId) return;
                cp.PinToCasePanel(target, ev, keys, true, Vector2.zero, false);
                if (!auto) Toast($"{PlayerLabel(pid)} shared evidence: it is pinned to your case '{target.name}'.");
                Plugin.L.LogInfo($"[JOB] улика '{evId}' от игрока {pid} закреплена в деле '{target.name}'");
            }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] закрепить улику: " + e.Message); }
        }

        static void JobTick()
        {
            JobLoopDepth = 0;
            try { RulesTick(); } catch (Exception e) { Plugin.L.LogWarning("[RULES] " + e.Message); }
            try { ModifiersTick(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] " + e.Message); }
            try { PermaTick(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] " + e.Message); }
            try { WatchTick(); } catch (Exception e) { Plugin.L.LogWarning("[WANTED] " + e.Message); }
            try { FameTick(); } catch (Exception e) { Plugin.L.LogWarning("[FAME] " + e.Message); }
            try { DebtTick(); } catch (Exception e) { Plugin.L.LogWarning("[DEBT] " + e.Message); }
            try { RatTick(); } catch (Exception e) { Plugin.L.LogWarning("[RAT] " + e.Message); }
            try { RatCollisionTick(); } catch (Exception e) { Plugin.L.LogWarning("[RAT] столкновения: " + e.Message); }
            try { WorldMsgQueueTick(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] очередь: " + e.Message); }
            try { LeverTick(); } catch (Exception e) { Plugin.L.LogWarning("[SWITCH] рычаги: " + e.Message); }
            long tj = Perf.T();
            if (IsHost && RtMode == NetMode.Host && Plugin.CfgJobSync.Value) { try { JobIndexStep(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] таблица предметов: " + e.Message); } }
            Perf.Add("таблица предметов заданий", tj);
            if (!Plugin.CfgJobSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
            if (IsHost)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                tj = Perf.T();
                JobHostTick();
                Perf.Add("задания (хост)", tj); tj = Perf.T();
                long t1 = sw.ElapsedMilliseconds;
                try { MurderHostTick(); } catch (Exception e) { if (!murderErrLogged) { murderErrLogged = true; Plugin.L.LogWarning("[MURDER] " + e); } }
                Perf.Add("убийства (хост)", tj); tj = Perf.T();
                long t2 = sw.ElapsedMilliseconds;
                try { AlarmSnapshotTick(); } catch (Exception e) { Plugin.L.LogWarning("[ALARM] " + e.Message); }
                Perf.Add("тревоги (хост)", tj);
                if (sw.ElapsedMilliseconds > 40) Plugin.L.LogWarning($"[LAG] хост: задания {t1} мс, убийства {t2 - t1} мс, тревоги {sw.ElapsedMilliseconds - t2} мс");
            }
            else if (Time.realtimeSinceStartup >= clientLoopAt)
            {
                clientLoopAt = Time.realtimeSinceStartup + 2f;
                try { ClientJobWorldLoop(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] " + e.Message); }
            }
            EvidenceShareTick();
            PendingJobInfoTick();
            try { PendingObjectivesTick(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] отложенные цели: " + e.Message); }
            try { PendingCompletesTick(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] отложенные выполнения целей: " + e.Message); }
            try { ObjectiveHudTick(); } catch (Exception e) { Plugin.L.LogWarning("[HUD] цели: " + e.Message); }
            try { JobPhaseReportTick(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] этап: " + e.Message); }
            if (Time.realtimeSinceStartup >= printAt) { printAt = Time.realtimeSinceStartup + 1f; try { PrintLettersTick(); } catch (Exception e) { Plugin.L.LogWarning("[PRINTS] " + e.Message); } }
            if (Time.realtimeSinceStartup >= jobChoiceAt) { jobChoiceAt = Time.realtimeSinceStartup + 1f; try { JobChoicesTick(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] выбор: " + e.Message); } }
        }

        internal static double MoneyNow() { return GcGet("money"); }
    }
}

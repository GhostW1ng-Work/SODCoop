using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        internal static int JobLoopDepth;
        static bool applyingJobCall, applyingGuestPass;
        static Dictionary<string, DialogPreset> dialogByName;

        internal static bool MirrorJobs
        {
            get { return !IsHost && RtMode == NetMode.Client && manager != null && serverPeer != null && Plugin.CfgJobSync.Value && Plugin.CfgJobHostAuthority.Value; }
        }

        internal static bool MirrorJob(SideJob j)
        {
            try { return j != null && j.accepted && j.state != SideJob.JobState.ended && MirrorJobs; } catch (Exception) { return false; }
        }

        static bool HostJobLoopSync
        {
            get { return IsHost && JobLoopDepth > 0 && manager != null && manager.ConnectedPeersCount > 0 && Plugin.CfgJobSync.Value && Plugin.CfgJobHostAuthority.Value; }
        }

        internal static void OnJobPin(Case c, Evidence ev, Il2CppSystem.Collections.Generic.List<Evidence.DataKey> keys)
        {
            if (!HostJobLoopSync || c == null || ev == null || c.jobReference <= 0 || string.IsNullOrEmpty(ev.evID)) return;
            var ks = new List<int>();
            if (keys != null) foreach (var k in keys) ks.Add((int)k);
            SendEvidence(ev, ev.evID, ks, c.jobReference, true);
            Plugin.L.LogInfo($"[JOB] задание {c.jobReference}: игра прикрепила улику '{ev.evID}' к делу - отправляю клиентам");
        }

        internal static void OnJobGuestPass(NewAddress loc, float hours)
        {
            if (!HostJobLoopSync || loc == null || applyingGuestPass) return;
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)26); w.Put(loc.id); w.Put(hours);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[JOB] игра дала гостевой пропуск в {loc.name} на {hours} ч - отправляю клиентам");
        }

        static void OnJobGuestPassMsg(NetDataReader r)
        {
            int id = r.GetInt(); float hours = r.GetFloat();
            NewAddress a;
            if (!CityData.Instance.addressDictionary.TryGetValue(id, out a) || a == null) { Plugin.L.LogWarning($"[JOB] гостевой пропуск: адрес {id} не найден"); return; }
            applyingGuestPass = true;
            try { GameplayController.Instance.AddGuestPass(a, hours); Plugin.L.LogInfo($"[JOB] гостевой пропуск в {a.name} на {hours} ч - как у хоста"); }
            catch (Exception e) { Plugin.L.LogWarning("[JOB] гостевой пропуск: " + e.Message); }
            finally { applyingGuestPass = false; }
        }

        static void SendJobCall(int from, int to, Human caller, TelephoneController.CallSource src, float ring, bool specific)
        {
            var w = new NetDataWriter(); w.Put(MsgJob); w.Put((byte)24);
            w.Put(from); w.Put(to); w.Put(caller != null && !caller.isPlayer ? caller.humanID : -1);
            w.Put((int)src.callType); w.Put(src.dds ?? ""); w.Put(src.audio ?? ""); w.Put(src.dialog ?? ""); w.Put(src.job); w.Put((int)src.convoType);
            w.Put(src.dialogGreeting != null ? src.dialogGreeting.name : ""); w.Put(ring); w.Put(specific);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.L.LogInfo($"[PHONE] задание {src.job} звонит игроку {from} -> {to} ('{src.dialog}') - звонок и клиентам");
        }

        static void OnJobCallMsg(NetDataReader r)
        {
            int from = r.GetInt(), to = r.GetInt(), callerId = r.GetInt();
            var type = (TelephoneController.CallType)r.GetInt();
            string dds = r.GetString(), audio = r.GetString(), dialog = r.GetString(); int job = r.GetInt();
            var convo = (InteractionController.ConversationType)r.GetInt();
            string greet = r.GetString(); float ring = r.GetFloat(); bool specific = r.GetBool();
            var tc = TelephoneController.Instance; var p = Player.Instance;
            if (tc == null || p == null) return;
            try
            {
                var cs = new TelephoneController.CallSource(type, dds);
                cs.dds = dds; cs.audio = audio; cs.dialog = dialog; cs.job = job; cs.convoType = convo;
                cs.dialogGreeting = FindDialogPreset(greet);
                Human caller = null;
                if (callerId >= 0) CityData.Instance.GetHuman(callerId, out caller, false);
                applyingJobCall = true;
                var call = tc.CreateNewCall(from, to, caller, p, cs, ring, specific);
                Plugin.L.LogInfo($"[PHONE] звонок задания {job} от хоста: {from} -> {to} ('{dialog}'), создан: {call != null}");
            }
            catch (Exception e) { Plugin.L.LogWarning("[PHONE] звонок задания: " + e.Message); }
            finally { applyingJobCall = false; }
        }

        static DialogPreset FindDialogPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (dialogByName == null)
            {
                dialogByName = new Dictionary<string, DialogPreset>();
                foreach (var d in Resources.FindObjectsOfTypeAll<DialogPreset>())
                    if (d != null && !string.IsNullOrEmpty(d.name) && !dialogByName.ContainsKey(d.name)) dialogByName[d.name] = d;
            }
            DialogPreset res; dialogByName.TryGetValue(name, out res);
            return res;
        }
    }
}

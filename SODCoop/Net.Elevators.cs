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
        static bool applyingElevator, elevErrLogged;
        static System.Collections.Generic.Dictionary<int, Elevator> elevById;
        static float elevMapAt = -100f, elevStateAt;
        static int elevMissLog;

        static int ElevId(Elevator e)
        {
            try { var t = e.bottom ?? e.top; return t != null ? t.tileID : -1; } catch (Exception) { return -1; }
        }

        static void RebuildElevMap()
        {
            elevMapAt = Time.realtimeSinceStartup;
            var map = new System.Collections.Generic.Dictionary<int, Elevator>();
            try
            {
                var bl = CityBuildings.Instance != null ? CityBuildings.Instance.buildingDirectory : null;
                for (int i = 0; bl != null && i < bl.Count; i++)
                {
                    var sw = bl[i] != null ? bl[i].stairwells : null;
                    if (sw == null) continue;
                    foreach (var kv in sw) { var e = kv.Value; if (e == null) continue; int id = ElevId(e); if (id >= 0) map[id] = e; }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[LIFT] список лифтов: " + e.Message); }
            elevById = map;
            Plugin.Dbg($"[LIFT] лифтов в городе: {map.Count}");
        }

        static bool flashErrLogged, flashLogged;

        static void AvatarFlash(GameObject av, float yaw)
        {
            var t = av.transform.Find("SodCoopFlash");
            if (!remoteFlash) { if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false); return; }
            if (t == null)
            {
                var fp = FirstPersonItemController.Instance;
                var src = fp != null ? fp.flashLightObject : null;
                if (src == null) return;
                var go = UnityEngine.Object.Instantiate(src);
                go.name = "SodCoopFlash";
                go.transform.SetParent(av.transform, false);
                t = go.transform;
                if (!flashLogged) { flashLogged = true; Plugin.L.LogInfo($"[FLASH] фонарик на аватаре: источников света {go.GetComponentsInChildren<Light>(true).Length}"); }
            }
            if (!t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(true);
                foreach (var l in t.GetComponentsInChildren<Light>(true)) { l.enabled = true; l.gameObject.SetActive(true); }
            }
            var rot = Quaternion.Euler(remotePitch, yaw, 0f);
            float ratK = RpRat(curRp) ? RatAvatarScale : 1f;
            t.position = av.transform.position + av.transform.up * 1.5f * ratK + rot * Vector3.forward * 0.3f * ratK;
            t.rotation = rot;
        }

        static void ElevatorUnder(Player p, float feetY, out int id, out float dy)
        {
            id = -1; dy = 0f;
            if (p == null || !p.inElevator) return;
            if (elevById == null || Time.realtimeSinceStartup - elevMapAt > 60f) RebuildElevMap();
            Vector3 me = p.transform.position;
            float best = 9f;
            foreach (var kv in elevById)
            {
                Transform cab = null; try { cab = kv.Value != null ? kv.Value.spawnedObject : null; } catch (Exception __e) { Err.Swallow(__e); }
                if (cab == null) continue;
                Vector3 c = cab.position;
                float dx = c.x - me.x, dz = c.z - me.z, d2 = dx * dx + dz * dz;
                if (d2 < best && Mathf.Abs(feetY - c.y) < 3f) { best = d2; id = kv.Key; dy = feetY - c.y; }
            }
        }

        static Elevator ElevatorById(int id)
        {
            Elevator e;
            if (elevById != null && elevById.TryGetValue(id, out e) && e != null) return e;
            if (Time.realtimeSinceStartup - elevMapAt > 10f) { RebuildElevMap(); if (elevById.TryGetValue(id, out e)) return e; }
            return null;
        }

        internal static void OnElevatorCall(Elevator e, int floor, bool up)
        {
            if (applyingElevator || manager == null || manager.ConnectedPeersCount == 0 || e == null) return;
            int id = ElevId(e);
            if (id < 0) return;
            var w = new NetDataWriter(); w.Put(MsgElevator); w.Put((byte)1); w.Put(id); w.Put(floor); w.Put(up);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Plugin.Dbg($"[LIFT] вызов лифта {id} на этаж {floor} ({(up ? "вверх" : "вниз")}) отправлен");
        }

        static void OnElevatorMsg(NetDataReader r)
        {
            byte op = r.GetByte();
            if (op == 1)
            {
                int id = r.GetInt(), floor = r.GetInt(); bool up = r.GetBool();
                var e = ElevatorById(id);
                if (e == null) { if (elevMissLog++ < 5) Plugin.L.LogWarning($"[LIFT] лифт {id} не найден"); return; }
                applyingElevator = true;
                try { e.CallElevator(floor, up); }
                finally { applyingElevator = false; }
                Plugin.Dbg($"[LIFT] другой игрок вызвал лифт {id} на этаж {floor}");
            }
            else if (op == 2 && !IsHost)
            {
                bool full = r.GetBool();
                int n = r.GetInt(), fixedN = 0;
                for (int i = 0; i < n; i++)
                {
                    int id = r.GetInt(); float y = r.GetFloat(); int floor = r.GetInt();
                    var e = ElevatorById(id);
                    if (e == null) continue;
                    try
                    {
                        if (e.isMoving || e.inTransit) continue;
                        float cy = e.spawnedObject != null ? e.spawnedObject.position.y : y;
                        if (e.currentFloor == floor && Mathf.Abs(cy - y) < 0.05f) continue;
                        var sv = new StateSaveData.ElevatorStateSave(); sv.tileID = id; sv.yPos = y; sv.floor = floor;
                        e.LoadElevatorSaveData(sv);
                        fixedN++;
                    }
                    catch (Exception ex) { if (elevMissLog++ < 5) Plugin.L.LogWarning($"[LIFT] выровнять лифт {id}: {ex.Message}"); }
                }
                if (full || fixedN > 0) Plugin.L.LogInfo($"[LIFT] положение лифтов от хоста: {n}, выровнено: {fixedN}");
            }
        }

        static void SendElevatorState(NetPeer to, bool full)
        {
            if (manager == null) return;
            if (elevById == null || Time.realtimeSinceStartup - elevMapAt > 60f) RebuildElevMap();
            var list = new System.Collections.Generic.List<KeyValuePair<int, Elevator>>();
            foreach (var kv in elevById)
            {
                var e = kv.Value; if (e == null) continue;
                try { if (e.isMoving || e.inTransit) continue; } catch (Exception) { continue; }
                list.Add(kv);
            }
            var w = new NetDataWriter(); w.Put(MsgElevator); w.Put((byte)2); w.Put(full); w.Put(list.Count);
            foreach (var kv in list)
            {
                var e = kv.Value;
                float y = 0f; try { y = e.spawnedObject != null ? e.spawnedObject.position.y : e.desiredY; } catch (Exception __e) { Err.Swallow(__e); }
                w.Put(kv.Key); w.Put(y); w.Put(e.currentFloor);
            }
            if (to != null) to.Send(w, DeliveryMethod.ReliableOrdered); else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void ElevatorTick()
        {
            if (!IsHost || manager == null || manager.ConnectedPeersCount == 0) return;
            float now = Time.realtimeSinceStartup;
            if (now < elevStateAt) return;
            elevStateAt = now + 5f;
            SendElevatorState(null, false);
        }

        static int MaxPlayersNow() { return Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4); }

        static void RemovePlayer(RP rp, string why)
        {
            try
            {
                Enter(rp);
                DestroyAvatar();
                if (puppet != null) ReleasePuppet("игрок вышел");
                else if (!IsHost && puppetId >= 0) { UnhidePuppet(puppetId); puppetId = -1; }
                Leave();
            }
            catch (Exception e) { Leave(); Plugin.L.LogWarning("[NET] RemovePlayer: " + e.Message); }
            players.Remove(rp);
            try { RemoveRemoteSnail(rp.pid); ghostPids.Remove(rp.pid); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[NET] игрок {rp.pid} убран ({why}); осталось удалённых игроков: {players.Count}");
            if (IsHost)
            {
                Toast((string.IsNullOrEmpty(rp.nick) ? PlayerLabel(rp.pid) : rp.nick) + " left the game (" + why + ").");
                var w = new NetDataWriter(); w.Put(MsgPlayerLeft); w.Put((byte)rp.pid);
                foreach (var o in players) if (o.peer != null) { try { o.peer.Send(w, DeliveryMethod.ReliableOrdered); } catch (Exception __e) { Err.Swallow(__e); } }
            }
        }

        static void ClearPlayers()
        {
            var copy = new List<RP>(players);
            foreach (var rp in copy)
            {
                try
                {
                    Enter(rp);
                    DestroyAvatar();
                    if (puppet != null) ReleasePuppet("сеть закрыта");
                    Leave();
                }
                catch (Exception) { Leave(); }
            }
            Leave();
            players.Clear();
            MapSessionEnded();
            foreach (var id in new List<int>(puppetHiddenRends.Keys)) UnhidePuppet(id);
            foreach (var id in new List<int>(puppetHiddenCols.Keys)) UnhidePuppet(id);
        }

        static bool IsRelayed(byte t)
        {
            return t == MsgPos || t == MsgAction || t == MsgItem || t == MsgHeld || t == MsgObj || t == MsgItemEv || t == MsgDoor || t == MsgBodyFx || t == MsgShot || t == MsgWeapon || t == MsgSound || t == MsgDown || t == MsgRevive || t == MsgElevator || t == MsgKnock || t == MsgJob || t == MsgMurder || t == MsgCorpse || t == MsgSnail || t == MsgPermaDead || t == MsgInvite;
        }

        static bool NeedsCtx(byte t)
        {
            return t == MsgKnock || t == MsgPos || t == MsgAction || t == MsgItem || t == MsgHeld || t == MsgBodyFx || t == MsgShot || t == MsgWeapon || t == MsgSound || t == MsgNpcHit || t == MsgDown || t == MsgRevive;
        }

        static void RelayFromPlayer(RP from, byte type, byte[] rest, DeliveryMethod method)
        {
            if (players.Count < 2) return;
            var w = new NetDataWriter();
            w.Put(MsgRelay); w.Put((byte)from.pid); w.Put(type); w.Put(rest, 0, rest.Length);
            foreach (var o in players)
                if (o != from && o.peer != null) { try { o.peer.Send(w, method); } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static void OnReceive(NetPeer peer, NetPacketReader raw, DeliveryMethod method)
        {
            byte type = raw.GetByte();
            int senderPid;
            if (type == MsgRelay && !IsHost) { senderPid = raw.GetByte(); type = raw.GetByte(); }
            else if (IsHost) { var hp = RpByPeer(peer); senderPid = hp != null ? hp.pid : -1; }
            else senderPid = 0;
            byte[] rest = raw.GetRemainingBytes();
            var reader = new NetDataReader();
            reader.SetSource(rest);
            RP rp = senderPid >= 0 ? RpByPid(senderPid) : null;
            if (rp == null && !IsHost && senderPid >= 0 && senderPid != MyPid && NeedsCtx(type)) rp = NewRp(senderPid);
            if (IsHost && rp != null && IsRelayed(type)) RelayFromPlayer(rp, type, rest, method);
            bool ctx = rp != null && NeedsCtx(type);
            if (NeedsCtx(type) && rp == null) return;
            if (ctx) Enter(rp);
            try { HandleMsg(peer, type, reader, rp); }
            finally { if (ctx) Leave(); }
        }

        static void HandleMsg(NetPeer peer, byte type, NetDataReader reader, RP rp)
        {
            if (type == MsgHello && IsHost)
            {
                string theirs = reader.GetString();
                string theirApp = reader.AvailableBytes > 0 ? reader.GetString() : "";
                string cid = reader.AvailableBytes > 0 ? reader.GetString() : "";
                string profId = reader.AvailableBytes > 0 ? reader.GetString() : "";
                string mine = Describe();
                bool ok = theirs == mine;
                RP me = RpByPeer(peer);
                if (me == null && cid != "")
                {
                    RP stale = null;
                    foreach (var o in players) if (o.clientId == cid && o.peer != peer) { stale = o; break; }
                    if (stale != null)
                    {
                        var sp = stale.peer; RemovePlayer(stale, "переподключился");
                        try { if (sp != null) sp.Disconnect(); } catch (Exception __e) { Err.Swallow(__e); }
                        Plugin.L.LogInfo("[NET] новое подключение заменило старое соединение игрока");
                    }
                }
                if (me == null)
                {
                    if (players.Count >= MaxPlayersNow() - 1)
                    {
                        Plugin.L.LogWarning($"[NET] сервер полон ({players.Count + 1}/{MaxPlayersNow()}), отклоняю игрока");
                        var fw = new NetDataWriter();
                        fw.Put(MsgHelloReply); fw.Put(false); fw.Put("FULL"); fw.Put(""); fw.Put((byte)255); fw.Put(true);
                        peer.Send(fw, DeliveryMethod.ReliableOrdered);
                        return;
                    }
                    me = NewRp(FreePid()); me.peer = peer; me.clientId = cid;
                }
                me.profileId = profId;
                if (profId != "") foreach (var o2 in players) if (o2 != me && o2.profileId == profId) { Plugin.L.LogWarning("[PROFILE] у двух игроков одинаковый постоянный номер (скопирована папка игры с SodCoop_playerid.txt?). Удалите этот файл у одной из копий"); break; }
                Enter(me); SetRemoteAppearance(theirApp); Leave();
                Toast(ok ? UiKit.T(PlayerLabel(me.pid) + " joined the game.", PlayerLabel(me.pid) + " зашёл в игру.") : "A player connected, but their world does not match yours.");
                Plugin.L.LogInfo($"[NET] Handshake от клиента: ok={ok}, ему выдан номер {me.pid}");
                Plugin.L.LogInfo($"[NET]   клиент: {theirs}");
                Plugin.L.LogInfo($"[NET]   хост:   {mine}");

                var w = new NetDataWriter();
                w.Put(MsgHelloReply);
                w.Put(ok);
                w.Put(mine);
                w.Put(MyAppearanceJson());
                w.Put((byte)me.pid);
                w.Put(false);
                w.Put(Plugin.ModVersion);
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                if (ok)
                {
                    SendRules(peer);
                    SendDoorSnapshot(peer); SendRoomSnapshot(peer); SendWindowSnapshot(peer); SendTime(); SendSoldSnapshot(peer); SendFurnSnapshot(peer);
                    try { SendElevatorState(peer, true); } catch (Exception e) { Plugin.L.LogWarning("[LIFT] снимок: " + e.Message); }
                    try { SendJobSnapshot(peer); } catch (Exception e) { Plugin.L.LogWarning("[JOB] снимок: " + e.Message); }
                    try { SendMurderSnapshot(peer); } catch (Exception e) { Plugin.L.LogWarning("[MURDER] снимок: " + e.Message); }
                    try { SendPrintSnapshot(peer); } catch (Exception e) { Plugin.L.LogWarning("[PRINTS] снимок: " + e.Message); }
                    try { SendAlarmSnapshot(peer); } catch (Exception e) { Plugin.L.LogWarning("[ALARM] снимок: " + e.Message); }
                    foreach (var o in players)
                    {
                        if (o == me || o.peer == null) continue;
                        var i1 = new NetDataWriter(); i1.Put(MsgPlayerInfo); i1.Put((byte)o.pid); i1.Put(o.remoteAppJson ?? "");
                        peer.Send(i1, DeliveryMethod.ReliableOrdered);
                        var i2 = new NetDataWriter(); i2.Put(MsgPlayerInfo); i2.Put((byte)me.pid); i2.Put(me.remoteAppJson ?? "");
                        o.peer.Send(i2, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
            else if (type == MsgHelloReply && !IsHost)
            {
                bool ok = reader.GetBool();
                string hostInfo = reader.GetString();
                string hostApp = reader.AvailableBytes > 0 ? reader.GetString() : "";
                int pid = reader.AvailableBytes > 0 ? reader.GetByte() : 0;
                bool full = reader.AvailableBytes > 0 && reader.GetBool();
                string hostVer = reader.AvailableBytes > 0 ? reader.GetString() : "";
                if (!full && hostVer != Plugin.ModVersion)
                {
                    try { peer.Disconnect(); } catch (Exception __e) { Err.Swallow(__e); }
                    VersionMismatch(hostVer);
                    return;
                }
                if (full)
                {
                    Toast("The server is full.");
                    Plugin.L.LogWarning("[NET] Сервер полон, повторов не будет");
                    connectRejected = true;
                    try { peer.Disconnect(); } catch (Exception __e) { Err.Swallow(__e); }
                    return;
                }
                MyPid = pid;
                helloOkFlag = ok; if (ok && profState == 0) profReadyAt = Time.realtimeSinceStartup + 4f;
                var hostRp = RpByPid(0) ?? NewRp(0);
                Enter(hostRp); SetRemoteAppearance(hostApp); Leave();
                Plugin.L.LogInfo($"[NET] Ответ хоста: ok={ok}, мой номер {pid}");
                if (!ok && helloWasOk) { Plugin.L.LogInfo("[RELOAD] у хоста теперь другой мир - скачаю и загружу его"); ClientFollowHostReload(); }
                else if (!ok) Toast("Your world does not match the host's (different city or build). Leave to the main menu and use CONNECT to download the host's save.");
                if (ok) helloWasOk = true;
                Plugin.L.LogInfo($"[NET]   хост:   {hostInfo}");
                Plugin.L.LogInfo($"[NET]   клиент: {Describe()}");
                if (ok) SendFurnSnapshot(null);
            }
            else if (type == MsgProfile)
            {
                byte pop = reader.GetByte();
                if (IsHost) { if (rp != null) ProfileHostMsg(rp, pop, reader); }
                else if (pop == 3) ProfileClientData(reader);
                else if (pop == 4) SendProfile(true);
            }
            else if (type == MsgPlayerInfo && !IsHost)
            {
                int pid = reader.GetByte(); string app = reader.GetString();
                if (pid != MyPid)
                {
                    var o = RpByPid(pid) ?? NewRp(pid);
                    Enter(o); SetRemoteAppearance(app); Leave();
                    Plugin.L.LogInfo($"[NET] в игре ещё один игрок: номер {pid}");
                }
            }
            else if (type == MsgPermaDead)
            {
                OnPermaDeadMsg(rp, reader);
            }
            else if (type == MsgInvite)
            {
                OnInviteMsg(reader);
            }
            else if (type == MsgDebt)
            {
                OnDebtMsg(rp, reader);
            }
            else if (type == MsgTalk)
            {
                OnTalkMsg(rp, reader);
            }
            else if (type == MsgWatch && !IsHost)
            {
                OnWatchMsg(reader);
            }
            else if (type == MsgSnail)
            {
                OnSnailMsg(rp, reader);
            }
            else if (type == MsgIllegal && IsHost)
            {
                if (rp != null) OnIllegalMsg(rp, reader);
            }
            else if (type == MsgRules && !IsHost)
            {
                Rules.Read(reader);
            }
            else if (type == MsgHostReload && !IsHost)
            {
                ClientFollowHostReload();
                return;
            }
            else if (type == MsgPlayerLeft && !IsHost)
            {
                int pid = reader.GetByte();
                var o = RpByPid(pid);
                if (o != null) { string nm = PlayerLabel(pid); RemovePlayer(o, "вышел"); Toast(nm + " left the game."); }
            }
            else if (type == MsgAction)
            {
                byte kind = reader.GetByte();
                if (avatarAnim != null)
                {
                    if (kind == 1 || kind == 2) combatUntil = Time.realtimeSinceStartup + 4f;
                    if (kind == 3 || kind == 4)
                    {
                        string trg = Plugin.CfgThrowState.Value ?? "";
                        if (trg != "") avatarAnim.SetTrigger(trg);
                    }
                    else if (kind == 1)
                    {
                        attackAlt = !attackAlt;
                        avatarAnim.SetTrigger(attackAlt ? "attack" : "attack2");
                    }
                    else if (kind == 2) avatarAnim.SetTrigger("block");
                }
            }
            else if (type == MsgItemEv)
            {
                byte ik = reader.GetByte();
                int iid = reader.GetInt();
                string ipn = reader.GetString(); Vector3 ip = Vector3.zero, ie = Vector3.zero; ItemState ist = null;
                if (ik == 2)
                {
                    ip = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    ie = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    ist = ReadState(reader);
                }
                else if (ik == 5)
                {
                    ip = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    ie = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    ist = new ItemState(); ist.val = reader.GetFloat();
                }
                if (Plugin.CfgObjSync.Value) OnItemEvent(ik, iid, ipn, ip, ie, ist);
            }
            else if (type == MsgObj)
            {
                int oid = reader.GetInt();
                byte oflags = reader.GetByte();
                int ohash = reader.GetInt();
                Vector3 opos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                Quaternion orot = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                if (Plugin.CfgObjSync.Value) OnRemoteObject(oid, oflags, ohash, opos, orot);
            }
            else if (type == MsgDoor)
            {
                doorSender = rp;
                try { OnDoorMessage(reader); } finally { doorSender = null; }
            }
            else if (type == MsgNpcHit)
            {
                if (IsHost && Plugin.CfgNpcSync.Value) OnNpcHit(reader);
            }
            else if (type == MsgSaveReq) { if (IsHost) HostBeginSend(peer); }
            else if (type == MsgSaveWant) { if (IsHost) HostOnWant(reader); }
            else if (type == MsgSaveMeta) { if (xferMode) ClientOnMeta(reader); }
            else if (type == MsgSaveChunk) { if (xferMode) ClientOnChunk(reader); }
            else if (type == MsgSaveDone) { if (xferMode) xferFinish = true; }
            else if (type == MsgSaveFail) { if (xferMode) xferFailMsg = "Host: " + reader.GetString(); }
            else if (type == MsgBodyFx)
            {
                OnBodyFx(reader);
            }
            else if (type == MsgMurder)
            {
                try { OnMurderMsg(reader); } catch (Exception e) { Plugin.L.LogWarning("[MURDER] " + e); }
            }
            else if (type == MsgCorpse)
            {
                try { OnCorpseMsg(reader); } catch (Exception e) { Plugin.L.LogWarning("[CORPSE] " + e.Message); }
            }
            else if (type == MsgJob)
            {
                OnJobMsg(rp, reader);
            }
            else if (type == MsgKnock)
            {
                if (rp != null) OnKnockMsg(rp, reader);
            }
            else if (type == MsgElevator)
            {
                OnElevatorMsg(reader);
            }
            else if (type == MsgDown)
            {
                if (rp != null) OnDownMsg(rp, reader);
            }
            else if (type == MsgRevive)
            {
                if (rp != null) OnReviveMsg(rp, reader);
            }
            else if (type == MsgSound)
            {
                OnSoundMessage(reader);
            }
            else if (type == MsgShot)
            {
                OnShotMessage(reader);
            }
            else if (type == MsgWeapon)
            {
                OnWeaponMessage(reader);
            }
            else if (type == MsgNpcAct)
            {
                if (!IsHost && Plugin.CfgNpcSync.Value) OnNpcAct(reader);
            }
            else if (type == MsgPuppet)
            {
                if (!IsHost) OnPuppetTable(reader);
            }
            else if (type == MsgPuppetHurt)
            {
                int victim = reader.GetByte();
                if (IsHost && victim != 0)
                {
                    var vr = RpByPid(victim);
                    if (vr != null && vr.peer != null)
                    {
                        byte[] tail = reader.GetRemainingBytes();
                        var fw = new NetDataWriter(); fw.Put(MsgPuppetHurt); fw.Put((byte)victim); fw.Put(tail, 0, tail.Length);
                        try { vr.peer.Send(fw, DeliveryMethod.ReliableOrdered); } catch (Exception __e) { Err.Swallow(__e); }
                    }
                }
                else if (!IsHost || Rules.Pvp) OnPuppetHurt(reader);
            }
            else if (type == MsgNpc)
            {
                if (!IsHost && Plugin.CfgNpcSync.Value) OnNpcMessage(reader);
            }
            else if (type == MsgHeld)
            {
                remoteHeld.Clear();
                int n = reader.GetInt();
                for (int i = 0; i < n; i++)
                {
                    var hp = new HeldPart();
                    hp.mesh = reader.GetString();
                    hp.side = reader.GetByte();
                    int mc = reader.GetInt();
                    hp.mats = new string[mc];
                    for (int j = 0; j < mc; j++) hp.mats[j] = reader.GetString();
                    hp.u = reader.GetFloat();
                    int nn = reader.GetInt();
                    hp.chain = new float[nn * 10];
                    for (int j = 0; j < hp.chain.Length; j++) hp.chain[j] = reader.GetFloat();
                    remoteHeld.Add(hp);
                }
                remoteHeldPreset = reader.AvailableBytes > 0 ? reader.GetString() : "";
                string rsig = HeldSig(remoteHeld) + "#" + remoteHeldPreset;
                if (rsig != remoteHeldSig) { remoteHeldSig = rsig; remoteHeldDirty = true; }
            }
            else if (type == MsgItem)
            {
                remoteItem = reader.GetString();
            }
            else if (type == MsgPos)
            {
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                float yaw = reader.GetFloat();
                targetPos = new Vector3(x, y, z);
                targetYaw = yaw;
                byte fl = reader.GetByte();
                remoteCrouched = (fl & 1) != 0;
                remoteRunning = (fl & 2) != 0;
                remoteFists = (fl & 4) != 0;
                remoteBlocking = false;
                remoteConsuming = (fl & 16) != 0;
                remoteSitting = (fl & 32) != 0;
                remoteVent = (fl & 8) != 0;
                if (remoteVent) { remoteCrouched = true; remoteSitting = false; }
                remoteCarryObj = (fl & 64) != 0;
                remoteLean = reader.GetFloat();
                remoteSeatId = reader.AvailableBytes >= 4 ? reader.GetInt() : -1;
                remoteDrunk = reader.AvailableBytes >= 1 ? reader.GetByte() / 255f : 0f;
                if (reader.AvailableBytes >= 8) { remoteElevId = reader.GetInt(); remoteElevDy = reader.GetFloat(); } else remoteElevId = -1;
                if (reader.AvailableBytes >= 5) { remoteFlash = reader.GetBool(); remotePitch = reader.GetFloat(); } else remoteFlash = false;
                bool ratOn = reader.AvailableBytes >= 1 && reader.GetBool();
                Vector3 ratUp = Vector3.up;
                if (ratOn && reader.AvailableBytes >= 12) ratUp = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                if (rp != null)
                {
                    if (ratOn != rp.ratOn) Plugin.L.LogInfo($"[RAT] игрок {rp.pid} {(ratOn ? "теперь крыса - уменьшаю аватар" : "больше не крыса")}");
                    rp.ratOn = ratOn; rp.ratUp = ratUp;
                }
                if (avatar == null) CreateAvatar();
            }
        }
    }
}

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
        static float hostReloadAt = -1f;
        static bool followHost, followPending, helloWasOk;
        static float followAt = -1f;

        internal static void OnMenuComponent(int comp)
        {
            if (comp != (int)MainMenuController.Component.loadingCity || !IsHost || manager == null || hostReloadAt >= 0f) return;
            int n = manager.ConnectedPeersCount;
            Plugin.L.LogInfo($"[RELOAD] хост загружает сейв/город - сообщаю клиентам ({n}) и закрываю сервер до конца загрузки");
            if (n > 0)
            {
                var w = new NetDataWriter(); w.Put(MsgHostReload);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            hostReloadAt = Time.realtimeSinceStartup + (n > 0 ? 0.7f : 0f);
        }

        static void ReloadTick()
        {
            float now = Time.realtimeSinceStartup;
            if (versionMismatchHost != null) { var hv = versionMismatchHost; versionMismatchHost = null; VersionMismatchApply(hv); }
            if (hostReloadAt >= 0f && now >= hostReloadAt)
            {
                hostReloadAt = -1f;
                Shutdown();
                Plugin.L.LogInfo("[RELOAD] сервер закрыт на время загрузки");
            }
            if (followPending)
            {
                followPending = false;
                Plugin.L.LogInfo("[RELOAD] хост загружает другой сейв - ухожу в меню, потом скачаю и загружу его мир");
                try { ProfileOnShutdown(); } catch (Exception __e) { Err.Swallow(__e); }
                Shutdown();
                ClientReleaseWorld();
                followHost = true;
                followAt = now + 3f;
                OpenMenuForLoad();
                MenuAuto.Notice = "The host is loading a save. You will follow automatically.";
            }
            if (followAt >= 0f && now >= followAt && RtMode == NetMode.Client)
            {
                followAt = -1f;
                Plugin.L.LogInfo("[RELOAD] жду хоста и прошу его новый мир");
                Shutdown();
                StartTransfer();
                xferText = "The host is loading a save. Waiting for the host...";
            }
        }

        static void ClientFollowHostReload() { followPending = true; }

        static string versionMismatchHost;

        static void VersionMismatch(string hostVer) { connectRejected = true; versionMismatchHost = hostVer ?? ""; }

        static void VersionMismatchApply(string hostVer)
        {
            string mine = Plugin.ModVersion;
            int cmp = hostVer == "" ? -1 : CompareVersions(hostVer, mine);
            string hv = hostVer != "" ? hostVer : UiKit.T("older than 0.12.0", "старее 0.12.0");
            string text = cmp < 0
                ? UiKit.T($"The host has an older mod version ({hv}), you have {mine}.\nThe host needs to update the mod.", $"У хоста более старая версия мода ({hv}), у тебя {mine}.\nХосту нужно обновить мод.")
                : UiKit.T($"The host has a newer mod version ({hv}), you have {mine}.\nUpdate the mod to play together.", $"У хоста более новая версия мода ({hv}), у тебя {mine}.\nОбнови мод, чтобы играть вместе.");
            Plugin.L.LogWarning($"[NET] версия мода у хоста {hv}, у меня {mine} - подключение невозможно");
            if (xferMode) { xferMode = false; xferFinish = false; xferText = ""; }
            followHost = false; followAt = -1f;
            Shutdown();
            ClientReleaseWorld();
            RtMode = NetMode.Off;
            Toast(text.Replace("\n", " "));
            if (cmp > 0 && Updater.Newer) UiKit.ShowMessage(UiKit.T("Can't connect", "Подключиться нельзя"), text, (UiKit.T("Update", "Обновить"), () => Updater.Offer(UiKit.T("Mod update", "Обновление мода"))), ("OK", null));
            else if (cmp > 0) UiKit.ShowMessage(UiKit.T("Can't connect", "Подключиться нельзя"), text, (UiKit.T("Open page", "Открыть страницу"), Updater.OpenPage), ("OK", null));
            else UiKit.ShowMessage(UiKit.T("Can't connect", "Подключиться нельзя"), text);
        }

        internal static int CompareVersions(string a, string b)
        {
            Version va, vb;
            if (Version.TryParse(a, out va) && Version.TryParse(b, out vb)) return va.CompareTo(vb);
            return string.CompareOrdinal(a, b);
        }

        static void OpenMenuForLoad()
        {
            try
            {
                var mm = MainMenuController.Instance;
                if (mm != null && !mm.mainMenuActive) mm.EnableMainMenu(true, false, false, MainMenuController.Component.mainMenuButtons);
            }
            catch (Exception e) { Plugin.L.LogWarning("[RELOAD] открыть меню: " + e.Message); }
        }

        static void HostBeginSend(NetPeer peer)
        {
            if (sendStage != 0)
            {
                if (peer != sendPeer && !sendQueue.Contains(peer)) { sendQueue.Add(peer); Plugin.L.LogInfo($"[XFER] клиент просит сохранение - в очереди {sendQueue.Count}"); }
                return;
            }
            sendPeer = peer; sendFiles = new List<XFile>(); snapTask = null; sendIdx = 0; snapVariant = 0;
            Plugin.L.LogInfo("[XFER] клиент просит сохранение");
            SnapStart();
            sendStage = 1;
        }

        static void SnapStart()
        {
            string full = Path.Combine(SaveDir(), SyncName);
            string[] variants = { Path.Combine(SaveDir(), "SodCoop_sync.sod"), full, Path.GetFileNameWithoutExtension(SyncName) };
            if (snapVariant == 0) { try { foreach (var o in Directory.GetFiles(SaveDir(), "SodCoop_sync*")) File.Delete(o); } catch (Exception __e) { Err.Swallow(__e); } }
            string path = variants[Mathf.Clamp(snapVariant, 0, variants.Length - 1)];
            snapTask = null; snapStartUtc = DateTime.UtcNow; snapAt = Time.realtimeSinceStartup;
            try { snapTask = SaveStateController.Instance.CaptureSaveStateAsync(path, true); Plugin.L.LogInfo("[XFER] снимок, вариант " + snapVariant + ": " + path); }
            catch (Exception e) { Plugin.L.LogWarning("[XFER] CaptureSaveStateAsync(" + path + "): " + e.Message); snapTask = null; }
        }

        static string TaskInfo(object t)
        {
            if (t == null) return "null";
            var sb = new StringBuilder();
            foreach (var n in new[] { "Status", "IsCompleted", "IsFaulted", "IsCanceled" })
            {
                try { var p = t.GetType().GetProperty(n); if (p != null) sb.Append(n + "=" + p.GetValue(t, null) + " "); } catch (Exception __e) { Err.Swallow(__e); }
            }
            try { var p = t.GetType().GetProperty("Exception"); var ex = p != null ? p.GetValue(t, null) : null; if (ex != null) sb.Append("Exception=" + ex); } catch (Exception __e) { Err.Swallow(__e); }
            return sb.ToString();
        }

        static string FindFreshSave()
        {
            string best = null; DateTime bt = DateTime.MinValue;
            try
            {
                foreach (var f in Directory.GetFiles(SaveDir()))
                {
                    if (f.EndsWith(".part")) continue;
                    var t = File.GetLastWriteTimeUtc(f);
                    if (Path.GetFileName(f).StartsWith("SodCoop_sync")) t = t.AddDays(1);
                    if (t >= snapStartUtc.AddSeconds(-1) && new FileInfo(f).Length > 0 && t > bt) { bt = t; best = f; }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return best;
        }

        static bool TaskDone(object t)
        {
            if (t == null) return true;
            try { var p = t.GetType().GetProperty("IsCompleted"); if (p != null) return (bool)p.GetValue(t, null); } catch (Exception __e) { Err.Swallow(__e); }
            return true;
        }

        static void HostSendTick()
        {
            if (sendStage == 0) return;
            try
            {
                if (sendPeer == null || sendPeer.ConnectionState != ConnectionState.Connected) { Plugin.L.LogWarning("[XFER] клиент отключился"); HostEndSend(); return; }
                float now = Time.realtimeSinceStartup;
                if (sendStage == 1)
                {
                    float el = now - snapAt;
                    if (el < 1.5f) return;
                    if (!TaskDone(snapTask) && el < 60f) return;
                    string use = FindFreshSave();
                    if (use == null && snapVariant < 2)
                    {
                        Plugin.L.LogWarning("[XFER] вариант " + snapVariant + ": файл не появился. Задача: " + TaskInfo(snapTask));
                        snapVariant++; SnapStart(); return;
                    }
                    if (use != null) Plugin.L.LogInfo("[XFER] снимок готов: " + use + " (вариант " + snapVariant + "); задача: " + TaskInfo(snapTask));
                    if (use == null)
                    {
                        Plugin.L.LogWarning("[XFER] снимок не получился ни одним способом, беру самое новое сохранение с диска. Задача: " + TaskInfo(snapTask));
                        DateTime best = DateTime.MinValue;
                        foreach (var f in Directory.GetFiles(SaveDir(), "*.sodb")) { var t = File.GetLastWriteTimeUtc(f); if (t > best) { best = t; use = f; } }
                    }
                    if (use == null) { HostFail("the host has no save to send"); return; }
                    if (!use.EndsWith(".sodb", StringComparison.OrdinalIgnoreCase) || (Path.GetFileName(use).StartsWith("SodCoop_sync") && Path.GetFileName(use) != SyncName))
                    {
                        try
                        {
                            string norm = Path.Combine(SaveDir(), SyncName);
                            File.Copy(use, norm, true);
                            Plugin.L.LogInfo("[XFER] имя снимка исправлено: " + Path.GetFileName(use) + " -> " + SyncName);
                            use = norm;
                        }
                        catch (Exception e) { HostFail("cannot rename snapshot: " + e.Message); return; }
                    }
                    sendFiles.Add(new XFile { Name = Path.GetFileName(use), Dest = use, Kind = 0, Size = new FileInfo(use).Length });
                    string seed = "" + CityData.Instance.seed;
                    if (seed.Length > 3)
                        foreach (var f in Directory.GetFiles(CityDir()))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if ((ext == ".citb" || ext == ".txt") && Path.GetFileName(f).Contains(seed))
                                sendFiles.Add(new XFile { Name = Path.GetFileName(f), Dest = f, Kind = 1, Size = new FileInfo(f).Length });
                        }
                    var w = new NetDataWriter(); w.Put(MsgSaveMeta); w.Put(sendFiles.Count);
                    foreach (var f in sendFiles) { w.Put(f.Name); w.Put(f.Kind); w.Put(f.Size); }
                    sendPeer.Send(w, DeliveryMethod.ReliableOrdered);
                    sendStage = 2; snapAt = now;
                    Plugin.L.LogInfo("[XFER] отправил список файлов: " + sendFiles.Count);
                }
                else if (sendStage == 2)
                {
                    if (now - snapAt > 30f) HostFail("the client did not answer");
                }
                else if (sendStage == 3)
                {
                    for (int budget = 0; budget < 8 && sendIdx < sendFiles.Count;)
                    {
                        var f = sendFiles[sendIdx];
                        if (!f.Need) { sendIdx++; continue; }
                        if (f.Fs == null) f.Fs = new FileStream(f.Dest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var buf = new byte[16384];
                        int n = f.Fs.Read(buf, 0, buf.Length);
                        if (n <= 0) { f.Fs.Close(); f.Fs = null; sendIdx++; continue; }
                        var w = new NetDataWriter(); w.Put(MsgSaveChunk); w.Put((byte)sendIdx); w.Put(n); w.Put(buf, 0, n);
                        sendPeer.Send(w, DeliveryMethod.ReliableOrdered);
                        budget++;
                    }
                    if (sendIdx >= sendFiles.Count)
                    {
                        var w = new NetDataWriter(); w.Put(MsgSaveDone);
                        sendPeer.Send(w, DeliveryMethod.ReliableOrdered);
                        Plugin.L.LogInfo("[XFER] отправка закончена");
                        HostEndSend();
                    }
                }
            }
            catch (Exception e) { HostFail("send error: " + e.Message); }
        }

        static void HostOnWant(NetDataReader r)
        {
            if (sendStage != 2 || sendFiles == null) return;
            int n = r.GetInt();
            for (int i = 0; i < n && i < sendFiles.Count; i++) sendFiles[i].Need = r.GetBool();
            sendIdx = 0; sendStage = 3;
            Plugin.L.LogInfo("[XFER] клиент ответил, начинаю отправку");
        }

        static string MyAppearanceJson()
        {
            try { var a = CharEditor.Load(); return a == null ? "" : CharEditor.ToJson(a); } catch (Exception) { return ""; }
        }

        static string PlayerLabel(int pid)
        {
            foreach (var r in players) if (r.pid == pid && !string.IsNullOrEmpty(r.nick)) return r.nick;
            return UiKit.T("Player ", "Игрок ") + (pid + 1);
        }

        static float nameAt;

        static void NameTick(Player p)
        {
            if (p == null || Time.realtimeSinceStartup < nameAt) return;
            nameAt = Time.realtimeSinceStartup + 2f;
            var a = CharEditor.Mine;
            if (a == null) return;
            string f = (a.FirstName ?? "").Trim(), s = (a.SurName ?? "").Trim();
            if (f == "" && s == "") return;
            string full = a.FullName();
            if (p.firstName == f && p.surName == s && p.citizenName == full) return;
            p.firstName = f; p.surName = s; p.casualName = f != "" ? f : s; p.citizenName = full;
            try { var g = Game.Instance; if (g != null) { g.playerFirstName = f; g.playerSurname = s; } } catch (Exception e) { Err.Swallow(e); }
            Plugin.L.LogInfo("[CHAR] моё имя в игре: " + full);
        }

        static void SetRemoteAppearance(string json)
        {
            json = json ?? "";
            if (json == remoteAppJson) return;
            remoteAppJson = json;
            remoteApp = CharEditor.FromJson(json);
            if (curRp != null) curRp.nick = remoteApp != null ? remoteApp.FullName() : "";
            Plugin.L.LogInfo("[CHAR] получен внешний вид второго игрока: " + (remoteApp == null ? "нет (останется модель жителя)" : "голова=" + remoteApp.Head + " волосы=" + remoteApp.Hair + " верх=" + remoteApp.Top + " низ=" + remoteApp.Bottom));
            if (avatar != null) DestroyAvatar();
        }

        static string Describe()
        {
            var c = CityData.Instance;
            return $"seed={c.seed} build={Game.Instance.buildID} " +
                   $"npc={c.citizenDictionary.Count} doors={c.doorDictionary.Count}";
        }

        static Vector3 lastSendPos; static bool haveSendPos, talkLogged, talkHold; static float talkHoldAt, talkLogAt;

        static float GroundBelow(Vector3 pos, float fallback)
        {
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(pos + new Vector3(0f, 0.3f, 0f), Vector3.down, out hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return fallback;
        }
        static float FeetY(Player p)
        {
            try
            {
                var cc = p.charController;
                if (cc != null) return cc.bounds.min.y;
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return p.transform.position.y - 0.9f;
        }

        static float LookYaw(Player p)
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.eulerAngles.y : p.transform.eulerAngles.y;
        }

        internal static void Start()
        {
            Started = true;
            EnsureRt();
            var mode = RtMode;
            if (mode == NetMode.Off)
            {
                Plugin.L.LogInfo("[NET] Сеть выключена (Mode=Off)");
                return;
            }

            try
            {
                listener = new EventBasedNetListener();
                manager = new NetManager(listener);

                listener.ConnectionRequestEvent += request =>
                {
                    try
                    {
                        if (!IsHost) { request.Reject(); return; }
                        string k = request.Data.GetString();
                        string pw = request.Data.AvailableBytes > 0 ? request.Data.GetString() : "";
                        string ver = request.Data.AvailableBytes > 0 ? request.Data.GetString() : "";
                        if (k != Key) { request.Reject(); return; }
                        if (ver != Plugin.ModVersion)
                        {
                            string shown = ver != "" ? ver : UiKit.T("older than 0.12.0", "старее 0.12.0");
                            Plugin.L.LogWarning($"[NET] клиент с версией мода {shown} (у меня {Plugin.ModVersion}) - отклоняю");
                            Toast(UiKit.T($"A player with mod version {shown} tried to join (yours is {Plugin.ModVersion}).", $"Игрок с версией мода {shown} пытался подключиться (у тебя {Plugin.ModVersion})."));
                            var rw = new NetDataWriter(); rw.Put("VERSION"); rw.Put(Plugin.ModVersion);
                            request.Reject(rw); return;
                        }
                        if (!string.IsNullOrEmpty(RtPassword) && pw != RtPassword)
                        {
                            Plugin.L.LogWarning("[NET] Неверный пароль при подключении, отклоняю");
                            request.Reject(); return;
                        }
                        request.Accept();
                    }
                    catch (Exception) { try { request.Reject(); } catch (Exception __e) { Err.Swallow(__e); } }
                };

                listener.PeerConnectedEvent += peer =>
                {
                    if (IsHost)
                    {
                        Plugin.L.LogInfo($"[NET] Клиент подключился: {peer}");
                    }
                    else
                    {
                        serverPeer = peer;
                        connecting = false;
                        Plugin.L.LogInfo($"[NET] Подключен к хосту: {peer}");
                        if (!xferMode)
                        {
                            Toast(lostAt >= 0f ? "Reconnected to the host." : "Connected to the host.");
                            lostAt = -1f; everConnected = true;
                        }
                        var w = new NetDataWriter();
                        if (xferMode)
                        {
                            w.Put(MsgSaveReq);
                            xferText = "Connected, waiting for the host to prepare the save...";
                            xferAt = Time.realtimeSinceStartup;
                        }
                        else
                        {
                            w.Put(MsgHello);
                            w.Put(Describe());
                            w.Put(MyAppearanceJson());
                            w.Put(MyClientId);
                            w.Put(MyProfileId());
                        }
                        peer.Send(w, DeliveryMethod.ReliableOrdered);
                    }
                };

                listener.PeerDisconnectedEvent += (peer, info) =>
                {
                    Plugin.L.LogInfo($"[NET] Отключение: {peer}, причина: {info.Reason}");
                    if (IsHost)
                    {
                        var gone = RpByPeer(peer);
                        if (gone != null) RemovePlayer(gone, "" + info.Reason);
                        if (profDirty) ProfilesWrite();
                    }
                    if (!IsHost)
                    {
                        ClearPlayers();
                        bool hadHost = serverPeer != null && !xferMode;
                        serverPeer = null;
                        if (hadHost && info.Reason != DisconnectReason.ConnectionRejected)
                        {
                            lostAt = Time.realtimeSinceStartup;
                            ClientReleaseWorld();
                            Toast("Lost connection to the host (" + info.Reason + "). Reconnecting...  End = leave.");
                        }
                        connecting = false;
                        nextAttempt = Time.realtimeSinceStartup + 3f;
                        if (info.Reason == DisconnectReason.ConnectionRejected && info.AdditionalData != null)
                        {
                            try
                            {
                                if (info.AdditionalData.AvailableBytes > 0 && info.AdditionalData.GetString() == "VERSION")
                                {
                                    VersionMismatch(info.AdditionalData.AvailableBytes > 0 ? info.AdditionalData.GetString() : "");
                                    return;
                                }
                            }
                            catch (Exception __e) { Err.Swallow(__e); }
                        }
                        if (xferMode && xferFailMsg == null && !xferFinish && !(followHost && info.Reason != DisconnectReason.ConnectionRejected))
                            xferFailMsg = info.Reason == DisconnectReason.ConnectionRejected ? "Rejected by the host (wrong password?)" : "Connection to the host failed: " + info.Reason;
                        if (info.Reason == DisconnectReason.ConnectionRejected)
                        {
                            connectRejected = true;
                            Plugin.L.LogWarning("[NET] Хост отклонил подключение (неверный пароль?). Повторов не будет, выберите сервер заново");
                        }
                    }
                };

                listener.NetworkErrorEvent += (ep, err) =>
                    Plugin.L.LogWarning($"[NET] Ошибка сети: {err} ({ep})");

                listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
                {
                    try { OnReceive(peer, reader, method); }
                    finally { reader.Recycle(); }
                };

                if (IsHost)
                {
                    manager.Start(RtPort);
                    Plugin.L.LogInfo($"[NET] Хост запущен, UDP-порт {RtPort}. {Describe()}");
                    Lan.StartAdvertise();
                    try { SteamLink.StartHost(RtPort); } catch (Exception e) { Plugin.L.LogWarning("[STEAM] " + e.Message); }
                    try { ProfilesHostInit(); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] " + e.Message); }
                }
                else
                {
                    manager.Start();
                    Plugin.L.LogInfo($"[NET] Клиент запущен, цель {RtAddress}:{RtPort}");
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"[NET] Не удалось запустить сеть: {e}");
            }
        }

        internal static void Poll()
        {
            try { ReloadTick(); } catch (Exception e) { Plugin.L.LogWarning("[RELOAD] " + e.Message); }
            if (manager == null) return;
            try
            {
                manager.PollEvents();
                XferTick();
                if (manager == null) return;

                if (!IsHost && serverPeer == null && !connecting && !connectRejected &&
                    Time.realtimeSinceStartup >= nextAttempt)
                {
                    string ca = RtAddress; int cp = RtPort;
                    if (SteamLink.IsSteamAddress(RtAddress))
                    {
                        cp = SteamLink.StartClient(RtAddress, RtPort); ca = "127.0.0.1";
                        if (cp < 0) { nextAttempt = Time.realtimeSinceStartup + 5f; return; }
                    }
                    connecting = true;
                    var cw = new NetDataWriter();
                    cw.Put(Key); cw.Put(RtPassword ?? ""); cw.Put(Plugin.ModVersion);
                    manager.Connect(ca, cp, cw);
                }
            }
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.L.LogError($"[NET] Ошибка в Poll: {e}");
                }
            }
        }

        internal static void Tick(Player p)
        {
            bool debug = Plugin.CfgDebugAvatar.Value;
            if (manager == null && !debug) return;
            long tTick = Perf.T(); double n0 = Perf.Enter();
            try { TickInner(p, debug); }
            finally { Perf.Leave("тик: аватары и отправка", tTick, n0); }
        }

        static void TickInner(Player p, bool debug)
        {
            Leave();
            try
            {
                float dt = Time.unscaledDeltaTime;

                if (debug)
                {
                    var cam = Camera.main;
                    Vector3 origin = cam != null ? cam.transform.position : p.transform.position;
                    Vector3 fwd = cam != null ? cam.transform.forward : p.transform.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                    fwd = fwd.normalized;
                    Vector3 flat = origin + fwd * 3f;
                    var drp = RpByPid(0) ?? (players.Count == 0 ? NewRp(0) : players[0]);
                    Enter(drp);
                    targetPos = new Vector3(flat.x, FeetY(p), flat.z);
                    targetYaw = LookYaw(p) + 180f;
                    if (avatar == null) CreateAvatar();
                    Leave();
                }
                PollRemoteModel();

                if (manager != null)
                {
                    sendTimer += dt;
                    if (sendTimer >= 0.05f && manager.ConnectedPeersCount > 0)
                    {
                        sendTimer = 0f;
                        Vector3 pos = p.transform.position;
                        float feetSend = FeetY(p);
                        bool talkNow = false;
                        try { var ic0 = InteractionController.Instance; talkNow = ic0 != null && (ic0.dialogMode || ic0.talkingTo != null || ic0.activeInteractionAction); } catch (Exception __e) { Err.Swallow(__e); }
                        float groundY = GroundBelow(pos, lastSendPos.y);
                        if (!talkNow && talkHold)
                        {
                            bool back = Mathf.Abs(feetSend - groundY) < 0.3f;
                            if (Time.realtimeSinceStartup - talkLogAt > 1f) { talkLogAt = Time.realtimeSinceStartup; Plugin.Dbg($"[TALK] после разговора: тело y={feetSend:F2}, пол y={groundY:F2}, y трансформа={pos.y:F2}"); }
                            if (back || Time.realtimeSinceStartup - talkHoldAt > 8f) { talkHold = false; Plugin.Dbg($"[TALK] разговор закончен, отпускаю аватар (тело y={feetSend:F2}, пол y={groundY:F2}, вернулось={back})"); }
                            else talkNow = true;
                        }
                        if (talkNow && haveSendPos)
                        {
                            if (!talkHold) { talkHold = true; }
                            talkHoldAt = talkHoldAt > 0f && talkLogged ? talkHoldAt : Time.realtimeSinceStartup;
                            if (!talkLogged) { talkLogged = true; Plugin.Dbg($"[TALK] разговор: тело y={feetSend:F2} (держу {lastSendPos.y:F2}), x={pos.x:F2} z={pos.z:F2} (держу {lastSendPos.x:F2}, {lastSendPos.z:F2})"); }
                            pos = new Vector3(lastSendPos.x, pos.y, lastSendPos.z); feetSend = groundY;
                        }
                        else { talkLogged = false; lastSendPos = new Vector3(pos.x, feetSend, pos.z); haveSendPos = true; }
                        bool ratNow = LocalRat(); Vector3 ratUpNow = Vector3.up;
                        if (ratNow) RatPose(p, ref pos, ref feetSend, out ratUpNow);
                        var w = new NetDataWriter();
                        w.Put(MsgPos);
                        w.Put(pos.x);
                        w.Put(feetSend);
                        w.Put(pos.z);
                        w.Put(LookYaw(p));
                        byte fl = 0;
                        try { if (p.isCrouched) fl |= 1; if (p.isRunning) fl |= 2; } catch (Exception __e) { Err.Swallow(__e); }
                        bool fistsNow = FistsDrawn();
                        if (fistsNow) fl |= 4;
                        try { var fpc = FirstPersonItemController.Instance; if (fpc != null && fpc.isConsuming && !fpc.forceHolstered && !string.IsNullOrEmpty(curItem) && curItem != "nothing") fl |= 16; } catch (Exception __e) { Err.Swallow(__e); }
                        if (SittingNow(p)) fl |= 32;
                        if (InVent(p)) fl |= 8;
                        try { if (InteractionController.Instance != null && InteractionController.Instance.carryingObject != null) fl |= 64; } catch (Exception __e) { Err.Swallow(__e); }

                        w.Put(fl);
                        w.Put(LeanRoll());
                        w.Put(SeatIdNow());
                        float drunkNow = 0f; try { drunkNow = Mathf.Clamp01(p.drunk); } catch (Exception __e) { Err.Swallow(__e); }
                        w.Put((byte)Mathf.RoundToInt(drunkNow * 255f));
                        int elevId = -1; float elevDy = 0f;
                        try { ElevatorUnder(p, feetSend, out elevId, out elevDy); } catch (Exception __e) { Err.Swallow(__e); }
                        w.Put(elevId); w.Put(elevDy);
                        bool flashOn = false; try { var fpc = FirstPersonItemController.Instance; flashOn = fpc != null && fpc.flashlight; } catch (Exception __e) { Err.Swallow(__e); }
                        float pitch = 0f; try { var cm = Camera.main; if (cm != null) pitch = Mathf.DeltaAngle(0f, cm.transform.eulerAngles.x); } catch (Exception __e) { Err.Swallow(__e); }
                        w.Put(flashOn); w.Put(pitch);
                        w.Put(ratNow); if (ratNow) { w.Put(ratUpNow.x); w.Put(ratUpNow.y); w.Put(ratUpNow.z); }
                        manager.SendToAll(w, DeliveryMethod.Sequenced);
                        if (curItem != sentItem || Time.realtimeSinceStartup > itemSendAt)
                        {
                            sentItem = curItem;
                            itemSendAt = Time.realtimeSinceStartup + 3f;
                            var wi = new NetDataWriter();
                            wi.Put(MsgItem);
                            wi.Put(curItem ?? "");
                            manager.SendToAll(wi, DeliveryMethod.ReliableOrdered);
                        }
                        SendHeldIfChanged();
                    }
                }

                long tp0 = Perf.T();
                try { NpcTick(dt); } catch (Exception e) { if (!npcErrLogged) { npcErrLogged = true; Plugin.L.LogWarning($"[NPC] {e.Message}"); } }
                Perf.Add("жители", tp0); tp0 = Perf.T();
                try { ProfileTick(dt); } catch (Exception e) { if (!profErrLogged) { profErrLogged = true; Plugin.L.LogWarning($"[PROFILE] {e.Message}"); } }
                try { NameTick(p); } catch (Exception e) { Err.Swallow(e); }
                try { IllegalTick(p); } catch (Exception e) { Err.Swallow(e); }
                Perf.Add("профили", tp0); tp0 = Perf.T();
                try { RatGoodTick(p); } catch (Exception __e) { Err.Swallow(__e); }
                try { KoTick(p, dt); } catch (Exception e) { if (!koErrLogged) { koErrLogged = true; Plugin.L.LogWarning($"[KO] {e.Message}"); } }
                try { DownTick(p, dt); } catch (Exception e) { if (!downErrLogged) { downErrLogged = true; Plugin.L.LogWarning($"[DOWN] {e.Message}"); } }
                Perf.Add("нокаут и помощь", tp0); tp0 = Perf.T();
                try { ElevatorTick(); } catch (Exception e) { if (!elevErrLogged) { elevErrLogged = true; Plugin.L.LogWarning($"[LIFT] {e.Message}"); } }
                Perf.Add("лифты", tp0); tp0 = Perf.T();
                double jn0 = Perf.Enter();
                try { JobTick(); } catch (Exception e) { if (!jobErrLogged) { jobErrLogged = true; Plugin.L.LogWarning($"[JOB] {e}"); } }
                Perf.Leave("задания: прочее", tp0, jn0); tp0 = Perf.T();

                if (Plugin.CfgObjSync.Value)
                {
                    if (manager != null && manager.ConnectedPeersCount > 0) LocalObjects(dt);
                    RemoteObjectsTick(dt);
                    HiddenTick();
                    DoorTick();
                }
                Perf.Add("предметы и двери", tp0);

                for (int pi = 0; pi < players.Count; pi++)
                {
                    Enter(players[pi]);
                    if (avatar != null)
                    {
                        var tr = avatar.transform;
                        try { AvatarFlash(avatar, targetYaw); } catch (Exception e) { if (!flashErrLogged) { flashErrLogged = true; Plugin.L.LogWarning("[FLASH] " + e.Message); } }
                        Vector3 tp = targetPos + new Vector3(0f, Plugin.CfgAvatarYOffset.Value, 0f);
                        Quaternion tq = Quaternion.Euler(0f, targetYaw + Plugin.CfgAvatarYawOffset.Value, 0f);
                        seatSnapped = false;
                        if (remoteSitting && remoteSeatId >= 0)
                        {
                            Vector3 sp; float sy;
                            if (SeatPose(remoteSeatId, out sp, out sy))
                            {
                                Vector3 off = sp - targetPos; off.y = 0f;
                                if (off.sqrMagnitude < 0.8f * 0.8f)
                                {
                                    tp = sp + new Vector3(0f, Plugin.CfgAvatarYOffset.Value, 0f);
                                    tq = Quaternion.Euler(0f, sy + Plugin.CfgAvatarYawOffset.Value, 0f);
                                    seatSnapped = true;
                                }
                            }
                        }

                        tq = RatTilt(curRp, tq);
                        float ratK = RpRat(curRp) ? RatAvatarScale : 1f;
                        if (tr.localScale.y != 1f) tr.localScale = Vector3.one;
                        if (ratK < 1f) tp.y += Plugin.CfgAvatarYOffset.Value * (ratK - 1f);
                        bool inLift = false;
                        if (remoteElevId >= 0)
                        {
                            var el = ElevatorById(remoteElevId);
                            Transform cab = null; try { cab = el != null ? el.spawnedObject : null; } catch (Exception __e) { Err.Swallow(__e); }
                            if (cab != null) { tp.y = cab.position.y + remoteElevDy + Plugin.CfgAvatarYOffset.Value; inLift = true; }
                        }

                        Vector3 before = tr.position;
                        if (inLift && !avatarSnap) { var bp = tr.position; bp.y = tp.y; tr.position = bp; before = bp; }
                        if (avatarSnap || (tr.position - tp).sqrMagnitude > 25f)
                        {
                            avatarSnap = false;
                            tr.position = tp;
                            tr.rotation = tq;
                            animSpeed = 0f;
                        }
                        else
                        {
                            float k = 1f - Mathf.Exp(-15f * dt);
                            tr.position = Vector3.Lerp(tr.position, tp, k);
                            tr.rotation = Quaternion.Slerp(tr.rotation, tq, k);
                            Vector3 d = tr.position - before;
                            if (RpRat(curRp)) d = Vector3.ProjectOnPlane(d, curRp.ratUp) / RatAvatarScale;
                            else d.y = 0f;
                            float inst = dt > 0.0001f ? d.magnitude / dt : 0f;
                            animSpeed = Mathf.Lerp(animSpeed, inst, 1f - Mathf.Exp(-8f * dt));
                        }

                        if (avatarAnim != null)
                        {
                            float ms = Mathf.Clamp01(animSpeed / Plugin.CfgMaxSpeed.Value);
                            if (remoteRunning && animSpeed > 0.5f) ms = 1f;
                            if (ms < 0.03f) ms = 0f;
                            avatarAnim.SetFloat("moveSpeed", ms);
                            avatarAnim.SetFloat("drunk", remoteDrunk);
                            bool combat = remoteFists || Time.realtimeSinceStartup < combatUntil;
                            int cao = Plugin.CfgCombatArms.Value;
                            int armsSt = Plugin.CfgArmsState.Value;
                            if (remoteBlocking)
                            {
                                combat = true; cao = Plugin.CfgBlockCao.Value;
                                if (Plugin.CfgBlockArms.Value >= 0) armsSt = Plugin.CfgBlockArms.Value;
                            }
                            if (!remoteBlocking && remoteWeaponCao >= 0) { combat = true; cao = remoteWeaponCao; }
                            int itemArms = remoteBlocking ? -1 : ItemArms(remoteItem);
                            if (remoteConsuming && remoteHeld.Count > 0 && !combat && Plugin.CfgConsumeArms.Value >= 0) itemArms = Plugin.CfgConsumeArms.Value;
                            if (itemArms >= 0 && !combat) armsSt = itemArms;
                            if (Plugin.CfgDebugCycle.Value)
                            {
                                combat = true;
                                float t = Time.realtimeSinceStartup;
                                cao = (int)(t / 5f) % 6;
                                if (cao != lastCycleCao) { lastCycleCao = cao; Plugin.Dbg($"[ANIM] подбор: combatArmsOverride={cao}"); }
                                if (t >= nextCycleAttack)
                                {
                                    nextCycleAttack = t + 2f;
                                    avatarAnim.SetTrigger("attack");
                                }
                            }
                            avatarAnim.SetBool("inCombat", combat);
                            avatarAnim.SetInteger("combatArmsOverride", combat ? cao : 0);

                            int lc = avatarAnim.layerCount;
                            armsW = Mathf.MoveTowards(armsW, ((combat || itemArms >= 0 || (Plugin.CfgCarryAnim.Value && (remoteHeld.Count > 0 || remoteCarryObj))) && lc > 1) ? 1f : 0f, dt * 5f);
                            for (int li = 1; li < lc; li++)
                                avatarAnim.SetLayerWeight(li, li == 1 ? armsW : 0f);
                            bool carrying = Plugin.CfgCarryAnim.Value && !combat && ((remoteHeld.Count > 0 && (itemArms < 0 || remoteConsuming)) || remoteCarryObj);
                            avatarAnim.SetBool("carryingItem", carrying);
                            avatarAnim.SetInteger("carryItem", carrying ? (remoteCarryObj ? Plugin.CfgCarryObjValue.Value : Plugin.CfgCarryValue.Value) : 0);
                            avatarAnim.SetBool("armsLock", false);
                            avatarAnim.SetInteger("ArmsState", armsSt);
                            int idleVal = Plugin.CfgIdleState.Value;
                            paramSit = remoteSitting && Plugin.CfgSitIdleValue.Value >= 0;
                            int crouchVal = CrouchIdleValue();
                            paramCrouch = !paramSit && remoteCrouched && crouchVal >= 0 && animSpeed < 0.3f;
                            if (paramSit) idleVal = Plugin.CfgSitIdleValue.Value;
                            else if (paramCrouch) idleVal = crouchVal;
                            avatarAnim.SetInteger("IdleAnimationState", idleVal);

                            if (Plugin.CfgForceCombat.Value >= 0) avatarAnim.SetBool("inCombat", Plugin.CfgForceCombat.Value > 0);
                            if (Plugin.CfgForceCao.Value >= 0) avatarAnim.SetInteger("combatArmsOverride", Plugin.CfgForceCao.Value);
                            if (Plugin.CfgForceArms.Value >= 0) avatarAnim.SetInteger("ArmsState", Plugin.CfgForceArms.Value);
                            if (Plugin.CfgForceIdle.Value >= 0) avatarAnim.SetInteger("IdleAnimationState", Plugin.CfgForceIdle.Value);
                            string ls = Plugin.CfgForceLayers.Value ?? "";
                            if (ls != layersStr)
                            {
                                layersStr = ls;
                                var parts = ls.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                layersVal = new float[parts.Length];
                                for (int lk = 0; lk < parts.Length; lk++)
                                {
                                    float fv;
                                    float.TryParse(parts[lk], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv);
                                    layersVal[lk] = fv;
                                }
                            }
                            for (int lk = 0; lk < layersVal.Length && lk + 1 < lc; lk++)
                                avatarAnim.SetLayerWeight(lk + 1, layersVal[lk]);
                        }

                        if (avatarAnim != null) UpdatePose(dt);
                        if (avatarAnim != null) avatarAnim.Update(dt);

                        crouchK = Mathf.MoveTowards(crouchK, remoteCrouched ? 1f : 0f, dt * 6f);
                        crouchBoneK = Mathf.MoveTowards(crouchBoneK, (remoteCrouched && !poseAnimCrouch) ? 1f : 0f, dt * 6f);
                        crouchAnimK = Mathf.MoveTowards(crouchAnimK, (remoteCrouched && poseAnimCrouch) ? 1f : 0f, dt * 6f);
                        if (avatarModel != null)
                        {
                            sitK = Mathf.MoveTowards(sitK, poseAnimSit ? 1f : 0f, dt * 4f);
                            float ratM = RpRat(curRp) ? RatAvatarScale : 1f;
                            avatarModel.localScale = AvatarBaseScale() * Mathf.Clamp(Plugin.CfgAvatarHeight.Value, 0.3f, 2f) * ratM;
                            avatarModel.localPosition = modelBasePos * ratM + new Vector3(0f, -(Plugin.CfgDropWithAnim.Value * crouchAnimK + (seatSnapped ? 0f : Plugin.CfgSitDrop.Value) * sitK) * ratM, 0f);
                            if (ratM < 1f) RatAvatarDiag(curRp, avatar, targetPos);
                            leanSmooth = Mathf.Lerp(leanSmooth, remoteLean, 1f - Mathf.Exp(-10f * dt));
                            avatarModel.localRotation = Quaternion.Euler(0f, 0f, -leanSmooth * (poseAnimLean ? Plugin.CfgTiltWithAnim.Value : Plugin.CfgLeanScale.Value)) * modelBaseRot;
                        }
                        UpdateHeld();

                        CrouchBones(crouchBoneK);
                    }
                }
                Leave();
            }
            catch (Exception e)
            {
                Leave();
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.L.LogError($"[NET] Ошибка в Tick: {e}");
                }
            }
        }
    }
}

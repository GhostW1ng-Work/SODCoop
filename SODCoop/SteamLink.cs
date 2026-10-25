using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SodCoop
{
    internal static class SteamLink
    {
        const string Api = "steam_api64";
        [DllImport(Api)] static extern int SteamAPI_GetHSteamUser();
        [DllImport(Api)] static extern IntPtr SteamAPI_SteamUser_v023();
        [DllImport(Api)] static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
        [DllImport(Api)] static extern IntPtr SteamAPI_SteamFriends_v017();
        [DllImport(Api)] static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
        [DllImport(Api)] static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int i, int flags);
        [DllImport(Api)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong id);
        [DllImport(Api)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendRichPresence(IntPtr self, ulong id, byte[] key);
        [DllImport(Api)] static extern void SteamAPI_ISteamFriends_RequestFriendRichPresence(IntPtr self, ulong id);
        [DllImport(Api)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong id, byte[] info);
        [DllImport(Api)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamFriends_SetRichPresence(IntPtr self, byte[] key, byte[] value);
        [DllImport(Api)] static extern IntPtr SteamAPI_SteamNetworkingMessages_SteamAPI_v002();
        [DllImport(Api)] static extern int SteamAPI_ISteamNetworkingMessages_SendMessageToUser(IntPtr self, ref Identity who, byte[] data, uint len, int flags, int channel);
        [DllImport(Api)] static extern int SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(IntPtr self, int channel, IntPtr[] msgs, int max);
        [DllImport(Api)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(IntPtr self, IntPtr identity);
        [DllImport(Api)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(IntPtr self, ref Identity who);
        [DllImport(Api)] static extern void SteamAPI_SteamNetworkingMessage_t_Release(IntPtr msg);
        [DllImport(Api)] static extern IntPtr SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
        [DllImport(Api)] static extern void SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(IntPtr self);
        [DllImport(Api)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValuePtr(IntPtr self, int key, IntPtr value);
        [DllImport(Api)] static extern void SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialogConnectString(IntPtr self, byte[] connect);
        [DllImport(Api)] static extern IntPtr SteamAPI_SteamUtils_v010();
        [DllImport(Api)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamUtils_IsOverlayEnabled(IntPtr self);
        [DllImport("winmm")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm")] static extern uint timeEndPeriod(uint ms);

        [StructLayout(LayoutKind.Sequential, Size = 136)]
        struct Identity { public int type; public int size; public ulong id; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void SessionRequestFn(IntPtr req);

        const string PresenceKey = "sodcoop", JoinKey = "sodcoop_join", ConnectArg = "+sodcoop_connect";
        const int ChData = 0, ChHello = 1, SendFlags = 1 | 32, FriendFlagImmediate = 4;
        static readonly byte[] PresenceKeyB = Utf8(PresenceKey), JoinKeyB = Utf8(JoinKey), ConnectKeyB = Utf8("connect");

        static bool probed, ok;
        static IntPtr user, friends, msgs, utils;
        static ulong me;
        static SessionRequestFn sessionCb;
        static IntPtr sessionCbPtr;

        static Thread th;
        static volatile bool run;
        static volatile bool hosting;
        static bool udpTest;
        static int hostPort;
        static ulong hostId;
        static Socket udp, local;
        static readonly object gate = new object();
        static readonly Dictionary<long, Link> links = new Dictionary<long, Link>();
        static long sentPk, recvPk;

        sealed class Link { public ulong Peer; public ushort Tag; public Socket Sock; public float Seen; }

        static byte[] Utf8(string s) { var b = System.Text.Encoding.UTF8.GetBytes(s ?? ""); var r = new byte[b.Length + 1]; Array.Copy(b, r, b.Length); return r; }
        static string FromUtf8(IntPtr p) { try { return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? ""; } catch (Exception) { return ""; } }

        internal static bool Enabled { get { return Plugin.CfgSteam.Value; } }
        internal static bool TestUdp { get { return Plugin.CfgSteamTestUdp.Value; } }

        internal static bool Ready
        {
            get
            {
                if (probed) return ok;
                probed = true;
                try
                {
                    if (SteamAPI_GetHSteamUser() == 0) { Plugin.L.LogInfo("[STEAM] Steam не запущен - подключение через Steam недоступно"); return ok = false; }
                    user = SteamAPI_SteamUser_v023(); friends = SteamAPI_SteamFriends_v017();
                    msgs = SteamAPI_SteamNetworkingMessages_SteamAPI_v002(); utils = SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
                    if (user == IntPtr.Zero || friends == IntPtr.Zero || msgs == IntPtr.Zero || utils == IntPtr.Zero) { Plugin.L.LogWarning("[STEAM] интерфейсы Steam не найдены"); return ok = false; }
                    me = SteamAPI_ISteamUser_GetSteamID(user);
                    try { SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(utils); } catch (Exception e) { Plugin.L.LogWarning("[STEAM] relay: " + e.Message); }
                    sessionCb = OnSessionRequest;
                    sessionCbPtr = Marshal.GetFunctionPointerForDelegate(sessionCb);
                    bool cb = SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValuePtr(utils, 204, sessionCbPtr);
                    Plugin.L.LogInfo($"[STEAM] relay готов, мой SteamID {me}, приём подключений через обработчик: {(cb ? "да" : "нет")}");
                    ok = true;
                    RegisterJoin();
                    return true;
                }
                catch (Exception e) { Plugin.L.LogInfo("[STEAM] Steam API недоступен: " + e.Message); return ok = false; }
            }
        }

        internal static ulong MyId { get { return Ready ? me : 0; } }

        static SteamJoinCallback joinCb;
        static bool joinTried;

        static void RegisterJoin()
        {
            if (joinCb != null || joinTried) return;
            try
            {
                var dict = Steamworks.CallbackDispatcher.m_registeredCallbacks;
                if (dict == null) { Plugin.L.LogInfo("[STEAM] обработчик Steam ещё не готов - приглашения подключу позже"); return; }
                joinTried = true;
                ClassInjector.RegisterTypeInIl2Cpp<SteamJoinCallback>();
                var cb = new SteamJoinCallback();
                if (!dict.ContainsKey(SteamJoinCallback.Id)) dict[SteamJoinCallback.Id] = new Il2CppSystem.Collections.Generic.List<Steamworks.Callback>();
                dict[SteamJoinCallback.Id].Add(cb);
                joinCb = cb;
                Plugin.L.LogInfo("[STEAM] приглашения и «Присоединиться» из Steam подключены");
            }
            catch (Exception e) { joinTried = true; Plugin.L.LogWarning("[STEAM] не удалось подключить приглашения Steam: " + e.Message); }
        }

        static ulong pendingJoin;
        static float pendingUntil;
        static bool argsChecked;

        static ulong ParseConnect(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int i = s.IndexOf(ConnectArg, StringComparison.Ordinal);
            if (i < 0) return 0;
            var rest = s.Substring(i + ConnectArg.Length).Trim().Split(' ', '"');
            ulong id;
            return rest.Length > 0 && ulong.TryParse(rest[0], out id) ? id : 0;
        }

        internal static void OnJoinRequest(ulong friend, string connect)
        {
            ulong id = ParseConnect(connect);
            Plugin.L.LogInfo($"[STEAM] запрос из Steam присоединиться к {friend}: «{connect}»");
            if (id == 0) id = friend;
            QueueJoin(id);
        }

        static void QueueJoin(ulong id)
        {
            pendingJoin = id; pendingUntil = Time.realtimeSinceStartup + 20f;
            if (ok && friends != IntPtr.Zero) { try { SteamAPI_ISteamFriends_RequestFriendRichPresence(friends, id); } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static void JoinTick(float now)
        {
            if (!argsChecked)
            {
                argsChecked = true;
                try
                {
                    ulong id = ParseConnect(string.Join(" ", Environment.GetCommandLineArgs()));
                    if (id != 0) { Plugin.L.LogInfo($"[STEAM] игра запущена по приглашению Steam: хост {id}"); QueueJoin(id); pendingUntil = now + 120f; }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            if (!Ready) return;
            if (joinCb == null && !joinTried) RegisterJoin();
            if (pendingJoin == 0) return;
            bool menu = false, loading = false;
            try { var mm = MainMenuController.Instance; menu = mm != null && mm.mainMenuActive; loading = menu && mm.currentComponent != null && mm.currentComponent.component == MainMenuController.Component.loadingCity; } catch (Exception __e) { Err.Swallow(__e); }
            if (loading || (!menu && Player.Instance == null)) return;
            var sv = PresenceServer(pendingJoin, now);
            if (sv == null && now < pendingUntil) return;
            ulong host = pendingJoin; pendingJoin = 0;
            if (sv == null)
            {
                sv = new Lan.Server { Ip = "steam:" + host, Port = 0, Version = Plugin.ModVersion, Seen = now, Name = FriendName("steam:" + host) };
                Plugin.L.LogWarning($"[STEAM] не вижу сервер у {host} в статусе Steam - пробую подключиться всё равно");
            }
            NetMenu.JoinSteam(sv);
        }

        internal static void InviteFriends()
        {
            if (!Ready || !hosting || udpTest) { Net.Toast(UiKit.T("Host a game first: invites work while your server is running.", "Сначала создай сервер: приглашать можно, пока он запущен.")); return; }
            bool overlay = false;
            try { var u = SteamAPI_SteamUtils_v010(); overlay = u != IntPtr.Zero && SteamAPI_ISteamUtils_IsOverlayEnabled(u); } catch (Exception __e) { Err.Swallow(__e); }
            if (!overlay)
            {
                Net.Toast(UiKit.T("The Steam overlay is off. Invite from the Steam friends list: right-click a friend > Invite to game.", "Оверлей Steam выключен. Пригласи через список друзей Steam: ПКМ по другу > Пригласить в игру."));
                return;
            }
            try { SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialogConnectString(friends, Utf8(ConnectArg + " " + me)); Plugin.L.LogInfo("[STEAM] открыл окно приглашения Steam"); }
            catch (Exception e) { Plugin.L.LogWarning("[STEAM] окно приглашения: " + e.Message); }
        }

        internal static bool HostingViaSteam { get { return hosting && !udpTest && ok; } }

        static void OnSessionRequest(IntPtr req)
        {
            try
            {
                ulong from = (ulong)Marshal.ReadInt64(req, 8);
                bool accept = hosting || (hostId != 0 && from == hostId);
                if (accept) SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(msgs, req);
                Plugin.L.LogInfo($"[STEAM] запрос связи от {from}: {(accept ? "принят" : "отклонён")}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static bool IsSteamAddress(string addr) { return addr != null && (addr.StartsWith("steam:") || addr.StartsWith("steamtest:")); }

        static Identity Who(ulong id) { var i = new Identity(); i.type = 16; i.size = 8; i.id = id; return i; }

        static ulong UdpId(IPEndPoint ep) { var b = ep.Address.GetAddressBytes(); return ((ulong)BitConverter.ToUInt32(b, 0) << 16) | (ushort)ep.Port; }
        static IPEndPoint UdpEp(ulong id) { return new IPEndPoint(new IPAddress(BitConverter.GetBytes((uint)(id >> 16))), (int)(id & 0xFFFF)); }

        static void TransportSend(ulong peer, byte[] buf, int len, int ch)
        {
            if (udpTest)
            {
                if (ch != ChData) return;
                try { udp.SendTo(buf, 0, len, SocketFlags.None, UdpEp(peer)); } catch (Exception __e) { Err.Swallow(__e); }
                return;
            }
            var w = Who(peer);
            byte[] data = buf;
            if (len != buf.Length) { data = new byte[len]; Buffer.BlockCopy(buf, 0, data, 0, len); }
            SteamAPI_ISteamNetworkingMessages_SendMessageToUser(msgs, ref w, data, (uint)len, SendFlags, ch);
        }

        static readonly IntPtr[] rx = new IntPtr[64];
        static readonly byte[] rbuf = new byte[65536];

        static bool TransportReceive(Action<ulong, byte[], int> got)
        {
            bool any = false;
            if (udpTest)
            {
                EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                while (udp.Available > 0)
                {
                    int n = udp.ReceiveFrom(rbuf, ref ep);
                    got(UdpId((IPEndPoint)ep), rbuf, n); any = true;
                }
                return any;
            }
            int c = SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(msgs, ChData, rx, rx.Length);
            for (int i = 0; i < c; i++)
            {
                try
                {
                    IntPtr m = rx[i];
                    IntPtr data = Marshal.ReadIntPtr(m, 0);
                    int size = Marshal.ReadInt32(m, 8);
                    ulong from = (ulong)Marshal.ReadInt64(m, 0x18);
                    if (size > 0 && size <= rbuf.Length) { Marshal.Copy(data, rbuf, 0, size); got(from, rbuf, size); }
                }
                finally { SteamAPI_SteamNetworkingMessage_t_Release(rx[i]); }
                any = true;
            }
            c = SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(msgs, ChHello, rx, rx.Length);
            for (int i = 0; i < c; i++) SteamAPI_SteamNetworkingMessage_t_Release(rx[i]);
            return any;
        }

        static Socket LocalSock()
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            s.Blocking = false;
            try { s.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception __e) { Err.Swallow(__e); }
            return s;
        }

        internal static bool StartHost(int port)
        {
            Stop();
            udpTest = TestUdp;
            if (!Enabled && !udpTest) return false;
            if (!udpTest && !Ready) return false;
            try
            {
                if (udpTest)
                {
                    udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    udp.Bind(new IPEndPoint(IPAddress.Any, port + 1));
                    udp.Blocking = false;
                    try { udp.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception __e) { Err.Swallow(__e); }
                }
                hostPort = port; hostId = 0; hosting = true;
                Launch("host");
                Plugin.L.LogInfo(udpTest ? $"[STEAM] тестовый мост по UDP: принимаю на порту {port + 1}, передаю в игру на порт {port}" : $"[STEAM] хост доступен через Steam (SteamID {me}), передаю в игру на порт {port}");
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning("[STEAM] мост хоста: " + e.Message); Stop(); return false; }
        }

        internal static int StartClient(string addr, int port)
        {
            if (run && !hosting && local != null) return ((IPEndPoint)local.LocalEndPoint).Port;
            Stop();
            udpTest = addr.StartsWith("steamtest:");
            try
            {
                if (udpTest)
                {
                    var ip = IPAddress.Parse(addr.Substring("steamtest:".Length).Trim());
                    hostId = UdpId(new IPEndPoint(ip, port + 1));
                    udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    udp.Bind(new IPEndPoint(IPAddress.Any, 0));
                    udp.Blocking = false;
                    try { udp.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception __e) { Err.Swallow(__e); }
                }
                else
                {
                    if (!Ready) { Plugin.L.LogWarning("[STEAM] Steam недоступен - не могу подключиться"); return -1; }
                    if (!ulong.TryParse(addr.Substring("steam:".Length).Trim(), out hostId) || hostId == 0) { Plugin.L.LogWarning("[STEAM] неверный SteamID: " + addr); return -1; }
                    SetPresence(JoinKeyB, hostId.ToString());
                }
                local = LocalSock();
                hosting = false;
                Launch("client");
                int lp = ((IPEndPoint)local.LocalEndPoint).Port;
                Plugin.L.LogInfo(udpTest ? $"[STEAM] тестовый мост по UDP к {UdpEp(hostId)}, игра подключается к 127.0.0.1:{lp}" : $"[STEAM] подключаюсь через Steam к {hostId}, игра подключается к 127.0.0.1:{lp}");
                return lp;
            }
            catch (Exception e) { Plugin.L.LogWarning("[STEAM] мост клиента: " + e.Message); Stop(); return -1; }
        }

        static void Launch(string what)
        {
            run = true; sentPk = recvPk = 0;
            try { timeBeginPeriod(1); } catch (Exception __e) { Err.Swallow(__e); }
            th = new Thread(Loop) { IsBackground = true, Name = "SodCoop Steam " + what };
            th.Start();
        }

        internal static void Stop()
        {
            bool was = run;
            run = false;
            try { if (th != null && th != Thread.CurrentThread) th.Join(500); } catch (Exception __e) { Err.Swallow(__e); }
            th = null;
            lock (gate)
            {
                foreach (var l in links.Values) { try { l.Sock.Close(); } catch (Exception __e) { Err.Swallow(__e); } }
                links.Clear();
            }
            try { if (local != null) local.Close(); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (udp != null) udp.Close(); } catch (Exception __e) { Err.Swallow(__e); }
            local = null; udp = null;
            if (!udpTest && ok && hostId != 0 && !hosting)
            {
                try { var w = Who(hostId); SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(msgs, ref w); } catch (Exception __e) { Err.Swallow(__e); }
                SetPresence(JoinKeyB, "");
            }
            hosting = false; hostId = 0;
            if (was)
            {
                try { timeEndPeriod(1); } catch (Exception __e) { Err.Swallow(__e); }
                Plugin.L.LogInfo($"[STEAM] мост остановлен (отправлено пакетов {sentPk}, получено {recvPk})");
            }
        }

        static readonly byte[] sbuf = new byte[65536 + 2];
        static float clock;

        static void Loop()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (run)
            {
                bool any = false;
                clock = (float)sw.Elapsed.TotalSeconds;
                try
                {
                    any |= TransportReceive(FromRemote);
                    any |= hosting ? HostLocal() : ClientLocal();
                    if (hosting) DropIdle();
                }
                catch (SocketException) { }
                catch (Exception e) { Plugin.L.LogWarning("[STEAM] мост: " + e.Message); Thread.Sleep(50); }
                if (!any) Thread.Sleep(1);
            }
        }

        static void FromRemote(ulong from, byte[] buf, int n)
        {
            if (n < 2) return;
            ushort tag = (ushort)(buf[0] | (buf[1] << 8));
            recvPk++;
            if (hosting)
            {
                Link l;
                long key = ((long)from * 65599) ^ tag;
                lock (gate)
                {
                    if (!links.TryGetValue(key, out l))
                    {
                        l = new Link { Peer = from, Tag = tag, Sock = LocalSock() };
                        links[key] = l;
                        Plugin.L.LogInfo($"[STEAM] новый игрок {from} (сеанс {tag}) -> 127.0.0.1:{((IPEndPoint)l.Sock.LocalEndPoint).Port}");
                    }
                }
                l.Seen = clock;
                l.Sock.SendTo(buf, 2, n - 2, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, hostPort));
            }
            else
            {
                if (from != hostId || local == null) return;
                local.SendTo(buf, 2, n - 2, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, tag));
            }
        }

        static bool HostLocal()
        {
            bool any = false;
            List<Link> ls;
            lock (gate) ls = new List<Link>(links.Values);
            foreach (var l in ls)
            {
                EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                while (l.Sock.Available > 0)
                {
                    int n = l.Sock.ReceiveFrom(sbuf, 2, sbuf.Length - 2, SocketFlags.None, ref ep);
                    sbuf[0] = (byte)l.Tag; sbuf[1] = (byte)(l.Tag >> 8);
                    TransportSend(l.Peer, sbuf, n + 2, ChData); sentPk++; any = true;
                }
            }
            return any;
        }

        static bool ClientLocal()
        {
            bool any = false;
            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            while (local != null && local.Available > 0)
            {
                int n = local.ReceiveFrom(sbuf, 2, sbuf.Length - 2, SocketFlags.None, ref ep);
                ushort tag = (ushort)((IPEndPoint)ep).Port;
                sbuf[0] = (byte)tag; sbuf[1] = (byte)(tag >> 8);
                TransportSend(hostId, sbuf, n + 2, ChData); sentPk++; any = true;
            }
            return any;
        }

        static float idleAt;

        static void DropIdle()
        {
            if (clock < idleAt) return;
            idleAt = clock + 10f;
            lock (gate)
            {
                var dead = new List<long>();
                foreach (var kv in links) if (clock - kv.Value.Seen > 60f) dead.Add(kv.Key);
                foreach (var k in dead)
                {
                    var l = links[k];
                    try { l.Sock.Close(); } catch (Exception __e) { Err.Swallow(__e); }
                    links.Remove(k);
                    Plugin.L.LogInfo($"[STEAM] игрок {l.Peer} (сеанс {l.Tag}) молчит минуту - закрыл его канал");
                }
            }
        }

        static void SetPresence(byte[] key, string value)
        {
            try { if (ok) SteamAPI_ISteamFriends_SetRichPresence(friends, key, Utf8(value)); } catch (Exception __e) { Err.Swallow(__e); }
        }

        static string presenceNow = "";
        static float presenceAt, helloAt, friendsAt, requestAt;
        static readonly Dictionary<ulong, float> helloSent = new Dictionary<ulong, float>();

        internal static void Tick(bool menuOpen)
        {
            if (!Enabled) return;
            float now = Time.realtimeSinceStartup;
            if (now >= presenceAt)
            {
                presenceAt = now + 5f;
                string want = "";
                if (hosting && !udpTest && Net.HostingNow)
                    want = "1|" + Plugin.ModVersion + "|" + (string.IsNullOrEmpty(Net.RtPassword) ? "0" : "1") + "|" + Net.PlayerCount + "|" + Net.MaxPlayersPublic + "|" + Net.SeedInfo() + "|" + (Net.RtName ?? "");
                if (want != presenceNow && (want != "" || presenceNow != "") && Ready)
                {
                    SetPresence(PresenceKeyB, want);
                    SetPresence(ConnectKeyB, want != "" ? ConnectArg + " " + me : "");
                    if ((want == "") != (presenceNow == "")) Plugin.L.LogInfo(want != "" ? "[STEAM] статус для друзей выставлен: сервер виден друзьям в Steam" : "[STEAM] статус для друзей снят");
                    presenceNow = want;
                }
            }
            if (hosting && !udpTest && ok && now >= helloAt)
            {
                helloAt = now + 2f;
                try { HelloJoiners(now); } catch (Exception e) { Plugin.L.LogWarning("[STEAM] друзья: " + e.Message); }
            }
            try { JoinTick(now); } catch (Exception e) { pendingJoin = 0; Plugin.L.LogWarning("[STEAM] вход по приглашению: " + e.Message); }
            if (menuOpen && now >= friendsAt && Ready)
            {
                friendsAt = now + 3f;
                try { ScanFriends(now); } catch (Exception e) { Plugin.L.LogWarning("[STEAM] список друзей: " + e.Message); }
            }
        }

        static readonly byte[] gameInfo = new byte[32];

        static IEnumerable<ulong> FriendsInGame()
        {
            int n = SteamAPI_ISteamFriends_GetFriendCount(friends, FriendFlagImmediate);
            for (int i = 0; i < n; i++)
            {
                ulong id = SteamAPI_ISteamFriends_GetFriendByIndex(friends, i, FriendFlagImmediate);
                if (id == 0 || !SteamAPI_ISteamFriends_GetFriendGamePlayed(friends, id, gameInfo)) continue;
                yield return id;
            }
        }

        static void HelloJoiners(float now)
        {
            foreach (ulong id in FriendsInGame())
            {
                if (FromUtf8(SteamAPI_ISteamFriends_GetFriendRichPresence(friends, id, JoinKeyB)) != me.ToString()) continue;
                float at;
                if (helloSent.TryGetValue(id, out at) && now - at < 5f) continue;
                helloSent[id] = now;
                var w = Who(id); var b = new byte[] { 1 };
                SteamAPI_ISteamNetworkingMessages_SendMessageToUser(msgs, ref w, b, 1, SendFlags, ChHello);
                Plugin.Dbg($"[STEAM] друг {id} подключается ко мне - открываю ему связь");
            }
        }

        static void ScanFriends(float now)
        {
            bool req = now >= requestAt;
            if (req) requestAt = now + 10f;
            foreach (ulong id in FriendsInGame())
            {
                if (req) SteamAPI_ISteamFriends_RequestFriendRichPresence(friends, id);
                var sv = PresenceServer(id, now);
                if (sv == null) continue;
                int idx = Lan.Servers.FindIndex(s => s.Ip == sv.Ip);
                if (idx >= 0) Lan.Servers[idx] = sv; else Lan.Servers.Add(sv);
            }
        }

        static Lan.Server PresenceServer(ulong id, float now)
        {
            string v = FromUtf8(SteamAPI_ISteamFriends_GetFriendRichPresence(friends, id, PresenceKeyB));
            if (v == "") return null;
            var p = v.Split(new[] { '|' }, 7);
            if (p.Length < 7 || p[0] != "1") return null;
            var sv = new Lan.Server();
            sv.Version = p[1]; sv.Pw = p[2] == "1";
            int x; if (int.TryParse(p[3], out x)) sv.Players = x; if (int.TryParse(p[4], out x)) sv.Max = x;
            sv.Seed = p[5];
            sv.Name = p[6] + "  (Steam: " + FromUtf8(SteamAPI_ISteamFriends_GetFriendPersonaName(friends, id)) + ")";
            sv.Ip = "steam:" + id; sv.Port = 0; sv.Seen = now;
            return sv;
        }

        internal static string FriendName(string addr)
        {
            ulong id;
            if (addr == null || !addr.StartsWith("steam:") || !ulong.TryParse(addr.Substring(6), out id) || !Ready) return addr;
            try { string n = FromUtf8(SteamAPI_ISteamFriends_GetFriendPersonaName(friends, id)); return "Steam: " + (n != "" ? n : id.ToString()); } catch (Exception) { return addr; }
        }
    }
}

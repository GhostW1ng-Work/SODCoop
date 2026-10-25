using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        const byte MsgInvite = 45;
        static UiKit.Win inviteWin;
        static bool inviteOpen, inviteBuilt;
        static int inviteMode, inviteAddr = -1, inviteFrom = -1;
        static string inviteAddrName = "";
        static float inviteBtnAt;
        const string HostProfKey = "~host";
        static readonly Dictionary<int, int> residents = new Dictionary<int, int>();
        static bool residentsKnown;
        static int hintAskAddr = -1;

        internal static bool InviteOpen { get { return inviteOpen; } }

        static int CurrentOwnedAddr()
        {
            var p = Player.Instance;
            if (p == null || p.apartmentsOwned == null) return -1;
            var gl = p.currentGameLocation;
            var ad = gl != null ? gl.thisAsAddress : null;
            if (ad == null) return -1;
            foreach (var a in p.apartmentsOwned) if (a != null && a.id == ad.id) return ad.id;
            return -1;
        }

        static bool OwnsAddr(int addr)
        {
            var p = Player.Instance;
            try { if (p != null && p.apartmentsOwned != null) foreach (var a in p.apartmentsOwned) if (a != null && a.id == addr) return true; } catch (Exception __e) { Err.Swallow(__e); }
            return false;
        }

        static string AddrName(int id)
        {
            try { NewAddress ad; if (CityData.Instance.addressDictionary.TryGetValue(id, out ad) && ad != null) return ad.name ?? ""; } catch (Exception __e) { Err.Swallow(__e); }
            return "";
        }

        static string inviteKeyName = "";
        static KeyCode inviteKey = KeyCode.F7;
        static int inviteHintAddr = -1;

        static KeyCode InviteKey()
        {
            string s = Plugin.CfgInviteKey.Value ?? "F7";
            if (s != inviteKeyName) { inviteKeyName = s; KeyCode k; inviteKey = Enum.TryParse<KeyCode>(s, true, out k) ? k : KeyCode.F7; }
            return inviteKey;
        }

        static void InviteKeyTick()
        {
            if (manager == null || Player.Instance == null || inviteOpen || cheatOpen || NetMenu.Open || CharEditor.Open) return;
            try { var mm = MainMenuController.Instance; if (mm != null && mm.mainMenuActive) return; } catch (Exception __e) { Err.Swallow(__e); }
            float now = Time.realtimeSinceStartup;
            if (now >= inviteBtnAt)
            {
                inviteBtnAt = now + 1f;
                int here = OthersHere() && !GhostMe ? CurrentOwnedAddr() : -1;
                if (here >= 0 && here != inviteHintAddr)
                {
                    if (IsHost) { if (OwnerOf(here) == MyPid) HintToast(); }
                    else { hintAskAddr = here; SendInviteMsg(4, MyPid, 0, here, ""); }
                }
                inviteHintAddr = here;
            }
            if (KeyDown(InviteKey())) OnInviteButton();
        }

        static void OnInviteButton()
        {
            if (!OthersHere()) { Toast(UiKit.T("There are no other players in the game.", "Других игроков в игре нет.")); return; }
            if (GhostMe) return;
            int addr = CurrentOwnedAddr();
            if (addr < 0) { Toast(UiKit.T("You can only invite players into an apartment you own. Go inside it first.", "Пригласить можно только в свою квартиру. Сначала зайди в неё.")); return; }
            residents.Clear(); residentsKnown = false; hintAskAddr = -1;
            if (IsHost)
            {
                if (OwnerOf(addr) != MyPid) { NotOwnerToast(); return; }
                OpenInvite(1, addr, -1, AddrName(addr));
                HostResidents(MyPid, addr, residents); residentsKnown = true;
            }
            else { OpenInvite(1, addr, -1, AddrName(addr)); SendInviteMsg(4, MyPid, 0, addr, ""); }
        }

        static void HintToast() { Toast(UiKit.T($"Press {InviteKey()} to invite a player to live here.", $"Нажми {InviteKey()}, чтобы пригласить игрока жить здесь.")); }

        static void NotOwnerToast() { Toast(UiKit.T("Only the owner of the apartment can invite and evict residents.", "Приглашать и выселять жильцов может только хозяин квартиры.")); }

        static int ResidentRaw(int pid, int addr)
        {
            ProfileData pd = null;
            if (pid == 0)
            {
                var p = Player.Instance; bool owns = false;
                try { if (p != null && p.apartmentsOwned != null) foreach (var a in p.apartmentsOwned) if (a != null && a.id == addr) { owns = true; break; } } catch (Exception __e) { Err.Swallow(__e); }
                if (!owns) return 0;
                profiles.TryGetValue(HostProfKey, out pd);
                bool g = pd != null && pd.apts.Exists(a => a.id == addr && a.guest);
                return g ? 1 : 2;
            }
            var rp = RpByPid(pid);
            if (rp == null || string.IsNullOrEmpty(rp.profileId) || !profiles.TryGetValue(rp.profileId, out pd) || pd == null) return 0;
            var pa = pd.apts.Find(a => a.id == addr);
            return pa == null ? 0 : pa.guest ? 1 : 2;
        }

        static int OwnerOf(int addr)
        {
            if (ResidentRaw(0, addr) == 2) return 0;
            foreach (var rp in players) if (ResidentRaw(rp.pid, addr) == 2) return rp.pid;
            return -1;
        }

        static bool HostResidents(int asker, int addr, Dictionary<int, int> into)
        {
            into.Clear();
            int owner = OwnerOf(addr);
            Func<int, int> st = pid => ResidentRaw(pid, addr) == 0 ? 0 : pid == owner ? 2 : 1;
            if (asker != 0) into[0] = st(0);
            foreach (var rp in players) if (rp.pid != asker) into[rp.pid] = st(rp.pid);
            return owner == asker;
        }

        static void HostGuestNote(int addr, bool add)
        {
            ProfileData pd;
            if (!profiles.TryGetValue(HostProfKey, out pd) || pd == null) { if (!add) return; pd = new ProfileData(); pd.id = HostProfKey; profiles[HostProfKey] = pd; }
            pd.apts.RemoveAll(a => a.id == addr);
            if (add) pd.apts.Add(new ProfApt { id = addr, guest = true });
            profDirty = true;
        }

        static void SendEvict(int target)
        {
            SendInviteMsg(6, MyPid, target, inviteAddr, inviteAddrName);
            if (IsHost) HostEvictNote(target, inviteAddr);
            Plugin.L.LogInfo($"[ROOMMATE] выселяю игрока {target} из квартиры (адрес {inviteAddr})");
            Toast(UiKit.T($"{PlayerLabel(target)} was evicted.", $"{PlayerLabel(target)} выселен."));
            CloseInvite();
        }

        static void HostEvictNote(int target, int addr)
        {
            if (target == 0) { HostGuestNote(addr, false); return; }
            var rp = RpByPid(target);
            ProfileData pd;
            if (rp == null || string.IsNullOrEmpty(rp.profileId) || !profiles.TryGetValue(rp.profileId, out pd) || pd == null) return;
            if (pd.apts.RemoveAll(a => a.id == addr) > 0) { profDirty = true; Plugin.L.LogInfo($"[PROFILE] квартира (адрес {addr}) убрана у игрока {target}"); }
        }

        static void MoveOut(int addr, int byPid)
        {
            var p = Player.Instance;
            NewAddress ad = null;
            try { CityData.Instance.addressDictionary.TryGetValue(addr, out ad); } catch (Exception __e) { Err.Swallow(__e); }
            if (p == null || ad == null) return;
            try { if (p.apartmentsOwned != null) for (int i = p.apartmentsOwned.Count - 1; i >= 0; i--) { var a = p.apartmentsOwned[i]; if (a != null && a.id == addr) p.apartmentsOwned.RemoveAt(i); } } catch (Exception e) { Plugin.L.LogWarning("[ROOMMATE] выселение: " + e.Message); }
            try { if (ad.entrances != null) foreach (var en in ad.entrances) if (en != null && en.door != null) p.RemoveFromKeyring(en.door); } catch (Exception e) { Plugin.L.LogWarning("[ROOMMATE] ключи: " + e.Message); }
            try
            {
                if (AddrIdOf(p.residence) == addr)
                {
                    NewAddress other = null;
                    if (p.apartmentsOwned != null) foreach (var a in p.apartmentsOwned) if (a != null && a.residence != null) { other = a; break; }
                    if (other != null) p.SetResidence(other.residence, false); else p.residence = null;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[ROOMMATE] дом: " + e.Message); }
            Plugin.L.LogInfo($"[ROOMMATE] меня выселил игрок {byPid} из квартиры (адрес {addr}) {ad.name}, дом теперь {AddrIdOf(p.residence)}");
            Toast(UiKit.T($"{PlayerLabel(byPid)} evicted you from {ad.name}.", $"{PlayerLabel(byPid)} выселил вас: {ad.name}."));
        }

        static void OpenInvite(int mode, int addr, int from, string name)
        {
            if (cheatOpen) CloseCheatMenu();
            inviteMode = mode; inviteAddr = addr; inviteFrom = from; inviteAddrName = name ?? "";
            inviteOpen = true; inviteBuilt = false;
            CheatInput(false);
        }

        static void CloseInvite()
        {
            inviteOpen = false;
            if (inviteWin != null && inviteWin.Alive) UiKit.Show(inviteWin, false);
            CheatInput(true);
        }

        internal static void InviteUiTick()
        {
            try { InviteKeyTick(); } catch (Exception e) { Plugin.L.LogWarning("[ROOMMATE] клавиша: " + e.Message); inviteBtnAt = Time.realtimeSinceStartup + 5f; }
            if (!inviteOpen) return;
            if (!UiKit.Init()) return;
            bool menu = false; try { var mm = MainMenuController.Instance; menu = mm != null && mm.mainMenuActive; } catch (Exception __e) { Err.Swallow(__e); }
            if (menu || Player.Instance == null || manager == null || manager.ConnectedPeersCount == 0) { if (inviteMode == 2) AnswerInvite(false); else CloseInvite(); return; }
            if (KeyDown(KeyCode.Escape)) { if (inviteMode == 2) AnswerInvite(false); else CloseInvite(); return; }
            if (inviteWin == null || !inviteWin.Alive) { inviteWin = UiKit.MakeWindow("SodCoop InvitePanel", "", 900f, 620f, true); inviteBuilt = false; }
            if (!inviteBuilt) { BuildInvite(); inviteBuilt = true; }
            if (!inviteWin.Root.activeSelf) UiKit.Show(inviteWin, true);
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        static void BuildInvite()
        {
            var w = inviteWin; var c = w.Content;
            UiKit.Clear(c); UiKit.Clear(w.Buttons);
            float W = c.rect.width > 100f ? c.rect.width : 772f;
            if (inviteMode == 1)
            {
                w.Title.text = UiKit.T("Invite player", "Пригласить игрока");
                UiKit.Label(c, UiKit.T("Who do you want to move in with you?", "Кого поселить к себе?") + (inviteAddrName != "" ? "\n" + inviteAddrName : ""), 30f, 16f, W - 60f, 70f, 24f, TMPro.TextAlignmentOptions.Center, Color.white);
                float y = 100f; int n = 0;
                if (!residentsKnown) { UiKit.Label(c, UiKit.T("Checking who lives here...", "Проверяю, кто здесь живёт..."), 30f, y, W - 60f, 50f, 22f, TMPro.TextAlignmentOptions.Center, UiKit.HeadColor); n = 1; }
                else foreach (var rp in players)
                {
                    if (rp.pid == MyPid) continue;
                    int pid = rp.pid, st;
                    residents.TryGetValue(pid, out st);
                    string name = PlayerLabel(pid);
                    if (st == 0) UiKit.Button(c, UiKit.T($"Invite {name}", $"Пригласить: {name}"), 60f, y, W - 120f, 52f, () => SendInvite(pid), 24f);
                    else UiKit.Button(c, UiKit.T($"Evict {name}", $"Выселить: {name}"), 60f, y, W - 120f, 52f, () => SendEvict(pid), 24f);
                    y += 62f; n++;
                }
                if (n == 0) UiKit.Label(c, UiKit.T("No other players in the game.", "Других игроков в игре нет."), 30f, y, W - 60f, 50f, 22f, TMPro.TextAlignmentOptions.Center, UiKit.HeadColor);
                UiKit.BarButton(w, UiKit.T("Cancel", "Отмена"), 300f, () => CloseInvite());
            }
            else
            {
                w.Title.text = UiKit.T("Invitation", "Приглашение");
                string who = PlayerLabel(inviteFrom);
                string q = UiKit.T($"{who} invites you to move in with them.", $"Игрок {who} предлагает вам поселиться к нему.");
                if (inviteAddrName != "") q += "\n" + inviteAddrName;
                UiKit.Label(c, q, 30f, 60f, W - 60f, 160f, 26f, TMPro.TextAlignmentOptions.Center, Color.white);
                UiKit.BarButton(w, UiKit.T("Yes", "Да"), 260f, () => AnswerInvite(true));
                UiKit.BarButton(w, UiKit.T("No", "Нет"), 260f, () => AnswerInvite(false));
            }
        }

        static void SendInviteMsg(byte op, int from, int target, int addr, string name)
        {
            if (manager == null) return;
            var w = new NetDataWriter();
            w.Put(MsgInvite); w.Put(op); w.Put((byte)from); w.Put((byte)target); w.Put(addr); w.Put(name ?? "");
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void SendInvite(int target)
        {
            SendInviteMsg(1, MyPid, target, inviteAddr, inviteAddrName);
            Plugin.L.LogInfo($"[ROOMMATE] приглашаю игрока {target} в квартиру (адрес {inviteAddr}) {inviteAddrName}");
            Toast(UiKit.T($"Invitation sent to {PlayerLabel(target)}.", $"Приглашение отправлено: {PlayerLabel(target)}."));
            CloseInvite();
        }

        static void AnswerInvite(bool yes)
        {
            int from = inviteFrom, addr = inviteAddr; string name = inviteAddrName;
            CloseInvite();
            if (yes && !MoveIn(addr)) yes = false;
            SendInviteMsg((byte)(yes ? 2 : 3), MyPid, from, addr, name);
            Plugin.L.LogInfo($"[ROOMMATE] приглашение игрока {from} в квартиру (адрес {addr}): {(yes ? "согласился" : "отказался")}");
        }

        static bool MoveIn(int addr)
        {
            var p = Player.Instance;
            NewAddress ad = null;
            try { CityData.Instance.addressDictionary.TryGetValue(addr, out ad); } catch (Exception __e) { Err.Swallow(__e); }
            if (p == null || ad == null || ad.residence == null)
            {
                Plugin.L.LogWarning($"[ROOMMATE] квартира (адрес {addr}) не найдена");
                Toast(UiKit.T("Could not move in: the apartment was not found.", "Не получилось заселиться: квартира не найдена."));
                return false;
            }
            var prev = p.residence;
            soldAddrs.Add(addr);
            p.SetResidence(ad.residence, false);
            if (prev != null && AddrIdOf(prev) != addr) p.SetResidence(prev, false);
            try { p.AddToKeyring(ad, false); } catch (Exception e) { Plugin.L.LogWarning("[ROOMMATE] ключи: " + e.Message); }
            Plugin.L.LogInfo($"[ROOMMATE] я заселился в квартиру (адрес {addr}) {ad.name}, дом остался {AddrIdOf(p.residence)}");
            Toast(UiKit.T($"You moved in: {ad.name}. You have the keys and can use it like your own.", $"Вы заселились: {ad.name}. Ключи у вас, квартира теперь и ваша."));
            return true;
        }

        static void OnInviteMsg(NetDataReader r)
        {
            byte op = r.GetByte(); int from = r.GetByte(); int target = r.GetByte(); int addr = r.GetInt(); string name = r.GetString();
            if (IsHost && op == 2)
            {
                var rp = RpByPid(from);
                List<int[]> rems; soldRemovals.TryGetValue(addr, out rems);
                if (rp != null) ProfileNoteApt(rp, addr, rems, true);
                else if (from == 0) HostGuestNote(addr, true);
            }
            if (IsHost && op == 6 && from != MyPid) HostEvictNote(target, addr);
            if (IsHost && op == 7 && OwnerOf(addr) == target)
            {
                var rp = RpByPid(from);
                List<int[]> rems; soldRemovals.TryGetValue(addr, out rems);
                if (rp != null) ProfileNoteApt(rp, addr, rems, true);
                else if (from == 0) HostGuestNote(addr, true);
            }
            if (IsHost && op == 4)
            {
                var into = new Dictionary<int, int>();
                bool g = HostResidents(from, addr, into);
                if (manager == null) return;
                var w = new NetDataWriter();
                w.Put(MsgInvite); w.Put((byte)5); w.Put((byte)0); w.Put((byte)from); w.Put(addr); w.Put("");
                w.Put(g); w.Put(into.Count); foreach (var kv in into) { w.Put(kv.Key); w.Put(kv.Value); }
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                return;
            }
            if (target != MyPid) return;
            if (op == 5)
            {
                bool own = r.GetBool();
                var got = new Dictionary<int, int>();
                int n = r.GetInt();
                for (int i = 0; i < n; i++) { int pid = r.GetInt(); got[pid] = r.GetInt(); }
                if (inviteOpen && inviteMode == 1 && addr == inviteAddr)
                {
                    if (!own) { CloseInvite(); NotOwnerToast(); return; }
                    residents.Clear(); foreach (var kv in got) residents[kv.Key] = kv.Value;
                    residentsKnown = true; inviteBuilt = false;
                }
                else if (addr == hintAskAddr) { hintAskAddr = -1; if (own) HintToast(); }
                return;
            }
            if (op == 6) { MoveOut(addr, from); return; }
            if (op == 1)
            {
                if (OwnsAddr(addr)) { SendInviteMsg(7, MyPid, from, addr, name); return; }
                Plugin.L.LogInfo($"[ROOMMATE] игрок {from} приглашает меня в квартиру (адрес {addr}) {name}");
                OpenInvite(2, addr, from, name);
            }
            else if (op == 2) Toast(UiKit.T($"{PlayerLabel(from)} moved in with you.", $"{PlayerLabel(from)} поселился у вас."));
            else if (op == 7) Toast(UiKit.T($"{PlayerLabel(from)} already lives here - now listed as your roommate.", $"{PlayerLabel(from)} уже живёт здесь - теперь записан как ваш жилец."));
            else if (op == 3) Toast(UiKit.T($"{PlayerLabel(from)} declined the invitation.", $"{PlayerLabel(from)} отказался от приглашения."));
        }
    }
}

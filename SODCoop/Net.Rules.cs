using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        internal static bool ClientOfHost { get { return !IsHost && RtMode == NetMode.Client && serverPeer != null; } }

        static string rulesSent = "";
        static float rulesAt;
        static readonly HashSet<int> localSubmitted = new HashSet<int>();

        static void SendRules(NetPeer peer)
        {
            var w = new NetDataWriter(); w.Put(MsgRules); Rules.Write(w);
            if (peer != null) peer.Send(w, DeliveryMethod.ReliableOrdered); else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void RulesTick()
        {
            if (!IsHost || manager == null || manager.ConnectedPeersCount == 0 || Time.realtimeSinceStartup < rulesAt) return;
            rulesAt = Time.realtimeSinceStartup + 2f;
            string sig = Rules.Signature();
            if (sig == rulesSent) return;
            rulesSent = sig;
            SendRules(null);
            Plugin.L.LogInfo("[RULES] правила сервера разосланы: " + Rules.Summary());
        }

        static int CaseKey(Case c) { return c != null && c.jobReference > 0 ? c.jobReference : -1; }

        internal static double SocialNow() { return GcGet("socialCredit"); }

        static bool RewardRuleActive { get { return Rules.RewardOnlyHandIn && manager != null && manager.ConnectedPeersCount > 0 && RtMode != NetMode.Off; } }

        internal static void ApplyRewardRule(Case c, double money0, double social0)
        {
            if (!RewardRuleActive || c == null) return;
            int key = CaseKey(c);
            if (localSubmitted.Contains(key)) { Plugin.L.LogInfo($"[RULES] дело '{c.name}' сдал я - награда моя"); return; }
            double m1 = MoneyNow(), s1 = SocialNow();
            if (m1 > money0) GcSet("money", "SetMoney", money0);
            if (s1 > social0) GcSet("socialCredit", "SetSocialCredit", social0);
            Plugin.L.LogInfo($"[RULES] дело '{c.name}' сдал другой игрок - награду ({m1 - money0:F0} денег, {s1 - social0:F0} рейтинга) не получаю");
        }

        internal static bool SkipJobReward(SideJob j)
        {
            if (!RewardRuleActive || j == null || localSubmitted.Contains(j.jobID)) return false;
            Plugin.L.LogInfo($"[RULES] задание {j.jobID} сдал другой игрок - награду в почтовый ящик мне не кладу");
            return true;
        }
    }

    internal static class Rules
    {
        static bool fromHost;
        static bool pvp, downed, clean, neverPause, cheats, onlyHandIn;
        static float downSec, holdSec, revHealth, hospSec;
        static int money, picks;

        static bool UseHost { get { return fromHost && Net.ClientOfHost; } }

        internal static bool Pvp { get { return UseHost ? pvp : Plugin.CfgPvp.Value; } }
        internal static bool Downed { get { return UseHost ? downed : Plugin.CfgDowned.Value; } }
        internal static float DownedSeconds { get { return UseHost ? downSec : Plugin.CfgDownedSeconds.Value; } }
        internal static float ReviveHoldSeconds { get { return UseHost ? holdSec : Plugin.CfgReviveHoldSeconds.Value; } }
        internal static float ReviveHealth { get { return UseHost ? revHealth : Plugin.CfgReviveHealth.Value; } }
        internal static float HospitalSeconds { get { return UseHost ? hospSec : Plugin.CfgHospitalSeconds.Value; } }
        internal static int NewPlayerMoney { get { return UseHost ? money : Plugin.CfgNewPlayerMoney.Value; } }
        internal static int NewPlayerLockpicks { get { return UseHost ? picks : Plugin.CfgNewPlayerLockpicks.Value; } }
        internal static bool NewPlayerClean { get { return UseHost ? clean : Plugin.CfgNewPlayerClean.Value; } }
        internal static bool WorldNeverPauses { get { return UseHost ? neverPause : Plugin.CfgWorldNeverPauses.Value; } }
        internal static bool ClientCheats { get { return UseHost ? cheats : Plugin.CfgClientCheats.Value; } }
        internal static bool RewardOnlyHandIn { get { return UseHost ? onlyHandIn : Plugin.CfgRewardOnlyHandIn.Value; } }

        internal static void Write(NetDataWriter w)
        {
            w.Put(Plugin.CfgPvp.Value); w.Put(Plugin.CfgDowned.Value); w.Put(Plugin.CfgDownedSeconds.Value); w.Put(Plugin.CfgReviveHoldSeconds.Value);
            w.Put(Plugin.CfgReviveHealth.Value); w.Put(Plugin.CfgHospitalSeconds.Value); w.Put(Plugin.CfgNewPlayerMoney.Value); w.Put(Plugin.CfgNewPlayerLockpicks.Value);
            w.Put(Plugin.CfgNewPlayerClean.Value); w.Put(Plugin.CfgWorldNeverPauses.Value); w.Put(Plugin.CfgClientCheats.Value); w.Put(Plugin.CfgRewardOnlyHandIn.Value);
            Net.WriteModifiers(w);
        }

        internal static string Signature()
        {
            var w = new NetDataWriter(); Write(w);
            return Convert.ToBase64String(w.CopyData());
        }

        internal static void Read(NetDataReader r)
        {
            bool first = !fromHost;
            pvp = r.GetBool(); downed = r.GetBool(); downSec = r.GetFloat(); holdSec = r.GetFloat();
            revHealth = r.GetFloat(); hospSec = r.GetFloat(); money = r.GetInt(); picks = r.GetInt();
            clean = r.GetBool(); neverPause = r.GetBool(); cheats = r.GetBool(); onlyHandIn = r.GetBool();
            fromHost = true;
            if (r.AvailableBytes > 0) { try { Net.ReadModifiers(r); } catch (Exception e) { Plugin.L.LogWarning("[MODS] модификаторы хоста: " + e.Message); } }
            Plugin.L.LogInfo("[RULES] правила хоста: " + Summary());
            if (first) Net.Toast(UiKit.T("Server rules: ", "Правила сервера: ") + Summary());
        }

        internal static void Clear() { fromHost = false; }

        internal static string Summary()
        {
            string on = UiKit.T("on", "вкл"), off = UiKit.T("off", "выкл");
            return UiKit.T("PvP ", "урон между игроками ") + (Pvp ? on : off)
                + "; " + UiKit.T("revive ", "помощь упавшему ") + (Downed ? DownedSeconds.ToString("0") + UiKit.T(" s", " с") : off)
                + "; " + UiKit.T("reward ", "награда ") + (RewardOnlyHandIn ? UiKit.T("to who hands in", "только сдавшему") : UiKit.T("to everyone", "всем"))
                + "; " + UiKit.T("cheats ", "читы ") + (ClientCheats ? on : off);
        }
    }
}

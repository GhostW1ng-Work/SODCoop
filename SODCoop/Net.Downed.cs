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
        static bool applyingTimeSpeed, koActive, koRecovery, koErrLogged;
        static float koStartAt, koLogAt, koLogGt;
        static int koClamped;
        static SessionData.TimeSpeed koPrevSpeed = SessionData.TimeSpeed.normal;

        internal static bool LocalKo()
        {
            if (manager == null || !Plugin.CfgKoNoTimeSkip.Value) return false;
            try { var p = Player.Instance; return p != null && p.playerKOInProgress; } catch (Exception) { return false; }
        }

        static SessionData.TimeSpeed SharedTimeSpeed(SessionData sd)
        {
            return koActive && LocalKo() ? koPrevSpeed : sd.currentTimeSpeed;
        }

        internal static void AfterTimeSpeed(SessionData.TimeSpeed spd)
        {
            if (applyingTimeSpeed || !LocalKo()) return;
            ClampKoTime("SetTimeSpeed(" + spd + ")");
        }

        static void ClampKoTime(string why)
        {
            var sd = SessionData.Instance;
            if (sd == null) return;
            bool did = false;
            if (sd.currentTimeMultiplier > 1.01f) { sd.currentTimeMultiplier = 1f; did = true; }
            if (Time.timeScale > 1.01f) { Time.timeScale = 1f; did = true; }
            if (did && ++koClamped <= 3) Plugin.L.LogInfo($"[KO] {why}: игра ускорила время, держу x1 (скорость в игре {sd.currentTimeSpeed}, для других {koPrevSpeed})");
        }

        static string KoBar()
        {
            try
            {
                var ic = InteractionController.Instance;
                if (ic == null) return "?";
                return $"{ic.interactionActionName}:{(ic.activeInteractionAction ? "идёт" : "нет")} {ic.interactionActionAmount:F3}/{ic.interactionActionThreshold:F3}";
            }
            catch (Exception) { return "?"; }
        }

        static bool koSeen;

        static void KoTick(Player p, float dt)
        {
            if (p == null) return;
            bool ko = p.playerKOInProgress, rec = p.KORecovery;
            if (ko && !koSeen && !allowKo && !GhostMe && !permaOut && PermaOn && manager != null)
            {
                Plugin.L.LogInfo("[MODS] нокаут прошёл мимо проверки - применяю перманентную смерть");
                try { PermaDeathOut(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] " + e.Message); }
            }
            if (ko) koSeen = true;
            else if (koSeen)
            {
                koSeen = false;
                if (fxLastSent == 2 && !downed && !GhostMe) { SendBodyFx(3, Vector3.zero); Plugin.L.LogInfo("[KO] игра подняла меня без восстановления в больнице - сообщаю другим, что я встал"); }
            }
            if (!Plugin.CfgKoNoTimeSkip.Value) return;
            float now = Time.realtimeSinceStartup;
            var sd = SessionData.Instance;
            if (!ko)
            {
                if (koActive)
                {
                    koActive = false;
                    if (sd != null && sd.currentTimeSpeed != koPrevSpeed)
                    {
                        applyingTimeSpeed = true;
                        try { sd.SetTimeSpeed(koPrevSpeed); } finally { applyingTimeSpeed = false; }
                    }
                    Plugin.L.LogInfo($"[KO] игрок пришёл в себя, фаза длилась {now - koStartAt:F0} с, здоровье {p.currentHealth:F2}/{p.maximumHealth:F2}, скорость {(sd != null ? "" + sd.currentTimeSpeed : "?")}");
                }
                return;
            }
            if (!koActive || rec != koRecovery)
            {
                if (!koActive && sd != null) koPrevSpeed = sd.currentTimeSpeed == SessionData.TimeSpeed.veryFast ? SessionData.TimeSpeed.normal : sd.currentTimeSpeed;
                koActive = true; koRecovery = rec; koStartAt = now; koLogAt = now; koClamped = 0; koLogGt = sd != null ? sd.gameTime : 0f;
                Plugin.L.LogInfo(rec ? $"[KO] игрок встаёт после больницы (KOTime={p.KOTime:F2})" : $"[KO] игрок без сознания, лечится без перемотки времени (KOTime={p.KOTime:F2})");
            }
            ClampKoTime("кадр");
            float total = rec ? 5f : Rules.HospitalSeconds;
            float el = now - koStartAt;
            if (now >= koLogAt && sd != null)
            {
                koLogAt = now + 5f;
                Plugin.L.LogInfo($"[KO] {(rec ? "подъём" : "больница")} {el:F0}/{total + 3f:F0} с: KOTimePassed={p.KOTimePassed:F3} KOTime={p.KOTime:F3} hp={p.currentHealth:F2}/{p.maximumHealth:F2} скорость={sd.currentTimeSpeed} x{sd.currentTimeMultiplier:F2} timeScale={Time.timeScale:F2} время +{(sd.gameTime - koLogGt) * 60f:F1} игр. мин | затемнение={p.playerKOFadeOut} полоска={KoBar()}");
                koLogGt = sd.gameTime;
            }
            float max = p.maximumHealth;
            if (max > 0f && p.currentHealth > max) p.SetHealth(max);
            if (rec && Plugin.CfgKoInstantGetUp.Value)
            {
                float kt = p.KOTime;
                if (kt > 0f && p.KOTimePassed < kt - 0.0005f) p.KOTimePassed = kt - 0.0005f;
                var ic = InterfaceController.Instance;
                if (ic != null && ic.fade != ic.desiredFade) ic.fade = ic.desiredFade;
                return;
            }
            if (el < 3f) return;
            float koTime = p.KOTime;
            if (total < 1f || koTime <= 0f) return;
            float passed = p.KOTimePassed;
            float goal = koTime - 0.0005f;
            if (passed < goal) { passed = Mathf.Min(goal, passed + koTime * dt / total); p.KOTimePassed = passed; }
            if (!rec)
            {
                float target = max * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(passed / koTime));
                if (p.currentHealth < target) p.SetHealth(Mathf.Min(target, max));
            }
        }

        static readonly string[] DownedActions = { "Get In", "Get Up", "Enter", "Sleep..." };
        static bool downed, allowKo, downErrLogged;
        static float downUntil, downFreezeAt, downSentAt, reviveProgress, reviveSentAt;
        static Vector3 downDir;
        static int reviveTarget = -1;
        static RP nearDowned;
        static readonly System.Collections.Generic.Dictionary<int, float> revivers = new System.Collections.Generic.Dictionary<int, float>();
        static System.Reflection.MethodInfo keyHeldMethod;
        static KeyCode reviveKey = KeyCode.None; static string reviveKeyName = "";
        static GUIStyle downStyle, downShadow;

        static KeyCode ReviveKey()
        {
            string s = Plugin.CfgReviveKey.Value ?? "H";
            if (s != reviveKeyName) { reviveKeyName = s; KeyCode k; reviveKey = Enum.TryParse<KeyCode>(s, true, out k) ? k : KeyCode.H; }
            return reviveKey;
        }

        static bool KeyHeld(KeyCode k)
        {
            if (keyHeldMethod == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { var t = asm.GetType("UnityEngine.Input"); if (t == null) continue; keyHeldMethod = t.GetMethod("GetKey", new[] { typeof(KeyCode) }); if (keyHeldMethod != null) break; }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
                if (keyHeldMethod == null) return false;
            }
            return (bool)keyHeldMethod.Invoke(null, new object[] { k });
        }

        internal static bool KoAllowed { get { return allowKo; } }

        static bool OthersHere() { return manager != null && manager.ConnectedPeersCount > 0 && players.Count > 0; }

        internal static bool TryGoDown(Vector3 dir)
        {
            if (GhostMe) return true;
            if (allowKo) return false;
            if (downed) return true;
            if (!Rules.Downed || !OthersHere()) return false;
            var p = Player.Instance;
            if (p == null || p.playerKOInProgress) return false;
            downed = true; downDir = dir; revivers.Clear();
            float now = Time.realtimeSinceStartup;
            downUntil = now + Mathf.Max(1f, Rules.DownedSeconds); downSentAt = 0f; downFreezeAt = 0f;
            try { float max = p.maximumHealth; if (p.currentHealth < max * 0.05f) p.SetHealth(max * 0.05f); } catch (Exception __e) { Err.Swallow(__e); }
            DownFreeze(p, true);
            SendBodyFx(2, Vector3.zero);
            SendDown(1);
            Toast($"You are down! Wait for help ({Rules.DownedSeconds:F0} s).");
            Plugin.L.LogInfo($"[DOWN] я лежу, жду помощи {Rules.DownedSeconds:F0} с");
            return true;
        }

        internal static bool DownedHit(float amount)
        {
            if (!downed || amount <= 0f) return false;
            downUntil -= Mathf.Max(0f, Plugin.CfgDownedHitPenalty.Value);
            return true;
        }

        static void DownFreeze(Player p, bool on)
        {
            try { p.EnablePlayerMovement(!on, true); } catch (Exception e) { if (!downErrLogged) { downErrLogged = true; Plugin.L.LogWarning("[DOWN] движение: " + e.Message); } }
            foreach (var a in DownedActions) { try { p.SetActionDisable(a, on); } catch (Exception __e) { Err.Swallow(__e); } }
            if (on) { try { var fp = FirstPersonItemController.Instance; if (fp != null) fp.ForceHolster(); } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static void SendDown(byte state)
        {
            if (manager == null) return;
            var w = new NetDataWriter(); w.Put(MsgDown); w.Put(state); w.Put(Mathf.Max(0f, downUntil - Time.realtimeSinceStartup));
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void SendRevive(int target, byte state)
        {
            if (manager == null) return;
            var w = new NetDataWriter(); w.Put(MsgRevive); w.Put((byte)target); w.Put(state);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void BleedOut(Player p, string why)
        {
            downed = false; revivers.Clear();
            DownFreeze(p, false);
            SendDown(2);
            if (PermaDeathOut()) return;
            Plugin.L.LogInfo("[DOWN] " + why + ": KO как обычно");
            allowKo = true;
            try { p.TriggerPlayerKO(downDir, 0f, false); }
            catch (Exception e) { Plugin.L.LogWarning("[DOWN] TriggerPlayerKO: " + e.Message); }
            finally { allowKo = false; }
        }

        static void GetUp(Player p, int byPid)
        {
            downed = false; revivers.Clear();
            DownFreeze(p, false);
            try { float max = p.maximumHealth; p.SetHealth(Mathf.Clamp01(Rules.ReviveHealth) * max); } catch (Exception __e) { Err.Swallow(__e); }
            SendBodyFx(3, Vector3.zero);
            SendDown(0);
            Toast($"{PlayerLabel(byPid)} revived you.");
            Plugin.L.LogInfo($"[DOWN] меня поднял игрок {byPid}");
        }

        static void DownTick(Player p, float dt)
        {
            float now = Time.realtimeSinceStartup;
            if (downed)
            {
                if (!OthersHere()) { BleedOut(p, "помочь некому (все вышли)"); return; }
                bool helping = false;
                foreach (var kv in revivers) if (now - kv.Value < 0.7f) helping = true;
                if (helping) downUntil += dt;
                if (now >= downFreezeAt) { downFreezeAt = now + 1f; try { p.EnablePlayerMovement(false, true); } catch (Exception __e) { Err.Swallow(__e); } }
                if (now >= downSentAt) { downSentAt = now + 1f; SendDown(helping ? (byte)3 : (byte)1); }
                if (now >= downUntil) BleedOut(p, "помощь не пришла");
                reviveTarget = -1; nearDowned = null;
                return;
            }
            RP best = null;
            if (!p.playerKOInProgress)
            {
                float bd = Plugin.CfgReviveDistance.Value;
                var my = p.transform.position;
                Transform cam = null;
                try { var c = Camera.main; if (c != null) cam = c.transform; } catch (Exception __e) { Err.Swallow(__e); }
                foreach (var o in players)
                {
                    if (!o.downed) continue;
                    if (cam != null && Vector3.Angle(cam.forward, o.targetPos + Vector3.up * 0.4f - cam.position) > 30f) continue;
                    if (Mathf.Abs(my.y - o.targetPos.y) > 2f) continue;
                    float d = Vector2.Distance(new Vector2(my.x, my.z), new Vector2(o.targetPos.x, o.targetPos.z));
                    if (d <= bd) { bd = d; best = o; }
                }
            }
            nearDowned = best;
            bool held = false;
            try { held = best != null && KeyHeld(ReviveKey()); } catch (Exception __e) { Err.Swallow(__e); }
            if (!held)
            {
                if (reviveTarget >= 0) SendRevive(reviveTarget, 0);
                reviveTarget = -1; reviveProgress = 0f;
                return;
            }
            if (reviveTarget != best.pid) { if (reviveTarget >= 0) SendRevive(reviveTarget, 0); reviveTarget = best.pid; reviveProgress = 0f; reviveSentAt = 0f; }
            reviveProgress += dt;
            if (now >= reviveSentAt) { reviveSentAt = now + 0.3f; SendRevive(best.pid, 1); }
            if (reviveProgress >= Mathf.Max(0.2f, Rules.ReviveHoldSeconds))
            {
                SendRevive(best.pid, 2);
                best.downed = false;
                Plugin.L.LogInfo($"[DOWN] поднял игрока {best.pid}");
                reviveTarget = -1; reviveProgress = 0f;
            }
        }

        static void OnDownMsg(RP rp, NetDataReader r)
        {
            byte st = r.GetByte(); float secs = r.GetFloat();
            bool was = rp.downed;
            rp.downed = st == 1 || st == 3;
            rp.downUntil = Time.realtimeSinceStartup + secs;
            rp.reviveAt = st == 3 ? Time.realtimeSinceStartup : 0f;
            if (rp.downed || st == 2) { try { ClearPuppetHeat(rp, rp.downed ? "лежит" : "KO"); } catch (Exception e) { Plugin.L.LogWarning("[ALARM] " + e.Message); } }
            if (rp.downed && !was) { Toast(UiKit.T($"{PlayerLabel(rp.pid)} is down! Hold {ReviveKey()} next to them to revive.", $"{PlayerLabel(rp.pid)} ранен! Подойди и держи {ReviveKey()}, чтобы поднять.")); Plugin.L.LogInfo($"[DOWN] игрок {rp.pid} лежит"); }
            if (!rp.downed && was)
            {
                Toast(st == 0 ? $"{PlayerLabel(rp.pid)} is back on their feet." : $"{PlayerLabel(rp.pid)} was not revived in time.");
                Plugin.L.LogInfo($"[DOWN] игрок {rp.pid}: " + (st == 0 ? "поднят" : "не дождался, KO"));
            }
        }

        static void OnReviveMsg(RP rp, NetDataReader r)
        {
            int target = r.GetByte(); byte st = r.GetByte();
            if (target != MyPid || !downed) return;
            if (st == 1) revivers[rp.pid] = Time.realtimeSinceStartup;
            else if (st == 0) revivers.Remove(rp.pid);
            else if (st == 2) { var p = Player.Instance; if (p != null) GetUp(p, rp.pid); }
        }

        static void DownedOnShutdown()
        {
            if (!downed) return;
            downed = false; revivers.Clear();
            try { var p = Player.Instance; if (p != null) DownFreeze(p, false); } catch (Exception __e) { Err.Swallow(__e); }
        }

        static void DrawDownHud()
        {
            float now = Time.realtimeSinceStartup;
            string text = null;
            if (downed)
            {
                bool helping = false; foreach (var kv in revivers) if (now - kv.Value < 0.7f) helping = true;
                text = helping ? "You are being revived..." : $"You are down! Help arrives or hospital in {Mathf.Max(0f, downUntil - now):F0} s";
                var oc = GUI.color; GUI.color = new Color(0.5f, 0f, 0f, 0.35f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = oc;
            }
            else if (nearDowned != null)
            {
                float hold = Mathf.Max(0.2f, Rules.ReviveHoldSeconds);
                text = reviveTarget == nearDowned.pid ? $"Reviving player {nearDowned.pid}... {Mathf.Min(1f, reviveProgress / hold) * 100f:F0}%"
                                                      : $"Hold {ReviveKey()} to revive player {nearDowned.pid} ({Mathf.Max(0f, nearDowned.downUntil - now):F0} s left)";
            }
            if (text == null) return;
            if (downStyle == null)
            {
                downStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter }; downStyle.normal.textColor = new Color(1f, 0.85f, 0.4f, 1f);
                downShadow = new GUIStyle(downStyle); downShadow.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
            }
            float sc = Screen.height / 1080f;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            float w = Screen.width / sc;
            GUI.Label(new Rect(2f, 702f, w, 40f), text, downShadow);
            GUI.Label(new Rect(0f, 700f, w, 40f), text, downStyle);
            GUI.matrix = old;
        }
    }
}

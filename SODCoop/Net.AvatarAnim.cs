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
        static readonly string[] KnownStates = {
            "Idle","Walking","Running","DrunkWalk","DrunkIdle","CombatIdle","CombatWalking","CombatRunning","CrouchingStackingObjects","OutOfBreath","Leaning","SatAgainstWall","SitDownHunched",
            "PunchBlock","PunchBlockPerfect","Arms_Punch","Arms_LeftPunch","CombatKick","RecoilUpper","RecoilLower","RecoilLeft","RecoilRight","AimingStance","Arms_HoldingSword","Arms_SwingingSword",
            "Arms_HoldingRifle","Arms_ShootingRifle","Arms_PumpAction","Arms_HoldingPistol","Arms_ShootingPistol","Arms_FirePistol","Arms_ThrowingObject","Arms_Fleeing","Arms_Restrained","Arms_Choking",
            "Dead","GetUpFromRagdoll","TripForwards","HandcuffRagdoll","GetUpFromRestrainedRagdoll","Cowering","PickUpFromFloor","SitDown","StandtoSitAgainstWall","KneelDown","Kneeling","StandUpFromKneel",
            "GetIntoBedLeft","GetIntoBedRight","GetIntoBedRollLeft","GetIntoBedRollRight","Sweeping","CleaningBar","CookingChopping","CookingFrying","WashingHands","WashinginShower","BrushingTeeth",
            "Arms_HangUpCoat","Arms_CarryHeavy","Arms_Carry1","StackingObjects","BargeDoor","Arms_LockDoor","Arms_GenericUse","Arms_GenericUseOneShot","Arms_Umbrella","WarmingHands","PullPlayerFromCover",
            "CheckPulse","CheckPulseTransition","Arms_Consume1","Arms_Eating","Arms_EatingCutlery","Arms_BigBite","Arms_Smoking","Arms_SmokingPipe","RubbingEyes","Crying","TelephoneCall","PlayingViolin",
            "PlayingPiano","Arms_Reading","Arms_Typing","Arms_Resting","Arms_Idle","TheTwist","DanceWatusi" };
        static string poseCur = "";
        static readonly System.Collections.Generic.HashSet<string> clipLogged = new System.Collections.Generic.HashSet<string>();

        static string CrouchState(bool moving)
        {
            string v = ((moving ? Plugin.CfgCrouchWalkState.Value : Plugin.CfgCrouchIdleState.Value) ?? "").Trim();
            if (v.Equals("bones", StringComparison.OrdinalIgnoreCase)) return "";
            return v;
        }

        static int CrouchIdleValue() { return Plugin.CfgCrouchIdleValue.Value; }

        static Transform FootOf(Transform lower)
        {
            if (lower == null) return null;
            for (int i = 0; i < lower.childCount; i++) { var c = lower.GetChild(i); if (c != null && c.name.IndexOf("foot", StringComparison.OrdinalIgnoreCase) >= 0) return c; }
            return lower.childCount > 0 ? lower.GetChild(0) : null;
        }

        static void BendAround(Transform t, float deg, Vector3 axis)
        {
            if (t != null && deg != 0f) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation;
        }

        static void CrouchBones(float k)
        {
            if (avatar == null || avatarModel == null) return;
            Transform fl = FootOf(boneLL), fr = FootOf(boneLR);
            float rootY = avatar.transform.position.y;
            if (k <= 0.001f)
            {
                if (fl != null && fr != null && !remoteSitting && !poseAnimSit)
                {
                    float b = Mathf.Min(fl.position.y, fr.position.y) - rootY;
                    crouchFootBase = float.IsNaN(crouchFootBase) ? b : Mathf.Lerp(crouchFootBase, b, 0.05f);
                }
                return;
            }
            Vector3 right = avatar.transform.right;
            float hip = (remoteVent ? Plugin.CfgCrawlHip.Value : Plugin.CfgCrouchBendHip.Value) * k;
            float knee = (remoteVent ? Plugin.CfgCrawlKnee.Value : Plugin.CfgCrouchBendKnee.Value) * k;
            float lean = (remoteVent ? Plugin.CfgCrawlLean.Value : Plugin.CfgCrouchLean.Value) * k;
            BendAround(boneUL, -hip, right); BendAround(boneUR, -hip, right);
            BendAround(boneLL, knee, right); BendAround(boneLR, knee, right);
            BendAround(fl, hip - knee, right); BendAround(fr, hip - knee, right);
            BendAround(boneTorso, lean, right);
            if (remoteVent) { float arms = -Plugin.CfgCrawlArms.Value * k; BendAround(boneArmL, arms, right); BendAround(boneArmR, arms, right); }
            if (fl != null && fr != null && !float.IsNaN(crouchFootBase))
            {
                float drop = Mathf.Min(fl.position.y, fr.position.y) - rootY - crouchFootBase;
                if (drop > 0f) avatarModel.position -= new Vector3(0f, drop, 0f);
            }
            else avatarModel.position -= new Vector3(0f, Plugin.CfgCrouchDrop.Value * k, 0f);
        }
        static int poseLayer = -1, posePrevHash, posePrevLayer;
        static bool poseAnimCrouch, poseAnimLean, poseAnimSit, statesLogged;
        static float poseKick;
        static System.Collections.Generic.Dictionary<string, int> stateLayerCache = new System.Collections.Generic.Dictionary<string, int>();

        static int StateLayer(string name)
        {
            int cached;
            if (stateLayerCache.TryGetValue(name, out cached)) return cached;
            int res = -1;
            try
            {
                int h = Animator.StringToHash(name);
                int forced = Plugin.CfgStateLayer.Value;
                if (forced >= 0) res = avatarAnim.HasState(forced, h) ? forced : -1;
                else for (int l = 0; l < avatarAnim.layerCount; l++) if (avatarAnim.HasState(l, h)) { res = l; break; }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            stateLayerCache[name] = res;
            if (res < 0) Plugin.L.LogWarning($"[ANIM] состояние '{name}' не найдено в аниматоре (имя состояния может отличаться от имени клипа)");
            else Plugin.Dbg($"[ANIM] состояние '{name}' найдено в слое {res}");
            return res;
        }

        static void LogStatesOnce()
        {
            if (statesLogged || avatarAnim == null) return;
            statesLogged = true;
            var ok = new StringBuilder(); var bad = new StringBuilder();
            foreach (var n in KnownStates)
            {
                int h = Animator.StringToHash(n); int found = -1;
                for (int l = 0; l < avatarAnim.layerCount; l++) if (avatarAnim.HasState(l, h)) { found = l; break; }
                if (found >= 0) ok.Append(n).Append('(').Append(found).Append(") "); else bad.Append(n).Append(' ');
            }
            Plugin.Dbg($"[ANIM] состояния, которые можно играть (в скобках слой): {ok}");
            Plugin.Dbg($"[ANIM] имена без такого состояния: {bad}");
        }

        static string PickConsumeState(string item)
        {
            try
            {
                foreach (var pair in (Plugin.CfgConsumeStates.Value ?? "").Split(','))
                {
                    int c = pair.IndexOf(':');
                    if (c <= 0) continue;
                    string key = pair.Substring(0, c).Trim();
                    if (key.Length > 0 && (item ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return pair.Substring(c + 1).Trim();
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return Plugin.CfgConsumeDefault.Value ?? "";
        }

        static bool fxDead; static float fxDeadUntil, fxHitAt; static int fxLog;
        static byte fxLastSent;
        static int[] fxStandHash = new int[0];
        static float fxTripFallAt, fxTripUpAt, fxTripUntil;

        internal static void SendBodyFx(byte kind, Vector3 dir)
        {
            try
            {
                if (manager == null || manager.ConnectedPeersCount == 0 || applyingRemote && kind != 1) return;
                float hx = 0f, hy = 0f;
                var me = Player.Instance;
                if (kind == 1 && me != null)
                {
                    var t = me.transform;
                    Vector3 d = dir; d.y = 0f;
                    if (d.sqrMagnitude > 0.0001f) { d.Normalize(); hx = Vector3.Dot(d, t.right); hy = Vector3.Dot(d, t.forward); }
                }
                var w = new NetDataWriter(); w.Put(MsgBodyFx); w.Put(kind); w.Put(hx); w.Put(hy);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                if (kind != 1) fxLastSent = kind;
                if (fxLog++ < 12) Plugin.Dbg($"[FX] отправил: {(kind == 1 ? "меня ударили" : kind == 6 ? "я поскользнулся" : kind == 2 ? "я упал" : kind == 3 ? "я встал" : "я умер")}");
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void OnBodyFx(NetDataReader r)
        {
            byte kind = r.GetByte(); float hx = r.GetFloat(), hy = r.GetFloat();
            var an = avatarAnim; if (an == null) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (kind == 1)
                {
                    if (fxDead || now - fxHitAt < 0.15f) return;
                    fxHitAt = now;
                    an.SetFloat("hitX", hx); an.SetFloat("hitY", hy);
                    an.SetTrigger("hit");
                }
                else if (kind == 2 || kind == 4)
                {
                    fxDead = true; fxDeadUntil = now + (kind == 4 ? 3600f : 180f);
                    an.ResetTrigger("hit");
                    an.SetBool("isDead", true);
                }
                else if (kind == 6)
                {
                    if (fxDead) return;
                    an.ResetTrigger("hit");
                    an.SetBool("trip", true);
                    fxTripFallAt = now + Mathf.Max(0.1f, Plugin.CfgTripFall.Value);
                    fxTripUpAt = now + Mathf.Max(0.2f, Plugin.CfgTripGetUp.Value);
                    fxTripUntil = fxTripUpAt + 2.5f;
                    poseCur = ""; poseKick = 0f;
                }
                else if (kind == 3)
                {
                    fxDead = false;
                    an.SetBool("isDead", false);
                    an.ResetTrigger("hit");
                    bool any = false;
                    for (int i = 0; i < fxStandHash.Length && i < an.layerCount; i++) if (fxStandHash[i] != 0) { an.CrossFade(fxStandHash[i], 0.25f, i); any = true; }
                    if (!any) an.SetTrigger("ragdollTransitionFrom");
                    poseCur = ""; poseKick = 0f;
                }
                if (fxLog++ < 24) Plugin.Dbg($"[FX] получил: {(kind == 1 ? $"второго игрока ударили (x={hx:F2}, y={hy:F2})" : kind == 2 ? "второй игрок упал" : kind == 3 ? "второй игрок встаёт" : "второй игрок умер")}");
            }
            catch (Exception e) { if (fxLog++ < 24) Plugin.L.LogWarning("[FX] " + e.Message); }
        }

        static void UpdatePose(float dt)
        {
            if (fxDead && Time.realtimeSinceStartup > fxDeadUntil)
            {
                fxDead = false;
                try { if (avatarAnim != null) avatarAnim.SetBool("isDead", false); } catch (Exception __e) { Err.Swallow(__e); }
            }
            if (fxDead) return;
            {
                float tnow = Time.realtimeSinceStartup;
                if (fxTripFallAt > 0f && tnow >= fxTripFallAt) { fxTripFallAt = 0f; try { avatarAnim.SetBool("trip", false); } catch (Exception __e) { Err.Swallow(__e); } }
                if (fxTripUpAt > 0f && tnow >= fxTripUpAt)
                {
                    fxTripUpAt = 0f; fxTripUntil = tnow + 0.3f;
                    try
                    {
                        avatarAnim.SetBool("trip", false);
                        bool any = false;
                        for (int i = 0; i < fxStandHash.Length && i < avatarAnim.layerCount; i++) if (fxStandHash[i] != 0) { avatarAnim.CrossFade(fxStandHash[i], 0.3f, i); any = true; }
                        if (!any) { int gl = StateLayer("GetUpFromRagdoll"); if (gl >= 0) avatarAnim.CrossFade(Animator.StringToHash("GetUpFromRagdoll"), 0.2f, gl); }
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
                if (tnow < fxTripUntil) return;
            }
            if (poseCur == "" && avatarAnim != null && Time.realtimeSinceStartup - fxHitAt > 1.5f)
            {
                try
                {
                    int lc = avatarAnim.layerCount;
                    if (fxStandHash.Length != lc) fxStandHash = new int[lc];
                    for (int i = 0; i < lc; i++) if (!avatarAnim.IsInTransition(i)) fxStandHash[i] = avatarAnim.GetCurrentAnimatorStateInfo(i).fullPathHash;
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            LogStatesOnce();
            string want = "";
            bool crouchAnim = false, leanAnim = false, sitAnim = false;
            string dbg = Plugin.CfgPlayState.Value ?? "";
            if (dbg != "") want = dbg;
            else if (remoteSitting && !string.IsNullOrEmpty(Plugin.CfgSitState.Value))
            {
                want = Plugin.CfgSitState.Value; sitAnim = true;
            }
            else if (remoteConsuming && PickConsumeState(remoteItem) != "")
            {
                want = PickConsumeState(remoteItem);
            }
            else if (remoteCrouched && !remoteVent)
            {
                string c = CrouchState(false);
                string cw = CrouchState(true);
                if (animSpeed > 0.3f && cw != "") c = cw;
                if (c != "") { want = c; crouchAnim = true; }
            }
            else
            {
                float thr = Plugin.CfgLeanThreshold.Value;
                string ls = remoteLean < -thr ? Plugin.CfgLeanLeftState.Value : (remoteLean > thr ? Plugin.CfgLeanRightState.Value : "");
                if (!string.IsNullOrEmpty(ls)) { want = ls; leanAnim = true; }
            }
            int layer = -1;
            if (want != "")
            {
                layer = StateLayer(want);
                if (layer < 0) { want = ""; crouchAnim = leanAnim = sitAnim = false; }
            }
            float fade = Plugin.CfgStateFade.Value;
            if (want != poseCur)
            {
                if (poseCur == "" && want != "")
                {
                    posePrevLayer = layer;
                    posePrevHash = avatarAnim.GetCurrentAnimatorStateInfo(layer).fullPathHash;
                }
                if (want != "") { avatarAnim.CrossFade(Animator.StringToHash(want), fade, layer); poseKick = Time.realtimeSinceStartup + 0.3f; }
                else avatarAnim.CrossFade(posePrevHash, fade, posePrevLayer);
                poseCur = want;
            }
            else if (want != "" && Time.realtimeSinceStartup > poseKick)
            {
                if (crouchAnim && clipLogged.Add(want))
                {
                    try
                    {
                        var ci = avatarAnim.GetCurrentAnimatorClipInfo(layer);
                        for (int i = 0; ci != null && i < ci.Length; i++)
                            if (ci[i].clip != null) Plugin.L.LogInfo($"[ANIM] присед '{want}': клип '{ci[i].clip.name}', длина {ci[i].clip.length:F2} с, зациклен={ci[i].clip.isLooping}");
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
                try
                {
                    if (avatarAnim.GetCurrentAnimatorStateInfo(layer).shortNameHash != Animator.StringToHash(want) && !avatarAnim.IsInTransition(layer))
                    {
                        avatarAnim.CrossFade(Animator.StringToHash(want), fade, layer);
                        poseKick = Time.realtimeSinceStartup + 0.3f;
                    }
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            poseLayer = want != "" ? layer : -1;
            if (poseLayer > 0) avatarAnim.SetLayerWeight(poseLayer, 1f);
            poseAnimCrouch = crouchAnim || paramCrouch;
            poseAnimLean = leanAnim;
            poseAnimSit = sitAnim || paramSit;
        }

        static void DestroyAvatar()
        {
            MapUntrack();
            if (remotePending) { remotePending = false; CharEditor.RemoteAbort(); }
            remoteLod.Clear();
            if (avatar != null)
            {
                UnityEngine.Object.Destroy(avatar);
                avatar = null;
                avatarAnim = null;
                ClearHeld(); remoteHeldDirty = remoteHeld.Count > 0; poseCur = ""; poseAnimCrouch = poseAnimLean = poseAnimSit = false; remoteConsuming = remoteSitting = false; sitK = 0f;
                avatarModel = null; fxDead = false;
                boneUL = boneUR = boneLL = boneLR = boneTorso = null; crouchFootBase = float.NaN;
                remoteLean = leanSmooth = 0f;
                remoteBlocking = false;
                remoteCrouched = remoteRunning = remoteFists = false;
                armsW = 0f;
                crouchK = 0f; crouchBoneK = crouchAnimK = 0f;
                Plugin.L.LogInfo("[NET] Аватар второго игрока удалён");
            }
        }
    }
}

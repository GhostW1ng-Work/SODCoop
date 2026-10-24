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
        const string Key = "sodcoop-1";

        sealed class RP
        {
            public int pid; public NetPeer peer; public string clientId = ""; public string profileId = ""; public string label = "";
            public string nick = "";
            public bool downed; public float downUntil, reviveAt;
            public bool ratOn; public Vector3 ratUp = Vector3.up;
            public bool illTres, illAction, illArrestOnly; public int illEsc;
            public float combatUntil;
            public bool attackAlt;
            public string remoteItem = "";
            public GameObject avatar;
            public Vector3 targetPos;
            public float targetYaw;
            public bool avatarSnap;
            public Animator avatarAnim;
            public Transform avatarModel;
            public Vector3 modelBaseScale = Vector3.one;
            public Vector3 modelBasePos;
            public Quaternion modelBaseRot = Quaternion.identity;
            public Transform boneUL;
            public Transform boneUR;
            public Transform boneLL;
            public Transform boneLR;
            public Transform boneTorso, boneArmL, boneArmR;
            public bool remoteVent;
            public float crouchFootBase = float.NaN;
            public float remoteLean;
            public float leanSmooth;
            public bool remoteBlocking;
            public bool remoteConsuming;
            public bool remoteSitting;
            public bool paramSit;
            public bool paramCrouch;
            public bool remoteCarryObj;
            public int remoteSeatId = -1, puppetSeatId = -1;
            public float remoteDrunk;
            public int remoteElevId = -1; public float remoteElevDy;
            public bool remoteFlash; public float remotePitch;
            public float sitK;
            public float crouchBoneK;
            public float crouchAnimK;
            public float crouchK;
            public bool remoteCrouched;
            public bool remoteRunning;
            public bool remoteFists;
            public float armsW;
            public float animSpeed;
            public Appearance remoteApp;
            public string remoteAppJson = "";
            public bool remotePending;
            public int remoteTries;
            public List<Renderer> remoteLod = new List<Renderer>();
            public Citizen puppet;
            public int puppetId = -1;
            public float puppetAt;
            public float puppetAiAt;
            public float puppetLocAt;
            public float puppetIdAt;
            public float puppetHideAt;
            public bool puppetCrouch;
            public Renderer[] puppetRends;
            public Collider[] puppetCols;
            public int puppetHideId = -1;
            public float puppetClientHideAt;
            public string remoteWeaponName = "";
            public int remoteWeaponCao = -1;
            public List<HeldPart> remoteHeld = new List<HeldPart>();
            public bool remoteHeldDirty;
            public string remoteHeldSig = "";
            public GameObject heldL;
            public GameObject heldR;
            public float meshMissAt;
            public float heldU = 1f;
            public bool heldWeaponMode;
            public string remoteHeldPreset = "";
            public bool heldPresetMode;
            public string poseCur = "";
            public int poseLayer = -1;
            public int posePrevHash;
            public int posePrevLayer;
            public bool poseAnimCrouch;
            public bool poseAnimLean;
            public bool poseAnimSit;
            public float poseKick;
            public bool fxDead;
            public float fxDeadUntil;
            public float fxHitAt;
            public int[] fxStandHash = new int[0];
            public float fxTripFallAt, fxTripUpAt, fxTripUntil;
            public Transform mapTracked;
        }

        static readonly List<RP> players = new List<RP>();
        static RP curRp;
        internal static int MyPid;

        static RP RpByPid(int pid) { for (int i = 0; i < players.Count; i++) if (players[i].pid == pid) return players[i]; return null; }
        static RP RpByPeer(NetPeer peer) { if (peer == null) return null; for (int i = 0; i < players.Count; i++) if (players[i].peer == peer) return players[i]; return null; }

        static void Enter(RP rp)
        {
            if (rp == curRp) return;
            Leave();
            if (rp == null) return;
            LoadRp(rp); curRp = rp;
        }
        static void Leave() { if (curRp == null) return; SaveRp(curRp); curRp = null; }
        static void SyncCur() { if (curRp != null) SaveRp(curRp); }

        static void LoadRp(RP r)
        {
            combatUntil = r.combatUntil;
            attackAlt = r.attackAlt;
            remoteItem = r.remoteItem;
            avatar = r.avatar;
            targetPos = r.targetPos;
            targetYaw = r.targetYaw;
            avatarSnap = r.avatarSnap;
            avatarAnim = r.avatarAnim;
            avatarModel = r.avatarModel;
            modelBaseScale = r.modelBaseScale;
            modelBasePos = r.modelBasePos;
            modelBaseRot = r.modelBaseRot;
            boneUL = r.boneUL;
            boneUR = r.boneUR;
            boneLL = r.boneLL;
            boneLR = r.boneLR;
            boneTorso = r.boneTorso; boneArmL = r.boneArmL; boneArmR = r.boneArmR; remoteVent = r.remoteVent;
            crouchFootBase = r.crouchFootBase;
            remoteLean = r.remoteLean;
            leanSmooth = r.leanSmooth;
            remoteBlocking = r.remoteBlocking;
            remoteConsuming = r.remoteConsuming;
            remoteSitting = r.remoteSitting;
            paramSit = r.paramSit;
            paramCrouch = r.paramCrouch;
            remoteCarryObj = r.remoteCarryObj;
            remoteSeatId = r.remoteSeatId; puppetSeatId = r.puppetSeatId;
            remoteDrunk = r.remoteDrunk;
            remoteElevId = r.remoteElevId; remoteElevDy = r.remoteElevDy;
            remoteFlash = r.remoteFlash; remotePitch = r.remotePitch;
            sitK = r.sitK;
            crouchBoneK = r.crouchBoneK;
            crouchAnimK = r.crouchAnimK;
            crouchK = r.crouchK;
            remoteCrouched = r.remoteCrouched;
            remoteRunning = r.remoteRunning;
            remoteFists = r.remoteFists;
            armsW = r.armsW;
            animSpeed = r.animSpeed;
            remoteApp = r.remoteApp;
            remoteAppJson = r.remoteAppJson;
            remotePending = r.remotePending;
            remoteTries = r.remoteTries;
            remoteLod = r.remoteLod;
            puppet = r.puppet;
            puppetId = r.puppetId;
            puppetAt = r.puppetAt;
            puppetAiAt = r.puppetAiAt;
            puppetLocAt = r.puppetLocAt;
            puppetIdAt = r.puppetIdAt;
            puppetHideAt = r.puppetHideAt;
            puppetCrouch = r.puppetCrouch;
            puppetRends = r.puppetRends;
            puppetCols = r.puppetCols;
            puppetHideId = r.puppetHideId;
            puppetClientHideAt = r.puppetClientHideAt;
            remoteWeaponName = r.remoteWeaponName;
            remoteWeaponCao = r.remoteWeaponCao;
            remoteHeld = r.remoteHeld;
            remoteHeldDirty = r.remoteHeldDirty;
            remoteHeldSig = r.remoteHeldSig;
            heldL = r.heldL;
            heldR = r.heldR;
            meshMissAt = r.meshMissAt;
            heldU = r.heldU;
            heldWeaponMode = r.heldWeaponMode;
            remoteHeldPreset = r.remoteHeldPreset;
            heldPresetMode = r.heldPresetMode;
            poseCur = r.poseCur;
            poseLayer = r.poseLayer;
            posePrevHash = r.posePrevHash;
            posePrevLayer = r.posePrevLayer;
            poseAnimCrouch = r.poseAnimCrouch;
            poseAnimLean = r.poseAnimLean;
            poseAnimSit = r.poseAnimSit;
            poseKick = r.poseKick;
            fxDead = r.fxDead;
            fxDeadUntil = r.fxDeadUntil;
            fxHitAt = r.fxHitAt;
            fxStandHash = r.fxStandHash;
            fxTripFallAt = r.fxTripFallAt; fxTripUpAt = r.fxTripUpAt; fxTripUntil = r.fxTripUntil;
            mapTracked = r.mapTracked;
        }

        static void SaveRp(RP r)
        {
            r.combatUntil = combatUntil;
            r.attackAlt = attackAlt;
            r.remoteItem = remoteItem;
            r.avatar = avatar;
            r.targetPos = targetPos;
            r.targetYaw = targetYaw;
            r.avatarSnap = avatarSnap;
            r.avatarAnim = avatarAnim;
            r.avatarModel = avatarModel;
            r.modelBaseScale = modelBaseScale;
            r.modelBasePos = modelBasePos;
            r.modelBaseRot = modelBaseRot;
            r.boneUL = boneUL;
            r.boneUR = boneUR;
            r.boneLL = boneLL;
            r.boneLR = boneLR;
            r.boneTorso = boneTorso; r.boneArmL = boneArmL; r.boneArmR = boneArmR; r.remoteVent = remoteVent;
            r.crouchFootBase = crouchFootBase;
            r.remoteLean = remoteLean;
            r.leanSmooth = leanSmooth;
            r.remoteBlocking = remoteBlocking;
            r.remoteConsuming = remoteConsuming;
            r.remoteSitting = remoteSitting;
            r.paramSit = paramSit;
            r.paramCrouch = paramCrouch;
            r.remoteCarryObj = remoteCarryObj;
            r.remoteSeatId = remoteSeatId; r.puppetSeatId = puppetSeatId;
            r.remoteDrunk = remoteDrunk;
            r.remoteElevId = remoteElevId; r.remoteElevDy = remoteElevDy;
            r.remoteFlash = remoteFlash; r.remotePitch = remotePitch;
            r.sitK = sitK;
            r.crouchBoneK = crouchBoneK;
            r.crouchAnimK = crouchAnimK;
            r.crouchK = crouchK;
            r.remoteCrouched = remoteCrouched;
            r.remoteRunning = remoteRunning;
            r.remoteFists = remoteFists;
            r.armsW = armsW;
            r.animSpeed = animSpeed;
            r.remoteApp = remoteApp;
            r.remoteAppJson = remoteAppJson;
            r.remotePending = remotePending;
            r.remoteTries = remoteTries;
            r.remoteLod = remoteLod;
            r.puppet = puppet;
            r.puppetId = puppetId;
            r.puppetAt = puppetAt;
            r.puppetAiAt = puppetAiAt;
            r.puppetLocAt = puppetLocAt;
            r.puppetIdAt = puppetIdAt;
            r.puppetHideAt = puppetHideAt;
            r.puppetCrouch = puppetCrouch;
            r.puppetRends = puppetRends;
            r.puppetCols = puppetCols;
            r.puppetHideId = puppetHideId;
            r.puppetClientHideAt = puppetClientHideAt;
            r.remoteWeaponName = remoteWeaponName;
            r.remoteWeaponCao = remoteWeaponCao;
            r.remoteHeld = remoteHeld;
            r.remoteHeldDirty = remoteHeldDirty;
            r.remoteHeldSig = remoteHeldSig;
            r.heldL = heldL;
            r.heldR = heldR;
            r.meshMissAt = meshMissAt;
            r.heldU = heldU;
            r.heldWeaponMode = heldWeaponMode;
            r.remoteHeldPreset = remoteHeldPreset;
            r.heldPresetMode = heldPresetMode;
            r.poseCur = poseCur;
            r.poseLayer = poseLayer;
            r.posePrevHash = posePrevHash;
            r.posePrevLayer = posePrevLayer;
            r.poseAnimCrouch = poseAnimCrouch;
            r.poseAnimLean = poseAnimLean;
            r.poseAnimSit = poseAnimSit;
            r.poseKick = poseKick;
            r.fxDead = fxDead;
            r.fxDeadUntil = fxDeadUntil;
            r.fxHitAt = fxHitAt;
            r.fxStandHash = fxStandHash;
            r.fxTripFallAt = fxTripFallAt; r.fxTripUpAt = fxTripUpAt; r.fxTripUntil = fxTripUntil;
            r.mapTracked = mapTracked;
        }

        const byte MsgHello = 1;
        const byte MsgHelloReply = 2;
        const byte MsgPos = 3;
        const byte MsgAction = 4;
        static float combatUntil;
        static bool attackAlt;
        static bool itemLogged;

        static float fistsCheckAt;
        static bool fistsCached;
        static string lastItemName;
        static string curItem = "";
        static string sentItem = null;
        static float itemSendAt;
        static string remoteItem = "";
        static string layersStr = null;
        static float[] layersVal = new float[0];
        static int lastForceLog = -2;
        const byte MsgItem = 5;
        const byte MsgHeld = 6;
        const byte MsgObj = 7;
        const byte MsgItemEv = 8;
        const byte MsgDoor = 9;
        const byte MsgNpc = 10;
        const byte MsgNpcHit = 11;
        const byte MsgPuppet = 12;
        const byte MsgNpcAct = 15;
        const byte MsgSaveReq = 18, MsgSaveMeta = 19, MsgSaveWant = 20, MsgSaveChunk = 21, MsgSaveDone = 22, MsgSaveFail = 23;
        const byte MsgBodyFx = 17;
        const byte MsgShot = 24;
        const byte MsgWeapon = 25;
        const byte MsgRelay = 26;
        const byte MsgPlayerInfo = 27;
        const byte MsgPlayerLeft = 28;
        const byte MsgHostReload = 37;
        const byte MsgRules = 38;
        const byte MsgIllegal = 39;
        const byte MsgJob = 34;
        const byte MsgCorpse = 36;
        const byte MsgMurder = 35;
        const byte MsgKnock = 33;
        const byte MsgElevator = 32;
        const byte MsgDown = 30;
        const byte MsgRevive = 31;
        const byte MsgProfile = 29;
        const byte MsgSound = 16;
        const byte MsgPuppetHurt = 13;

        static int ItemArms(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            try
            {
                foreach (var pair in (Plugin.CfgItemMap.Value ?? "").Split(','))
                {
                    int c = pair.IndexOf(':');
                    if (c <= 0) continue;
                    string key = pair.Substring(0, c).Trim();
                    int v;
                    if (key.Length == 0 || !int.TryParse(pair.Substring(c + 1).Trim(), out v)) continue;
                    if (name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return v;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return -1;
        }

        static bool FistsDrawn()
        {
            if (Time.realtimeSinceStartup < fistsCheckAt) return fistsCached;
            fistsCheckAt = Time.realtimeSinceStartup + 0.2f;
            bool res = false;
            try
            {
                var fp = FirstPersonItemController.Instance;
                var it = fp != null ? fp.currentItem : null;
                curItem = "";
                if (it != null && !fp.forceHolstered)
                {
                    string nm = it.name ?? "";
                    curItem = nm;
                    res = nm.IndexOf("fist", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (nm != lastItemName)
                    {
                        lastItemName = nm;
                        Plugin.L.LogInfo($"[NET] Предмет в руках: {nm}, кулаки={res}");
                    }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            fistsCached = res;
            return res;
        }

        static float LeanRoll()
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) return 0f;
                float z = cam.transform.eulerAngles.z;
                if (z > 180f) z -= 360f;
                return Mathf.Clamp(z, -45f, 45f);
            }
            catch (Exception) { return 0f; }
        }

        static bool inputBroken;
        static bool LocalBlocking()
        {
            if (!inputBroken)
            {
                try { return Input.GetMouseButton(1); }
                catch (Exception) { inputBroken = true; }
            }
            return Time.realtimeSinceStartup < blockUntil;
        }

        internal static void NoteBlock() { blockUntil = Time.realtimeSinceStartup + 0.5f; }

        static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static Quaternion AxisRot(float deg)
        {
            switch ((Plugin.CfgCrouchAxis.Value ?? "X").Trim().ToUpperInvariant())
            {
                case "Y": return Quaternion.Euler(0f, deg, 0f);
                case "Z": return Quaternion.Euler(0f, 0f, deg);
                default: return Quaternion.Euler(deg, 0f, 0f);
            }
        }

        static float lastSeatLog;
        internal static bool SeatTaken(PlayerTransitionPreset enter, Interactable it)
        {
            try
            {
                if (it == null || enter == null || players.Count == 0) return false;
                SyncCur();
                {
                    string tn0 = enter.name ?? "";
                    if (tn0.IndexOf("exit", StringComparison.OrdinalIgnoreCase) < 0)
                        for (int pi = 0; pi < players.Count; pi++)
                        {
                            var o = players[pi];
                            if (o.remoteSitting && o.remoteSeatId >= 0 && o.remoteSeatId == it.id)
                            {
                                if (Time.realtimeSinceStartup - lastSeatLog > 1f) { lastSeatLog = Time.realtimeSinceStartup; Plugin.Dbg($"[SIT] на этом месте ('{tn0}') сидит игрок {o.pid} - посадка отменена"); }
                                return true;
                            }
                        }
                }
                RP sitter = null; float bestD = float.MaxValue;
                for (int pi = 0; pi < players.Count; pi++)
                {
                    var o = players[pi];
                    if (!o.remoteSitting || o.avatar == null) continue;
                    if (o.remoteSeatId >= 0) continue;
                    Vector3 dd = it.wPos - o.avatar.transform.position; dd.y = 0f;
                    if (dd.sqrMagnitude < bestD) { bestD = dd.sqrMagnitude; sitter = o; }
                }
                if (sitter == null) return false;
                var avatar = sitter.avatar;
                string tn = enter.name ?? "";
                if (tn.IndexOf("exit", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                bool match = false;
                foreach (var kw in ((Plugin.CfgSitKeywords.Value ?? "") + ",booth,sofa,couch,armchair,bench,stool,seat,chair,sit,toilet").Split(','))
                {
                    string k = kw.Trim();
                    if (k.Length > 0 && tn.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                }
                if (!match) return false;
                Vector3 d = it.wPos - avatar.transform.position; d.y = 0f;
                float rad = Plugin.CfgSeatRadius.Value;
                if (rad <= 0f || d.sqrMagnitude > rad * rad)
                {
                    if (Time.realtimeSinceStartup - lastSeatLog > 1f) { lastSeatLog = Time.realtimeSinceStartup; Plugin.Dbg($"[SIT] сажусь рядом с другим игроком, расстояние до места {Mathf.Sqrt(d.sqrMagnitude):F2} м"); }
                    return false;
                }
                if (Time.realtimeSinceStartup - lastSeatLog > 1f)
                {
                    lastSeatLog = Time.realtimeSinceStartup;
                    Plugin.Dbg($"[SIT] место занято другим игроком ('{tn}', {Mathf.Sqrt(d.sqrMagnitude):F2} м) - посадка отменена");
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        static string lastSitLog = "";
        static bool sitLatched;
        static Interactable sitSeat;

        static Vector3 refScaleM, refScaleF, refScaleAll;
        static float refScaleAt = -1000f;
        static bool refScaleLogged;

        static void RecalcCitizenScale()
        {
            Vector3 sm = Vector3.zero, sf = Vector3.zero, sa = Vector3.zero; int nm = 0, nf = 0, na = 0;
            try
            {
                var list = CityData.Instance.citizenDirectory;
                for (int i = 0; list != null && i < list.Count && na < 400; i++)
                {
                    var c = list[i];
                    if (c == null || c.isPlayer || IsPuppetCit(c)) continue;
                    var ac = c.animationController;
                    var an = ac != null ? ac.mainAnimator : null;
                    if (an == null) continue;
                    var ls = an.transform.lossyScale;
                    if (ls.y <= 0.0001f) continue;
                    sa += ls; na++;
                    int g = (int)c.gender;
                    if (g == 0) { sm += ls; nm++; } else if (g == 1) { sf += ls; nf++; }
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            if (na == 0) return;
            refScaleAll = sa / na; refScaleM = nm > 0 ? sm / nm : refScaleAll; refScaleF = nf > 0 ? sf / nf : refScaleAll;
            string msg = $"[CHAR] средний масштаб моделей горожан: все {refScaleAll.y:F3} ({na}), мужчины {refScaleM.y:F3} ({nm}), женщины {refScaleF.y:F3} ({nf})";
            if (!refScaleLogged) { refScaleLogged = true; Plugin.L.LogInfo(msg + " - такой рост получат аватары игроков"); } else Plugin.Dbg(msg);
        }

        static Vector3 AvatarBaseScale()
        {
            float now = Time.realtimeSinceStartup;
            if (now - refScaleAt > 60f) { refScaleAt = now; RecalcCitizenScale(); }
            int g = remoteApp != null ? remoteApp.Gender : -1;
            Vector3 r = g == 0 ? refScaleM : g == 1 ? refScaleF : refScaleAll;
            if (r.y <= 0.0001f || avatarModel == null) return modelBaseScale;
            Vector3 ps = avatarModel.parent != null ? avatarModel.parent.lossyScale : Vector3.one;
            if (ps.x == 0f || ps.y == 0f || ps.z == 0f) return modelBaseScale;
            return new Vector3(r.x / ps.x, r.y / ps.y, r.z / ps.z);
        }

        static int SeatIdNow()
        {
            try { return sitLatched && sitSeat != null ? sitSeat.id : -1; } catch (Exception) { return -1; }
        }

        static bool SeatPose(int id, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            if (id < 0) return false;
            var it = FindById(id);
            var up = it != null ? it.usagePoint : null;
            if (up == null) return false;
            try { up.PositionUpdate(); } catch (Exception __e) { Err.Swallow(__e); }
            pos = up.useageWorldPosition;
            if (pos == Vector3.zero) return false;
            Vector3 look = up.worldLookAtPoint - pos; look.y = 0f;
            if (look.sqrMagnitude < 0.0001f) return false;
            yaw = Quaternion.LookRotation(look).eulerAngles.y;
            return true;
        }
        static Vector3 sitLastPos;
        static bool SittingNow(Player p)
        {
            try
            {
                string tn = "", inm = "";
                bool act = p.transitionActive;
                if (act)
                {
                    var ct = p.currentTransition;
                    tn = ct != null ? ct.name : "";
                    var ti = p.transitionInteractable;
                    inm = ti != null ? ti.name : "";
                    bool exit = tn.IndexOf("exit", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool match = false;
                    foreach (var kw in ((Plugin.CfgSitKeywords.Value ?? "") + ",booth,sofa,couch,armchair,bench,stool,seat,chair,sit,toilet").Split(','))
                    {
                        string k = kw.Trim();
                        if (k.Length > 0 && tn.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                    }
                    if (exit) { sitLatched = false; sitSeat = null; }
                    else if (match) { sitLatched = true; sitLastPos = p.transform.position; sitSeat = p.transitionInteractable; }
                }
                if (sitLatched && !act)
                {
                    Vector3 d = p.transform.position - sitLastPos; d.y = 0f;
                    if (d.sqrMagnitude > 1.5f) { sitLatched = false; sitSeat = null; }
                }
                string sig = act + "|" + tn + "|" + inm + "|" + sitLatched;
                if (sig != lastSitLog)
                {
                    lastSitLog = sig;
                    Plugin.Dbg($"[SIT] переход активен={act} имя='{tn}' объект='{inm}' сидит={sitLatched}");
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return sitLatched;
        }

        static float lastThrowSent;
        internal static void SendAction(byte kind)
        {
            try
            {
                if (kind == 3 || kind == 4)
                {
                    if (Time.realtimeSinceStartup - lastThrowSent < 0.4f) return;
                    lastThrowSent = Time.realtimeSinceStartup;
                    Plugin.Dbg($"[ITEM] бросок/положил (kind={kind})");
                }
                if (!itemLogged)
                {
                    itemLogged = true;
                    try
                    {
                        var it = FirstPersonItemController.Instance.currentItem;
                        Plugin.L.LogInfo($"[NET] Первое действие kind={kind}, предмет в руках: {(it != null ? it.name : "null")}");
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgAction);
                w.Put(kind);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception e) { Plugin.L.LogWarning($"[NET] SendAction: {e.Message}"); }
        }

        static EventBasedNetListener listener;
        static NetManager manager;
        static NetPeer serverPeer;
        static bool connecting;
        static float nextAttempt;
        static bool errorLogged;

        static float sendTimer;
        static GameObject avatar;
        static Vector3 targetPos;
        static float targetYaw;
        static bool avatarSnap;
        static Animator avatarAnim;
        static Transform avatarModel;
        static Vector3 modelBaseScale = Vector3.one;
        static Vector3 modelBasePos;
        static Quaternion modelBaseRot = Quaternion.identity;
        static Transform boneUL, boneUR, boneLL, boneLR, boneTorso, boneArmL, boneArmR;
        static bool remoteVent;

        static bool inVentNow;

        internal static void SetInVent(bool v)
        {
            if (inVentNow == v) return;
            inVentNow = v;
            Plugin.L.LogInfo(v ? "[POSE] я залез в вентиляцию" : "[POSE] я вылез из вентиляции");
        }

        static bool InVent(Player p) { return inVentNow; }
        static float crouchFootBase = float.NaN;
        static float remoteLean, leanSmooth;
        static bool remoteBlocking;
        static bool remoteConsuming, remoteSitting, paramSit, paramCrouch, remoteCarryObj;
        static int remoteSeatId = -1;
        static int remoteElevId = -1; static float remoteElevDy;
        static bool remoteFlash; static float remotePitch;
        static float remoteDrunk;
        static int puppetSeatId = -1;
        static bool seatSnapped;
        static float sitK;
        static float blockUntil;
        static float crouchBoneK, crouchAnimK;
        static float crouchK;
        static bool remoteCrouched, remoteRunning, remoteFists;
        static float armsW;
        static int lastCycleCao = -1;
        static float nextCycleAttack;
        static float animSpeed;

        internal static bool Started { get; private set; }
        static bool IsHost { get { EnsureRt(); return RtMode == NetMode.Host; } }

        internal static NetMode RtMode = NetMode.Off;
        internal static string RtAddress = "127.0.0.1", RtPassword = "", RtName = "SodCoop server";
        internal static int RtPort = 9050;
        static bool rtInit, connectRejected;

        internal static void EnsureRt()
        {
            if (rtInit) return;
            rtInit = true;
            try { RtMode = Plugin.CfgMode.Value; RtAddress = Plugin.CfgAddress.Value; RtPort = Plugin.CfgPort.Value; RtPassword = Plugin.CfgPassword.Value ?? ""; RtName = Plugin.CfgServerName.Value ?? "SodCoop server"; }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static int PlayerCount { get { try { return manager == null ? 1 : players.Count + 1; } catch (Exception) { return 1; } } }
        internal static int MaxPlayersPublic { get { return MaxPlayersNow(); } }
        internal static string SeedInfo() { try { return "" + CityData.Instance.seed; } catch (Exception) { return ""; } }
        internal static string BuildInfo() { try { return "" + Game.Instance.buildID; } catch (Exception) { return ""; } }

        static readonly List<KeyValuePair<string, float>> toasts = new List<KeyValuePair<string, float>>();
        static readonly string MyClientId = Guid.NewGuid().ToString("N");
        static float lostAt = -1f;
        static bool everConnected;
        static GUIStyle hudStyle, hudShadow;

        internal static void Toast(string text)
        {
            try { toasts.Add(new KeyValuePair<string, float>(text, Time.realtimeSinceStartup + 6f)); while (toasts.Count > 4) toasts.RemoveAt(0); } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo("[HUD] " + text);
        }

        internal static bool MenuOpen() { return InMainMenu(); }
        internal static bool Following() { return followHost; }

        static bool InMainMenu() { try { var mm = MainMenuController.Instance; return mm != null && mm.mainMenuActive; } catch (Exception) { return false; } }

        internal static void DrawHud()
        {
            try { DrawCaseAnswers(); } catch (Exception __e) { Err.Swallow(__e); }
            try
            {
                if (manager == null || RtMode == NetMode.Off || xferMode || InMainMenu()) return;
                if (hudStyle == null)
                {
                    hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperLeft };
                    hudStyle.normal.textColor = new Color(1f, 0.92f, 0.92f, 1f);
                    hudShadow = new GUIStyle(hudStyle); hudShadow.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
                }
                float sc = Screen.height / 1080f;
                var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
                string st = StatusLine();
                if (st != "")
                {
                    GUI.Label(new Rect(21f, 11f, 1400f, 30f), st, hudShadow);
                    GUI.Label(new Rect(20f, 10f, 1400f, 30f), st, hudStyle);
                }
                float now = Time.realtimeSinceStartup; float y = 44f;
                for (int i = toasts.Count - 1; i >= 0; i--) if (toasts[i].Value < now) toasts.RemoveAt(i);
                foreach (var t in toasts)
                {
                    GUI.Label(new Rect(21f, y + 1f, 1400f, 30f), t.Key, hudShadow);
                    GUI.Label(new Rect(20f, y, 1400f, 30f), t.Key, hudStyle); y += 28f;
                }
                GUI.matrix = old;
                DrawDownHud();
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static void ClientReleaseWorld()
        {
            try { npcTargets.Clear(); } catch (Exception __e) { Err.Swallow(__e); }
            if (npcClientPaused)
            {
                try { Game.Instance.SetPauseAI(false); } catch (Exception __e) { Err.Swallow(__e); }
                npcClientPaused = false;
                Plugin.L.LogInfo("[NPC] клиент: связь с хостом потеряна, ИИ горожан снова включён");
            }
        }

        internal static void LeaveSession()
        {
            bool wasClient = RtMode == NetMode.Client;
            followHost = false; followPending = false; followAt = -1f; hostReloadAt = -1f; helloWasOk = false;
            Plugin.L.LogInfo("[NET] End: выход из мультиплеера");
            ApplyOff();
            if (wasClient) ClientReleaseWorld();
            lostAt = -1f; everConnected = false;
            Toast("Multiplayer is off. Use the MULTIPLAYER button in the main menu to start again.");
        }

        internal static void Shutdown()
        {
            try { PermaOnShutdown(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] " + e.Message); }
            try { ModifiersRestore(); } catch (Exception e) { Plugin.L.LogWarning("[MODS] " + e.Message); }
            Rules.Clear();
            doorPending.Clear();
            ratScanDone = false;
            try { ProfileOnShutdown(); } catch (Exception __e) { Err.Swallow(__e); }
            try { DownedOnShutdown(); } catch (Exception __e) { Err.Swallow(__e); }
            lostAt = -1f; everConnected = false; weaponSent = null;
            try { Lan.StopAdvertise(); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (manager != null) manager.Stop(); } catch (Exception __e) { Err.Swallow(__e); }
            manager = null; listener = null; serverPeer = null; connecting = false; connectRejected = false; Started = false;
            try { ClearPlayers(); } catch (Exception __e) { Err.Swallow(__e); }
            MyPid = 0;
        }

        internal static void ApplyHost(string name, string pass, int port)
        {
            EnsureRt(); Shutdown();
            RtName = name; RtPassword = pass ?? ""; RtPort = port; RtMode = NetMode.Host;
            try { Plugin.CfgServerName.Value = name; Plugin.CfgPassword.Value = RtPassword; Plugin.CfgPort.Value = port; } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[NET] Из меню: хост «{name}», порт {port}, пароль={(RtPassword != "" ? "да" : "нет")}");
        }

        internal static void ApplyClient(string addr, int port, string pass)
        {
            EnsureRt(); Shutdown();
            RtAddress = addr; RtPort = port; RtPassword = pass ?? ""; RtMode = NetMode.Client;
            try { Plugin.CfgAddress.Value = addr; } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[NET] Из меню: подключение к {addr}:{port}, пароль={(RtPassword != "" ? "да" : "нет")}");
        }

        internal static void ApplyOff() { EnsureRt(); Shutdown(); RtMode = NetMode.Off; Plugin.L.LogInfo("[NET] Из меню: мультиплеер выключен"); }

        internal static string StatusLine()
        {
            EnsureRt();
            if (xferMode) return UiKit.T("CLIENT: ", "КЛИЕНТ: ") + xferText;
            if (MenuAuto.Notice != "") return MenuAuto.Notice;
            if (RtMode == NetMode.Off) return "";
            if (RtMode == NetMode.Host && sendStage != 0) return UiKit.T("HOSTING: sending the save to a player...", "ХОСТ: отправляю сейв игроку...");
            if (RtMode == NetMode.Host) return (manager != null ? UiKit.T("HOSTING", "ХОСТ") : UiKit.T("HOST (starts when the city has loaded)", "ХОСТ (запустится, когда город загрузится)")) + ": " + RtName + " : " + RtPort + (manager != null ? "   [" + (players.Count + 1) + "/" + Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4) + UiKit.T(" players", " игроков") + (players.Count == 0 ? UiKit.T(", waiting", ", жду") : "") + "]   " + UiKit.T("(End = stop)", "(End - остановить)") : "");
            if (connectRejected) return UiKit.T("REJECTED by ", "ОТКАЗ от ") + RtAddress + ":" + RtPort + UiKit.T(" (wrong password?)", " (неверный пароль?)");
            if (serverPeer != null) return UiKit.T("CONNECTED to ", "ПОДКЛЮЧЁН к ") + RtAddress + ":" + RtPort + "   " + UiKit.T("(End = leave multiplayer)", "(End - выйти из мультиплеера)");
            if (lostAt >= 0f && manager != null) return UiKit.T("CONNECTION LOST, reconnecting to ", "СВЯЗЬ ПОТЕРЯНА, переподключаюсь к ") + RtAddress + ":" + RtPort + " ...   " + UiKit.T("(End = leave multiplayer)", "(End - выйти из мультиплеера)");
            return (manager != null ? UiKit.T("CONNECTING", "ПОДКЛЮЧАЮСЬ") : UiKit.T("CLIENT (connects when the city has loaded; load the same save as the host)", "КЛИЕНТ (подключусь, когда город загрузится; загрузи тот же сейв, что у хоста)")) + ": " + RtAddress + ":" + RtPort;
        }

        static Appearance remoteApp;
        static string remoteAppJson = "";
    }
}

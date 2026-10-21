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
        static bool CitizenModelAvailable()
        {
            try
            {
                var list = CityData.Instance.citizenDirectory;
                if (list == null) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c == null) continue;
                    var ac = c.animationController;
                    if (ac != null && ac.mainAnimator != null) return true;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            return false;
        }

        static void CreateAvatar()
        {
            if (Plugin.CfgUseCitizenModel.Value && !CitizenModelAvailable()) return;
            if (CharEditor.RemoteBusy) return;

            avatar = new GameObject("SodCoop_RemotePlayer" + (curRp != null ? "_" + curRp.pid : ""));

            bool ok = false;
            var app = remoteApp;
            if (app == null && Plugin.CfgDebugAvatar.Value) app = CharEditor.Load();
            if (app != null && Plugin.CfgUseCitizenModel.Value && !CharEditor.Open)
            {
                if (CharEditor.RemoteBegin(app)) { remotePending = true; remoteTries = 0; Plugin.L.LogInfo("[CHAR] собираю аватар по внешнему виду"); avatarSnap = true; return; }
                Plugin.L.LogWarning("[CHAR] не удалось начать сборку аватара, беру модель жителя");
            }
            if (Plugin.CfgUseCitizenModel.Value)
                ok = TryCreateCitizenModel(avatar);

            if (!ok)
                CreateCapsule(avatar);

            avatarSnap = true;
        }

        static bool remotePending; static int remoteTries;
        static List<Renderer> remoteLod = new List<Renderer>();

        static void PollRemoteModel()
        {
            for (int i = 0; i < players.Count; i++)
            {
                Enter(players[i]);
                try { PollRemoteModelOne(); } catch (Exception __e) { Err.Swallow(__e); }
            }
            Leave();
        }

        static void PollRemoteModelOne()
        {
            try
            {
                if (remotePending)
                {
                    if (avatar == null) { remotePending = false; CharEditor.RemoteAbort(); return; }
                    Transform m; List<Renderer> lod;
                    int st = CharEditor.RemoteStep(out m, out lod);
                    if (st == 0) return;
                    remotePending = false;
                    if (st > 0 && AttachRemoteModel(m, lod)) return;
                    Plugin.L.LogWarning("[CHAR] сборка не удалась, беру модель жителя");
                    if (avatar != null && !TryCreateCitizenModel(avatar)) CreateCapsule(avatar);
                    return;
                }
                for (int i = 0; i < remoteLod.Count; i++) { try { if (remoteLod[i] != null && remoteLod[i].enabled) remoteLod[i].enabled = false; } catch (Exception __e) { Err.Swallow(__e); } }
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] PollRemoteModel: " + e.Message); }
        }

        static bool AttachRemoteModel(Transform m, List<Renderer> lod)
        {
            try
            {
                var clone = m.gameObject;
                Vector3 lp = Vector3.zero; Quaternion lr = Quaternion.identity;
                Strip<CapsuleCollider>(clone);
                Strip<InteractableController>(clone);
                Strip<OutlineController>(clone);
                Strip<CitizenOutfitController>(clone);
                Strip<CitizenAnimationEvents>(clone);
                try
                {
                    var list = CityData.Instance.citizenDirectory;
                    for (int i = 0; list != null && i < list.Count; i++)
                    {
                        var sac = list[i] != null ? list[i].animationController : null;
                        if (sac == null || sac.mainAnimator == null) continue;
                        handNameR = sac.rightHandJoint != null ? sac.rightHandJoint.transform.name : "";
                        handNameL = sac.leftHandJoint != null ? sac.leftHandJoint.transform.name : "";
                        break;
                    }
                }
                catch (Exception __e) { Err.Swallow(__e); }
                clone.transform.SetParent(avatar.transform, false);
                clone.transform.localPosition = lp;
                clone.transform.localRotation = lr;
                clone.transform.localScale = Vector3.one;
                clone.name = "SodCoop_Model";
                avatarModel = clone.transform;
                modelBaseScale = clone.transform.localScale;
                modelBasePos = clone.transform.localPosition;
                modelBaseRot = clone.transform.localRotation;
                boneUL = FindDeep(clone.transform, "UpperLegLeft");
                boneUR = FindDeep(clone.transform, "UpperLegRight");
                boneLL = FindDeep(clone.transform, "LowerLegLeft");
                boneLR = FindDeep(clone.transform, "LowerLegRight");
                boneTorso = FindDeep(clone.transform, "Midriff");
                remoteLod = lod ?? new List<Renderer>();
                var anim = clone.GetComponent<Animator>();
                if (anim == null) anim = clone.GetComponentInChildren<Animator>(true);
                if (anim != null)
                {
                    anim.enabled = false; anim.fireEvents = false; anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    avatarAnim = anim;
                    for (int li = 1; li < anim.layerCount; li++) anim.SetLayerWeight(li, 0f);
                    anim.SetBool("armsLock", false); anim.SetBool("carryingItem", false); anim.SetBool("restrained", false);
                    anim.SetInteger("carryItem", 0);
                    anim.SetInteger("ArmsState", Plugin.CfgArmsState.Value);
                    anim.SetInteger("IdleAnimationState", Plugin.CfgIdleState.Value);
                }
                var rs = clone.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++) { try { if (rs[i] != null) rs[i].renderingLayerMask = 0xFFFFFFFFu; } catch (Exception __e) { Err.Swallow(__e); } }
                avatarSnap = true;
                Plugin.L.LogInfo($"[CHAR] аватар собран по внешнему виду: рендереров {rs.Length}, аниматор={(anim != null)}, кости ноги={boneUL != null && boneUR != null}");
                return anim != null;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] AttachRemoteModel: " + e); return false; }
        }

        static bool TryCreateCitizenModel(GameObject root)
        {
            try
            {
                var city = CityData.Instance;
                GameObject src = null;
                Citizen srcCit = null;

                for (int i = 0; i < city.citizenDirectory.Count; i++)
                {
                    var c = city.citizenDirectory[i];
                    if (c == null) continue;
                    var ac = c.animationController;
                    if (ac == null || ac.mainAnimator == null) continue;
                    src = ac.mainAnimator.gameObject;
                    srcCit = c;
                    break;
                }

                if (src == null)
                {
                    Plugin.L.LogWarning("[NET] Не нашёл NPC с моделью, использую капсулу");
                    return false;
                }

                var holder = new GameObject("SodCoop_Holder");
                holder.SetActive(false);
                var clone = UnityEngine.Object.Instantiate<GameObject>(src, holder.transform);

                try
                {
                    var sac = srcCit.animationController;
                    handNameR = sac.rightHandJoint != null ? sac.rightHandJoint.transform.name : "";
                    handNameL = sac.leftHandJoint != null ? sac.leftHandJoint.transform.name : "";
                    var jr = sac.rightHandJoint != null ? sac.rightHandJoint.transform : null;
                    string chainR = "";
                    for (int d = 0; jr != null && d < 6; d++, jr = jr.parent) chainR += jr.name + " < ";
                    Plugin.Dbg($"[HELD] суставы рук NPC: правая='{handNameR}' левая='{handNameL}' цепочка={chainR}");
                }
                catch (Exception e) { Plugin.L.LogWarning($"[HELD] суставы рук: {e.Message}"); }
                Strip<CapsuleCollider>(clone);
                Strip<InteractableController>(clone);
                Strip<OutlineController>(clone);
                Strip<CitizenOutfitController>(clone);
                Strip<CitizenAnimationEvents>(clone);

                clone.transform.SetParent(root.transform, false);
                clone.transform.localPosition = src.transform.localPosition;
                clone.transform.localRotation = src.transform.localRotation;
                clone.transform.localScale = src.transform.localScale;
                clone.name = "SodCoop_Model";
                avatarModel = clone.transform;
                modelBaseScale = clone.transform.localScale;
                modelBasePos = clone.transform.localPosition;
                modelBaseRot = clone.transform.localRotation;
                boneUL = FindDeep(clone.transform, "UpperLegLeft");
                boneUR = FindDeep(clone.transform, "UpperLegRight");
                boneLL = FindDeep(clone.transform, "LowerLegLeft");
                boneLR = FindDeep(clone.transform, "LowerLegRight");
                boneTorso = FindDeep(clone.transform, "Midriff");
                Plugin.L.LogInfo($"[NET] Кости: UL={boneUL != null} UR={boneUR != null} LL={boneLL != null} LR={boneLR != null} torso={boneTorso != null}");
                clone.SetActive(true);
                UnityEngine.Object.Destroy(holder);

                var anim = clone.GetComponent<Animator>();
                if (anim != null)
                {
                    anim.enabled = false;
                    anim.fireEvents = false;
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    avatarAnim = anim;
                    try
                    {
                        for (int li = 1; li < anim.layerCount; li++) anim.SetLayerWeight(li, 0f);
                        anim.SetBool("armsLock", false);
                        anim.SetBool("carryingItem", false);
                        anim.SetBool("restrained", false);
                        anim.SetInteger("carryItem", 0);
                        anim.SetInteger("ArmsState", Plugin.CfgArmsState.Value);
                        anim.SetInteger("IdleAnimationState", Plugin.CfgIdleState.Value);
                        Plugin.L.LogInfo($"[NET] Аниматор: слоёв={anim.layerCount} ArmsState={Plugin.CfgArmsState.Value} IdleAnimationState={Plugin.CfgIdleState.Value}");
                    }
                    catch (Exception ex) { Plugin.L.LogWarning($"[NET] Не удалось настроить аниматор: {ex.Message}"); }
                }

                var rends = clone.GetComponentsInChildren<MeshRenderer>(true);
                int total = 0, on = 0;
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null) continue;
                    total++;
                    if (!IsLodPart(r.transform)) r.enabled = true;
                    if (r.enabled) on++;
                }

                Plugin.L.LogInfo($"[NET] Аватар: клон модели NPC {srcCit.citizenName}, " +
                                 $"рендереров {on}/{total}, аниматор={(anim != null)}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"[NET] Не удалось клонировать модель, использую капсулу: {e}");
                return false;
            }
        }

        static void Strip<T>(GameObject go) where T : Component
        {
            var arr = go.GetComponentsInChildren<T>(true);
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null) UnityEngine.Object.DestroyImmediate(arr[i]);
            }
        }

        static bool IsLodPart(Transform t)
        {
            for (int d = 0; d < 20 && t != null; d++)
            {
                string n = t.name;
                if (n.Contains("LODS") || n.StartsWith("Level0")) return true;
                t = t.parent;
            }
            return false;
        }

        static void CreateCapsule(GameObject root)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Capsule";
            RemoveCollider(body);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);

            var mat = MakeMaterial();
            var r = body.GetComponent<Renderer>();
            if (r != null && mat != null) r.material = mat;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            RemoveCollider(nose);
            nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.5f, 0.6f);
            nose.transform.localScale = new Vector3(0.5f, 0.3f, 0.6f);
            var nr = nose.GetComponent<Renderer>();
            if (nr != null && mat != null) nr.material = mat;

            Plugin.L.LogInfo("[NET] Аватар: капсула");
        }

        static Material MakeMaterial()
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null) shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Plugin.L.LogWarning("[NET] Шейдеры HDRP не найдены");
                return null;
            }
            var m = new Material(shader);
            m.SetColor("_UnlitColor", Color.green);
            m.SetColor("_BaseColor", Color.green);
            m.SetColor("_Color", Color.green);
            return m;
        }

        static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
        }
    }
}

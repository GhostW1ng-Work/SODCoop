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
    [HarmonyPatch(typeof(FirstPersonItemController), "ThrowFood")]
    public static class ThrowFoodPatch { [HarmonyPostfix] public static void Postfix() { Net.SendAction(3); } }
    [HarmonyPatch(typeof(FirstPersonItemController), "ThrowGrenade")]
    public static class ThrowGrenadePatch { [HarmonyPostfix] public static void Postfix() { Net.SendAction(3); } }
    [HarmonyPatch(typeof(FirstPersonItemController), "ThrowCoin")]
    public static class ThrowCoinPatch { [HarmonyPostfix] public static void Postfix() { Net.SendAction(3); } }
    [HarmonyPatch(typeof(FirstPersonItemController), "PutDown")]
    public static class PutDownPatch { [HarmonyPostfix] public static void Postfix() { Net.SendAction(4); } }
    [HarmonyPatch(typeof(FirstPersonItemController), "EmptySlot")]
    public static class EmptySlotPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool throwObject)
        {
            Plugin.Dbg($"[ITEM] EmptySlot throw={throwObject}");
            if (throwObject) Net.SendAction(3);
        }
    }
    [HarmonyPatch(typeof(FirstPersonItemController), "PickUpItem")]
    public static class PickUpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Interactable pickUpThis, bool __result)
        {
            try { Plugin.Dbg($"[ITEM] PickUpItem: {(pickUpThis != null ? pickUpThis.name : "null")} успех={__result}"); } catch (Exception __e) { Err.Swallow(__e); }
            if (__result && pickUpThis != null) Net.OnItemPickedUp(pickUpThis);
        }
    }

    [HarmonyPatch(typeof(Interactable), "SetAsNotInventory")]
    public static class DropPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Interactable __instance, ref bool __state)
        {
            __state = false;
            try { var inv = __instance.inInventory; __state = inv != null && inv.isPlayer; } catch (Exception __e) { Err.Swallow(__e); }
        }
        [HarmonyPostfix]
        public static void Postfix(Interactable __instance, bool __state)
        {
            if (__state) Net.OnItemDropped(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnCrawlIntoVent")]
    public static class VentInPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { try { Net.SetInVent(true); } catch (Exception e) { Err.Swallow(e); } }
    }

    [HarmonyPatch(typeof(Player), "OnCrawlOutOfVent")]
    public static class VentOutPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { try { Net.SetInVent(false); } catch (Exception e) { Err.Swallow(e); } }
    }

    [HarmonyPatch(typeof(Player), "RecieveDamage")]
    public static class GodPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() { return !(Plugin.CfgGod.Value && Net.CheatsActive); }
    }

    [HarmonyPatch(typeof(NewDoor), "SetOpen")]
    public static class DoorOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewDoor __instance, float newAjar, Actor actor, bool skipAnimation)
        {
            Net.OnDoorOpen(__instance, newAjar, actor, skipAnimation);
        }
    }

    [HarmonyPatch(typeof(NewDoor), "SetLocked")]
    public static class DoorLockPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewDoor __instance, bool val, Actor actor)
        {
            Net.OnDoorFlag(2, __instance, val, actor);
        }
    }

    [HarmonyPatch(typeof(NewDoor), "SetJammed")]
    public static class DoorJamPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewDoor __instance, bool val)
        {
            Net.OnDoorFlag(3, __instance, val, null);
        }
    }

    [HarmonyPatch(typeof(DoorMovementController), "OnOpen")]
    public static class DoorMoveOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DoorMovementController __instance, Actor interactor)
        {
            Net.OnMoveDoor(__instance, true, interactor);
        }
    }

    [HarmonyPatch(typeof(DoorMovementController), "OnClose")]
    public static class DoorMoveClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(DoorMovementController __instance, Actor interactor)
        {
            Net.OnMoveDoor(__instance, false, interactor);
        }
    }

    [HarmonyPatch(typeof(DoorMovementController), "SetOpen")]
    public static class DoorMoveSetOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DoorMovementController __instance, float newAjar, Actor interactor)
        {
            Net.OnMoveDoor(__instance, newAjar > 0.01f, interactor);
        }
    }

    [HarmonyPatch(typeof(Interactable), "SetSwitchState")]
    public static class SwitchStatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Interactable __instance, bool val, Actor interactor)
        {
            Net.OnSwitchState(__instance, 0, val, interactor);
        }
    }

    [HarmonyPatch(typeof(NewRoom), "SetMainLights")]
    public static class RoomLightsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewRoom __instance, bool newVal, Actor actor)
        {
            Net.OnRoomLights(__instance, newVal, actor);
        }
    }

    [HarmonyPatch(typeof(Interactable), "OnInteraction", new System.Type[] { typeof(InteractablePreset.InteractionAction), typeof(Actor), typeof(bool), typeof(float) })]
    public static class InteractActionPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Actor who) { Net.MarkPlayerPress(who); }
    }

    [HarmonyPatch(typeof(Interactable), "OnInteraction", new System.Type[] { typeof(InteractablePreset.InteractionKey), typeof(Actor) })]
    public static class InteractKeyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Actor who) { Net.MarkPlayerPress(who); }
    }

    [HarmonyPatch(typeof(Interactable), "SetCustomState1")]
    public static class CustomState1Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Interactable __instance, bool val, Actor interactor) { Net.OnSwitchState(__instance, 1, val, interactor); }
    }

    [HarmonyPatch(typeof(Interactable), "SetCustomState2")]
    public static class CustomState2Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Interactable __instance, bool val, Actor interactor) { Net.OnSwitchState(__instance, 2, val, interactor); }
    }

    [HarmonyPatch(typeof(Interactable), "SetCustomState3")]
    public static class CustomState3Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Interactable __instance, bool val, Actor interactor) { Net.OnSwitchState(__instance, 3, val, interactor); }
    }

    [HarmonyPatch(typeof(BreakerController), "SetOpen")]
    public static class BreakerSetOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BreakerController __instance, float newAjar, Actor interactor)
        {
            Net.OnMoveDoor(__instance, newAjar > 0.01f, interactor);
        }
    }

    [HarmonyPatch(typeof(Interactable), "SetLockedState")]
    public static class LockStatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Interactable __instance, bool val, Actor interactor)
        {
            Net.OnLockState(__instance, val, interactor);
        }
    }

    [HarmonyPatch(typeof(Actor), "SightingCheck")]
    public static class InvisSightPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Actor __instance)
        {
            if (!(Plugin.CfgInvisible.Value && Net.CheatsActive)) return true;
            try { return __instance.isPlayer; } catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(Actor), "AddToSeesIllegal")]
    public static class InvisIllegalPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Actor newTarget)
        {
            if (!(Plugin.CfgInvisible.Value && Net.CheatsActive)) return true;
            try { return newTarget == null || !newTarget.isPlayer; } catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(Interactable), "SafeDelete")]
    public static class SafeDeletePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Interactable __instance) { Net.OnItemDeleting(__instance); }
    }

    [HarmonyPatch(typeof(InteractableController), "BreakObject")]
    public static class BreakPatch
    {
        [HarmonyPrefix]
        public static void Prefix(InteractableController __instance, Vector3 contactPoint, Vector3 normal, float magnitude)
        {
            Net.OnItemBroken(__instance, contactPoint, normal, magnitude);
        }
    }

    [HarmonyPatch(typeof(InteractableController), "Shatter")]
    public static class ShatterPatch
    {
        [HarmonyPrefix]
        public static void Prefix(InteractableController __instance, Vector3 contact, float force)
        {
            Plugin.L.LogInfo($"[BREAK] вызван Shatter: {__instance.gameObject.name} сила={force:F1}");
            Net.OnWorldBreak(__instance, 0, contact, Vector3.up, force);
        }
    }

    [HarmonyPatch(typeof(SessionData), "ExecuteWeatherChange")]
    public static class WeatherChangePatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !Net.ClientFollowsHostWeather();
        }
    }

    [HarmonyPatch(typeof(InteractionController), "OnPlayerLookAtChange")]
    public static class PuppetLookPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InteractionController __instance)
        {
            try { Net.FilterPuppetLook(__instance); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch(typeof(Player), "TriggerPlayerKO")]
    public static class DownedKoPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Vector3 __0)
        {
            try { return !Net.TryGoDown(__0); } catch (Exception e) { Plugin.L.LogWarning("[DOWN] " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(Player), "RecieveDamage")]
    public static class DownedDamagePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(float __0)
        {
            return !Net.DownedHit(__0);
        }
    }

    [HarmonyPatch]
    public static class ProfileEventPatch
    {
        static readonly (System.Type, string[])[] targets = {
            (typeof(GameplayController), new[] { "AddMoney", "SetMoney", "SetLockpicks", "AddLockpicks", "AddPasscode", "SetPlayerKnowsPassword" }),
            (typeof(Player), new[] { "AddToKeyring", "RemoveFromKeyring" }),
            (typeof(UpgradesController), new[] { "InstallSyncDisk", "UninstallSyncDisk", "UpgradeSyncDisk" }),
            (typeof(PlayerApartmentController), new[] { "BuyNewResidence" }),
            (typeof(FirstPersonItemController), new[] { "PickUpItem", "EmptySlot", "OnConsumableFinished" }),
        };

        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var (t, names) in targets)
                foreach (var m in t.GetMethods(flags))
                    if (System.Array.IndexOf(names, m.Name) >= 0) yield return m;
        }

        [HarmonyPostfix]
        public static void Postfix(System.Reflection.MethodBase __originalMethod)
        {
            try { Net.ProfileSoon(__originalMethod.Name); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch(typeof(GameplayController), "KnockOnDoor")]
    public static class KnockPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NewDoor __0, Actor __1, int __2, float __3)
        {
            try { Net.OnLocalKnock(__0, __1, __2, __3); } catch (Exception e) { Plugin.L.LogWarning("[KNOCK] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(NewDoor), "Barge")]
    public static class BargePatch
    {
        [HarmonyPrefix]
        public static void Prefix(NewDoor __instance, Actor __0)
        {
            try { Net.OnLocalKnock(__instance, __0, -1, 0f); } catch (Exception e) { Plugin.L.LogWarning("[KNOCK] " + e.Message); }
        }
    }

    [HarmonyPatch]
    public static class JobCreationPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SideJobController), "JobCreationCheck");
            yield return AccessTools.Method(typeof(SideJobController), "DeadPeopleJobCheck");
            yield return typeof(SideJob).GetMethod("PostJob", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            var mp = typeof(SideJobMissingPerson).GetMethod("PostJob", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            if (mp != null) yield return mp;
        }

        [HarmonyPrefix]
        public static bool Prefix(System.Reflection.MethodBase __originalMethod)
        {
            if (!Net.BlockLocalJobCreation()) return true;
            if (__originalMethod.Name == "JobCreationCheck") { try { Net.ClientJobWorldLoop(); } catch (Exception e) { Plugin.L.LogWarning("[JOB] " + e.Message); } }
            return false;
        }
    }

    [HarmonyPatch]
    public static class JobAcceptPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var t in new[] { typeof(SideJob), typeof(SideJobAffair), typeof(SideJobSabotage), typeof(SideJobStolenItem), typeof(SideJobMissingPerson), typeof(SideJobRevenge), typeof(SideJobStealBriefcase) })
            {
                var m = t.GetMethod("AcceptJob", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (m != null) yield return m;
            }
        }

        static int depth;

        [HarmonyPrefix]
        public static bool Prefix(SideJob __instance, out bool __state)
        {
            __state = false;
            if (depth == 0 && __instance != null && __instance.accepted)
            {
                Plugin.L.LogInfo($"[JOB] задание {__instance.jobID} уже взято - второй раз не беру");
                return false;
            }
            if (depth == 0 && __instance != null && !Net.EnsureJobPost(__instance)) return false;
            depth++; __state = true;
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(SideJob __instance, bool __state)
        {
            if (!__state || depth > 1) return;
            try { Net.OnJobAccepted(__instance); } catch (Exception e) { Plugin.L.LogWarning("[JOB] " + e.Message); }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state) depth--;
            return __exception;
        }
    }

    [HarmonyPatch]
    public static class PinnedMenuPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PinnedItemController), "Setup");
            yield return AccessTools.Method(typeof(PinnedItemController), "UpdateContextMenuOptions");
        }

        [HarmonyPostfix]
        public static void Postfix(PinnedItemController __instance)
        {
            try { Net.AddShareMenuItem(__instance); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch(typeof(ContextButtonController), "OnLeftClick")]
    public static class ShareCommandPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ContextButtonController __instance)
        {
            try { return !Net.HandleShareCommand(__instance != null ? __instance.cmc : null, __instance); } catch (Exception e) { Plugin.L.LogWarning("[JOB] " + e.Message); return true; }
        }
    }

    [HarmonyPatch]
    public static class PhoneCallLogPatch
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TelephoneController), "CreateNewCall", new[] { typeof(int), typeof(int), typeof(Human), typeof(Human), typeof(TelephoneController.CallSource), typeof(float), typeof(bool) });
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, int __1, Human __2, Human __3, TelephoneController.CallSource __4, float __5, bool __6, TelephoneController.PhoneCall __result)
        {
            try { Net.LogPhoneCall(__0, __1, __2, __3, __4, __result, __5, __6); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch]
    public static class PhoneCallLogPatch2
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TelephoneController), "CreateNewCall", new[] { typeof(Telephone), typeof(Telephone), typeof(Human), typeof(Human), typeof(TelephoneController.CallSource), typeof(float), typeof(bool) });
        }

        [HarmonyPostfix]
        public static void Postfix(Telephone __0, Telephone __1, Human __2, Human __3, TelephoneController.CallSource __4, float __5, bool __6, TelephoneController.PhoneCall __result)
        {
            try { Net.LogPhoneCall(__0 != null ? __0.number : -1, __1 != null ? __1.number : -1, __2, __3, __4, __result, __5, __6); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch]
    public static class MurderClientBlockPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            var mc = new System.Collections.Generic.HashSet<string> { "Tick", "PickNewMurderer", "PickNewVictim", "ExecuteNewMurder", "TriggerNextMurder", "SpawnItemsCheck", "SpawnItem", "SpawnPlayerTaunt", "ExecuteSniperShot" };
            foreach (var m in typeof(MurderController).GetMethods(bf)) if (mc.Contains(m.Name)) yield return m;
            var mu = new System.Collections.Generic.HashSet<string> { "CreateMurderGoal", "PickNewMurderWeapon", "PickNewCallingCard", "PlaceCallingCard", "GenerateGraffiti", "WeaponDisposal", "GenerateRansomDetails", "GenerateFakeNumber", "GenerateMoniker", "TryPickNewVictimSite", "OnCleanCrimeScene" };
            foreach (var m in typeof(MurderController.Murder).GetMethods(bf)) if (mu.Contains(m.Name)) yield return m;
        }

        [HarmonyPrefix]
        public static bool Prefix() { return !Net.BlockLocalMurders(); }
    }

    [HarmonyPatch(typeof(MurderController), "OnVictimDiscovery")]
    public static class MurderDiscoveryPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { Net.OnVictimDiscovered(); } catch (Exception e) { Plugin.L.LogWarning("[MURDER] находка: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GameplayController), "NewMurderCaseNotify")]
    public static class MurderCaseNotifyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewGameLocation __0)
        {
            try { Net.OnMurderCaseNotify(__0); } catch (Exception e) { Plugin.L.LogWarning("[MURDER] дело: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Actor), "UpdateTrespassing")]
    public static class PuppetTrespassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Actor __instance)
        {
            try { return !Net.PuppetIllegalOverride(__instance); } catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(MapController), "PressTracked")]
    public static class MapMarkerPressPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ButtonController pressedButton)
        {
            try { return !Net.OnMapMarkerPressed(pressedButton); } catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(NewBuilding), "SetAlarm")]
    public static class BuildingAlarmPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() { return Net.AlarmAllowed(); }

        [HarmonyPostfix]
        public static void Postfix(NewBuilding __instance, bool __0, Human __1, NewFloor __2)
        {
            try { Net.OnBuildingAlarm(__instance, __0, __1, __2); } catch (Exception e) { Plugin.L.LogWarning("[ALARM] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(NewAddress), "SetAlarm")]
    public static class AddressAlarmPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() { return Net.AlarmAllowed(); }

        [HarmonyPostfix]
        public static void Postfix(NewAddress __instance, bool __0, Human __1)
        {
            try { Net.OnAddressAlarm(__instance, __0, __1); } catch (Exception e) { Plugin.L.LogWarning("[ALARM] " + e.Message); }
        }
    }

    [HarmonyPatch]
    public static class ComputerPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ComputerController), "SetComputerApp");
            yield return AccessTools.Method(typeof(ComputerController), "SetLoggedIn");
        }

        [HarmonyPostfix]
        public static void Postfix(ComputerController __instance)
        {
            try { Net.OnComputer(__instance); } catch (Exception e) { Plugin.L.LogWarning("[PC] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(EvidenceMultiPage), "SetPage")]
    public static class PagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(EvidenceMultiPage __instance, int __0)
        {
            try { Net.OnPage(__instance, __0); } catch (Exception e) { Plugin.L.LogWarning("[PAGE] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Objective), "Complete")]
    public static class ObjectiveCompletePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Objective __instance, out bool __state) { __state = __instance != null && __instance.isComplete; }

        [HarmonyPostfix]
        public static void Postfix(Objective __instance, bool __state)
        {
            try { if (!__state) Net.OnObjectiveCompleted(__instance); } catch (Exception e) { Plugin.L.LogWarning("[JOB] цель: " + e.Message); }
        }
    }

    [HarmonyPatch]
    public static class ObjectiveAddPatch
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            return Net.AddObjectiveMethod();
        }

        [HarmonyPrefix]
        public static void Prefix(Case __instance, object[] __args, out bool __state)
        {
            __state = false;
            try { __state = Net.HasObjective(__instance, __args[0] as string, false); } catch (Exception __e) { Err.Swallow(__e); }
        }

        [HarmonyPostfix]
        public static void Postfix(Case __instance, object[] __args, bool __state)
        {
            try { Net.NoteJobObjective(__instance); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (Net.IsRewardObjective(__args[0] as string)) { Net.NoteRewardItem(__args[1] as Objective.ObjectiveTrigger); return; } } catch (Exception __e) { Err.Swallow(__e); }
            if (__state) return;
            try { Net.OnObjectiveAdded(__instance, __args); } catch (Exception e) { Plugin.L.LogWarning("[JOB] новая цель: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SideJob), "OnRewarded")]
    public static class RewardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SideJob __instance, out int __state)
        {
            __state = Interactable.worldAssignID;
            try { return !Net.SkipJobReward(__instance); } catch (Exception e) { Err.Swallow(e); return true; }
        }

        [HarmonyPostfix]
        public static void Postfix(SideJob __instance, int __state)
        {
            try { Net.OnRewarded(__instance, __state); } catch (Exception e) { Plugin.L.LogWarning("[JOB] награда в ящике: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Human), "SetVisible")]
    public static class PuppetVisiblePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Human __instance, bool vis)
        {
            try { Net.OnHumanSetVisible(__instance, vis); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch(typeof(MainMenuController), "SetMenuComponent", new[] { typeof(MainMenuController.Component) })]
    public static class MenuComponentPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MainMenuController.Component newComponent)
        {
            try { Net.OnMenuComponent((int)newComponent); } catch (Exception e) { Plugin.L.LogWarning("[RELOAD] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MainMenuController), "SetMenuComponent", new[] { typeof(int) })]
    public static class MenuComponentIntPatch
    {
        [HarmonyPrefix]
        public static void Prefix(int newComponent)
        {
            try { Net.OnMenuComponent(newComponent); } catch (Exception e) { Plugin.L.LogWarning("[RELOAD] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Objective), "Cancel")]
    public static class ObjectiveCancelPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Objective __instance)
        {
            try { Net.OnObjectiveCancel(__instance); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch(typeof(SideJob), "OnAcquireJobInfo", new[] { typeof(string) })]
    public static class JobInfoPatch
    {
        [HarmonyPrefix]
        public static void Prefix(SideJob __instance, out bool __state) { __state = __instance != null && __instance.knowHandInLocation; }

        [HarmonyPostfix]
        public static void Postfix(SideJob __instance, string __0, bool __state)
        {
            try { if (!__state && __instance != null && __instance.knowHandInLocation) Net.OnJobInfoAcquired(__instance, __0); } catch (Exception e) { Plugin.L.LogWarning("[JOB] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Case), "Resolve")]
    public static class CaseResolvePatch
    {
        [HarmonyPrefix]
        public static void Prefix(out double[] __state) { __state = new[] { Net.MoneyNow(), Net.SocialNow() }; }

        [HarmonyPostfix]
        public static void Postfix(Case __instance, double[] __state)
        {
            try { Net.OnCaseResolved(__instance, __state[0]); } catch (Exception e) { Plugin.L.LogWarning("[JOB] " + e.Message); }
            try { Net.ApplyRewardRule(__instance, __state[0], __state[1]); } catch (Exception e) { Plugin.L.LogWarning("[RULES] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Human), "UpdateTickRateOnProx")]
    public static class TickRatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Human __instance)
        {
            long t0 = Perf.T();
            try { Net.RaiseTickNearPlayers(__instance); } catch (Exception e) { Err.Swallow(e); }
            Perf.Add("частота жителей (патч)", t0);
        }
    }

    [HarmonyPatch(typeof(DialogController), "TestSpecialCaseAvailability")]
    public static class LookAroundPatch
    {
        [HarmonyPrefix]
        public static void Prefix(DialogPreset preset, Citizen saysTo)
        {
            try { Net.BeforeLookAround(preset, saysTo); } catch (Exception __e) { Err.Swallow(__e); }
        }

        [HarmonyPostfix]
        public static void Postfix(DialogPreset preset, Citizen saysTo, bool __result)
        {
            try { Net.AfterLookAround(preset, saysTo, __result); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch(typeof(Actor), "AddToKeyring", new[] { typeof(NewAddress), typeof(bool) })]
    public static class KeyringPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Actor __instance, NewAddress __0)
        {
            try { Net.OnKeyringAddress(__instance, __0); } catch (Exception e) { Plugin.L.LogWarning("[JOB] ключи: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SideJob), "AddDialogOption")]
    public static class JobDialogAddPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SideJob __instance, Human person, Evidence.DataKey key, DialogPreset newPreset, NewRoom roomRef)
        {
            try { Net.OnJobDialogAdded(__instance, person, key, newPreset, roomRef); } catch (Exception e) { Plugin.L.LogWarning("[JOB] фраза задания: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SceneRecorder), "ExecuteCapture")]
    public static class CctvCapturePatch
    {
        [HarmonyPostfix]
        public static void Postfix(SceneRecorder.SceneCapture __result)
        {
            try { Net.OnCctvCapture(__result != null); } catch (Exception __e) { Err.Swallow(__e); }
        }
    }

    [HarmonyPatch]
    public static class JobLoopPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var t in new[] { typeof(SideJob), typeof(SideJobMissingPerson), typeof(SideJobSabotage), typeof(SideJobStealBriefcase) })
            {
                var m = t.GetMethod("GameWorldLoop", bf);
                if (m != null) yield return m;
            }
            var o = typeof(SideJob).GetMethod("ObjectiveStateLoop", bf);
            if (o != null) yield return o;
        }

        [HarmonyPrefix]
        public static bool Prefix(SideJob __instance, out bool __state)
        {
            __state = false;
            if (Net.MirrorJob(__instance)) return false;
            __state = true; Net.JobLoopDepth++;
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(bool __state)
        {
            if (__state && Net.JobLoopDepth > 0) Net.JobLoopDepth--;
        }
    }

    [HarmonyPatch]
    public static class JobPinPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in typeof(CasePanelController).GetMethods())
                if (m.Name == "PinToCasePanel" && m.GetParameters().Length == 6) yield return m;
        }

        [HarmonyPostfix]
        public static void Postfix(object[] __args)
        {
            try
            {
                var c = __args[0] as Case; var ev = __args[1] as Evidence;
                var keys = __args[2] as Il2CppSystem.Collections.Generic.List<Evidence.DataKey>;
                if (keys == null && __args[2] is Evidence.DataKey)
                {
                    keys = new Il2CppSystem.Collections.Generic.List<Evidence.DataKey>();
                    keys.Add((Evidence.DataKey)__args[2]);
                }
                Net.OnJobPin(c, ev, keys);
            }
            catch (Exception e) { Err.Swallow(e); }
        }
    }

    [HarmonyPatch(typeof(GameplayController), "AddGuestPass", new[] { typeof(NewAddress), typeof(float) })]
    public static class JobGuestPassPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewAddress loc, float forHours)
        {
            try { Net.OnJobGuestPass(loc, forHours); } catch (Exception e) { Err.Swallow(e); }
        }
    }

    [HarmonyPatch(typeof(InteractionController), "Update")]
    public static class CheatMenuInteractionBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() { return !Net.CheatMenuOpen; }
    }

    [HarmonyPatch(typeof(FirstPersonItemController), "Update")]
    public static class CheatMenuItemBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() { return !Net.CheatMenuOpen; }
    }

    [HarmonyPatch(typeof(SideJob), "OnGooseChaseSuccess")]
    public static class GooseChasePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SideJob __instance)
        {
            try { return Net.OnGooseChaseSuccess(__instance); } catch (Exception e) { Plugin.L.LogWarning("[JOB] погоня: " + e.Message); return true; }
        }
    }

    [HarmonyPatch]
    public static class WorldAlivePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SessionData), "Update");
            yield return AccessTools.Method(typeof(CitizenBehaviour), "Update");
            yield return AccessTools.Method(typeof(CitizenBehaviour), "LateUpdate");
            yield return AccessTools.Method(typeof(CitizenBehaviour), "RoutineCheck");
            yield return AccessTools.Method(typeof(CitizenBehaviour), "GameWorldCheck");
            yield return AccessTools.Method(typeof(CitizenBehaviour), "LightLevelLoop");
            yield return AccessTools.Method(typeof(MurderController), "Update");
        }

        [HarmonyPrefix]
        public static void Prefix(out bool __state) { __state = Net.ForcePlayBegin(); }

        [HarmonyPostfix]
        public static void Postfix(bool __state) { if (__state) Net.ForcePlayEnd(); }
    }

    [HarmonyPatch(typeof(CitizenBehaviour), "GameSpeedChange")]
    public static class KeepInvokesPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CitizenBehaviour __instance) { return Net.OnGameSpeedChange(__instance); }
    }

    [HarmonyPatch(typeof(SessionData), "PauseGame")]
    public static class PauseReentryPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() { return !Net.ForcingPlay; }
    }

    [HarmonyPatch(typeof(Case), "SetStatus")]
    public static class CaseStatusPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Case __instance, Case.CaseStatus newStatus)
        {
            try { Net.OnCaseStatus(__instance, newStatus); } catch (Exception e) { Plugin.L.LogWarning("[JOB] бланк: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(NewAIController), "SetRestrained")]
    public static class RestrainPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewAIController __instance, bool val, float duration)
        {
            try { Net.OnRestrained(__instance, val, duration); } catch (Exception e) { Plugin.L.LogWarning("[NPC] наручники: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Elevator), "CallElevator")]
    public static class ElevatorCallPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Elevator __instance, int __0, bool __1)
        {
            try { Net.OnElevatorCall(__instance, __0, __1); } catch (Exception e) { Plugin.L.LogWarning("[LIFT] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveStateController), "CaptureSaveStateAsync")]
    public static class GameSavePatch
    {
        [HarmonyPrefix]
        public static void Prefix(string __0)
        {
            try { Net.OnGameSaving(__0); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] снимок при сохранении: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CityConstructor), "StartLoading")]
    public static class WorldLoadPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.OnWorldLoading(); }
    }

    [HarmonyPatch(typeof(SessionData), "SetTimeSpeed")]
    public static class KoTimeSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SessionData.TimeSpeed __0)
        {
            Net.AfterTimeSpeed(__0);
        }
    }

    [HarmonyPatch(typeof(NewRoom), "RemoveAllInhabitantFurniture")]
    public static class RoomClearPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NewRoom __instance, bool removeSkipAddressInhabitantsFurniture, FurnitureClusterLocation.RemoveInteractablesOption spawnedOnFurnitureRemovalOption)
        {
            Net.OnRoomCleared(__instance, removeSkipAddressInhabitantsFurniture, (int)spawnedOnFurnitureRemovalOption);
        }
    }

    [HarmonyPatch(typeof(PlayerApartmentController), "ApplyDecor")]
    public static class DecorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerApartmentController __instance, MaterialGroupPreset.MaterialType decorType, MaterialGroupPreset material, Toolbox.MaterialKey key, bool saveChanges)
        {
            Net.OnDecorApplied(__instance, decorType, material, key, saveChanges);
        }
    }

    [HarmonyPatch(typeof(NewAIController), "AddTrackedTarget")]
    public static class PuppetTrackPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewAIController __instance, Actor __0) { Net.PuppetNote("заметил", __instance, __0); }
    }

    [HarmonyPatch(typeof(NewAIController), "SetPersue")]
    public static class PuppetPersuePatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewAIController __instance, Actor __0) { Net.PuppetNote("преследует", __instance, __0); }
    }

    [HarmonyPatch(typeof(NewAIController), "StartAttack")]
    public static class PuppetAttackPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewAIController __instance, Actor __0) { Net.PuppetNote("нападает на", __instance, __0); }
    }

    [HarmonyPatch(typeof(NewAIController), "Investigate")]
    public static class PuppetInvestigatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewAIController __instance, Actor __2) { Net.PuppetNote("проверяет", __instance, __2); }
    }

    [HarmonyPatch(typeof(CitizenAnimationController), "AttackTrigger")]
    public static class NpcActAttackPatch { [HarmonyPostfix] public static void Postfix(CitizenAnimationController __instance) { Net.NpcActAc(__instance, 1, 0f, false, Vector3.zero); } }
    [HarmonyPatch(typeof(CitizenAnimationController), "ThrowTrigger")]
    public static class NpcActThrowPatch { [HarmonyPostfix] public static void Postfix(CitizenAnimationController __instance) { Net.NpcActAc(__instance, 2, 0f, false, Vector3.zero); } }
    [HarmonyPatch(typeof(CitizenAnimationController), "AbortAttackTrigger")]
    public static class NpcActAbortPatch { [HarmonyPostfix] public static void Postfix(CitizenAnimationController __instance) { Net.NpcActAc(__instance, 3, 0f, false, Vector3.zero); } }
    [HarmonyPatch(typeof(CitizenAnimationController), "BlockTrigger")]
    public static class NpcActBlockPatch { [HarmonyPostfix] public static void Postfix(CitizenAnimationController __instance, float __0, bool __1) { Net.NpcActAc(__instance, 4, __0, __1, Vector3.zero); } }
    [HarmonyPatch(typeof(CitizenAnimationController), "TakeDamageRecoil")]
    public static class NpcActRecoilPatch { [HarmonyPostfix] public static void Postfix(CitizenAnimationController __instance, Vector3 __0) { Net.NpcActAc(__instance, 5, 0f, false, __0); } }
    [HarmonyPatch(typeof(NewAIController), "UpdateHeldItems")]
    public static class NpcActHeldPatch { [HarmonyPostfix] public static void Postfix(NewAIController __instance, AIActionPreset.ActionStateFlag __0) { Net.NpcActAi(__instance, 10, (int)__0); } }
    [HarmonyPatch(typeof(NewAIController), "DespawnRightItem")]
    public static class NpcActDespawnRPatch { [HarmonyPostfix] public static void Postfix(NewAIController __instance) { Net.NpcActAi(__instance, 11, 0); } }
    [HarmonyPatch(typeof(NewAIController), "DespawnLeftItem")]
    public static class NpcActDespawnLPatch { [HarmonyPostfix] public static void Postfix(NewAIController __instance) { Net.NpcActAi(__instance, 12, 0); } }
    [HarmonyPatch(typeof(NewAIController), "UpdateCurrentWeapon")]
    public static class NpcActWeaponPatch { [HarmonyPostfix] public static void Postfix(NewAIController __instance) { Net.NpcActAi(__instance, 13, 0); } }

    [HarmonyPatch(typeof(AudioController), "PlayWorldOneShot")]
    public static class WorldSoundPatch
    {
        [HarmonyPostfix]
        public static void Postfix(AudioEvent __0, Actor __1, Vector3 __3) { Net.OnWorldSound(__0, __1, __3); }
    }

    [HarmonyPatch(typeof(Toolbox), "Shoot")]
    public static class ShootPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Actor __0, Vector3 __1, Vector3 __2, float __3, float __4, float __5, MurderWeaponPreset __6, bool __7, Vector3 __8, bool __9, bool __10)
        {
            Net.OnShoot(__0, __1, __2, __3, __4, __5, __6, __7, __8);
        }
    }

    [HarmonyPatch(typeof(Player), "RecieveDamage")]
    public static class SelfHurtFxPatch
    {
        [HarmonyPostfix]
        public static void Postfix(float __0, Vector3 __3) { if (__0 > 0.001f) Net.SendBodyFx(1, __3); }
    }

    [HarmonyPatch(typeof(Player), "TriggerPlayerKO")]
    public static class SelfKoFxPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.SendBodyFx(2, Vector3.zero); }
    }

    [HarmonyPatch(typeof(Player), "TriggerPlayerRecovery")]
    public static class SelfRecoverFxPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.SendBodyFx(3, Vector3.zero); }
    }

    [HarmonyPatch(typeof(Player), "Trip")]
    public static class SelfTripFxPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.SendBodyFx(6, Vector3.zero); }
    }

    [HarmonyPatch(typeof(Player), "KillPlayer")]
    public static class SelfDeathFxPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.SendBodyFx(4, Vector3.zero); }
    }

    [HarmonyPatch(typeof(Player), "TransformPlayerController")]
    public static class SeatGuardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerTransitionPreset __0, Interactable __2) { return !Net.SeatTaken(__0, __2); }
    }

    [HarmonyPatch(typeof(Citizen), "RecieveDamage")]
    public static class NpcDamagePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Citizen __instance, float amount, Actor fromWho, Vector3 damagePosition, Vector3 damageDirection, SpatterPatternPreset forwardSpatter, SpatterPatternPreset backSpatter, SpatterSimulation.EraseMode spatterErase, bool alertSurrounding, bool forceRagdoll, float forcedRagdollDuration, float shockMP, bool enableKill, bool allowRecoil, float ragdollForceMP)
        {
            return Net.OnClientHitsNpc(__instance, amount, fromWho, damagePosition, damageDirection, forwardSpatter, backSpatter, (int)spatterErase, alertSurrounding, forceRagdoll, forcedRagdollDuration, shockMP, enableKill, allowRecoil, ragdollForceMP);
        }
    }

    [HarmonyPatch]
    public static class OutfitSeedPatch
    {
        static readonly string[] names = { "GenerateOutfits", "MakeClothed", "SetCurrentOutfit", "LoadCurrentOutfit", "SelectRandomOutfits" };
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in typeof(CitizenOutfitController).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                if (System.Array.IndexOf(names, m.Name) >= 0) yield return m;
        }
        [HarmonyPrefix] public static void Prefix(CitizenOutfitController __instance) { Net.LookSeedBegin(__instance != null ? __instance.human : null); }
        [HarmonyPostfix] public static void Postfix() { Net.LookSeedEnd(); }
    }

    [HarmonyPatch(typeof(Human), "SetPhysicalModelParams")]
    public static class PhysSeedPatch
    {
        [HarmonyPrefix] public static void Prefix(Human __instance) { Net.LookSeedBegin(__instance); }
        [HarmonyPostfix] public static void Postfix() { Net.LookSeedEnd(); }
    }

    [HarmonyPatch(typeof(NewRoom), "AddFurnitureCustom", new System.Type[] { typeof(PlayerApartmentController.FurniturePlacement) })]
    public static class FurnAddPatch1
    {
        [HarmonyPostfix]
        public static void Postfix(NewRoom __instance, PlayerApartmentController.FurniturePlacement __0, FurnitureLocation __result)
        {
            Net.OnFurnAdded(__instance, __result, __0);
        }
    }

    [HarmonyPatch(typeof(NewRoom), "AddFurnitureCustom", new System.Type[] { typeof(FurnitureLocation) })]
    public static class FurnAddPatch2
    {
        [HarmonyPostfix]
        public static void Postfix(NewRoom __instance, FurnitureLocation __result)
        {
            Net.OnFurnAdded(__instance, __result, null);
        }
    }

    [HarmonyPatch(typeof(PlayerApartmentController), "ExecutePlacement")]
    public static class ExecPlacePatch
    {
        [HarmonyPrefix] public static void Prefix() { Net.BeginExecute(); }
        [HarmonyPostfix] public static void Postfix() { Net.EndExecute(); }
    }

    [HarmonyPatch(typeof(PlayerApartmentController), "SellFurniture")]
    public static class SellFurnPatch
    {
        [HarmonyPrefix] public static void Prefix(FurnitureLocation __0) { Net.OnFurnRemoved(__0, "продал"); }
    }

    [HarmonyPatch(typeof(PlayerApartmentController), "MoveFurnitureToStorage")]
    public static class StoreFurnPatch
    {
        [HarmonyPrefix] public static void Prefix(FurnitureLocation __0) { Net.OnFurnRemoved(__0, "убрал на склад"); }
    }

    [HarmonyPatch(typeof(PlayerApartmentController), "BuyNewResidence")]
    public static class BuyResidencePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ResidenceController newHome)
        {
            if (Net.IsApartmentSoldElsewhere(newHome)) return false;
            Net.BeginBuy();
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(ResidenceController newHome)
        {
            Net.OnApartmentBought(newHome);
        }
    }

    [HarmonyPatch(typeof(BreakableWindowController), "BreakWindow")]
    public static class WindowBreakPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BreakableWindowController __instance, Vector3 contactPosition, Vector3 relativeVelocity)
        {
            Net.OnWindowBreak(__instance, contactPosition, relativeVelocity);
        }
    }

    [HarmonyPatch(typeof(Interactable), "Delete")]
    public static class DeletePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Interactable __instance) { Net.OnItemDeleting(__instance); }
    }

    [HarmonyPatch(typeof(FirstPersonItemController), "MeleeAttack")]
    public static class MeleePatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.SendAction(1); }
    }

    [HarmonyPatch(typeof(FirstPersonItemController), "Block")]
    public static class BlockPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.NoteBlock(); Net.SendAction(2); }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    public static class PlayerPatch
    {
        static float timer;
        static DateTime cfgStamp;
        static string lastSeed;
        static int lastNpc = -1;
        static int lastDoors = -1;
        static float lastChange;
        static bool dumped;

        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            Net.Poll();
            Net.Tick(__instance);
            Net.CheatKeys();

            timer += Time.unscaledDeltaTime;
            if (timer < 1f) return;
            timer = 0f;

            Application.runInBackground = true;

            try
            {
                if (Plugin.CfgLiveReload.Value && Plugin.Cfg != null)
                {
                    string path = Plugin.Cfg.ConfigFilePath;
                    var wt = System.IO.File.GetLastWriteTimeUtc(path);
                    if (cfgStamp == default(DateTime)) cfgStamp = wt;
                    else if (wt != cfgStamp)
                    {
                        cfgStamp = wt;
                        Plugin.Cfg.Reload();
                        Plugin.L.LogInfo("[CFG] конфиг перечитан");
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CFG] {e.Message}"); }
            Net.CheatsSecond();

            var city = CityData.Instance;
            if (city == null) return;

            string seed = city.seed;
            if (seed != lastSeed)
            {
                lastSeed = seed;
                lastNpc = -1;
                lastDoors = -1;
                lastChange = Time.realtimeSinceStartup;
                Plugin.L.LogInfo($"[CITY] seed={seed} name={city.cityName} size={city.citySize} " +
                                 $"builtWith={city.cityBuiltWith} buildID={Game.Instance.buildID}");
            }

            int npc = city.citizenDictionary.Count;
            int doors = city.doorDictionary.Count;
            if (npc != lastNpc || doors != lastDoors)
            {
                lastNpc = npc;
                lastDoors = doors;
                lastChange = Time.realtimeSinceStartup;
                Plugin.L.LogInfo($"[WORLD] npc={npc} doors={doors}");
            }

            bool worldReady = npc > 0 && doors > 0 &&
                              Time.realtimeSinceStartup - lastChange >= 5f;

            if (!Net.Started && worldReady && !Net.MenuOpen() && !Net.Following())
            {
                Net.Start();
            }

            if (Plugin.CfgDumpCitizen.Value && !dumped && worldReady)
            {
                dumped = true;
                Dumper.Run();
            }
        }
    }
}

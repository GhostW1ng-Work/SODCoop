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
    public enum NetMode { Off, Host, Client }

    [BepInPlugin("com.yourname.sodcoop", "SodCoop", "0.11.3")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal static void Dbg(string m) { try { if (CfgDebugLog != null && CfgDebugLog.Value) L.LogInfo(m); } catch (System.Exception) { } }
        internal static ConfigEntry<NetMode> CfgMode;
        internal static ConfigEntry<string> CfgAddress;
        internal static ConfigEntry<int> CfgPort;
        internal static ConfigEntry<string> CfgPassword, CfgServerName;
        internal static ConfigEntry<bool> CfgDebugAvatar;
        internal static ConfigEntry<bool> CfgDumpCitizen;
        internal static ConfigEntry<bool> CfgUseCitizenModel;
        internal static ConfigEntry<float> CfgAvatarYOffset;
        internal static ConfigEntry<float> CfgAvatarScale;
        internal static ConfigEntry<int> CfgMaxPlayers;
        internal static ConfigEntry<bool> CfgProfiles, CfgNewPlayerClean;
        internal static ConfigEntry<float> CfgProfileInterval;
        internal static ConfigEntry<int> CfgNewPlayerMoney;
        internal static ConfigEntry<bool> CfgKoNoTimeSkip;
        internal static ConfigEntry<float> CfgHospitalSeconds;
        internal static ConfigEntry<bool> CfgKoInstantGetUp;
        internal static ConfigEntry<float> CfgAvatarYawOffset;
        internal static ConfigEntry<float> CfgMaxSpeed;
        internal static ConfigEntry<int> CfgArmsState;
        internal static ConfigEntry<int> CfgIdleState;
        internal static ConfigEntry<float> CfgCrouchDrop;
        internal static ConfigEntry<float> CfgCrouchHip;
        internal static ConfigEntry<float> CfgCrouchKnee;
        internal static ConfigEntry<float> CfgCrouchTorso;
        internal static ConfigEntry<string> CfgCrouchAxis;
        internal static ConfigEntry<float> CfgLeanScale;
        internal static ConfigEntry<int> CfgCombatArms;
        internal static ConfigEntry<bool> CfgPlayBlock;
        internal static ConfigEntry<bool> CfgDebugCycle;
        internal static ConfigEntry<int> CfgBlockCao;
        internal static ConfigEntry<int> CfgBlockArms;
        internal static ConfigEntry<string> CfgItemMap;
        internal static ConfigEntry<int> CfgForceCao;
        internal static ConfigEntry<int> CfgForceArms;
        internal static ConfigEntry<int> CfgForceIdle;
        internal static ConfigEntry<int> CfgForceCombat;
        internal static ConfigEntry<string> CfgForceLayers;
        internal static ConfigEntry<bool> CfgLiveReload;
        internal static BepInEx.Configuration.ConfigFile Cfg;
        internal static ConfigEntry<bool> CfgHeldShow;
        internal static ConfigEntry<string> CfgHeldHand;
        internal static ConfigEntry<string> CfgHeldPos;
        internal static ConfigEntry<string> CfgWpnPos;
        internal static ConfigEntry<string> CfgWpnRot;
        internal static ConfigEntry<float> CfgWpnScale;
        internal static ConfigEntry<string> CfgHeldRot;
        internal static ConfigEntry<float> CfgHeldScale;
        internal static ConfigEntry<bool> CfgCarryAnim;
        internal static ConfigEntry<bool> CfgObjSync;
        internal static ConfigEntry<bool> CfgTimeSync;
        internal static ConfigEntry<bool> CfgDoorSync;
        internal static ConfigEntry<bool> CfgCheatKeys;
        internal static ConfigEntry<int> CfgAddMoney;
        internal static ConfigEntry<int> CfgMoneyAmount;
        internal static ConfigEntry<string> CfgItemPreset;
        internal static ConfigEntry<bool> CfgSpawnNow;
        internal static ConfigEntry<string> CfgSearchPreset;
        internal static ConfigEntry<bool> CfgPauseNpc;
        internal static ConfigEntry<bool> CfgGod;
        internal static ConfigEntry<int> CfgAddSocial;
        internal static ConfigEntry<int> CfgSocialAmount;
        internal static ConfigEntry<bool> CfgInvisible;
        internal static ConfigEntry<bool> CfgTraceBreak;
        internal static ConfigEntry<string> CfgTypeRegex;
        internal static ConfigEntry<bool> CfgAvatarLightFix;
        internal static ConfigEntry<bool> CfgDoorSnap;
        internal static ConfigEntry<bool> CfgNpcSync;
        internal static ConfigEntry<bool> CfgPuppet;
        internal static ConfigEntry<bool> CfgDebugLog;
        internal static ConfigEntry<int> CfgPuppetId;
        internal static ConfigEntry<bool> CfgPuppetNoAI;
        internal static ConfigEntry<bool> CfgPuppetLog;
        internal static ConfigEntry<bool> CfgNoFriendlyFire;
        internal static ConfigEntry<bool> CfgPvp;
        internal static ConfigEntry<string> CfgProbeKey;
        internal static ConfigEntry<float> CfgSeatRadius;
        internal static ConfigEntry<bool> CfgNpcHitHostAttacker;
        internal static ConfigEntry<bool> CfgSeedLook;
        internal static ConfigEntry<float> CfgNpcRate;
        internal static ConfigEntry<float> CfgNpcRange;
        internal static ConfigEntry<bool> CfgMapIcon;
        internal static ConfigEntry<float> CfgMapIconSize;
        internal static ConfigEntry<int> CfgCarryObjValue;
        internal static ConfigEntry<int> CfgConsumeArms;
        internal static ConfigEntry<string> CfgConsumeStates, CfgConsumeDefault;
        internal static ConfigEntry<string> CfgSitState, CfgSitKeywords, CfgThrowState;
        internal static ConfigEntry<float> CfgSitDrop;
        internal static ConfigEntry<int> CfgSitIdleValue, CfgCrouchIdleValue;
        internal static ConfigEntry<string> CfgCrouchIdleState, CfgCrouchWalkState, CfgLeanLeftState, CfgLeanRightState, CfgPlayState;
        internal static ConfigEntry<int> CfgStateLayer;
        internal static ConfigEntry<float> CfgStateFade, CfgLeanThreshold, CfgDropWithAnim, CfgTiltWithAnim;
        internal static ConfigEntry<int> CfgCarryValue;

        public override void Load()
        {
            L = Log;
            CfgMode = Config.Bind("Network", "Mode", NetMode.Off, "Off, Host or Client");
            CfgAddress = Config.Bind("Network", "Address", "127.0.0.1", "Host IP address (used by Client)");
            CfgPort = Config.Bind("Network", "Port", 9050, "UDP port");
            CfgPassword = Config.Bind("Network", "Password", "", "Server password (empty = no password)");
            CfgServerName = Config.Bind("Network", "ServerName", "SodCoop server", "Server name shown in the LAN list");

            CfgUseCitizenModel = Config.Bind("Avatar", "UseCitizenModel", true,
                "Use a cloned NPC model for the other player (false = green capsule)");
            CfgAvatarYOffset = Config.Bind("Avatar", "YOffset", 0f,
                "Extra vertical offset of the avatar in meters (if it floats above or sinks into the floor)");
            CfgMaxPlayers = Config.Bind("Network", "MaxPlayers", 4,
                "Maximum number of players on the server including the host (2..4). Host only");
            CfgProfiles = Config.Bind("Profiles", "Enabled", true,
                "The host keeps a profile of every joined player (money, social credit, health, status effects, inventory, bought apartments) and gives it back when he joins again");
            CfgProfileInterval = Config.Bind("Profiles", "SaveInterval", 10f,
                "How often (seconds) a client sends his profile to the host");
            CfgNewPlayerClean = Config.Bind("Profiles", "NewPlayerCleanStart", true,
                "A player who joins for the first time starts with an empty inventory and no bad statuses instead of a copy of the host's items");
            CfgNewPlayerMoney = Config.Bind("Profiles", "NewPlayerMoney", 500,
                "Money of a player who joins for the first time (-1 = keep what the host's save gives)");
            CfgKoNoTimeSkip = Config.Bind("Hospital", "NoTimeSkip", true,
                "In multiplayer the game does not fast-forward time while a player is knocked out or in hospital (time is shared by all players). The player heals in real time instead");
            CfgHospitalSeconds = Config.Bind("Hospital", "HospitalSeconds", 60f,
                "How long (real seconds) a knocked out player lies in hospital when NoTimeSkip is on");
            CfgKoInstantGetUp = Config.Bind("Hospital", "InstantGetUp", true,
                "After the pay/don't pay choice in hospital the player gets up at once instead of a second fade and progress bar (when NoTimeSkip is on)");
            CfgAvatarScale = Config.Bind("Avatar", "Scale", 0.92f,
                "Size of the other player's avatar relative to the model (1 = as is). Lower it if the avatar looks taller than NPCs; edit while the game runs");
            CfgAvatarYawOffset = Config.Bind("Avatar", "YawOffset", 0f,
                "Extra rotation of the avatar in degrees (if it faces the wrong way, try 180)");

            CfgMaxSpeed = Config.Bind("Avatar", "MaxSpeed", 5f,
                "Speed in m/s that maps to moveSpeed=1 of the animator (lower = legs move faster)");

            CfgArmsState = Config.Bind("Avatar", "ArmsState", 0,
                "Animator int ArmsState (try 0,1,2,... if the arms pose looks wrong)");
            CfgIdleState = Config.Bind("Avatar", "IdleAnimationState", 0,
                "Animator int IdleAnimationState (try 0,1,2,... if the idle pose looks wrong)");

            CfgCrouchDrop = Config.Bind("Avatar", "CrouchDrop", 0.35f,
                "How many meters the avatar body is lowered while crouched");
            CfgCrouchHip = Config.Bind("Avatar", "CrouchHipAngle", 60f,
                "Upper-leg (hip) bend in degrees while crouched. Negative = other direction");
            CfgCrouchKnee = Config.Bind("Avatar", "CrouchKneeAngle", -100f,
                "Lower-leg (knee) bend in degrees while crouched. Usually the opposite sign of the hip angle");
            CfgCrouchTorso = Config.Bind("Avatar", "CrouchTorsoAngle", 15f,
                "Forward torso lean in degrees while crouched");
            CfgCrouchAxis = Config.Bind("Avatar", "CrouchAxis", "X",
                "Local axis (X, Y or Z) the bones are bent around; change it if legs bend sideways");
            CfgLeanScale = Config.Bind("Avatar", "LeanScale", 1f,
                "Multiplier of the Q/E lean tilt on the avatar (negative = tilt the other way, 0 = off)");

            CfgCombatArms = Config.Bind("Avatar", "FistsArmsOverride", 0,
                "Animator int combatArmsOverride for fists (found by testing: 0=fists, 1=pistol, 2=block, 3=rifle, 4=melee weapon, 5=wounded)");
            CfgPlayBlock = Config.Bind("Avatar", "PlayBlock", false,
                "Play the block animation on the avatar (false = ignore block)");

            CfgDebugCycle = Config.Bind("Debug", "CycleCombatPoses", false,
                "Test: cycle combatArmsOverride 0..5 every 5 s and play an attack every 2 s (use with ShowAvatarInFront)");

            Cfg = Config;
            CfgHeldShow = Config.Bind("HeldItems", "Show", true,
                "Show the item the other player holds (cola, burger, watch...) in his hand");
            CfgHeldHand = Config.Bind("HeldItems", "Hand", "Auto",
                "Auto = the same hand as the sender, or force Right / Left");
            CfgHeldPos = Config.Bind("HeldItems", "Position", "0,0,0",
                "Local offset x,y,z from the forearm bone (live reload works)");
            CfgHeldRot = Config.Bind("HeldItems", "Rotation", "0,0,0",
                "Local rotation x,y,z in degrees");
            CfgCrouchIdleState = Config.Bind("Poses", "CrouchClip", "",
                "Animator state (clip name) played while the other player is crouched and standing still. Empty = bone-based crouch. The log lists names that work: [ANIM] состояния");
            CfgCrouchWalkState = Config.Bind("Poses", "CrouchWalkClip", "",
                "State played while crouched AND moving. Empty = use CrouchIdleState");
            CfgLeanLeftState = Config.Bind("Poses", "LeanLeftState", "",
                "State played while leaning (Q). Empty = just tilt the body");
            CfgLeanRightState = Config.Bind("Poses", "LeanRightState", "",
                "State played while leaning (E). Empty = just tilt the body");
            CfgStateLayer = Config.Bind("Poses", "StateLayer", -1,
                "Animator layer for these states. -1 = find automatically");
            CfgStateFade = Config.Bind("Poses", "Fade", 0.2f, "Cross-fade time in seconds");
            CfgLeanThreshold = Config.Bind("Poses", "LeanThreshold", 5f, "Lean angle (degrees) from which the lean state starts");
            CfgDropWithAnim = Config.Bind("Poses", "CrouchDropWithAnim", 0f,
                "Extra body lowering (m) when a crouch STATE is playing (CrouchDrop is used only without a state)");
            CfgTiltWithAnim = Config.Bind("Poses", "LeanTiltWithAnim", 0f,
                "Body tilt multiplier when a lean STATE is playing (LeanScale is used only without a state)");
            CfgPlayState = Config.Bind("Debug", "PlayState", "",
                "Test: play this state on the avatar all the time (any clip name from the list, e.g. Leaning, KneelDown). Empty = off");
            CfgConsumeArms = Config.Bind("Poses", "ConsumeArmsState", 6,
                "ArmsState while the other player eats/drinks: 6 = armsConsuming (the game's own parameter). -1 = off. ConsumeStates (clip names) is an alternative but may flicker");
            CfgConsumeStates = Config.Bind("Poses", "ConsumeClips", "",
                "Held item name (substring) -> animator state played while eating/drinking. First match wins, e.g. food_drink -> Arms_Consume1");
            CfgConsumeDefault = Config.Bind("Poses", "ConsumeDefaultClip", "", "State when no ConsumeStates entry matches (empty = use ConsumeArmsState instead)");
            CfgSitState = Config.Bind("Poses", "SitClip", "",
                "Animator state played while the other player sits on a chair/sofa (try SitDown, SitDownHunched, SatAgainstWall). Empty = no sit animation");
            CfgSitKeywords = Config.Bind("Poses", "SitKeywords", "booth,sit,seat,chair,couch,sofa,stool,bench,armchair,toilet",
                "Player sits down when the name of his current transition (see log [SIT]) contains one of these words; a name with 'exit' ends it");
            CfgSitIdleValue = Config.Bind("Poses", "SitIdleAnimationState", 1,
                "IdleAnimationState while sitting (the game's own parameter: 1 sitting, 10 sitAgainstWall, 11 leanAgainstWall, 14 cowering). -1 = off");
            CfgCrouchIdleValue = Config.Bind("Poses", "CrouchAnimState", -1,
                "IdleAnimationState while crouched AND standing still (21 = stackingObjectsCrouching). -1 = bone-based crouch only. While moving, the bone crouch is used");
            CfgSitDrop = Config.Bind("Poses", "SitDropMeters", 0.35f,
                "Lower the sitting avatar by this many meters (adjust if he floats above / sinks into the chair)");
            CfgThrowState = Config.Bind("Poses", "ThrowTrigger", "throw",
                "Animator trigger played when the other player throws / puts down the item in his hands");
            CfgTimeSync = Config.Bind("World", "TimeWeatherSync", true,
                "Host sends game time, time speed and weather to the client every few seconds");
            CfgObjSync = Config.Bind("Objects", "Sync", true,
                "Sync world objects the player carries (garbage bags, boxes...) and drops/throws: the other player sees them move");
            CfgCheatKeys = Config.Bind("Cheats", "Hotkeys", true,
                "F6 = add money (MoneyAmount), F7 = spawn item ItemPreset in front of you, F8 = toggle NPC AI pause, F9 = toggle god mode, F12 = spawn next weapon (press repeatedly to cycle), F10 = add social credit (SocialCreditAmount), F11 = toggle NPCs-cannot-see-me");
            CfgAddMoney = Config.Bind("Cheats", "AddMoney", 0,
                "Set to a number while the game runs: that much money is added once, then it resets to 0");
            CfgMoneyAmount = Config.Bind("Cheats", "MoneyAmount", 1000, "How much money the F6 key adds");
            CfgItemPreset = Config.Bind("Cheats", "ItemPreset", "StarchKola",
                "Item preset name spawned by F7 (see SearchPreset to find names)");
            CfgSpawnNow = Config.Bind("Cheats", "SpawnNow", false,
                "Set to true while the game runs: spawns ItemPreset in front of you, then resets to false");
            CfgSearchPreset = Config.Bind("Cheats", "SearchPreset", "",
                "Type part of a name (e.g. kola): matching item preset names are written to the log, then this resets");
            CfgPauseNpc = Config.Bind("Cheats", "PauseNpcAI", false,
                "Pause NPC AI (they freeze and stop reacting to you). F8 toggles");
            CfgGod = Config.Bind("Cheats", "GodMode", false,
                "The local player takes no damage. F9 toggles");
            CfgAddSocial = Config.Bind("Cheats", "AddSocialCredit", 0,
                "Set to a number while the game runs: that much social credit is added once, then it resets to 0");
            CfgSocialAmount = Config.Bind("Cheats", "SocialCreditAmount", 10, "How much social credit the F10 key adds");
            CfgInvisible = Config.Bind("Cheats", "NpcCannotSee", false,
                "NPCs cannot see you (no spotting, no illegal-activity witnesses). F11 toggles");
            CfgAvatarLightFix = Config.Bind("Poses", "AvatarLightFix", true,
                "Keep the other player's model lit after room lights are switched off and on (sets the renderers' light layer mask to all layers)");
            CfgTypeRegex = Config.Bind("Debug", "TypeRegex", "apartment|residen|purchas|forsale|realestate|estate|landlord|lease|ownership|sale",
                "Regex (case-insensitive) for class names listed in the log as [TYPES] ~25 s after entering the game when TraceBreak=true");
            CfgTraceBreak = Config.Bind("Debug", "TraceBreak", false,
                "Diagnostics: once, ~25 s after you are in the game, log game classes whose names contain Window/Glass/Break/Shatter (no patching). Used to find how windows break");
            CfgDoorSync = Config.Bind("Doors", "Sync", true,
                "Sync doors the players open/close/lock/jam (room doors, cupboards, drawers): the other player sees the same state");
            CfgSeedLook = Config.Bind("Npc", "SeedAppearance", true, "Make the look of every citizen (clothes, colours) depend only on the citizen, so both players see the same people. Both players must use the same value");
            CfgNpcHitHostAttacker = Config.Bind("Npc", "ClientHitsAsHost", true, "Hits from the client are applied on the host as if the host player dealt them (safe). false = no attacker is passed (the citizen does not know who hit him; may cause errors)");
            CfgDebugLog = Config.Bind("Debug", "VerboseLog", false, "Подробный журнал: двери, свет, анимации, звуки, бой, посадка и т.д. (шумно). По умолчанию выключен");
            CfgPuppet = Config.Bind("Npc", "SecondPlayerPuppet", true, "Host: an invisible 'double' of the client (a spare citizen) stands where the client avatar is, so the other citizens notice, fear, fight and report the client like a normal person. Damage to the double is forwarded to the client");
            CfgPuppetId = Config.Bind("Npc", "PuppetCitizenId", -1, "humanID of the citizen used as the double. -1 = pick automatically (a homeless one first). The log prints the chosen one");
            CfgPuppetNoAI = Config.Bind("Npc", "PuppetDisableAI", true, "Switch off the double's own AI so that only the client controls it");
            CfgPuppetLog = Config.Bind("Npc", "PuppetLog", true, "Log when a citizen notices / chases / attacks the second player");
            CfgNoFriendlyFire = Config.Bind("Npc", "NoFriendlyFire", true, "Citizens fighting the second player do not hurt bystanders (prevents NPC vs NPC brawls)");
            CfgSeatRadius = Config.Bind("Seat", "GuardRadius", 0.6f, "Нельзя сесть, если другой игрок сидит ближе этого расстояния (м) до места; 0 = выключить");
            CfgPvp = Config.Bind("Npc", "PlayerVsPlayer", true, "Players can damage each other (client hits the host through its hidden double citizen)");
            CfgNpcSync = Config.Bind("Npc", "Sync", true, "The host controls the citizens: their positions and animations are sent to the client (the client's NPC AI is paused)");
            CfgNpcRate = Config.Bind("Npc", "SendInterval", 0.1f, "Seconds between citizen updates sent by the host");
            CfgNpcRange = Config.Bind("Npc", "NearRange", 120f, "Citizens closer than this many meters to any player are sent every update, the rest are sent in a slow rotation");
            CfgMapIcon = Config.Bind("Map", "ShowOtherPlayer", true, "Show the other player as a green circle on the minimap and the big map");
            CfgMapIconSize = Config.Bind("Map", "IconSize", 18f, "Size of the other player's icon on the map (pixels)");
            CfgDoorSnap = Config.Bind("Doors", "InitialSnapshot", true,
                "When the client connects the host sends the current state of all doors once");
            CfgCarryObjValue = Config.Bind("Objects", "CarryObjectValue", 2,
                "Animator int carryItem while the other player carries a big world object (1 = Arms_Carry1, 2 = Arms_CarryHeavy)");
            CfgCarryAnim = Config.Bind("HeldItems", "CarryAnimation", true,
                "While the other player holds an item, play the carry pose (carryingItem=true, arms layer on)");
            CfgCarryValue = Config.Bind("HeldItems", "CarryItemValue", 1,
                "Animator int carryItem while carrying (try 0,1,2: Arms_Carry1 / Arms_CarryHeavy)");
            CfgWpnPos = Config.Bind("Weapons", "ModelPosition", "0,0,0",
                "Extra offset (x,y,z, metres, in the hand joint space) of the weapon model in the other player's hand. Edit while the game runs");
            CfgWpnRot = Config.Bind("Weapons", "ModelRotation", "0,0,0",
                "Extra rotation (euler x,y,z degrees) of the weapon model in the other player's hand");
            CfgWpnScale = Config.Bind("Weapons", "ModelScale", 1f,
                "Scale multiplier of the weapon model in the other player's hand");
            CfgHeldScale = Config.Bind("HeldItems", "Scale", 1f,
                "Scale multiplier of the item model");
            CfgBlockCao = Config.Bind("Avatar", "BlockCombatArmsOverride", 2,
                "combatArmsOverride while the other player holds block (RMB). 0 fists,1 pistol,2 block?,3 rifle,4 melee,5 wounded");
            CfgBlockArms = Config.Bind("Avatar", "BlockArmsState", -1,
                "ArmsState while blocking (-1 = use Avatar.ArmsState)");
            CfgItemMap = Config.Bind("Avatar", "ItemArmsMap", "watch:10,time:10",
                "Held item name (substring, case-insensitive) -> ArmsState, comma separated. Item name is in the log: [NET] Предмет в руках");
            CfgLiveReload = Config.Bind("Debug", "LiveReload", true,
                "Re-read this cfg file while the game runs (edit, save, see the result at once)");
            CfgForceCombat = Config.Bind("Debug", "ForceInCombat", -1,
                "-1 = off, 0/1 = force the inCombat bool on the avatar (always, for testing)");
            CfgForceCao = Config.Bind("Debug", "ForceCombatArmsOverride", -1,
                "-1 = off, 0..N = force combatArmsOverride on the avatar");
            CfgForceArms = Config.Bind("Debug", "ForceArmsState", -1,
                "-1 = off, 0..11 = force ArmsState (10 reading, 3 use, 6 consuming, 8 smoking ...)");
            CfgForceIdle = Config.Bind("Debug", "ForceIdleAnimationState", -1,
                "-1 = off, force IdleAnimationState");
            CfgForceLayers = Config.Bind("Debug", "ForceLayerWeights", "",
                "Empty = off. Weights for layers 1,2,.. e.g. \"1,0\" or \"1,1\" (layer 0 is always on)");
            CfgDebugAvatar = Config.Bind("Debug", "ShowAvatarInFront", false,
                "Test: show the avatar 3 m in front of the camera (no network needed)");
            CfgDumpCitizen = Config.Bind("Debug", "DumpCitizen", false,
                "Test: log the animator parameters of some NPCs once after the world is loaded");

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<SodCoopDriver>();
                AddComponent<SodCoopDriver>();
                L.LogInfo("[DRV] драйвер меню/игры запущен");
            }
            catch (Exception e) { L.LogWarning("[DRV] не удалось запустить драйвер: " + e.Message); }
            CfgProbeKey = Config.Bind("Debug", "ProbeKey", "F4", "Клавиша разведки для редактора персонажа (имя из UnityEngine.KeyCode, например F4, Backslash, Keypad0)");
            Harmony.CreateAndPatchAll(typeof(PlayerPatch));
            Harmony.CreateAndPatchAll(typeof(MeleePatch));
            Harmony.CreateAndPatchAll(typeof(BlockPatch));
            foreach (var t in new[] { typeof(ThrowFoodPatch), typeof(ThrowGrenadePatch), typeof(ThrowCoinPatch), typeof(PutDownPatch), typeof(EmptySlotPatch), typeof(PickUpPatch), typeof(DropPatch), typeof(SafeDeletePatch), typeof(DeletePatch), typeof(BreakPatch), typeof(DoorOpenPatch), typeof(DoorLockPatch), typeof(DoorJamPatch), typeof(DoorMoveOpenPatch), typeof(DoorMoveClosePatch), typeof(DoorMoveSetOpenPatch), typeof(LockStatePatch), typeof(SwitchStatePatch), typeof(CustomState1Patch), typeof(CustomState2Patch), typeof(CustomState3Patch), typeof(BreakerSetOpenPatch), typeof(ShatterPatch), typeof(WindowBreakPatch), typeof(AptTrace), typeof(RoomClearPatch), typeof(DecorPatch), typeof(NpcDamagePatch), typeof(SelfHurtFxPatch), typeof(SelfKoFxPatch), typeof(SelfRecoverFxPatch), typeof(SelfDeathFxPatch), typeof(SeatGuardPatch), typeof(WorldSoundPatch), typeof(ShootPatch), typeof(NpcActAttackPatch), typeof(NpcActThrowPatch), typeof(NpcActAbortPatch), typeof(NpcActBlockPatch), typeof(NpcActRecoilPatch), typeof(NpcActHeldPatch), typeof(NpcActDespawnRPatch), typeof(NpcActDespawnLPatch), typeof(NpcActWeaponPatch), typeof(PuppetTrackPatch), typeof(PuppetPersuePatch), typeof(PuppetAttackPatch), typeof(PuppetInvestigatePatch), typeof(OutfitSeedPatch), typeof(PhysSeedPatch), typeof(FurnAddPatch1), typeof(FurnAddPatch2), typeof(ExecPlacePatch), typeof(SellFurnPatch), typeof(StoreFurnPatch), typeof(BuyResidencePatch), typeof(WeatherChangePatch), typeof(RoomLightsPatch), typeof(InteractActionPatch), typeof(InteractKeyPatch), typeof(InvisSightPatch), typeof(InvisIllegalPatch), typeof(GodPatch), typeof(KoTimeSpeedPatch), typeof(GameSavePatch), typeof(WorldLoadPatch) })
            {
                try { Harmony.CreateAndPatchAll(t); }
                catch (Exception e) { L.LogWarning($"[PATCH] {t.Name}: {e.Message}"); }
            }
            L.LogInfo($"SodCoop загружен! Режим: {CfgMode.Value}");
        }
    }

    // Диагностика: какие методы игры вызываются, когда разбивается окно
    public static class BreakTrace
    {
        static readonly System.Collections.Generic.Dictionary<string, int> counts = new System.Collections.Generic.Dictionary<string, int>();

        static bool tried; static float firstSeen;

        // ставим перехват не при запуске, а через ~25 с после входа в игру, чтобы не тормозить загрузку
        public static void MaybeInstall()
        {
            if (tried || !Plugin.CfgTraceBreak.Value) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (firstSeen == 0f) { firstSeen = now; return; }
            if (now - firstSeen < 25f) return;
            tried = true;
            Install();
        }

        // Ничего не перехватываем: только перечисляем классы игры, похожие на окна/стекло/разбиваемое
        static void Install()
        {
            try
            {
                var asm = typeof(Player).Assembly;
                var rx = new System.Text.RegularExpressions.Regex(Plugin.CfgTypeRegex.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                int n = 0;
                foreach (var t in asm.GetTypes())
                {
                    try
                    {
                        if (t.Namespace != null || t.IsGenericType || t.Name.Contains("<") || t.Name.Contains("__")) continue;
                        if (!rx.IsMatch(t.Name)) continue;
                        var ms = new System.Collections.Generic.List<string>();
                        foreach (var m in t.GetMethods(flags))
                        {
                            string mn = m.Name;
                            if (mn.StartsWith("get_") || mn.StartsWith("set_") || mn.StartsWith("add_") || mn.StartsWith("remove_") || mn.StartsWith("NativeMethodInfoPtr")) continue;
                            if (mn == "Equals" || mn == "GetHashCode" || mn == "ToString" || mn == "GetType" || mn == "Finalize" || mn == "MemberwiseClone") continue;
                            ms.Add(mn);
                        }
                        string bt = t.BaseType != null ? t.BaseType.Name : "";
                        var ps = new System.Collections.Generic.List<string>();
                        foreach (var p in t.GetProperties(flags)) ps.Add(p.Name);
                        Plugin.Dbg($"[TYPES] {t.Name} : {bt} | методы: {string.Join(", ", ms.ToArray())} | поля: {string.Join(", ", ps.ToArray())}");
                        n++;
                    }
                    catch (Exception) { }
                }
                Plugin.Dbg($"[TYPES] найдено классов: {n}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[TYPES] {e.Message}"); }
        }

        public static void Prefix(System.Reflection.MethodBase __originalMethod)
        {
            try
            {
                string key = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
                int c; counts.TryGetValue(key, out c);
                if (c >= 6) return;
                counts[key] = c + 1;
                Plugin.Dbg($"[TRACE] вызван {key}");
            }
            catch (Exception) { }
        }
    }

    // Бросок / положить предмет -> событие по сети
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
            try { Plugin.Dbg($"[ITEM] PickUpItem: {(pickUpThis != null ? pickUpThis.name : "null")} успех={__result}"); } catch (Exception) { }
            if (__result && pickUpThis != null) Net.OnItemPickedUp(pickUpThis);
        }
    }

    // Предмет из инвентаря игрока выброшен/поставлен в мир
    [HarmonyPatch(typeof(Interactable), "SetAsNotInventory")]
    public static class DropPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Interactable __instance, ref bool __state)
        {
            __state = false;
            try { var inv = __instance.inInventory; __state = inv != null && inv.isPlayer; } catch (Exception) { }
        }
        [HarmonyPostfix]
        public static void Postfix(Interactable __instance, bool __state)
        {
            if (__state) Net.OnItemDropped(__instance);
        }
    }

    // Тестовый бессмертный режим
    [HarmonyPatch(typeof(Player), "RecieveDamage")]
    public static class GodPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() { return !Plugin.CfgGod.Value; }
    }

    // Двери комнат: открыл / запер / заклинил (только действия локального игрока)
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

    // Подвижные интерактивные двери (шкафы, ящики, холодильники)
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

    // Основной свет комнаты (выключатель): синхронизируем целиком комнату по roomID
    [HarmonyPatch(typeof(NewRoom), "SetMainLights")]
    public static class RoomLightsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NewRoom __instance, bool newVal, Actor actor)
        {
            Net.OnRoomLights(__instance, newVal, actor);
        }
    }

    // Окно "игрок только что что-то нажал": только в нём изменения света без актёра считаются переключением
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

    // Электрощиток (BreakerController) переопределяет SetOpen
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

    // Чит "NPC меня не видят"
    [HarmonyPatch(typeof(Actor), "SightingCheck")]
    public static class InvisSightPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Actor __instance)
        {
            if (!Plugin.CfgInvisible.Value) return true;
            try { return __instance.isPlayer; } catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(Actor), "AddToSeesIllegal")]
    public static class InvisIllegalPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Actor newTarget)
        {
            if (!Plugin.CfgInvisible.Value) return true;
            try { return newTarget == null || !newTarget.isPlayer; } catch (Exception) { return true; }
        }
    }

    // Предмет удалён (съеден, разбит и т.д.) -> второй игрок удаляет свою копию
    [HarmonyPatch(typeof(Interactable), "SafeDelete")]
    public static class SafeDeletePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Interactable __instance) { Net.OnItemDeleting(__instance); }
    }

    // Предмет разбился (брошенная бутылка и т.п.)
    [HarmonyPatch(typeof(InteractableController), "BreakObject")]
    public static class BreakPatch
    {
        [HarmonyPrefix]
        public static void Prefix(InteractableController __instance, Vector3 contactPoint, Vector3 normal, float magnitude)
        {
            Net.OnItemBroken(__instance, contactPoint, normal, magnitude);
        }
    }

    // Стекло (окна и т.п.) разбилось
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

    // Погоду на клиенте меняет только хост
    [HarmonyPatch(typeof(SessionData), "ExecuteWeatherChange")]
    public static class WeatherChangePatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !Net.ClientFollowsHostWeather();
        }
    }

    // Хост сохраняет игру: рядом с сейвом записываем профили игроков
    [HarmonyPatch(typeof(SaveStateController), "CaptureSaveStateAsync")]
    public static class GameSavePatch
    {
        [HarmonyPrefix]
        public static void Prefix(string __0)
        {
            try { Net.OnGameSaving(__0); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] снимок при сохранении: " + e.Message); }
        }
    }

    // Начало загрузки мира (новая игра или сейв): профили хоста надо взять заново
    [HarmonyPatch(typeof(CityConstructor), "StartLoading")]
    public static class WorldLoadPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.OnWorldLoading(); }
    }

    // Без сознания / в больнице игра ускоряет время (veryFast). В мультиплеере время общее: состояние оставляем, а множитель возвращаем к x1
    [HarmonyPatch(typeof(SessionData), "SetTimeSpeed")]
    public static class KoTimeSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SessionData.TimeSpeed __0)
        {
            Net.AfterTimeSpeed(__0);
        }
    }

    // Диагностика: что игра делает при покупке и обустройстве квартиры (только действия игрока, не каждый кадр)
    [HarmonyPatch]
    public static class AptTrace
    {
        static readonly string[] playerNames = { "BuyNewResidence", "ExecutePlacement", "ApplyDecor", "RevertDecor", "SellFurniture", "MoveFurnitureToStorage", "RemoveFromStorage", "MoveItemToStorage", "RemoveItemFromStorage", "SellItem", "PlaceIndividualCluster", "SetDecoratingMode" };
        static readonly string[] roomNames = { "AddFurnitureCustom" };

        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var m in typeof(PlayerApartmentController).GetMethods(flags))
                if (System.Array.IndexOf(playerNames, m.Name) >= 0) yield return m;
            foreach (var m in typeof(NewRoom).GetMethods(flags))
                if (System.Array.IndexOf(roomNames, m.Name) >= 0) yield return m;
        }

        static string Show(object o)
        {
            if (o == null) return "null";
            try
            {
                var t = o.GetType();
                if (t.IsPrimitive || t.IsEnum || o is string || o is Vector3 || o is Vector3Int) return o.ToString();
                var sb = new System.Text.StringBuilder(t.Name);
                foreach (var pn in new[] { "name", "roomID", "id", "presetName", "preset", "angle", "anchorNode", "nodeCoord", "worldPos", "wPos" })
                {
                    var p = t.GetProperty(pn);
                    if (p == null || p.GetIndexParameters().Length > 0) continue;
                    object v = null;
                    try { v = p.GetValue(o, null); } catch (Exception) { continue; }
                    if (v == null) continue;
                    string sv = (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum || v is Vector3 || v is Vector3Int) ? v.ToString() : v.GetType().Name;
                    sb.Append(' ').Append(pn).Append('=').Append(sv);
                }
                return sb.ToString();
            }
            catch (Exception) { return o.GetType().Name; }
        }

        [HarmonyPostfix]
        public static void Postfix(System.Reflection.MethodBase __originalMethod, object __instance, object[] __args)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("[APTTRACE] ").Append(__originalMethod.DeclaringType.Name).Append('.').Append(__originalMethod.Name).Append('(');
                if (__args != null) for (int i = 0; i < __args.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Show(__args[i])); }
                sb.Append(')');
                if (__originalMethod.DeclaringType == typeof(NewRoom)) sb.Append(" | room: ").Append(Show(__instance));
                if (__originalMethod.DeclaringType == typeof(PlayerApartmentController))
                {
                    foreach (var fn in new[] { "furnPlacement", "decoratingRoom", "furnPlacementRoom", "decoratingType", "decoratingMaterial" })
                    {
                        var p = __instance.GetType().GetProperty(fn);
                        if (p == null) continue;
                        object v = null; try { v = p.GetValue(__instance, null); } catch (Exception) { }
                        sb.Append(" | ").Append(fn).Append('=').Append(Show(v));
                    }
                }
                Plugin.Dbg(sb.ToString());
            }
            catch (Exception e) { Plugin.L.LogWarning($"[APTTRACE] {e.Message}"); }
        }
    }

    // Во время покупки игра убирает хлам из комнат квартиры: запоминаем эти вызовы, чтобы повторить у второго игрока
    [HarmonyPatch(typeof(NewRoom), "RemoveAllInhabitantFurniture")]
    public static class RoomClearPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NewRoom __instance, bool removeSkipAddressInhabitantsFurniture, FurnitureClusterLocation.RemoveInteractablesOption spawnedOnFurnitureRemovalOption)
        {
            Net.OnRoomCleared(__instance, removeSkipAddressInhabitantsFurniture, (int)spawnedOnFurnitureRemovalOption);
        }
    }

    // Отделка (обои, пол, потолок) в своей квартире
    [HarmonyPatch(typeof(PlayerApartmentController), "ApplyDecor")]
    public static class DecorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerApartmentController __instance, MaterialGroupPreset.MaterialType decorType, MaterialGroupPreset material, Toolbox.MaterialKey key, bool saveChanges)
        {
            Net.OnDecorApplied(__instance, decorType, material, key, saveChanges);
        }
    }


    // Диагностика двойника: кто заметил / преследует / бьёт второго игрока
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


    // Разовые действия горожан: хост шлёт клиенту, чтобы тот видел драку и стрельбу
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

    // Выстрел игрока: повторяем вспышку, звук, гильзу и попадание у второго игрока (урон считает тот, кто стрелял)
    [HarmonyPatch(typeof(Toolbox), "Shoot")]
    public static class ShootPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Actor __0, Vector3 __1, Vector3 __2, float __3, float __4, float __5, MurderWeaponPreset __6, bool __7, Vector3 __8, bool __9, bool __10)
        {
            Net.OnShoot(__0, __1, __2, __3, __4, __5, __6, __7, __8);
        }
    }

    // Моё тело получило удар / упало / умерло -> другому игроку для анимации аватара
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

    [HarmonyPatch(typeof(Player), "KillPlayer")]
    public static class SelfDeathFxPatch
    {
        [HarmonyPostfix]
        public static void Postfix() { Net.SendBodyFx(4, Vector3.zero); }
    }

    // Не даём сесть на место, где уже сидит другой игрок
    [HarmonyPatch(typeof(Player), "TransformPlayerController")]
    public static class SeatGuardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerTransitionPreset __0, Interactable __2) { return !Net.SeatTaken(__0, __2); }
    }

    // Клиент бьёт горожанина: урон считает хост (у клиента ИИ горожан остановлен), результат вернётся в потоке состояния
    [HarmonyPatch(typeof(Citizen), "RecieveDamage")]
    public static class NpcDamagePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Citizen __instance, float amount, Actor fromWho, Vector3 damagePosition, Vector3 damageDirection, SpatterPatternPreset forwardSpatter, SpatterPatternPreset backSpatter, SpatterSimulation.EraseMode spatterErase, bool alertSurrounding, bool forceRagdoll, float forcedRagdollDuration, float shockMP, bool enableKill, bool allowRecoil, float ragdollForceMP)
        {
            return Net.OnClientHitsNpc(__instance, amount, fromWho, damagePosition, damageDirection, forwardSpatter, backSpatter, (int)spatterErase, alertSurrounding, forceRagdoll, forcedRagdollDuration, shockMP, enableKill, allowRecoil, ragdollForceMP);
        }
    }

    // Внешность горожан: игра выбирает одежду и цвета «случайно» в момент показа, у каждого игрока по-своему.
    // Привязываем случайность к самому горожанину, чтобы у обоих игроков выглядел одинаково
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

    // Мебель, которую владелец ставит / двигает / продаёт / убирает на склад
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

    // Покупка квартиры с доски объявлений
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

    // Окна: BreakableWindowController.BreakWindow
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

    // Удар / блок игрока -> событие по сети
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
            // Каждый кадр: сеть, отправка позиции, движение аватара
            Net.Poll();
            Net.Tick(__instance);
            Net.CheatKeys();

            // Остальное проверяем раз в секунду
            timer += Time.unscaledDeltaTime;
            if (timer < 1f) return;
            timer = 0f;

            // Чтобы неактивное окно (вторая копия игры) не вставало на паузу
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

            // Мир считается загруженным, если счётчики не менялись 5 секунд
            bool worldReady = npc > 0 && doors > 0 &&
                              Time.realtimeSinceStartup - lastChange >= 5f;

            if (!Net.Started && worldReady)
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

    // Диагностика: параметры аниматора NPC (нужны, чтобы оживить модель второго игрока)
    internal static class Dumper
    {
        internal static void Run()
        {
            try
            {
                var city = CityData.Instance;
                bool first = false, moving = false, running = false;

                for (int i = 0; i < city.citizenDirectory.Count; i++)
                {
                    var c = city.citizenDirectory[i];
                    if (c == null) continue;

                    if (!first)
                    {
                        first = true;
                        DumpAnimator("первый NPC", c, true);
                    }
                    if (!moving && c.isMoving && !c.isRunning)
                    {
                        moving = true;
                        DumpAnimator("идущий NPC", c, false);
                    }
                    if (!running && c.isRunning)
                    {
                        running = true;
                        DumpAnimator("бегущий NPC", c, false);
                    }
                    if (first && moving && running) break;
                }

                if (!moving) Plugin.Dbg("[ANIM] идущих NPC на момент дампа не нашлось");
                if (!running) Plugin.Dbg("[ANIM] бегущих NPC на момент дампа не нашлось");
                Plugin.Dbg("[ANIM] ===== END =====");
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"[ANIM] {e}");
            }
        }

        static void DumpAnimator(string label, Citizen c, bool full)
        {
            try
            {
                var ac = c.animationController;
                if (ac == null || ac.mainAnimator == null)
                {
                    Plugin.Dbg($"[ANIM] {label}: у {c.citizenName} нет аниматора");
                    return;
                }

                var a = ac.mainAnimator;
                string ctrl = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null";
                Plugin.Dbg($"[ANIM] ===== {label}: {c.citizenName} moving={c.isMoving} running={c.isRunning} " +
                                 $"crouched={c.isCrouched} controller={ctrl} layers={a.layerCount} speed={a.speed} =====");

                var ps = a.parameters;
                var line = new StringBuilder();
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    string val;
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            val = a.GetFloat(p.name).ToString("0.###");
                            break;
                        case AnimatorControllerParameterType.Int:
                            val = a.GetInteger(p.name).ToString();
                            break;
                        case AnimatorControllerParameterType.Bool:
                            val = a.GetBool(p.name).ToString();
                            break;
                        default:
                            val = "(trigger)";
                            break;
                    }

                    if (full)
                        Plugin.Dbg($"[ANIM]   {p.name} : {p.type} = {val}");
                    else
                        line.Append(p.name).Append('=').Append(val).Append("; ");
                }

                if (!full) Plugin.Dbg("[ANIM]   " + line);

                if (full)
                {
                    for (int i = 0; i < a.layerCount; i++)
                        Plugin.Dbg($"[ANIM]   слой {i}: {a.GetLayerName(i)} вес={a.GetLayerWeight(i):0.##}");

                    // Все клипы контроллера: по названиям видно, какие есть кулаки, удары, пистолет и т.д.
                    var rc = a.runtimeAnimatorController;
                    if (rc != null)
                    {
                        var clips = rc.animationClips;
                        var sb = new StringBuilder();
                        for (int i = 0; i < clips.Length; i++)
                            if (clips[i] != null) sb.Append(clips[i].name).Append(" | ");
                        Plugin.Dbg($"[ANIM] клипов={clips.Length}: {sb}");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"[ANIM] {label}: {e}");
            }
        }
    }


    // ---- Разведка для редактора персонажа: Insert в меню или в игре пишет BepInEx\SodCoop_probe.txt ----
    // Живёт и в меню, и в игре (Harmony-патчи на Update меню не срабатывают от движка)
    // Внешность игрока. Хранится в BepInEx\config\SodCoop_character.json
    public class Appearance
    {
        public int Gender;      // 0 male, 1 female, 2 nonBinary (Human.Gender)
        public int Build = 1;  // Descriptors.BuildType: 0 skinny, 1 average, 2 overweight, 3 muscular
        public int Eyes;        // Descriptors.EyeColour
        public int Skin;        // индекс в CharEditor.Skins
        public int HairCol;     // индекс в CharEditor.HairCols
        public string Head = "";
        public string Hair = "";
        public string Top = "";
        public string Bottom = "";
        public string Shoes = "";
        public string Hat = "";
        public string Glasses = "";
        public int TopCol = 3;
        public int BottomCol = 5;
        public int ShoesCol = 0;
    }

    // Редактор персонажа. Этап 1: окно на IMGUI поверх главного меню, предпросмотр модели в RenderTexture.
    internal static class CharEditor
    {
        internal static readonly Color[] Skins =
        {
            new Color(0.97f, 0.89f, 0.87f, 1f), new Color(0.93f, 0.80f, 0.68f, 1f), new Color(0.85f, 0.69f, 0.52f, 1f),
            new Color(0.70f, 0.52f, 0.37f, 1f), new Color(0.55f, 0.38f, 0.26f, 1f), new Color(0.38f, 0.26f, 0.18f, 1f),
            new Color(0.25f, 0.17f, 0.12f, 1f)
        };
        internal static readonly string[] HairColNames = { "black", "brown", "blonde", "ginger", "red", "blue", "green", "purple", "pink", "grey", "white" };
        internal static readonly Color[] HairCols =
        {
            new Color(0.05f, 0.04f, 0.04f, 1f), new Color(0.30f, 0.18f, 0.10f, 1f), new Color(0.85f, 0.72f, 0.40f, 1f),
            new Color(0.70f, 0.30f, 0.12f, 1f), new Color(0.61f, 0.09f, 0.09f, 1f), new Color(0.15f, 0.25f, 0.75f, 1f),
            new Color(0.15f, 0.60f, 0.25f, 1f), new Color(0.45f, 0.20f, 0.65f, 1f), new Color(0.90f, 0.45f, 0.65f, 1f),
            new Color(0.55f, 0.55f, 0.55f, 1f), new Color(0.92f, 0.92f, 0.92f, 1f)
        };
        internal static readonly Color[] Cloth =
        {
            new Color(0.10f, 0.10f, 0.10f, 1f), new Color(0.95f, 0.95f, 0.95f, 1f), new Color(0.50f, 0.50f, 0.50f, 1f),
            new Color(0.20f, 0.30f, 0.60f, 1f), new Color(0.65f, 0.15f, 0.15f, 1f), new Color(0.20f, 0.45f, 0.25f, 1f),
            new Color(0.80f, 0.65f, 0.25f, 1f), new Color(0.45f, 0.28f, 0.15f, 1f), new Color(0.80f, 0.55f, 0.65f, 1f),
            new Color(0.35f, 0.20f, 0.50f, 1f), new Color(0.85f, 0.45f, 0.15f, 1f), new Color(0.75f, 0.70f, 0.60f, 1f)
        };
        static readonly string[] GenderNames = { "Male", "Female", "Non-binary" };
        static readonly string[] BuildNames = { "Skinny", "Average", "Overweight", "Muscular" };
        static readonly string[] EyeNames = { "Blue", "Brown", "Green", "Grey" };

        internal static bool Open;
        static Appearance cur = new Appearance();
        static string status = "";
        static GameObject donor, camGo, lightGo, menuCanvas;
        static Camera cam;
        static RenderTexture rt;
        static float yaw = 180f, zoom = 3.2f, camY;
        static bool dirty, poolsDirty = true, dumped; static int frameDue; static bool fillNaked = true;
        static float applyAt;
        static readonly Vector3 FarPos = new Vector3(7000f, 7000f, 7000f);

        // каталоги одежды под текущие пол и телосложение
        static readonly List<ClothesPreset> heads = new List<ClothesPreset>(), hairs = new List<ClothesPreset>(), tops = new List<ClothesPreset>(),
            bottoms = new List<ClothesPreset>(), shoes = new List<ClothesPreset>(), hats = new List<ClothesPreset>(), glasses = new List<ClothesPreset>();
        static readonly List<ClothesPreset> nakedAll = new List<ClothesPreset>();

        static string SavePath { get { return Path.Combine(BepInEx.Paths.ConfigPath, "SodCoop_character.json"); } }

        // ---------- данные ----------
        internal static Appearance Load()
        {
            try { if (File.Exists(SavePath)) { var a = JsonSerializer.Deserialize<Appearance>(File.ReadAllText(SavePath), new JsonSerializerOptions { IncludeFields = true }); if (a != null) return a; } }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] не прочитал " + SavePath + ": " + e.Message); }
            return null;
        }

        static void Save()
        {
            try { File.WriteAllText(SavePath, JsonSerializer.Serialize(cur, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true })); status = "Saved"; Plugin.L.LogInfo("[CHAR] сохранено: " + SavePath); }
            catch (Exception e) { status = "Save failed: " + e.Message; Plugin.L.LogWarning("[CHAR] " + e.Message); }
        }

        static bool Has<T>(Il2CppSystem.Collections.Generic.List<T> l, T v) where T : struct
        {
            if (l == null) return false;
            for (int i = 0; i < l.Count; i++) if (EqualityComparer<T>.Default.Equals(l[i], v)) return true;
            return false;
        }

        static bool Covers(ClothesPreset c, CitizenOutfitController.CharacterAnchor a) { return Has(c.covers, a); }
        static bool Undressed(ClothesPreset c) { return Has(c.outfitCategories, ClothesPreset.OutfitCategory.undressed); }

        static void BuildPools()
        {
            poolsDirty = false;
            heads.Clear(); hairs.Clear(); tops.Clear(); bottoms.Clear(); shoes.Clear(); hats.Clear(); glasses.Clear(); nakedAll.Clear();
            var all = Toolbox.Instance.allClothes;
            var g = (Human.Gender)cur.Gender; var b = (Descriptors.BuildType)cur.Build;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i]; if (c == null) continue;
                if (!Has(c.suitableForGenders, g) || !Has(c.suitableForBuilds, b)) continue;
                var A = CitizenOutfitController.CharacterAnchor.Head;
                if (c.isHead && Covers(c, A)) { heads.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.Hair)) { hairs.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.Hat)) { hats.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.Glasses)) { glasses.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.beard)) continue;
                if (Undressed(c)) { nakedAll.Add(c); continue; }
                if (Covers(c, CitizenOutfitController.CharacterAnchor.upperTorso)) tops.Add(c);
                else if (Covers(c, CitizenOutfitController.CharacterAnchor.UpperLegRight) || Covers(c, CitizenOutfitController.CharacterAnchor.UpperLegLeft) || Covers(c, CitizenOutfitController.CharacterAnchor.lowerTorso)) bottoms.Add(c);
                else if (Covers(c, CitizenOutfitController.CharacterAnchor.RightFoot) || Covers(c, CitizenOutfitController.CharacterAnchor.LeftFoot)) shoes.Add(c);
            }
            Comparison<ClothesPreset> cmp = (x, y) => string.CompareOrdinal(x.name, y.name);
            heads.Sort(cmp); hairs.Sort(cmp); tops.Sort(cmp); bottoms.Sort(cmp); shoes.Sort(cmp); hats.Sort(cmp); glasses.Sort(cmp);
            Fix(ref cur.Head, heads, false); Fix(ref cur.Hair, hairs, true); Fix(ref cur.Top, tops, true); Fix(ref cur.Bottom, bottoms, true);
            Fix(ref cur.Shoes, shoes, true); Fix(ref cur.Hat, hats, true); Fix(ref cur.Glasses, glasses, true);
        }

        // properties нельзя передавать по ref, поэтому через временные переменные
        static void Fix(ref string v, List<ClothesPreset> pool, bool allowNone)
        {
            if (string.IsNullOrEmpty(v) && allowNone) return;
            for (int i = 0; i < pool.Count; i++) if (pool[i].name == v) return;
            v = (allowNone || pool.Count == 0) ? "" : pool[0].name;
        }

        static ClothesPreset Find(List<ClothesPreset> pool, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < pool.Count; i++) if (pool[i].name == name) return pool[i];
            return null;
        }

        // ---------- донор: настоящий горожанин из префаба игры ----------
        static void DestroyDonor()
        {
            try { if (donor != null) UnityEngine.Object.Destroy(donor); } catch (Exception) { }
            donor = null;
        }

        static bool CreateDonor()
        {
            try
            {
                var prefab = PrefabControls.Instance.citizen;
                if (prefab == null) { status = "No citizen prefab"; return false; }
                var holder = new GameObject("SodCoop_DonorHolder"); holder.SetActive(false);
                var go = UnityEngine.Object.Instantiate<GameObject>(prefab, holder.transform);
                go.name = "SodCoop_Donor";
                rebound = false; scaled.Clear();
                go.transform.SetParent(null, false);
                go.transform.position = FarPos;
                go.transform.rotation = Quaternion.identity;
                UnityEngine.Object.Destroy(holder);
                var hum = go.GetComponent<Citizen>();
                if (hum != null)
                {
                    try { hum.gender = (Human.Gender)cur.Gender; } catch (Exception) { }
                    try { if (hum.descriptors == null) hum.descriptors = new Descriptors(hum); } catch (Exception e) { Plugin.L.LogWarning("[CHAR] descriptors: " + e.Message); }
                }
                // ИИ и прочие игровые скрипты донору не нужны: оставляем только одежду и аниматор
                var mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < mbs.Length; i++)
                {
                    var m = mbs[i]; if (m == null) continue;
                    if (m.TryCast<CitizenOutfitController>() != null) continue;
                    try { m.enabled = false; } catch (Exception) { }
                }
                go.SetActive(true);
                donor = go;
                Plugin.L.LogInfo("[CHAR] донор создан из префаба, citizen=" + (hum != null));
                return true;
            }
            catch (Exception e) { status = "Donor failed: " + e.Message; Plugin.L.LogWarning("[CHAR] донор: " + e); return false; }
        }

        static Color Src(ClothesPreset.ClothingColourSource s, Color own, Color skin, Color hair, Color dflt)
        {
            switch (s)
            {
                case ClothesPreset.ClothingColourSource.skin: return skin;
                case ClothesPreset.ClothingColourSource.hair: return hair;
                case ClothesPreset.ClothingColourSource.white: return Color.white;
                case ClothesPreset.ClothingColourSource.garment:
                case ClothesPreset.ClothingColourSource.underneathColour1:
                case ClothesPreset.ClothingColourSource.underneathColour2:
                case ClothesPreset.ClothingColourSource.underneathColour3: return own;
                default: return dflt;
            }
        }

        static CitizenOutfitController.OutfitClothes Make(ClothesPreset p, Color own, Color skin, Color hair)
        {
            var oc = new CitizenOutfitController.OutfitClothes();
            oc.clothes = p.name;
            var clear = new Color(0f, 0f, 0f, 0f);
            oc.baseColor = Src(p.baseColourSource, own, skin, hair, Color.white);
            oc.color1 = Src(p.colour1Source, own, skin, hair, clear);
            oc.color2 = Src(p.colour2Source, own, skin, hair, clear);
            oc.color3 = Src(p.colour3Source, own, skin, hair, clear);
            return oc;
        }

        static readonly CitizenOutfitController.CharacterAnchor[] BodyAnchors =
        {
            CitizenOutfitController.CharacterAnchor.lowerTorso, CitizenOutfitController.CharacterAnchor.upperTorso, CitizenOutfitController.CharacterAnchor.Midriff,
            CitizenOutfitController.CharacterAnchor.UpperArmRight, CitizenOutfitController.CharacterAnchor.UpperArmLeft,
            CitizenOutfitController.CharacterAnchor.LowerArmRight, CitizenOutfitController.CharacterAnchor.LowerArmLeft,
            CitizenOutfitController.CharacterAnchor.HandRight, CitizenOutfitController.CharacterAnchor.HandLeft,
            CitizenOutfitController.CharacterAnchor.UpperLegRight, CitizenOutfitController.CharacterAnchor.UpperLegLeft,
            CitizenOutfitController.CharacterAnchor.LowerLegRight, CitizenOutfitController.CharacterAnchor.LowerLegLeft,
            CitizenOutfitController.CharacterAnchor.RightFoot, CitizenOutfitController.CharacterAnchor.LeftFoot
        };

        // собирает наряд из выбора игрока, голые части добавляет там, где ничего не надето
        static CitizenOutfitController.Outfit BuildOutfit()
        {
            Color skin = Skins[Mathf.Clamp(cur.Skin, 0, Skins.Length - 1)];
            Color hair = HairCols[Mathf.Clamp(cur.HairCol, 0, HairCols.Length - 1)];
            var chosen = new List<KeyValuePair<ClothesPreset, Color>>();
            Action<ClothesPreset, Color> add = (p, c) => { if (p != null) chosen.Add(new KeyValuePair<ClothesPreset, Color>(p, c)); };
            add(Find(heads, cur.Head), Color.white);
            add(Find(hairs, cur.Hair), hair);
            add(Find(tops, cur.Top), Cloth[cur.TopCol % Cloth.Length]);
            add(Find(bottoms, cur.Bottom), Cloth[cur.BottomCol % Cloth.Length]);
            add(Find(shoes, cur.Shoes), Cloth[cur.ShoesCol % Cloth.Length]);
            add(Find(hats, cur.Hat), Cloth[cur.TopCol % Cloth.Length]);
            add(Find(glasses, cur.Glasses), Color.white);

            var covered = new HashSet<CitizenOutfitController.CharacterAnchor>();
            foreach (var kv in chosen) for (int i = 0; i < kv.Key.covers.Count; i++) covered.Add(kv.Key.covers[i]);
            if (fillNaked) foreach (var a in BodyAnchors)
            {
                if (covered.Contains(a)) continue;
                ClothesPreset best = null;
                for (int i = 0; i < nakedAll.Count; i++)
                {
                    var n = nakedAll[i]; if (!Covers(n, a)) continue;
                    if (n.name.Contains("Prosthetic")) continue;   // протезы и прочее: перекрывают голое тело и мерцают
                    if (best == null) { best = n; continue; }
                    int sn = n.covers.Count * 10 + (n.name.Contains("Naked") ? 0 : 1), sb = best.covers.Count * 10 + (best.name.Contains("Naked") ? 0 : 1);
                    if (sn < sb || (sn == sb && string.CompareOrdinal(n.name, best.name) < 0)) best = n;
                }
                if (best == null) continue;
                add(best, Color.white);
                for (int i = 0; i < best.covers.Count; i++) covered.Add(best.covers[i]);
            }

            var outfit = new CitizenOutfitController.Outfit();
            outfit.category = ClothesPreset.OutfitCategory.casual;
            var list = new Il2CppSystem.Collections.Generic.List<CitizenOutfitController.OutfitClothes>();
            foreach (var kv in chosen) list.Add(Make(kv.Key, kv.Value, skin, hair));
            outfit.clothes = list;
            return outfit;
        }

        static void ApplyToDonor()
        {
            dirty = false;
            if (donor == null && !CreateDonor()) return;
            try
            {
                var hum = donor.GetComponent<Citizen>();
                Color skin = Skins[Mathf.Clamp(cur.Skin, 0, Skins.Length - 1)];
                Color hair = HairCols[Mathf.Clamp(cur.HairCol, 0, HairCols.Length - 1)];
                if (hum != null)
                {
                    hum.gender = (Human.Gender)cur.Gender;
                    var d = hum.descriptors;
                    if (d != null)
                    {
                        d.build = (Descriptors.BuildType)cur.Build;
                        d.skinColour = skin; d.hairColour = hair;
                        d.hairColourCategory = (Descriptors.HairColour)Mathf.Clamp(cur.HairCol, 0, HairColNames.Length - 1);
                        d.eyeColour = (Descriptors.EyeColour)cur.Eyes;
                        d.hairType = string.IsNullOrEmpty(cur.Hair) ? Descriptors.HairStyle.bald : Descriptors.HairStyle.shortHair;
                    }
                    try { hum.SetPhysicalModelParams(); } catch (Exception) { }
                }
                var oc = donor.GetComponentInChildren<CitizenOutfitController>(true);
                if (oc == null) { status = "No outfit controller"; return; }
                try { oc.enabled = true; } catch (Exception) { }
                var outfit = BuildOutfit();
                try { oc.RemoveCurrentOutfit(); } catch (Exception) { }
                oc.outfits.Clear();
                oc.outfits.Add(outfit);
                oc.SetCurrentOutfit(ClothesPreset.OutfitCategory.casual, true, true, false);
                try { oc.LoadExpression(); } catch (Exception) { }
                FixLights();
                var rs = donor.GetComponentsInChildren<Renderer>(true);
                int on = 0; for (int i = 0; i < rs.Length; i++) if (rs[i] != null && rs[i].enabled) on++;
                frameDue = 2;
                status = "Parts: " + outfit.clothes.Count + ", renderers: " + on + "/" + rs.Length;
                Plugin.L.LogInfo("[CHAR] наряд применён: " + status);
            }
            catch (Exception e) { status = "Apply failed: " + e.Message; Plugin.L.LogWarning("[CHAR] наряд: " + e); }
        }

        static bool LodPart(Transform t)
        {
            for (int d = 0; d < 20 && t != null; d++) { string n = t.name; if (n.Contains("LODS") || n.StartsWith("Level0")) return true; t = t.parent; }
            return false;
        }

        static Vector3 frameCenter = new Vector3(0f, 0.95f, 0f); static float frameH = 1.8f;

        // точные дубликаты (одна и та же сетка в одном месте, например обе ноги = один меш бёдер) дают борьбу за глубину
        static void Dedupe()
        {
            try
            {
                var seen = new HashSet<string>();
                var rs = donor.GetComponentsInChildren<Renderer>(false);
                int removed = 0;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null || !r.enabled || LodPart(r.transform) || r.name == "DistantLOD") continue;
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    var p = r.transform.position - donor.transform.position;
                    string key = mf.sharedMesh.GetInstanceID() + "|" + Mathf.Round(p.x * 500f) + "|" + Mathf.Round(p.y * 500f) + "|" + Mathf.Round(p.z * 500f) + "|" + Mathf.Sign(r.transform.lossyScale.x);
                    if (!seen.Add(key)) { r.enabled = false; lodList.Add(r); removed++; }
                }
                if (removed > 0) Plugin.Dbg("[CHAR] убрано дублей: " + removed);
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] Dedupe: " + e.Message); }
        }

        // голый низ торса лежит в одной плоскости с бёдрами: чуть увеличиваем его, как игра делает с одеждой (масштаб 1.05)
        static readonly HashSet<int> scaled = new HashSet<int>();
        static void Offset()
        {
            try
            {
                var rs = donor.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null || !r.enabled || LodPart(r.transform)) continue;
                    string n = r.name;
                    if (!n.Contains("Naked")) continue;
                    int id = r.gameObject.GetInstanceID(); if (scaled.Contains(id)) continue;
                    float k = 0f;
                    if (n.Contains("LowerTorso")) k = 1.03f;
                    else if (n.Contains("LowerArm") || n.Contains("LowerLeg")) k = 1.025f;   // локти и колени
                    else if (n.Contains("Hand") || n.Contains("Foot")) k = 1.03f;
                    if (k > 0f) { scaled.Add(id); r.transform.localScale = r.transform.localScale * k; }
                }
            }
            catch (Exception) { }
        }

        static void Frame()
        {
            Dedupe(); Offset();
            try
            {
                var rs = donor.GetComponentsInChildren<Renderer>(false);
                bool any = false; Bounds b = new Bounds();
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null || !r.enabled || LodPart(r.transform)) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!dumped)
                {
                    dumped = true;
                    var all = donor.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < all.Length && i < 60; i++)
                    {
                        var r = all[i]; if (r == null) continue;
                        string mesh = "-";
                        try { var smr = r.TryCast<SkinnedMeshRenderer>(); if (smr != null) mesh = "SMR mesh=" + (smr.sharedMesh != null ? smr.sharedMesh.name + "/" + smr.sharedMesh.vertexCount : "null") + " root=" + (smr.rootBone != null) + " lb=" + smr.localBounds.size; else { var mf = r.GetComponent<MeshFilter>(); mesh = "MR mesh=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + "/" + mf.sharedMesh.vertexCount : "null"); } } catch (Exception) { }
                        Plugin.L.LogInfo("[CHAR-R] " + r.name + " en=" + r.enabled + " go=" + r.gameObject.activeInHierarchy + " lod=" + LodPart(r.transform) + " mats=" + r.sharedMaterials.Length + " " + mesh + " pos=" + (r.transform.position - donor.transform.position));
                    }
                }
                if (!dumpedB)
                {
                    dumpedB = true;
                    try
                    {
                        var an = donor.GetComponentInChildren<Animator>(true);
                        Plugin.L.LogInfo("[CHAR-A] animator=" + (an != null) + (an == null ? "" : " ctrl=" + (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "null") + " avatar=" + (an.avatar != null ? an.avatar.name + " human=" + an.avatar.isHuman + " valid=" + an.avatar.isValid : "null") + " init=" + an.isInitialized + " hier=" + an.hasTransformHierarchy + " layers=" + an.layerCount + " enabled=" + an.enabled + " go=" + an.gameObject.name + " speed=" + an.speed));
                        var trs = donor.GetComponentsInChildren<Transform>(true);
                        Plugin.L.LogInfo("[CHAR-A] всего transform=" + trs.Length);
                        for (int i = 0; i < trs.Length && i < 70; i++)
                        {
                            var t = trs[i]; if (t == null) continue;
                            int d = 0; var p = t; while (p != null && p != donor.transform && d < 30) { p = p.parent; d++; }
                            Plugin.L.LogInfo("[CHAR-T] " + new string(' ', d) + t.name + " lp=" + t.localPosition + " wp=" + (t.position - donor.transform.position) + " ls=" + t.localScale);
                        }
                    }
                    catch (Exception e) { Plugin.L.LogWarning("[CHAR-A] " + e.Message); }
                }
                if (!any || b.size.y < 0.3f) { Plugin.L.LogWarning("[CHAR] нет видимых рендереров/нулевые границы"); return; }
                // кадр фиксированный: шапки и причёски не должны менять масштаб предпросмотра
                frameCenter = new Vector3(0f, 1.0f, 0f); frameH = 2.0f;
                Plugin.L.LogInfo("[CHAR] донор pos=" + donor.transform.position + " bounds.center=" + b.center + " size=" + b.size + " scale=" + donor.transform.lossyScale);
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] Frame: " + e.Message); }
        }

        static void FixLights()
        {
            if (donor == null) return;
            try { var m = donor.transform.Find("Model"); if (m != null) m.localScale = Vector3.one; } catch (Exception) { }
            try
            {
                var tr = donor.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < tr.Length; i++) if (tr[i] != null) tr[i].gameObject.layer = 0;
                var lgs = donor.GetComponentsInChildren<LODGroup>(true);
                for (int i = 0; i < lgs.Length; i++) if (lgs[i] != null) lgs[i].enabled = false;
                var rr = donor.GetComponentsInChildren<Renderer>(true);
                lodList.Clear();
                for (int i = 0; i < rr.Length; i++) if (rr[i] != null && (LodPart(rr[i].transform) || rr[i].name == "DistantLOD")) { rr[i].enabled = false; lodList.Add(rr[i]); }
            }
            catch (Exception) { }
            try
            {
                var sk = donor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < sk.Length; i++) if (sk[i] != null) sk[i].updateWhenOffscreen = true;
                var an = donor.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < an.Length; i++) if (an[i] != null) an[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            catch (Exception) { }
            var rs = donor.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) { try { if (rs[i] != null) rs[i].renderingLayerMask = 0xFFFFFFFFu; } catch (Exception) { } }
        }

        // ---------- камера предпросмотра ----------
        static void EnsureCamera()
        {
            if (cam != null) return;
            try
            {
                rt = new RenderTexture(640, 960, 24, RenderTextureFormat.ARGB32);
                rt.Create();
                camGo = new GameObject("SodCoop_PreviewCam");
                camGo.transform.position = FarPos + new Vector3(0f, 1f, 3f);
                cam = camGo.AddComponent<Camera>();
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.07f, 0.12f, 1f);
                cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
                cam.cullingMask = -1; cam.allowHDR = true;
                try
                {
                    var hd = camGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                    hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
                    hd.backgroundColorHDR = cam.backgroundColor;
                    hd.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
                }
                catch (Exception e) { Plugin.L.LogWarning("[CHAR] HD-камера: " + e.Message); }
                lightGo = new GameObject("SodCoop_PreviewLight");
                lightGo.transform.position = FarPos + new Vector3(2f, 3f, 4f);
                lightGo.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 3f;
                try { var hl = lightGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); hl.intensity = 2600f; hl.lightUnit = UnityEngine.Rendering.HighDefinition.LightUnit.Lux; }
                catch (Exception e) { Plugin.L.LogWarning("[CHAR] HD-свет: " + e.Message); }
                var fill = new GameObject("SodCoop_PreviewFill");
                fill.transform.SetParent(lightGo.transform, false);
                fill.transform.rotation = Quaternion.Euler(10f, 20f, 0f);
                var l2 = fill.AddComponent<Light>(); l2.type = LightType.Directional; l2.intensity = 1f;
                try { var h2 = fill.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); h2.intensity = 1000f; h2.lightUnit = UnityEngine.Rendering.HighDefinition.LightUnit.Lux; }
                catch (Exception) { }
            }
            catch (Exception e) { status = "Camera failed: " + e.Message; Plugin.L.LogWarning("[CHAR] камера: " + e); }
        }

        static void UpdateCamera()
        {
            if (cam == null || donor == null) return;
            donor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var look = donor.transform.position + frameCenter + new Vector3(0f, camY, 0f);
            camGo.transform.position = look + new Vector3(0f, 0.1f, zoom * (frameH / 1.8f));
            camGo.transform.LookAt(look);
        }

        // ---------- открытие / закрытие ----------
        internal static void OpenEditor()
        {
            if (Open) return;
            cur = Load() ?? new Appearance();
            poolsDirty = true; Open = true; status = "";
            try { menuCanvas = GameObject.Find("MenuCanvas"); if (menuCanvas != null) menuCanvas.SetActive(false); } catch (Exception) { }
            BuildPools(); EnsureCamera(); dirty = true; applyAt = 0f;
        }

        internal static void CloseEditor()
        {
            Open = false;
            try { if (menuCanvas != null) menuCanvas.SetActive(true); } catch (Exception) { }
            menuCanvas = null;
            DestroyDonor();
            try { if (camGo != null) UnityEngine.Object.Destroy(camGo); if (lightGo != null) UnityEngine.Object.Destroy(lightGo); } catch (Exception) { }
            try { if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); } } catch (Exception) { }
            camGo = null; lightGo = null; cam = null; rt = null;
        }

        internal static void Tick()
        {
            if (!Open) return;
            if (poolsDirty) BuildPools();
            if (dirty && Time.realtimeSinceStartup >= applyAt) { applyAt = Time.realtimeSinceStartup + 0.15f; ApplyToDonor(); }
            PoseDonor();
            for (int i = 0; i < lodList.Count; i++) { try { if (lodList[i] != null && lodList[i].enabled) lodList[i].enabled = false; } catch (Exception) { } }
            if (frameDue > 0 && --frameDue == 0) { Frame(); try { var oc = donor.GetComponentInChildren<CitizenOutfitController>(true); if (oc != null) oc.enabled = false; } catch (Exception) { } }
            UpdateCamera();
        }

        // аниматор донора обновляем вручную: без этого все части лежат в нуле
        static void PoseDonor()
        {
            if (donor == null) return;
            try
            {
                var an = donor.GetComponent<Animator>();
                if (an == null) an = donor.GetComponentInChildren<Animator>(true);
                if (an == null) return;
                if (an.transform.localScale.x < 0.5f) an.transform.localScale = Vector3.one;   // игра прячет модель, масштабируя «Model» в ноль
                if (an.enabled) { an.enabled = false; an.fireEvents = false; an.applyRootMotion = false; }
                for (int li = 1; li < an.layerCount; li++) an.SetLayerWeight(li, 0f);
                an.SetBool("armsLock", false); an.SetBool("carryingItem", false); an.SetBool("restrained", false);
                an.SetInteger("carryItem", 0); an.SetInteger("ArmsState", 0);
                if (!rebound) { rebound = true; try { an.Rebind(); } catch (Exception) { } }
                an.Update(0.02f);
            }
            catch (Exception e) { if (!poseWarn) { poseWarn = true; Plugin.L.LogWarning("[CHAR] PoseDonor: " + e.Message); } }
        }
        static readonly List<Renderer> lodList = new List<Renderer>();
        static bool poseWarn, dumpedB, rebound;

        // ---------- аватар другого игрока: тот же донор, но результат (модель «Model») забирает сеть ----------
        static bool remoteBusy; static int remoteFrames;
        internal static bool RemoteBusy { get { return remoteBusy; } }

        internal static bool RemoteBegin(Appearance a)
        {
            if (Open || remoteBusy || a == null) return false;
            try
            {
                cur = a; BuildPools();
                DestroyDonor();
                if (!CreateDonor()) return false;
                ApplyToDonor();
                if (donor == null) return false;
                remoteBusy = true; remoteFrames = 0;
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] RemoteBegin: " + e.Message); DestroyDonor(); return false; }
        }

        internal static void RemoteAbort() { remoteBusy = false; DestroyDonor(); }

        // 0 = ещё готовится, 1 = готово (model отцеплена от донора), -1 = не получилось
        internal static int RemoteStep(out Transform model, out List<Renderer> lod)
        {
            model = null; lod = null;
            if (!remoteBusy || donor == null) { remoteBusy = false; return -1; }
            try
            {
                PoseDonor();
                for (int i = 0; i < lodList.Count; i++) { try { if (lodList[i] != null && lodList[i].enabled) lodList[i].enabled = false; } catch (Exception) { } }
                if (++remoteFrames < 3) return 0;
                Dedupe(); Offset();
                try { var oc = donor.GetComponentInChildren<CitizenOutfitController>(true); if (oc != null) oc.enabled = false; } catch (Exception) { }
                var rr = donor.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rr.Length; i++)
                {
                    var r = rr[i]; if (r == null) continue;
                    if ((LodPart(r.transform) || r.name == "DistantLOD") && !lodList.Contains(r)) { r.enabled = false; lodList.Add(r); }
                }
                PoseDonor();
                var an = donor.GetComponentInChildren<Animator>(true);
                if (an == null || an.transform == donor.transform) { Plugin.L.LogWarning("[CHAR] у донора нет отдельной «Model» с аниматором"); RemoteAbort(); return -1; }
                model = an.transform;
                lod = new List<Renderer>(lodList);
                model.SetParent(null, true);
                var old = donor; donor = null; remoteBusy = false;
                UnityEngine.Object.Destroy(old);
                return 1;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] RemoteStep: " + e); RemoteAbort(); return -1; }
        }

        internal static string ToJson(Appearance a)
        {
            try { return JsonSerializer.Serialize(a, new JsonSerializerOptions { IncludeFields = true }); } catch (Exception) { return ""; }
        }

        internal static Appearance FromJson(string j)
        {
            try { if (string.IsNullOrEmpty(j)) return null; return JsonSerializer.Deserialize<Appearance>(j, new JsonSerializerOptions { IncludeFields = true }); }
            catch (Exception) { return null; }
        }

        // ---------- IMGUI ----------
        static GUIStyle btnStyle, labelStyle, titleStyle;

        static void Styles()
        {
            if (btnStyle != null) return;
            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            labelStyle.normal.textColor = new Color(0.95f, 0.85f, 0.9f, 1f);
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.95f, 0.3f, 0.35f, 1f);
        }

        static bool MenuVisible()
        {
            try
            {
                var mm = MainMenuController.Instance;
                return mm != null && mm.mainMenuActive;
            }
            catch (Exception) { return false; }
        }

        internal static void OnGui()
        {
            try
            {
                Styles();
                float s = Screen.height / 1080f;
                var old = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
                if (!Open)
                {
                    if (MenuVisible() && !NetMenu.Open && GUI.Button(new Rect(278f, 940f, 297f, 48f), "CHARACTER CREATE", btnStyle)) OpenEditor();
                    GUI.matrix = old; return;
                }
                float W = Screen.width / s, H = Screen.height / s;
                GUI.color = new Color(0f, 0f, 0f, 1f);
                GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(40f, 20f, 800f, 50f), "CHARACTER CREATE", titleStyle);

                float x = 40f, y = 90f, w = 520f;
                bool ch = false;
                ch |= Choice(ref y, x, w, "Gender", GenderNames, ref cur.Gender, true);
                ch |= Choice(ref y, x, w, "Build", BuildNames, ref cur.Build, true);
                ch |= Choice(ref y, x, w, "Eyes", EyeNames, ref cur.Eyes, false);
                ch |= Swatches(ref y, x, w, "Skin tone", Skins, ref cur.Skin);
                ch |= Swatches(ref y, x, w, "Hair colour", HairCols, ref cur.HairCol);
                bool pool = false;
                pool |= Pick(ref y, x, w, "Face", heads, v => cur.Head = v, cur.Head, false);
                pool |= Pick(ref y, x, w, "Hair", hairs, v => cur.Hair = v, cur.Hair, true);
                pool |= Pick(ref y, x, w, "Top", tops, v => cur.Top = v, cur.Top, true);
                ch |= Swatches(ref y, x, w, "Top colour", Cloth, ref cur.TopCol);
                pool |= Pick(ref y, x, w, "Bottom", bottoms, v => cur.Bottom = v, cur.Bottom, true);
                ch |= Swatches(ref y, x, w, "Bottom colour", Cloth, ref cur.BottomCol);
                pool |= Pick(ref y, x, w, "Shoes", shoes, v => cur.Shoes = v, cur.Shoes, true);
                ch |= Swatches(ref y, x, w, "Shoes colour", Cloth, ref cur.ShoesCol);
                pool |= Pick(ref y, x, w, "Hat", hats, v => cur.Hat = v, cur.Hat, true);
                pool |= Pick(ref y, x, w, "Glasses", glasses, v => cur.Glasses = v, cur.Glasses, true);
                if (ch) { poolsDirty = true; }
                if (ch || pool) dirty = true;

                // предпросмотр
                var pr = new Rect(620f, 90f, 640f, 900f);
                if (rt != null) GUI.DrawTexture(pr, rt, ScaleMode.ScaleToFit, false);
                var e = Event.current;
                if (e != null && pr.Contains(e.mousePosition))
                {
                    if (e.type == EventType.MouseDrag)
                    {
                        if (e.button == 2) camY = Mathf.Clamp(camY - e.delta.y * 0.004f * zoom, -0.9f, 0.9f);
                        else yaw += e.delta.x * 0.6f;
                        e.Use();
                    }
                    else if (e.type == EventType.ScrollWheel) { zoom = Mathf.Clamp(zoom + e.delta.y * 0.15f, 0.5f, 6f); e.Use(); }
                }
                GUI.Label(new Rect(620f, 995f, 640f, 30f), "LMB drag: rotate   MMB drag: up/down   Wheel: zoom", labelStyle);

                if (GUI.Button(new Rect(1320f, 880f, 240f, 56f), "SAVE", btnStyle)) Save();
                if (GUI.Button(new Rect(1320f, 950f, 240f, 56f), "CLOSE", btnStyle)) CloseEditor();
                if (GUI.Button(new Rect(1320f, 810f, 240f, 56f), "RANDOM", btnStyle)) Randomize();
                GUI.Label(new Rect(1320f, 90f, 560f, 120f), status, labelStyle);
                bool fn = GUI.Toggle(new Rect(1320f, 740f, 400f, 36f), fillNaked, " Fill bare body parts myself", btnStyle);
                if (fn != fillNaked) { fillNaked = fn; dirty = true; }
                GUI.matrix = old;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] GUI: " + e.Message); }
        }

        static void Randomize()
        {
            var r = new System.Random();
            cur.Gender = r.Next(0, 3); cur.Build = r.Next(0, 4); cur.Eyes = r.Next(0, 4);
            cur.Skin = r.Next(0, Skins.Length); cur.HairCol = r.Next(0, HairCols.Length);
            cur.TopCol = r.Next(0, Cloth.Length); cur.BottomCol = r.Next(0, Cloth.Length); cur.ShoesCol = r.Next(0, Cloth.Length);
            BuildPools();
            Func<List<ClothesPreset>, string> any = l => l.Count == 0 ? "" : l[r.Next(0, l.Count)].name;
            cur.Head = any(heads); cur.Hair = any(hairs); cur.Top = any(tops); cur.Bottom = any(bottoms); cur.Shoes = any(shoes);
            cur.Hat = ""; cur.Glasses = "";
            dirty = true;
        }

        // ряд кнопок-вариантов
        static bool Choice(ref float y, float x, float w, string label, string[] names, ref int v, bool unused)
        {
            GUI.Label(new Rect(x, y, 150f, 34f), label, labelStyle);
            float bw = (w - 160f) / names.Length; bool ch = false;
            for (int i = 0; i < names.Length; i++)
            {
                GUI.color = i == v ? new Color(1f, 0.45f, 0.5f, 1f) : Color.white;
                if (GUI.Button(new Rect(x + 160f + i * bw, y, bw - 4f, 34f), names[i], btnStyle)) { if (v != i) { v = i; ch = true; } }
            }
            GUI.color = Color.white; y += 42f; return ch;
        }

        static bool Swatches(ref float y, float x, float w, string label, Color[] cols, ref int v)
        {
            GUI.Label(new Rect(x, y, 150f, 30f), label, labelStyle);
            float bw = (w - 160f) / cols.Length; bool ch = false;
            for (int i = 0; i < cols.Length; i++)
            {
                var rc = new Rect(x + 160f + i * bw, y, bw - 3f, 28f);
                GUI.color = cols[i];
                GUI.DrawTexture(rc, Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (i == v) GUI.Box(rc, "");
                if (GUI.Button(rc, GUIContent.none, GUIStyle.none)) { if (v != i) { v = i; ch = true; } }
            }
            y += 36f; return ch;
        }

        // [<] имя [>]
        static bool Pick(ref float y, float x, float w, string label, List<ClothesPreset> pool, Action<string> set, string curName, bool allowNone)
        {
            GUI.Label(new Rect(x, y, 150f, 34f), label, labelStyle);
            int idx = -1; for (int i = 0; i < pool.Count; i++) if (pool[i].name == curName) { idx = i; break; }
            string shown = idx >= 0 ? pool[idx].name : (allowNone ? "(none)" : "-");
            bool ch = false;
            int count = pool.Count + (allowNone ? 1 : 0);
            if (count > 0)
            {
                int pos = allowNone ? idx + 1 : Mathf.Max(0, idx); // 0 = (none) при allowNone
                int np = pos;
                if (GUI.Button(new Rect(x + 160f, y, 40f, 34f), "<", btnStyle)) np = (pos + count - 1) % count;
                if (GUI.Button(new Rect(x + w - 40f, y, 40f, 34f), ">", btnStyle)) np = (pos + 1) % count;
                if (np != pos)
                {
                    int pi = allowNone ? np - 1 : np;
                    set(pi < 0 ? "" : pool[pi].name); ch = true;
                }
            }
            GUI.Label(new Rect(x + 206f, y, w - 250f, 34f), shown, labelStyle);
            y += 40f; return ch;
        }
    }


    // Поиск серверов в локальной сети: свой UDP-обмен (порт 47777), не зависит от версии LiteNetLib
    internal static class Lan
    {
        const int DiscPort = 47777;
        const string Q = "SODCOOP?1", A = "SODCOOP!1";
        internal class Server { public string Name = "", Seed = "", Build = "", Ip = ""; public bool Pw; public int Players, Max = 2, Port; public float Seen; }
        internal static readonly List<Server> Servers = new List<Server>();
        static UdpClient hostSock, scanSock;
        static float nextScan;

        internal static void StartAdvertise()
        {
            StopAdvertise();
            try
            {
                var u = new UdpClient(AddressFamily.InterNetwork);
                u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                u.Client.Bind(new IPEndPoint(IPAddress.Any, DiscPort));
                u.Client.Blocking = false;
                try { u.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception) { }
                hostSock = u;
                Plugin.L.LogInfo("[LAN] Сервер виден в локальной сети (UDP " + DiscPort + ")");
            }
            catch (Exception e) { Plugin.L.LogWarning("[LAN] Не удалось открыть порт поиска " + DiscPort + ": " + e.Message); hostSock = null; }
        }

        internal static void StopAdvertise()
        {
            try { if (hostSock != null) hostSock.Close(); } catch (Exception) { }
            hostSock = null;
        }

        internal static void CloseScan()
        {
            try { if (scanSock != null) scanSock.Close(); } catch (Exception) { }
            scanSock = null;
        }

        internal static void Scan()
        {
            try
            {
                if (scanSock == null)
                {
                    scanSock = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                    scanSock.EnableBroadcast = true;
                    scanSock.Client.Blocking = false;
                    try { scanSock.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception) { }
                }
                var data = Encoding.UTF8.GetBytes(Q);
                var targets = new HashSet<string> { "255.255.255.255", "127.0.0.1" };
                try
                {
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                            var ip = ua.Address.GetAddressBytes(); var mk = ua.IPv4Mask.GetAddressBytes();
                            var bc = new byte[4]; for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mk[i]);
                            targets.Add(new IPAddress(bc).ToString());
                        }
                    }
                }
                catch (Exception) { }
                foreach (var t in targets)
                {
                    try { scanSock.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(t), DiscPort)); } catch (Exception) { }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[LAN] Scan: " + e.Message); }
        }

        internal static void Tick()
        {
            try
            {
                // хост отвечает на запросы
                if (hostSock != null)
                {
                    int guard = 0;
                    while (hostSock.Client.Available > 0 && guard++ < 20)
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        var d = hostSock.Receive(ref ep);
                        if (Encoding.UTF8.GetString(d) != Q) continue;
                        using (var ms = new MemoryStream())
                        {
                            using (var bw = new BinaryWriter(ms))
                            {
                                bw.Write(A); bw.Write(Net.RtName ?? ""); bw.Write(!string.IsNullOrEmpty(Net.RtPassword));
                                bw.Write(Net.PlayerCount); bw.Write(Net.MaxPlayersPublic); bw.Write(Net.RtPort); bw.Write(Net.SeedInfo()); bw.Write(Net.BuildInfo());
                            }
                            var rb = ms.ToArray();
                            hostSock.Send(rb, rb.Length, ep);
                        }
                    }
                }
                // клиент собирает ответы
                if (scanSock != null)
                {
                    int guard = 0;
                    while (scanSock.Client.Available > 0 && guard++ < 50)
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        var d = scanSock.Receive(ref ep);
                        try
                        {
                            using (var br = new BinaryReader(new MemoryStream(d)))
                            {
                                if (br.ReadString() != A) continue;
                                var sv = new Server();
                                sv.Name = br.ReadString(); sv.Pw = br.ReadBoolean(); sv.Players = br.ReadInt32(); sv.Max = br.ReadInt32();
                                sv.Port = br.ReadInt32(); sv.Seed = br.ReadString(); sv.Build = br.ReadString();
                                sv.Ip = ep.Address.ToString(); sv.Seen = Time.realtimeSinceStartup;
                                int idx = Servers.FindIndex(x => x.Ip == sv.Ip && x.Port == sv.Port);
                                if (idx >= 0) Servers[idx] = sv; else Servers.Add(sv);
                            }
                        }
                        catch (Exception) { }
                    }
                    Servers.RemoveAll(x => Time.realtimeSinceStartup - x.Seen > 8f);
                }
                if (NetMenu.Open && Time.realtimeSinceStartup >= nextScan) { nextScan = Time.realtimeSinceStartup + 2.5f; Scan(); }
                if (!NetMenu.Open && scanSock != null) { CloseScan(); Servers.Clear(); }
            }
            catch (SocketException) { }
            catch (Exception e) { Plugin.L.LogWarning("[LAN] Tick: " + e.Message); }
        }
    }


    // Автопрохождение меню игры: нажимает те же кнопки, что и игрок (пути и обработчики взяты из разведки)
    // Подсказки в главном меню для хоста/клиента (само меню игры мод не трогает: сохранение выбирает и запускает игрок)
    internal static class MenuAuto
    {
        internal static string Notice = "";
        static string lastNotice = ""; static float noticeAt;

        internal static List<string> SaveNames()
        {
            var l = new List<KeyValuePair<DateTime, string>>();
            try
            {
                foreach (var f in Directory.GetFiles(Net.SaveDir(), "*.sodb"))
                {
                    if (Path.GetFileName(f) == "SodCoop_sync.sodb") continue;
                    l.Add(new KeyValuePair<DateTime, string>(File.GetLastWriteTimeUtc(f), Path.GetFileName(f)));
                }
            }
            catch (Exception) { }
            l.Sort((x, y) => y.Key.CompareTo(x.Key));
            var r = new List<string>(); foreach (var kv in l) r.Add(kv.Value);
            return r;
        }

        internal static void LoadSave(string fileName)
        {
            Notice = (fileName ?? "") == ""
                ? "HOST: choose a save with Load Game and press Start. The server starts when the city has loaded."
                : "Save received. Press Load Game, pick '" + Path.GetFileNameWithoutExtension(fileName) + "' and press Start. You connect automatically when the city has loaded.";
            Plugin.L.LogInfo("[MENU] ручная загрузка: " + (fileName ?? "(хост)"));
        }

        internal static void NewGame()
        {
            Notice = "HOST: start a New Game. The server starts when the city has loaded.";
            Plugin.L.LogInfo("[MENU] ручная новая игра");
        }

        internal static void Tick()
        {
            if (Notice != lastNotice) { lastNotice = Notice; noticeAt = Time.realtimeSinceStartup; }
            if (Notice != "" && Time.realtimeSinceStartup - noticeAt > 120f) { Notice = ""; lastNotice = ""; }
        }
    }

    // Меню «Мультиплеер» (IMGUI поверх главного меню): категория LAN, список серверов, HOST / CONNECT / DIRECT
    internal static class NetMenu
    {
        internal static bool Open;
        static int page;                 // 0 список, 1 хост, 2 прямое подключение, 3 пароль выбранного сервера
        static GameObject menuCanvas;
        static string fName = "", fPass = "", fPort = "9050", fAddr = "127.0.0.1", fAPort = "9050", fAPass = "";
        static string selIp = ""; static int selPort; static string msg = "";
        static GUIStyle btn, lab, title, field, rowBtn;

        static void Styles()
        {
            if (btn != null) return;
            btn = new GUIStyle(GUI.skin.button) { fontSize = 20 };
            lab = new GUIStyle(GUI.skin.label) { fontSize = 20 };
            lab.normal.textColor = new Color(0.95f, 0.85f, 0.9f, 1f);
            title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.95f, 0.3f, 0.35f, 1f);
            field = new GUIStyle(GUI.skin.textField) { fontSize = 22 };
            rowBtn = new GUIStyle(GUI.skin.button) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
        }

        static bool MenuVisible()
        {
            try { var mm = MainMenuController.Instance; return mm != null && mm.mainMenuActive; } catch (Exception) { return false; }
        }

        static void OpenMenu()
        {
            Net.EnsureRt(); MenuAuto.Notice = "";
            Open = true; page = 0; msg = ""; selIp = ""; selPort = 0;
            fName = Net.RtName; fPass = Net.RtPassword; fPort = "" + Net.RtPort; fAddr = Net.RtAddress; fAPort = "" + Net.RtPort; fAPass = "";
            try { menuCanvas = GameObject.Find("MenuCanvas"); if (menuCanvas != null) menuCanvas.SetActive(false); } catch (Exception) { }
            Lan.Servers.Clear(); Lan.Scan();
        }

        static void CloseMenu()
        {
            Open = false;
            try { if (menuCanvas != null) menuCanvas.SetActive(true); } catch (Exception) { }
            menuCanvas = null;
        }

        static bool Port(string t, out int p) { return int.TryParse(t, out p) && p >= 1024 && p <= 65535; }

        internal static void OnGui()
        {
            try
            {
                if (CharEditor.Open) return;
                Styles();
                float s = Screen.height / 1080f;
                var old = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
                if (!Open)
                {
                    if (MenuVisible())
                    {
                        if (GUI.Button(new Rect(585f, 940f, 297f, 48f), "MULTIPLAYER", btn)) OpenMenu();
                        string st = Net.StatusLine();
                        if (st != "") GUI.Label(new Rect(278f, 994f, 1400f, 34f), st, lab);
                    }
                    GUI.matrix = old; return;
                }
                float W = Screen.width / s, H = Screen.height / s;
                GUI.color = Color.black; GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(new Rect(40f, 20f, 900f, 50f), page == 1 ? "HOST A GAME" : page == 2 ? "DIRECT CONNECT" : page == 3 ? "PASSWORD" : "MULTIPLAYER", title);
                if (page == 0) Page0(W, H);
                else if (page == 1) PageHost();
                else if (page == 2) PageDirect();
                else PagePass();
                string cur = Net.StatusLine();
                if (cur != "") GUI.Label(new Rect(40f, H - 100f, W - 80f, 34f), "Now: " + cur, lab);
                if (msg != "") GUI.Label(new Rect(40f, H - 140f, W - 80f, 34f), msg, lab);
                GUI.matrix = old;
            }
            catch (Exception) { }
        }

        static void Page0(float W, float H)
        {
            GUI.enabled = true;
            GUI.Button(new Rect(40f, 90f, 140f, 40f), "LAN", btn);
            GUI.enabled = false; GUI.Button(new Rect(190f, 90f, 160f, 40f), "INTERNET", btn); GUI.enabled = true;

            float y = 150f;
            GUI.Label(new Rect(50f, y, 420f, 30f), "SERVER", lab);
            GUI.Label(new Rect(480f, y, 160f, 30f), "PLAYERS", lab);
            GUI.Label(new Rect(640f, y, 300f, 30f), "ADDRESS", lab);
            GUI.Label(new Rect(950f, y, 400f, 30f), "CITY SEED", lab);
            y += 36f;
            var list = Lan.Servers;
            if (list.Count == 0) GUI.Label(new Rect(50f, y, 1200f, 34f), "No servers found yet. The host must have loaded a city. Use DIRECT to type an address.", lab);
            for (int i = 0; i < list.Count && i < 12; i++)
            {
                var sv = list[i];
                bool sel = sv.Ip == selIp && sv.Port == selPort;
                GUI.color = sel ? new Color(1f, 0.55f, 0.55f, 1f) : Color.white;
                if (GUI.Button(new Rect(40f, y, 1340f, 40f), "", rowBtn)) { selIp = sv.Ip; selPort = sv.Port; }
                GUI.color = Color.white;
                GUI.Label(new Rect(50f, y + 4f, 420f, 32f), (sv.Pw ? "[lock] " : "") + sv.Name, lab);
                GUI.Label(new Rect(480f, y + 4f, 160f, 32f), sv.Players + " / " + sv.Max, lab);
                GUI.Label(new Rect(640f, y + 4f, 300f, 32f), sv.Ip + ":" + sv.Port, lab);
                GUI.Label(new Rect(950f, y + 4f, 420f, 32f), sv.Seed, lab);
                y += 44f;
            }

            float by = H - 70f;
            if (GUI.Button(new Rect(40f, by, 200f, 50f), "HOST", btn)) { page = 1; msg = ""; hostSaves = MenuAuto.SaveNames(); hostSel = 0; }
            Server_Connect(new Rect(250f, by, 200f, 50f));
            if (GUI.Button(new Rect(460f, by, 200f, 50f), "DIRECT", btn)) { page = 2; msg = ""; }
            if (GUI.Button(new Rect(670f, by, 200f, 50f), "REFRESH", btn)) { Lan.Servers.Clear(); Lan.Scan(); }
            if (GUI.Button(new Rect(880f, by, 240f, 50f), "DISABLE MULTIPLAYER", btn)) { Net.ApplyOff(); msg = "Multiplayer disabled."; }
            if (GUI.Button(new Rect(W - 240f, by, 200f, 50f), "BACK", btn)) CloseMenu();
        }

        static void Server_Connect(Rect r)
        {
            Lan.Server sv = null;
            foreach (var x in Lan.Servers) if (x.Ip == selIp && x.Port == selPort) sv = x;
            GUI.enabled = sv != null;
            if (GUI.Button(r, "CONNECT", btn) && sv != null)
            {
                if (sv.Pw) { fAPass = ""; page = 3; msg = ""; }
                else { Net.ApplyClient(sv.Ip, sv.Port, ""); Net.StartTransfer(); Done(""); }
            }
            GUI.enabled = true;
        }

        static void Done(string m) { msg = m; CloseMenu(); }

        static List<string> hostSaves = new List<string>(); static int hostSel;

        static void PageHost()
        {
            float x = 40f, y = 110f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Server name", lab); y += 32f;
            fName = GUI.TextField(new Rect(x, y, 600f, 40f), fName ?? "", 32, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Password (empty = no password)", lab); y += 32f;
            fPass = GUI.PasswordField(new Rect(x, y, 600f, 40f), fPass ?? "", '*', 32, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Port", lab); y += 32f;
            fPort = GUI.TextField(new Rect(x, y, 200f, 40f), fPort ?? "", 5, field); y += 56f;
            GUI.Label(new Rect(x, y, 800f, 30f), "Max players: " + Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4) + " (Network/MaxPlayers in the config, 2..4)", lab);
            y += 50f;
            GUI.Label(new Rect(x, y, 1100f, 30f), "HOST returns to the main menu: load a save or start a new game yourself. The server starts when the city has loaded.", lab); y += 60f;
            if (GUI.Button(new Rect(x, y, 300f, 50f), "HOST (LOAD GAME)", btn)) StartHost(false);
            if (GUI.Button(new Rect(x + 320f, y, 260f, 50f), "HOST NEW GAME", btn)) StartHost(true);
            if (GUI.Button(new Rect(x + 600f, y, 200f, 50f), "BACK", btn)) { page = 0; msg = ""; }
        }

        static void StartHost(bool newGame)
        {
            int p;
            if (string.IsNullOrWhiteSpace(fName)) { msg = "Enter a server name."; return; }
            if (!Port(fPort, out p)) { msg = "Port must be a number from 1024 to 65535."; return; }
            Net.ApplyHost(fName.Trim(), fPass ?? "", p);
            CloseMenu();
            if (newGame) MenuAuto.NewGame(); else MenuAuto.LoadSave("");
            msg = "";
        }

        static void PageDirect()
        {
            float x = 40f, y = 110f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Host IP address", lab); y += 32f;
            fAddr = GUI.TextField(new Rect(x, y, 400f, 40f), fAddr ?? "", 40, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Port", lab); y += 32f;
            fAPort = GUI.TextField(new Rect(x, y, 200f, 40f), fAPort ?? "", 5, field); y += 56f;
            GUI.Label(new Rect(x, y, 600f, 30f), "Password (if the server has one)", lab); y += 32f;
            fAPass = GUI.PasswordField(new Rect(x, y, 600f, 40f), fAPass ?? "", '*', 32, field); y += 70f;
            if (GUI.Button(new Rect(x, y, 260f, 50f), "CONNECT", btn))
            {
                int p;
                if (string.IsNullOrWhiteSpace(fAddr)) msg = "Enter the host IP address.";
                else if (!Port(fAPort, out p)) msg = "Port must be a number from 1024 to 65535.";
                else { Net.ApplyClient(fAddr.Trim(), p, fAPass ?? ""); Net.StartTransfer(); Done(""); }
            }
            if (GUI.Button(new Rect(x + 280f, y, 200f, 50f), "BACK", btn)) { page = 0; msg = ""; }
        }

        static void PagePass()
        {
            Lan.Server sv = null;
            foreach (var x in Lan.Servers) if (x.Ip == selIp && x.Port == selPort) sv = x;
            float px = 40f, y = 110f;
            GUI.Label(new Rect(px, y, 900f, 30f), sv != null ? "Server: " + sv.Name + "  (" + sv.Ip + ":" + sv.Port + ")" : "Server is gone", lab); y += 50f;
            GUI.Label(new Rect(px, y, 600f, 30f), "Password", lab); y += 32f;
            fAPass = GUI.PasswordField(new Rect(px, y, 600f, 40f), fAPass ?? "", '*', 32, field); y += 70f;
            GUI.enabled = sv != null;
            if (GUI.Button(new Rect(px, y, 260f, 50f), "CONNECT", btn) && sv != null)
            {
                Net.ApplyClient(sv.Ip, sv.Port, fAPass ?? "");
                Net.StartTransfer();
                Done("");
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(px + 280f, y, 200f, 50f), "BACK", btn)) { page = 0; msg = ""; }
        }
    }

    public class SodCoopDriver : MonoBehaviour
    {
        public SodCoopDriver(IntPtr ptr) : base(ptr) { }
        float nextLog; bool alive; KeyCode key = KeyCode.F4; bool keyParsed;
        public void OnGUI() { CharEditor.OnGui(); NetMenu.OnGui(); Net.DrawHud(); }
        public void Update()
        {
            try
            {
                CharEditor.Tick();
                Lan.Tick();
                Net.Poll();
                MenuAuto.Tick();
                if (Net.Started && Net.KeyDown(KeyCode.End)) Net.LeaveSession();
                if (!alive) { alive = true; Plugin.L.LogInfo("[DRV] Update работает"); }
                if (!keyParsed)
                {
                    keyParsed = true;
                    KeyCode k; if (Enum.TryParse<KeyCode>(Plugin.CfgProbeKey.Value ?? "F4", true, out k)) key = k;
                    Plugin.L.LogInfo("[DRV] клавиша разведки: " + key);
                }
                if (Net.KeyDown(key))
                {
                    Plugin.L.LogInfo("[DRV] клавиша нажата, запускаю разведку");
                    var mm = MainMenuController.Instance;
                    bool menu = false;
                    try { menu = mm != null && mm.mainMenuActive; } catch (Exception) { }
                    Probe.Run(menu ? "главное меню" : "игра");
                }
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup > nextLog) { nextLog = Time.realtimeSinceStartup + 30f; Plugin.L.LogWarning("[DRV] " + e); }
            }
        }
    }

    internal static class Probe
    {
        static void ListDir(StringBuilder sb, string dir, int depth)
        {
            try
            {
                if (!System.IO.Directory.Exists(dir) || depth > 2) return;
                sb.AppendLine("[DIR] " + dir);
                int n = 0;
                foreach (var f in System.IO.Directory.GetFiles(dir)) { if (n++ > 40) break; var fi = new System.IO.FileInfo(f); sb.AppendLine("[FILE]   " + fi.Name + " " + fi.Length + " B " + fi.LastWriteTime); }
                foreach (var d in System.IO.Directory.GetDirectories(dir)) ListDir(sb, d, depth + 1);
            }
            catch (Exception) { }
        }

        static void DumpType(StringBuilder sb, System.Reflection.Assembly asm, string name, bool brief)
        {
            try
            {
                Type t = null;
                foreach (var x in asm.GetTypes()) if (x.Name == name) { t = x; break; }
                if (t == null) { sb.AppendLine("[API] нет типа " + name); return; }
                sb.AppendLine("[API] === " + t.FullName + " : " + (t.BaseType != null ? t.BaseType.Name : ""));
                var f = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var p in t.GetProperties(f)) sb.AppendLine("[API]   prop " + p.PropertyType.Name + " " + p.Name + (p.GetMethod != null && p.GetMethod.IsStatic ? " (static)" : ""));
                if (brief) return;
                foreach (var m in t.GetMethods(f))
                {
                    if (m.IsSpecialName || m.Name.StartsWith("_") || m.Name.StartsWith("NativeMethod")) continue;
                    var ps = m.GetParameters(); var sig = new List<string>();
                    foreach (var q in ps) sig.Add(q.ParameterType.Name + " " + q.Name);
                    sb.AppendLine("[API]   " + (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", sig.ToArray()) + ")");
                }
            }
            catch (Exception e) { sb.AppendLine("[API] " + name + ": " + e.Message); }
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 8 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static string Join<T>(Il2CppSystem.Collections.Generic.List<T> l)
        {
            if (l == null) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); sb.Append(l[i].ToString()); }
            return sb.ToString();
        }

        internal static void Run(string where)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== PROBE " + where + " ===");
            try
            {
                var mm = MainMenuController.Instance;
                if (mm != null)
                {
                    sb.AppendLine("[MENU] mainMenuActive=" + mm.mainMenuActive + " container=" + (mm.mainMenuContainer != null ? PathOf(mm.mainMenuContainer) : "null"));
                    try { sb.AppendLine("[MENU] components=" + mm.components.Count); } catch (Exception) { }
                    var mroot = GameObject.Find("MenuCanvas");
                    var bs = (mroot != null ? mroot.transform : mm.transform).GetComponentsInChildren<ButtonController>(true);
                    for (int i = 0; i < bs.Length && i < 400; i++)
                    {
                        var b = bs[i]; if (b == null) continue;
                        string txt = ""; try { txt = b.text != null ? b.text.text : ""; } catch (Exception) { }
                        string dict = ""; try { dict = b.textDictionary + "/" + b.textReference + " auto=" + b.useAutomaticText; } catch (Exception) { }
                        var rt = b.transform as RectTransform;
                        string rs = rt != null ? ("pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta + " anchors=" + rt.anchorMin + "-" + rt.anchorMax) : "";
                        string ev = "";
                        try
                        {
                            var ub = b.button;
                            if (ub != null)
                            {
                                int n = ub.onClick.GetPersistentEventCount();
                                for (int k = 0; k < n; k++)
                                {
                                    var tg = ub.onClick.GetPersistentTarget(k);
                                    ev += " [" + (tg != null ? tg.GetType().Name + ":" + tg.name : "null") + "." + ub.onClick.GetPersistentMethodName(k) + "]";
                                }
                                if (n == 0) ev = " (нет постоянных обработчиков)";
                            }
                            else ev = " (нет Button)";
                        }
                        catch (Exception e) { ev = " (onClick: " + e.Message + ")"; }
                        sb.AppendLine("[BTN] " + PathOf(b.transform) + " | text='" + txt + "' | active=" + b.gameObject.activeInHierarchy + " | onClick=" + ev);
                    }
                    try
                    {
                        var cs = mm.components;
                        for (int i = 0; i < cs.Count; i++) { var c = cs[i]; if (c != null) sb.AppendLine("[COMP] " + c.component + " rect=" + (c.rect != null ? PathOf(c.rect) : "null")); }
                    }
                    catch (Exception e) { sb.AppendLine("[COMP] ошибка: " + e.Message); }
                }
                else sb.AppendLine("[MENU] MainMenuController.Instance = null");
            }
            catch (Exception e) { sb.AppendLine("[MENU] ошибка: " + e); }

            try
            {
                var all = Toolbox.Instance.allClothes;
                sb.AppendLine("[CLOTHES] allClothes=" + (all != null ? all.Count : -1));
                for (int i = 0; false && all != null && i < all.Count; i++)
                {
                    var c = all[i]; if (c == null) continue;
                    sb.AppendLine("[CL] " + c.name + " | cat=" + Join(c.outfitCategories) + " | gender=" + Join(c.suitableForGenders) + " | build=" + Join(c.suitableForBuilds) + " | covers=" + Join(c.covers) + " | head=" + c.isHead + " | models=" + (c.models != null ? c.models.Count : -1) + " | tags=" + Join(c.tags) + " | hair=" + Join(c.suitableForHairstyle) + " | chance=" + c.baseChance);
                }
            }
            catch (Exception e) { sb.AppendLine("[CLOTHES] ошибка: " + e); }

            try
            {
                var go = PrefabControls.Instance.citizen;
                sb.AppendLine("[PREFAB] citizen=" + (go != null ? go.name : "null"));
                if (go != null)
                {
                    var comps = go.GetComponents<Component>();
                    for (int i = 0; i < comps.Length; i++) sb.AppendLine("[PREFAB]   root: " + comps[i]);
                    sb.AppendLine("[PREFAB] outfitCtrl in children=" + (go.GetComponentInChildren<CitizenOutfitController>(true) != null) + " animator=" + (go.GetComponentInChildren<Animator>(true) != null) + " citizen=" + (go.GetComponent<Citizen>() != null));
                    var h = new GameObject("SodCoop_ProbeHolder"); h.SetActive(false);
                    try
                    {
                        var inst = UnityEngine.Object.Instantiate<GameObject>(go, h.transform);
                        var oc = inst.GetComponentInChildren<CitizenOutfitController>(true);
                        sb.AppendLine("[PREFAB] инстанс создан без города: outfitCtrl=" + (oc != null) + (oc != null ? (" human=" + (oc.human != null) + " anchors=" + oc.anchorConfig.Count) : "") + " renderers=" + inst.GetComponentsInChildren<Renderer>(true).Length);
                        var anim = inst.GetComponentInChildren<Animator>(true);
                        if (anim != null) sb.AppendLine("[PREFAB] animator controller=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null"));
                    }
                    catch (Exception e) { sb.AppendLine("[PREFAB] инстанс не удался: " + e.Message); }
                    UnityEngine.Object.Destroy(h);
                }
            }
            catch (Exception e) { sb.AppendLine("[PREFAB] ошибка: " + e); }

            try
            {
                var city = CityData.Instance;
                if (city != null && city.citizenDirectory != null && city.citizenDirectory.Count > 0)
                {
                    Citizen cit = null;
                    for (int i = 0; i < city.citizenDirectory.Count; i++) { var c = city.citizenDirectory[i]; if (c != null && c.outfitController != null && c.descriptors != null) { cit = c; break; } }
                    if (cit != null)
                    {
                        var d = cit.descriptors;
                        sb.AppendLine("[CIT] " + cit.citizenName + " gender=" + cit.gender + " genderScale=" + cit.genderScale + " build=" + d.build + " height=" + d.height + " heightCM=" + d.heightCM + " skin=" + d.skinColour + " hairStyle=" + d.hairType + " hairCat=" + d.hairColourCategory + " hairCol=" + d.hairColour + " eyes=" + d.eyeColour + " footwear=" + d.footwear + " facial=" + (d.facialFeatures != null ? d.facialFeatures.Count : -1));
                        var oc = cit.outfitController;
                        sb.AppendLine("[CIT] currentOutfit=" + oc.currentOutfit + " loaded=" + oc.loadedOutfit + " outfits=" + oc.outfits.Count + " hair=" + (oc.currentHair != null ? oc.currentHair.name : "-") + " hat=" + (oc.currentHat != null ? oc.currentHat.name : "-"));
                        for (int i = 0; i < oc.outfits.Count; i++)
                        {
                            var o = oc.outfits[i]; if (o == null) continue;
                            var line = new StringBuilder("[OUTFIT] " + o.category + ": ");
                            for (int k = 0; k < o.clothes.Count; k++) { var oc2 = o.clothes[k]; line.Append(oc2.clothes).Append(" base=").Append(oc2.baseColor).Append(" c1=").Append(oc2.color1).Append("; "); }
                            sb.AppendLine(line.ToString());
                        }
                    }
                }
                else sb.AppendLine("[CIT] города нет (это меню) - горожан не смотрю");
            }
            catch (Exception e) { sb.AppendLine("[CIT] ошибка: " + e); }

            try
            {
                var pl = Player.Instance;
                if (pl != null) sb.AppendLine("[PLAYER] gender=" + pl.gender + " descriptors=" + (pl.descriptors != null) + " outfitController=" + (pl.outfitController != null));
            }
            catch (Exception) { }

            try
            {
                var asm = typeof(MainMenuController).Assembly;
                var names = new List<string>();
                foreach (var t in asm.GetTypes())
                {
                    string n = t.Name;
                    if (n.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("CityConstruct", StringComparison.OrdinalIgnoreCase) >= 0)
                        if (!n.StartsWith("<") && !n.Contains("__")) names.Add(n);
                }
                sb.AppendLine("[API] типы с Save/Load: " + string.Join(", ", names.ToArray()));
                string[] want = { "SaveStateController", "SaveGameEntryController", "CityConstructor", "Game", "StateSaveData", "CitySaveData", "SessionData" };
                foreach (var w in want) DumpType(sb, asm, w, w == "SessionData");
            }
            catch (Exception e) { sb.AppendLine("[API] ошибка: " + e.Message); }

            try
            {
                sb.AppendLine("[PATH] persistentDataPath=" + Application.persistentDataPath + " dataPath=" + Application.dataPath);
                ListDir(sb, Application.persistentDataPath, 0);
                try { ListDir(sb, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? "", "Save"), 0); } catch (Exception) { }
            }
            catch (Exception e) { sb.AppendLine("[PATH] ошибка: " + e.Message); }

            try
            {
                var mm2 = MainMenuController.Instance;
                if (mm2 != null)
                {
                    var l1 = mm2.spawnedLoadGames; var l2 = mm2.spawnedSaveGames;
                    sb.AppendLine("[SAVES] load=" + (l1 != null ? l1.Count : -1) + " save=" + (l2 != null ? l2.Count : -1) + " selected=" + (mm2.selectedSave != null ? mm2.selectedSave.name : "null"));
                    for (int i = 0; l1 != null && i < l1.Count && i < 20; i++) { var e1 = l1[i]; if (e1 != null) sb.AppendLine("[SAVES]   load[" + i + "] " + e1.name + " " + e1.GetType().Name); }
                    try { var sec = mm2.selectedSave ?? (l1 != null && l1.Count > 0 ? l1[0] : null); if (sec != null) DumpType(sb, typeof(MainMenuController).Assembly, "SaveGameEntryController", false); } catch (Exception) { }
                }
            }
            catch (Exception e) { sb.AppendLine("[SAVES] ошибка: " + e.Message); }

            try
            {
                string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "SodCoop_probe_" + (where.StartsWith("гл") ? "menu" : "game") + ".txt");
                System.IO.File.WriteAllText(path, sb.ToString());
                Plugin.L.LogInfo("[PROBE] записано: " + path);
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROBE] запись: " + e.Message); }
        }
    }

    internal static class Net
    {
        const string Key = "sodcoop-1";

        // ---------- контекст удалённого игрока ----------
        // Состояние одного второго игрока. Статические поля ниже (avatar, puppet, remote*...) - рабочая копия активного игрока:
        // Enter(rp) загружает их из rp, Leave() сохраняет обратно. Так весь код аватара работает на любое число игроков.
        sealed class RP
        {
            public int pid; public NetPeer peer; public string clientId = ""; public string profileId = ""; public string label = "";
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
            public Transform boneTorso;
            public float remoteLean;
            public float leanSmooth;
            public bool remoteBlocking;
            public bool remoteConsuming;
            public bool remoteSitting;
            public bool paramSit;
            public bool paramCrouch;
            public bool remoteCarryObj;
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
            public Transform mapTracked;
        }

        static readonly List<RP> players = new List<RP>();
        static RP curRp;
        internal static int MyPid;   // у хоста 0, у клиента номер, выданный хостом

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
        // для кода, который читает поля игроков вне цикла: свежие значения активного игрока попадают в его RP
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
            boneTorso = r.boneTorso;
            remoteLean = r.remoteLean;
            leanSmooth = r.leanSmooth;
            remoteBlocking = r.remoteBlocking;
            remoteConsuming = r.remoteConsuming;
            remoteSitting = r.remoteSitting;
            paramSit = r.paramSit;
            paramCrouch = r.paramCrouch;
            remoteCarryObj = r.remoteCarryObj;
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
            r.boneTorso = boneTorso;
            r.remoteLean = remoteLean;
            r.leanSmooth = leanSmooth;
            r.remoteBlocking = remoteBlocking;
            r.remoteConsuming = remoteConsuming;
            r.remoteSitting = remoteSitting;
            r.paramSit = paramSit;
            r.paramCrouch = paramCrouch;
            r.remoteCarryObj = remoteCarryObj;
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
            r.mapTracked = mapTracked;
        }

        const byte MsgHello = 1;
        const byte MsgHelloReply = 2;
        const byte MsgPos = 3;
        const byte MsgAction = 4;     // 1 = удар, 2 = блок
        static float combatUntil;     // до какого момента держим боевую стойку
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
        const byte MsgPuppet = 12;      // хост -> клиент: humanID горожанина-двойника
        const byte MsgNpcAct = 15;      // хост -> клиент: разовые действия горожанина (удар, бросок, блок, оружие в руках)
        const byte MsgSaveReq = 18, MsgSaveMeta = 19, MsgSaveWant = 20, MsgSaveChunk = 21, MsgSaveDone = 22, MsgSaveFail = 23;   // передача сохранения хост -> клиент
        const byte MsgBodyFx = 17;      // игрок -> игрок: меня ударили / я упал / я встал / я умер (для анимации аватара)
        const byte MsgShot = 24;        // игрок -> игрок: я выстрелил (оружие, дуло, точка прицеливания)
        const byte MsgWeapon = 25;      // игрок -> игрок: какое оружие у меня в руках
        const byte MsgRelay = 26;       // хост -> клиент: [pid отправителя][тип][данные] - сообщение другого клиента
        const byte MsgPlayerInfo = 27;  // хост -> клиент: [pid][внешний вид] - сведения о другом игроке
        const byte MsgPlayerLeft = 28;  // хост -> клиент: [pid] - игрок вышел
        const byte MsgProfile = 29;     // профиль игрока: [1][json] клиент->хост сохранить; [2] клиент->хост запросить; [3][есть][json] хост->клиент; [4] хост->клиент: сохраняю игру, пришли профиль сейчас
        const byte MsgSound = 16;       // хост -> клиент: звук горожанина в бою
        const byte MsgPuppetHurt = 13;  // хост -> клиент: горожанин ударил двойника = ударил тебя

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
            catch (Exception) { }
            return -1;
        }

        // Кулаки в руках: у предмета есть действие со SpecialAction.punch
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
            catch (Exception) { }
            fistsCached = res;
            return res;
        }

        // Крен камеры игрока (наклон Q/E), градусы -180..180
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

        // Блок удерживается: ПКМ зажата. Если читать кнопку не получается, держим блок 0.5 с после вызова Block()
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
                RP sitter = null; float bestD = float.MaxValue;
                for (int pi = 0; pi < players.Count; pi++)
                {
                    var o = players[pi];
                    if (!o.remoteSitting || o.avatar == null) continue;
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
        static Vector3 sitLastPos;
        // Игрок сидит: переход "...Enter" с именем из SitKeywords включает сидение, переход "...Exit" или ходьба выключают
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
                    if (exit) sitLatched = false;
                    else if (match) { sitLatched = true; sitLastPos = p.transform.position; }
                }
                if (sitLatched && !act)
                {
                    // Переход закончился, а мы всё ещё сидим: сбрасываем, только если игрок ушёл от места
                    Vector3 d = p.transform.position - sitLastPos; d.y = 0f;
                    if (d.sqrMagnitude > 1.5f) sitLatched = false;
                }
                string sig = act + "|" + tn + "|" + inm + "|" + sitLatched;
                if (sig != lastSitLog)
                {
                    lastSitLog = sig;
                    Plugin.Dbg($"[SIT] переход активен={act} имя='{tn}' объект='{inm}' сидит={sitLatched}");
                }
            }
            catch (Exception) { }
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
                    catch (Exception) { }
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

        // Отправка позиции и удалённый игрок
        static float sendTimer;
        static GameObject avatar;     // корень аватара: стоит на уровне ног игрока
        static Vector3 targetPos;     // позиция ног
        static float targetYaw;       // куда смотрит игрок
        static bool avatarSnap;
        static Animator avatarAnim;
        static Transform avatarModel;
        static Vector3 modelBaseScale = Vector3.one;
        static Vector3 modelBasePos;
        static Quaternion modelBaseRot = Quaternion.identity;
        static Transform boneUL, boneUR, boneLL, boneLR, boneTorso;
        static float remoteLean, leanSmooth;
        static bool remoteBlocking;
        static bool remoteConsuming, remoteSitting, paramSit, paramCrouch, remoteCarryObj;
        static float sitK;
        static float blockUntil;
        static float crouchBoneK, crouchAnimK;
        static float crouchK;         // 0 = стоит, 1 = присел
        static bool remoteCrouched, remoteRunning, remoteFists;
        static float armsW;
        static int lastCycleCao = -1;
        static float nextCycleAttack;
        static float animSpeed;       // сглаженная скорость аватара, м/с

        internal static bool Started { get; private set; }
        static bool IsHost { get { EnsureRt(); return RtMode == NetMode.Host; } }

        // ---- параметры сессии на лету (из меню «Мультиплеер»); по умолчанию берутся из конфига ----
        internal static NetMode RtMode = NetMode.Off;
        internal static string RtAddress = "127.0.0.1", RtPassword = "", RtName = "SodCoop server";
        internal static int RtPort = 9050;
        static bool rtInit, connectRejected;

        internal static void EnsureRt()
        {
            if (rtInit) return;
            rtInit = true;
            try { RtMode = Plugin.CfgMode.Value; RtAddress = Plugin.CfgAddress.Value; RtPort = Plugin.CfgPort.Value; RtPassword = Plugin.CfgPassword.Value ?? ""; RtName = Plugin.CfgServerName.Value ?? "SodCoop server"; }
            catch (Exception) { }
        }

        internal static int PlayerCount { get { try { return manager == null ? 1 : players.Count + 1; } catch (Exception) { return 1; } } }
        internal static int MaxPlayersPublic { get { return MaxPlayersNow(); } }
        internal static string SeedInfo() { try { return "" + CityData.Instance.seed; } catch (Exception) { return ""; } }
        internal static string BuildInfo() { try { return "" + Game.Instance.buildID; } catch (Exception) { return ""; } }

        // ---- уведомления, отключение и переподключение ----
        static readonly List<KeyValuePair<string, float>> toasts = new List<KeyValuePair<string, float>>();
        static readonly string MyClientId = Guid.NewGuid().ToString("N");   // клиент: по нему хост узнаёт переподключившегося игрока
        static float lostAt = -1f;               // клиент: когда потеряна связь с хостом (-1 = связь есть/не терялась)
        static bool everConnected;               // клиент: уже был в игре на этом сервере
        static GUIStyle hudStyle, hudShadow;

        internal static void Toast(string text)
        {
            try { toasts.Add(new KeyValuePair<string, float>(text, Time.realtimeSinceStartup + 6f)); while (toasts.Count > 4) toasts.RemoveAt(0); } catch (Exception) { }
            Plugin.L.LogInfo("[HUD] " + text);
        }

        static bool InMainMenu() { try { var mm = MainMenuController.Instance; return mm != null && mm.mainMenuActive; } catch (Exception) { return false; } }

        internal static void DrawHud()
        {
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
            }
            catch (Exception) { }
        }

        // клиент потерял хоста: возвращаем горожанам их собственный ИИ, чтобы игра не стояла
        static void ClientReleaseWorld()
        {
            try { npcTargets.Clear(); } catch (Exception) { }
            if (npcClientPaused)
            {
                try { Game.Instance.SetPauseAI(false); } catch (Exception) { }
                npcClientPaused = false;
                Plugin.L.LogInfo("[NPC] клиент: связь с хостом потеряна, ИИ горожан снова включён");
            }
        }

        // End: выйти из мультиплеера (хост закрывает сервер, клиент перестаёт переподключаться)
        internal static void LeaveSession()
        {
            bool wasClient = RtMode == NetMode.Client;
            Plugin.L.LogInfo("[NET] End: выход из мультиплеера");
            ApplyOff();
            if (wasClient) ClientReleaseWorld();
            lostAt = -1f; everConnected = false;
            Toast("Multiplayer is off. Use the MULTIPLAYER button in the main menu to start again.");
        }

        internal static void Shutdown()
        {
            try { ProfileOnShutdown(); } catch (Exception) { }
            lostAt = -1f; everConnected = false; weaponSent = null;
            try { Lan.StopAdvertise(); } catch (Exception) { }
            try { if (manager != null) manager.Stop(); } catch (Exception) { }
            manager = null; listener = null; serverPeer = null; connecting = false; connectRejected = false; Started = false;
            try { ClearPlayers(); } catch (Exception) { }
            MyPid = 0;
        }

        internal static void ApplyHost(string name, string pass, int port)
        {
            EnsureRt(); Shutdown();
            RtName = name; RtPassword = pass ?? ""; RtPort = port; RtMode = NetMode.Host;
            try { Plugin.CfgServerName.Value = name; Plugin.CfgPassword.Value = RtPassword; Plugin.CfgPort.Value = port; } catch (Exception) { }
            Plugin.L.LogInfo($"[NET] Из меню: хост «{name}», порт {port}, пароль={(RtPassword != "" ? "да" : "нет")}");
        }

        internal static void ApplyClient(string addr, int port, string pass)
        {
            EnsureRt(); Shutdown();
            RtAddress = addr; RtPort = port; RtPassword = pass ?? ""; RtMode = NetMode.Client;
            try { Plugin.CfgAddress.Value = addr; } catch (Exception) { }
            Plugin.L.LogInfo($"[NET] Из меню: подключение к {addr}:{port}, пароль={(RtPassword != "" ? "да" : "нет")}");
        }

        internal static void ApplyOff() { EnsureRt(); Shutdown(); RtMode = NetMode.Off; Plugin.L.LogInfo("[NET] Из меню: мультиплеер выключен"); }

        internal static string StatusLine()
        {
            EnsureRt();
            if (xferMode) return "CLIENT: " + xferText;
            if (MenuAuto.Notice != "") return MenuAuto.Notice;
            if (RtMode == NetMode.Off) return "";
            if (RtMode == NetMode.Host && sendStage != 0) return "HOSTING: sending the save to a player...";
            if (RtMode == NetMode.Host) return (manager != null ? "HOSTING" : "HOST (starts when the city has loaded)") + ": " + RtName + " : " + RtPort + (manager != null ? "   [" + (players.Count + 1) + "/" + Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4) + " players" + (players.Count == 0 ? ", waiting" : "") + "]   (End = stop)" : "");
            if (connectRejected) return "REJECTED by " + RtAddress + ":" + RtPort + " (wrong password?)";
            if (serverPeer != null) return "CONNECTED to " + RtAddress + ":" + RtPort + "   (End = leave multiplayer)";
            if (lostAt >= 0f && manager != null) return "CONNECTION LOST, reconnecting to " + RtAddress + ":" + RtPort + " ...   (End = leave multiplayer)";
            return (manager != null ? "CONNECTING" : "CLIENT (connects when the city has loaded; load the same save as the host)") + ": " + RtAddress + ":" + RtPort;
        }


        static Appearance remoteApp;
        static string remoteAppJson = "";

        // ================= Передача сохранения (хост -> клиент) =================
        class XFile { public string Name = "", Dest = ""; public byte Kind; public long Size, Got; public bool Need; public FileStream Fs; }
        static bool xferMode, xferFinish; static string xferFailMsg; static string xferText = "", xferSaveName = ""; static float xferAt;
        static readonly List<XFile> xfFiles = new List<XFile>();
        // хост
        static NetPeer sendPeer; static int sendStage, sendIdx; static object snapTask; static float snapAt; static List<XFile> sendFiles;
        static int snapVariant; static DateTime snapStartUtc;
        const string SyncName = "SodCoop_sync.sodb";

        internal static string SaveDir() { var d = Path.Combine(Application.persistentDataPath, "Save"); try { Directory.CreateDirectory(d); } catch (Exception) { } return d; }
        internal static string CityDir() { var d = Path.Combine(Application.persistentDataPath, "Cities"); try { Directory.CreateDirectory(d); } catch (Exception) { } return d; }

        internal static void StartTransfer()
        {
            xferMode = true; xferFinish = false; xferFailMsg = null; xferText = "Connecting to the host..."; xferAt = Time.realtimeSinceStartup; xferSaveName = "";
            xfFiles.Clear();
            Start();
        }

        static void XferFail(string m)
        {
            Plugin.L.LogWarning("[XFER] " + m);
            foreach (var f in xfFiles) { try { if (f.Fs != null) f.Fs.Close(); } catch (Exception) { } f.Fs = null; }
            xferMode = false; xferFinish = false; xferText = "Transfer failed: " + m;
            Shutdown();
            RtMode = NetMode.Off;
            MenuAuto.Notice = "Could not get the host's save: " + m;
        }

        static void XferFinish()
        {
            try
            {
                foreach (var f in xfFiles)
                {
                    if (!f.Need) continue;
                    if (f.Fs != null) { f.Fs.Close(); f.Fs = null; }
                    if (f.Got != f.Size) { XferFail("file " + f.Name + " incomplete (" + f.Got + "/" + f.Size + ")"); return; }
                    if (File.Exists(f.Dest)) File.Delete(f.Dest);
                    File.Move(f.Dest + ".part", f.Dest);
                }
            }
            catch (Exception e) { XferFail("saving files: " + e.Message); return; }
            string name = xferSaveName;
            Plugin.L.LogInfo("[XFER] сохранение получено: " + name);
            xferMode = false; xferFinish = false; xferText = "";
            Shutdown();   // на время загрузки города сеть выключаем; клиент подключится снова, когда мир загрузится
            MenuAuto.LoadSave(name);
        }

        static void XferTick()
        {
            HostSendTick();
            if (xferFailMsg != null) { var m = xferFailMsg; xferFailMsg = null; XferFail(m); return; }
            if (xferFinish) { XferFinish(); return; }
            if (xferMode && Time.realtimeSinceStartup - xferAt > 90f) XferFail("timed out waiting for the host (is the host in a loaded city?)");
        }

        // ---- клиент ----
        static void ClientOnMeta(NetDataReader r)
        {
            xfFiles.Clear();
            int n = r.GetInt();
            for (int i = 0; i < n; i++)
            {
                var f = new XFile(); f.Name = Path.GetFileName(r.GetString()); f.Kind = r.GetByte(); f.Size = r.GetLong();
                xfFiles.Add(f);
            }
            var w = new NetDataWriter(); w.Put(MsgSaveWant); w.Put(n);
            foreach (var f in xfFiles)
            {
                string ext = Path.GetExtension(f.Name).ToLowerInvariant();
                bool okExt = ext == ".sodb" || ext == ".citb" || ext == ".txt";
                f.Dest = Path.Combine(f.Kind == 0 ? SaveDir() : CityDir(), f.Name);
                bool have = f.Kind == 1 && File.Exists(f.Dest) && new FileInfo(f.Dest).Length == f.Size;
                f.Need = okExt && !have;
                if (f.Kind == 0) xferSaveName = f.Name;
                w.Put(f.Need);
                Plugin.L.LogInfo($"[XFER] файл {f.Name}: {f.Size} Б, {(f.Need ? "нужен" : "уже есть")}");
            }
            serverPeer.Send(w, DeliveryMethod.ReliableOrdered);
            xferAt = Time.realtimeSinceStartup;
            xferText = "Receiving the save...";
        }

        static void ClientOnChunk(NetDataReader r)
        {
            int idx = r.GetByte(); int n = r.GetInt();
            var buf = new byte[n]; r.GetBytes(buf, 0, n);
            if (idx < 0 || idx >= xfFiles.Count) return;
            var f = xfFiles[idx];
            if (f.Fs == null) f.Fs = new FileStream(f.Dest + ".part", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            f.Fs.Write(buf, 0, n); f.Got += n;
            xferAt = Time.realtimeSinceStartup;
            long tot = 0, got = 0; foreach (var x in xfFiles) if (x.Need) { tot += x.Size; got += x.Got; }
            xferText = "Receiving the save: " + (got / 1048576.0).ToString("F1") + " / " + (tot / 1048576.0).ToString("F1") + " MB";
        }

        // ---- хост ----
        static void HostFail(string m)
        {
            Plugin.L.LogWarning("[XFER] " + m);
            try { if (sendPeer != null) { var w = new NetDataWriter(); w.Put(MsgSaveFail); w.Put(m); sendPeer.Send(w, DeliveryMethod.ReliableOrdered); } } catch (Exception) { }
            HostEndSend();
        }

        static void HostEndSend()
        {
            try { if (sendFiles != null) foreach (var f in sendFiles) { if (f.Fs != null) f.Fs.Close(); f.Fs = null; } } catch (Exception) { }
            sendStage = 0; sendPeer = null; sendFiles = null; snapTask = null;
        }

        static void HostBeginSend(NetPeer peer)
        {
            if (sendStage != 0) { try { var w = new NetDataWriter(); w.Put(MsgSaveFail); w.Put("host is busy sending to another player"); peer.Send(w, DeliveryMethod.ReliableOrdered); } catch (Exception) { } return; }
            sendPeer = peer; sendFiles = new List<XFile>(); snapTask = null; sendIdx = 0; snapVariant = 0;
            Plugin.L.LogInfo("[XFER] клиент просит сохранение");
            SnapStart();
            sendStage = 1;
        }

        // игра принимает «путь» сохранения не очень понятно в каком виде: пробуем три варианта по очереди
        static void SnapStart()
        {
            string full = Path.Combine(SaveDir(), SyncName);
            string[] variants = { Path.Combine(SaveDir(), "SodCoop_sync.sod"), full, Path.GetFileNameWithoutExtension(SyncName) };
            if (snapVariant == 0) { try { foreach (var o in Directory.GetFiles(SaveDir(), "SodCoop_sync*")) File.Delete(o); } catch (Exception) { } }
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
                try { var p = t.GetType().GetProperty(n); if (p != null) sb.Append(n + "=" + p.GetValue(t, null) + " "); } catch (Exception) { }
            }
            try { var p = t.GetType().GetProperty("Exception"); var ex = p != null ? p.GetValue(t, null) : null; if (ex != null) sb.Append("Exception=" + ex); } catch (Exception) { }
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
                    if (Path.GetFileName(f).StartsWith("SodCoop_sync")) t = t.AddDays(1);   // наш снимок важнее случайного автосохранения
                    if (t >= snapStartUtc.AddSeconds(-1) && new FileInfo(f).Length > 0 && t > bt) { bt = t; best = f; }
                }
            }
            catch (Exception) { }
            return best;
        }

        static bool TaskDone(object t)
        {
            if (t == null) return true;
            try { var p = t.GetType().GetProperty("IsCompleted"); if (p != null) return (bool)p.GetValue(t, null); } catch (Exception) { }
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

        static void SetRemoteAppearance(string json)
        {
            json = json ?? "";
            if (json == remoteAppJson) return;
            remoteAppJson = json;
            remoteApp = CharEditor.FromJson(json);
            Plugin.L.LogInfo("[CHAR] получен внешний вид второго игрока: " + (remoteApp == null ? "нет (останется модель жителя)" : "голова=" + remoteApp.Head + " волосы=" + remoteApp.Hair + " верх=" + remoteApp.Top + " низ=" + remoteApp.Bottom));
            if (avatar != null) DestroyAvatar();   // пересоздастся со следующим пакетом позиции
        }

        static string Describe()
        {
            var c = CityData.Instance;
            return $"seed={c.seed} build={Game.Instance.buildID} " +
                   $"npc={c.citizenDictionary.Count} doors={c.doorDictionary.Count}";
        }

        // Высота ног игрока (нижняя точка капсулы контроллера)
        static Vector3 lastSendPos; static bool haveSendPos, talkLogged, talkHold; static float talkHoldAt, talkLogAt;

        // пол под игроком: луч вниз от тела (попадает в пол или мебель, на которой он стоит)
        static float GroundBelow(Vector3 pos, float fallback)
        {
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(pos + new Vector3(0f, 0.3f, 0f), Vector3.down, out hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            }
            catch (Exception) { }
            return fallback;
        }
        static float FeetY(Player p)
        {
            try
            {
                var cc = p.charController;
                if (cc != null) return cc.bounds.min.y;
            }
            catch (Exception) { }
            return p.transform.position.y - 0.9f;
        }

        // Куда смотрит игрок (по камере)
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
                        if (k != Key) { request.Reject(); return; }
                        if (!string.IsNullOrEmpty(RtPassword) && pw != RtPassword)
                        {
                            Plugin.L.LogWarning("[NET] Неверный пароль при подключении, отклоняю");
                            request.Reject(); return;
                        }
                        request.Accept();
                    }
                    catch (Exception) { try { request.Reject(); } catch (Exception) { } }
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
                        if (xferMode && xferFailMsg == null && !xferFinish)
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
            if (manager == null) return;
            try
            {
                manager.PollEvents();
                XferTick();
                if (manager == null) return;

                // Клиент пробует подключиться, пока хост не станет доступен
                if (!IsHost && serverPeer == null && !connecting && !connectRejected &&
                    Time.realtimeSinceStartup >= nextAttempt)
                {
                    connecting = true;
                    var cw = new NetDataWriter();
                    cw.Put(Key); cw.Put(RtPassword ?? "");
                    manager.Connect(RtAddress, RtPort, cw);
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

        // Вызывается каждый кадр из Player.Update
        internal static void Tick(Player p)
        {
            bool debug = Plugin.CfgDebugAvatar.Value;
            if (manager == null && !debug) return;
            Leave();
            try
            {
                float dt = Time.unscaledDeltaTime;

                // Тестовый режим: аватар всегда в 3 м перед камерой лицом к игроку, сеть не нужна
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

                // Отправка своей позиции ~20 раз в секунду
                if (manager != null)
                {
                    sendTimer += dt;
                    if (sendTimer >= 0.05f && manager.ConnectedPeersCount > 0)
                    {
                        sendTimer = 0f;
                        Vector3 pos = p.transform.position;
                        float feetSend = FeetY(p);
                        // во время разговора игра двигает/поднимает камеру и тело: аватар остаётся там, где игрок стоял
                        bool talkNow = false;
                        try { var ic0 = InteractionController.Instance; talkNow = ic0 != null && (ic0.dialogMode || ic0.talkingTo != null); } catch (Exception) { }
                        // после разговора тело ещё несколько кадров/секунд возвращается на землю: ждём, пока высота вернётся
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
                        var w = new NetDataWriter();
                        w.Put(MsgPos);
                        w.Put(pos.x);
                        w.Put(feetSend);
                        w.Put(pos.z);
                        w.Put(LookYaw(p));
                        byte fl = 0;
                        try { if (p.isCrouched) fl |= 1; if (p.isRunning) fl |= 2; } catch (Exception) { }
                        bool fistsNow = FistsDrawn();
                        if (fistsNow) fl |= 4;
                        try { var fpc = FirstPersonItemController.Instance; if (fpc != null && fpc.isConsuming && !fpc.forceHolstered && !string.IsNullOrEmpty(curItem) && curItem != "nothing") fl |= 16; } catch (Exception) { }
                        if (SittingNow(p)) fl |= 32;
                        try { if (InteractionController.Instance != null && InteractionController.Instance.carryingObject != null) fl |= 64; } catch (Exception) { }

                        w.Put(fl);
                        w.Put(LeanRoll());
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

                try { NpcTick(dt); } catch (Exception e) { if (!npcErrLogged) { npcErrLogged = true; Plugin.L.LogWarning($"[NPC] {e.Message}"); } }
                try { ProfileTick(dt); } catch (Exception e) { if (!profErrLogged) { profErrLogged = true; Plugin.L.LogWarning($"[PROFILE] {e.Message}"); } }
                try { KoTick(p, dt); } catch (Exception e) { if (!koErrLogged) { koErrLogged = true; Plugin.L.LogWarning($"[KO] {e.Message}"); } }

                // Синхронизация предметов мира (которые несёт игрок)
                if (Plugin.CfgObjSync.Value)
                {
                    if (manager != null && manager.ConnectedPeersCount > 0) LocalObjects(dt);
                    RemoteObjectsTick(dt);
                    HiddenTick();
                    DoorTick();
                }

                // Плавное движение удалённых игроков
                for (int pi = 0; pi < players.Count; pi++)
                {
                    Enter(players[pi]);
                    if (avatar != null)
                    {
                        var tr = avatar.transform;
                        Vector3 tp = targetPos + new Vector3(0f, Plugin.CfgAvatarYOffset.Value, 0f);
                        Quaternion tq = Quaternion.Euler(0f, targetYaw + Plugin.CfgAvatarYawOffset.Value, 0f);

                        Vector3 before = tr.position;
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
                            d.y = 0f;
                            float inst = dt > 0.0001f ? d.magnitude / dt : 0f;
                            animSpeed = Mathf.Lerp(animSpeed, inst, 1f - Mathf.Exp(-8f * dt));
                        }

                        // Анимация ходьбы: параметр moveSpeed (0..1) по скорости перемещения
                        if (avatarAnim != null)
                        {
                            float ms = Mathf.Clamp01(animSpeed / Plugin.CfgMaxSpeed.Value);
                            if (remoteRunning && animSpeed > 0.5f) ms = 1f;
                            if (ms < 0.03f) ms = 0f;
                            avatarAnim.SetFloat("moveSpeed", ms);
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
                                // Подбор: каждые 5 с следующее значение combatArmsOverride, каждые 2 с удар
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

                            // Руки свободны: отключаем слои-оверрайды (ArmsOverride, UmbrellaOverride)
                            // и состояния "несёт предмет"
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
                            paramCrouch = !paramSit && remoteCrouched && Plugin.CfgCrouchIdleValue.Value >= 0 && animSpeed < 0.3f;
                            if (paramSit) idleVal = Plugin.CfgSitIdleValue.Value;
                            else if (paramCrouch) idleVal = Plugin.CfgCrouchIdleValue.Value;
                            avatarAnim.SetInteger("IdleAnimationState", idleVal);

                            // Ручной подбор из конфига (Debug.Force*)
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

                        // Аниматор выключен и обновляется вручную: так мы можем поверх анимации
                        // довернуть кости (присед), и игра не перезапишет их до следующего кадра
                        if (avatarAnim != null) UpdatePose(dt);
                        if (avatarAnim != null) avatarAnim.Update(dt);

                        crouchK = Mathf.MoveTowards(crouchK, remoteCrouched ? 1f : 0f, dt * 6f);
                        crouchBoneK = Mathf.MoveTowards(crouchBoneK, (remoteCrouched && !poseAnimCrouch) ? 1f : 0f, dt * 6f);
                        crouchAnimK = Mathf.MoveTowards(crouchAnimK, (remoteCrouched && poseAnimCrouch) ? 1f : 0f, dt * 6f);
                        if (avatarModel != null)
                        {
                            // Корпус вниз (ноги уходят в пол) и наклон по Q/E
                            sitK = Mathf.MoveTowards(sitK, poseAnimSit ? 1f : 0f, dt * 4f);
                            avatarModel.localScale = modelBaseScale * Mathf.Clamp(Plugin.CfgAvatarScale.Value, 0.3f, 2f);
                            avatarModel.localPosition = modelBasePos + new Vector3(0f, -(Plugin.CfgCrouchDrop.Value * crouchBoneK + Plugin.CfgDropWithAnim.Value * crouchAnimK + Plugin.CfgSitDrop.Value * sitK), 0f);
                            leanSmooth = Mathf.Lerp(leanSmooth, remoteLean, 1f - Mathf.Exp(-10f * dt));
                            avatarModel.localRotation = Quaternion.Euler(0f, 0f, -leanSmooth * (poseAnimLean ? Plugin.CfgTiltWithAnim.Value : Plugin.CfgLeanScale.Value)) * modelBaseRot;
                        }
                        UpdateHeld();

                        if (crouchBoneK > 0.001f)
                        {
                            float hip = Plugin.CfgCrouchHip.Value * crouchBoneK;
                            float knee = Plugin.CfgCrouchKnee.Value * crouchBoneK;
                            if (boneUL != null) boneUL.localRotation = boneUL.localRotation * AxisRot(hip);
                            if (boneUR != null) boneUR.localRotation = boneUR.localRotation * AxisRot(hip);
                            if (boneLL != null) boneLL.localRotation = boneLL.localRotation * AxisRot(knee);
                            if (boneLR != null) boneLR.localRotation = boneLR.localRotation * AxisRot(knee);
                            if (boneTorso != null) boneTorso.localRotation = boneTorso.localRotation * AxisRot(Plugin.CfgCrouchTorso.Value * crouchBoneK);
                        }
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

        // ---------------- Аватар ----------------

        // Есть ли уже загруженный NPC, у которого можно взять модель (в меню города ещё нет)
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
            catch (Exception) { }
            return false;
        }

        static void CreateAvatar()
        {
            // Пока мир не загружен, аватар не создаём: иначе вместо персонажа останется капсула
            if (Plugin.CfgUseCitizenModel.Value && !CitizenModelAvailable()) return;
            if (CharEditor.RemoteBusy) return;   // другой игрок собирается: подождём (пакет позиции повторит попытку)

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

        // сборка моделей: игроки по очереди (редактор персонажа собирает одну модель за раз)
        static void PollRemoteModel()
        {
            for (int i = 0; i < players.Count; i++)
            {
                Enter(players[i]);
                try { PollRemoteModelOne(); } catch (Exception) { }
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
                for (int i = 0; i < remoteLod.Count; i++) { try { if (remoteLod[i] != null && remoteLod[i].enabled) remoteLod[i].enabled = false; } catch (Exception) { } }
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
                catch (Exception) { }
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
                for (int i = 0; i < rs.Length; i++) { try { if (rs[i] != null) rs[i].renderingLayerMask = 0xFFFFFFFFu; } catch (Exception) { } }
                avatarSnap = true;
                Plugin.L.LogInfo($"[CHAR] аватар собран по внешнему виду: рендереров {rs.Length}, аниматор={(anim != null)}, кости ноги={boneUL != null && boneUR != null}");
                return anim != null;
            }
            catch (Exception e) { Plugin.L.LogWarning("[CHAR] AttachRemoteModel: " + e); return false; }
        }

        // Клонирует модель одного из NPC (только внешний вид и аниматор, без ИИ)
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

                // Клон создаём под выключенным родителем: пока он неактивен, Awake/OnEnable
                // игровых скриптов не срабатывают, и мы успеваем убрать лишние компоненты
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
                    anim.enabled = false;   // обновляем вручную в Tick
                    anim.fireEvents = false;
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    avatarAnim = anim;
                    try
                    {
                        // Гасим «руки заняты»: оверрайд-слои рук/зонта и связанные параметры
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

                // Включаем рендереры модели (кроме служебных LOD-копий)
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

        // Запасной вариант: зелёная капсула с "носом"
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
            // Игра на HDRP: берём шейдер HDRP напрямую
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

        // ---------- Объекты мира (которые несёт игрок) ----------
        static System.Collections.Generic.Dictionary<int, Interactable> idCache;
        static float idCacheAt;
        static InteractableController objTrackIc;
        static Interactable objTrackIt;
        static int objTrackId = -1;
        static float objSendTimer, objSettleUntil, objStillSince = -1f;
        static Interactable pendingDrop;
        static int objTrackHash;
        static float pendingDropUntil;
        static System.Collections.Generic.Dictionary<int, Interactable> idMap = new System.Collections.Generic.Dictionary<int, Interactable>();
        static System.Collections.Generic.Dictionary<string, InteractablePreset> presetCache;
        // Общий (глобальный) id предмета: у созданных копий он равен id у того, кто их выбросил первым.
        // Во всех сообщениях ходит только глобальный id, поэтому оба игрока говорят об одном и том же предмете
        static System.Collections.Generic.Dictionary<int, int> gidOf = new System.Collections.Generic.Dictionary<int, int>();
        static int GidOf(Interactable it)
        {
            if (it == null) return -1;
            int g;
            if (gidOf.TryGetValue(it.id, out g)) return g;
            // предметы, созданные во время игры, нумеруются у каждого игрока по-своему и совпадают с чужими:
            // помечаем их стороной-создателем, чтобы общий номер был уникальным
            if (it.id >= RuntimeIdStart && (it.id & SideMask) == 0) return it.id | SideOf(MyPid);
            return it.id;
        }
        const int RuntimeIdStart = 10000000;
        // кто создал предмет во время игры: 3 бита (28..30) = номер игрока + 1 (хост = 1, клиенты = 2..4)
        const int SideMask = 7 << 28;
        static int SideOf(int pid) { return (pid + 1) << 28; }

        static string PresetNameOf(Interactable it)
        {
            try { return it != null && it.preset != null ? (it.preset.name ?? "") : ""; } catch (Exception) { return ""; }
        }

        static int NameHash(string s)
        {
            unchecked { int h = 17; foreach (char c in (s ?? "")) h = h * 31 + c; return h; }
        }

        // id у предметов, созданных во время игры (купленные, выброшенные), у игроков разные и могут совпасть
        // с чужим предметом: поэтому найденный по id объект принимаем, только если пресет совпадает
        static Interactable ResolveChecked(int id, string preset, int presetHash)
        {
            var it = ResolveId(id);
            if (it == null) return null;
            string pn = PresetNameOf(it);
            bool ok = preset != null ? pn == preset : NameHash(pn) == presetHash;
            if (!ok)
            {
                Plugin.L.LogWarning($"[ITEM] id={id}: у меня это '{pn}', а у отправителя '{preset ?? presetHash.ToString()}' (id не совпали) — игнорирую");
                return null;
            }
            return it;
        }

        static Interactable ResolveId(int id)
        {
            Interactable it;
            if (idMap.TryGetValue(id, out it) && it != null) return it;
            int side = id & SideMask;
            if (side != 0)
            {
                // номер чужого рантайм-предмета: у меня он может быть только в виде созданной копии (idMap)
                if (side != SideOf(MyPid)) return null;
                return FindById(id & ~SideMask);
            }
            return FindById(id);
        }

        static InteractablePreset FindPreset(string name)
        {
            if (presetCache == null)
            {
                presetCache = new System.Collections.Generic.Dictionary<string, InteractablePreset>();
                try
                {
                    foreach (var p in Resources.FindObjectsOfTypeAll<InteractablePreset>())
                        if (p != null && !string.IsNullOrEmpty(p.name) && !presetCache.ContainsKey(p.name)) presetCache[p.name] = p;
                    Plugin.Dbg($"[ITEM] пресетов предметов: {presetCache.Count}");
                }
                catch (Exception e) { Plugin.L.LogWarning($"[ITEM] пресеты: {e.Message}"); }
            }
            InteractablePreset r;
            return presetCache.TryGetValue(name ?? "", out r) ? r : null;
        }

        static void SendItemEvent(byte kind, Interactable it, bool withPose)
        {
            try
            {
                if (profileApplying || profRestoring || manager == null || manager.ConnectedPeersCount == 0 || it == null) return;
                var w = new NetDataWriter();
                w.Put(MsgItemEv);
                w.Put(kind);
                w.Put(GidOf(it));
                w.Put(PresetNameOf(it));
                sharedLocal.Add(it.id);
                if (withPose)
                {
                    Vector3 p = it.wPos; Vector3 e = it.wEuler;
                    try { if (it.controller != null) { var tr = RootOf(it.controller); p = tr.position; e = tr.eulerAngles; } } catch (Exception) { }
                    w.Put(p.x); w.Put(p.y); w.Put(p.z);
                    w.Put(e.x); w.Put(e.y); w.Put(e.z);
                    WriteState(w, it);
                }
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception ex) { Plugin.L.LogWarning($"[ITEM] отправка: {ex.Message}"); }
        }

        static System.Collections.Generic.HashSet<int> sharedLocal = new System.Collections.Generic.HashSet<int>();
        static bool applyingRemote;

        class PassedVal { public int type; public float value; public string str; }
        class ItemState { public float val, cs; public System.Collections.Generic.List<PassedVal> pv = new System.Collections.Generic.List<PassedVal>(); }

        // состояние предмета: сколько осталось выпить/съесть и прочие переменные
        static void WriteState(NetDataWriter w, Interactable it)
        {
            float v = 0f, c = 0f; int n = 0;
            try { v = it.val; c = it.cs; } catch (Exception) { }
            var list = new System.Collections.Generic.List<PassedVal>();
            try
            {
                var pv = it.pv;
                if (pv != null)
                    for (int i = 0; i < pv.Count && i < 30; i++)
                    {
                        var x = pv[i];
                        if (x == null) continue;
                        list.Add(new PassedVal { type = (int)x.varType, value = x.value, str = x.str ?? "" });
                    }
            }
            catch (Exception) { }
            n = list.Count;
            w.Put(v); w.Put(c); w.Put((byte)n);
            foreach (var x in list) { w.Put(x.type); w.Put(x.value); w.Put(x.str); }
        }

        static ItemState ReadState(NetDataReader r)
        {
            var st = new ItemState();
            st.val = r.GetFloat(); st.cs = r.GetFloat();
            int n = r.GetByte();
            for (int i = 0; i < n; i++) st.pv.Add(new PassedVal { type = r.GetInt(), value = r.GetFloat(), str = r.GetString() });
            return st;
        }

        static void ApplyState(Interactable it, ItemState st)
        {
            if (it == null || st == null) return;
            try { it.val = st.val; it.cs = st.cs; } catch (Exception) { }
            try
            {
                var pv = it.pv;
                if (pv == null)
                {
                    pv = new Il2CppSystem.Collections.Generic.List<Interactable.Passed>();
                    it.pv = pv;
                }
                foreach (var x in st.pv)
                {
                    bool found = false;
                    for (int i = 0; i < pv.Count; i++)
                    {
                        var e = pv[i];
                        if (e != null && (int)e.varType == x.type) { e.value = x.value; if (!string.IsNullOrEmpty(x.str)) e.str = x.str; found = true; break; }
                    }
                    if (!found) pv.Add(new Interactable.Passed((Interactable.PassedVarType)x.type, x.value, string.IsNullOrEmpty(x.str) ? null : x.str));
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[ITEM] состояние: {e.Message}"); }
        }

        internal static void OnItemDeleting(Interactable it)
        {
            try
            {
                if (applyingRemote || profileApplying || profRestoring || it == null || !Plugin.CfgObjSync.Value) return;
                if (!sharedLocal.Remove(it.id)) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgItemEv); w.Put((byte)4); w.Put(GidOf(it)); w.Put(PresetNameOf(it));
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.Dbg($"[ITEM] предмет id={it.id} (общий id={GidOf(it)}) удалён, сообщаю второму игроку");
            }
            catch (Exception) { }
        }

        // ---------- Тестовые читы ----------
        static bool cheatKeysBroken;
        static bool lastPause;
        static bool moneyDumped;

        internal static void CheatKeys()
        {
            if (cheatKeysBroken || !Plugin.CfgCheatKeys.Value) return;
            try
            {
                if (KeyDown(KeyCode.F6)) GiveMoney(Plugin.CfgMoneyAmount.Value);
                if (KeyDown(KeyCode.F12)) SpawnNextWeapon();
                if (KeyDown(KeyCode.F7)) SpawnItem(Plugin.CfgItemPreset.Value);
                if (KeyDown(KeyCode.F8)) { Plugin.CfgPauseNpc.Value = !Plugin.CfgPauseNpc.Value; ApplyPause(); Plugin.L.LogInfo($"[CHEAT] пауза ИИ NPC: {Plugin.CfgPauseNpc.Value}"); }
                if (KeyDown(KeyCode.F10)) GiveSocial(Plugin.CfgSocialAmount.Value);
                if (KeyDown(KeyCode.F11)) { Plugin.CfgInvisible.Value = !Plugin.CfgInvisible.Value; Plugin.L.LogInfo($"[CHEAT] NPC не видят меня: {Plugin.CfgInvisible.Value}"); }
                if (KeyDown(KeyCode.F9)) { Plugin.CfgGod.Value = !Plugin.CfgGod.Value; Plugin.L.LogInfo($"[CHEAT] бессмертие: {Plugin.CfgGod.Value}"); }
            }
            catch (Exception e)
            {
                cheatKeysBroken = true;
                Plugin.L.LogWarning($"[CHEAT] горячие клавиши недоступны ({e.GetType().Name}); используйте значения в конфиге");
            }
        }

        static System.Reflection.MethodInfo keyDownMethod;

        // UnityEngine.Input через рефлексию, чтобы не требовать ссылку на InputLegacyModule
        internal static bool KeyDown(KeyCode k)
        {
            if (keyDownMethod == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("UnityEngine.Input");
                        if (t == null) continue;
                        keyDownMethod = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                        if (keyDownMethod != null) break;
                    }
                    catch (Exception) { }
                }
                if (keyDownMethod == null) throw new InvalidOperationException("Input.GetKeyDown не найден");
            }
            return (bool)keyDownMethod.Invoke(null, new object[] { k });
        }

        // раз в секунду: команды, которые можно задать в конфиге во время игры
        internal static void CheatsSecond()
        {
            try
            {
                if (Plugin.CfgAddMoney.Value != 0)
                {
                    int a = Plugin.CfgAddMoney.Value;
                    Plugin.CfgAddMoney.Value = 0;
                    GiveMoney(a);
                    Plugin.Cfg.Save();
                }
                if (Plugin.CfgAddSocial.Value != 0)
                {
                    int a = Plugin.CfgAddSocial.Value;
                    Plugin.CfgAddSocial.Value = 0;
                    GiveSocial(a);
                    Plugin.Cfg.Save();
                }
                if (Plugin.CfgSpawnNow.Value)
                {
                    Plugin.CfgSpawnNow.Value = false;
                    SpawnItem(Plugin.CfgItemPreset.Value);
                    Plugin.Cfg.Save();
                }
                string q = Plugin.CfgSearchPreset.Value;
                if (!string.IsNullOrEmpty(q))
                {
                    Plugin.CfgSearchPreset.Value = "";
                    SearchPresets(q);
                    Plugin.Cfg.Save();
                }
                ApplyPause();
                AvatarLightFix(false);
                BreakTrace.MaybeInstall();
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] {e.Message}"); }
        }

        static void ApplyPause()
        {
            bool v = Plugin.CfgPauseNpc.Value;
            if (v == lastPause) return;
            lastPause = v;
            try { Game.Instance.SetPauseAI(v); Plugin.L.LogInfo($"[CHEAT] ИИ NPC {(v ? "остановлен" : "включён")}"); }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] SetPauseAI: {e.Message}"); }
        }

        static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                        if (t.Name == name && t.Namespace == null) return t;
                }
                catch (Exception) { }
            }
            return null;
        }

        static void GiveMoney(int amount)
        {
            try
            {
                var t = FindType("GameplayController");
                if (t == null) { Plugin.L.LogWarning("[CHEAT] класс GameplayController не найден"); return; }
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
                if (!moneyDumped)
                {
                    moneyDumped = true;
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var m in t.GetMembers(flags))
                        if (m.Name.IndexOf("oney", StringComparison.OrdinalIgnoreCase) >= 0 && !m.Name.StartsWith("NativeFieldInfoPtr") && !m.Name.StartsWith("NativeMethodInfoPtr")) names.Add(m.MemberType + " " + m.Name);
                    Plugin.L.LogInfo("[CHEAT] члены GameplayController про деньги: " + string.Join(", ", names.ToArray()));
                }
                object inst = null;
                var ip = t.GetProperty("Instance", flags);
                if (ip != null) inst = ip.GetValue(null, null);
                if (inst == null) { Plugin.L.LogWarning("[CHEAT] GameplayController.Instance пуст"); return; }
                foreach (var m in t.GetMethods(flags))
                {
                    if (m.Name != "AddMoney") continue;
                    var ps = m.GetParameters();
                    var args = new object[ps.Length];
                    bool first = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt == typeof(int)) args[i] = amount;
                        else if (pt == typeof(float)) args[i] = (float)amount;
                        else if (pt == typeof(bool)) args[i] = first ? true : false;
                        else if (pt == typeof(string)) args[i] = "cheat";
                        else args[i] = null;
                        if (pt == typeof(bool)) first = false;
                    }
                    m.Invoke(inst, args);
                    Plugin.L.LogInfo($"[CHEAT] добавлено денег: {amount} (AddMoney)");
                    return;
                }
                var mp = t.GetProperty("money", flags);
                if (mp != null && mp.PropertyType == typeof(int))
                {
                    mp.SetValue(inst, (int)mp.GetValue(inst, null) + amount, null);
                    Plugin.L.LogInfo($"[CHEAT] добавлено денег: {amount} (поле money)");
                    return;
                }
                Plugin.L.LogWarning("[CHEAT] не нашёл AddMoney / money у GameplayController, смотрите список членов в логе выше");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] деньги: {(e.InnerException != null ? e.InnerException.Message : e.Message)}"); }
        }

        static bool socialDumped;

        static void GiveSocial(int amount)
        {
            try
            {
                var t = FindType("GameplayController");
                if (t == null) { Plugin.L.LogWarning("[CHEAT] класс GameplayController не найден"); return; }
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
                if (!socialDumped)
                {
                    socialDumped = true;
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var m in t.GetMembers(flags))
                        if (m.Name.IndexOf("ocial", StringComparison.OrdinalIgnoreCase) >= 0 && !m.Name.StartsWith("NativeFieldInfoPtr") && !m.Name.StartsWith("NativeMethodInfoPtr")) names.Add(m.MemberType + " " + m.Name);
                    Plugin.L.LogInfo("[CHEAT] члены GameplayController про соцрейтинг: " + string.Join(", ", names.ToArray()));
                }
                object inst = null;
                var ip = t.GetProperty("Instance", flags);
                if (ip != null) inst = ip.GetValue(null, null);
                if (inst == null) { Plugin.L.LogWarning("[CHEAT] GameplayController.Instance пуст"); return; }
                foreach (var m in t.GetMethods(flags))
                {
                    if (!m.Name.StartsWith("Add") || m.Name.IndexOf("ocial", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ps = m.GetParameters();
                    var args = new object[ps.Length];
                    bool ok = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt == typeof(int)) args[i] = amount;
                        else if (pt == typeof(float)) args[i] = (float)amount;
                        else if (pt == typeof(bool)) args[i] = false;
                        else if (pt == typeof(string)) args[i] = "cheat";
                        else if (!pt.IsValueType) args[i] = null;
                        else ok = false;
                    }
                    if (!ok) continue;
                    m.Invoke(inst, args);
                    Plugin.L.LogInfo($"[CHEAT] соцрейтинг +{amount} ({m.Name})");
                    return;
                }
                foreach (var pr in t.GetProperties(flags))
                {
                    if (pr.Name.IndexOf("ocialcredit", StringComparison.OrdinalIgnoreCase) < 0 || !pr.CanRead || !pr.CanWrite) continue;
                    if (pr.PropertyType == typeof(int)) { pr.SetValue(inst, (int)pr.GetValue(inst, null) + amount, null); Plugin.L.LogInfo($"[CHEAT] соцрейтинг +{amount} (поле {pr.Name})"); return; }
                    if (pr.PropertyType == typeof(float)) { pr.SetValue(inst, (float)pr.GetValue(inst, null) + amount, null); Plugin.L.LogInfo($"[CHEAT] соцрейтинг +{amount} (поле {pr.Name})"); return; }
                }
                Plugin.L.LogWarning("[CHEAT] не нашёл метод/поле соцрейтинга, смотрите список членов в логе выше");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] соцрейтинг: {(e.InnerException != null ? e.InnerException.Message : e.Message)}"); }
        }

        static void SpawnItem(string preset)
        {
            try
            {
                var pr = FindPreset(preset);
                if (pr == null) { Plugin.L.LogWarning($"[CHEAT] пресет '{preset}' не найден (используйте Cheats.SearchPreset)"); return; }
                var cam = Camera.main;
                Vector3 pos = cam != null ? cam.transform.position + cam.transform.forward * 1.0f : Player.Instance.transform.position + Vector3.up;
                var it = InteractableCreator.Instance.CreateWorldInteractable(pr, null, null, null, pos, Vector3.zero, null, null, "");
                Plugin.L.LogInfo(it != null ? $"[CHEAT] создан предмет '{preset}' (id={it.id})" : $"[CHEAT] не удалось создать '{preset}'");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] предмет: {e.Message}"); }
        }

        static System.Collections.Generic.List<InteractablePreset> weaponPresets;
        static int weaponIdx;

        // F12: создаёт перед игроком следующее оружие из игры (подобрать как обычный предмет)
        static void SpawnNextWeapon()
        {
            try
            {
                if (weaponPresets == null)
                {
                    weaponPresets = new System.Collections.Generic.List<InteractablePreset>();
                    foreach (var p in Resources.FindObjectsOfTypeAll<InteractablePreset>())
                        if (p != null && p.weapon != null && !string.IsNullOrEmpty(p.name)) weaponPresets.Add(p);
                    weaponPresets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var p in weaponPresets) names.Add(p.name + "(" + p.weapon.type + ")");
                    Plugin.L.LogInfo($"[CHEAT] оружие в игре ({weaponPresets.Count}): " + string.Join(", ", names.ToArray()));
                }
                if (weaponPresets.Count == 0) { Plugin.L.LogWarning("[CHEAT] оружейных пресетов не найдено"); return; }
                var w = weaponPresets[weaponIdx % weaponPresets.Count];
                weaponIdx++;
                var cam = Camera.main;
                Vector3 pos = cam != null ? cam.transform.position + cam.transform.forward * 1.0f : Player.Instance.transform.position + Vector3.up;
                var it = InteractableCreator.Instance.CreateWorldInteractable(w, null, null, null, pos, Vector3.zero, null, null, "");
                Plugin.L.LogInfo($"[CHEAT] оружие '{w.name}' ({w.weapon.type}) создано перед вами: {(it != null ? "ok" : "ошибка")}");
                Net.Toast("Weapon spawned: " + w.name + " - pick it up (F12 = next)");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] оружие: {e.Message}"); }
        }

        static void SearchPresets(string q)
        {
            FindPreset("");
            var found = new System.Collections.Generic.List<string>();
            if (presetCache != null)
                foreach (var k in presetCache.Keys)
                    if (k.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) found.Add(k);
            found.Sort();
            Plugin.L.LogInfo($"[CHEAT] пресеты по '{q}' ({found.Count}): " + string.Join(", ", found.ToArray()));
        }

        // ---------- Двери ----------
        static bool applyingDoor;
        static bool doorActorFallback;
        static System.Collections.Generic.Dictionary<IntPtr, int> doorKeys = new System.Collections.Generic.Dictionary<IntPtr, int>();
        static float doorKeysAt;

        static bool IsPlayerActor(Actor a)
        {
            try { return a != null && a.isPlayer; } catch (Exception) { return false; }
        }

        static Actor DoorActor()
        {
            if (!doorActorFallback) return null;
            try { return Player.Instance; } catch (Exception) { return null; }
        }

        static int DoorKey(NewDoor d)
        {
            int k;
            if (doorKeys.TryGetValue(d.Pointer, out k)) return k;
            if (Time.realtimeSinceStartup < doorKeysAt) return -1;
            doorKeysAt = Time.realtimeSinceStartup + 2f;
            try
            {
                doorKeys.Clear();
                foreach (var kv in CityData.Instance.doorDictionary)
                    if (kv.Value != null) doorKeys[kv.Value.Pointer] = kv.Key;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] индекс дверей: {e.Message}"); }
            return doorKeys.TryGetValue(d.Pointer, out k) ? k : -1;
        }

        static NewDoor DoorByKey(int key)
        {
            try
            {
                NewDoor d;
                var dict = CityData.Instance.doorDictionary;
                if (dict != null && dict.TryGetValue(key, out d)) return d;
            }
            catch (Exception) { }
            return null;
        }

        static void SendDoor(NetDataWriter w)
        {
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        internal static void OnDoorOpen(NewDoor d, float ajar, Actor actor, bool skip)
        {
            try
            {
                if (applyingDoor || !Plugin.CfgDoorSync.Value || d == null || manager == null || manager.ConnectedPeersCount == 0) return;
                // двери, которые открывают горожане, шлёт только хост (у клиента ИИ горожан остановлен)
                if (!IsPlayerActor(actor) && !(IsHost && actor != null && Plugin.CfgNpcSync.Value)) return;
                int key = DoorKey(d);
                if (key < 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)1); w.Put(key); w.Put(ajar); w.Put(skip);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] я открыл/закрыл дверь {key}: ajar={ajar:0.00}");
            }
            catch (Exception) { }
        }

        internal static void OnDoorFlag(byte kind, NewDoor d, bool val, Actor actor)
        {
            try
            {
                if (applyingDoor || !Plugin.CfgDoorSync.Value || d == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (kind == 2 && !IsPlayerActor(actor)) return; // замки, которые меняет игра/NPC, не синхронизируем
                int key = DoorKey(d);
                if (key < 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put(kind); w.Put(key); w.Put(val);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] {(kind == 2 ? "замок" : "заклинивание")} двери {key}: {val}");
            }
            catch (Exception) { }
        }

        static readonly System.Collections.Generic.Dictionary<long, bool> moveState = new System.Collections.Generic.Dictionary<long, bool>();

        internal static void OnLockState(Interactable it, bool val, Actor interactor)
        {
            try
            {
                if (applyingDoor || it == null || !Plugin.CfgDoorSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
                if (!IsPlayerActor(interactor) || it.id == 0) return;
                var w = new NetDataWriter();
                Vector3 wp = it.wPos;
                w.Put(MsgDoor); w.Put((byte)6); w.Put(GidOf(it)); w.Put(NameHash(PresetNameOf(it))); w.Put(val);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] я {(val ? "запер" : "отпер")} объект id={it.id} '{PresetNameOf(it)}'");
            }
            catch (Exception) { }
        }

        static readonly System.Collections.Generic.HashSet<string> seenSwitchNames = new System.Collections.Generic.HashSet<string>();

        static bool SwitchWorthy(Interactable it, string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("light") || n.Contains("lamp") || n.Contains("switch") || n.Contains("lantern")) return true;
            try { if (it.preset != null && it.preset.isLight != null) return true; } catch (Exception) { }
            return false;
        }

        // Выключатели света и лампы: включил / выключил -> то же у второго игрока
        static float pressUntil;
        static float rateWindowStart; static int rateCount;
        static readonly System.Collections.Generic.Dictionary<long, bool> lastSwitchSent = new System.Collections.Generic.Dictionary<long, bool>();

        internal static void MarkPlayerPress(Actor who)
        {
            try { if (IsPlayerActor(who)) pressUntil = Time.realtimeSinceStartup + 1.5f; } catch (Exception) { }
        }

        static readonly System.Collections.Generic.Dictionary<int, bool> lastRoomSent = new System.Collections.Generic.Dictionary<int, bool>();
        static System.Collections.Generic.List<int> roomSnapIds; static System.Collections.Generic.List<bool> roomSnapVals; static int roomSnapPos;

        internal static void OnRoomLights(NewRoom room, bool val, Actor actor)
        {
            try
            {
                if (applyingDoor || room == null || !Plugin.CfgDoorSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
                float now = Time.realtimeSinceStartup;
                if (!IsPlayerActor(actor) && now > pressUntil) return;
                int rid = room.roomID;
                if (lastRoomSent.TryGetValue(rid, out var lv) && lv == val) return;
                lastRoomSent[rid] = val;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)8); w.Put(rid); w.Put(val);
                SendDoor(w);
                Plugin.Dbg($"[LIGHT] я {(val ? "включил" : "выключил")} свет в комнате {rid}");
                AvatarLightFix(true);
            }
            catch (Exception) { }
        }

        static bool avatarMaskLogged;

        internal static void AvatarLightFix(bool log)
        {
            try
            {
                if (!Plugin.CfgAvatarLightFix.Value) return;
                SyncCur();
                for (int i = 0; i < players.Count; i++) FixAvatarLight(players[i] == curRp ? avatar : players[i].avatar, log);
            }
            catch (Exception) { }
        }

        static void FixAvatarLight(GameObject av, bool log)
        {
            try
            {
                if (av == null) return;
                var rs = av.GetComponentsInChildren<Renderer>(true);
                uint first = 0; bool got = false; int changed = 0;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    uint m = r.renderingLayerMask;
                    if (!got) { first = m; got = true; }
                    if (m != 0xFFFFFFFFu) { r.renderingLayerMask = 0xFFFFFFFFu; changed++; }
                }
                if ((log || !avatarMaskLogged) && got)
                {
                    avatarMaskLogged = true;
                    Plugin.Dbg($"[LIGHT] маска света аватара: было {first:X}, исправлено рендереров {changed} из {rs.Length}");
                }
            }
            catch (Exception) { }
        }

        static NewRoom RoomById(int rid)
        {
            try
            {
                var dict = CityData.Instance.roomDictionary;
                NewRoom r;
                if (dict != null && dict.TryGetValue(rid, out r)) return r;
            }
            catch (Exception) { }
            return null;
        }

        static bool ApplyRoomLights(int rid, bool val, bool log)
        {
            var r = RoomById(rid);
            if (r == null) { if (log) Plugin.L.LogWarning($"[LIGHT] комната {rid} не найдена"); return false; }
            lastRoomSent[rid] = val;
            bool before = r.mainLightStatus;
            applyingDoor = true;
            try
            {
                if (before != val) r.SetMainLights(val, "coop", null, false, false);
                // кнопки выключателей: подтягиваем в то же состояние
                try
                {
                    var sw = r.lightswitchInteractables;
                    if (sw != null)
                        for (int i = 0; i < sw.Count; i++)
                        {
                            var x = sw[i];
                            if (x != null && x.sw0 != val) x.SetSwitchState(val, DoorActor(), false, false, false);
                        }
                }
                catch (Exception e) { if (log) Plugin.L.LogWarning($"[LIGHT] выключатели: {e.Message}"); }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[LIGHT] SetMainLights: {e.Message}"); }
            finally { applyingDoor = false; }
            if (log) Plugin.Dbg($"[LIGHT] второй игрок {(val ? "включил" : "выключил")} свет в комнате {rid}: {before} -> {r.mainLightStatus}");
            AvatarLightFix(log);
            return true;
        }

        static void SendRoomSnapshot(NetPeer peer)
        {
            try
            {
                var dict = CityData.Instance.roomDictionary;
                if (dict == null) return;
                var ids = new System.Collections.Generic.List<int>();
                var vals = new System.Collections.Generic.List<bool>();
                foreach (var kv in dict)
                {
                    var r = kv.Value;
                    if (r == null) continue;
                    ids.Add(kv.Key); vals.Add(r.mainLightStatus);
                }
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)9); w.Put(ids.Count);
                for (int i = 0; i < ids.Count; i++) { w.Put(ids[i]); w.Put(vals[i]); }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.Dbg($"[LIGHT] отправил снимок света комнат: {ids.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[LIGHT] снимок: {e.Message}"); }
        }

        static void RoomSnapTick()
        {
            if (roomSnapIds == null) return;
            int budget = 15;
            while (budget > 0 && roomSnapPos < roomSnapIds.Count)
            {
                int i = roomSnapPos++;
                try
                {
                    var r = RoomById(roomSnapIds[i]);
                    if (r != null && r.mainLightStatus != roomSnapVals[i]) { ApplyRoomLights(roomSnapIds[i], roomSnapVals[i], false); budget--; }
                    else lastRoomSent[roomSnapIds[i]] = roomSnapVals[i];
                }
                catch (Exception) { }
            }
            if (roomSnapPos >= roomSnapIds.Count)
            {
                Plugin.Dbg($"[LIGHT] снимок света комнат применён: {roomSnapIds.Count}");
                roomSnapIds = null; roomSnapVals = null;
            }
        }

        static bool SwitchExcluded(string name)
        {
            return name.StartsWith("Door") || name.StartsWith("Drawer") || name.StartsWith("Telephone") || name == "";
        }

        internal static void OnSwitchState(Interactable it, int idx, bool val, Actor interactor)
        {
            try
            {
                if (applyingDoor || it == null || !Plugin.CfgDoorSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
                string name = PresetNameOf(it);
                // мебель, поставленная игроком (лампы, телевизоры): её переключатели синхронизируем всегда
                bool placed = false;
                try { var fpar = it.furnitureParent; placed = fpar != null && fpar.userPlaced; } catch (Exception) { }
                bool isMain = name.StartsWith("Lightswitch");
                try { if (it.preset != null && it.preset.isMainLight) isMain = true; } catch (Exception) { }
                if (isMain && !placed)
                {
                    bool hasFurn = false;
                    try { hasFurn = it.furnitureParent != null; } catch (Exception) { }
                    if (hasFurn && seenSwitchNames.Add("main:" + name)) Plugin.Dbg($"[SWITCH] пропущен основной свет '{name}' sw{idx} id={it.id} (мебель есть, userPlaced=false)");
                    return;
                }
                bool player = IsPlayerActor(interactor);
                bool noActor = interactor == null;
                if (!player && !noActor) return;
                float now = Time.realtimeSinceStartup;
                // свет в комнате переключается без актёра: считаем это переключением, только если игрок только что нажимал
                if (!player && now > pressUntil) return;
                if (now - rateWindowStart > 1f) { rateWindowStart = now; rateCount = 0; }
                if (++rateCount > 30) return;
                long sk = ((long)it.Pointer << 2) | (long)idx;
                if (lastSwitchSent.TryGetValue(sk, out var lastV) && lastV == val) return;
                lastSwitchSent[sk] = val;
                bool worthy = placed || (player ? (SwitchWorthy(it, name) || it.id < 0) : SwitchWorthy(it, name));
                if (SwitchExcluded(name) || !worthy)
                {
                    string k = name + "#" + idx;
                    if (seenSwitchNames.Add(k)) Plugin.Dbg($"[SWITCH] не синхронизирую '{name}' sw{idx} id={it.id}");
                    return;
                }
                var w = new NetDataWriter();
                Vector3 wp = it.wPos;
                w.Put(MsgDoor); w.Put((byte)7); w.Put(it.id); w.Put(NameHash(name)); w.Put(val);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z); w.Put((byte)idx);
                SendDoor(w);
                Plugin.Dbg($"[SWITCH] я {(val ? "включил" : "выключил")} '{name}' sw{idx} id={it.id} (актёр: {(player ? "игрок" : "нет")}{(placed ? ", моя мебель" : "")})");
            }
            catch (Exception) { }
        }

        static Interactable FindInteractableAt(int hash, Vector3 wp, out float dist)
        {
            Interactable best = null; float bd = 0.15f;
            try
            {
                var list = CityData.Instance.interactableDirectory;
                for (int i = 0; i < list.Count; i++)
                {
                    var x = list[i];
                    if (x == null) continue;
                    float d = Vector3.Distance(x.wPos, wp);
                    if (d >= bd) continue;
                    if (NameHash(PresetNameOf(x)) != hash) continue;
                    bd = d; best = x;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[SWITCH] поиск: {e.Message}"); }
            dist = bd;
            return best;
        }

        static bool SwGet(Interactable it, int idx)
        {
            return idx == 0 ? it.sw0 : idx == 1 ? it.sw1 : idx == 2 ? it.sw2 : it.sw3;
        }

        static void ApplySwitch(int id, int hash, Vector3 wp, bool val, int idx)
        {
            float dist;
            var it = FindInteractableAt(hash, wp, out dist);
            if (it == null) { Plugin.L.LogWarning($"[SWITCH] объект id={id} в позиции {wp} не найден"); return; }
            bool before = SwGet(it, idx);
            lastSwitchSent[((long)it.Pointer << 2) | (long)idx] = val;
            applyingDoor = true;
            try
            {
                if (before != val)
                {
                    Actor act = DoorActor();
                    if (idx == 0) it.SetSwitchState(val, act, false, false, false);
                    else if (idx == 1) it.SetCustomState1(val, act, false, false, false);
                    else if (idx == 2) it.SetCustomState2(val, act, false, false, false);
                    else it.SetCustomState3(val, act, false, false, false);
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[SWITCH] SetState: {e.Message}"); }
            finally { applyingDoor = false; }
            Plugin.Dbg($"[SWITCH] второй игрок {(val ? "включил" : "выключил")} '{PresetNameOf(it)}' sw{idx} ({dist:F3} м), {before} -> {SwGet(it, idx)}");
        }

        static DoorMovementController FindMoveCtl(int hash, Vector3 wp, out float dist)
        {
            DoorMovementController c = null;
            float best = 0.1f;
            try
            {
                foreach (var cand in UnityEngine.Object.FindObjectsOfType<DoorMovementController>())
                {
                    if (cand == null) continue;
                    Interactable ci = null;
                    try { ci = cand.interactable; } catch (Exception) { }
                    if (ci == null || NameHash(PresetNameOf(ci)) != hash) continue;
                    float d = Vector3.Distance(ci.wPos, wp);
                    if (d < best) { best = d; c = cand; }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] поиск: {e.Message}"); }
            dist = best;
            return c;
        }

        internal static void OnMoveDoor(DoorMovementController c, bool open, Actor interactor)
        {
            try
            {
                if (applyingDoor || !Plugin.CfgDoorSync.Value || c == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (!IsPlayerActor(interactor)) return;
                Interactable it = null;
                try { it = c.interactable; } catch (Exception) { }
                if (it == null) return;
                long mk = (long)c.Pointer;
                if (moveState.TryGetValue(mk, out var known) && known == open) return;
                moveState[mk] = open;
                var w = new NetDataWriter();
                Vector3 wp = it.wPos;
                w.Put(MsgDoor); w.Put((byte)4); w.Put(GidOf(it)); w.Put(NameHash(PresetNameOf(it))); w.Put(open);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                SendDoor(w);
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] я {(open ? "открыл" : "закрыл")} объект id={it.id} '{PresetNameOf(it)}'");
            }
            catch (Exception) { }
        }

        static void ApplyDoorOpen(NewDoor d, float ajar, bool skip)
        {
            applyingDoor = true;
            try
            {
                try { d.SetOpen(ajar, DoorActor(), skip, 1f); }
                catch (Exception)
                {
                    if (doorActorFallback) throw;
                    doorActorFallback = true;
                    Plugin.L.LogWarning("[DOOR] SetOpen с пустым актёром не прошёл, использую локального игрока как актёра");
                    d.SetOpen(ajar, DoorActor(), skip, 1f);
                }
            }
            finally { applyingDoor = false; }
        }

        static void ApplyDoorLock(NewDoor d, bool val)
        {
            applyingDoor = true;
            try
            {
                try { d.SetLocked(val, DoorActor(), true); }
                catch (Exception)
                {
                    if (doorActorFallback) throw;
                    doorActorFallback = true;
                    d.SetLocked(val, DoorActor(), true);
                }
            }
            finally { applyingDoor = false; }
        }

        static void OnDoorMessage(NetDataReader r)
        {
            byte kind = r.GetByte();
            try
            {
                if (!Plugin.CfgDoorSync.Value) { if (kind == 5) { /* всё равно прочитаем ниже */ } else return; }
                if (kind == 1)
                {
                    int key = r.GetInt(); float ajar = r.GetFloat(); bool skip = r.GetBool();
                    var d = DoorByKey(key);
                    if (d == null) { Plugin.L.LogWarning($"[DOOR] дверь {key} не найдена"); return; }
                    ApplyDoorOpen(d, ajar, skip);
                    if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] второй игрок открыл/закрыл дверь {key}: ajar={ajar:0.00}");
                }
                else if (kind == 2 || kind == 3)
                {
                    int key = r.GetInt(); bool val = r.GetBool();
                    var d = DoorByKey(key);
                    if (d == null) { Plugin.L.LogWarning($"[DOOR] дверь {key} не найдена"); return; }
                    if (kind == 2) ApplyDoorLock(d, val);
                    else
                    {
                        applyingDoor = true;
                        try { d.SetJammed(val, null, val); } finally { applyingDoor = false; }
                    }
                    if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] второй игрок: {(kind == 2 ? "замок" : "заклинивание")} двери {key} = {val}");
                }
                else if (kind == 4)
                {
                    int id = r.GetInt(); int hash = r.GetInt(); bool open = r.GetBool();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    // у ящиков и шкафов номера у игроков разные (отрицательные), поэтому ищем по названию и позиции в мире
                    ApplyMove(id, hash, wp, open, false);
                }
                else if (kind == 14)
                {
                    int sid = r.GetInt();
                    var srems = GetRems(r);
                    ApplyApartmentSold(sid, true, srems);
                    if (IsHost && doorSender != null) ProfileNoteApt(doorSender, sid, srems);
                }
                else if (kind == 16)
                {
                    ApplyDecorRemote(r);
                }
                else if (kind == 15)
                {
                    int cnt = r.GetInt();
                    for (int i = 0; i < cnt; i++) { int sid = r.GetInt(); ApplyApartmentSold(sid, false, GetRems(r)); }
                    Plugin.L.LogInfo($"[APT] получен список проданных квартир: {cnt}");
                }
                else if (kind == 13)
                {
                    float gt = r.GetFloat(); int leap = r.GetInt(); int spd = r.GetByte();
                    float rain = r.GetFloat(), wind = r.GetFloat(), snow = r.GetFloat(), lit = r.GetFloat(), fog = r.GetFloat();
                    ApplyTime(gt, leap, spd, rain, wind, snow, lit, fog);
                }
                else if (kind == 11)
                {
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 cp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 vv = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    ApplyWindowBreak(wp, cp, vv, true);
                }
                else if (kind == 12)
                {
                    int cnt = r.GetInt();
                    for (int i = 0; i < cnt; i++)
                    {
                        Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                        ApplyWindowBreak(wp, wp, Vector3.zero, false);
                    }
                    Plugin.L.LogInfo($"[WINDOW] получен снимок разбитых окон: {cnt}");
                }
                else if (kind == 10)
                {
                    int type = r.GetByte(); int hash = r.GetInt();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 cp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    Vector3 nn = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    float mag = r.GetFloat();
                    ApplyWorldBreak(type, hash, wp, cp, nn, mag);
                }
                else if (kind == 8)
                {
                    int rid = r.GetInt(); bool val = r.GetBool();
                    ApplyRoomLights(rid, val, true);
                }
                else if (kind == 9)
                {
                    int n = r.GetInt();
                    var ids = new System.Collections.Generic.List<int>(); var vals = new System.Collections.Generic.List<bool>();
                    for (int i = 0; i < n; i++) { ids.Add(r.GetInt()); vals.Add(r.GetBool()); }
                    if (!IsHost && Plugin.CfgDoorSnap.Value && Plugin.CfgDoorSync.Value)
                    {
                        roomSnapIds = ids; roomSnapVals = vals; roomSnapPos = 0;
                        Plugin.Dbg($"[LIGHT] получен снимок света комнат: {n}");
                    }
                }
                else if (kind == 7)
                {
                    int id = r.GetInt(); int hash = r.GetInt(); bool val = r.GetBool();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    int sidx = r.GetByte();
                    ApplySwitch(id, hash, wp, val, sidx);
                }
                else if (kind == 6)
                {
                    int id = r.GetInt(); int hash = r.GetInt(); bool val = r.GetBool();
                    Vector3 wp = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                    ApplyMove(id, hash, wp, val, true);
                }
                else if (kind >= 17 && kind <= 19) OnFurnMessage(kind, r);
                else if (kind == 5)
                {
                    int n = r.GetInt();
                    var list = new System.Collections.Generic.List<int[]>();
                    var ajars = new System.Collections.Generic.List<float>();
                    for (int i = 0; i < n; i++)
                    {
                        int key = r.GetInt(); float ajar = r.GetFloat(); byte fl = r.GetByte();
                        list.Add(new[] { key, fl }); ajars.Add(ajar);
                    }
                    if (!IsHost && Plugin.CfgDoorSnap.Value && Plugin.CfgDoorSync.Value)
                    {
                        doorSnap = list; doorSnapAjar = ajars; doorSnapPos = 0;
                        Plugin.L.LogInfo($"[DOOR] получен снимок дверей хоста: {n}");
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] приём kind={kind}: {e.Message}"); }
        }

        static System.Collections.Generic.List<int[]> doorSnap;
        static System.Collections.Generic.List<float> doorSnapAjar;
        static int doorSnapPos;

        static void SendDoorSnapshot(NetPeer peer)
        {
            try
            {
                if (!Plugin.CfgDoorSnap.Value || !Plugin.CfgDoorSync.Value) return;
                var dict = CityData.Instance.doorDictionary;
                if (dict == null) return;
                var keys = new System.Collections.Generic.List<int>();
                var ajars = new System.Collections.Generic.List<float>();
                var flags = new System.Collections.Generic.List<byte>();
                foreach (var kv in dict)
                {
                    var d = kv.Value;
                    if (d == null) continue;
                    byte fl = 0;
                    try { if (d.isLocked) fl |= 1; if (d.isJammed) fl |= 2; } catch (Exception) { }
                    keys.Add(kv.Key); ajars.Add(d.ajar); flags.Add(fl);
                }
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)5); w.Put(keys.Count);
                for (int i = 0; i < keys.Count; i++) { w.Put(keys[i]); w.Put(ajars[i]); w.Put(flags[i]); }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[DOOR] отправил снимок дверей: {keys.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DOOR] снимок: {e.Message}"); }
        }

        class PendingMove
        {
            public int id, hash; public Vector3 wp; public bool isLock, val; public float due; public DoorMovementController c;
        }
        static readonly System.Collections.Generic.List<PendingMove> pendingMoves = new System.Collections.Generic.List<PendingMove>();

        static bool QueueMove(PendingMove m)
        {
            for (int i = 0; i < pendingMoves.Count; i++)
            {
                var o = pendingMoves[i];
                if (o.c == null && m.c == null && o.isLock == m.isLock && o.hash == m.hash && Vector3.Distance(o.wp, m.wp) < 0.1f) { pendingMoves[i] = m; return false; }
            }
            if (pendingMoves.Count > 300) pendingMoves.RemoveAt(0);
            pendingMoves.Add(m);
            return true;
        }

        // Применить состояние ящика/шкафа (открыт или заперт). Если объекта рядом нет, запоминаем и применяем позже
        static bool ApplyMove(int id, int hash, Vector3 wp, bool val, bool isLock)
        {
            float dist;
            var c = FindMoveCtl(hash, wp, out dist);
            if (c == null)
            {
                bool isNew = QueueMove(new PendingMove { id = id, hash = hash, wp = wp, isLock = isLock, val = val, due = Time.realtimeSinceStartup + 1f });
                if (isNew) Plugin.L.LogInfo($"[DOOR] объект id={id} далеко/не заспавнен, применю позже ({(isLock ? "замок" : "открыт")}={val})");
                return false;
            }
            Interactable ci = null;
            try { ci = c.interactable; } catch (Exception) { }
            if (isLock)
            {
                if (ci == null) return false;
                applyingDoor = true;
                try { ci.SetLockedState(val, DoorActor(), false, true); }
                catch (Exception e) { Plugin.L.LogWarning($"[DOOR] SetLockedState: {e.Message}"); }
                finally { applyingDoor = false; }
                if (Plugin.CfgDebugLog.Value) Plugin.L.LogInfo($"[DOOR] второй игрок {(val ? "запер" : "отпер")} объект id={id} ({dist:F3} м)");
                return true;
            }
            bool before = c.isOpen;
            moveState[(long)c.Pointer] = val;
            string how = "уже в нужном состоянии";
            if (before != val)
            {
                applyingDoor = true;
                try
                {
                    how = "SetSwitchState";
                    if (ci != null) ci.SetSwitchState(val, DoorActor(), false, false, false);
                    else { how = "SetOpen"; c.SetOpen(val ? 1f : 0f, DoorActor(), false); }
                }
                catch (Exception e) { Plugin.L.LogWarning($"[DOOR] открытие: {e.Message}"); }
                finally { applyingDoor = false; }
                // проверим после анимации и только тогда доделаем, чтобы не запускать две анимации сразу
                QueueMove(new PendingMove { id = id, hash = hash, wp = wp, isLock = false, val = val, due = Time.realtimeSinceStartup + 2f, c = c });
            }
            Plugin.L.LogInfo($"[DOOR] ящик id={id}: {dist:F3} м, isOpen {before} -> {c.isOpen} (нужно {val}), способ: {how}");
            return true;
        }

        static void MoveTick()
        {
            if (pendingMoves.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = pendingMoves.Count - 1; i >= 0; i--)
            {
                var m = pendingMoves[i];
                if (now < m.due) continue;
                pendingMoves.RemoveAt(i);
                try
                {
                    if (m.c != null)
                    {
                        // проверка после анимации
                        if (m.c.isOpen != m.val)
                        {
                            applyingDoor = true;
                            try { if (m.val) m.c.OnOpen(DoorActor(), false); else m.c.OnClose(DoorActor(), false); }
                            finally { applyingDoor = false; }
                            Plugin.L.LogInfo($"[DOOR] ящик id={m.id}: доделал через OnOpen/OnClose, isOpen={m.c.isOpen}");
                        }
                    }
                    else ApplyMove(m.id, m.hash, m.wp, m.val, m.isLock);
                }
                catch (Exception) { }
            }
        }

        // Применяем снимок хоста порциями, чтобы не вешать кадр
        static void DoorTick()
        {
            MoveTick();
            RoomSnapTick();
            WindowTick();
            TimeTick();
            if (Time.unscaledTime - lastAptTick > 2f) { lastAptTick = Time.unscaledTime; AptTick(); }
            if (doorSnap == null) return;
            int budget = 60;
            while (budget-- > 0 && doorSnapPos < doorSnap.Count)
            {
                int i = doorSnapPos++;
                try
                {
                    int key = doorSnap[i][0]; byte fl = (byte)doorSnap[i][1]; float ajar = doorSnapAjar[i];
                    var d = DoorByKey(key);
                    if (d == null) continue;
                    if (Mathf.Abs(d.ajar - ajar) > 0.01f) ApplyDoorOpen(d, ajar, true);
                    bool lk = (fl & 1) != 0;
                    if (d.isLocked != lk) ApplyDoorLock(d, lk);
                    bool jm = (fl & 2) != 0;
                    if (d.isJammed != jm) { applyingDoor = true; try { d.SetJammed(jm, null, false); } finally { applyingDoor = false; } }
                }
                catch (Exception e) { if (doorSnapPos < 5) Plugin.L.LogWarning($"[DOOR] снимок дверь #{i}: {e.Message}"); }
            }
            if (doorSnapPos >= doorSnap.Count)
            {
                Plugin.L.LogInfo($"[DOOR] снимок дверей применён: {doorSnap.Count}");
                doorSnap = null; doorSnapAjar = null;
            }
        }

        static readonly System.Collections.Generic.HashSet<int> soldAddrs = new System.Collections.Generic.HashSet<int>();
        static readonly System.Collections.Generic.HashSet<int> soldPending = new System.Collections.Generic.HashSet<int>();

        static int AddrIdOf(ResidenceController rc)
        {
            try { var a = rc != null ? rc.address : null; return a != null ? a.id : -1; } catch (Exception) { return -1; }
        }

        internal static bool IsApartmentSoldElsewhere(ResidenceController rc)
        {
            if (applyingRemote) return false;
            int id = AddrIdOf(rc);
            if (id >= 0 && soldAddrs.Contains(id))
            {
                Plugin.L.LogWarning($"[APT] квартира (адрес {id}) уже куплена вторым игроком, моя покупка блокируется");
                return true;
            }
            return false;
        }

        static bool buying;
        static readonly System.Collections.Generic.List<int[]> buyRemovals = new System.Collections.Generic.List<int[]>();
        static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int[]>> soldRemovals = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int[]>>();
        static readonly System.Collections.Generic.HashSet<int> removalsDone = new System.Collections.Generic.HashSet<int>();

        internal static void BeginBuy() { buying = true; buyRemovals.Clear(); }

        internal static void OnRoomCleared(NewRoom room, bool skipFlag, int opt)
        {
            try
            {
                if (!buying || applyingRemote || room == null) return;
                buyRemovals.Add(new[] { room.roomID, skipFlag ? 1 : 0, opt });
            }
            catch (Exception) { }
        }

        static void PutSale(NetDataWriter w, int id, System.Collections.Generic.List<int[]> rems)
        {
            w.Put(id);
            int n = rems != null ? rems.Count : 0;
            w.Put(n);
            for (int i = 0; i < n; i++) { w.Put(rems[i][0]); w.Put(rems[i][1] != 0); w.Put((byte)rems[i][2]); }
        }

        static System.Collections.Generic.List<int[]> GetRems(NetDataReader r)
        {
            int n = r.GetInt();
            var l = new System.Collections.Generic.List<int[]>();
            for (int i = 0; i < n; i++) { int rid = r.GetInt(); bool b = r.GetBool(); int o = r.GetByte(); l.Add(new[] { rid, b ? 1 : 0, o }); }
            return l;
        }

        internal static void OnApartmentBought(ResidenceController rc)
        {
            try
            {
                buying = false;
                if (applyingRemote) return;
                int id = AddrIdOf(rc);
                if (id < 0) return;
                if (!soldAddrs.Add(id)) return;
                var rems = new System.Collections.Generic.List<int[]>(buyRemovals);
                buyRemovals.Clear();
                soldRemovals[id] = rems; removalsDone.Add(id);
                Plugin.L.LogInfo($"[APT] я купил квартиру (адрес {id}): {rc.GetResidenceString()}, очисток комнат: {rems.Count}");
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)14); PutSale(w, id, rems);
                SendDoor(w);
            }
            catch (Exception e) { Plugin.L.LogWarning($"[APT] {e.Message}"); }
        }

        static void ApplyRemovals(int id, System.Collections.Generic.List<int[]> rems)
        {
            if (rems == null || rems.Count == 0 || !removalsDone.Add(id)) return;
            soldRemovals[id] = rems;
            applyingRemote = true; applyingDoor = true;
            try
            {
                foreach (var e in rems)
                {
                    var room = RoomById(e[0]);
                    if (room == null) { Plugin.L.LogWarning($"[APT] комната {e[0]} не найдена"); continue; }
                    int opt = e[2] == 2 ? 1 : e[2]; // «на склад» у второго игрока не нужно: просто убираем
                    try
                    {
                        room.RemoveAllInhabitantFurniture(e[1] != 0, (FurnitureClusterLocation.RemoveInteractablesOption)opt);
                        Plugin.L.LogInfo($"[APT] очистил комнату {e[0]} (флаг={e[1]}, опция={opt})");
                    }
                    catch (Exception ex) { Plugin.L.LogWarning($"[APT] очистка комнаты {e[0]}: {ex.Message}"); }
                }
            }
            finally { applyingRemote = false; applyingDoor = false; }
        }

        // Отделка: комната, тип, пресет материала и ключ (материал + цвета)
        static System.Collections.Generic.Dictionary<string, Material> matByName;

        static Material FindMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (matByName == null)
            {
                matByName = new System.Collections.Generic.Dictionary<string, Material>();
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && !matByName.ContainsKey(m.name)) matByName[m.name] = m;
            }
            Material res; matByName.TryGetValue(name, out res);
            return res;
        }

        static MaterialGroupPreset FindMatPreset(string name)
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<MaterialGroupPreset>())
                if (m != null && m.name == name) return m;
            return null;
        }

        static void PutColor(NetDataWriter w, Color c) { w.Put(c.r); w.Put(c.g); w.Put(c.b); w.Put(c.a); }
        static Color GetColor(NetDataReader r) { return new Color(r.GetFloat(), r.GetFloat(), r.GetFloat(), r.GetFloat()); }

        internal static void OnDecorApplied(PlayerApartmentController pac, MaterialGroupPreset.MaterialType type, MaterialGroupPreset mat, Toolbox.MaterialKey key, bool save)
        {
            try
            {
                if (!save || applyingRemote || mat == null || manager == null || manager.ConnectedPeersCount == 0) return;
                NewRoom room = pac.decoratingRoom;
                if (room == null) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)16);
                w.Put(room.roomID); w.Put((byte)(int)type); w.Put(mat.name);
                bool hk = key != null;
                w.Put(hk);
                if (hk)
                {
                    w.Put(key.baseMaterial != null ? key.baseMaterial.name : "");
                    PutColor(w, key.mainColour); PutColor(w, key.colour1); PutColor(w, key.colour2); PutColor(w, key.colour3);
                    w.Put(key.grubiness);
                }
                SendDoor(w);
                Plugin.L.LogInfo($"[DECOR] я применил отделку: комната {room.roomID}, тип {type}, материал '{mat.name}'");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DECOR] {e.Message}"); }
        }

        static void ApplyDecorRemote(NetDataReader r)
        {
            int rid = r.GetInt(); int type = r.GetByte(); string mname = r.GetString();
            Toolbox.MaterialKey key = null;
            if (r.GetBool())
            {
                string bm = r.GetString();
                key = new Toolbox.MaterialKey();
                key.baseMaterial = FindMaterial(bm);
                key.mainColour = GetColor(r); key.colour1 = GetColor(r); key.colour2 = GetColor(r); key.colour3 = GetColor(r);
                key.grubiness = r.GetFloat();
            }
            var room = RoomById(rid);
            var preset = FindMatPreset(mname);
            var pac = PlayerApartmentController.Instance;
            if (room == null || preset == null || pac == null) { Plugin.L.LogWarning($"[DECOR] не могу применить: комната={(room != null)} материал='{mname}'={(preset != null)} контроллер={(pac != null)}"); return; }
            applyingRemote = true; applyingDoor = true;
            var prevRoom = pac.decoratingRoom;
            try
            {
                pac.decoratingRoom = room;
                pac.ApplyDecor((MaterialGroupPreset.MaterialType)type, preset, key, true);
                Plugin.L.LogInfo($"[DECOR] второй игрок сменил отделку: комната {rid}, тип {type}, материал '{mname}'");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[DECOR] применить: {e.Message}"); }
            finally { try { pac.decoratingRoom = prevRoom; } catch (Exception) { } applyingRemote = false; applyingDoor = false; }
        }

        // Объявление о продаже убираем у себя (если оно ещё на доске)
        static bool TryRemoveSaleNote(int id)
        {
            var dict = CityData.Instance.addressDictionary;
            NewAddress a = null;
            if (dict == null || !dict.TryGetValue(id, out a) || a == null) return false;
            Interactable note = null;
            try { note = a.saleNote; } catch (Exception) { }
            if (note == null) return true;
            applyingRemote = true; applyingDoor = true;
            try { note.SafeDelete(false); a.saleNote = null; Plugin.L.LogInfo($"[APT] убрал объявление о продаже (адрес {id})"); }
            catch (Exception e) { Plugin.L.LogWarning($"[APT] убрать объявление: {e.Message}"); }
            finally { applyingRemote = false; applyingDoor = false; }
            return true;
        }

        static void ApplyApartmentSold(int id, bool log, System.Collections.Generic.List<int[]> rems = null)
        {
            soldAddrs.Add(id);
            try { ApplyRemovals(id, rems); } catch (Exception e) { Plugin.L.LogWarning($"[APT] {e.Message}"); }
            bool ok = false;
            try { ok = TryRemoveSaleNote(id); } catch (Exception e) { Plugin.L.LogWarning($"[APT] {e.Message}"); }
            if (log) Plugin.L.LogInfo($"[APT] второй игрок купил квартиру (адрес {id}), найдено={ok}");
            if (!ok) soldPending.Add(id);
        }

        // ---------- Одинаковая внешность горожан ----------
        static int lookDepth; static float lookAt; static int lookLogged;
        static UnityEngine.Random.State lookSaved;

        internal static void LookSeedBegin(Human h)
        {
            try
            {
                if (!Plugin.CfgSeedLook.Value || h == null) return;
                if (lookDepth > 0 && Time.realtimeSinceStartup - lookAt > 2f) lookDepth = 0; // страховка, если послесловие не вызвалось
                if (lookDepth++ > 0) return;
                lookAt = Time.realtimeSinceStartup;
                lookSaved = UnityEngine.Random.state;
                int sd;
                unchecked { sd = NameHash(h.seed ?? "") * 31 + h.humanID * 7919 + 12345; }
                UnityEngine.Random.InitState(sd);
                if (lookLogged < 6) { lookLogged++; Plugin.Dbg($"[LOOK] внешность горожанина {h.humanID} привязана к его seed ({sd})"); }
            }
            catch (Exception) { }
        }

        internal static void LookSeedEnd()
        {
            try
            {
                if (lookDepth <= 0) return;
                if (--lookDepth == 0) UnityEngine.Random.state = lookSaved;
            }
            catch (Exception) { lookDepth = 0; }
        }

        // ---------- Удары по горожанам ----------
        static System.Collections.Generic.Dictionary<string, SpatterPatternPreset> spatterCache;
        static SpatterPatternPreset FindSpatter(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (spatterCache == null)
            {
                spatterCache = new System.Collections.Generic.Dictionary<string, SpatterPatternPreset>();
                foreach (var p in Resources.FindObjectsOfTypeAll<SpatterPatternPreset>())
                    if (p != null && !string.IsNullOrEmpty(p.name) && !spatterCache.ContainsKey(p.name)) spatterCache[p.name] = p;
            }
            SpatterPatternPreset r; return spatterCache.TryGetValue(name, out r) ? r : null;
        }

        static int ffLog;
        // true = выполнить обычный урон; false = урон пошёл хосту
        internal static bool OnClientHitsNpc(Citizen c, float amount, Actor fromWho, Vector3 pos, Vector3 dir, SpatterPatternPreset fs, SpatterPatternPreset bs, int erase, bool alert, bool forceRag, float dur, float shock, bool kill, bool recoil, float ragMp)
        {
            try
            {
                if (IsHost && !applyingRemote && players.Count > 0 && c != null)
                {
                    var vo = OwnerOfPuppet(c);
                    if (vo != null) return !OnPuppetDamaged(vo.pid, amount, fromWho, pos, dir, fs, bs, erase, alert, forceRag, dur, shock, kill, recoil, ragMp);
                }
                if (IsHost && !applyingRemote && players.Count > 0 && c != null && !IsPuppetCit(c) && Plugin.CfgNoFriendlyFire.Value)
                {
                    var att = fromWho as Citizen;
                    if (att != null && att != c && !IsPuppetCit(att) && att.ai != null)
                    {
                        bool atPuppet = false;
                        try { atPuppet = IsPuppetActor(att.ai.persuitTarget) || IsPuppetActor(att.ai.attackTarget); } catch (Exception) { }
                        if (atPuppet)
                        {
                            if (ffLog++ < 12) Plugin.Dbg($"[FF] удар/выстрел горожанина {att.humanID} по случайному горожанину {c.humanID} (цель - второй игрок) отменён");
                            return false;
                        }
                    }
                }
                if (!IsHost && !applyingRemote && c != null && fromWho != null && fromWho.isPlayer)
                {
                    // я ударил двойника другого игрока = ударил этого игрока
                    var vo = OwnerOfPuppet(c);
                    if (vo != null)
                    {
                        if (Plugin.CfgPvp.Value) OnPuppetDamaged(vo.pid, amount, fromWho, pos, dir, fs, bs, erase, alert, forceRag, dur, shock, kill, recoil, ragMp);
                        return false;
                    }
                }
                if (IsHost || applyingRemote || !Plugin.CfgNpcSync.Value || manager == null || manager.ConnectedPeersCount == 0) return true;
                if (c == null || fromWho == null || !fromWho.isPlayer) return true;
                var w = new NetDataWriter();
                w.Put(MsgNpcHit);
                w.Put(c.humanID); w.Put(amount);
                w.Put(pos.x); w.Put(pos.y); w.Put(pos.z); w.Put(dir.x); w.Put(dir.y); w.Put(dir.z);
                w.Put(fs != null ? (fs.name ?? "") : ""); w.Put(bs != null ? (bs.name ?? "") : "");
                w.Put(erase);
                int fl = (alert ? 1 : 0) | (forceRag ? 2 : 0) | (kill ? 4 : 0) | (recoil ? 8 : 0);
                w.Put((byte)fl); w.Put(dur); w.Put(shock); w.Put(ragMp);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                // мгновенная реакция у себя: отдача в анимации
                try { c.animationController.TakeDamageRecoil(pos); } catch (Exception) { }
                Plugin.Dbg($"[HIT] я ударил горожанина {c.humanID}: урон {amount:F2}, отправил хосту");
                return false;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HIT] {e.Message}"); return true; }
        }

        static void OnNpcHit(NetDataReader r)
        {
            int id = r.GetInt(); float amount = r.GetFloat();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            Vector3 dir = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            string fsn = r.GetString(), bsn = r.GetString();
            int erase = r.GetInt(); int fl = r.GetByte();
            float dur = r.GetFloat(), shock = r.GetFloat(), ragMp = r.GetFloat();
            if (npcById == null || !npcById.ContainsKey(id)) { try { BuildNpcMap(); } catch (Exception) { } }
            Citizen c; if (npcById == null || !npcById.TryGetValue(id, out c) || c == null) { Plugin.L.LogWarning($"[HIT] горожанин {id} не найден у хоста"); return; }
            Actor attacker = null;
            if (Plugin.CfgPuppet.Value && puppet != null && puppet != c) attacker = puppet;
            else if (Plugin.CfgNpcHitHostAttacker.Value) { try { attacker = Player.Instance; } catch (Exception) { } }
            applyingRemote = true;
            try
            {
                c.RecieveDamage(amount, attacker, pos, dir, FindSpatter(fsn), FindSpatter(bsn), (SpatterSimulation.EraseMode)erase,
                    (fl & 1) != 0, (fl & 2) != 0, dur, shock, (fl & 4) != 0, (fl & 8) != 0, ragMp);
                Plugin.Dbg($"[HIT] второй игрок ударил горожанина {id}: урон {amount:F2}, здоровье теперь {c.currentHealth:F2}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HIT] применить удар: {e.Message}"); }
            finally { applyingRemote = false; }
        }


        // ---------- Двойник второго игрока (хост) ----------
        // Горожане реагируют только на Actor. Клиентский аватар - просто картинка, поэтому на хосте
        // запасной горожанин (невидимый) стоит там же, где клиент, и игра видит в нём обычного человека.
        static Citizen puppet;
        static int puppetId = -1;
        static float puppetAt, puppetAiAt, puppetLocAt, puppetIdAt, puppetHideAt;
        static bool puppetErrLogged, puppetCrouch;
        static Renderer[] puppetRends;
        static Collider[] puppetCols;
        static int puppetHideId = -1;
        static float puppetClientHideAt;
        static readonly System.Collections.Generic.Dictionary<int, float> puppetSeenLog = new System.Collections.Generic.Dictionary<int, float>();

        // двойник кого-то из игроков (хост: свои; клиент: тот, что стоит на месте аватара)
        static Citizen PuppetOf(RP rp) { return rp == curRp ? puppet : rp.puppet; }
        static int PuppetIdOf(RP rp) { return rp == curRp ? puppetId : rp.puppetId; }

        static bool IsPuppetCit(Citizen c)
        {
            if (c == null) return false;
            for (int i = 0; i < players.Count; i++)
            {
                if (IsHost) { if (PuppetOf(players[i]) == c) return true; }
                else if (PuppetIdOf(players[i]) >= 0 && PuppetIdOf(players[i]) == c.humanID) return true;
            }
            return false;
        }

        static bool IsPuppetId(int humanId)
        {
            for (int i = 0; i < players.Count; i++) { int id = PuppetIdOf(players[i]); if (id >= 0 && id == humanId) return true; }
            return false;
        }

        static bool IsPuppetActor(Actor a)
        {
            var c = a as Citizen;
            return c != null && IsPuppetCit(c);
        }

        // хост: чьим двойником является горожанин / клиент: чей это двойник (по humanID)
        static RP OwnerOfPuppet(Citizen c)
        {
            if (c == null) return null;
            for (int i = 0; i < players.Count; i++)
            {
                if (IsHost) { if (PuppetOf(players[i]) == c) return players[i]; }
                else if (PuppetIdOf(players[i]) >= 0 && PuppetIdOf(players[i]) == c.humanID) return players[i];
            }
            return null;
        }

        static Citizen PickPuppet()
        {
            int want = Plugin.CfgPuppetId.Value;
            Citizen best = null; int bestScore = int.MinValue;
            if (want >= 0)
            {
                foreach (var c in npcById.Values) if (c != null && c.humanID == want && !IsPuppetCit(c)) return c;
            }
            foreach (var c in npcById.Values)
            {
                if (c == null) continue;
                try
                {
                    if (IsPuppetCit(c)) continue;
                    if (c.removedFromWorld || c.isDead || c.isEnforcer || c.isPlayer || c.isAsleep) continue;
                    int sc = 0;
                    if (c.isHomeless) sc += 100;
                    if (c.job == null) sc += 10;
                    if (c.partner == null) sc += 5;
                    if (c.home == null) sc += 20;
                    sc -= c.humanID / 1000; // при равенстве - стабильный выбор
                    if (sc > bestScore) { bestScore = sc; best = c; }
                }
                catch (Exception) { }
            }
            return best;
        }

        static void CachePuppetParts(Citizen c)
        {
            puppetRends = null; puppetCols = null;
            try
            {
                var rs = c.gameObject.GetComponentsInChildren<Renderer>(true);
                var l = new System.Collections.Generic.List<Renderer>();
                for (int i = 0; i < rs.Length; i++) if (rs[i] != null) l.Add(rs[i]);
                puppetRends = l.ToArray();
            }
            catch (Exception) { }
        }

        static float puppetHideLog;
        static void HidePuppet()
        {
            // рендереры берём заново: игра дорисовывает одежду и части модели позже, их нет в кэше
            var c = puppet; if (c == null) return;
            int on = 0;
            try
            {
                var rs = c.gameObject.GetComponentsInChildren<Renderer>(true);
                puppetRends = rs;
                for (int i = 0; i < rs.Length; i++)
                {
                    try { var r = rs[i]; if (r != null && r.enabled) { r.enabled = false; on++; } } catch (Exception) { }
                }
            }
            catch (Exception) { }
            if (on > 0 && Plugin.CfgPuppetLog.Value && Time.unscaledTime - puppetHideLog > 3f)
            {
                puppetHideLog = Time.unscaledTime;
                Plugin.L.LogInfo($"[PUPPET] игра снова включила {on} рендереров двойника, выключил (сидит клиент={remoteSitting})");
            }
        }

        static void ReleasePuppet(string why)
        {
            var c = puppet; puppet = null; puppetId = -1;
            if (c == null) return;
            try
            {
                if (puppetRends != null) for (int i = 0; i < puppetRends.Length; i++) { try { if (puppetRends[i] != null) puppetRends[i].enabled = true; } catch (Exception) { } }
                if (Plugin.CfgPuppetNoAI.Value && c.ai != null) c.ai.EnableAI(true);
            }
            catch (Exception) { }
            puppetRends = null;
            Plugin.L.LogInfo($"[PUPPET] двойник {c.humanID} ({c.citizenName}) возвращён в город: {why}");
        }

        static void PuppetTick(float dt)
        {
            bool want = Plugin.CfgPuppet.Value && manager != null && manager.ConnectedPeersCount > 0 && avatar != null;
            if (!want)
            {
                if (puppet != null) ReleasePuppet("клиент не подключён");
                return;
            }
            if (puppet == null)
            {
                if (Time.unscaledTime - puppetAt < 2f) return;
                puppetAt = Time.unscaledTime;
                var c = PickPuppet();
                if (c == null) { Plugin.L.LogWarning("[PUPPET] не нашёл подходящего горожанина для двойника"); return; }
                puppet = c; puppetId = c.humanID;
                CachePuppetParts(c);
                try { if (Plugin.CfgPuppetNoAI.Value && c.ai != null) c.ai.EnableAI(false); } catch (Exception e) { Plugin.L.LogWarning($"[PUPPET] EnableAI: {e.Message}"); }
                try { c.SetVisible(true, false); } catch (Exception) { }
                Plugin.L.LogInfo($"[PUPPET] двойником второго игрока стал горожанин {c.humanID} ({c.citizenName}), бездомный={c.isHomeless}. Он невидим и стоит там, где стоит клиент");
            }
            var p = puppet;
            if (p == null) return;
            var tr = p.transform;
            Vector3 pos = targetPos;
            tr.position = pos;
            tr.rotation = Quaternion.Euler(0f, targetYaw - 180f, 0f);
            float now = Time.unscaledTime;
            if (now - puppetLocAt > 0.2f)
            {
                puppetLocAt = now;
                try { p.UpdateGameLocation(0f, false); } catch (Exception) { }
            }
            if (now - puppetHideAt > 0.2f) { puppetHideAt = now; HidePuppet(); }
            if (now - puppetAiAt > 5f)
            {
                puppetAiAt = now;
                try { if (Plugin.CfgPuppetNoAI.Value && p.ai != null) p.ai.EnableAI(false); } catch (Exception) { }
                try { if (!p.visible) p.SetVisible(true, false); } catch (Exception) { }
                try { p.UpdateLightLevel(); } catch (Exception) { }
            }
            try { if (puppetCrouch != remoteCrouched) { puppetCrouch = remoteCrouched; p.SetCrouched(remoteCrouched, true); } } catch (Exception) { }
        }

        // хост: раз в 3 с сообщает клиентам, какой горожанин чей двойник (номер игрока -> humanID)
        static float puppetTblAt;
        static void PuppetTableTick()
        {
            float now = Time.unscaledTime;
            if (now - puppetTblAt < 3f || manager == null) return;
            puppetTblAt = now;
            var ids = new List<int[]>();
            foreach (var rp in players) { int id = PuppetIdOf(rp); if (id >= 0) ids.Add(new[] { rp.pid, id }); }
            if (ids.Count == 0) return;
            var w = new NetDataWriter(); w.Put(MsgPuppet); w.Put((byte)ids.Count);
            foreach (var pr in ids) { w.Put((byte)pr[0]); w.Put(pr[1]); }
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        // клиент: таблица двойников. Для хоста (мой аватар №0) двойник это тот горожанин, которого хост выбрал двойником меня;
        // для остальных клиентов - их собственные двойники
        static void OnPuppetTable(NetDataReader r)
        {
            int n = r.GetByte();
            for (int i = 0; i < n; i++)
            {
                int pid = r.GetByte(); int npc = r.GetInt();
                RP t = RpByPid(pid == MyPid ? 0 : pid);
                if (t == null) continue;
                Enter(t); OnPuppetId(npc); Leave();
            }
        }

        // все игроки за кадр: двойники
        static void PuppetsFrame(float dt)
        {
            for (int i = 0; i < players.Count; i++)
            {
                Enter(players[i]);
                try
                {
                    if (IsHost) PuppetTick(dt);
                    else { PuppetClientTick(); PuppetClientFollow(); }
                }
                catch (Exception e) { if (!puppetErrLogged) { puppetErrLogged = true; Plugin.L.LogWarning($"[PUPPET] {e}"); } }
            }
            Leave();
            if (IsHost) { try { PuppetTableTick(); } catch (Exception) { } }
        }

        // клиент: двойник другого игрока это обычный житель, его надо спрятать и сделать неосязаемым
        static void OnPuppetId(int id)
        {
            if (id == puppetId) return;
            puppetId = id; puppetHideId = -1;
        }

        // ПвП: у клиента двойник стоит на месте аватара хоста, по нему можно ударить (урон уходит хосту)
        static void PuppetClientFollow()
        {
            if (!Plugin.CfgPvp.Value || puppetId < 0 || avatar == null || npcById == null) return;
            Citizen c; if (!npcById.TryGetValue(puppetId, out c) || c == null) return;
            try
            {
                var tr = c.transform; var at = avatar.transform;
                tr.position = at.position;
                tr.rotation = Quaternion.Euler(0f, at.eulerAngles.y, 0f);
            }
            catch (Exception) { }
        }

        static void PuppetClientTick()
        {
            if (puppetId < 0 || npcById == null) return;
            if (puppetHideId == puppetId && Time.unscaledTime - puppetClientHideAt < 0.2f) return;
            puppetClientHideAt = Time.unscaledTime;
            Citizen c; if (!npcById.TryGetValue(puppetId, out c) || c == null) return;
            try
            {
                var rs = c.gameObject.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++) { try { if (rs[i] != null && rs[i].enabled) rs[i].enabled = false; } catch (Exception) { } }
                if (!Plugin.CfgPvp.Value)
                {
                    var cs = c.gameObject.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < cs.Length; i++) { try { if (cs[i] != null && cs[i].enabled) cs[i].enabled = false; } catch (Exception) { } }
                }
                if (puppetHideId != puppetId) Plugin.L.LogInfo($"[PUPPET] у меня горожанин {puppetId} ({c.citizenName}) - двойник другого игрока: спрятан");
                puppetHideId = puppetId;
            }
            catch (Exception) { }
        }

        // горожанин ударил двойника = ударил клиента. true = урон перехвачен
        static bool OnPuppetDamaged(int victimPid, float amount, Actor fromWho, Vector3 pos, Vector3 dir, SpatterPatternPreset fs, SpatterPatternPreset bs, int erase, bool alert, bool forceRag, float dur, float shock, bool kill, bool recoil, float ragMp)
        {
            try
            {
                if (manager == null || manager.ConnectedPeersCount == 0) return true;
                if (IsHost && !Plugin.CfgPvp.Value && fromWho != null && fromWho.isPlayer) return true;
                int aid = -1;
                try { var h = fromWho as Human; if (h != null && !h.isPlayer) aid = h.humanID; } catch (Exception) { }
                var w = new NetDataWriter();
                w.Put(MsgPuppetHurt); w.Put((byte)victimPid);
                w.Put(aid); w.Put(amount);
                w.Put(pos.x); w.Put(pos.y); w.Put(pos.z); w.Put(dir.x); w.Put(dir.y); w.Put(dir.z);
                w.Put(fs != null ? (fs.name ?? "") : ""); w.Put(bs != null ? (bs.name ?? "") : "");
                w.Put(erase);
                int fl = (alert ? 1 : 0) | (forceRag ? 2 : 0) | (kill ? 4 : 0) | (recoil ? 8 : 0);
                w.Put((byte)fl); w.Put(dur); w.Put(shock); w.Put(ragMp);
                if (IsHost)
                {
                    var vr = RpByPid(victimPid);
                    if (vr == null || vr.peer == null) return true;
                    vr.peer.Send(w, DeliveryMethod.ReliableOrdered);
                }
                else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);   // хост получит и перешлёт пострадавшему
                if (Plugin.CfgPuppetLog.Value) Plugin.L.LogInfo($"[PUPPET] по игроку {victimPid} ударили (урон {amount:F2}, от {(aid >= 0 ? aid.ToString() : "игрока")}), переслал ему");
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning($"[PUPPET] {e.Message}"); return false; }
        }

        static void OnPuppetHurt(NetDataReader r)
        {
            int aid = r.GetInt(); float amount = r.GetFloat();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            Vector3 dir = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            string fsn = r.GetString(), bsn = r.GetString();
            int erase = r.GetInt(); int fl = r.GetByte();
            float dur = r.GetFloat(), shock = r.GetFloat(), ragMp = r.GetFloat();
            Actor from = null;
            if (aid >= 0 && npcById != null) { Citizen a; if (npcById.TryGetValue(aid, out a)) from = a; }
            var me = Player.Instance;
            if (me == null) return;
            try
            {
                me.RecieveDamage(amount, from, pos, dir, FindSpatter(fsn), FindSpatter(bsn), (SpatterSimulation.EraseMode)erase,
                    (fl & 1) != 0, (fl & 2) != 0, dur, shock, (fl & 4) != 0, (fl & 8) != 0, ragMp);
                Plugin.L.LogInfo($"[PUPPET] меня ударил горожанин {aid}: урон {amount:F2}, здоровье {me.currentHealth:F2}");
                PlayHitSound(from, pos);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"[PUPPET] урон с атакующим не прошёл ({e.Message}), пробую без него");
                try { me.RecieveDamage(amount, null, pos, dir, FindSpatter(fsn), FindSpatter(bsn), (SpatterSimulation.EraseMode)erase, (fl & 1) != 0, (fl & 2) != 0, dur, shock, (fl & 4) != 0, (fl & 8) != 0, ragMp); }
                catch (Exception e2) { Plugin.L.LogWarning($"[PUPPET] {e2.Message}"); }
            }
        }

        // диагностика: житель нацелился не на двойника, а на другого жителя
        static int aggroLog;
        static readonly System.Collections.Generic.Dictionary<int, float> aggroSeen = new System.Collections.Generic.Dictionary<int, float>();
        static void AggroNote(string what, NewAIController ai, Actor target)
        {
            try
            {
                var tc = target as Citizen; if (tc == null) return;
                var h = ai.human; if (h == null) return;
                int key = (what + h.humanID + ":" + tc.humanID).GetHashCode();
                float last; if (aggroSeen.TryGetValue(key, out last) && Time.unscaledTime - last < 20f) return;
                aggroSeen[key] = Time.unscaledTime;
                if (aggroLog++ >= 80) return;
                string st = "";
                try { st = $"бой={ai.inCombat} атака={ai.attackActive} оружие={(ai.currentWeaponPreset != null ? ai.currentWeaponPreset.name : "нет")}"; } catch (Exception) { }
                string tst = ""; try { tst = $"цель: ko={tc.ai != null && tc.ai.ko} бой={tc.ai != null && tc.ai.inCombat}"; } catch (Exception) { }
                string pd = ""; try { pd = $"; двойник в {Vector3.Distance(tc.transform.position, h.transform.position):F1} м от него, цель в {Vector3.Distance(tc.transform.position, h.transform.position):F1} м"; } catch (Exception) { }
                Plugin.Dbg($"[AGGRO] горожанин {h.humanID} ({h.citizenName}) {what} ДРУГОГО жителя {tc.humanID} ({tc.citizenName}); {st}; {tst}{pd}");
            }
            catch (Exception) { }
        }

        // диагностика: что горожане делают с двойником
        internal static void PuppetNote(string what, NewAIController ai, Actor target)
        {
            try
            {
                if (!IsHost || players.Count == 0 || target == null || !Plugin.CfgPuppetLog.Value || ai == null) return;
                if (!IsPuppetActor(target)) { if (what != "заметил") AggroNote(what, ai, target); return; }
                var h = ai.human;
                int id = h != null ? h.humanID : -1;
                float last; string key = what + id;
                int k = key.GetHashCode();
                if (puppetSeenLog.TryGetValue(k, out last) && Time.unscaledTime - last < 20f) return;
                puppetSeenLog[k] = Time.unscaledTime;
                Plugin.L.LogInfo($"[PUPPET] горожанин {id} ({(h != null ? h.citizenName : "?")}): {what} второго игрока");
            }
            catch (Exception) { }
        }


        // ---------- Разовые действия горожан (хост -> клиент) ----------
        static int npcActLogged;
        static readonly System.Collections.Generic.Dictionary<int, float> actLastAt = new System.Collections.Generic.Dictionary<int, float>();
        static readonly int[] actLogCnt = new int[16], actLogCnt2 = new int[16];
        class AtkState { public bool atk, thr; public int shots; }
        static readonly System.Collections.Generic.Dictionary<int, AtkState> atkStates = new System.Collections.Generic.Dictionary<int, AtkState>();

        // Бой горожан: игра может запускать анимации мимо методов, которые я перехватываю,
        // поэтому дополнительно слежу за самим состоянием боя у хоста и шлю разовые события по смене
        static void NpcActPoll(System.Collections.Generic.List<Citizen> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || IsPuppetCit(c)) continue;
                try
                {
                    var ai = c.ai; if (ai == null) continue;
                    AtkState st;
                    if (!atkStates.TryGetValue(c.humanID, out st)) { st = new AtkState(); atkStates[c.humanID] = st; }
                    bool atk = ai.attackActive, thr = ai.throwActive; int shots = ai.revolverShots;
                    if (atk && !st.atk)
                    {
                        string wn = ""; try { var wp = ai.currentWeaponPreset; if (wp != null) wn = wp.name; } catch (Exception) { }
                        if (actLogCnt[0]++ < 8)
                        {
                            string lw = ""; try { var an = c.animationController.mainAnimator; for (int li = 0; li < an.layerCount; li++) lw += $" L{li}={an.GetLayerWeight(li):F1}"; lw += $" desired={c.animationController.armsLayerDesiredWeight:F1}"; } catch (Exception) { }
                            Plugin.Dbg($"[NPCACT] хост: горожанин {c.humanID} начал атаку, оружие='{wn}', выстрелов={shots}, веса слоёв:{lw}");
                        }
                        NpcAct(c, 1, 0f, false, Vector3.zero, 0);
                    }
                    else if (shots != st.shots && shots > 0) NpcAct(c, 1, 0f, false, Vector3.zero, 0);
                    if (thr && !st.thr) NpcAct(c, 2, 0f, false, Vector3.zero, 0);
                    st.atk = atk; st.thr = thr; st.shots = shots;
                }
                catch (Exception) { }
            }
        }
        static readonly System.Collections.Generic.Dictionary<int, int> npcHeldState = new System.Collections.Generic.Dictionary<int, int>();

        internal static void NpcAct(Citizen c, byte kind, float f, bool b, Vector3 pos, int state)
        {
            try
            {
                if (!IsHost || applyingRemote || !Plugin.CfgNpcSync.Value || c == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (kind <= 5)
                {   // одно и то же действие от перехвата и от слежения за боем не дублируем
                    int key = c.humanID * 16 + kind; float la;
                    if (actLastAt.TryGetValue(key, out la) && Time.unscaledTime - la < 0.2f) return;
                    actLastAt[key] = Time.unscaledTime;
                }
                if (kind == 11) npcHeldState.Remove(c.humanID);
                if (kind == 10)
                {   // одинаковое состояние рук не шлём повторно
                    int old; if (npcHeldState.TryGetValue(c.humanID, out old) && old == state) return;
                    npcHeldState[c.humanID] = state;
                }
                var w = new NetDataWriter();
                w.Put(MsgNpcAct); w.Put(c.humanID); w.Put(kind); w.Put(f); w.Put(b);
                w.Put(pos.x); w.Put(pos.y); w.Put(pos.z); w.Put(state);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                if (kind < 10 && actLogCnt[kind & 15]++ < 8) Plugin.Dbg($"[NPCACT] хост: горожанин {c.humanID} действие {kind} (f={f:F2})");
            }
            catch (Exception) { }
        }

        internal static void NpcActAc(CitizenAnimationController ac, byte kind, float f, bool b, Vector3 pos)
        {
            try { var c = ac != null ? ac.cit as Citizen : null; if (c != null) NpcAct(c, kind, f, b, pos, 0); } catch (Exception) { }
        }

        internal static void NpcActAi(NewAIController ai, byte kind, int state)
        {
            try { var c = ai != null ? ai.human as Citizen : null; if (c != null) NpcAct(c, kind, 0f, false, Vector3.zero, state); } catch (Exception) { }
        }

        static bool npcEnumLogged, combatErrLogged;
        static void OnNpcAct(NetDataReader r)
        {
            int id = r.GetInt(); byte kind = r.GetByte(); float f = r.GetFloat(); bool b = r.GetBool();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat()); int state = r.GetInt();
            if (npcById == null || !npcById.ContainsKey(id)) { try { BuildNpcMap(); } catch (Exception) { } }
            Citizen c; if (npcById == null || !npcById.TryGetValue(id, out c) || c == null) return;
            if (kind < 10 && actLogCnt2[kind & 15]++ < 8) Plugin.Dbg($"[NPCACT] клиент: горожанин {id} действие {kind} (f={f:F2})");
            if (!npcEnumLogged)
            {
                npcEnumLogged = true;
                try { Plugin.Dbg("[NPCACT] ActionStateFlag: " + string.Join(", ", Enum.GetNames(typeof(AIActionPreset.ActionStateFlag)))); } catch (Exception) { }
            }
            applyingRemote = true;
            try
            {
                var ac = c.animationController; var ai = c.ai;
                switch (kind)
                {
                    case 1:
                        if (ac != null)
                        {
                            NpcTarget nt; npcTargets.TryGetValue(id, out nt);
                            var an = ac.mainAnimator;
                            if (nt != null) nt.combatHoldUntil = Time.unscaledTime + 2.5f;
                            if (an != null && nt != null) { try { an.SetInteger("combatArmsOverride", nt.combatArms); } catch (Exception) { } }
                            ac.AttackTrigger();
                            if (nt != null) nt.atkUntil = Time.unscaledTime + 1.2f;
                            if (actLogCnt2[8]++ < 6)
                            {
                                string d = "";
                                try
                                {
                                    if (an != null)
                                    {
                                        d = $"inCombat={an.GetBool("inCombat")} cao={an.GetInteger("combatArmsOverride")} arms={an.GetInteger("ArmsState")} слоёв={an.layerCount} enabled={an.enabled} speed={an.speed}";
                                        for (int li = 0; li < an.layerCount && li < 4; li++) d += $" L{li}:w={an.GetLayerWeight(li):F1}/{an.GetCurrentAnimatorClipInfo(li).Length}";
                                    }
                                }
                                catch (Exception) { }
                                string held = "";
                                try
                                {
                                    var hi = ai != null ? ai.spawnedRightItem : null;
                                    if (hi == null) held = "нет";
                                    else
                                    {
                                        var rs = hi.GetComponentsInChildren<Renderer>(true); int on = 0; for (int ri = 0; ri < rs.Length; ri++) if (rs[ri] != null && rs[ri].enabled) on++;
                                        held = $"{hi.name} активен={hi.activeInHierarchy} рендереров={on}/{rs.Length} в {Vector3.Distance(hi.transform.position, c.transform.position):F2} м от жителя";
                                    }
                                }
                                catch (Exception) { }
                                Plugin.Dbg($"[NPCACT] клиент: аниматор {id}: {d}; предмет в правой руке: {held}");
                            }
                        }
                        break;
                    case 2: if (ac != null) ac.ThrowTrigger(); break;
                    case 3: if (ac != null) ac.AbortAttackTrigger(); break;
                    case 4: if (ac != null) ac.BlockTrigger(f, b); break;
                    case 5: if (ac != null) ac.TakeDamageRecoil(pos); break;
                    case 10: if (ai != null) ai.UpdateHeldItems((AIActionPreset.ActionStateFlag)state); break;
                    case 11: if (ai != null) ai.DespawnRightItem(); break;
                    case 12: if (ai != null) ai.DespawnLeftItem(); break;
                    case 13: if (ai != null) ai.UpdateCurrentWeapon(); break;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[NPCACT] действие {kind} у {id}: {e.Message}"); }
            finally { applyingRemote = false; }
        }


        // ---------- Звуки жителей в бою (хост -> клиент) ----------
        static readonly System.Text.RegularExpressions.Regex combatSoundRx = new System.Text.RegularExpressions.Regex("gun|shot|shoot|fire|pistol|revolver|rifle|bullet|punch|hit|kick|swing|draw(?!er)|holster|reload|impact|knife|sword|melee|block|click|cock|brass|casing|weapon|attack", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Collections.Generic.HashSet<string> soundLogged = new System.Collections.Generic.HashSet<string>();
        static readonly System.Collections.Generic.Dictionary<string, AudioEvent> audioByName = new System.Collections.Generic.Dictionary<string, AudioEvent>();

        internal static void OnWorldSound(AudioEvent ev, Actor who, Vector3 pos)
        {
            try
            {
                if (applyingRemote || ev == null || manager == null || manager.ConnectedPeersCount == 0) return;
                if (who != null && who.isPlayer) { OnPlayerSound(ev, pos); return; }
                if (!IsHost || !Plugin.CfgNpcSync.Value) return;
                var c = who as Citizen;
                if (c == null || IsPuppetCit(c)) return;
                string name = ev.name; if (string.IsNullOrEmpty(name)) return;
                bool fight = false;
                try { var ai = c.ai; fight = ai != null && (ai.inCombat || ai.attackActive || ai.ko); } catch (Exception) { }
                bool match = combatSoundRx.IsMatch(name);
                if (!match && !fight) return;
                if (soundLogged.Count < 60 && soundLogged.Add(name)) Plugin.Dbg($"[SOUND] хост: '{name}' от горожанина {c.humanID} (бой={fight}, по имени={match})");
                var w = new NetDataWriter();
                w.Put(MsgSound); w.Put(c.humanID); w.Put(name); w.Put(pos.x); w.Put(pos.y); w.Put(pos.z);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception) { }
        }

        // ---------- Оружие второго игрока: звуки, выстрелы, вспышки ----------
        static readonly System.Text.RegularExpressions.Regex playerSoundRx = new System.Text.RegularExpressions.Regex("gun|shot|shoot|fire|pistol|revolver|rifle|bullet|punch|kick|swing|reload|knife|sword|melee|block|brass|casing|weapon|attack|equip|holster|cock|hammer|slash|stab|bat|crowbar|whoosh", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Collections.Generic.HashSet<string> fireEventNames = new System.Collections.Generic.HashSet<string>();
        static System.Collections.Generic.Dictionary<string, MurderWeaponPreset> weaponByName, weaponByPrefab;
        static int wpnLog;
        static MurderWeaponPreset lastShotWp; static float lastShotAt;

        static void BuildWeaponMaps()
        {
            if (weaponByName != null) return;
            weaponByName = new System.Collections.Generic.Dictionary<string, MurderWeaponPreset>();
            weaponByPrefab = new System.Collections.Generic.Dictionary<string, MurderWeaponPreset>();
            try
            {
                foreach (var wp in Resources.FindObjectsOfTypeAll<MurderWeaponPreset>())
                {
                    if (wp == null) continue;
                    weaponByName[wp.name] = wp;
                    try { if (wp.fireEvent != null) fireEventNames.Add(wp.fireEvent.name); } catch (Exception) { }
                    try { if (wp.itemRightOverride != null) weaponByPrefab[wp.itemRightOverride.name] = wp; } catch (Exception) { }
                    try { if (wp.itemLeftOverride != null) weaponByPrefab[wp.itemLeftOverride.name] = wp; } catch (Exception) { }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[WEAPON] список оружия: " + e.Message); }
            Plugin.L.LogInfo("[WEAPON] видов оружия: " + weaponByName.Count + ", моделей в руках: " + weaponByPrefab.Count);
        }

        static MurderWeaponPreset FindWeapon(string name)
        {
            BuildWeaponMaps();
            MurderWeaponPreset w; weaponByName.TryGetValue(name ?? "", out w); return w;
        }

        static Citizen RemotePuppet()
        {
            if (IsHost) return puppet;
            Citizen c = null; if (puppetId >= 0 && npcById != null) npcById.TryGetValue(puppetId, out c);
            return c;
        }

        // какое оружие у меня в руках: по имени модели, которую игра создаёт в руке (префаб из MurderWeaponPreset)
        static string DetectHeldWeapon(out string names)
        {
            names = "";
            try
            {
                var fp = FirstPersonItemController.Instance;
                if (fp == null || fp.forceHolstered) return "";
                BuildWeaponMaps();
                var sb = new StringBuilder();
                MurderWeaponPreset wp;
                foreach (var pr in new[] { fp.rightPrefabReference, fp.leftPrefabReference })
                {
                    if (pr == null) continue;
                    string k = pr.name.Replace("(Clone)", "").Trim();
                    sb.Append("ref:" + k + ";");
                    if (weaponByPrefab.TryGetValue(k, out wp)) { names = sb.ToString(); return wp.name; }
                }
                for (int side = 0; side < 2; side++)
                {
                    Transform par = side == 0 ? fp.rightHandObjectParent : fp.leftHandObjectParent;
                    if (par == null) continue;
                    for (int i = 0; i < par.childCount; i++)
                    {
                        string k = par.GetChild(i).name.Replace("(Clone)", "").Trim();
                        sb.Append(k + ";");
                        if (weaponByPrefab.TryGetValue(k, out wp)) { names = sb.ToString(); return wp.name; }
                    }
                }
                names = sb.ToString();
            }
            catch (Exception) { }
            return "";
        }

        static string weaponSent = null; static float weaponResendAt; static string remoteWeaponName = ""; static int remoteWeaponCao = -1;

        static void SendWeaponIfChanged()
        {
            string names; string wn = DetectHeldWeapon(out names);
            bool changed = wn != weaponSent;
            if (!changed && Time.realtimeSinceStartup < weaponResendAt) return;
            weaponSent = wn; weaponResendAt = Time.realtimeSinceStartup + 5f;
            if (changed) Plugin.L.LogInfo("[WEAPON] у меня в руках оружие: '" + wn + "' (предмет '" + curItem + "', модели в руках: " + names + ")");
            var w = new NetDataWriter(); w.Put(MsgWeapon); w.Put(wn);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        static void OnWeaponMessage(NetDataReader r)
        {
            string prevWeapon = remoteWeaponName;
            remoteWeaponName = r.GetString(); remoteWeaponCao = -1;
            var wp = remoteWeaponName != "" ? FindWeapon(remoteWeaponName) : null;
            if (wp != null)
            {
                switch (wp.type)
                {
                    // игроки не стреляют: любое оружие в их руках это дубина (как в оригинале)
                    case MurderWeaponPreset.WeaponType.poison: case MurderWeaponPreset.WeaponType.strangulation: case MurderWeaponPreset.WeaponType.fists: remoteWeaponCao = -1; break;
                    default: remoteWeaponCao = 4; break;
                }
            }
            if (prevWeapon == remoteWeaponName) return;
            remoteHeldDirty = true;
            Plugin.L.LogInfo("[WEAPON] у второго игрока в руках оружие: '" + remoteWeaponName + "' тип=" + (wp != null ? "" + wp.type : "-") + " поза=" + remoteWeaponCao);
        }

        // мой звук (оружие, удары, замах) -> второму игроку
        static void OnPlayerSound(AudioEvent ev, Vector3 pos)
        {
            string name = ev.name; if (string.IsNullOrEmpty(name)) return;
            bool match = playerSoundRx.IsMatch(name);
            if (soundLogged.Count < 120 && soundLogged.Add("me:" + name)) Plugin.Dbg($"[SOUND] мой звук '{name}' (отправляю={match})");
            if (!match) return;
            var w = new NetDataWriter();
            w.Put(MsgSound); w.Put(-1); w.Put(name); w.Put(pos.x); w.Put(pos.y); w.Put(pos.z);
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        // мой выстрел -> второму игроку (до расчёта попадания, чтобы звук/вспышка пришли раньше сообщения об уроне)
        internal static void OnShoot(Actor who, Vector3 muzzle, Vector3 aim, float range, float acc, float dmg, MurderWeaponPreset wp, bool eject, Vector3 ejectPt)
        {
            try
            {
                if (applyingRemote || who == null || !who.isPlayer || manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgShot); w.Put(wp != null ? (wp.name ?? "") : "");
                w.Put(muzzle.x); w.Put(muzzle.y); w.Put(muzzle.z); w.Put(aim.x); w.Put(aim.y); w.Put(aim.z);
                w.Put(range); w.Put(eject); w.Put(ejectPt.x); w.Put(ejectPt.y); w.Put(ejectPt.z);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                if (wpnLog++ < 8) Plugin.L.LogInfo($"[WEAPON] мой выстрел: {(wp != null ? wp.name : "?")}, урон {dmg:F2}, дальность {range:F1}, гильза={eject}");
            }
            catch (Exception) { }
        }

        static bool IsUnder(Transform t, Transform root)
        {
            for (int i = 0; t != null && i < 40; i++) { if (t == root) return true; t = t.parent; }
            return false;
        }

        static void SpawnCasing(MurderWeaponPreset wp, Vector3 pos, Vector3 right)
        {
            try
            {
                if (wp.shellCasing == null) return;
                applyingRemote = true;
                Interactable it;
                try { it = InteractableCreator.Instance.CreateWorldInteractable(wp.shellCasing, null, null, null, pos, new Vector3(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), 0f), null, null, ""); }
                finally { applyingRemote = false; }
                if (it == null) return;
                GameObject go = it.spawnedObject;
                if (go == null && it.controller != null) go = it.controller.gameObject;
                if (go != null)
                {
                    var rb = go.GetComponentInChildren<Rigidbody>();
                    if (rb != null) { rb.isKinematic = false; rb.velocity = right * UnityEngine.Random.Range(1.2f, 2.2f) + Vector3.up * UnityEngine.Random.Range(1.0f, 2.0f); rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f; }
                }
                casings.Add(new KeyValuePair<Interactable, float>(it, Time.unscaledTime + 20f));
            }
            catch (Exception e) { if (brassLog++ < 4) Plugin.L.LogWarning("[FX] гильза игрока: " + e.Message); }
        }

        // выстрел второго игрока у меня: звук, вспышка, гильза, след попадания (урона здесь нет: его считает стрелявший и присылает отдельно)
        static void OnShotMessage(NetDataReader r)
        {
            string wn = r.GetString();
            Vector3 muzzle = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            Vector3 aim = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            float range = r.GetFloat(); bool eject = r.GetBool();
            Vector3 ep = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            try
            {
                var wp = FindWeapon(wn);
                var pc = RemotePuppet();
                lastShotWp = wp; lastShotAt = Time.realtimeSinceStartup;
                Vector3 dir = aim - muzzle; if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward; dir.Normalize();
                if (wp == null) { if (wpnLog++ < 8) Plugin.L.LogWarning("[WEAPON] выстрел из неизвестного оружия '" + wn + "'"); return; }
                if (wp.fireEvent != null)
                {
                    applyingRemote = true;
                    try { AudioController.Instance.PlayWorldOneShot(wp.fireEvent, pc, pc != null ? pc.currentNode : null, muzzle); }
                    finally { applyingRemote = false; }
                }
                if (wp.muzzleFlash != null)
                {
                    GameObject fx = UnityEngine.Object.Instantiate(wp.muzzleFlash);
                    fx.SetActive(true);
                    fx.transform.position = muzzle; fx.transform.rotation = Quaternion.LookRotation(dir);
                    foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { try { ps.Play(true); } catch (Exception) { } }
                    foreach (var l in fx.GetComponentsInChildren<Light>(true)) { try { l.enabled = true; } catch (Exception) { } }
                    UnityEngine.Object.Destroy(fx, 0.35f);
                }
                if (eject) SpawnCasing(wp, ep, Vector3.Cross(Vector3.up, dir).normalized);
                // куда попала пуля: брызги/искры/дыра в поверхности (себя и аватар/двойника пропускаем)
                float maxd = range > 1f ? range : 60f;
                try { if (wp.maximumBulletRange > 1f) maxd = Mathf.Min(maxd, wp.maximumBulletRange); } catch (Exception) { }
                var all = Physics.RaycastAll(muzzle, dir, maxd, ~0, QueryTriggerInteraction.Ignore);
                var list = new System.Collections.Generic.List<RaycastHit>();
                foreach (var h in all) list.Add(h);
                list.Sort((a, b) => a.distance.CompareTo(b.distance));
                var me = Player.Instance;
                foreach (var h in list)
                {
                    var col = h.collider; if (col == null) continue;
                    var ct = col.transform;
                    if ((avatar != null && IsUnder(ct, avatar.transform)) || (pc != null && IsUnder(ct, pc.transform)) || (me != null && IsUnder(ct, me.transform))) continue;
                    applyingRemote = true;
                    try { Toolbox.Instance.CreateBulletSurfaceContactFX(wp, h); }
                    catch (Exception e) { if (wpnLog++ < 8) Plugin.L.LogWarning("[WEAPON] след пули: " + e.Message); }
                    finally { applyingRemote = false; }
                    break;
                }
                if (wpnLog++ < 8) Plugin.L.LogInfo($"[WEAPON] выстрел второго игрока: {wp.name}, вспышка={(wp.muzzleFlash != null)}, звук={(wp.fireEvent != null ? wp.fireEvent.name : "нет")}, гильза={eject}");
            }
            catch (Exception e) { if (wpnLog++ < 8) Plugin.L.LogWarning("[WEAPON] выстрел второго игрока: " + e.Message); }
        }

        static void OnSoundMessage(NetDataReader r)
        {
            int id = r.GetInt(); string name = r.GetString();
            Vector3 pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            if (id >= 0 && (IsHost || !Plugin.CfgNpcSync.Value)) return;
            BuildWeaponMaps();
            if (id < 0 && fireEventNames.Contains(name)) return;   // звук выстрела играет сообщение о выстреле
            try
            {
                Citizen c = null;
                if (id < 0) c = RemotePuppet(); else if (npcById != null) npcById.TryGetValue(id, out c);
                AudioEvent ev;
                if (!audioByName.TryGetValue(name, out ev) || ev == null)
                {
                    ev = null;
                    foreach (var e in Resources.FindObjectsOfTypeAll<AudioEvent>()) { if (e != null && e.name == name) { ev = e; break; } }
                    audioByName[name] = ev;
                }
                if (ev == null) { if (soundLogged.Add("miss:" + name)) Plugin.L.LogWarning($"[SOUND] у меня нет звука '{name}'"); return; }
                if (soundLogged.Count < 60 && soundLogged.Add("play:" + name)) Plugin.Dbg($"[SOUND] клиент: играю '{name}'");
                applyingRemote = true;
                try { AudioController.Instance.PlayWorldOneShot(ev, c, c != null ? c.currentNode : null, pos); }
                finally { applyingRemote = false; }
                MuzzleFx(c, name, pos);
            }
            catch (Exception e) { if (soundLogged.Add("err")) Plugin.L.LogWarning($"[SOUND] {e.Message}"); }
        }

        static AudioEvent FindAudio(string name)
        {
            AudioEvent ev;
            if (audioByName.TryGetValue(name, out ev) && ev != null) return ev;
            ev = null;
            foreach (var e in Resources.FindObjectsOfTypeAll<AudioEvent>()) { if (e != null && e.name == name) { ev = e; break; } }
            audioByName[name] = ev;
            return ev;
        }

        static int brassLog;
        static readonly System.Collections.Generic.List<KeyValuePair<Interactable, float>> casings = new System.Collections.Generic.List<KeyValuePair<Interactable, float>>();

        // Гильза: настоящий предмет мира с физикой (звук падения даёт игра), удаляется через 20 с
        static void EjectCasing(Citizen c, MurderWeaponPreset wp, GameObject hi)
        {
            try
            {
                if (wp.ejectBrassSetting != MurderWeaponPreset.EjectBrass.onFire || wp.shellCasing == null) return;
                Vector3 pos = hi != null ? hi.transform.TransformPoint(wp.brassEjectOffset) : c.transform.position + Vector3.up * 1.3f;
                Vector3 right = hi != null ? hi.transform.right : c.transform.right;
                Vector3 up = Vector3.up;
                applyingRemote = true;
                Interactable it;
                try { it = InteractableCreator.Instance.CreateWorldInteractable(wp.shellCasing, null, null, null, pos, new Vector3(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), 0f), null, null, ""); }
                finally { applyingRemote = false; }
                if (it == null) { if (brassLog++ < 3) Plugin.L.LogWarning("[FX] гильза не создана"); return; }
                GameObject go = it.spawnedObject;
                if (go == null && it.controller != null) go = it.controller.gameObject;
                if (go != null)
                {
                    var rb = go.GetComponentInChildren<Rigidbody>();
                    if (rb != null) { rb.isKinematic = false; rb.velocity = right * UnityEngine.Random.Range(1.2f, 2.2f) + up * UnityEngine.Random.Range(1.0f, 2.0f); rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f; }
                    else if (brassLog < 3) Plugin.Dbg("[FX] у гильзы нет Rigidbody");
                }
                casings.Add(new KeyValuePair<Interactable, float>(it, Time.unscaledTime + 20f));
                if (brassLog++ < 4) Plugin.Dbg($"[FX] клиент: гильза '{wp.shellCasing.name}' у {c.humanID}");
            }
            catch (Exception e) { if (brassLog++ < 4) Plugin.L.LogWarning("[FX] гильза: " + e.Message); }
        }

        static void CasingTick()
        {
            if (casings.Count == 0) return;
            for (int i = casings.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < casings[i].Value) continue;
                var it = casings[i].Key; casings.RemoveAt(i);
                applyingRemote = true;
                try { if (it != null) it.SafeDelete(false); } catch (Exception) { } finally { applyingRemote = false; }
            }
        }

        // Звук попадания по клиенту: оригинальный урон по двойнику на хосте пропускается вместе со звуком
        static int hitSndLog;
        static void PlayHitSound(Actor from, Vector3 pos)
        {
            try
            {
                var cit = from as Citizen;
                AudioEvent ev = null;
                if (cit == null && lastShotWp != null && Time.realtimeSinceStartup - lastShotAt < 0.8f)
                {
                    ev = lastShotWp.impactEventPlayer; if (ev == null) ev = lastShotWp.impactEventBody;
                }
                if (ev == null && cit != null && cit.ai != null)
                {
                    var wp = cit.ai.currentWeaponPreset;
                    if (wp != null) { ev = wp.impactEventPlayer; if (ev == null) ev = wp.impactEventBody; }
                }
                if (ev == null) ev = FindAudio("punchHitFlesh");
                if (ev == null) return;
                applyingRemote = true;
                try { AudioController.Instance.PlayWorldOneShot(ev, cit, cit != null ? cit.currentNode : null, pos); }
                finally { applyingRemote = false; }
                if (hitSndLog++ < 5) Plugin.Dbg($"[SOUND] клиент: звук попадания '{ev.name}'");
            }
            catch (Exception e) { if (hitSndLog++ < 5) Plugin.L.LogWarning("[SOUND] попадание: " + e.Message); }
        }

        static int muzzleLog;
        // Вспышка выстрела: при звуке выстрела текущего оружия жителя
        static void MuzzleFx(Citizen c, string soundName, Vector3 pos)
        {
            try
            {
                if (c == null || c.ai == null) return;
                var wp = c.ai.currentWeaponPreset;
                if (wp == null || wp.fireEvent == null || wp.fireEvent.name != soundName) return;
                var flash = wp.muzzleFlash;
                var hi = c.ai.spawnedRightItem;
                if (flash == null) { if (muzzleLog++ < 3) Plugin.Dbg($"[FX] у оружия {wp.name} нет muzzleFlash"); return; }
                GameObject fx = UnityEngine.Object.Instantiate(flash);
                fx.SetActive(true);
                if (hi != null)
                {
                    fx.transform.SetParent(hi.transform, false);
                    fx.transform.localPosition = wp.muzzleOffset;
                    fx.transform.localRotation = Quaternion.identity;
                }
                else fx.transform.position = pos + Vector3.up * 1.4f;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { try { ps.Play(true); } catch (Exception) { } }
                foreach (var l in fx.GetComponentsInChildren<Light>(true)) { try { l.enabled = true; } catch (Exception) { } }
                UnityEngine.Object.Destroy(fx, 0.35f);
                EjectCasing(c, wp, hi);
                if (muzzleLog++ < 6) Plugin.Dbg($"[FX] клиент: вспышка '{flash.name}' у {c.humanID}, оружие {wp.name}, предмет={(hi != null ? hi.name : "нет")}, смещение={wp.muzzleOffset}");
            }
            catch (Exception e) { if (muzzleLog++ < 6) Plugin.L.LogWarning("[FX] " + e.Message); }
        }

        // ---------- Горожане: хост ведёт, клиент повторяет ----------
        static System.Collections.Generic.Dictionary<int, Citizen> npcById;
        static float npcMapAt, npcSendAt, npcLogAt;
        static int npcRoll;
        static bool npcErrLogged, npcClientPaused, npcMissLogged;
        static int npcSentCount, npcRecvCount;

        class NpcTarget
        {
            public Citizen c;
            public Vector3 pos; public float yaw, speed;
            public byte flags, flags2, idle, arms, carryItem, combatArms, outfit = 255;
            public int apOutfit = -1, apKo = -1; public float outfitTry;
            public bool fresh = true, snap = true, wasTalking;
            public float lastLoc; public Vector3 lastLocPos;
            public float lastSpeed = -1f;
            public int ap = -1;           // последнее применённое: битовая маска состояний
            public int apIdle = -1, apArms = -1, apCarry = -1, apCombat = -1;
            public float atkUntil, combatHoldUntil; public bool atkHeld; public int curCombat = -1;
        }
        static readonly System.Collections.Generic.Dictionary<int, NpcTarget> npcTargets = new System.Collections.Generic.Dictionary<int, NpcTarget>();

        static void BuildNpcMap()
        {
            npcMapAt = Time.unscaledTime;
            if (npcById == null) npcById = new System.Collections.Generic.Dictionary<int, Citizen>();
            var list = CityData.Instance.citizenDirectory;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                try { npcById[c.humanID] = c; } catch (Exception) { }
            }
        }

        static void NpcTick(float dt)
        {
            if (!Plugin.CfgNpcSync.Value || manager == null || manager.ConnectedPeersCount == 0) return;
            if (npcById == null || npcById.Count == 0 || Time.unscaledTime - npcMapAt > 30f) BuildNpcMap();
            if (npcById.Count == 0) return;
            PuppetsFrame(dt);
            if (IsHost) NpcSendTick();
            else NpcClientTick(dt);
        }

        static void PutNpc(NetDataWriter w, Citizen c)
        {
            Vector3 p = c.transform.position;
            w.Put(c.humanID);
            w.Put(p.x); w.Put(p.y); w.Put(p.z);
            w.Put(c.transform.eulerAngles.y);
            float sp = 0f; try { sp = c.currentNormalizedSpeed; } catch (Exception) { }
            w.Put(sp);
            int f = 0;
            try { if (c.isDead) f |= 1; } catch (Exception) { }
            try { if (c.ai != null && c.ai.ko) f |= 2; } catch (Exception) { }
            try { if (c.ai != null && c.ai.inCombat) f |= 4; } catch (Exception) { }
            try { if (c.isInBed) f |= 8; } catch (Exception) { }
            try { if (c.isInLowBed) f |= 16; } catch (Exception) { }
            try { if (c.isRunning) f |= 128; } catch (Exception) { }
            int f2 = 0;
            try { if (c.isMoving) f2 |= 1; } catch (Exception) { }
            try { if (c.ai != null && c.ai.restrained) f2 |= 2; } catch (Exception) { }
            int idle = 0, arms = 0, carry = 0, comb = 0;
            try
            {
                var ac = c.animationController;
                if (ac != null)
                {
                    idle = (int)ac.idleAnimationState; arms = (int)ac.armsBoolAnimationState;
                    var an = ac.mainAnimator;
                    if (an != null)
                    {
                        if (an.GetBool("carryingItem")) f |= 32;
                        carry = an.GetInteger("carryItem"); comb = an.GetInteger("combatArmsOverride");
                    }
                }
            }
            catch (Exception) { }
            int outfit = 255;
            try { var oc = c.outfitController; if (oc != null) outfit = (int)oc.currentOutfit; } catch (Exception) { }
            w.Put((byte)f); w.Put((byte)f2); w.Put((byte)idle); w.Put((byte)arms); w.Put((byte)carry); w.Put((byte)comb); w.Put((byte)outfit);
        }

        sealed class NpcSent { public byte[] data = new byte[48]; public int len; public float at; }
        static readonly System.Collections.Generic.Dictionary<int, NpcSent> npcSent = new System.Collections.Generic.Dictionary<int, NpcSent>();
        static NetDataWriter npcScratch = new NetDataWriter();
        static int npcBytes, npcSkipped;

        static void NpcSendTick()
        {
            float now = Time.unscaledTime;
            if (now - npcSendAt < Mathf.Max(0.03f, Plugin.CfgNpcRate.Value)) return;
            npcSendAt = now;
            Vector3 hp = Vector3.zero; bool hasH = false;
            try { hp = Player.Instance.transform.position; hasH = true; } catch (Exception) { }
            var cps = new List<Vector3>();
            SyncCur();
            foreach (var rp in players) if (rp.avatar != null) cps.Add(rp.targetPos);
            float rMid = Plugin.CfgNpcRange.Value; float rNear = rMid * 0.33f;
            float rNear2 = rNear * rNear, rMid2 = rMid * rMid;
            float baseIv = Mathf.Max(0.03f, Plugin.CfgNpcRate.Value);
            var all = new System.Collections.Generic.List<Citizen>(npcById.Values);
            NpcActPoll(all);
            var recs = new System.Collections.Generic.List<NpcSent>();
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || IsPuppetCit(c)) continue;
                try { if (c.removedFromWorld) continue; } catch (Exception) { }
                float d2 = float.MaxValue;
                try
                {
                    Vector3 p = c.transform.position;
                    if (hasH) d2 = Mathf.Min(d2, (p - hp).sqrMagnitude);
                    for (int ci = 0; ci < cps.Count; ci++) d2 = Mathf.Min(d2, (p - cps[ci]).sqrMagnitude);
                }
                catch (Exception) { continue; }
                // чем дальше горожанин от игроков, тем реже он обновляется
                float iv, keep;
                if (d2 < rNear2) { iv = baseIv; keep = 1f; }
                else if (d2 < rMid2) { iv = baseIv * 3f; keep = 1.5f; }
                else { iv = baseIv * 8f; keep = 3f; }
                NpcSent st;
                if (!npcSent.TryGetValue(c.humanID, out st)) { st = new NpcSent(); st.at = -100f; npcSent[c.humanID] = st; }
                float age = now - st.at;
                if (age < iv) { npcSkipped++; continue; }
                npcScratch.Reset();
                try { PutNpc(npcScratch, c); } catch (Exception) { continue; }
                int len = npcScratch.Length; if (len > st.data.Length) continue;
                bool same = st.len == len;
                if (same) { var d = npcScratch.Data; for (int b = 0; b < len; b++) if (d[b] != st.data[b]) { same = false; break; } }
                if (same && age < keep) { npcSkipped++; continue; } // ничего не изменилось
                Buffer.BlockCopy(npcScratch.Data, 0, st.data, 0, len); st.len = len; st.at = now;
                recs.Add(st);
            }
            int pos = 0;
            while (pos < recs.Count)
            {
                int n = Math.Min(28, recs.Count - pos);
                var w = new NetDataWriter();
                w.Put(MsgNpc); w.Put((byte)n);
                for (int i = 0; i < n; i++) { var st = recs[pos + i]; w.Put(st.data, 0, st.len); }
                manager.SendToAll(w, DeliveryMethod.Sequenced);
                npcSentCount += n; npcBytes += w.Length;
                pos += n;
            }
            if (now - npcLogAt > 15f)
            {
                npcLogAt = now;
                Plugin.L.LogInfo($"[NPC] хост: за 15 с отправлено записей: {npcSentCount} (~{npcBytes / 15 / 1024f:F1} КБ/с), пропущено как неизменных/далёких: {npcSkipped}, горожан: {all.Count}");
                npcSentCount = 0; npcBytes = 0; npcSkipped = 0;
            }
        }

        static void OnNpcMessage(NetDataReader r)
        {
            int n = r.GetByte();
            for (int i = 0; i < n; i++)
            {
                int id = r.GetInt();
                Vector3 p = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
                float yaw = r.GetFloat(); float sp = r.GetFloat();
                byte f = r.GetByte(), f2 = r.GetByte(), idle = r.GetByte(), arms = r.GetByte(), carry = r.GetByte(), comb = r.GetByte(), outfit = r.GetByte();
                if (npcById == null || !npcById.ContainsKey(id)) { try { BuildNpcMap(); } catch (Exception) { } }
                Citizen c; if (npcById == null || !npcById.TryGetValue(id, out c) || c == null)
                {
                    if (!npcMissLogged) { npcMissLogged = true; Plugin.L.LogWarning($"[NPC] горожанин {id} не найден у меня"); }
                    continue;
                }
                NpcTarget t;
                if (!npcTargets.TryGetValue(id, out t)) { t = new NpcTarget { c = c }; npcTargets[id] = t; }
                t.c = c; t.pos = p; t.yaw = yaw; t.speed = sp; t.flags = f; t.flags2 = f2; t.idle = idle; t.arms = arms; t.carryItem = carry; t.combatArms = comb; t.outfit = outfit;
                npcRecvCount++;
            }
        }

        static void NpcClientTick(float dt)
        {
            CasingTick();
            if (npcTargets.Count == 0) return;
            if (!npcClientPaused)
            {
                try { Game.Instance.SetPauseAI(true); npcClientPaused = true; Plugin.L.LogInfo("[NPC] клиент: ИИ горожан остановлен, слежу за хостом"); }
                catch (Exception e) { Plugin.L.LogWarning($"[NPC] SetPauseAI: {e.Message}"); }
            }
            if (Time.unscaledTime - npcLogAt > 15f)
            {
                npcLogAt = Time.unscaledTime;
                Plugin.L.LogInfo($"[NPC] клиент: за 15 с получено записей: {npcRecvCount}, веду горожан: {npcTargets.Count}");
                npcRecvCount = 0;
            }
            float kp = 1f - Mathf.Exp(-12f * dt);
            int locBudget = 8;
            foreach (var kv in npcTargets)
            {
                var t = kv.Value; var c = t.c;
                try
                {
                    if (c == null) continue;
                    if (IsPuppetId(kv.Key)) continue; // двойник другого игрока: у меня он невидим и стоит на месте его аватара
                    var tr = c.transform;
                    if (tr == null) continue;
                    bool talking = false;
                    try { talking = c.inConversation; } catch (Exception) { }
                    if (talking)
                    {
                        if (!t.wasTalking) { t.wasTalking = true; Plugin.L.LogInfo($"[NPC] разговор с горожанином {kv.Key}: моя y={tr.position.y:F2}, у хоста y={t.pos.y:F2}; позицию не трогаю"); }
                        ApplyNpcState(t);
                        continue;
                    }
                    t.wasTalking = false;
                    if ((t.flags & 2) != 0 && !t.snap) { ApplyNpcState(t); continue; } // оглушён: его тело ведёт физика, не двигаю
                    Vector3 cur = tr.position;
                    float d = Vector3.Distance(cur, t.pos);
                    if (t.snap || d > 4f) { tr.position = t.pos; tr.rotation = Quaternion.Euler(0f, t.yaw, 0f); t.snap = false; }
                    else
                    {
                        tr.position = Vector3.Lerp(cur, t.pos, kp);
                        tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.Euler(0f, t.yaw, 0f), kp);
                    }
                    ApplyNpcState(t);
                    ArmsLayerForAttack(t);
                    CombatSync(t);
                    if (locBudget > 0 && Time.unscaledTime - t.lastLoc > 0.5f && (t.lastLocPos - tr.position).sqrMagnitude > 0.25f)
                    {
                        t.lastLoc = Time.unscaledTime; t.lastLocPos = tr.position; locBudget--;
                        try { c.UpdateGameLocation(0f, false); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }
        }

        // Боевые позы рук (удар, выстрел) лежат в слое ArmsOverride. У клиента вес слоя оставался 0,
        // поэтому атака «проигрывалась», но не была видна. На время атаки включаю слой
        static void ArmsLayerForAttack(NpcTarget t)
        {
            var c = t.c; if (c == null) return;
            var ac = c.animationController; if (ac == null) return;
            var an = ac.mainAnimator; if (an == null || an.layerCount < 2) return;
            if (Time.unscaledTime < t.atkUntil)
            {
                try { an.SetLayerWeight(1, 1f); } catch (Exception) { }
                try { ac.armsLayerDesiredWeight = 1f; } catch (Exception) { }
                t.atkHeld = true;
            }
            else if (t.atkHeld)
            {
                t.atkHeld = false;
                if (t.arms == 0 && t.carryItem == 0)
                {
                    try { an.SetLayerWeight(1, 0f); } catch (Exception) { }
                    try { ac.armsLayerDesiredWeight = 0f; } catch (Exception) { }
                }
            }
        }

        // Боевая стойка с задержкой: у хоста флаг боя мигает, поэтому клиенту хватает «включил»,
        // а выключить он может только через 1 с после того, как хост перестал считать жителя дерущимся
        static void CombatSync(NpcTarget t)
        {
            var c = t.c; if (c == null) return;
            float now = Time.unscaledTime;
            if ((t.flags & 4) != 0) t.combatHoldUntil = Math.Max(t.combatHoldUntil, now + 1.0f);
            int want = now < t.combatHoldUntil ? 1 : 0;
            if (t.curCombat == want) return;
            bool first = t.curCombat < 0;
            t.curCombat = want;
            if (first && want == 0) return;
            bool cmb = want == 1;
            try { if (c.ai != null) { c.ai.SetInCombat(cmb, true); if (cmb) c.ai.UpdateCurrentWeapon(); } } catch (Exception e) { if (!combatErrLogged) { combatErrLogged = true; Plugin.L.LogWarning($"[NPCACT] ai.SetInCombat: {e.Message}"); } }
            try { var ac = c.animationController; if (ac != null) ac.SetInCombat(cmb); } catch (Exception) { }
        }

        static void ApplyNpcState(NpcTarget t)
        {
            var c = t.c;
            var ac = c.animationController;
            if (ac == null) return;
            bool dead = (t.flags & 1) != 0;
            bool inBed = (t.flags & 8) != 0, low = (t.flags & 16) != 0;
            int mask = (t.flags & (1 | 2 | 4 | 8 | 16 | 32 | 128)) | ((t.flags2 & 3) << 8);
            if (mask != t.ap)
            {
                int old = t.ap; t.ap = mask;
                if (old < 0 || ((old ^ mask) & 1) != 0)
                {
                    if (dead) { try { ac.SetDead(true); c.isDead = true; Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: мёртв (по данным хоста)"); } catch (Exception) { } }
                    else if (old >= 0)
                    {
                        // хост больше не считает его мёртвым (флаг мигнул при нокауте): поднимаю и у себя
                        try { ac.SetDead(false); c.isDead = false; Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: больше не мёртв (по данным хоста), снимаю позу смерти"); } catch (Exception) { }
                    }
                }
                if (old < 0 || ((old ^ mask) & (8 | 16)) != 0)
                {
                    try { c.isInBed = inBed; c.isInLowBed = low; ac.SetInBed(inBed, low, false, true); } catch (Exception) { }
                }
                bool koNow = (t.flags & 2) != 0;
                if (t.apKo != (koNow ? 1 : 0))
                {
                    bool first = t.apKo < 0; t.apKo = koNow ? 1 : 0;
                    if (!(first && !koNow))
                    {
                        try { if (c.ai != null) c.ai.SetKO(koNow, Vector3.zero, Vector3.zero, true, 0f); Plugin.L.LogInfo($"[NPC] горожанин {c.humanID}: {(koNow ? "оглушён" : "пришёл в себя")} (по данным хоста)"); }
                        catch (Exception) { }
                    }
                }
                if (old < 0 || ((old ^ mask) & 32) != 0) { try { ac.SetCarryingItem((t.flags & 32) != 0); } catch (Exception) { } }
                if (old < 0 || ((old ^ (mask)) & (1 << 8)) != 0) { try { c.isMoving = (t.flags2 & 1) != 0; } catch (Exception) { } }
                if (old < 0 || ((old ^ mask) & 128) != 0) { try { c.isRunning = (t.flags & 128) != 0; } catch (Exception) { } }
                if (old < 0 || ((old ^ (mask)) & (2 << 8)) != 0) { try { ac.SetRestrained((t.flags2 & 2) != 0); } catch (Exception) { } }
            }
            if (t.outfit != 255 && t.apOutfit != t.outfit && Time.unscaledTime - t.outfitTry > 1f)
            {
                t.outfitTry = Time.unscaledTime;
                try
                {
                    var oc = c.outfitController;
                    if (oc != null)
                    {
                        if ((int)oc.currentOutfit != t.outfit) { oc.SetCurrentOutfit((ClothesPreset.OutfitCategory)t.outfit, false, false, true); }
                        t.apOutfit = t.outfit;
                    }
                }
                catch (Exception) { }
            }
            if (t.apIdle != t.idle) { t.apIdle = t.idle; try { ac.SetIdleAnimationState((CitizenAnimationController.IdleAnimationState)t.idle); } catch (Exception) { } }
            if (t.apArms != t.arms) { t.apArms = t.arms; try { ac.SetArmsBoolState((CitizenAnimationController.ArmsBoolSate)t.arms); } catch (Exception) { } }
            if (t.apCarry != t.carryItem) { t.apCarry = t.carryItem; try { ac.SetCarryItemType(t.carryItem); } catch (Exception) { } }
            if (t.apCombat != t.combatArms) { t.apCombat = t.combatArms; try { ac.SetCombatArmsOverride(t.combatArms); } catch (Exception) { } }
            if (Mathf.Abs(t.speed - t.lastSpeed) > 0.01f)
            {
                t.lastSpeed = t.speed;
                try { c.currentNormalizedSpeed = t.speed; c.desiredNormalizedSpeed = t.speed; ac.UpdateMovementSpeed(); } catch (Exception) { }
            }
        }

        // ---------- Иконка второго игрока на карте ----------
        static Transform mapTracked;
        static Sprite mapSprite;
        static float lastMapTry; static bool mapFailLogged;

        static Sprite MakeMapSprite()
        {
            if (mapSprite != null) return mapSprite;
            int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    Color col = new Color(0f, 0f, 0f, 0f);
                    if (d <= 15.5f) col = d >= 12f ? new Color(0.25f, 0.25f, 0.25f, 1f) : new Color(1f, 1f, 1f, 1f);
                    px[y * n + x] = col;
                }
            tex.SetPixels(px); tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            mapSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return mapSprite;
        }

        static void MapUntrack()
        {
            try
            {
                if (mapTracked != null && MapController.Instance != null) MapController.Instance.RemoveTrackedObject(mapTracked);
            }
            catch (Exception) { }
            mapTracked = null;
        }

        static void MapTick()
        {
            if (Time.unscaledTime - lastMapTry < 1f) return;
            lastMapTry = Time.unscaledTime;
            for (int i = 0; i < players.Count; i++)
            {
                Enter(players[i]);
                try { MapTickOne(); } catch (Exception) { }
            }
            Leave();
        }

        static void MapTickOne()
        {
            if (!Plugin.CfgMapIcon.Value) { if (mapTracked != null) MapUntrack(); return; }
            if (avatar == null) { if (mapTracked != null) MapUntrack(); return; }
            var mc = MapController.Instance;
            if (mc == null) return;
            if (mapTracked != null && mapTracked == avatar.transform) return;
            if (mapTracked != null) MapUntrack();
            try
            {
                float sz = Mathf.Max(6f, Plugin.CfgMapIconSize.Value);
                mc.AddNewTrackedObject(avatar.transform, MakeMapSprite(), new Vector2(sz, sz), new Color(0.2f, 1f, 0.3f, 1f), true, null);
                mapTracked = avatar.transform;
                Plugin.L.LogInfo("[MAP] иконка второго игрока добавлена на карту");
            }
            catch (Exception e)
            {
                if (!mapFailLogged) { mapFailLogged = true; Plugin.L.LogWarning($"[MAP] не удалось добавить иконку: {e.Message}"); }
            }
        }

        // ---------- Мебель в квартирах ----------
        class FurnRec
        {
            public int room, angle, x, y, z, tries;
            public string preset = "", art = "", bm = "";
            public bool hasKey, hasOff;
            public Color c0, c1, c2, c3; public float grub;
            public Vector3 off;
            public System.Collections.Generic.List<int[]> covers = new System.Collections.Generic.List<int[]>();
        }

        static readonly System.Collections.Generic.List<FurnRec> furnRecs = new System.Collections.Generic.List<FurnRec>();
        static readonly System.Collections.Generic.List<FurnRec> furnPending = new System.Collections.Generic.List<FurnRec>();
        static bool inExecute; static float execAt;
        static string lastFurnKey = ""; static float lastFurnAt;
        static System.Collections.Generic.Dictionary<string, FurniturePreset> furnPresetCache;
        static System.Collections.Generic.Dictionary<string, ArtPreset> artPresetCache;

        static string FurnKey(FurnRec r) { return r.room + ":" + r.x + "," + r.y + "," + r.z + ":" + r.preset; }

        static FurniturePreset FindFurnPreset(string name)
        {
            if (furnPresetCache == null)
            {
                furnPresetCache = new System.Collections.Generic.Dictionary<string, FurniturePreset>();
                foreach (var p in Resources.FindObjectsOfTypeAll<FurniturePreset>())
                    if (p != null && !string.IsNullOrEmpty(p.name) && !furnPresetCache.ContainsKey(p.name)) furnPresetCache[p.name] = p;
                Plugin.L.LogInfo($"[FURN] пресетов мебели: {furnPresetCache.Count}");
            }
            FurniturePreset r; return furnPresetCache.TryGetValue(name ?? "", out r) ? r : null;
        }

        static ArtPreset FindArtPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (artPresetCache == null)
            {
                artPresetCache = new System.Collections.Generic.Dictionary<string, ArtPreset>();
                foreach (var p in Resources.FindObjectsOfTypeAll<ArtPreset>())
                    if (p != null && !string.IsNullOrEmpty(p.name) && !artPresetCache.ContainsKey(p.name)) artPresetCache[p.name] = p;
            }
            ArtPreset r; return artPresetCache.TryGetValue(name, out r) ? r : null;
        }

        static FurnRec MakeRec(NewRoom room, FurnitureLocation loc, PlayerApartmentController.FurniturePlacement pl)
        {
            NewNode an = null;
            try { an = loc != null ? loc.anchorNode : null; } catch (Exception) { }
            if (an == null && pl != null) { try { an = pl.anchorNode; } catch (Exception) { } }
            if (an == null) return null;
            if (room == null) { try { room = an.room; } catch (Exception) { } }
            if (room == null) return null;
            var rec = new FurnRec();
            rec.room = room.roomID;
            FurniturePreset fp = null;
            try { fp = loc != null ? loc.furniture : null; } catch (Exception) { }
            if (fp == null && pl != null) { try { fp = pl.preset; } catch (Exception) { } }
            if (fp == null) return null;
            rec.preset = fp.name ?? "";
            var c = an.nodeCoord; rec.x = c.x; rec.y = c.y; rec.z = c.z;
            try { rec.angle = loc != null ? loc.angle : pl.angle; } catch (Exception) { }
            Toolbox.MaterialKey key = null;
            try { key = loc != null ? loc.matKey : null; } catch (Exception) { }
            if (key == null && pl != null) { try { key = pl.materialKey; } catch (Exception) { } }
            if (key != null)
            {
                rec.hasKey = true;
                try { rec.bm = key.baseMaterial != null ? key.baseMaterial.name : ""; } catch (Exception) { }
                rec.c0 = key.mainColour; rec.c1 = key.colour1; rec.c2 = key.colour2; rec.c3 = key.colour3; rec.grub = key.grubiness;
            }
            try
            {
                ArtPreset ap = loc != null ? loc.art : null;
                if (ap == null && pl != null) ap = pl.art;
                if (ap != null) rec.art = ap.name ?? "";
            }
            catch (Exception) { }
            try { if (loc != null) { rec.off = loc.offset; rec.hasOff = true; } else if (pl != null) { rec.off = pl.offset; rec.hasOff = true; } } catch (Exception) { }
            try
            {
                var cov = loc != null ? loc.coversNodes : null;
                if (cov == null && pl != null) cov = pl.coversNodes;
                if (cov != null)
                    foreach (var n in cov) { if (n == null) continue; var cc = n.nodeCoord; rec.covers.Add(new[] { cc.x, cc.y, cc.z }); }
            }
            catch (Exception) { }
            return rec;
        }

        static void PutFurn(NetDataWriter w, FurnRec r)
        {
            w.Put(r.room); w.Put(r.preset); w.Put(r.angle); w.Put(r.x); w.Put(r.y); w.Put(r.z);
            w.Put(r.hasKey);
            if (r.hasKey) { w.Put(r.bm); PutColor(w, r.c0); PutColor(w, r.c1); PutColor(w, r.c2); PutColor(w, r.c3); w.Put(r.grub); }
            w.Put(r.art ?? "");
            w.Put(r.hasOff);
            if (r.hasOff) { w.Put(r.off.x); w.Put(r.off.y); w.Put(r.off.z); }
            w.Put(r.covers.Count);
            foreach (var c in r.covers) { w.Put(c[0]); w.Put(c[1]); w.Put(c[2]); }
        }

        static FurnRec GetFurn(NetDataReader r)
        {
            var f = new FurnRec();
            f.room = r.GetInt(); f.preset = r.GetString(); f.angle = r.GetInt(); f.x = r.GetInt(); f.y = r.GetInt(); f.z = r.GetInt();
            f.hasKey = r.GetBool();
            if (f.hasKey) { f.bm = r.GetString(); f.c0 = GetColor(r); f.c1 = GetColor(r); f.c2 = GetColor(r); f.c3 = GetColor(r); f.grub = r.GetFloat(); }
            f.art = r.GetString();
            f.hasOff = r.GetBool();
            if (f.hasOff) f.off = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            int n = r.GetInt();
            for (int i = 0; i < n; i++) f.covers.Add(new[] { r.GetInt(), r.GetInt(), r.GetInt() });
            return f;
        }

        internal static void BeginExecute()
        {
            inExecute = true; execAt = Time.unscaledTime;
            try
            {
                if (applyingRemote) return;
                var pac = PlayerApartmentController.Instance;
                var p = pac != null ? pac.furnPlacement : null;
                var ex = p != null ? p.existing : null;
                if (ex != null) OnFurnRemoved(ex, "переставил");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] begin: {e.Message}"); }
        }

        internal static void EndExecute() { inExecute = false; }

        internal static void OnFurnAdded(NewRoom room, FurnitureLocation loc, PlayerApartmentController.FurniturePlacement pl)
        {
            try
            {
                if (applyingRemote) return;
                if (!inExecute || Time.unscaledTime - execAt > 5f) return;
                var rec = MakeRec(room, loc, pl);
                if (rec == null) return;
                string k = FurnKey(rec);
                if (k == lastFurnKey && Time.unscaledTime - lastFurnAt < 1f) return; // две перегрузки AddFurnitureCustom подряд
                lastFurnKey = k; lastFurnAt = Time.unscaledTime;
                furnRecs.RemoveAll(x => FurnKey(x) == k);
                furnRecs.Add(rec);
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)17); PutFurn(w, rec);
                SendDoor(w);
                Plugin.L.LogInfo($"[FURN] я поставил '{rec.preset}': комната {rec.room}, узел ({rec.x},{rec.y},{rec.z}), угол {rec.angle}, занимает узлов {rec.covers.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] add: {e.Message}"); }
        }

        internal static void OnFurnRemoved(FurnitureLocation loc, string why)
        {
            try
            {
                if (applyingRemote || loc == null) return;
                var rec = MakeRec(null, loc, null);
                if (rec == null) return;
                string k = FurnKey(rec);
                furnRecs.RemoveAll(x => FurnKey(x) == k);
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)18);
                w.Put(rec.room); w.Put(rec.x); w.Put(rec.y); w.Put(rec.z); w.Put(rec.preset);
                SendDoor(w);
                Plugin.L.LogInfo($"[FURN] я {why} '{rec.preset}': комната {rec.room}, узел ({rec.x},{rec.y},{rec.z})");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] remove: {e.Message}"); }
        }

        static NewNode FindNodeAt(NewRoom room, int x, int y, int z)
        {
            try
            {
                foreach (var n in room.nodes)
                {
                    if (n == null) continue;
                    var c = n.nodeCoord;
                    if (c.x == x && c.y == y && c.z == z) return n;
                }
            }
            catch (Exception) { }
            try
            {
                NewNode nn;
                var map = PathFinder.Instance.nodeMap;
                if (map != null && map.TryGetValue(new Vector3(x, y, z), out nn) && nn != null) return nn;
            }
            catch (Exception) { }
            return null;
        }

        static FurnitureLocation FindFurnLoc(NewRoom room, int x, int y, int z, string preset)
        {
            try
            {
                foreach (var f in room.individualFurniture)
                    if (FurnMatch(f, x, y, z, preset)) return f;
            }
            catch (Exception) { }
            try
            {
                foreach (var cl in room.furniture)
                {
                    if (cl == null) continue;
                    foreach (var f in cl.clusterList)
                        if (FurnMatch(f, x, y, z, preset)) return f;
                }
            }
            catch (Exception) { }
            return null;
        }

        static bool FurnMatch(FurnitureLocation f, int x, int y, int z, string preset)
        {
            if (f == null) return false;
            try
            {
                var an = f.anchorNode; if (an == null) return false;
                var c = an.nodeCoord;
                if (c.x != x || c.y != y || c.z != z) return false;
                return f.furniture != null && f.furniture.name == preset;
            }
            catch (Exception) { return false; }
        }

        // true = обработано (применено или безнадёжно), false = попробовать позже
        static bool ApplyFurnAdd(FurnRec rec)
        {
            var room = RoomById(rec.room);
            if (room == null) return false;
            var node = FindNodeAt(room, rec.x, rec.y, rec.z);
            if (node == null) return false;
            var preset = FindFurnPreset(rec.preset);
            if (preset == null) { Plugin.L.LogWarning($"[FURN] нет пресета мебели '{rec.preset}'"); return true; }
            string k = FurnKey(rec);
            var existing = FindFurnLoc(room, rec.x, rec.y, rec.z, rec.preset);
            if (existing != null)
            {
                bool same = false;
                try { same = existing.angle == rec.angle; } catch (Exception) { }
                if (same) { furnRecs.RemoveAll(x => FurnKey(x) == k); furnRecs.Add(rec); return true; }
                RemoveFurnLoc(room, existing);
            }
            var pl = new PlayerApartmentController.FurniturePlacement();
            pl.preset = preset; pl.anchorNode = node; pl.angle = rec.angle;
            if (rec.hasKey)
            {
                var key = new Toolbox.MaterialKey();
                key.baseMaterial = FindMaterial(rec.bm);
                key.mainColour = rec.c0; key.colour1 = rec.c1; key.colour2 = rec.c2; key.colour3 = rec.c3; key.grubiness = rec.grub;
                pl.materialKey = key;
            }
            var art = FindArtPreset(rec.art);
            if (art != null) pl.art = art;
            if (rec.hasOff) pl.offset = rec.off;
            var cov = new Il2CppSystem.Collections.Generic.List<NewNode>();
            foreach (var c in rec.covers)
            {
                var n = FindNodeAt(room, c[0], c[1], c[2]);
                if (n != null) cov.Add(n);
            }
            if (cov.Count == 0) cov.Add(node);
            pl.coversNodes = cov;
            applyingRemote = true; applyingDoor = true;
            try
            {
                room.AddFurnitureCustom(pl);
                furnRecs.RemoveAll(x => FurnKey(x) == k);
                furnRecs.Add(rec);
                Plugin.L.LogInfo($"[FURN] второй игрок поставил '{rec.preset}': комната {rec.room}, узел ({rec.x},{rec.y},{rec.z}), угол {rec.angle}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] поставить '{rec.preset}': {e.Message}"); }
            finally { applyingRemote = false; applyingDoor = false; }
            return true;
        }

        static void RemoveFurnLoc(NewRoom room, FurnitureLocation loc)
        {
            bool prev1 = applyingRemote, prev2 = applyingDoor;
            applyingRemote = true; applyingDoor = true;
            try
            {
                loc.Delete(true, FurnitureClusterLocation.RemoveInteractablesOption.remove);
                try { if (room.individualFurniture.Contains(loc)) room.individualFurniture.Remove(loc); } catch (Exception) { }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] удалить: {e.Message}"); }
            finally { applyingRemote = prev1; applyingDoor = prev2; }
        }

        static bool ApplyFurnRemove(int rid, int x, int y, int z, string preset)
        {
            var room = RoomById(rid);
            if (room == null) return false;
            string k = rid + ":" + x + "," + y + "," + z + ":" + preset;
            furnRecs.RemoveAll(q => FurnKey(q) == k);
            var loc = FindFurnLoc(room, x, y, z, preset);
            if (loc == null) { Plugin.L.LogInfo($"[FURN] убрать '{preset}' (комната {rid}): у меня её уже нет"); return true; }
            RemoveFurnLoc(room, loc);
            Plugin.L.LogInfo($"[FURN] второй игрок убрал '{preset}': комната {rid}, узел ({x},{y},{z})");
            return true;
        }

        static void OnFurnMessage(byte kind, NetDataReader r)
        {
            if (kind == 17)
            {
                var rec = GetFurn(r);
                if (!ApplyFurnAdd(rec)) furnPending.Add(rec);
            }
            else if (kind == 18)
            {
                int rid = r.GetInt(); int x = r.GetInt(); int y = r.GetInt(); int z = r.GetInt(); string pn = r.GetString();
                furnPending.RemoveAll(q => q.room == rid && q.x == x && q.y == y && q.z == z && q.preset == pn);
                ApplyFurnRemove(rid, x, y, z, pn);
            }
            else if (kind == 19)
            {
                int n = r.GetInt();
                Plugin.L.LogInfo($"[FURN] получен снимок мебели: {n}");
                for (int i = 0; i < n; i++)
                {
                    var rec = GetFurn(r);
                    if (!ApplyFurnAdd(rec)) furnPending.Add(rec);
                }
            }
        }

        static void SendFurnSnapshot(NetPeer peer)
        {
            try
            {
                if (furnRecs.Count == 0) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)19); w.Put(furnRecs.Count);
                foreach (var f in furnRecs) PutFurn(w, f);
                if (peer != null) peer.Send(w, DeliveryMethod.ReliableOrdered); else manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[FURN] отправил снимок мебели: {furnRecs.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[FURN] снимок: {e.Message}"); }
        }

        static void FurnTick()
        {
            if (furnPending.Count == 0) return;
            var list = new System.Collections.Generic.List<FurnRec>(furnPending);
            foreach (var rec in list)
            {
                bool done = false;
                try { done = ApplyFurnAdd(rec); } catch (Exception) { }
                rec.tries++;
                if (done || rec.tries > 40) furnPending.Remove(rec);
            }
        }

        static void AptTick()
        {
            try { FurnTick(); } catch (Exception) { }
            try { MapTick(); } catch (Exception) { }
            if (soldPending.Count == 0) return;
            var done = new System.Collections.Generic.List<int>();
            foreach (var id in soldPending) { try { if (TryRemoveSaleNote(id)) done.Add(id); } catch (Exception) { } }
            foreach (var id in done) soldPending.Remove(id);
        }

        static void SendSoldSnapshot(NetPeer peer)
        {
            try
            {
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)15); w.Put(soldAddrs.Count);
                foreach (var id in soldAddrs)
                {
                    System.Collections.Generic.List<int[]> rems; soldRemovals.TryGetValue(id, out rems);
                    PutSale(w, id, rems);
                }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
            }
            catch (Exception) { }
        }

        static float lastAptTick;
        static float lastTimeSend, lastTimeLog;
        static bool timeLogged, applyingWeather;

        internal static bool ClientFollowsHostWeather()
        {
            return !IsHost && !applyingWeather && Plugin.CfgTimeSync.Value && manager != null && manager.ConnectedPeersCount > 0;
        }

        static void TimeTick()
        {
            if (!IsHost || !Plugin.CfgTimeSync.Value) return;
            if (manager == null || manager.ConnectedPeersCount == 0) return;
            if (Time.unscaledTime - lastTimeSend < 5f) return;
            lastTimeSend = Time.unscaledTime;
            SendTime();
        }

        static void SendTime()
        {
            try
            {
                var sd = SessionData.Instance;
                if (sd == null) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)13);
                w.Put(sd.gameTime); w.Put(sd.leapYearCycle); w.Put((byte)(int)SharedTimeSpeed(sd));
                w.Put(sd.desiredRain); w.Put(sd.desiredWind); w.Put(sd.desiredSnow); w.Put(sd.desiredLightning); w.Put(sd.desiredFog);
                SendDoor(w);
            }
            catch (Exception) { }
        }

        static void ApplyTime(float gt, int leap, int spd, float rain, float wind, float snow, float lit, float fog)
        {
            if (IsHost || !Plugin.CfgTimeSync.Value) return;
            try
            {
                var sd = SessionData.Instance;
                if (sd == null) return;
                float mine = sd.gameTime;
                if (!timeLogged || Time.unscaledTime - lastTimeLog > 30f)
                {
                    timeLogged = true; lastTimeLog = Time.unscaledTime;
                    Plugin.L.LogInfo($"[TIME] хост gameTime={gt:F4} у меня={mine:F4} скорость={spd}/{(int)sd.currentTimeSpeed} дождь={rain:F2} ветер={wind:F2} снег={snow:F2} молнии={lit:F2} туман={fog:F2}");
                }
                if (spd != (int)sd.currentTimeSpeed && !LocalKo())   // пока мы без сознания, скорость в игре своя (veryFast с множителем x1)
                {
                    applyingTimeSpeed = true;
                    try { sd.SetTimeSpeed((SessionData.TimeSpeed)spd); } finally { applyingTimeSpeed = false; }
                }
                if (Mathf.Abs(mine - gt) > 0.03f) sd.SetGameTime(gt, leap);
                if (Mathf.Abs(sd.desiredRain - rain) > 0.02f || Mathf.Abs(sd.desiredWind - wind) > 0.02f || Mathf.Abs(sd.desiredSnow - snow) > 0.02f
                    || Mathf.Abs(sd.desiredLightning - lit) > 0.02f || Mathf.Abs(sd.desiredFog - fog) > 0.02f)
                {
                    applyingWeather = true;
                    try { sd.SetWeather(rain, wind, snow, lit, fog, 0.1f, false); }
                    finally { applyingWeather = false; }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[TIME] {e.Message}"); }
        }

        class PendingWin { public Vector3 wp, cp, vel; public bool fx; public int tries; }
        static readonly System.Collections.Generic.List<PendingWin> pendingWins = new System.Collections.Generic.List<PendingWin>();
        static float lastWinTick;

        internal static void OnWindowBreak(BreakableWindowController bw, Vector3 cp, Vector3 vel)
        {
            try
            {
                if (applyingRemote || bw == null || !Plugin.CfgObjSync.Value) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                if (!bw.isBroken) return;
                Vector3 wp = bw.transform.position;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)11);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                w.Put(cp.x); w.Put(cp.y); w.Put(cp.z);
                w.Put(vel.x); w.Put(vel.y); w.Put(vel.z);
                SendDoor(w);
                Plugin.L.LogInfo($"[WINDOW] я разбил окно в {wp}");
            }
            catch (Exception) { }
        }

        // true = обработано (применено или уже разбито), false = окно не найдено (ещё не заспавнилось)
        static bool TryApplyWindow(Vector3 wp, Vector3 cp, Vector3 vel, bool fx)
        {
            BreakableWindowController found = null; float best = 0.5f;
            foreach (var bw in UnityEngine.Object.FindObjectsOfType<BreakableWindowController>())
            {
                if (bw == null) continue;
                float d = Vector3.Distance(bw.transform.position, wp);
                if (d < best) { best = d; found = bw; }
            }
            if (found == null) return false;
            if (found.isBroken) return true;
            applyingRemote = true; applyingDoor = true;
            try
            {
                if (fx) found.BreakWindow(cp, vel, null, false);
                else found.SetBroken(true);
                if (!found.isBroken) found.SetBroken(true);
                Plugin.L.LogInfo($"[WINDOW] разбил окно у себя ({best:F3} м), isBroken={found.isBroken}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[WINDOW] разбить: {e.Message}"); }
            finally { applyingRemote = false; applyingDoor = false; }
            return true;
        }

        static void ApplyWindowBreak(Vector3 wp, Vector3 cp, Vector3 vel, bool fx)
        {
            bool ok = false;
            try { ok = TryApplyWindow(wp, cp, vel, fx); } catch (Exception e) { Plugin.L.LogWarning($"[WINDOW] {e.Message}"); }
            if (!ok)
            {
                if (pendingWins.Count > 400) pendingWins.RemoveAt(0);
                pendingWins.Add(new PendingWin { wp = wp, cp = cp, vel = vel, fx = fx });
            }
        }

        static void WindowTick()
        {
            if (pendingWins.Count == 0) return;
            if (Time.unscaledTime - lastWinTick < 1f) return;
            lastWinTick = Time.unscaledTime;
            for (int i = pendingWins.Count - 1; i >= 0; i--)
            {
                var p = pendingWins[i];
                bool ok = false;
                try { ok = TryApplyWindow(p.wp, p.cp, p.vel, false); } catch (Exception) { }
                if (ok || ++p.tries > 600) pendingWins.RemoveAt(i);
            }
        }

        static void SendWindowSnapshot(NetPeer peer)
        {
            try
            {
                var list = new System.Collections.Generic.List<Vector3>();
                foreach (var bw in UnityEngine.Object.FindObjectsOfType<BreakableWindowController>())
                    if (bw != null && bw.isBroken) list.Add(bw.transform.position);
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)12); w.Put(list.Count);
                foreach (var v in list) { w.Put(v.x); w.Put(v.y); w.Put(v.z); }
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[WINDOW] отправил снимок разбитых окон: {list.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[WINDOW] снимок: {e.Message}"); }
        }

        static readonly System.Collections.Generic.HashSet<string> brokenKeys = new System.Collections.Generic.HashSet<string>();

        static string BreakKey(int hash, Vector3 wp)
        {
            return hash + ":" + Mathf.RoundToInt(wp.x * 20f) + ":" + Mathf.RoundToInt(wp.y * 20f) + ":" + Mathf.RoundToInt(wp.z * 20f);
        }

        // Разбилось стекло/объект мира, который не передаётся как общий предмет: ищем его по названию и позиции
        internal static void OnWorldBreak(InteractableController ic, int type, Vector3 p, Vector3 n, float m)
        {
            try
            {
                if (applyingDoor || applyingRemote || ic == null || !Plugin.CfgObjSync.Value) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                Interactable it = null;
                try { it = ic.interactable; } catch (Exception) { }
                if (it == null) it = FindByController(ic);
                string bname = it != null ? PresetNameOf(it) : ic.gameObject.name;
                Vector3 wp = it != null ? it.wPos : ic.transform.position;
                int hash = NameHash(bname);
                if (!brokenKeys.Add(BreakKey(hash, wp))) return;
                var w = new NetDataWriter();
                w.Put(MsgDoor); w.Put((byte)10); w.Put((byte)type); w.Put(hash);
                w.Put(wp.x); w.Put(wp.y); w.Put(wp.z);
                w.Put(p.x); w.Put(p.y); w.Put(p.z); w.Put(n.x); w.Put(n.y); w.Put(n.z); w.Put(m);
                SendDoor(w);
                Plugin.L.LogInfo($"[BREAK] я разбил '{bname}' ({(type == 0 ? "Shatter" : "BreakObject")}) в {wp}");
            }
            catch (Exception) { }
        }

        static void ApplyWorldBreak(int type, int hash, Vector3 wp, Vector3 p, Vector3 n, float m)
        {
            string key = BreakKey(hash, wp);
            if (!brokenKeys.Add(key)) return;
            InteractableController found = null; float best = 0.2f;
            try
            {
                foreach (var ic in UnityEngine.Object.FindObjectsOfType<InteractableController>())
                {
                    if (ic == null) continue;
                    Interactable ci = null;
                    try { ci = ic.interactable; } catch (Exception) { }
                    string nm = ci != null ? PresetNameOf(ci) : ic.gameObject.name;
                    if (NameHash(nm) != hash) continue;
                    float d = Vector3.Distance(ci != null ? ci.wPos : ic.transform.position, wp);
                    if (d < best) { best = d; found = ic; }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[BREAK] поиск: {e.Message}"); }
            if (found == null) { Plugin.L.LogWarning($"[BREAK] объект в {wp} не найден среди заспавненных (далеко от меня?)"); return; }
            applyingDoor = true; applyingRemote = true;
            try
            {
                if (type == 0) found.Shatter(p, m);
                else found.BreakObject(p, n, m, null);
                Plugin.L.LogInfo($"[BREAK] второй игрок разбил '{found.gameObject.name}' ({best:F3} м), повторяю у себя");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[BREAK] разбить: {e.Message}"); }
            finally { applyingDoor = false; applyingRemote = false; }
        }

        internal static void OnItemBroken(InteractableController ic, Vector3 p, Vector3 n, float m)
        {
            try
            {
                if (applyingRemote || ic == null || !Plugin.CfgObjSync.Value) return;
                Interactable it = null;
                try { it = ic.interactable; } catch (Exception) { }
                if (it == null) it = FindByController(ic);
                if (it != null && !sharedLocal.Contains(it.id)) { OnWorldBreak(ic, 1, p, n, m); return; }
                if (it == null || !sharedLocal.Contains(it.id)) return;
                if (manager == null || manager.ConnectedPeersCount == 0) return;
                sharedLocal.Remove(it.id);
                var w = new NetDataWriter();
                w.Put(MsgItemEv); w.Put((byte)5); w.Put(GidOf(it)); w.Put(PresetNameOf(it));
                w.Put(p.x); w.Put(p.y); w.Put(p.z); w.Put(n.x); w.Put(n.y); w.Put(n.z); w.Put(m);
                manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
                Plugin.Dbg($"[ITEM] предмет id={it.id} (общий id={GidOf(it)}) разбился, сообщаю второму игроку");
            }
            catch (Exception) { }
        }

        internal static void OnItemPickedUp(Interactable it)
        {
            if (!Plugin.CfgObjSync.Value) return;
            Plugin.Dbg($"[ITEM] я подобрал предмет id={it.id} (общий id={GidOf(it)})");
            SendItemEvent(1, it, false);
        }

        internal static void OnItemDropped(Interactable it)
        {
            if (!Plugin.CfgObjSync.Value) return;
            Plugin.Dbg($"[ITEM] я выбросил/поставил предмет id={it.id} (общий id={GidOf(it)})");
            SendItemEvent(2, it, true);
            pendingDrop = it;
            pendingDropUntil = Time.realtimeSinceStartup + 3f;
        }

        static System.Collections.Generic.Dictionary<int, GameObject> hiddenItems = new System.Collections.Generic.Dictionary<int, GameObject>();
        static float hiddenTickAt;

        // игра может заново заспавнить предмет (смена комнаты) и он снова станет видимым — прячем повторно,
        // пока другой игрок держит его в руках
        static void HiddenTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < hiddenTickAt || hiddenItems.Count == 0) return;
            hiddenTickAt = now + 0.5f;
            try
            {
                var keys = new System.Collections.Generic.List<int>(hiddenItems.Keys);
                foreach (var k in keys)
                {
                    var it = ResolveId(k);
                    if (it == null) continue;
                    GameObject go = null;
                    try { go = it.spawnedObject; } catch (Exception) { }
                    if (go == null) { try { if (it.controller != null) go = it.controller.gameObject; } catch (Exception) { } }
                    if (go != null) { if (go.activeSelf) go.SetActive(false); hiddenItems[k] = go; }
                }
            }
            catch (Exception) { }
        }

        static void OnItemEvent(byte kind, int id, string preset, Vector3 pos, Vector3 euler, ItemState st)
        {
            try
            {
                if (kind == 1)
                {
                    var it = ResolveChecked(id, preset, 0);
                    if (it == null) return;
                    sharedLocal.Add(it.id);
                    GameObject go = null;
                    try { go = it.spawnedObject; } catch (Exception) { }
                    if (go == null) { try { if (it.controller != null) go = it.controller.gameObject; } catch (Exception) { } }
                    if (go != null) { go.SetActive(false); hiddenItems[id] = go; Plugin.Dbg($"[ITEM] второй игрок подобрал предмет id={id}, скрываю"); }
                }
                else if (kind == 5)
                {
                    var it = ResolveChecked(id, preset, 0);
                    hiddenItems.Remove(id);
                    if (it == null) return;
                    sharedLocal.Remove(it.id);
                    GameObject bgo = null;
                    try { bgo = it.spawnedObject; } catch (Exception) { }
                    if (bgo != null && !bgo.activeSelf) bgo.SetActive(true);
                    applyingRemote = true;
                    try
                    {
                        bool done = false;
                        try
                        {
                            var ic = it.controller;
                            if (ic != null) { ic.BreakObject(pos, euler, st != null ? st.val : 3f, null); done = true; }
                        }
                        catch (Exception) { done = false; }
                        if (!done) it.SafeDelete(false);
                        Plugin.Dbg($"[ITEM] второй игрок разбил предмет id={id}, разбиваю свою копию (id={it.id}, разбито={done})");
                    }
                    finally { applyingRemote = false; }
                }
                else if (kind == 4)
                {
                    var it = ResolveChecked(id, preset, 0);
                    hiddenItems.Remove(id); idMap.Remove(id);
                    if (it == null) return;
                    sharedLocal.Remove(it.id);
                    applyingRemote = true;
                    try { it.SafeDelete(false); Plugin.Dbg($"[ITEM] второй игрок уничтожил/израсходовал предмет id={id}, удаляю свою копию (id={it.id})"); }
                    finally { applyingRemote = false; }
                }
                else if (kind == 2)
                {
                    GameObject hg;
                    if (hiddenItems.TryGetValue(id, out hg)) { hiddenItems.Remove(id); try { if (hg != null) hg.SetActive(true); } catch (Exception) { } }
                    var it = ResolveChecked(id, preset, 0);
                    if (it == null)
                    {
                        var pr = FindPreset(preset);
                        if (pr == null) { Plugin.L.LogWarning($"[ITEM] пресет '{preset}' не найден, предмет id={id} не создан"); return; }
                        it = InteractableCreator.Instance.CreateWorldInteractable(pr, null, null, null, pos, euler, null, null, "");
                        if (it != null) { idMap[id] = it; gidOf[it.id] = id; sharedLocal.Add(it.id); ApplyState(it, st); Plugin.Dbg($"[ITEM] создан предмет '{preset}' (у отправителя id={id}, у меня id={it.id})"); }
                        else Plugin.L.LogWarning($"[ITEM] не удалось создать '{preset}'");
                    }
                    else
                    {
                        try { it.MoveInteractable(pos, euler, false); } catch (Exception) { }
                        sharedLocal.Add(it.id);
                        ApplyState(it, st);
                        try
                        {
                            GameObject sgo = it.spawnedObject;
                            if (sgo == null && it.controller != null) sgo = it.controller.gameObject;
                            if (sgo != null)
                            {
                                if (!sgo.activeSelf) sgo.SetActive(true);
                                var rt = it.controller != null ? RootOf(it.controller) : sgo.transform;
                                rt.position = pos; rt.eulerAngles = euler;
                            }
                        }
                        catch (Exception) { }
                        Plugin.Dbg($"[ITEM] второй игрок выбросил предмет id={id} (у меня id={it.id})");
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[ITEM] приём: {e.Message}"); }
        }

        static Interactable FindById(int id)
        {
            Interactable it;
            if (idCache != null && idCache.TryGetValue(id, out it) && it != null) return it;
            if (Time.realtimeSinceStartup < idCacheAt && idCache != null) return null;
            idCacheAt = Time.realtimeSinceStartup + 5f;
            try
            {
                idCache = new System.Collections.Generic.Dictionary<int, Interactable>();
                var list = CityData.Instance.interactableDirectory;
                for (int i = 0; i < list.Count; i++)
                {
                    var x = list[i];
                    if (x != null) idCache[x.id] = x;
                }
                Plugin.Dbg($"[OBJ] индекс объектов мира: {idCache.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[OBJ] индекс: {e.Message}"); }
            return idCache.TryGetValue(id, out it) ? it : null;
        }

        static Interactable FindByController(InteractableController ic)
        {
            try
            {
                var list = CityData.Instance.interactableDirectory;
                for (int i = 0; i < list.Count; i++)
                {
                    var x = list[i];
                    if (x != null && x.controller != null && x.controller.Pointer == ic.Pointer) return x;
                }
            }
            catch (Exception) { }
            return null;
        }

        // Корень физического объекта: у Rigidbody свой трансформ, иначе трансформ контроллера
        static Transform RootOf(Component c)
        {
            Rigidbody rb = c.GetComponent<Rigidbody>();
            if (rb == null) rb = c.GetComponentInParent<Rigidbody>();
            return rb != null ? rb.transform : c.transform;
        }

        static void SendObject(byte flags)
        {
            try
            {
                var tr = RootOf(objTrackIc);
                var w = new NetDataWriter();
                w.Put(MsgObj);
                w.Put(objTrackId);
                w.Put(flags);
                w.Put(objTrackHash);
                Vector3 p = tr.position; Quaternion q = tr.rotation;
                w.Put(p.x); w.Put(p.y); w.Put(p.z);
                w.Put(q.x); w.Put(q.y); w.Put(q.z); w.Put(q.w);
                manager.SendToAll(w, (flags & 2) != 0 ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
            }
            catch (Exception e) { Plugin.L.LogWarning($"[OBJ] отправка: {e.Message}"); }
        }

        static void FinishObject()
        {
            if (objTrackIc != null && objTrackId >= 0) SendObject(2);
            if (objTrackIc != null) Plugin.Dbg($"[OBJ] объект id={objTrackId} отпущен, финальная поза отправлена");
            objTrackIc = null; objTrackIt = null; objTrackId = -1; objSettleUntil = 0f; objStillSince = -1f;
        }

        // Предмет, созданный во время игры (купленный, со склада), у второго игрока ещё не существует: создаём ему копию
        static void EnsureShared(Interactable it)
        {
            try
            {
                if (it == null || it.id < RuntimeIdStart || sharedLocal.Contains(it.id)) return;
                if (manager == null || manager.ConnectedPeersCount == 0 || !Plugin.CfgObjSync.Value) return;
                SendItemEvent(2, it, true);
                Plugin.Dbg($"[ITEM] новый предмет '{PresetNameOf(it)}' id={it.id} (общий {GidOf(it)}): отправил второму игроку для создания");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[ITEM] EnsureShared: {e.Message}"); }
        }

        static void LocalObjects(float dt)
        {
            objSendTimer += dt;
            if (objSendTimer < 0.066f) return;
            objSendTimer = 0f;
            float now = Time.realtimeSinceStartup;
            InteractableController cur = null;
            try { cur = InteractionController.Instance.carryingObject; } catch (Exception) { }
            if (cur == null && objTrackIc == null && pendingDrop != null)
            {
                try
                {
                    if (pendingDrop.controller != null)
                    {
                        objTrackIc = pendingDrop.controller; objTrackIt = pendingDrop; objTrackId = GidOf(pendingDrop); objTrackHash = NameHash(PresetNameOf(pendingDrop));
                        objSettleUntil = now + 8f; objStillSince = -1f; pendingDrop = null;
                    }
                    else if (now > pendingDropUntil) pendingDrop = null;
                }
                catch (Exception) { pendingDrop = null; }
            }
            if (cur != null)
            {
                if (objTrackIc == null || objTrackIc.Pointer != cur.Pointer)
                {
                    FinishObject();
                    objTrackIc = cur;
                    try { objTrackIt = cur.interactable; } catch (Exception) { objTrackIt = null; }
                    if (objTrackIt == null) objTrackIt = FindByController(cur);
                    objTrackId = objTrackIt != null ? GidOf(objTrackIt) : -1;
                    objTrackHash = NameHash(PresetNameOf(objTrackIt));
                    string pn = "";
                    try { pn = objTrackIt != null && objTrackIt.preset != null ? objTrackIt.preset.name : ""; } catch (Exception) { }
                    Plugin.Dbg($"[OBJ] несу объект id={objTrackId} пресет='{pn}'");
                    EnsureShared(objTrackIt);
                }
                objSettleUntil = 0f; objStillSince = -1f;
                if (objTrackId == 0 && objTrackIt != null) { objTrackId = GidOf(objTrackIt); if (objTrackId > 0) EnsureShared(objTrackIt); }
                if (objTrackId > 0) SendObject(1);
            }
            else if (objTrackIc != null)
            {
                if (objSettleUntil == 0f) { objSettleUntil = now + 8f; objStillSince = -1f; }
                if (objTrackId > 0) SendObject(0);
                bool still;
                try
                {
                    var rb = objTrackIc.GetComponent<Rigidbody>();
                    if (rb == null) rb = objTrackIc.GetComponentInParent<Rigidbody>();
                    still = rb == null || rb.IsSleeping() || rb.velocity.sqrMagnitude < 0.01f;
                }
                catch (Exception) { still = true; }
                if (still) { if (objStillSince < 0f) objStillSince = now; } else objStillSince = -1f;
                if ((objStillSince >= 0f && now - objStillSince > 0.6f) || now > objSettleUntil) FinishObject();
            }
        }

        class RObj { public Interactable it; public Transform tr; public Rigidbody rb; public Vector3 pos; public Quaternion rot; public float last; }
        static System.Collections.Generic.Dictionary<int, RObj> robjs = new System.Collections.Generic.Dictionary<int, RObj>();
        static System.Collections.Generic.HashSet<int> robjMissLogged = new System.Collections.Generic.HashSet<int>();

        static void ReleaseRemote(RObj o, bool exact)
        {
            try
            {
                if (o.tr != null && exact) { o.tr.position = o.pos; o.tr.rotation = o.rot; }
                if (o.rb != null) { o.rb.isKinematic = false; o.rb.velocity = Vector3.zero; o.rb.angularVelocity = Vector3.zero; }
            }
            catch (Exception) { }
        }

        static void OnRemoteObject(int id, byte flags, int phash, Vector3 pos, Quaternion rot)
        {
            try
            {
                RObj o;
                if (!robjs.TryGetValue(id, out o))
                {
                    var it = ResolveChecked(id, null, phash);
                    if (it == null)
                    {
                        if (robjMissLogged.Add(id)) Plugin.L.LogWarning($"[OBJ] объект id={id} не найден у меня");
                        return;
                    }
                    Transform ctr = null;
                    try { if (it.controller != null) ctr = RootOf(it.controller); } catch (Exception) { }
                    if (ctr == null) { try { if (it.spawnedObject != null) ctr = it.spawnedObject.transform; } catch (Exception) { } }
                    if (ctr == null)
                    {
                        // Объект сейчас не заспавнен (далеко от меня): запоминаем позицию в данных, мир применит её при спавне
                        if ((flags & 2) != 0) { try { it.MoveInteractable(pos, rot.eulerAngles, false); } catch (Exception) { } }
                        return;
                    }
                    o = new RObj { it = it, tr = ctr, rb = ctr.GetComponent<Rigidbody>() };
                    robjs[id] = o;
                    string pn = "";
                    try { pn = it.preset != null ? it.preset.name : ""; } catch (Exception) { }
                    Plugin.Dbg($"[OBJ] второй игрок взял объект id={id} пресет='{pn}'");
                }
                o.pos = pos; o.rot = rot; o.last = Time.realtimeSinceStartup;
                if ((flags & 2) != 0)
                {
                    ReleaseRemote(o, true);
                    try { o.it.MoveInteractable(pos, rot.eulerAngles, false); } catch (Exception) { }
                    robjs.Remove(id);
                    Plugin.Dbg($"[OBJ] объект id={id} положен/брошен вторым игроком");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[OBJ] приём: {e.Message}"); }
        }

        static void RemoteObjectsTick(float dt)
        {
            if (robjs.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            System.Collections.Generic.List<int> done = null;
            float k = 1f - Mathf.Exp(-18f * dt);
            foreach (var kv in robjs)
            {
                var o = kv.Value;
                try
                {
                    if (o.tr == null || now - o.last > 2.5f)
                    {
                        ReleaseRemote(o, false);
                        if (done == null) done = new System.Collections.Generic.List<int>();
                        done.Add(kv.Key);
                        continue;
                    }
                    if (o.rb != null && !o.rb.isKinematic) o.rb.isKinematic = true;
                    o.tr.position = Vector3.Lerp(o.tr.position, o.pos, k);
                    o.tr.rotation = Quaternion.Slerp(o.tr.rotation, o.rot, k);
                }
                catch (Exception) { if (done == null) done = new System.Collections.Generic.List<int>(); done.Add(kv.Key); }
            }
            if (done != null) foreach (var id in done) robjs.Remove(id);
        }

        // ---------- Предметы в руках ----------
        class HeldPart { public string mesh; public string[] mats; public byte side; public float u = 1f; public float[] chain = new float[0]; }
        static System.Collections.Generic.List<HeldPart> remoteHeld = new System.Collections.Generic.List<HeldPart>();
        static bool remoteHeldDirty;
        static string remoteHeldSig = "";
        static string heldFor = "";
        static GameObject heldL, heldR;
        static float heldScanAt, heldResendAt;
        static string heldSentSig = "";
        static float meshMissAt;
        static float heldU = 1f;
        static string handNameR = "", handNameL = "";
        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Mesh>> meshCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Mesh>>();
        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Material>> matCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Material>>();

        static string CleanName(string n)
        {
            if (n == null) return "";
            n = n.Replace(" (Instance)", "").Replace("(Clone)", "").Trim();
            return n;
        }

        static void ScanHeld(System.Collections.Generic.List<HeldPart> res)
        {
            try
            {
                var fp = FirstPersonItemController.Instance;
                if (fp == null || fp.forceHolstered) return;
                string nm = curItem ?? "";
                if (nm == "" || nm == "nothing" || nm.IndexOf("fist", StringComparison.OrdinalIgnoreCase) >= 0) return;
                for (byte side = 0; side < 2; side++)
                {
                    Transform par = side == 0 ? fp.rightHandObjectParent : fp.leftHandObjectParent;
                    if (par == null) continue;
                    Vector3 pls = par.lossyScale;
                    foreach (var mf in par.GetComponentsInChildren<MeshFilter>(false))
                    {
                        if (mf == null || mf.sharedMesh == null || string.IsNullOrEmpty(mf.sharedMesh.name)) continue;
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mr == null || !mr.enabled) continue;
                        var tr = mf.transform;
                        var sm = mr.sharedMaterials;
                        var names = new string[sm.Length];
                        for (int i = 0; i < sm.Length; i++) names[i] = sm[i] != null ? CleanName(sm[i].name) + "\t" + TexName(sm[i]) : "\t";
                        // Цепочка трансформов от руки (par) до меша: локальные позиция/поворот/масштаб каждого узла,
                        // так форма предмета восстанавливается точно (даже при неравномерных масштабах родителей)
                        var nodes = new System.Collections.Generic.List<Transform>();
                        Transform t = tr;
                        while (t != null && t != par && nodes.Count < 10) { nodes.Add(t); t = t.parent; }
                        if (t != par) continue;
                        nodes.Reverse();
                        var chain = new float[nodes.Count * 10];
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var n = nodes[i];
                            var lp = n.localPosition; var lr = n.localRotation; var lsc = n.localScale;
                            int o = i * 10;
                            chain[o] = lp.x; chain[o + 1] = lp.y; chain[o + 2] = lp.z;
                            chain[o + 3] = lr.x; chain[o + 4] = lr.y; chain[o + 5] = lr.z; chain[o + 6] = lr.w;
                            chain[o + 7] = lsc.x; chain[o + 8] = lsc.y; chain[o + 9] = lsc.z;
                        }
                        var part = new HeldPart();
                        part.mesh = mf.sharedMesh.name + "\t" + mf.sharedMesh.vertexCount;
                        part.mats = names;
                        part.side = side;
                        // Масштаб самой руки (в игре он крошечный, а узлы предмета внутри огромные) вносим в первый узел:
                        // так итоговый размер в метрах точный и не зависит от порогов
                        float u = Mathf.Pow(Mathf.Abs(pls.x * pls.y * pls.z), 1f / 3f);
                        if (u > 0f && !float.IsNaN(u) && !float.IsInfinity(u) && chain.Length >= 10)
                        {
                            chain[0] *= u; chain[1] *= u; chain[2] *= u;
                            chain[7] *= u; chain[8] *= u; chain[9] *= u;
                        }
                        part.u = 1f;
                        part.chain = chain;
                        res.Add(part);
                        if (res.Count >= 12) return;
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HELD] скан: {e.Message}"); }
        }

        static string HeldSig(System.Collections.Generic.List<HeldPart> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                sb.Append(p.mesh).Append('|').Append(p.side).Append('|').Append(string.Join(",", p.mats)).Append(';');
            }
            return sb.ToString();
        }

        // Раз в 0.5 с смотрим, что у нас в руках (объекты появляются после смены предмета) и шлём при изменении
        static void SendHeldIfChanged()
        {
            if (Time.realtimeSinceStartup < heldScanAt) return;
            try { var fpc0 = FirstPersonItemController.Instance; if (fpc0 != null && fpc0.isConsuming) return; } catch (Exception) { }
            heldScanAt = Time.realtimeSinceStartup + 0.5f;
            var parts = new System.Collections.Generic.List<HeldPart>();
            ScanHeld(parts);
            SendWeaponIfChanged();
            string sig = HeldSig(parts);
            if (sig == heldSentSig && Time.realtimeSinceStartup < heldResendAt) return;
            if (sig != heldSentSig)
                Plugin.Dbg($"[HELD] у меня в руках: {curItem}, частей={parts.Count} {(parts.Count > 0 ? "первая: меш=" + parts[0].mesh + " материал=" + string.Join("/", parts[0].mats) + " масштаб узла0=" + (parts[0].chain.Length >= 10 ? parts[0].chain[7].ToString("G4") : "-") + " узлов=" + (parts[0].chain.Length / 10) : "")}");
            heldSentSig = sig;
            heldResendAt = Time.realtimeSinceStartup + 5f;
            var w = new NetDataWriter();
            w.Put(MsgHeld);
            w.Put(parts.Count);
            foreach (var p in parts)
            {
                w.Put(p.mesh ?? "");
                w.Put(p.side);
                w.Put(p.mats.Length);
                foreach (var m in p.mats) w.Put(m ?? "");
                w.Put(p.u);
                w.Put(p.chain.Length / 10);
                foreach (var f in p.chain) w.Put(f);
            }
            manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        // Имя основной текстуры материала: различает материалы с одинаковым именем
        static string TexName(Material m)
        {
            try
            {
                Texture t = null;
                if (m.HasProperty("_BaseColorMap")) t = m.GetTexture("_BaseColorMap");
                if (t == null && m.HasProperty("_MainTex")) t = m.GetTexture("_MainTex");
                if (t == null && m.HasProperty("_UnlitColorMap")) t = m.GetTexture("_UnlitColorMap");
                return t != null ? t.name : "";
            }
            catch (Exception) { return ""; }
        }

        static Mesh PickMesh(string name, int verts)
        {
            System.Collections.Generic.List<Mesh> l;
            if (!meshCache.TryGetValue(name, out l) || l.Count == 0) return null;
            foreach (var m in l) if (m != null && m.vertexCount == verts) return m;
            Plugin.L.LogWarning($"[HELD] меш '{name}': вариантов={l.Count}, с {verts} вершинами нет, беру первый");
            return l[0];
        }

        static Material PickMat(string name, string tex)
        {
            System.Collections.Generic.List<Material> l;
            if (!matCache.TryGetValue(name, out l) || l.Count == 0) return null;
            foreach (var m in l) if (m != null && TexName(m) == tex) return m;
            Plugin.L.LogWarning($"[HELD] материал '{name}': вариантов={l.Count}, с текстурой '{tex}' нет, беру первый");
            return l[0];
        }

        static void RebuildCaches()
        {
            try
            {
                meshCache.Clear(); matCache.Clear();
                foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                {
                    if (m == null || string.IsNullOrEmpty(m.name)) continue;
                    System.Collections.Generic.List<Mesh> l;
                    if (!meshCache.TryGetValue(m.name, out l)) { l = new System.Collections.Generic.List<Mesh>(); meshCache[m.name] = l; }
                    l.Add(m);
                }
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || string.IsNullOrEmpty(m.name)) continue;
                    string cn = CleanName(m.name);
                    System.Collections.Generic.List<Material> l;
                    if (!matCache.TryGetValue(cn, out l)) { l = new System.Collections.Generic.List<Material>(); matCache[cn] = l; }
                    l.Add(m);
                }
                Plugin.Dbg($"[HELD] кэш: мешей={meshCache.Count} материалов={matCache.Count}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[HELD] кэш: {e.Message}"); }
        }

        static void ClearHeld()
        {
            if (heldL != null) UnityEngine.Object.Destroy(heldL);
            if (heldR != null) UnityEngine.Object.Destroy(heldR);
            heldL = heldR = null; heldWeaponMode = false;
        }

        static GameObject Container(Transform bone)
        {
            var go = new GameObject("HeldItem");
            go.layer = bone.gameObject.layer;
            go.transform.SetParent(bone, false);
            return go;
        }

        static bool heldWeaponMode;
        static System.Collections.Generic.Dictionary<string, InteractablePreset> weaponItemPreset;

        static void SetLayerRec(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRec(t.GetChild(i), layer);
        }

        // Оружие в руке: модель из данных игры (как её держат NPC), а не из вида от первого лица
        static Transform FindHandDesc(Transform t, int depth)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                string n = c.name.ToLowerInvariant();
                if (n.Contains("hand") && !n.Contains("finger") && !n.Contains("thumb") && !n.Contains("index") && !n.Contains("pinky")) return c;
            }
            if (depth > 0)
                for (int i = 0; i < t.childCount; i++) { var r = FindHandDesc(t.GetChild(i), depth - 1); if (r != null) return r; }
            return null;
        }

        static void DumpTree(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(t.name).Append('(').Append(t.localPosition.ToString("F2")).Append(')');
            if (depth <= 0 || t.childCount == 0) return;
            sb.Append('[');
            for (int i = 0; i < t.childCount && i < 6; i++) { if (i > 0) sb.Append(", "); DumpTree(t.GetChild(i), depth - 1, sb); }
            sb.Append(']');
        }

        static bool handLogged;

        // Кисть, а не предплечье: у предплечья начало в локте, оружие торчало оттуда
        static Transform FindHandBone(bool left)
        {
            string hn = left ? handNameL : handNameR;
            Transform t = hn != "" ? FindDeep(avatarModel, hn) : null;
            Transform arm = FindDeep(avatarModel, left ? "LowerArmLeft" : "LowerArmRight");
            if (t == null) t = arm;
            if (t == null) return null;
            string ln = t.name.ToLowerInvariant();
            if (!ln.Contains("hand"))
            {
                var h = FindHandDesc(t, 3);
                if (h == null && t.childCount > 0) h = t.GetChild(0);
                if (h != null) t = h;
            }
            if (!handLogged)
            {
                handLogged = true;
                var sb = new StringBuilder();
                if (arm != null) DumpTree(arm, 3, sb);
                Plugin.L.LogInfo($"[WEAPON] кость руки: '{t.name}' (сустав NPC '{hn}'); дерево предплечья: {sb}");
            }
            return t;
        }

        static bool BuildWeaponModel()
        {
            try
            {
                var wp = FindWeapon(remoteWeaponName);
                if (wp == null) return false;
                GameObject pf = null; Vector3 lp = Vector3.zero, le = Vector3.zero; bool left = false; string src = "";
                if (wp.itemRightOverride != null) { pf = wp.itemRightOverride; lp = wp.itemRightLocalPos; le = wp.itemRightLocalEuler; src = "itemRightOverride"; }
                else if (wp.itemLeftOverride != null) { pf = wp.itemLeftOverride; lp = wp.itemLeftLocalPos; le = wp.itemLeftLocalEuler; left = true; src = "itemLeftOverride"; }
                else
                {
                    if (weaponItemPreset == null)
                    {
                        weaponItemPreset = new System.Collections.Generic.Dictionary<string, InteractablePreset>();
                        foreach (var ip in Resources.FindObjectsOfTypeAll<InteractablePreset>())
                            if (ip != null && ip.weapon != null && ip.prefab != null && !weaponItemPreset.ContainsKey(ip.weapon.name)) weaponItemPreset[ip.weapon.name] = ip;
                    }
                    InteractablePreset ipr;
                    if (weaponItemPreset.TryGetValue(wp.name, out ipr)) { pf = ipr.prefab; src = "модель предмета " + ipr.name; }
                }
                if (pf == null) { Plugin.L.LogWarning("[WEAPON] нет модели для '" + wp.name + "', показываю как есть"); return false; }
                Transform bone = FindHandBone(left);
                if (bone == null) return false;
                var cont = Container(bone);
                if (left) heldL = cont; else heldR = cont;
                var inst = UnityEngine.Object.Instantiate<GameObject>(pf);
                inst.name = "WeaponModel";
                inst.transform.SetParent(cont.transform, false);
                inst.transform.localPosition = lp;
                inst.transform.localRotation = Quaternion.Euler(le);
                inst.transform.localScale = Vector3.one;
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
                foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.Destroy(rb);
                SetLayerRec(cont.transform, bone.gameObject.layer);
                inst.SetActive(true);
                heldU = 1f;
                heldWeaponMode = true;
                Plugin.L.LogInfo($"[WEAPON] модель '{wp.name}' в руке аватара: источник {src}, кость {bone.name}, локальная поза {lp} / {le}, масштаб кости {bone.lossyScale}");
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning("[WEAPON] модель в руке: " + e.Message); ClearHeld(); return false; }
        }

        static void BuildHeld()
        {
            ClearHeld();
            if (avatarModel == null) return;
            if (remoteWeaponName != "" && BuildWeaponModel()) return;
            if (remoteHeld.Count == 0) return;
            if (meshCache.Count == 0) RebuildCaches();
            var bl = FindHandBone(true);
            var br = FindHandBone(false);
            string hand = (Plugin.CfgHeldHand.Value ?? "Auto").Trim().ToLowerInvariant();
            int made = 0;
            foreach (var p in remoteHeld)
            {
                string mname = p.mesh; int verts = -1;
                int tab = p.mesh.IndexOf('\t');
                if (tab >= 0) { mname = p.mesh.Substring(0, tab); int.TryParse(p.mesh.Substring(tab + 1), out verts); }
                Mesh mesh = PickMesh(mname, verts);
                if (mesh == null && Time.realtimeSinceStartup > meshMissAt)
                {
                    meshMissAt = Time.realtimeSinceStartup + 5f;
                    RebuildCaches();
                    mesh = PickMesh(mname, verts);
                }
                if (mesh == null) { Plugin.L.LogWarning($"[HELD] меш '{mname}' не найден"); continue; }
                bool left = hand == "left" || (hand == "auto" && p.side == 1);
                Transform bone = left ? bl : br;
                if (bone == null) continue;
                GameObject cont = left ? heldL : heldR;
                if (cont == null) { cont = Container(bone); if (left) heldL = cont; else heldR = cont; }
                heldU = 1f;
                Transform parent = cont.transform;
                GameObject go = null;
                for (int ci = 0; ci + 9 < p.chain.Length; ci += 10)
                {
                    go = new GameObject("n");
                    go.layer = cont.layer;
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = new Vector3(p.chain[ci], p.chain[ci + 1], p.chain[ci + 2]);
                    go.transform.localRotation = new Quaternion(p.chain[ci + 3], p.chain[ci + 4], p.chain[ci + 5], p.chain[ci + 6]);
                    go.transform.localScale = new Vector3(p.chain[ci + 7], p.chain[ci + 8], p.chain[ci + 9]);
                    parent = go.transform;
                }
                if (go == null) continue;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[p.mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    string mn = p.mats[i] ?? ""; string tn = "";
                    int mt = mn.IndexOf('\t');
                    if (mt >= 0) { tn = mn.Substring(mt + 1); mn = mn.Substring(0, mt); }
                    Material m = PickMat(CleanName(mn), tn);
                    if (m == null) Plugin.L.LogWarning($"[HELD] материал '{mn}' не найден");
                    mats[i] = m;
                }
                mr.sharedMaterials = new Il2CppReferenceArray<Material>(mats);
                made++;
            }
            Plugin.Dbg($"[HELD] у второго игрока собрано частей: {made} из {remoteHeld.Count}");
        }

        static void UpdateHeld()
        {
            if (avatarModel == null) return;
            if (remoteHeldDirty || (heldL == null && heldR == null && (remoteHeld.Count > 0 || remoteWeaponName != "") && Plugin.CfgHeldShow.Value && Time.realtimeSinceStartup > meshMissAt))
            {
                remoteHeldDirty = false;
                meshMissAt = Math.Max(meshMissAt, Time.realtimeSinceStartup + 2f);
                if (Plugin.CfgHeldShow.Value) BuildHeld(); else ClearHeld();
            }
            if (heldL == null && heldR == null) return;
            if (heldWeaponMode)
            {
                ApplyHeld(heldR ?? heldL, ParseV3(Plugin.CfgWpnPos.Value, Vector3.zero), Quaternion.Euler(ParseV3(Plugin.CfgWpnRot.Value, Vector3.zero)), Plugin.CfgWpnScale.Value);
                return;
            }
            Vector3 pos = ParseV3(Plugin.CfgHeldPos.Value, Vector3.zero);
            Quaternion rot = Quaternion.Euler(ParseV3(Plugin.CfgHeldRot.Value, Vector3.zero));
            float sc = Plugin.CfgHeldScale.Value;
            ApplyHeld(heldL, pos, rot, sc);
            ApplyHeld(heldR, pos, rot, sc);
        }

        // Контейнер компенсирует масштаб кости: части предмета заданы в реальных метрах
        static void ApplyHeld(GameObject go, Vector3 pos, Quaternion rot, float sc)
        {
            if (go == null) return;
            var t = go.transform;
            Vector3 bs = t.parent != null ? t.parent.lossyScale : Vector3.one;
            Vector3 inv = new Vector3(bs.x != 0f ? 1f / bs.x : 1f, bs.y != 0f ? 1f / bs.y : 1f, bs.z != 0f ? 1f / bs.z : 1f);
            t.localPosition = Vector3.Scale(pos, inv);
            t.localRotation = rot;
            t.localScale = inv * (sc * heldU);
        }

        static Vector3 ParseV3(string str, Vector3 def)
        {
            try
            {
                var p = (str ?? "").Split(',');
                if (p.Length != 3) return def;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                return new Vector3(float.Parse(p[0].Trim(), ci), float.Parse(p[1].Trim(), ci), float.Parse(p[2].Trim(), ci));
            }
            catch (Exception) { return def; }
        }

        // ---------- Позы по имени состояния аниматора ----------
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
            catch (Exception) { }
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
            catch (Exception) { }
            return Plugin.CfgConsumeDefault.Value ?? "";
        }

        // ---------- Анимация удара / падения / смерти аватара ----------
        static bool fxDead; static float fxDeadUntil, fxHitAt; static int fxLog;

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
                if (fxLog++ < 12) Plugin.Dbg($"[FX] отправил: {(kind == 1 ? "меня ударили" : kind == 2 ? "я упал" : kind == 3 ? "я встал" : "я умер")}");
            }
            catch (Exception) { }
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
                else if (kind == 3)
                {
                    fxDead = false;
                    an.SetBool("isDead", false);
                    an.SetTrigger("ragdollTransitionFrom");
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
                try { if (avatarAnim != null) avatarAnim.SetBool("isDead", false); } catch (Exception) { }
            }
            if (fxDead) return;
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
            else if (remoteCrouched)
            {
                string c = Plugin.CfgCrouchIdleState.Value ?? "";
                string cw = Plugin.CfgCrouchWalkState.Value ?? "";
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
                // Если контроллер сам увёл аниматор из нашего состояния, возвращаем
                try
                {
                    if (avatarAnim.GetCurrentAnimatorStateInfo(layer).shortNameHash != Animator.StringToHash(want) && !avatarAnim.IsInTransition(layer))
                    {
                        avatarAnim.CrossFade(Animator.StringToHash(want), fade, layer);
                        poseKick = Time.realtimeSinceStartup + 0.3f;
                    }
                }
                catch (Exception) { }
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
                boneUL = boneUR = boneLL = boneLR = boneTorso = null;
                remoteLean = leanSmooth = 0f;
                remoteBlocking = false;
                remoteCrouched = remoteRunning = remoteFists = false;
                armsW = 0f;
                crouchK = 0f; crouchBoneK = crouchAnimK = 0f;
                Plugin.L.LogInfo("[NET] Аватар второго игрока удалён");
            }
        }

        // ---------- игроки: добавление, удаление, ретрансляция через хост ----------
        static RP NewRp(int pid)
        {
            var rp = new RP(); rp.pid = pid; rp.label = "P" + pid;
            players.Add(rp);
            return rp;
        }

        static int FreePid()
        {
            for (int i = 1; i < 8; i++) if (RpByPid(i) == null) return i;
            return 7;
        }

        // ---------- Профили игроков: деньги, соцрейтинг, здоровье, статусы, инвентарь, квартиры ----------
        public sealed class ProfPv { public int type; public float value; public string str = ""; }
        public sealed class ProfItem
        {
            public string preset = ""; public int slot;
            public bool hasState; public float val, cs;   // сколько осталось выпить/съесть, патроны и прочие переменные предмета
            public System.Collections.Generic.List<ProfPv> pv = new System.Collections.Generic.List<ProfPv>();
        }
        public sealed class ProfApt { public int id; public System.Collections.Generic.List<int[]> rems = new System.Collections.Generic.List<int[]>(); }
        public sealed class ProfCode { public int type, id; public System.Collections.Generic.List<int> digits = new System.Collections.Generic.List<int>(); }
        public sealed class ProfDisk { public string upgrade = ""; public int state, list, level, objId, uninstallCost; }
        public sealed class ProfileData
        {
            public string id = ""; public string saved = ""; public bool valid;
            public int version;   // 2: есть позиция, ключи, отмычки, коды, синк-диски (у старых профилей этих данных нет, их не трогаем)
            public int money; public int social;
            public float hp = -1f, maxHp = -1f, nerve = -1f, maxNerve = -1f;
            public System.Collections.Generic.Dictionary<string, float> stats = new System.Collections.Generic.Dictionary<string, float>();
            public System.Collections.Generic.List<ProfItem> items = new System.Collections.Generic.List<ProfItem>();
            public System.Collections.Generic.List<ProfApt> apts = new System.Collections.Generic.List<ProfApt>();
            public int residence = -1;   // адрес текущего жилья игрока
            public bool hasPos; public float px, py, pz; public int nx, ny, nz;
            public int lockpicks = -1;
            public System.Collections.Generic.List<int> doorKeys = new System.Collections.Generic.List<int>();
            public System.Collections.Generic.List<int> itemKeys = new System.Collections.Generic.List<int>();
            public System.Collections.Generic.List<ProfCode> codes = new System.Collections.Generic.List<ProfCode>();
            public System.Collections.Generic.List<int> knowsPw = new System.Collections.Generic.List<int>();
            public System.Collections.Generic.List<ProfDisk> disks = new System.Collections.Generic.List<ProfDisk>();
        }

        static bool profileApplying, helloOkFlag, profDirty, profErrLogged;
        static int profState;   // клиент: 0 - ещё не запрашивал, 1 - ждёт ответа хоста, 2 - профиль применён, можно сохранять
        static float profReadyAt, profReqAt, profSendAt, profWriteAt;
        static string profLastSent = "", profFile = "", myProfileId;
        static RP doorSender;
        static readonly System.Collections.Generic.Dictionary<string, ProfileData> profiles = new System.Collections.Generic.Dictionary<string, ProfileData>();
        static readonly JsonSerializerOptions ProfJson = new JsonSerializerOptions { IncludeFields = true };

        static readonly string[] StatNames = { "nourishment", "hydration", "alertness", "energy", "hygiene", "heat", "drunk", "sick", "headache", "wet", "brokenLeg", "bruised", "blackEye", "blackedOut", "numb", "poisoned", "bleeding", "wellRested", "starchAddiction", "syncDiskInstall", "blinded" };
        static readonly string[] BadStatNames = { "drunk", "sick", "headache", "wet", "brokenLeg", "bruised", "blackEye", "blackedOut", "numb", "poisoned", "bleeding", "starchAddiction", "syncDiskInstall", "blinded" };

        static float GetStat(Player p, string n)
        {
            switch (n)
            {
                case "nourishment": return p.nourishment;
                case "hydration": return p.hydration;
                case "alertness": return p.alertness;
                case "energy": return p.energy;
                case "hygiene": return p.hygiene;
                case "heat": return p.heat;
                case "drunk": return p.drunk;
                case "sick": return p.sick;
                case "headache": return p.headache;
                case "wet": return p.wet;
                case "brokenLeg": return p.brokenLeg;
                case "bruised": return p.bruised;
                case "blackEye": return p.blackEye;
                case "blackedOut": return p.blackedOut;
                case "numb": return p.numb;
                case "poisoned": return p.poisoned;
                case "bleeding": return p.bleeding;
                case "wellRested": return p.wellRested;
                case "starchAddiction": return p.starchAddiction;
                case "syncDiskInstall": return p.syncDiskInstall;
                case "blinded": return p.blinded;
            }
            return 0f;
        }

        static void SetStat(Player p, string n, float v)
        {
            switch (n)
            {
                case "nourishment": p.nourishment = v; break;
                case "hydration": p.hydration = v; break;
                case "alertness": p.alertness = v; break;
                case "energy": p.energy = v; break;
                case "hygiene": p.hygiene = v; break;
                case "heat": p.heat = v; break;
                case "drunk": p.drunk = v; break;
                case "sick": p.sick = v; break;
                case "headache": p.headache = v; break;
                case "wet": p.wet = v; break;
                case "brokenLeg": p.brokenLeg = v; break;
                case "bruised": p.bruised = v; break;
                case "blackEye": p.blackEye = v; break;
                case "blackedOut": p.blackedOut = v; break;
                case "numb": p.numb = v; break;
                case "poisoned": p.poisoned = v; break;
                case "bleeding": p.bleeding = v; break;
                case "wellRested": p.wellRested = v; break;
                case "starchAddiction": p.starchAddiction = v; break;
                case "syncDiskInstall": p.syncDiskInstall = v; break;
                case "blinded": p.blinded = v; break;
            }
        }

        // постоянный номер игрока (не меняется между запусками): по нему хост находит его профиль
        static string MyProfileId()
        {
            if (myProfileId != null) return myProfileId;
            try
            {
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "SodCoop_playerid.txt");
                string v = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
                if (v.Length < 8) { v = Guid.NewGuid().ToString("N"); File.WriteAllText(path, v); Plugin.L.LogInfo("[PROFILE] создан постоянный номер игрока: " + v); }
                myProfileId = v;
            }
            catch (Exception e) { myProfileId = Guid.NewGuid().ToString("N"); Plugin.L.LogWarning("[PROFILE] не смог сохранить номер игрока: " + e.Message); }
            return myProfileId;
        }

        const System.Reflection.BindingFlags GcFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;

        static object GcInst(out Type t)
        {
            t = FindType("GameplayController");
            if (t == null) return null;
            var ip = t.GetProperty("Instance", GcFlags);
            return ip != null ? ip.GetValue(null, null) : null;
        }

        static double GcGet(string prop)
        {
            try
            {
                Type t; var inst = GcInst(out t);
                if (inst == null) return 0;
                var pr = t.GetProperty(prop, GcFlags);
                if (pr == null) return 0;
                return Convert.ToDouble(pr.GetValue(inst, null));
            }
            catch (Exception) { return 0; }
        }

        static bool GcSet(string prop, string method, double v)
        {
            try
            {
                Type t; var inst = GcInst(out t);
                if (inst == null) return false;
                if (method != null)
                {
                    foreach (var m in t.GetMethods(GcFlags))
                    {
                        if (m.Name != method) continue;
                        var ps = m.GetParameters();
                        if (ps.Length < 1) continue;
                        var args = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                        {
                            var pt = ps[i].ParameterType;
                            if (i == 0 && pt == typeof(int)) args[i] = (int)Math.Round(v);
                            else if (i == 0 && pt == typeof(float)) args[i] = (float)v;
                            else if (pt == typeof(bool)) args[i] = false;
                            else if (pt == typeof(int)) args[i] = 0;
                            else if (pt == typeof(float)) args[i] = 0f;
                            else args[i] = null;
                        }
                        m.Invoke(inst, args);
                        return true;
                    }
                }
                var pr = t.GetProperty(prop, GcFlags);
                if (pr != null && pr.CanWrite)
                {
                    if (pr.PropertyType == typeof(int)) pr.SetValue(inst, (int)Math.Round(v), null);
                    else if (pr.PropertyType == typeof(float)) pr.SetValue(inst, (float)v, null);
                    else return false;
                    return true;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] " + prop + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
            return false;
        }

        static ProfileData CaptureProfile()
        {
            var p = Player.Instance;
            var fp = FirstPersonItemController.Instance;
            if (p == null || fp == null) return null;
            if (p.isDead) return null;
            var pd = new ProfileData();
            pd.valid = true; pd.id = MyProfileId();
            pd.money = (int)Math.Round(GcGet("money"));
            pd.social = (int)Math.Round(GcGet("socialCredit"));
            pd.hp = p.currentHealth; pd.maxHp = p.maximumHealth; pd.nerve = p.currentNerve; pd.maxNerve = p.maxNerve;
            foreach (var n in StatNames) pd.stats[n] = (float)Math.Round(GetStat(p, n), 3);
            var slots = fp.slots;
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var sl = slots[i];
                    if (sl == null || sl.isStatic != FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) continue;
                    Interactable it = null;
                    try { it = sl.GetInteractable(); } catch (Exception) { }
                    if (it == null) continue;
                    string pn = PresetNameOf(it);
                    if (pn == "") continue;
                    var pi2 = new ProfItem(); pi2.preset = pn; pi2.slot = sl.index;
                    try
                    {
                        pi2.val = it.val; pi2.cs = it.cs; pi2.hasState = true;
                        var pv = it.pv;
                        if (pv != null)
                            for (int k = 0; k < pv.Count && k < 30; k++)
                            {
                                var x = pv[k];
                                if (x != null) pi2.pv.Add(new ProfPv { type = (int)x.varType, value = x.value, str = x.str ?? "" });
                            }
                    }
                    catch (Exception) { }
                    pd.items.Add(pi2);
                }
            }
            pd.version = 2;
            try { CaptureExtra(p, pd); } catch (Exception e) { if (!profErrLogged) { profErrLogged = true; Plugin.L.LogWarning("[PROFILE] сбор профиля: " + e.Message); } }
            return pd;
        }

        // позиция, жильё, ключи, отмычки, коды, синк-диски
        static void CaptureExtra(Player p, ProfileData pd)
        {
            var pos = p.transform.position;
            pd.px = pos.x; pd.py = pos.y; pd.pz = pos.z;
            try { var n = p.currentNode; if (n != null) { var c = n.nodeCoord; pd.nx = (int)c.x; pd.ny = (int)c.y; pd.nz = (int)c.z; pd.hasPos = true; } } catch (Exception) { }
            pd.residence = AddrIdOf(p.residence);
            pd.lockpicks = (int)Math.Round(GcGet("lockPicks"));
            try { var kr = p.keyring; if (kr != null) for (int i = 0; i < kr.Count; i++) { var d = kr[i]; if (d == null) continue; int k = DoorKey(d); if (k >= 0) pd.doorKeys.Add(k); } } catch (Exception) { }
            try { var ki = p.playerKeyringInt; if (ki != null) for (int i = 0; i < ki.Count; i++) { var it = ki[i]; if (it != null) pd.itemKeys.Add(it.id); } } catch (Exception) { }
            var gc = GameplayController.Instance;
            if (gc != null)
            {
                try
                {
                    var ac = gc.acquiredPasscodes;
                    if (ac != null)
                        for (int i = 0; i < ac.Count; i++)
                        {
                            var c = ac[i]; if (c == null) continue;
                            var pc = new ProfCode { type = (int)c.type, id = c.id };
                            var dg = c.digits; if (dg != null) for (int j = 0; j < dg.Count; j++) pc.digits.Add(dg[j]);
                            pd.codes.Add(pc);
                        }
                }
                catch (Exception) { }
                try { var kp = gc.playerKnowsPasswords; if (kp != null) for (int i = 0; i < kp.Count; i++) pd.knowsPw.Add(kp[i]); } catch (Exception) { }
            }
            try
            {
                var uc = UpgradesController.Instance;
                var ups = uc != null ? uc.upgrades : null;
                if (ups != null)
                    for (int i = 0; i < ups.Count; i++)
                    {
                        var u = ups[i]; if (u == null) continue;
                        pd.disks.Add(new ProfDisk { upgrade = u.upgrade ?? "", state = (int)u.state, list = u.list, level = u.level, objId = u.objId, uninstallCost = u.uninstallCost });
                    }
            }
            catch (Exception) { }
        }

        static void ClearInventory()
        {
            var fp = FirstPersonItemController.Instance;
            if (fp == null || fp.slots == null) return;
            var list = new System.Collections.Generic.List<FirstPersonItemController.InventorySlot>();
            for (int i = 0; i < fp.slots.Count; i++)
            {
                var sl = fp.slots[i];
                if (sl != null && sl.isStatic == FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) list.Add(sl);
            }
            int n = 0;
            foreach (var sl in list)
            {
                try { if (sl.GetInteractable() == null) continue; fp.EmptySlot(sl, false, true, false, false); n++; }
                catch (Exception e) { Plugin.L.LogWarning("[PROFILE] очистка слота: " + e.Message); }
            }
            Plugin.L.LogInfo($"[PROFILE] инвентарь очищен, предметов убрано: {n}");
        }

        static bool profRestoring;
        static float profVerifyAt, profVerifyEnd, pendDeadline;
        static bool pendClearDone;
        static double prevMoney, prevSocial;
        static ProfileData profTarget;
        static System.Collections.Generic.List<ProfItem> pendingItems;
        static Interactable pendingIt;
        static int pendingTries, pendOk, pendBad;
        static float pendingAt;

        // предметы возвращаются по одному с паузами: игре нужно время, чтобы создать предмет в мире, прежде чем его можно подобрать
        static void RestoreInventory(System.Collections.Generic.List<ProfItem> items)
        {
            pendingIt = null; pendOk = 0; pendBad = 0; pendClearDone = false;
            pendingItems = new System.Collections.Generic.List<ProfItem>(items ?? new System.Collections.Generic.List<ProfItem>());
            pendingItems.Sort((a2, b2) => a2.slot.CompareTo(b2.slot));
            profRestoring = true; pendingAt = Time.realtimeSinceStartup + 0.5f; pendDeadline = Time.realtimeSinceStartup + 90f;
            Plugin.L.LogInfo($"[PROFILE] жду инвентарь игры, чтобы вернуть предметы: {pendingItems.Count}");
        }

        static void FinishRestore()
        {
            Plugin.L.LogInfo($"[PROFILE] инвентарь восстановлен: {pendOk} из {pendOk + pendBad}" + (pendBad > 0 ? $", не удалось: {pendBad}" : ""));
            pendingItems = null; pendingIt = null; profRestoring = false;
        }

        static void PendingItemsTick(float now)
        {
            if (pendingItems == null || now < pendingAt) return;
            var fp = FirstPersonItemController.Instance; var pl = Player.Instance;
            if (fp == null || pl == null) return;
            try
            {
                int nsl = 0;
                try { for (int i = 0; i < fp.slots.Count; i++) { var sl0 = fp.slots[i]; if (sl0 != null && sl0.isStatic == FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) nsl++; } } catch (Exception) { }
                if (nsl == 0)
                {
                    if (now > pendDeadline) { Plugin.L.LogWarning("[PROFILE] инвентарь игры так и не появился, предметы не возвращены"); pendingItems = null; pendingIt = null; profRestoring = false; }
                    else pendingAt = now + 0.5f;
                    return;
                }
                if (!pendClearDone) { pendClearDone = true; ClearInventory(); }
                if (pendingIt == null)
                {
                    if (pendingItems.Count == 0) { FinishRestore(); return; }
                    var pi2 = pendingItems[0];
                    var pr = FindPreset(pi2.preset);
                    if (pr == null) { Plugin.L.LogWarning($"[PROFILE] пресет предмета '{pi2.preset}' не найден"); pendingItems.RemoveAt(0); pendBad++; pendingAt = now + 0.05f; return; }
                    pendingIt = InteractableCreator.Instance.CreateWorldInteractable(pr, null, null, null, pl.transform.position + Vector3.up, Vector3.zero, null, null, "");
                    pendingTries = 0; pendingAt = now + 0.4f;
                    if (pendingIt == null) { Plugin.L.LogWarning($"[PROFILE] не смог создать предмет '{pi2.preset}'"); pendingItems.RemoveAt(0); pendBad++; }
                    else if (pi2.hasState)
                    {
                        var st = new ItemState(); st.val = pi2.val; st.cs = pi2.cs;
                        foreach (var x in pi2.pv) st.pv.Add(new PassedVal { type = x.type, value = x.value, str = x.str });
                        ApplyState(pendingIt, st);
                    }
                    return;
                }
                pendingTries++;
                bool done = false;
                try { done = fp.PickUpItem(pendingIt, false, false, false, false, false); } catch (Exception e) { if (pendingTries == 1) Plugin.L.LogWarning("[PROFILE] PickUpItem: " + e.Message); }
                if (!done) { try { done = fp.PickUpItem(pendingIt, false, true, false, false, false); } catch (Exception) { } }
                if (!done) { try { done = pendingIt.PickUpTarget(pl, false); } catch (Exception e) { if (pendingTries == 1) Plugin.L.LogWarning("[PROFILE] PickUpTarget: " + e.Message); } }
                if (done)
                {
                    pendOk++; pendingItems.RemoveAt(0); pendingIt = null; pendingAt = now + 0.2f;
                    return;
                }
                if (pendingTries == 1)
                {
                    int ns = 0, empty = 0;
                    try { for (int i = 0; i < fp.slots.Count; i++) { var sl = fp.slots[i]; if (sl != null && sl.isStatic == FirstPersonItemController.InventorySlot.StaticSlot.nonStatic) { ns++; if (sl.GetInteractable() == null) empty++; } } } catch (Exception) { }
                    bool spawned = false; try { spawned = pendingIt.spawnedObject != null; } catch (Exception) { }
                    bool avail = false; try { avail = fp.IsSlotAvailable(); } catch (Exception) { }
                    Plugin.L.LogWarning($"[PROFILE] подбор '{PresetNameOf(pendingIt)}' не прошёл: слотов {ns}, пустых {empty}, inventorySlots={fp.inventorySlots}, IsSlotAvailable={avail}, предмет в мире={spawned}");
                }
                if (pendingTries >= 6)
                {
                    Plugin.L.LogWarning($"[PROFILE] предмет '{PresetNameOf(pendingIt)}' так и не удалось положить в инвентарь");
                    try { pendingIt.SafeDelete(false); } catch (Exception) { }
                    pendingItems.RemoveAt(0); pendBad++; pendingIt = null; pendingAt = now + 0.2f;
                }
                else pendingAt = now + 0.5f;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[PROFILE] возврат предметов: " + e.Message);
                pendingItems = null; pendingIt = null; profRestoring = false;
            }
        }

        // все купленные квартиры с ключами; жильём становится то, что было жильём в профиле (иначе последняя купленная)
        static void RestoreResidence(System.Collections.Generic.List<ProfApt> apts, int residence = -1)
        {
            if (apts == null || apts.Count == 0) return;
            var pl = Player.Instance;
            var dict = CityData.Instance.addressDictionary;
            if (pl == null || dict == null) return;
            int home = apts[apts.Count - 1].id;
            foreach (var a in apts) if (a.id == residence) home = residence;
            var order = new System.Collections.Generic.List<int>();
            foreach (var a in apts) if (a.id != home && !order.Contains(a.id)) order.Add(a.id);
            order.Add(home);   // жильё - последним, чтобы SetResidence оставил именно его
            int n = 0;
            foreach (int id in order)
            {
                NewAddress ad = null;
                if (!dict.TryGetValue(id, out ad) || ad == null) { Plugin.L.LogWarning($"[PROFILE] квартира (адрес {id}) не найдена"); continue; }
                var rc = ad.residence;
                if (rc == null) { Plugin.L.LogWarning($"[PROFILE] у адреса {id} нет жилья"); continue; }
                soldAddrs.Add(id);
                if (AddrIdOf(pl.residence) != id) pl.SetResidence(rc, false);
                try { pl.AddToKeyring(ad, false); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] ключи от квартиры: " + e.Message); }
                n++;
            }
            Plugin.L.LogInfo($"[PROFILE] квартир возвращено: {n} из {apts.Count}, жильё игрока = {AddrIdOf(pl.residence)}");
        }

        static void ApplyProfile(ProfileData pd)
        {
            prevMoney = GcGet("money"); prevSocial = GcGet("socialCredit");
            profileApplying = true;
            try
            {
                var p = Player.Instance;
                try { GcSet("money", "SetMoney", pd.money); GcSet("socialCredit", "SetSocialCredit", pd.social); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] деньги/рейтинг: " + e.Message); }
                if (p != null)
                {
                    try
                    {
                        if (pd.maxHp > 0f) p.SetMaxHealth(pd.maxHp, false);
                        if (pd.hp > 0f) p.SetHealth(pd.hp);
                        if (pd.maxNerve > 0f) p.SetMaxNerve(pd.maxNerve, false);
                        if (pd.nerve >= 0f) p.SetNerve(pd.nerve);
                    }
                    catch (Exception e) { Plugin.L.LogWarning("[PROFILE] здоровье: " + e.Message); }
                    try { foreach (var kv in pd.stats) SetStat(p, kv.Key, kv.Value); }
                    catch (Exception e) { Plugin.L.LogWarning("[PROFILE] статусы: " + e.Message); }
                }
                try { RestoreInventory(pd.items); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] инвентарь: " + e.Message); }
                try { RestoreResidence(pd.apts, pd.residence); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] квартира: " + e.Message); }
                if (pd.version >= 2 && p != null) RestoreExtra(p, pd);
            }
            finally { profileApplying = false; }
        }

        static void RestoreExtra(Player p, ProfileData pd)
        {
            if (pd.lockpicks >= 0) { try { GcSet("lockPicks", "SetLockpicks", pd.lockpicks); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] отмычки: " + e.Message); } }
            try { RestoreKeys(p, pd); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] ключи: " + e.Message); }
            try { RestoreCodes(pd); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] коды: " + e.Message); }
            try { RestoreDisks(pd.disks); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] синк-диски: " + e.Message); }
            try { RestorePosition(p, pd); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] позиция: " + e.Message); }
        }

        // связка ключей как при загрузке сейва игрой: двери по номеру в CityData.doorDictionary, предметы-ключи через AddToKeyring
        static void RestoreKeys(Player p, ProfileData pd)
        {
            var kr = p.keyring;
            if (kr != null)
            {
                kr.Clear();
                foreach (int k in pd.doorKeys) { var d = DoorByKey(k); if (d != null && !kr.Contains(d)) kr.Add(d); }
            }
            var ki = p.playerKeyringInt;
            if (ki != null) ki.Clear();
            int ok = 0;
            foreach (int id in pd.itemKeys) { var it = FindById(id); if (it != null) { p.AddToKeyring(it, false); ok++; } }
            foreach (var a in pd.apts) { NewAddress ad; if (CityData.Instance.addressDictionary.TryGetValue(a.id, out ad) && ad != null) p.AddToKeyring(ad, false); }
            Plugin.L.LogInfo($"[PROFILE] ключи: дверей {(kr != null ? kr.Count : 0)} из {pd.doorKeys.Count}, предметов {ok} из {pd.itemKeys.Count}");
        }

        // известные коды: ищем настоящий код у адреса, комнаты, жителя или предмета; если не нашли - создаём с теми же цифрами
        static void RestoreCodes(ProfileData pd)
        {
            var gc = GameplayController.Instance;
            if (gc == null) return;
            var ac = gc.acquiredPasscodes;
            if (ac != null)
            {
                ac.Clear();
                var city = CityData.Instance;
                foreach (var c in pd.codes)
                {
                    GameplayController.Passcode code = null;
                    try
                    {
                        switch ((GameplayController.PasscodeType)c.type)
                        {
                            case GameplayController.PasscodeType.address: { NewAddress a; if (city.addressDictionary.TryGetValue(c.id, out a) && a != null) code = a.passcode; break; }
                            case GameplayController.PasscodeType.room: { NewRoom r; if (city.roomDictionary.TryGetValue(c.id, out r) && r != null) code = r.passcode; break; }
                            case GameplayController.PasscodeType.citizen: { Human h; if (city.citizenDictionary.TryGetValue(c.id, out h) && h != null) code = h.passcode; break; }
                            case GameplayController.PasscodeType.interactable: { var it = FindById(c.id); if (it != null) code = it.passcode; break; }
                        }
                    }
                    catch (Exception) { }
                    if (code == null)
                    {
                        code = new GameplayController.Passcode((GameplayController.PasscodeType)c.type);
                        code.id = c.id;
                        code.digits = new Il2CppSystem.Collections.Generic.List<int>();
                        foreach (int d in c.digits) code.digits.Add(d);
                    }
                    if (!ac.Contains(code)) ac.Add(code);
                }
            }
            var kp = gc.playerKnowsPasswords;
            if (kp != null) { kp.Clear(); foreach (int a in pd.knowsPw) kp.Add(a); }
            Plugin.L.LogInfo($"[PROFILE] коды: {pd.codes.Count}, известных паролей адресов: {pd.knowsPw.Count}");
        }

        // синк-диски как при загрузке сейва игрой: подставить список и пересчитать эффекты
        static void RestoreDisks(System.Collections.Generic.List<ProfDisk> disks)
        {
            var uc = UpgradesController.Instance;
            if (uc == null || uc.upgrades == null) return;
            uc.upgrades.Clear();
            foreach (var d in disks ?? new System.Collections.Generic.List<ProfDisk>())
            {
                var u = new UpgradesController.Upgrades();
                u.upgrade = d.upgrade; u.state = (UpgradesController.SyncDiskState)d.state; u.list = d.list; u.level = d.level; u.objId = d.objId; u.uninstallCost = d.uninstallCost;
                uc.upgrades.Add(u);
            }
            try { uc.UpdateActivation(); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] UpdateActivation: " + e.Message); }
            try { var ue = UpgradeEffectController.Instance; if (ue != null) ue.OnSyncDiskChange(true); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] OnSyncDiskChange: " + e.Message); }
            try { uc.UpdateUpgrades(); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] UpdateUpgrades: " + e.Message); }
            Plugin.L.LogInfo($"[PROFILE] синк-дисков: {uc.upgrades.Count}");
        }

        // игрок появляется там, где вышел
        static void RestorePosition(Player p, ProfileData pd)
        {
            if (!pd.hasPos) return;
            NewNode node = null;
            var map = PathFinder.Instance != null ? PathFinder.Instance.nodeMap : null;
            if (map == null || !map.TryGetValue(new Vector3(pd.nx, pd.ny, pd.nz), out node) || node == null)
            {
                Plugin.L.LogWarning($"[PROFILE] точка выхода ({pd.nx}, {pd.ny}, {pd.nz}) не найдена, игрок остаётся где есть");
                return;
            }
            p.Teleport(node, null, true, false, true);
            Plugin.L.LogInfo($"[PROFILE] игрок перенесён туда, где вышел: ({pd.px:F1}, {pd.py:F1}, {pd.pz:F1})");
        }

        // первый заход: не копия вещей хоста, а чистый старт
        static void NewPlayerStart()
        {
            prevMoney = GcGet("money"); prevSocial = GcGet("socialCredit");
            profileApplying = true;
            try
            {
                var p = Player.Instance;
                try { RestoreInventory(null); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] инвентарь: " + e.Message); }
                if (Plugin.CfgNewPlayerMoney.Value >= 0) GcSet("money", "SetMoney", Plugin.CfgNewPlayerMoney.Value);
                GcSet("socialCredit", "SetSocialCredit", 0);
                if (p != null)
                {
                    try { p.ResetHealthToMaximum(); p.ResetNerveToMaximum(); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] здоровье: " + e.Message); }
                    try { foreach (var n in BadStatNames) SetStat(p, n, 0f); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] статусы: " + e.Message); }
                }
                // синк-диски из сейва хоста - это улучшения хоста, новичку они не положены
                try { RestoreDisks(null); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] синк-диски: " + e.Message); }
            }
            finally { profileApplying = false; }
        }

        // ----- клиент -----
        static void SendProfile(bool force)
        {
            if (profState != 2 || profRestoring || profTarget != null || serverPeer == null || serverPeer.ConnectionState != ConnectionState.Connected) return;
            var pd = CaptureProfile();
            if (pd == null) return;
            string js = JsonSerializer.Serialize(pd, ProfJson);
            if (!force && js == profLastSent) return;
            profLastSent = js;
            var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)1); w.Put(js);
            serverPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        // 25 секунд следим, не вернула ли игра старые значения (из сохранения хоста) после нашего применения
        static void VerifyProfile()
        {
            var t = profTarget;
            if (t == null) return;
            float now = Time.realtimeSinceStartup;
            if (now >= profVerifyEnd) { profTarget = null; Plugin.L.LogInfo($"[PROFILE] проверка закончена: деньги {GcGet("money"):F0}, рейтинг {GcGet("socialCredit"):F0}"); return; }
            profVerifyAt = now + 2f;
            profileApplying = true;
            try
            {
                double m = GcGet("money"), sc = GcGet("socialCredit");
                if (t.money >= 0 && Math.Abs(m - t.money) >= 1 && Math.Abs(m - prevMoney) < 1) { Plugin.L.LogWarning($"[PROFILE] игра вернула деньги ({m:F0}), ставлю {t.money}"); GcSet("money", "SetMoney", t.money); }
                if (Math.Abs(sc - t.social) >= 1 && Math.Abs(sc - prevSocial) < 1) { Plugin.L.LogWarning($"[PROFILE] игра вернула рейтинг ({sc:F0}), ставлю {t.social}"); GcSet("socialCredit", "SetSocialCredit", t.social); }
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] проверка: " + e.Message); }
            finally { profileApplying = false; }
        }

        static void ProfileClientData(NetDataReader r)
        {
            bool found = r.GetBool(); string js = r.GetString();
            if (profState == 2) return;
            ProfileData pd = null;
            try { if (found && js != "") pd = JsonSerializer.Deserialize<ProfileData>(js, ProfJson); }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] ответ хоста не разобран: " + e.Message); }
            try
            {
                if (pd != null && pd.valid)
                {
                    ApplyProfile(pd);
                    profTarget = pd; profVerifyAt = Time.realtimeSinceStartup + 2f; profVerifyEnd = Time.realtimeSinceStartup + 25f;
                    try { var pp = Player.Instance; Plugin.L.LogInfo($"[PROFILE] после применения: деньги {GcGet("money"):F0} (в профиле {pd.money}), рейтинг {GcGet("socialCredit"):F0} (в профиле {pd.social}), здоровье {(pp != null ? pp.currentHealth : -1f):F2} (в профиле {pd.hp:F2}), нерв {(pp != null ? pp.currentNerve : -1f):F2} (в профиле {pd.nerve:F2})"); } catch (Exception) { }
                    Plugin.L.LogInfo($"[PROFILE] профиль загружен: деньги {pd.money}, рейтинг {pd.social}, здоровье {pd.hp:F1}/{pd.maxHp:F1}, предметов {pd.items.Count}, квартир {pd.apts.Count}");
                    Toast("Your saved profile was loaded.");
                }
                else
                {
                    if (Plugin.CfgNewPlayerClean.Value)
                    {
                        NewPlayerStart();
                        var nt = new ProfileData(); nt.money = Plugin.CfgNewPlayerMoney.Value; nt.social = 0; nt.valid = true;
                        profTarget = nt; profVerifyAt = Time.realtimeSinceStartup + 2f; profVerifyEnd = Time.realtimeSinceStartup + 25f;
                    }
                    if (pd != null && pd.apts.Count > 0) { profileApplying = true; try { RestoreResidence(pd.apts, pd.residence); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] квартира: " + e.Message); } finally { profileApplying = false; } }
                    Plugin.L.LogInfo("[PROFILE] профиля ещё нет, это новый игрок" + (Plugin.CfgNewPlayerClean.Value ? ": чистый старт" : ""));
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] применить профиль: " + e.Message); }
            profState = 2; profSendAt = Time.realtimeSinceStartup + 2f;
        }

        // ----- хост -----
        static string ProfFilePath()
        {
            string seed = "";
            try { seed = "" + CityData.Instance.seed; } catch (Exception) { }
            var sb = new StringBuilder();
            foreach (char ch in seed) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return Path.Combine(SaveDir(), "SodCoop_profiles_" + (sb.Length > 0 ? sb.ToString() : "city") + ".json");
        }

        // Профили на хосте живут в двух местах:
        //  - SodCoop_profiles_<seed>.json: последние профили этого города, обновляются всю сессию; по ним игрок получает свои вещи при переподключении;
        //  - <сейв>.sodcoop.json рядом с каждым сохранением хоста: профили на момент сохранения.
        // Когда хост загружает сейв, профили берутся из его снимка (откатываются вместе с миром); новая игра начинается без профилей
        static int worldGen, profWorldGen = -1;
        static string snapPath = "";
        static float snapUntil;

        internal static void OnWorldLoading() { worldGen++; }

        static string SnapPathFor(string savePath)
        {
            return Path.Combine(Path.GetDirectoryName(savePath) ?? SaveDir(), Path.GetFileNameWithoutExtension(savePath) + ".sodcoop.json");
        }

        static string LoadedSavePath()
        {
            try { var r = RestartSafeController.Instance; if (r != null && r.loadSaveGame && r.saveStateFileInfo != null) return r.saveStateFileInfo.FullName ?? ""; } catch (Exception) { }
            return "";
        }

        static bool ReadProfilesFile(string path)
        {
            foreach (var f in new[] { path, path + ".bak" })
            {
                if (!File.Exists(f)) continue;
                try
                {
                    var d = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, ProfileData>>(File.ReadAllText(f), ProfJson);
                    if (d == null) continue;
                    foreach (var kv in d) if (kv.Value != null) profiles[kv.Key] = kv.Value;
                    if (f != path) Plugin.L.LogWarning("[PROFILE] " + Path.GetFileName(path) + " повреждён, взял резервную копию");
                    return true;
                }
                catch (Exception e) { Plugin.L.LogWarning("[PROFILE] не прочитал " + f + ": " + e.Message); }
            }
            return false;
        }

        // запись через временный файл, прошлая версия остаётся в .bak: если игра упадёт посреди записи, файл не испортится
        static void WriteJsonSafe(string path, string json)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }

        static void ProfilesHostInit()
        {
            profDirty = false; profFile = "";
            if (!Plugin.CfgProfiles.Value) { profiles.Clear(); return; }
            profFile = ProfFilePath();
            string src;
            if (worldGen != profWorldGen)
            {
                // загружен другой мир: берём профили заново
                profWorldGen = worldGen; profiles.Clear(); soldAddrs.Clear();
                string save = LoadedSavePath();
                string snap = save != "" ? SnapPathFor(save) : "";
                if (snap != "" && ReadProfilesFile(snap)) { src = "снимок сейва " + Path.GetFileName(save); profDirty = true; }
                else if (save == "" && worldGen > 0) { src = "новая игра, профилей нет"; profDirty = true; }
                else { ReadProfilesFile(profFile); src = save != "" ? "у сейва " + Path.GetFileName(save) + " нет снимка, взял последние профили города" : "последние профили города"; }
            }
            else src = "профили этой сессии";   // сеть перезапущена в том же мире
            int n = 0;
            foreach (var kv in profiles)
                foreach (var a in kv.Value.apts) { try { ApplyApartmentSold(a.id, false, a.rems); n++; } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] квартира " + a.id + ": " + e.Message); } }
            Plugin.L.LogInfo($"[PROFILE] профилей игроков: {profiles.Count} ({src}), проданных квартир восстановлено: {n}");
        }

        static void ProfilesWrite()
        {
            if (profFile == "") return;
            try { WriteJsonSafe(profFile, JsonSerializer.Serialize(profiles, ProfJson)); profDirty = false; Plugin.Dbg("[PROFILE] файл профилей записан"); }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] не смог записать " + profFile + ": " + e.Message); }
        }

        static void SnapshotWrite()
        {
            if (snapPath == "") return;
            try { WriteJsonSafe(snapPath, JsonSerializer.Serialize(profiles, ProfJson)); Plugin.Dbg("[PROFILE] снимок профилей записан: " + snapPath); }
            catch (Exception e) { Plugin.L.LogWarning("[PROFILE] не смог записать снимок " + snapPath + ": " + e.Message); }
        }

        // хост сохраняет игру: рядом с сейвом - профили игроков; клиенты присылают свежие профили, и ещё 6 с снимок дописывается
        internal static void OnGameSaving(string path)
        {
            if (manager == null || !IsHost || !Plugin.CfgProfiles.Value || string.IsNullOrEmpty(path)) return;
            if (Path.GetFileName(path).StartsWith("SodCoop_")) return;   // снимок мира для передачи клиенту, не сохранение игрока
            snapPath = SnapPathFor(path); snapUntil = Time.realtimeSinceStartup + 6f;
            SnapshotWrite();
            var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)4);
            foreach (var o in players) if (o.peer != null) { try { o.peer.Send(w, DeliveryMethod.ReliableOrdered); } catch (Exception) { } }
            Plugin.L.LogInfo($"[PROFILE] игра сохранена: профили игроков ({profiles.Count}) записаны в {Path.GetFileName(snapPath)}");
        }

        static void ProfileNoteApt(RP rp, int id, System.Collections.Generic.List<int[]> rems)
        {
            if (!Plugin.CfgProfiles.Value || rp == null || string.IsNullOrEmpty(rp.profileId)) return;
            ProfileData pd;
            if (!profiles.TryGetValue(rp.profileId, out pd)) { pd = new ProfileData(); pd.id = rp.profileId; profiles[rp.profileId] = pd; }
            pd.apts.RemoveAll(a => a.id == id);
            var pa = new ProfApt(); pa.id = id; pa.rems = rems ?? new System.Collections.Generic.List<int[]>();
            pd.apts.Add(pa);
            profDirty = true;
            Plugin.L.LogInfo($"[PROFILE] квартира (адрес {id}) записана игроку {rp.pid}");
        }

        static void ProfileHostMsg(RP rp, byte op, NetDataReader r)
        {
            if (!Plugin.CfgProfiles.Value || string.IsNullOrEmpty(rp.profileId)) return;
            string id = rp.profileId;
            if (op == 1)
            {
                ProfileData pd = null;
                try { pd = JsonSerializer.Deserialize<ProfileData>(r.GetString(), ProfJson); } catch (Exception e) { Plugin.L.LogWarning("[PROFILE] профиль игрока " + rp.pid + " не разобран: " + e.Message); }
                if (pd == null || !pd.valid) return;
                ProfileData old; profiles.TryGetValue(id, out old);
                pd.id = id; pd.saved = DateTime.UtcNow.ToString("u");
                pd.apts = old != null ? old.apts : new System.Collections.Generic.List<ProfApt>();
                profiles[id] = pd; profDirty = true;
                if (snapPath != "" && Time.realtimeSinceStartup < snapUntil) SnapshotWrite();
            }
            else if (op == 2)
            {
                ProfileData pd; bool found = profiles.TryGetValue(id, out pd);
                var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)3); w.Put(found); w.Put(found ? JsonSerializer.Serialize(pd, ProfJson) : "");
                if (rp.peer != null) rp.peer.Send(w, DeliveryMethod.ReliableOrdered);
                Plugin.L.LogInfo($"[PROFILE] игрок {rp.pid} запросил профиль: " + (found && pd.valid ? "найден" : "новый игрок") + (found && pd.apts.Count > 0 ? $", квартир {pd.apts.Count}" : ""));
            }
        }

        static void ProfileTick(float dt)
        {
            if (!Plugin.CfgProfiles.Value || manager == null) return;
            float now = Time.realtimeSinceStartup;
            if (IsHost)
            {
                if (profDirty && now >= profWriteAt) { profWriteAt = now + 10f; ProfilesWrite(); }
                return;
            }
            if (xferMode || !helloOkFlag || serverPeer == null || serverPeer.ConnectionState != ConnectionState.Connected) return;
            if (Player.Instance == null || FirstPersonItemController.Instance == null) return;
            PendingItemsTick(now);
            if (profState < 2)
            {
                // ждём, пока игра по-настоящему началась (иначе она ещё догружает состояние игрока и перезапишет наши значения)
                if (now < profReadyAt || now < profReqAt) return;
                bool started = false;
                try { started = SessionData.Instance != null && SessionData.Instance.startedGame; } catch (Exception) { }
                if (!started && now < profReadyAt + 26f) return;

                profReqAt = now + 5f; profState = 1;
                var w = new NetDataWriter(); w.Put(MsgProfile); w.Put((byte)2);
                serverPeer.Send(w, DeliveryMethod.ReliableOrdered);
                return;
            }
            if (profTarget != null && now >= profVerifyAt) VerifyProfile();
            if (now >= profSendAt) { profSendAt = now + Mathf.Max(3f, Plugin.CfgProfileInterval.Value); SendProfile(false); }
        }

        static void ProfileOnShutdown()
        {
            if (IsHost) { if (profDirty) ProfilesWrite(); }
            else if (profState == 2 && manager != null)
            {
                SendProfile(true);
                System.Threading.Thread.Sleep(60);   // дать сети успеть отправить пакет до остановки
            }
            profState = 0; profLastSent = ""; helloOkFlag = false; profReqAt = 0f; pendingItems = null; pendingIt = null; profRestoring = false; profTarget = null;
        }

        // ---------- Без сознания и больница: без перемотки общего времени ----------
        // Игра при нуле здоровья: TriggerPlayerKO -> штраф -> телепорт в больницу -> SetTimeSpeed(veryFast), KOTimePassed растёт до KOTime (игровые часы),
        // потом игра сама ставит normal и снимает KO. Если запретить veryFast, KO не заканчивается никогда (проверено), поэтому veryFast пускаем,
        // но настоящий множитель времени держим 1, а другим игрокам сообщаем прежнюю скорость. KOTimePassed двигаем сами: лечение идёт HospitalSeconds
        static bool applyingTimeSpeed, koActive, koRecovery, koErrLogged;
        static float koStartAt, koLogAt, koLogGt;
        static int koClamped;
        static SessionData.TimeSpeed koPrevSpeed = SessionData.TimeSpeed.normal;

        internal static bool LocalKo()
        {
            if (manager == null || !Plugin.CfgKoNoTimeSkip.Value) return false;
            try { var p = Player.Instance; return p != null && p.playerKOInProgress; } catch (Exception) { return false; }
        }

        // скорость, которую видят другие игроки: пока мы без сознания, это скорость до KO
        static SessionData.TimeSpeed SharedTimeSpeed(SessionData sd)
        {
            return koActive && LocalKo() ? koPrevSpeed : sd.currentTimeSpeed;
        }

        // после SetTimeSpeed от игры во время KO: состояние veryFast остаётся, а время идёт как обычно
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

        // полоска на экране во время KO: действие взаимодействия "ko" с порогом KOTime
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

        static void KoTick(Player p, float dt)
        {
            if (!Plugin.CfgKoNoTimeSkip.Value || p == null) return;
            bool ko = p.playerKOInProgress, rec = p.KORecovery;
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
            float total = rec ? 5f : Plugin.CfgHospitalSeconds.Value;
            float el = now - koStartAt;
            if (now >= koLogAt && sd != null)
            {
                koLogAt = now + 5f;
                Plugin.L.LogInfo($"[KO] {(rec ? "подъём" : "больница")} {el:F0}/{total + 3f:F0} с: KOTimePassed={p.KOTimePassed:F3} KOTime={p.KOTime:F3} hp={p.currentHealth:F2}/{p.maximumHealth:F2} скорость={sd.currentTimeSpeed} x{sd.currentTimeMultiplier:F2} timeScale={Time.timeScale:F2} время +{(sd.gameTime - koLogGt) * 60f:F1} игр. мин | затемнение={p.playerKOFadeOut} полоска={KoBar()}");
                koLogGt = sd.gameTime;
            }
            // здоровье во время KO у клиента уходило выше максимума
            float max = p.maximumHealth;
            if (max > 0f && p.currentHealth > max) p.SetHealth(max);
            // Подъём после выбора в больнице (TriggerPlayerRecovery): игра заново включает KO с KOTime=0.5, затемнением и полоской.
            // Саму фазу не пропускаем (она возвращает игроку действия), а делаем мгновенной: KOTimePassed сразу у порога,
            // затемнение (InterfaceController.fade плавно идёт к desiredFade) - сразу в цель
            if (rec && Plugin.CfgKoInstantGetUp.Value)
            {
                float kt = p.KOTime;
                if (kt > 0f && p.KOTimePassed < kt - 0.0005f) p.KOTimePassed = kt - 0.0005f;
                var ic = InterfaceController.Instance;
                if (ic != null && ic.fade != ic.desiredFade) ic.fade = ic.desiredFade;
                return;
            }
            if (el < 3f) return;   // сначала игра доигрывает падение, берёт штраф и переносит в больницу
            float koTime = p.KOTime;
            if (total < 1f || koTime <= 0f) return;
            // Player.Update (GameAssembly, разобрано через Cpp2IL): каждый кадр KO игра прибавляет к KOTimePassed прошедшее игровое время,
            // и только если порог KOTime перейдён внутри этого кадра, ставит normal и вызывает InterfaceController.Fade(0) - убирает затемнение.
            // Если KOTimePassed >= KOTime уже в начале кадра, игра лишь ждёт fade <= 0, а Fade(0) никто не вызывает: экран тёмный навсегда.
            // Поэтому доводим только до чуть меньше порога, последний шаг делает сама игра
            float passed = p.KOTimePassed;
            float goal = koTime - 0.0005f;
            if (passed < goal) { passed = Mathf.Min(goal, passed + koTime * dt / total); p.KOTimePassed = passed; }
            if (!rec)
            {
                // здоровье растёт с половины до максимума
                float target = max * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(passed / koTime));
                if (p.currentHealth < target) p.SetHealth(Mathf.Min(target, max));
            }
        }

        static int MaxPlayersNow() { return Mathf.Clamp(Plugin.CfgMaxPlayers.Value, 2, 4); }

        // убрать игрока: аватар, двойник, иконка на карте
        static void RemovePlayer(RP rp, string why)
        {
            try
            {
                Enter(rp);
                DestroyAvatar();
                if (puppet != null) ReleasePuppet("игрок вышел");
                Leave();
            }
            catch (Exception e) { Leave(); Plugin.L.LogWarning("[NET] RemovePlayer: " + e.Message); }
            players.Remove(rp);
            Plugin.L.LogInfo($"[NET] игрок {rp.pid} убран ({why}); осталось удалённых игроков: {players.Count}");
            if (IsHost)
            {
                Toast("Player " + rp.pid + " left the game (" + why + ").");
                var w = new NetDataWriter(); w.Put(MsgPlayerLeft); w.Put((byte)rp.pid);
                foreach (var o in players) if (o.peer != null) { try { o.peer.Send(w, DeliveryMethod.ReliableOrdered); } catch (Exception) { } }
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
        }

        static bool IsRelayed(byte t)
        {
            return t == MsgPos || t == MsgAction || t == MsgItem || t == MsgHeld || t == MsgObj || t == MsgItemEv || t == MsgDoor || t == MsgBodyFx || t == MsgShot || t == MsgWeapon || t == MsgSound;
        }

        static bool NeedsCtx(byte t)
        {
            return t == MsgPos || t == MsgAction || t == MsgItem || t == MsgHeld || t == MsgBodyFx || t == MsgShot || t == MsgWeapon || t == MsgSound || t == MsgNpcHit;
        }

        static void RelayFromPlayer(RP from, byte type, byte[] rest, DeliveryMethod method)
        {
            if (players.Count < 2) return;
            var w = new NetDataWriter();
            w.Put(MsgRelay); w.Put((byte)from.pid); w.Put(type); w.Put(rest, 0, rest.Length);
            foreach (var o in players)
                if (o != from && o.peer != null) { try { o.peer.Send(w, method); } catch (Exception) { } }
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
            if (NeedsCtx(type) && rp == null) return;   // пакет игрока, который ещё не представился
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
                    // старое соединение этого игрока ещё не отвалилось по таймауту: заменяем его новым
                    RP stale = null;
                    foreach (var o in players) if (o.clientId == cid && o.peer != peer) { stale = o; break; }
                    if (stale != null)
                    {
                        var sp = stale.peer; RemovePlayer(stale, "переподключился");
                        try { if (sp != null) sp.Disconnect(); } catch (Exception) { }
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
                Toast(ok ? "Player " + me.pid + " joined the game." : "A player connected, but their world does not match yours.");
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
                peer.Send(w, DeliveryMethod.ReliableOrdered);
                if (ok)
                {
                    SendDoorSnapshot(peer); SendRoomSnapshot(peer); SendWindowSnapshot(peer); SendTime(); SendSoldSnapshot(peer); SendFurnSnapshot(peer);
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
                if (full)
                {
                    Toast("The server is full.");
                    Plugin.L.LogWarning("[NET] Сервер полон, повторов не будет");
                    connectRejected = true;
                    try { peer.Disconnect(); } catch (Exception) { }
                    return;
                }
                MyPid = pid;
                helloOkFlag = ok; if (ok && profState == 0) profReadyAt = Time.realtimeSinceStartup + 4f;
                var hostRp = RpByPid(0) ?? NewRp(0);
                Enter(hostRp); SetRemoteAppearance(hostApp); Leave();
                Plugin.L.LogInfo($"[NET] Ответ хоста: ok={ok}, мой номер {pid}");
                if (!ok) Toast("Your world does not match the host's (different city or build). Leave to the main menu and use CONNECT to download the host's save.");
                Plugin.L.LogInfo($"[NET]   хост:   {hostInfo}");
                Plugin.L.LogInfo($"[NET]   клиент: {Describe()}");
                if (ok) SendFurnSnapshot(null);
            }
            else if (type == MsgProfile)
            {
                byte pop = reader.GetByte();
                if (IsHost) { if (rp != null) ProfileHostMsg(rp, pop, reader); }
                else if (pop == 3) ProfileClientData(reader);
                else if (pop == 4) SendProfile(true);   // хост сохраняет игру и просит свежий профиль
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
            else if (type == MsgPlayerLeft && !IsHost)
            {
                int pid = reader.GetByte();
                var o = RpByPid(pid);
                if (o != null) { RemovePlayer(o, "вышел"); Toast("Player " + pid + " left the game."); }
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
                    else if (kind == 2) avatarAnim.SetTrigger("block"); // в игре блок = разовое уклонение
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
                    // удар по двойнику другого клиента: пересылаем пострадавшему
                    var vr = RpByPid(victim);
                    if (vr != null && vr.peer != null)
                    {
                        byte[] tail = reader.GetRemainingBytes();
                        var fw = new NetDataWriter(); fw.Put(MsgPuppetHurt); fw.Put((byte)victim); fw.Put(tail, 0, tail.Length);
                        try { vr.peer.Send(fw, DeliveryMethod.ReliableOrdered); } catch (Exception) { }
                    }
                }
                else if (!IsHost || Plugin.CfgPvp.Value) OnPuppetHurt(reader);
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
                string rsig = HeldSig(remoteHeld);
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
                remoteCarryObj = (fl & 64) != 0;
                remoteLean = reader.GetFloat();
                if (avatar == null) CreateAvatar();
            }
        }
    }
}
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

    [BepInPlugin("com.flyingfrogs.sodcoop", "SodCoop", Plugin.ModVersion)]
    public class Plugin : BasePlugin
    {
        internal const string ModVersion = "0.14.1";
        internal static ManualLogSource L;
        internal static void Dbg(string m) { try { if (CfgDebugLog != null && CfgDebugLog.Value) L.LogInfo(m); } catch (System.Exception) { } }
        internal static ConfigEntry<NetMode> CfgMode;
        internal static ConfigEntry<string> CfgAddress;
        internal static ConfigEntry<int> CfgPort;
        internal static ConfigEntry<string> CfgPassword, CfgServerName;
        internal static ConfigEntry<bool> CfgDebugAvatar;
        internal static ConfigEntry<bool> CfgDumpCitizen;
        internal static ConfigEntry<bool> CfgDevCommands;
        internal static ConfigEntry<bool> CfgUseCitizenModel;
        internal static ConfigEntry<float> CfgAvatarYOffset;
        internal static ConfigEntry<float> CfgAvatarScale, CfgAvatarHeight;
        internal static ConfigEntry<int> CfgMaxPlayers;
        internal static ConfigEntry<bool> CfgProfiles, CfgNewPlayerClean;
        internal static ConfigEntry<float> CfgProfileInterval;
        internal static ConfigEntry<int> CfgNewPlayerMoney;
        internal static ConfigEntry<int> CfgNewPlayerLockpicks;
        internal static ConfigEntry<bool> CfgKoNoTimeSkip;
        internal static ConfigEntry<float> CfgHospitalSeconds;
        internal static ConfigEntry<bool> CfgKoInstantGetUp;
        internal static ConfigEntry<bool> CfgDowned;
        internal static ConfigEntry<bool> CfgJobSync;
        internal static ConfigEntry<bool> CfgJobHostAuthority;
        internal static ConfigEntry<string> CfgShareEvidenceKey;
        internal static ConfigEntry<float> CfgTripFall, CfgTripGetUp;
        internal static ConfigEntry<float> CfgDownedSeconds, CfgReviveHoldSeconds, CfgReviveDistance, CfgReviveHealth, CfgDownedHitPenalty;
        internal static ConfigEntry<string> CfgReviveKey;
        internal static ConfigEntry<float> CfgAvatarYawOffset;
        internal static ConfigEntry<float> CfgMaxSpeed;
        internal static ConfigEntry<int> CfgArmsState;
        internal static ConfigEntry<int> CfgIdleState;
        internal static ConfigEntry<float> CfgCrouchDrop;
        internal static ConfigEntry<float> CfgCrawlHip, CfgCrawlKnee, CfgCrawlLean, CfgCrawlArms;
        internal static ConfigEntry<float> CfgCrouchHip;
        internal static ConfigEntry<float> CfgCrouchBendHip, CfgCrouchBendKnee, CfgCrouchLean;
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
        internal static ConfigEntry<bool> CfgClientCheats, CfgRewardOnlyHandIn;
        internal static ConfigEntry<bool> CfgUpdateCheck;
        internal static ConfigEntry<string> CfgUpdateRepo;
        internal static ConfigEntry<string> CfgCheatMenuKey;
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
        internal static ConfigEntry<string> CfgAutoTest, CfgAutoTestJobs;
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
        internal static ConfigEntry<bool> CfgWorldNeverPauses;
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
            CfgNewPlayerMoney = Config.Bind("Profiles", "NewPlayerMoney", 100,
                "Money of a player who joins for the first time (-1 = keep what the host's save gives)");
            CfgNewPlayerLockpicks = Config.Bind("Profiles", "NewPlayerLockpicks", 5,
                "Lockpicks of a player who joins for the first time (-1 = keep what the host's save gives)");
            CfgKoNoTimeSkip = Config.Bind("Hospital", "NoTimeSkip", true,
                "In multiplayer the game does not fast-forward time while a player is knocked out or in hospital (time is shared by all players). The player heals in real time instead");
            CfgHospitalSeconds = Config.Bind("Hospital", "HospitalSeconds", 60f,
                "How long (real seconds) a knocked out player lies in hospital when NoTimeSkip is on");
            CfgKoInstantGetUp = Config.Bind("Hospital", "InstantGetUp", true,
                "After the pay/don't pay choice in hospital the player gets up at once instead of a second fade and progress bar (when NoTimeSkip is on)");
            CfgTripFall = Config.Bind("Avatar", "TripFallSeconds", 0.5f,
                "When a player slips/trips: how long the avatar's fall animation runs before it lies down (citizens: animation event TripKO)");
            CfgTripGetUp = Config.Bind("Avatar", "TripGetUpSeconds", 1.5f,
                "When a player slips/trips: after how many seconds the avatar plays the get-up animation");
            CfgJobHostAuthority = Config.Bind("Jobs", "HostAuthority", true,
                "Only the host runs the job logic (phases, objectives, items, calls); clients follow the host. Off = every player runs his own copy of the job (old behaviour)");
            CfgJobSync = Config.Bind("Jobs", "Sync", true,
                "Job board: only the host creates jobs and sends them (with their items) to the clients; a job taken by one player is taken by all (each gets his own case), and everybody gets the reward when it is handed in");
            CfgShareEvidenceKey = Config.Bind("Jobs", "ShareEvidenceHotkey", "",
                "Optional key that shares the item selected on your case board (empty = off; use the right-click menu item 'Share with players' instead)");
            CfgDowned = Config.Bind("Downed", "Enabled", true,
                "In multiplayer a player who runs out of health first lies on the ground and can be revived by another player. If nobody revives him in time, he is knocked out as usual (hospital)");
            CfgDownedSeconds = Config.Bind("Downed", "Seconds", 10f,
                "How long (real seconds) a downed player waits for help. The timer stops while someone is reviving him");
            CfgReviveKey = Config.Bind("Downed", "ReviveKey", "H",
                "Key to hold near a downed player to revive him (name from UnityEngine.KeyCode)");
            CfgReviveHoldSeconds = Config.Bind("Downed", "ReviveHoldSeconds", 3f,
                "How long (seconds) the key must be held to revive");
            CfgReviveDistance = Config.Bind("Downed", "ReviveDistance", 2f,
                "How close (meters) you must be to revive");
            CfgReviveHealth = Config.Bind("Downed", "ReviveHealth", 0.3f,
                "Health after being revived, part of maximum (0..1)");
            CfgDownedHitPenalty = Config.Bind("Downed", "HitPenaltySeconds", 2f,
                "Each hit a downed player takes shortens his waiting time by this many seconds");
            CfgAvatarScale = Config.Bind("Avatar", "Scale", 0.92f,
                "Not used any more: the avatar now gets the real size of citizens of the same gender. Use HeightMultiplier");
            CfgAvatarHeight = Config.Bind("Avatar", "HeightMultiplier", 1f,
                "Height of the other player's avatar relative to an average citizen of the same gender (1 = same as citizens); edit while the game runs");
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
                "Not used any more (old bone crouch on a fixed bone axis twisted the legs). See CrouchBendHip");
            CfgCrouchBendHip = Config.Bind("Avatar", "CrouchBendHip", 75f,
                "Crouch: how far (degrees) the thighs swing forward. Bends are around the avatar's own left-right axis, so they work for any model");
            CfgCrouchBendKnee = Config.Bind("Avatar", "CrouchBendKnee", 115f,
                "Crouch: how far (degrees) the knees bend (the shins swing back). The feet stay flat and on the floor automatically");
            CfgCrouchLean = Config.Bind("Avatar", "CrouchLean", 25f,
                "Crouch: forward lean of the body in degrees");
            CfgCrawlHip = Config.Bind("Avatar", "CrawlHip", 10f, "Air vent crawl: hip bend in degrees (0 = thighs straight down)");
            CfgCrawlKnee = Config.Bind("Avatar", "CrawlKnee", 95f, "Air vent crawl: knee bend in degrees (lower legs lie on the floor behind)");
            CfgCrawlLean = Config.Bind("Avatar", "CrawlLean", 80f, "Air vent crawl: forward lean of the body in degrees");
            CfgCrawlArms = Config.Bind("Avatar", "CrawlArms", 80f, "Air vent crawl: how much the arms turn down towards the floor in degrees");
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
                "Animator state forced while the other player is crouched and standing still (the citizen controller may fight it and twitch). Empty = crouch made by bending the leg bones");
            CfgCrouchWalkState = Config.Bind("Poses", "CrouchWalkClip", "",
                "State forced while crouched AND moving. Empty = walking with bent legs (bones)");
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
                "IdleAnimationState while crouched AND standing still - a pose the citizen controller holds by itself (21 stackingObjectsCrouching, 15 checkPulse, 14 cowering). -1 = off, bent leg bones");
            CfgSitDrop = Config.Bind("Poses", "SitDropMeters", 0.35f,
                "Lower the sitting avatar by this many meters (adjust if he floats above / sinks into the chair)");
            CfgThrowState = Config.Bind("Poses", "ThrowTrigger", "throw",
                "Animator trigger played when the other player throws / puts down the item in his hands");
            CfgTimeSync = Config.Bind("World", "TimeWeatherSync", true,
                "Host sends game time, time speed and weather to the client every few seconds");
            CfgObjSync = Config.Bind("Objects", "Sync", true,
                "Sync world objects the player carries (garbage bags, boxes...) and drops/throws: the other player sees them move");
            CfgUpdateCheck = Config.Bind("Update", "Check", true, "Check GitHub for a newer mod version at game start and offer to install it");
            CfgUpdateRepo = Config.Bind("Update", "Repo", "GhostW1ng-Work/SODCoop", "GitHub repository (owner/name) whose latest release is checked");
            CfgClientCheats = Config.Bind("Rules", "ClientCheats", true, "Host: other players may use the cheat menu on this server");
            CfgRewardOnlyHandIn = Config.Bind("Rules", "RewardOnlyHandIn", false, "Host: only the player who handed in a case gets its money, social credit and reward item (false = everyone gets the reward)");
            CfgCheatKeys = Config.Bind("Cheats", "Hotkeys", false,
                "Cheat menu: MenuKey (F2 by default) opens a window with all cheats while you play");
            CfgCheatMenuKey = Config.Bind("Cheats", "MenuKey", "F2", "Key that opens the cheat menu (name from UnityEngine.KeyCode) when Hotkeys = true");
            CfgAddMoney = Config.Bind("Cheats", "AddMoney", 0,
                "Set to a number while the game runs: that much money is added once, then it resets to 0");
            CfgMoneyAmount = Config.Bind("Cheats", "MoneyAmount", 1000, "Money amount in the cheat menu");
            CfgItemPreset = Config.Bind("Cheats", "ItemPreset", "StarchKola",
                "Item preset name spawned from the cheat menu (see SearchPreset to find names)");
            CfgSpawnNow = Config.Bind("Cheats", "SpawnNow", false,
                "Set to true while the game runs: spawns ItemPreset in front of you, then resets to false");
            CfgSearchPreset = Config.Bind("Cheats", "SearchPreset", "",
                "Type part of a name (e.g. kola): matching item preset names are written to the log, then this resets");
            CfgPauseNpc = Config.Bind("Cheats", "PauseNpcAI", false,
                "Pause NPC AI (they freeze and stop reacting to you). Toggle in the cheat menu");
            CfgGod = Config.Bind("Cheats", "GodMode", false,
                "The local player takes no damage. Toggle in the cheat menu");
            CfgAddSocial = Config.Bind("Cheats", "AddSocialCredit", 0,
                "Set to a number while the game runs: that much social credit is added once, then it resets to 0");
            CfgSocialAmount = Config.Bind("Cheats", "SocialCreditAmount", 10, "Social credit amount in the cheat menu");
            CfgInvisible = Config.Bind("Cheats", "NpcCannotSee", false,
                "NPCs cannot see you (no spotting, no illegal-activity witnesses). Toggle in the cheat menu");
            CfgAvatarLightFix = Config.Bind("Poses", "AvatarLightFix", true,
                "Keep the other player's model lit after room lights are switched off and on (sets the renderers' light layer mask to all layers)");
            CfgTypeRegex = Config.Bind("Debug", "TypeRegex", "apartment|residen|purchas|forsale|realestate|estate|landlord|lease|ownership|sale",
                "Regex (case-insensitive) for class names listed in the log as [TYPES] ~25 s after entering the game when TraceBreak=true");
            CfgAutoTest = Config.Bind("Debug", "AutoTest", "",
                "Self-test without a person: 'host' loads the newest save and hosts on 127.0.0.1:Port; 'client' connects to it. Then the client takes the jobs " +
                "from AutoTestJobs and completes their call objective, the host opens the murder case; both log [AUTOTEST] and quit. Empty = off");
            CfgAutoTestJobs = Config.Bind("Debug", "AutoTestJobs", "19,5",
                "Self-test: job ids the client takes (from the host's save)");
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
            CfgWorldNeverPauses = Config.Bind("Npc", "WorldNeverPauses", true,
                "In multiplayer the pause menu, case board, inventory and other pausing windows do not stop the world: time and citizens keep going (the player still cannot move while a menu is open)");
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
            CfgDevCommands = Config.Bind("Debug", "DevCommands", false,
                "Test: read commands from BepInEx/sodcoop_cmd.txt (shot NAME, mp, char, close) - for UI checks");
            CfgDumpCitizen = Config.Bind("Debug", "DumpCitizen", false,
                "Test: log the animator parameters of some NPCs once after the world is loaded");

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<SodCoopDriver>();
                AddComponent<SodCoopDriver>();
                CrashLog.Start();
                L.LogInfo("[DRV] драйвер меню/игры запущен");
            }
            catch (Exception e) { L.LogWarning("[DRV] не удалось запустить драйвер: " + e.Message); }
            CfgProbeKey = Config.Bind("Debug", "ProbeKey", "", "Клавиша разведки для редактора персонажа, пусто = выключено (имя из UnityEngine.KeyCode, например F4, Backslash, Keypad0)");
            Harmony.CreateAndPatchAll(typeof(PlayerPatch));
            Harmony.CreateAndPatchAll(typeof(MeleePatch));
            Harmony.CreateAndPatchAll(typeof(BlockPatch));
            foreach (var t in new[] { typeof(ThrowFoodPatch), typeof(ThrowGrenadePatch), typeof(ThrowCoinPatch), typeof(PutDownPatch), typeof(EmptySlotPatch), typeof(PickUpPatch), typeof(DropPatch), typeof(SafeDeletePatch), typeof(DeletePatch), typeof(BreakPatch), typeof(DoorOpenPatch), typeof(DoorLockPatch), typeof(DoorJamPatch), typeof(DoorMoveOpenPatch), typeof(DoorMoveClosePatch), typeof(DoorMoveSetOpenPatch), typeof(LockStatePatch), typeof(SwitchStatePatch), typeof(CustomState1Patch), typeof(CustomState2Patch), typeof(CustomState3Patch), typeof(BreakerSetOpenPatch), typeof(ShatterPatch), typeof(WindowBreakPatch), typeof(AptTrace), typeof(RoomClearPatch), typeof(DecorPatch), typeof(NpcDamagePatch), typeof(SelfHurtFxPatch), typeof(SelfKoFxPatch), typeof(SelfRecoverFxPatch), typeof(SelfDeathFxPatch), typeof(SelfTripFxPatch), typeof(SeatGuardPatch), typeof(WorldSoundPatch), typeof(ShootPatch), typeof(NpcActAttackPatch), typeof(NpcActThrowPatch), typeof(NpcActAbortPatch), typeof(NpcActBlockPatch), typeof(NpcActRecoilPatch), typeof(NpcActHeldPatch), typeof(NpcActDespawnRPatch), typeof(NpcActDespawnLPatch), typeof(NpcActWeaponPatch), typeof(PuppetTrackPatch), typeof(PuppetPersuePatch), typeof(PuppetAttackPatch), typeof(PuppetInvestigatePatch), typeof(OutfitSeedPatch), typeof(PhysSeedPatch), typeof(FurnAddPatch1), typeof(FurnAddPatch2), typeof(ExecPlacePatch), typeof(SellFurnPatch), typeof(StoreFurnPatch), typeof(BuyResidencePatch), typeof(WeatherChangePatch), typeof(RoomLightsPatch), typeof(InteractActionPatch), typeof(InteractKeyPatch), typeof(InvisSightPatch), typeof(InvisIllegalPatch), typeof(GodPatch), typeof(KoTimeSpeedPatch), typeof(GameSavePatch), typeof(WorldLoadPatch), typeof(DownedKoPatch), typeof(DownedDamagePatch), typeof(PuppetLookPatch), typeof(ElevatorCallPatch), typeof(ProfileEventPatch), typeof(KnockPatch), typeof(BargePatch), typeof(JobCreationPatch), typeof(JobAcceptPatch), typeof(CaseResolvePatch), typeof(PinnedMenuPatch), typeof(ShareCommandPatch), typeof(PhoneCallLogPatch), typeof(PhoneCallLogPatch2), typeof(JobInfoPatch), typeof(ObjectiveCompletePatch), typeof(ObjectiveAddPatch), typeof(MurderClientBlockPatch), typeof(MurderCaseNotifyPatch), typeof(MurderDiscoveryPatch), typeof(BuildingAlarmPatch), typeof(AddressAlarmPatch), typeof(ComputerPatch), typeof(PagePatch), typeof(CaseStatusPatch), typeof(RestrainPatch), typeof(TickRatePatch), typeof(GooseChasePatch), typeof(WorldAlivePatch), typeof(KeepInvokesPatch), typeof(PauseReentryPatch), typeof(LookAroundPatch), typeof(CctvCapturePatch), typeof(KeyringPatch), typeof(JobDialogAddPatch), typeof(ObjectiveCancelPatch), typeof(RewardPatch), typeof(PuppetVisiblePatch), typeof(MenuComponentPatch), typeof(MenuComponentIntPatch), typeof(CheatMenuInteractionBlockPatch), typeof(CheatMenuItemBlockPatch), typeof(JobLoopPatch), typeof(JobPinPatch), typeof(JobGuestPassPatch), typeof(VentInPatch), typeof(VentOutPatch), typeof(PuppetTrespassPatch), typeof(MapMarkerPressPatch), typeof(GhostInventoryPatch), typeof(GhostSlotPatch), typeof(GhostPickUpPatch), typeof(GhostMoneyPatch), typeof(GhostSocialPatch), typeof(ArrestPersuePatch), typeof(ArrestAttackPatch), typeof(FameTrackPatch), typeof(FameTalkPatch), typeof(FameTalkReturnPatch), typeof(OutOfWorldPatch), typeof(SeaDeathPatch) })
            {
                try { Harmony.CreateAndPatchAll(t); }
                catch (Exception e) { L.LogWarning($"[PATCH] {t.Name}: {e.Message}"); }
            }
            L.LogInfo($"SodCoop загружен! Режим: {CfgMode.Value}");
        }
    }
}

using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Mirror;
using UnityEngine;

namespace BigWalk.Practice;

/// <summary>
/// Practice tool: spawn extra player bodies in a solo/hosted room and hot-swap
/// control between them. Modeled on the community 'Big Solo Walk' approach:
/// one connection, exactly one local player at a time, switched with
/// Mirror's NetworkServer.ReplacePlayerForConnection. The game keys camera,
/// audio listener, looks mode and footsteps off NetworkClient.localPlayer, so
/// the swap is a Replace + MakeRemote(old body) + MakeLocal(new body) handoff.
/// Host-only.
/// </summary>
[BepInPlugin(Guid, "Big Walk — Practice", "0.6.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bigwalk.practice";

    internal static Plugin Instance { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Trace;

    internal static ConfigEntry<int> MaxExtraBodies;
    internal static ConfigEntry<KeyCode> SpawnKey;
    internal static ConfigEntry<KeyCode> GatherKey;

    internal static ConfigEntry<string> SlotNames;
    internal static ConfigEntry<bool> ShowNameOverlay;

    internal static ConfigEntry<KeyCode> CheckpointSaveKey;
    internal static ConfigEntry<KeyCode> CheckpointRestoreKey;

    internal static ConfigEntry<bool> NoDrowsy;

    internal static ConfigEntry<KeyCode> NoclipKey;
    internal static ConfigEntry<float> NoclipSpeed;

    private Harmony _harmony;

    public override void Load()
    {
        Instance = this;
        Trace = Log;

        MaxExtraBodies = Config.Bind("Spawn", "MaxExtraBodies", 9,
            new ConfigDescription(
                "Maximum number of extra bodies (total = you + extras; number keys 1-0 select).",
                new AcceptableValueRange<int>(1, 9)));
        SpawnKey = Config.Bind("Keys", "SpawnKey", KeyCode.Plus,
            "Spawns a new body next to the active one and switches control to it. " +
            "Numpad + also works. Host only.");
        GatherKey = Config.Bind("Keys", "GatherKey", KeyCode.R,
            "Instantly teleport every inactive body to the active body.");

        SlotNames = Config.Bind("Slots", "SlotNames", "",
            "Optional comma-separated names per slot, matching the run's role assignments " +
            "(e.g. \"Bridge-A,Gate-Holder,Switch-2\"). Index 0 = the original player.");
        ShowNameOverlay = Config.Bind("Slots", "ShowNameOverlay", false,
            "Draw the active slot name in the top-left corner of the screen.");

        CheckpointSaveKey = Config.Bind("Keys", "CheckpointSaveKey", KeyCode.F5,
            "Save the current formation (every body's position) as a checkpoint.");
        CheckpointRestoreKey = Config.Bind("Keys", "CheckpointRestoreKey", KeyCode.F9,
            "Restore every body to the saved checkpoint positions.");

        NoDrowsy = Config.Bind("Practice", "NoDrowsy", true,
            "Keep the anti-AFK drowsy state off for every owned body. The game only " +
            "tracks input on the active body, so extras otherwise get stuck drowsy.");

        NoclipKey = Config.Bind("Keys", "NoclipKey", KeyCode.G,
            "Toggles body noclip on the active character (WASD + Space/Ctrl, Shift = fast).");
        NoclipSpeed = Config.Bind("Practice", "NoclipSpeed", 8f,
            new ConfigDescription("Base noclip movement speed (m/s).",
                new AcceptableValueRange<float>(1f, 40f)));

        // Belt and braces: stop non-local bodies driving cameras while Mirror's
        // ReplacePlayerForConnection swaps the local player.
        _harmony = new Harmony(Guid);
        _harmony.Patch(AccessTools.Method(typeof(PlayerCameraMinder), nameof(PlayerCameraMinder.Update), Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.CameraAllowedPrefix)));
        _harmony.Patch(AccessTools.Method(typeof(PlayerCameraMinder), nameof(PlayerCameraMinder.TakeCamera)),
            prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.CameraAllowedPrefix)));
        _harmony.Patch(AccessTools.Method(typeof(HouseNetworkManager), nameof(HouseNetworkManager.OnStopHost)),
            postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.NetworkStoppedPostfix)));

        _harmony.Patch(AccessTools.Method(typeof(HouseNetworkManager), nameof(HouseNetworkManager.OnStopClient)),
            postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.NetworkStoppedPostfix)));

        ClassInjector.RegisterTypeInIl2Cpp<PracticeController>();

        var host = new GameObject("BigWalk.Practice");
        host.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<PracticeController>();

        Log.LogInfo($"Loaded. {SpawnKey.Value}/Keypad+ = spawn, {GatherKey.Value} = gather, 1-0 = switch. " +
                    $"F5 save / F9 restore. Max extra bodies: {MaxExtraBodies.Value}.");
    }


    public override bool Unload()
    {
        PracticeController.ResetAll();
        _harmony?.UnpatchSelf();
        return true;
    }
}

internal static class Patches
{
    /// <summary>Only the body that IS the local player may drive its camera.</summary>
    internal static bool CameraAllowedPrefix(PlayerCameraMinder __instance)
    {
        if (__instance == null || !NetworkServer.activeHost)
        {
            return true;
        }

        try
        {
            var identity = __instance.playerCharacter?.GetComponent<NetworkIdentity>();
            var local = NetworkClient.localPlayer;
            if (identity == null || local == null)
            {
                return true;
            }

            return identity.netId == local.netId;
        }
        catch
        {
            return true;
        }
    }

    internal static void NetworkStoppedPostfix()
    {
        PracticeController.ResetAll();
    }
}
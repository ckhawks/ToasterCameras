using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace ToasterCameras;

public class Plugin : IPuckMod
{
    public static string MOD_NAME = "ToasterCameras";
    public static string MOD_VERSION = "1.0.0";
    public static string MOD_GUID = "pw.stellaric.toaster.cameras";

    static readonly Harmony harmony = new Harmony(MOD_GUID);

    public static List<PlayerCamera> becomePuckPlayerCameras = new List<PlayerCamera>();
    public static bool client_spectatorIsPuck = false;
    public static bool client_spectatorWatchPuck = false;
    public static bool client_spectatorWatchPuckAbove = false;
    public static bool client_spectatorWatchPuckSmart = false;
    public static bool client_spectatorWatchPuckSmart2 = false;
    public static bool client_spectatorWatchThirdPerson = false;
    public static bool client_spectatorStaticPositioning = false;
    public static string client_spectatorStaticPosition = "";
    public static bool client_cinematicSmoothingEnabled = false;
    public static InputAction cinematicSmoothingAction;
    // Cinematic smoothing state
    public static Vector3 _currentCinematicRotation = Vector3.zero; // Stores the smoothed rotation
    public static Vector3 _currentCinematicPosition = Vector3.zero; // Stores the smoothed position
    public static Vector3 _cinematicRotationVelocity = Vector3.zero; // For SmoothDamp rotational velocity
    public static Vector3 _cinematicPositionVelocity = Vector3.zero; // For SmoothDamp positional velocity
    public static Player thirdPersonPlayerToWatch = null;
    public static SpectatorCamera spectatorCamera;
    public static ModSettings modSettings;
    public static InputAction[] cameraPositionActions;
    public static InputAction becomePuckAction;
    public static InputAction watchPuckAction;
    public static InputAction watchPuckAboveAction;
    public static InputAction watchPuckSmartAction;
    public static InputAction watchPuckSmart2Action;
    public static InputAction watchOffAction;
    public static InputAction slowDownAction; // Add this line
    
    public bool OnEnable()
    {
        Plugin.Log($"Enabling...");
        try
        {
            if (IsDedicatedServer())
            {
                Plugin.Log("Environment: dedicated server.");
                Plugin.Log($"This mod is designed to be only used only on clients!");
            }
            else
            {
                Plugin.Log("Environment: client.");
                harmony.PatchAll();
                LogAllPatchedMethods();
                StatsToFiles.Setup();
                modSettings = ModSettings.Load();
                modSettings.Save();
                // Dump keybinds for user reference
                KeybindDumper.DumpAllKeybinds();
                CameraKeybinds.InitializeCameraPositionKeybinds();
                CameraKeybinds.InitializeCameraModeKeybinds();
                CameraKeybinds.InitializePlayerWatchKeybinds();
            }
            
            Plugin.Log($"Enabled!");
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogError($"Failed to Enable: {e.Message}!");
            return false;
        }
    }

    public bool OnDisable()
    {
        try
        {
            Plugin.Log($"Disabling...");
            harmony.UnpatchSelf();

            Plugin.Log($"Disabled! Goodbye!");
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogError($"Failed to disable: {e.Message}!");
            return false;
        }
    }

    public static bool IsDedicatedServer()
    {
        return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }
    
    public static void LogAllPatchedMethods()
    {
        var allPatchedMethods = harmony.GetPatchedMethods();
        var pluginId  = harmony.Id;

        var mine = allPatchedMethods
            .Select(m => new { method = m, info = Harmony.GetPatchInfo(m) })
            .Where(x =>
                // could be prefix, postfix, transpiler or finalizer
                x.info.Prefixes.  Any(p => p.owner == pluginId) ||
                x.info.Postfixes. Any(p => p.owner == pluginId) ||
                x.info.Transpilers.Any(p => p.owner == pluginId) ||
                x.info.Finalizers.Any(p => p.owner == pluginId)
            )
            .Select(x => x.method);

        foreach (var m in mine)
            Plugin.Log($" - {m.DeclaringType.FullName}.{m.Name}");
    }

    public static void Log(string message)
    {
        Debug.Log($"[{MOD_NAME}] {message}");
    }

    public static void LogError(string message)
    {
        Debug.LogError($"[{MOD_NAME}] {message}");
    }
}
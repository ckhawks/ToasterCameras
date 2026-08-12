using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace ToasterCameras;

// The spectator camera modes are mutually exclusive — at most one is active at a
// time. Tracking them as a single enum (rather than a fan of parallel bools)
// makes "switch to mode X" a single assignment that can't leave two modes half-on.
public enum CameraMode
{
    None,             // game's default camera behavior (no override)
    Puck,             // camera parented to the puck (/bep)
    PuckFreeLook,     // camera rides the puck's position, free mouse aim (/bepf)
    WatchPuck,        // free-fly while looking at the puck (/wp)
    WatchPuckGrid,    // auto-framed lead tracking (/wpg)
    WatchPuckAbove,   // top-down follow (/wpa)
    WatchPuckSmart,   // corner auto-switch (/wps)
    WatchPuckSmart2,  // nearest-of-many auto-switch (/wps2)
    WatchThirdPerson, // chase a specific player (/wpl)
    WatchCarrier,     // chase whoever currently has the puck (/wc)
    Director,         // autonomous hybrid broadcast director (/director)
    StaticPosition,   // fixed preset position (/cpos)
}

public class Plugin : IPuckPlugin
{
    public static string MOD_NAME = "ToasterCameras";
    public static string MOD_VERSION = "1.0.1";
    public static string MOD_GUID = "pw.stellaric.toaster.cameras";

    static readonly Harmony harmony = new Harmony(MOD_GUID);

    public static List<PlayerCamera> becomePuckPlayerCameras = new List<PlayerCamera>();

    // The single source of truth for which spectator camera mode is active.
    // Use SetCameraMode() to change it so the camera always gets un-parented when
    // leaving puck-follow.
    public static CameraMode cameraMode = CameraMode.None;

    // Switch to a camera mode, clearing any previous one. Every mode except Puck
    // runs with the spectator camera un-parented; the Puck caller re-parents the
    // camera to the puck transform itself after calling this.
    public static void SetCameraMode(CameraMode mode)
    {
        cameraMode = mode;
        if (mode != CameraMode.Puck && spectatorCamera != null &&
            spectatorCamera.transform.parent != null)
            spectatorCamera.transform.SetParent(null);
    }

    // /wpg rotation clamps. Pitch.x in Unity is positive when looking down, so the
    // "max look-up" clamp is expressed as a negative pitch floor (e.g. -15 means
    // the camera can tilt up to 15° above horizontal but no further).
    public static float watchPuckGridMaxLookUpDeg = 15f;
    public static float watchPuckGridYawDeviationDeg = 75f;
    // /wpg leads the camera to where the puck is going. The aim point is the
    // puck's current position projected forward by the puck's horizontal
    // velocity over this many seconds — so the area in front of the puck
    // gets framed instead of the puck itself.
    public static float watchPuckGridLeadSeconds = 1.5f;
    // Exponential smoothing factor (per second) for the velocity used to
    // compute the lead point. Higher = reacts faster, more twitchy on bounces;
    // lower = laggier but steadier framing. ~3-5 feels right.
    public static float watchPuckGridLeadVelocitySmoothing = 2f;
    // Maximum angular separation (in degrees, from the camera) between the puck
    // and the lead aim point. Caps how far the lead can push so the puck itself
    // doesn't get pushed outside the center cell of the 3x3 framing grid. A
    // value near ~half of the center cell's angular size in the current FOV
    // (≈ vertical FOV / 6) works well; 10° is a safe default for ~60° FOV.
    public static float watchPuckGridMaxLeadAngleDeg = 10f;
    public static Vector3 watchPuckGridSmoothedVelocity = Vector3.zero;
    public static bool client_dynamicFovEnabled = false;
    // Dynamic FOV bounds (degrees) and distance bounds (meters from camera to puck).
    public static float dynamicFovNearFov = 70f;
    public static float dynamicFovFarFov = 24f;
    public static float dynamicFovNearDistance = 4f;
    public static float dynamicFovFarDistance = 55f;
    public static float dynamicFovSmoothTime = 0.35f;
    public static float _dynamicFovCurrent = 60f;
    public static float _dynamicFovVel;
    public static float _dynamicFovOriginal = -1f;
    // Scroll-wheel zoom: optional manual FOV control. When enabled, the mouse
    // wheel nudges scrollZoomTargetFov (scroll up = zoom in = smaller FOV) and
    // the camera smooth-damps toward it. Independent of dynamic FOV; when both
    // are toggled on, scroll zoom takes precedence. scrollZoomNeedsInit makes
    // the target seed from the live FOV the first tick after it's enabled.
    public static bool client_scrollZoomEnabled = false;
    public static bool scrollZoomNeedsInit = false;
    public static float scrollZoomTargetFov = 60f;
    // Multiplicative per notch, not additive: a fixed degree step feels
    // imperceptible at the wide end and violent at the tight end. 1.1 gives
    // even-feeling zoom across the whole range (~33 notches min to max).
    public static float scrollZoomStepFactor = 1.1f;
    // Unity clamps fieldOfView to 1..179; stay inside that with room to spare.
    public static float scrollZoomMinFov = 5f;
    public static float scrollZoomMaxFov = 120f;
    public static float scrollZoomSmoothTime = 0.12f;
    // Which preset is active while cameraMode == StaticPosition.
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
    public static InputAction becomePuckFreeAction;
    public static InputAction watchPuckAction;
    public static InputAction watchPuckGridAction;
    public static InputAction watchPuckAboveAction;
    public static InputAction watchPuckSmartAction;
    public static InputAction watchPuckSmart2Action;
    public static InputAction watchCarrierAction;
    public static InputAction directorAction;
    public static InputAction watchOffAction;

    // /wc: how long (seconds) a new player must be the puck's last-toucher before
    // the carrier camera commits to following them. Debounces deflections/battles.
    public static float watchCarrierSwitchHold = 0.5f;
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
                if (modSettings.possessionCircle != null)
                    PuckPossessionIndicator.opacity = Mathf.Clamp01(modSettings.possessionCircle.opacity);

                // Restore persisted feature toggles to their live state (they reset to defaults
                // otherwise). The chat commands and TRL menu both write these back when changed.
                PuckBeam.enabled = modSettings.puckBeamOn;
                PuckPossessionIndicator.enabled = modSettings.possessionDiscOn;
                client_dynamicFovEnabled = modSettings.dynamicFovEnabled;
                client_scrollZoomEnabled = modSettings.scrollZoomEnabled;
                if (client_scrollZoomEnabled) scrollZoomNeedsInit = true;
                // Dump keybinds for user reference
                KeybindDumper.DumpAllKeybinds();
                CameraKeybinds.InitializeCameraPositionKeybinds();
                CameraKeybinds.InitializeCameraModeKeybinds();
                CameraKeybinds.InitializePlayerWatchKeybinds();

                // Hidden GameObject that drives PuckPossessionIndicator each frame.
                // FixedUpdate would only fire at the physics rate (and only on the
                // server for puck physics), so a regular Update is what gives smooth
                // visuals on clients.
                var runnerGo = new GameObject("ToasterCamerasIndicatorRunner");
                runnerGo.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(runnerGo);
                runnerGo.AddComponent<PuckIndicatorRunner>();

                // Contribute a Cameras page to ToasterReskinLoader's menu when TRL is present
                // (soft dependency — no-ops if it isn't installed).
                TRLSettingsPanel.TryRegister();
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
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using HarmonyLib.Tools;

namespace ToasterCameras;

[BepInPlugin("pw.stellaric.plugins.toastercameras", "Toaster Cameras", "1.0.0.0")]
public class Plugin : BasePlugin
{
    // the "configurable" things
    private readonly Harmony _harmony = new Harmony("pw.stellaric.plugins.toastercameras");
    
    // plugin managers
    public static new ManualLogSource Log;
    public static UIChat chat;

    public static PlayerManager playerManager;
    public static PuckManager puckManager;
    
    // client-side spectator state
    public static List<PlayerCamera> becomePuckPlayerCameras = new List<PlayerCamera>();
    public static bool client_spectatorIsPuck = false;
    public static bool client_spectatorWatchPuck = false;
    public static bool client_spectatorWatchPuckAbove = false;
    public static bool client_spectatorWatchPuckSmart = false;
    public static bool client_spectatorWatchPuckSmart2 = false;
    public static bool client_spectatorWatchThirdPerson = false;
    public static Player thirdPersonPlayerToWatch = null;
    public static SpectatorCamera spectatorCamera;
    
    
    public override void Load()
    {
        HarmonyFileLog.Enabled = true;
        
        // Plugin startup logic
        Log = base.Log;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded! Patching methods...");
        _harmony.PatchAll();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is all patched! Patched methods:");
        
        var originalMethods = Harmony.GetAllPatchedMethods();
        foreach (var method in originalMethods)
        {
            Log.LogInfo($" - {method.DeclaringType.FullName}.{method.Name}");
        }
    }
}
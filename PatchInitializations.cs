using HarmonyLib;
using ToasterCameras;

namespace ToasterTeamColors
{
    public static class PatchInitializations
    {
        [HarmonyPatch(typeof(UIChat), "Start")]
        class PatchUIChatStart
        {
            static void Prefix()
            {
                Plugin.Log.LogInfo($"Patch: UIChatStart (Prefix) was called.");
                Plugin.chat = UIChat.Instance;
            }
        }
        
        [HarmonyPatch(typeof(PlayerManagerController), nameof(PlayerManagerController.Start))]
        public class PatchPlayerManagerControllerOnServerStart
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerManagerController __instance)
            {
                Plugin.playerManager = __instance.playerManager;
            }
        }
        
        [HarmonyPatch(typeof(PuckManager), nameof(PuckManager.Server_SpawnPucksForPhase))]
        public static class PatchPuckManagerServerSpawnPucksForPhase
        {
            [HarmonyPrefix]
            public static void Prefix(PuckManager __instance, GamePhase phase)
            {
                Plugin.Log.LogInfo($"Patch: PuckManager.Server_SpawnPucksForPhase (Prefix) was called.");
                Plugin.puckManager = __instance;
            }
        }
        
        [HarmonyPatch(typeof(PuckManager), MethodType.Constructor)]
        public static class PatchPuckManagerAwake
        {
            [HarmonyPrefix]
            public static bool Prefix(PuckManager __instance)
            {
                Plugin.Log.LogInfo($"Patch: PuckManager.Constructor (Prefix) was called.");
                Plugin.puckManager = __instance;
                return true;
            }
        }
        
        [HarmonyPatch(typeof(PuckManagerController), nameof(PuckManagerController.Start))]
        public static class PatchPuckManagerControllerStart
        {
            [HarmonyPostfix]
            public static void Postfix(PuckManagerController __instance)
            {
                Plugin.Log.LogInfo($"Patch: PuckManagerController.Start (Postfix) was called.");
                Plugin.puckManager = __instance.puckManager;
                return;
            }
        }
    }
    
}


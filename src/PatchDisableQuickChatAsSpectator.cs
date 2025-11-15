using HarmonyLib;

namespace ToasterCameras;

public static class PatchDisableQuickChatAsSpectator
{
    // [HarmonyPatch(typeof(PlayerInput), "Update")]
    // class PatchPlayerInputUpdate
    // {
    //     [HarmonyPrefix]
    //     static bool Prefix(PlayerInput __instance)
    //     {
    //         if (Plugin.modSettings.disableQuickChatsInSpectator)
    //         {
    //             // if the player is currently in spectator mode
    //             PlayerManager pm = PlayerManager.Instance;
    //             if (pm.GetLocalPlayer().Team.Value != PlayerTeam.Blue || pm.GetLocalPlayer().Team.Value != PlayerTeam.Red)
    //             {
    //                 Plugin.Log($"Ignroing quick");
    //                 return false;
    //             }
    //         }
    //
    //         return true;
    //     }
    // }

    [HarmonyPatch(typeof(UIChat), nameof(UIChat.OpenQuickChat))]
    class PatchDisableQuickChatAsSpectatorInner
    {
        [HarmonyPrefix]
        static bool Prefix(UIChat __instance)
        {
            if (Plugin.modSettings.disableQuickChatsInSpectator)
            {
                // if the player is currently in spectator mode
                PlayerManager pm = PlayerManager.Instance;
                if (pm.GetLocalPlayer().Team.Value != PlayerTeam.Blue && pm.GetLocalPlayer().Team.Value != PlayerTeam.Red)
                {
                    Plugin.Log($"Ignroing quick chat input because is in spectator");
                    return false;
                }
            }

            return true;
        }
    }
}
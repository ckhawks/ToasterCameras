using HarmonyLib;

namespace ToasterCameras;

public static class PatchDisableQuickChatAsSpectator
{
    // In b323 the number-key flow goes through ChatManager.Client_QuickChatAction,
    // which calls Client_SendChatMessageRpc directly — bypassing the public
    // Client_SendChatMessage. Block at Client_QuickChatAction so both the
    // category-open (first press) and the quick-chat-pick (second press) are
    // suppressed for spectators.
    [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.Client_QuickChatAction))]
    private class PatchChatManagerClientQuickChatAction
    {
        [HarmonyPrefix]
        private static bool Prefix(int index)
        {
            if (Plugin.modSettings == null || !Plugin.modSettings.disableQuickChatsInSpectator) return true;

            PlayerManager pm = PlayerManager.Instance;
            if (pm == null) return true;

            var local = pm.GetLocalPlayer();
            if (local == null) return true;

            if (local.Team != PlayerTeam.Blue && local.Team != PlayerTeam.Red)
            {
                Plugin.Log($"Ignoring quick chat action ({index}) because local player is in spectator");
                return false;
            }

            return true;
        }
    }

    // Belt-and-suspenders: other mods (e.g. ToasterQuickChatPlus) may call
    // SetQuickChatEnabled directly to open the quick-chat category UI, bypassing
    // Client_QuickChatAction. Block any "enable" call while local player is spectator.
    [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.SetQuickChatEnabled))]
    private class PatchChatManagerSetQuickChatEnabled
    {
        [HarmonyPrefix]
        private static bool Prefix(bool isEnabled)
        {
            if (!isEnabled) return true;
            if (Plugin.modSettings == null || !Plugin.modSettings.disableQuickChatsInSpectator) return true;

            PlayerManager pm = PlayerManager.Instance;
            if (pm == null) return true;

            var local = pm.GetLocalPlayer();
            if (local == null) return true;

            if (local.Team != PlayerTeam.Blue && local.Team != PlayerTeam.Red)
            {
                Plugin.Log("Ignoring SetQuickChatEnabled(true) because local player is in spectator");
                return false;
            }

            return true;
        }
    }
}

using HarmonyLib;

namespace ToasterCameras;

public static class PatchDisableQuickChatAsSpectator
{
    // True when the "disable quick chats while spectating" option is on and the
    // local player is currently a spectator (not on Blue/Red). Shared by every
    // quick-chat block point below and by ClientChat's send-message patch.
    public static bool ShouldSuppressLocalQuickChat()
    {
        if (Plugin.modSettings == null || !Plugin.modSettings.disableQuickChatsInSpectator) return false;

        var pm = PlayerManager.Instance;
        if (pm == null) return false;

        var local = pm.GetLocalPlayer();
        if (local == null) return false;

        return local.Team != PlayerTeam.Blue && local.Team != PlayerTeam.Red;
    }

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
            if (ShouldSuppressLocalQuickChat())
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

            if (ShouldSuppressLocalQuickChat())
            {
                Plugin.Log("Ignoring SetQuickChatEnabled(true) because local player is in spectator");
                return false;
            }

            return true;
        }
    }
}

using HarmonyLib;

namespace ToasterCameras;

public static class PatchDisableQuickChatAsSpectator
{
    // In b312 there's no UIChat.OpenQuickChat; quick chats flow through
    // ChatManager.Client_SendChatMessage with isQuickChat=true.
    [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.Client_SendChatMessage))]
    private class PatchChatManagerClientSendChatMessageQuick
    {
        [HarmonyPrefix]
        private static bool Prefix(string content, bool isQuickChat, bool isTeamChat)
        {
            if (!isQuickChat) return true;
            if (Plugin.modSettings == null || !Plugin.modSettings.disableQuickChatsInSpectator) return true;

            PlayerManager pm = PlayerManager.Instance;
            if (pm == null) return true;

            var local = pm.GetLocalPlayer();
            if (local == null) return true;

            if (local.Team != PlayerTeam.Blue && local.Team != PlayerTeam.Red)
            {
                Plugin.Log("Ignoring quick chat because local player is in spectator");
                return false;
            }

            return true;
        }
    }
}

using Unity.Collections;

namespace ToasterCameras;

// Helper for posting client-side system messages into UIChat.
// In b312, UIChat.AddChatMessage takes a ChatMessage struct rather than a string.
public static class ChatHelper
{
    public static void AddSystemMessage(string content)
    {
        var chat = UIManager.Instance != null ? UIManager.Instance.Chat : null;
        if (chat == null) return;

        var msg = new ChatMessage
        {
            Content = new FixedString512Bytes(content),
            IsSystem = true,
            IsQuickChat = false,
            IsTeamChat = false,
            Team = null,
            Username = null,
            SteamID = null
        };
        chat.AddChatMessage(msg, SettingsManager.Units, false);
    }
}

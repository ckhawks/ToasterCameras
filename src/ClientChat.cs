// ClientChat.cs

using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ToasterCameras;

public static class ClientChat
{
    // In b312 chat sending moved out of UIChat onto ChatManager.
    // Signature: Client_SendChatMessage(string content, bool isQuickChat, bool isTeamChat)
    [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.Client_SendChatMessage))]
    private class PatchChatManagerClientSendChatMessage
    {
        [HarmonyPrefix]
        private static bool Prefix(ChatManager __instance, string content, bool isQuickChat, bool isTeamChat)
        {
            // Don't intercept quick chats here — let them flow through.
            if (isQuickChat) return true;

            if (string.IsNullOrEmpty(content)) return true;
            string[] messageParts = content.Split(' ');

            if (messageParts[0].Equals("/becomepuck", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/bep", StringComparison.OrdinalIgnoreCase))
            {
                // Don't SetParent onto the puck — it's a NetworkObject, so the client
                // reparent spams "only the server can re-parent" every frame. TickPuck
                // already copies the puck's position/rotation each frame.
                Plugin.SetCameraMode(CameraMode.Puck);
                return false;
            }

            if (messageParts[0].Equals("/watchpuck", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wp", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.SetCameraMode(CameraMode.WatchPuck);
                return false;
            }

            if (messageParts[0].Equals("/watchpuckgrid", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wpg", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckGrid);
                return false;
            }

            if (messageParts[0].Equals("/watchpuckabove", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wpa", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckAbove);
                return false;
            }

            if (messageParts[0].Equals("/watchpucksmart", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wps", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckSmart);
                return false;
            }

            if (messageParts[0].Equals("/watchpucksmart2", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wps2", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wpss", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckSmart2);
                return false;
            }

            if (messageParts[0].Equals("/watchplayer", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wpl", StringComparison.OrdinalIgnoreCase))
            {
                if (messageParts.Length >= 2)
                {
                    int playerNumber = -1;
                    Player playerToWatch = null;
                    try
                    {
                        playerNumber = Int32.Parse(messageParts[1]);
                    }
                    catch (FormatException)
                    {
                        // could not parse to int
                    }

                    if (playerNumber != -1)
                    {
                        Player playerByNumber = PlayerManager.Instance.GetPlayerByNumber(playerNumber);
                        if (playerByNumber != null)
                        {
                            playerToWatch = playerByNumber;
                        }
                    }

                    if (playerToWatch == null)
                    {
                        Player playerByName = PlayerManager.Instance.GetPlayerByUsername(string.Join(" ", messageParts.Skip(1)));
                        if (playerByName != null)
                        {
                            playerToWatch = playerByName;
                        }
                    }

                    if (playerToWatch == null)
                    {
                        ChatHelper.AddSystemMessage(
                            $"<s>-></s> <size=16><color=red>Could not find a user to watch with <b>{string.Join(" ", messageParts.Skip(1))}</b>.</color></size>");
                        return false;
                    }

                    Plugin.thirdPersonPlayerToWatch = playerToWatch;
                    Plugin.SetCameraMode(CameraMode.WatchThirdPerson);
                    return false;
                }

                if (Plugin.cameraMode != CameraMode.WatchThirdPerson)
                {
                    ChatHelper.AddSystemMessage(
                        $"<s>-></s> <size=16><color=red>Please specify a <b>name</b> or <b>number</b>.</color></size>");
                    return false;
                }
                Plugin.SetCameraMode(CameraMode.None);
                return false;
            }

            if (messageParts[0].Equals("/watchoff", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wo", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.SetCameraMode(CameraMode.None);
                return false;
            }

            if (messageParts[0].Equals("/cpos", StringComparison.OrdinalIgnoreCase))
            {
                if (messageParts.Length < 2)
                {
                    ChatHelper.AddSystemMessage("You must say what position you would like the camera to move to. /cpos [position]");
                    return false;
                }

                if (!Plugin.modSettings.cameraPositions.ContainsKey(messageParts[1].ToLower()))
                {
                    ChatHelper.AddSystemMessage($"That is not a valid static camera position. Options: {string.Join(" ", Plugin.modSettings.cameraPositions.Keys.ToList())}");
                    return false;
                }

                Plugin.client_spectatorStaticPosition = messageParts[1].ToLower();
                Plugin.SetCameraMode(CameraMode.StaticPosition);
                return false;
            }

            if (messageParts[0].Equals("/beam", StringComparison.OrdinalIgnoreCase))
            {
                PuckBeam.enabled = !PuckBeam.enabled;
                if (!PuckBeam.enabled)
                {
                    if (PuckManager.Instance != null)
                        foreach (var p in PuckManager.Instance.GetPucks(false))
                            PuckBeam.Cleanup(p);
                }
                if (Plugin.modSettings != null)
                {
                    Plugin.modSettings.puckBeamOn = PuckBeam.enabled;
                    Plugin.modSettings.Save();
                }
                ChatHelper.AddSystemMessage(
                    $"<s>-></s> Puck beam {(PuckBeam.enabled ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
                return false;
            }

            if (messageParts[0].Equals("/dfov", StringComparison.OrdinalIgnoreCase) ||
                messageParts[0].Equals("/dynamicfov", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.client_dynamicFovEnabled = !Plugin.client_dynamicFovEnabled;
                if (Plugin.modSettings != null)
                {
                    Plugin.modSettings.dynamicFovEnabled = Plugin.client_dynamicFovEnabled;
                    Plugin.modSettings.Save();
                }
                ChatHelper.AddSystemMessage(
                    $"<s>-></s> Dynamic FOV {(Plugin.client_dynamicFovEnabled ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
                return false;
            }

            if (messageParts[0].Equals("/circleopacity", StringComparison.OrdinalIgnoreCase) ||
                messageParts[0].Equals("/copacity", StringComparison.OrdinalIgnoreCase))
            {
                if (messageParts.Length < 2 ||
                    !float.TryParse(messageParts[1], out var op))
                {
                    ChatHelper.AddSystemMessage(
                        $"<s>-></s> Possession circle opacity is <b>{PuckPossessionIndicator.opacity:0.00}</b>. Usage: /circleopacity [0-1]");
                    return false;
                }

                PuckPossessionIndicator.opacity = Mathf.Clamp01(op);
                if (Plugin.modSettings != null)
                {
                    Plugin.modSettings.possessionCircle ??= new PossessionCircleSettings();
                    Plugin.modSettings.possessionCircle.opacity = PuckPossessionIndicator.opacity;
                    Plugin.modSettings.Save();
                }
                ChatHelper.AddSystemMessage(
                    $"<s>-></s> Possession circle opacity set to <b>{PuckPossessionIndicator.opacity:0.00}</b> (saved).");
                return false;
            }

            if (messageParts[0].Equals("/zoom", StringComparison.OrdinalIgnoreCase) ||
                messageParts[0].Equals("/scrollzoom", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.client_scrollZoomEnabled = !Plugin.client_scrollZoomEnabled;
                if (Plugin.client_scrollZoomEnabled)
                    Plugin.scrollZoomNeedsInit = true;
                if (Plugin.modSettings != null)
                {
                    Plugin.modSettings.scrollZoomEnabled = Plugin.client_scrollZoomEnabled;
                    Plugin.modSettings.Save();
                }
                ChatHelper.AddSystemMessage(
                    $"<s>-></s> Scroll-wheel zoom {(Plugin.client_scrollZoomEnabled ? "<color=green>enabled</color> — use the mouse wheel to zoom" : "<color=red>disabled</color>")}.");
                return false;
            }

            if (messageParts[0].Equals("/wpgdebug", StringComparison.OrdinalIgnoreCase) ||
                messageParts[0].Equals("/wpgd", StringComparison.OrdinalIgnoreCase))
            {
                WatchPuckGridDebug.SetEnabled(!WatchPuckGridDebug.enabled);
                ChatHelper.AddSystemMessage(
                    $"<s>-></s> /wpg diagnostics {(WatchPuckGridDebug.enabled ? "<color=green>enabled</color> — world markers + on-screen HUD + ~1/s logs" : "<color=red>disabled</color>")}." +
                    (WatchPuckGridDebug.enabled && Plugin.cameraMode != CameraMode.WatchPuckGrid
                        ? " <color=yellow>Switch to /wpg to see it.</color>" : ""));
                return false;
            }

            if (messageParts[0].Equals("/where", StringComparison.OrdinalIgnoreCase))
            {
                PatchPlayerCamera.PrintCameraCoordinates();
                return false;
            }

            return true;
        }
    }
}

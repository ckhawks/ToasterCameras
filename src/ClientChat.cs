// ClientChat.cs

using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ToasterCameras;

public static class ClientChat
{
    private static void DisableAllCameraModes()
    {
        Plugin.client_spectatorWatchPuck = false;
        Plugin.client_spectatorWatchPuckSmart = false;
        Plugin.client_spectatorWatchThirdPerson = false;
        Plugin.client_spectatorIsPuck = false;
        Plugin.client_spectatorWatchPuckAbove = false;
        Plugin.client_spectatorWatchPuckSmart2 = false;
        Plugin.client_spectatorStaticPositioning = false;
        Plugin.client_spectatorStaticPosition = "";
    }

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
                DisableAllCameraModes();
                Plugin.client_spectatorIsPuck = true;
                if (Plugin.spectatorCamera != null && PuckManager.Instance != null && PuckManager.Instance.GetPuck() != null)
                {
                    Plugin.spectatorCamera.transform.SetParent(PuckManager.Instance.GetPuck().transform);
                    Plugin.spectatorCamera.transform.localPosition = Vector3.zero;
                    Plugin.spectatorCamera.transform.localRotation = Quaternion.identity;
                }
                return false;
            }

            if (messageParts[0].Equals("/watchpuck", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wp", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuck = true;
                return false;
            }

            if (messageParts[0].Equals("/watchpuckabove", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wpa", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckAbove = true;
                return false;
            }

            if (messageParts[0].Equals("/watchpucksmart", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wps", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart = true;
                return false;
            }

            if (messageParts[0].Equals("/watchpucksmart2", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wps2", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wpss", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart2 = true;
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

                    DisableAllCameraModes();
                    Plugin.thirdPersonPlayerToWatch = playerToWatch;
                    Plugin.client_spectatorWatchThirdPerson = true;
                    return false;
                }

                if (Plugin.client_spectatorWatchThirdPerson == false)
                {
                    ChatHelper.AddSystemMessage(
                        $"<s>-></s> <size=16><color=red>Please specify a <b>name</b> or <b>number</b>.</color></size>");
                    return false;
                }
                Plugin.client_spectatorWatchThirdPerson = false;
                return false;
            }

            if (messageParts[0].Equals("/watchoff", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wo", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
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

                DisableAllCameraModes();
                Plugin.client_spectatorStaticPosition = messageParts[1].ToLower();
                Plugin.client_spectatorStaticPositioning = true;
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

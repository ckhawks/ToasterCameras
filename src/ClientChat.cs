using System;
using System.Linq;
using HarmonyLib;
using ToasterConnectWhileFull;
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
    }
    
    [HarmonyPatch(typeof(UIChat), nameof(UIChat.Client_SendClientChatMessage))]
    private class PatchUIChatClientSendClientChatMessage
    {
        [HarmonyPrefix]
        private static bool Prefix(UIChat __instance, string message)
        {
            // Plugin.Log($"Patch: UIChat.Client_SendClientChatMessage (Prefix) was called.");
            string[] messageParts = message.Split(' ');

            if (messageParts[0].Equals("/becomepuck", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/bep", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorIsPuck = true;
                // Reparent the spectator camera to the puck
                Plugin.spectatorCamera.transform.SetParent(PuckManager.Instance.GetPuck().transform);

                // Optionally reset the local position and rotation of the camera relative to the puck
                Plugin.spectatorCamera.transform.localPosition = Vector3.zero; // Center the camera on the puck
                Plugin.spectatorCamera.transform.localRotation =
                    Quaternion.identity; // Align the camera's rotation with the puck

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
            }

            if (messageParts[0].Equals("/watchpucksmart", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wps", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart = true;
            }
            
            if (messageParts[0].Equals("/watchpucksmart2", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wps2", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wpss", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart2 = true;
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

                    // If it's still null
                    if (playerToWatch == null)
                    {
                        __instance.AddChatMessage(
                            $"<s>-></s> <size=16><color=red>Could not find a user to watch with <b>{string.Join(" ", messageParts.Skip(1))}</b>.</color></size>");
                        return false;
                    }

                    DisableAllCameraModes();
                    Plugin.thirdPersonPlayerToWatch = playerToWatch;
                    Plugin.client_spectatorWatchThirdPerson = true;
                    return true;
                }
                
                if (Plugin.client_spectatorWatchThirdPerson == false)
                {
                    __instance.AddChatMessage(
                        $"<s>-></s> <size=16><color=red>Please specify a <b>name</b> or <b>number</b>.</color></size>");
                    return false;
                }
                Plugin.client_spectatorWatchThirdPerson = false;
            }

            if (messageParts[0].Equals("/watchoff", StringComparison.OrdinalIgnoreCase) || messageParts[0].Equals("/wo", StringComparison.OrdinalIgnoreCase))
            {
                DisableAllCameraModes();
            }
            
            return true;
        }
    }
}
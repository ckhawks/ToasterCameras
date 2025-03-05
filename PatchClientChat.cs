using System;
using System.Linq;
using HarmonyLib;
using ToasterCameras;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UIElements;

namespace ToasterTeamColors;

public class PatchClientChat
{
    public static void DisableAllCameraModes()
    {
        Plugin.client_spectatorWatchPuck = false;
        Plugin.client_spectatorWatchPuckSmart = false;
        Plugin.client_spectatorWatchThirdPerson = false;
        Plugin.client_spectatorIsPuck = false;
        Plugin.client_spectatorWatchPuckAbove = false;
        Plugin.client_spectatorWatchPuckSmart2 = false;
    }
    
    [HarmonyPatch(typeof(UIChat), nameof(UIChat.Client_SendClientChatMessage))]
    class PatchUIChatClientSendClientChatMessage
    {
        [HarmonyPrefix]
        static bool Prefix(UIChat __instance, string message)
        {
            Plugin.Log.LogInfo($"Patch: UIChat.Client_SendClientChatMessage (Prefix) was called.");
            Plugin.chat = __instance;
            string[] messageParts = message.Split(' ');

            if (messageParts[0].InvariantEqualsIgnoreCase("/becomepuck") || messageParts[0].InvariantEqualsIgnoreCase("/bep"))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorIsPuck = true;
                // Reparent the spectator camera to the puck
                Plugin.spectatorCamera.transform.SetParent(Plugin.puckManager.GetPuck().transform);

                // Optionally reset the local position and rotation of the camera relative to the puck
                Plugin.spectatorCamera.transform.localPosition = Vector3.zero; // Center the camera on the puck
                Plugin.spectatorCamera.transform.localRotation =
                    Quaternion.identity; // Align the camera's rotation with the puck

                return false;
            }

            if (messageParts[0].InvariantEqualsIgnoreCase("/watchpuck") || messageParts[0].InvariantEqualsIgnoreCase("/wp"))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuck = true;
                return false;
            }

            if (messageParts[0].InvariantEqualsIgnoreCase("/watchpuckabove") || messageParts[0].InvariantEqualsIgnoreCase("/wpa"))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckAbove = true;
            }

            if (messageParts[0].InvariantEqualsIgnoreCase("/watchpucksmart") || messageParts[0].InvariantEqualsIgnoreCase("/wps"))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart = true;
            }
            
            if (messageParts[0].InvariantEqualsIgnoreCase("/watchpucksmart2") || messageParts[0].InvariantEqualsIgnoreCase("/wps2") || messageParts[0].InvariantEqualsIgnoreCase("/wpss"))
            {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart2 = true;
            }
            
            if (messageParts[0].InvariantEqualsIgnoreCase("/watchplayer") || messageParts[0].InvariantEqualsIgnoreCase("/wpl"))
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
                        Player playerByNumber = Plugin.playerManager.GetPlayerByNumber(playerNumber);
                        if (playerByNumber != null)
                        {
                            playerToWatch = playerByNumber;

                        }
                    }

                    if (playerToWatch == null)
                    {
                        Player playerByName = Plugin.playerManager.GetPlayerByUsername(string.Join(" ", messageParts.Skip(1)));
                        if (playerByName != null)
                        {
                            playerToWatch = playerByName;
                        }
                    }

                    // If it's still null
                    if (playerToWatch == null)
                    {
                        Plugin.chat.AddChatMessage(
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
                    Plugin.chat.AddChatMessage(
                        $"<s>-></s> <size=16><color=red>Please specify a <b>name</b> or <b>number</b>.</color></size>");
                    return false;
                }
                Plugin.client_spectatorWatchThirdPerson = false;
            }

            if (messageParts[0].InvariantEqualsIgnoreCase("/watchoff") || messageParts[0].InvariantEqualsIgnoreCase("/wo"))
            {
                DisableAllCameraModes();
            }
            
            return true;
        }
    }
}
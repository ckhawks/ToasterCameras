using System;
using System.Linq;
using HarmonyLib;
using ToasterCameras;
using UnityEngine;
using UnityEngine.UIElements;

namespace ToasterTeamColors;

public class PatchClientChat
{
    [HarmonyPatch(typeof(UIChat), nameof(UIChat.Client_SendClientChatMessage))]
    class PatchUIChatClientSendClientChatMessage
    {
        [HarmonyPrefix]
        static bool Prefix(UIChat __instance, string message)
        {
            Plugin.Log.LogInfo($"Patch: UIChat.Client_SendClientChatMessage (Prefix) was called.");
            Plugin.chat = __instance;
            string[] messageParts = message.Split(' ');

            if (messageParts[0] == "/becomepuck")
            {
                Plugin.client_spectatorIsPuck = !Plugin.client_spectatorIsPuck;
                if (Plugin.client_spectatorIsPuck)
                {
                    // Reparent the spectator camera to the puck
                    Plugin.spectatorCamera.transform.SetParent(Plugin.puckManager.GetPuck().transform);

                    // Optionally reset the local position and rotation of the camera relative to the puck
                    Plugin.spectatorCamera.transform.localPosition = Vector3.zero; // Center the camera on the puck
                    Plugin.spectatorCamera.transform.localRotation =
                        Quaternion.identity; // Align the camera's rotation with the puck
                }

                __instance.chatMessages.Add(new Label($"Become puck: {Plugin.client_spectatorIsPuck}"));

                return false;
            }

            if (messageParts[0] == "/watchpuck")
            {
                Plugin.client_spectatorWatchPuck = !Plugin.client_spectatorWatchPuck;
                return false;
            }

            if (messageParts[0] == "/watchpuckabove")
            {
                Plugin.client_spectatorWatchPuckAbove = !Plugin.client_spectatorWatchPuckAbove;
            }

            if (messageParts[0] == "/watchpucksmart")
            {
                Plugin.client_spectatorWatchPuckSmart = !Plugin.client_spectatorWatchPuckSmart;
            }
            
            if (messageParts[0] == "/watchpucksmart2")
            {
                Plugin.client_spectatorWatchPuckSmart2 = !Plugin.client_spectatorWatchPuckSmart2;
            }


            
            if (messageParts[0] == "/watchplayer")
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

            return true;
        }
    }
}
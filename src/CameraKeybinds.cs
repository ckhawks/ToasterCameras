using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ToasterCameras;

public static class CameraKeybinds
{
    static readonly FieldInfo _isFocusedField = typeof(UIComponent<UIChat>)
        .GetField("isFocused", 
            BindingFlags.Instance | BindingFlags.NonPublic);
    
    // Player watch actions
    public static InputAction[] bluePlayerActions = new InputAction[6];
    public static InputAction[] redPlayerActions = new InputAction[6];
    
    public static void InitializeCameraPositionKeybinds()
    {
        if (Plugin.modSettings == null) return;
        
        var positions = Plugin.modSettings.cameraPositions;
        var actions = new List<InputAction>();
        
        foreach (var kvp in positions)
        {
            if (!string.IsNullOrEmpty(kvp.Value.keybind))
            {
                var action = new InputAction(
                    name: $"camera_pos_{kvp.Key}",
                    binding: kvp.Value.keybind
                );
                action.Enable();
                actions.Add(action);
                
                Plugin.Log($"Registered keybind '{kvp.Value.keybind}' for camera position '{kvp.Key}' ({kvp.Value.name})");
            }
        }
        
        Plugin.cameraPositionActions = actions.ToArray();
    }
    
    public static void InitializePlayerWatchKeybinds()
    {
        if (Plugin.modSettings == null) return;
        
        var blue = Plugin.modSettings.watchPlayer.blue;
        var red = Plugin.modSettings.watchPlayer.red;
        
        // Blue team
        RegisterPlayerKeybind(blue.player1, 0, PlayerTeam.Blue, "Blue Player 1");
        RegisterPlayerKeybind(blue.player2, 1, PlayerTeam.Blue, "Blue Player 2");
        RegisterPlayerKeybind(blue.player3, 2, PlayerTeam.Blue, "Blue Player 3");
        RegisterPlayerKeybind(blue.player4, 3, PlayerTeam.Blue, "Blue Player 4");
        RegisterPlayerKeybind(blue.player5, 4, PlayerTeam.Blue, "Blue Player 5");
        RegisterPlayerKeybind(blue.player6, 5, PlayerTeam.Blue, "Blue Player 6");
        
        // Red team
        RegisterPlayerKeybind(red.player1, 0, PlayerTeam.Red, "Red Player 1");
        RegisterPlayerKeybind(red.player2, 1, PlayerTeam.Red, "Red Player 2");
        RegisterPlayerKeybind(red.player3, 2, PlayerTeam.Red, "Red Player 3");
        RegisterPlayerKeybind(red.player4, 3, PlayerTeam.Red, "Red Player 4");
        RegisterPlayerKeybind(red.player5, 4, PlayerTeam.Red, "Red Player 5");
        RegisterPlayerKeybind(red.player6, 5, PlayerTeam.Red, "Red Player 6");
    }
    
    private static void RegisterPlayerKeybind(string keybind, int index, PlayerTeam team, string description)
    {
        if (string.IsNullOrEmpty(keybind)) return;
        
        var action = new InputAction(binding: keybind);
        action.Enable();
        
        if (team == PlayerTeam.Blue)
            bluePlayerActions[index] = action;
        else
            redPlayerActions[index] = action;
        
        Plugin.Log($"Registered keybind '{keybind}' for {description}");
    }
    
    private static List<Player> GetSortedTeamPlayers(PlayerTeam team)
    {
        if (PlayerManager.Instance == null)
            return new List<Player>();
        
        var players = PlayerManager.Instance.GetPlayers()
            .Where(p => p != null && p.Team != null && p.Team.Value == team)
            .ToList();
        
        // Sort by position priority: C -> LW -> RW -> LD -> RD -> G
        var positionOrder = new Dictionary<string, int>
        {
            { "C", 0 },
            { "LW", 1 },
            { "RW", 2 },
            { "LD", 3 },
            { "RD", 4 },
            { "G", 5 }
        };
        
        return players.OrderBy(p => {
            if (p.PlayerPosition == null || string.IsNullOrEmpty(p.PlayerPosition.Name))
                return 999;
            string pos = p.PlayerPosition.Name;
            return positionOrder.ContainsKey(pos) ? positionOrder[pos] : 999;
        }).ToList();
    }
    
    public static void InitializeCameraModeKeybinds()
    {
        if (Plugin.modSettings == null) return;
        
        var modes = Plugin.modSettings.cameraModes;
        
        if (!string.IsNullOrEmpty(modes.becomePuck))
        {
            Plugin.becomePuckAction = new InputAction(binding: modes.becomePuck);
            Plugin.becomePuckAction.Enable();
            Plugin.Log($"Registered keybind '{modes.becomePuck}' for Become Puck");
        }
        
        if (!string.IsNullOrEmpty(modes.watchPuck))
        {
            Plugin.watchPuckAction = new InputAction(binding: modes.watchPuck);
            Plugin.watchPuckAction.Enable();
            Plugin.Log($"Registered keybind '{modes.watchPuck}' for Watch Puck");
        }
        
        if (!string.IsNullOrEmpty(modes.watchPuckAbove))
        {
            Plugin.watchPuckAboveAction = new InputAction(binding: modes.watchPuckAbove);
            Plugin.watchPuckAboveAction.Enable();
            Plugin.Log($"Registered keybind '{modes.watchPuckAbove}' for Watch Puck Above");
        }
        
        if (!string.IsNullOrEmpty(modes.watchPuckSmart))
        {
            Plugin.watchPuckSmartAction = new InputAction(binding: modes.watchPuckSmart);
            Plugin.watchPuckSmartAction.Enable();
            Plugin.Log($"Registered keybind '{modes.watchPuckSmart}' for Watch Puck Smart");
        }
        
        if (!string.IsNullOrEmpty(modes.watchPuckSmart2))
        {
            Plugin.watchPuckSmart2Action = new InputAction(binding: modes.watchPuckSmart2);
            Plugin.watchPuckSmart2Action.Enable();
            Plugin.Log($"Registered keybind '{modes.watchPuckSmart2}' for Watch Puck Smart 2");
        }
        
        if (!string.IsNullOrEmpty(modes.watchOff))
        {
            Plugin.watchOffAction = new InputAction(binding: modes.watchOff);
            Plugin.watchOffAction.Enable();
            Plugin.Log($"Registered keybind '{modes.watchOff}' for Watch Off");
        }
        
        if (!string.IsNullOrEmpty(modes.cinematicSmoothing))
        {
            Plugin.cinematicSmoothingAction = new InputAction(binding: modes.cinematicSmoothing);
            Plugin.cinematicSmoothingAction.Enable();
            Plugin.Log($"Registered keybind '{modes.cinematicSmoothing}' for Cinematic Smoothing");
        }
        
        if (!string.IsNullOrEmpty(modes.slowDown))
        {
            Plugin.slowDownAction = new InputAction(binding: modes.slowDown);
            Plugin.slowDownAction.Enable();
            Plugin.Log($"Registered keybind '{modes.slowDown}' for Slow Down");
        }
    }
    
    [HarmonyPatch(typeof(PlayerInput), "Update")]
    public static class PlayerInputUpdatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerInput __instance)
        {
            // Plugin.Log("=== Postfix called ==="); // Add this
            if (Plugin.modSettings == null || Plugin.cameraPositionActions == null) return;

            if (_isFocusedField == null || UIChat.Instance == null) return;
            
            bool isFocusedChat = (bool)_isFocusedField.GetValue(UIChat.Instance);
            if (isFocusedChat) return;
            
            // Check if current watched player still exists
            if (Plugin.client_spectatorWatchThirdPerson && Plugin.thirdPersonPlayerToWatch != null)
            {
                if (PlayerManager.Instance != null)
                {
                    var allPlayers = PlayerManager.Instance.GetPlayers();
                    if (allPlayers != null && !allPlayers.Contains(Plugin.thirdPersonPlayerToWatch))
                    {
                        Plugin.Log("Watched player no longer exists, disabling third person mode");
                        DisableAllCameraModes();
                    }
                }
            }
            
            // Check camera mode keybinds
            CheckCameraModeKeybind("becomePuck", Plugin.becomePuckAction, () => {
                if (PuckManager.Instance == null || PuckManager.Instance.GetPuck() == null || 
                    Plugin.spectatorCamera == null) return;
                
                DisableAllCameraModes();
                Plugin.client_spectatorIsPuck = true;
                Plugin.spectatorCamera.transform.SetParent(PuckManager.Instance.GetPuck().transform);
                Plugin.spectatorCamera.transform.localPosition = Vector3.zero;
                Plugin.spectatorCamera.transform.localRotation = Quaternion.identity;
            });
            
            CheckCameraModeKeybind("watchPuck", Plugin.watchPuckAction, () => {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuck = true;
            });
            
            CheckCameraModeKeybind("watchPuckAbove", Plugin.watchPuckAboveAction, () => {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckAbove = true;
            });
            
            CheckCameraModeKeybind("watchPuckSmart", Plugin.watchPuckSmartAction, () => {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart = true;
            });
            
            CheckCameraModeKeybind("watchPuckSmart2", Plugin.watchPuckSmart2Action, () => {
                DisableAllCameraModes();
                Plugin.client_spectatorWatchPuckSmart2 = true;
            });
            
            CheckCameraModeKeybind("cinematicSmoothing", Plugin.cinematicSmoothingAction, () => {
                Plugin.client_cinematicSmoothingEnabled = !Plugin.client_cinematicSmoothingEnabled;
    
                if (Plugin.client_cinematicSmoothingEnabled)
                {
                    Plugin._currentCinematicPosition = Vector3.zero;
                    Plugin._currentCinematicRotation = Vector3.zero;
                }
    
                string status = Plugin.client_cinematicSmoothingEnabled ? "enabled" : "disabled";
                Plugin.Log($"Cinematic smoothing {status}");
            });
            
            CheckCameraModeKeybind("watchOff", Plugin.watchOffAction, () => {
                DisableAllCameraModes();
            });
            
            // Check player watch keybinds
            if (PlayerManager.Instance != null)
            {
                CheckPlayerWatchKeybinds(PlayerTeam.Blue, bluePlayerActions);
                CheckPlayerWatchKeybinds(PlayerTeam.Red, redPlayerActions);
            }
            
            // Check camera position keybinds
            var positions = Plugin.modSettings.cameraPositions;
            int actionIndex = 0;
            
            foreach (var kvp in positions)
            {
                string posKey = kvp.Key;
                
                if (!string.IsNullOrEmpty(kvp.Value.keybind) && 
                    actionIndex < Plugin.cameraPositionActions.Length)
                {
                    var action = Plugin.cameraPositionActions[actionIndex];
                    
                    if (action != null && action.WasPressedThisFrame())
                    {
                        SetCameraPosition(posKey);
                    }
                    
                    actionIndex++;
                }
            }
        }
        
        private static void CheckPlayerWatchKeybinds(PlayerTeam team, InputAction[] actions)
        {
            if (actions == null) return;
            
            var sortedPlayers = GetSortedTeamPlayers(team);
            
            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i] == null) continue;
                
                if (actions[i].WasPressedThisFrame())
                {
                    Player playerToWatch = null;
                    
                    // For player6 (index 5), always try to get goalie if exists
                    if (i == 5)
                    {
                        playerToWatch = sortedPlayers.FirstOrDefault(p => 
                            p != null && 
                            p.PlayerPosition != null && 
                            p.PlayerPosition.Name == "G");
                    }
                    
                    // If not found or not player6, use sorted order
                    if (playerToWatch == null && i < sortedPlayers.Count)
                    {
                        playerToWatch = sortedPlayers[i];
                    }
                    
                    if (playerToWatch != null && playerToWatch.Username != null)
                    {
                        DisableAllCameraModes();
                        Plugin.thirdPersonPlayerToWatch = playerToWatch;
                        Plugin.client_spectatorWatchThirdPerson = true;
                        
                        string teamName = team == PlayerTeam.Blue ? "Blue" : "Red";
                        string posName = playerToWatch.PlayerPosition != null ? 
                            playerToWatch.PlayerPosition.Name : "Unknown";
                        Plugin.Log($"Watching {teamName} player: {playerToWatch.Username.Value} ({posName})");
                    }
                    else
                    {
                        Plugin.Log($"No player found at position {i + 1} for {team} team");
                    }
                }
            }
        }
        
        private static void CheckCameraModeKeybind(string modeName, InputAction action, System.Action callback)
        {
            if (action == null) return;
            
            if (action.WasPressedThisFrame())
            {
                callback();
                Plugin.Log($"Activated camera mode: {modeName}");
            }
        }
        
        private static void SetCameraPosition(string positionKey)
        {
            DisableAllCameraModes();
            Plugin.client_spectatorStaticPosition = positionKey;
            Plugin.client_spectatorStaticPositioning = true;
            
            if (Plugin.modSettings.cameraPositions.TryGetValue(positionKey, out var camPos))
            {
                string posName = !string.IsNullOrEmpty(camPos.name) ? 
                    camPos.name : positionKey;
                Plugin.Log($"Activated camera position: {posName}");
            }
        }
        
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
    }
}
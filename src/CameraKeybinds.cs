using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ToasterCameras;

public static class CameraKeybinds
{
    // Player watch actions
    public static InputAction[] bluePlayerActions = new InputAction[6];
    public static InputAction[] redPlayerActions = new InputAction[6];

    // Disable + dispose an existing action before it's replaced, so re-initializing after a
    // live rebind doesn't leak InputActions (and orphaned-but-enabled actions can't keep firing).
    private static void Free(ref InputAction action)
    {
        if (action == null) return;
        try { action.Disable(); action.Dispose(); }
        catch { /* already torn down */ }
        action = null;
    }

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

        // Replace any existing actions (live rebind re-runs this).
        for (int i = 0; i < bluePlayerActions.Length; i++) Free(ref bluePlayerActions[i]);
        for (int i = 0; i < redPlayerActions.Length; i++) Free(ref redPlayerActions[i]);

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
            .Where(p => p != null && p.Team == team)
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

        return players.OrderBy(p =>
        {
            if (p.PlayerPosition == null || string.IsNullOrEmpty(p.PlayerPosition.Name))
                return 999;
            string pos = p.PlayerPosition.Name;
            return positionOrder.ContainsKey(pos) ? positionOrder[pos] : 999;
        }).ToList();
    }

    public static void InitializeCameraModeKeybinds()
    {
        if (Plugin.modSettings == null) return;

        // Replace any existing actions (live rebind re-runs this).
        Free(ref Plugin.becomePuckAction);
        Free(ref Plugin.becomePuckFreeAction);
        Free(ref Plugin.watchPuckAction);
        Free(ref Plugin.watchPuckGridAction);
        Free(ref Plugin.watchPuckAboveAction);
        Free(ref Plugin.watchPuckSmartAction);
        Free(ref Plugin.watchPuckSmart2Action);
        Free(ref Plugin.watchOffAction);
        Free(ref Plugin.cinematicSmoothingAction);
        Free(ref Plugin.slowDownAction);

        var modes = Plugin.modSettings.cameraModes;

        if (!string.IsNullOrEmpty(modes.becomePuck))
        {
            Plugin.becomePuckAction = new InputAction(binding: modes.becomePuck);
            Plugin.becomePuckAction.Enable();
            Plugin.Log($"Registered keybind '{modes.becomePuck}' for Become Puck");
        }

        if (!string.IsNullOrEmpty(modes.becomePuckFree))
        {
            Plugin.becomePuckFreeAction = new InputAction(binding: modes.becomePuckFree);
            Plugin.becomePuckFreeAction.Enable();
            Plugin.Log($"Registered keybind '{modes.becomePuckFree}' for Become Puck (free look)");
        }

        if (!string.IsNullOrEmpty(modes.watchPuck))
        {
            Plugin.watchPuckAction = new InputAction(binding: modes.watchPuck);
            Plugin.watchPuckAction.Enable();
            Plugin.Log($"Registered keybind '{modes.watchPuck}' for Watch Puck");
        }

        if (!string.IsNullOrEmpty(modes.watchPuckGrid))
        {
            Plugin.watchPuckGridAction = new InputAction(binding: modes.watchPuckGrid);
            Plugin.watchPuckGridAction.Enable();
            Plugin.Log($"Registered keybind '{modes.watchPuckGrid}' for Watch Puck Grid");
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
            if (Plugin.modSettings == null || Plugin.cameraPositionActions == null) return;

            var chat = UIManager.Instance != null ? UIManager.Instance.Chat : null;
            if (chat == null) return;

            if (chat.IsFocused) return;

            // Check if current watched player still exists
            if (Plugin.cameraMode == CameraMode.WatchThirdPerson && Plugin.thirdPersonPlayerToWatch != null)
            {
                if (PlayerManager.Instance != null)
                {
                    var allPlayers = PlayerManager.Instance.GetPlayers();
                    if (allPlayers != null && !allPlayers.Contains(Plugin.thirdPersonPlayerToWatch))
                    {
                        Plugin.Log("Watched player no longer exists, disabling third person mode");
                        Plugin.SetCameraMode(CameraMode.None);
                    }
                }
            }

            // Check camera mode keybinds
            CheckCameraModeKeybind("becomePuck", Plugin.becomePuckAction, () =>
            {
                if (PuckManager.Instance == null || PuckManager.Instance.GetPuck() == null ||
                    Plugin.spectatorCamera == null) return;

                // Don't SetParent onto the puck — it's a NetworkObject, so the client
                // reparent spams "only the server can re-parent" every frame. TickPuck
                // already copies the puck's position/rotation each frame.
                Plugin.SetCameraMode(CameraMode.Puck);
            });

            CheckCameraModeKeybind("becomePuckFree", Plugin.becomePuckFreeAction, () =>
            {
                Plugin.SetCameraMode(CameraMode.PuckFreeLook);
            });

            CheckCameraModeKeybind("watchPuck", Plugin.watchPuckAction, () =>
            {
                Plugin.SetCameraMode(CameraMode.WatchPuck);
            });

            CheckCameraModeKeybind("watchPuckGrid", Plugin.watchPuckGridAction, () =>
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckGrid);
            });

            CheckCameraModeKeybind("watchPuckAbove", Plugin.watchPuckAboveAction, () =>
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckAbove);
            });

            CheckCameraModeKeybind("watchPuckSmart", Plugin.watchPuckSmartAction, () =>
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckSmart);
            });

            CheckCameraModeKeybind("watchPuckSmart2", Plugin.watchPuckSmart2Action, () =>
            {
                Plugin.SetCameraMode(CameraMode.WatchPuckSmart2);
            });

            CheckCameraModeKeybind("cinematicSmoothing", Plugin.cinematicSmoothingAction, () =>
            {
                Plugin.client_cinematicSmoothingEnabled = !Plugin.client_cinematicSmoothingEnabled;

                if (Plugin.client_cinematicSmoothingEnabled)
                {
                    Plugin._currentCinematicPosition = Vector3.zero;
                    Plugin._currentCinematicRotation = Vector3.zero;
                }

                string status = Plugin.client_cinematicSmoothingEnabled ? "enabled" : "disabled";
                Plugin.Log($"Cinematic smoothing {status}");
            });

            CheckCameraModeKeybind("watchOff", Plugin.watchOffAction, () => { Plugin.SetCameraMode(CameraMode.None); });

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

                    if (playerToWatch == null && i < sortedPlayers.Count)
                    {
                        playerToWatch = sortedPlayers[i];
                    }

                    if (playerToWatch != null)
                    {
                        Plugin.thirdPersonPlayerToWatch = playerToWatch;
                        Plugin.SetCameraMode(CameraMode.WatchThirdPerson);

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
            Plugin.client_spectatorStaticPosition = positionKey;
            Plugin.SetCameraMode(CameraMode.StaticPosition);

            if (Plugin.modSettings.cameraPositions.TryGetValue(positionKey, out var camPos))
            {
                string posName = !string.IsNullOrEmpty(camPos.name) ?
                    camPos.name : positionKey;
                Plugin.Log($"Activated camera position: {posName}");
            }
        }

    }
}

// PatchPlayerCamera.cs

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ToasterCameras;

public static class PatchPlayerCamera
{
    private static readonly FieldInfo _freeLookPositionField = typeof(SpectatorCamera)
        .GetField("freeLookPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo _freeLookMovementSpeedField = typeof(SpectatorCamera)
        .GetField("freeLookMovementSpeed",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo _freeLookPositionSmoothingField = typeof(SpectatorCamera)
        .GetField("freeLookPositionSmoothing",
            BindingFlags.Instance | BindingFlags.NonPublic);

    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.OnTick))]
    private class PatchPlayerCameraOnTick
    {
        private static void Postfix(PlayerCamera __instance)
        {
            if (Plugin.becomePuckPlayerCameras.Contains(__instance))
            {
                __instance.transform.position = PuckManager.Instance.GetPuck().transform.position;
                __instance.transform.rotation = PuckManager.Instance.GetPuck().transform.rotation;
            }
        }
    }

    [HarmonyPatch(typeof(SpectatorCamera), nameof(SpectatorCamera.OnTick))]
    private class PatchSpectatorCameraOnTick
    {
        private static float elapsedTime; // Tracks the time passed
        private static bool isMoving;
        public static Vector3 targetPos = Vector3.zero;
        public static readonly float duration = 6.0f;
        private static readonly LevelManager lm = LevelManager.Instance;
        private static readonly float iceWidth = lm.IceBounds.extents.x;
        private static readonly float iceLength = lm.IceBounds.extents.z;
        private static readonly float longPosition = iceLength - 5;
        private static readonly float height = 6;
        private static readonly float widthPosition = iceWidth - 3;
        private static Vector3 velocity = Vector3.zero;
        private static readonly float cooldownTime = 3.0f;
        private static float timeSinceLastTargetChange;

        [HarmonyPrefix]
        public static bool Prefix(SpectatorCamera __instance, float deltaTime)
        {
            // Plugin.Log.LogInfo($"Patch: SpectatorCamera.OnTick (Postfix) was called.");
            elapsedTime += Time.deltaTime;
            Plugin.spectatorCamera = __instance;
            var im = InputManager.Instance;


            if (Plugin.client_spectatorIsPuck)
            {
                if (PuckManager.Instance == null) Plugin.Log("Puckmanager is so dead bro");

                var puck = PuckManager.Instance.GetPuck();


                if (puck != null)
                {
                    __instance.transform.position = puck.transform.position;
                    __instance.transform.rotation = puck.transform.rotation;
                }

                return false;
            }

            if (_freeLookMovementSpeedField == null || _freeLookPositionField == null ||
                _freeLookPositionSmoothingField == null)
            {
                Plugin.Log(
                    "ERROR: FieldInfo for _freeLookMovementSpeedField or _freeLookPositionField or _freeLookPositionSmoothingField is null!");
                return true;
            }

            var freeLookMovementSpeedDefault = (float)_freeLookMovementSpeedField.GetValue(__instance);
            var freeLookPosition = (Vector3)_freeLookPositionField.GetValue(__instance);
            var freeLookPositionSmoothing = (float)_freeLookPositionSmoothingField.GetValue(__instance);


            if (Plugin.client_spectatorWatchPuck)
            {
                var puck = PuckManager.Instance.GetPuck();
                if (puck != null) Plugin.spectatorCamera.transform.LookAt(puck.transform.position);
                var isMouseActive = UIManager.Instance.isMouseActive;
                if (!isMouseActive)
                {
                    var moveVector = new Vector3(
                        im.TurnRightAction.ReadValue<float>() - im.TurnLeftAction.ReadValue<float>(),
                        im.MoveForwardAction.ReadValue<float>() - im.MoveBackwardAction.ReadValue<float>(),
                        im.JumpAction.IsPressed() ? 1 : im.SlideAction.IsPressed() ? -1 : 0);
                    // Check for speed modifiers
                    // Check for speed modifiers
                    var isSprinting = im.SprintAction.IsPressed();
                    var isSlowingDown = Plugin.slowDownAction != null && Plugin.slowDownAction.IsPressed();

                    // Calculate speed with modifiers
                    var speedMultiplier = 1f;
                    if (isSprinting)
                        speedMultiplier = 2f;
                    else if (isSlowingDown)
                        speedMultiplier = 0.25f; // Adjust: 0.5 = half speed, 0.25 = quarter speed

                    var speed = freeLookMovementSpeedDefault * speedMultiplier;
                    freeLookPosition += __instance.transform.right * moveVector.x * deltaTime * speed;
                    freeLookPosition += __instance.transform.forward * moveVector.y * deltaTime * speed;
                    freeLookPosition += __instance.transform.up * moveVector.z * deltaTime * speed;
                    _freeLookPositionField.SetValue(__instance, freeLookPosition);
                    // Vector2 lookDelta = this.stickAction.ReadValue<Vector2>();
                    // float mouseSensitivity = MonoBehaviourSingleton<SettingsManager>.Instance.LookSensitivity;
                    // this.freeLookAngle += new Vector3(-lookDelta.y * mouseSensitivity, lookDelta.x * mouseSensitivity, -this.freeLookRotation.eulerAngles.z);
                    // this.freeLookAngle.x = Mathf.Clamp(this.freeLookAngle.x, -80f, 80f);
                    // this.freeLookRotation = Quaternion.Euler(Utils.WrapEulerAngles(this.freeLookAngle));
                    __instance.transform.position = Vector3.Lerp(__instance.transform.position, freeLookPosition,
                        deltaTime / freeLookPositionSmoothing);
                    // base.transform.rotation = Quaternion.Lerp(base.transform.rotation, this.freeLookRotation, deltaTime / this.freeLookRotationSmoothing);
                }

                return false;
            }

            if (Plugin.client_spectatorWatchPuckAbove)
            {
                var puck = PuckManager.Instance.GetPuck();
                if (puck != null)
                {
                    var moveVector = new Vector3(
                        im.TurnRightAction.ReadValue<float>() - im.TurnLeftAction.ReadValue<float>(),
                        im.MoveForwardAction.ReadValue<float>() - im.MoveBackwardAction.ReadValue<float>(),
                        im.JumpAction.IsPressed() ? 1 : im.SlideAction.IsPressed() ? -1 : 0);
                    // Check for speed modifiers
                    var isSprinting = im.SprintAction.IsPressed();
                    var isSlowingDown = Plugin.slowDownAction != null && Plugin.slowDownAction.IsPressed();

                    // Calculate speed with modifiers
                    var speedMultiplier = 1f;
                    if (isSprinting)
                        speedMultiplier = 2f;
                    else if (isSlowingDown)
                        speedMultiplier = 0.25f; // Adjust: 0.5 = half speed, 0.25 = quarter speed

                    var speed = freeLookMovementSpeedDefault * speedMultiplier;
                    // __instance.freeLookPosition += __instance.transform.right * moveVector.x * deltaTime * speed;
                    // __instance.freeLookPosition += __instance.transform.up * moveVector.z * deltaTime * speed;
                    // __instance.transform.position = Vector3.Lerp(__instance.transform.position, __instance.freeLookPosition, deltaTime / __instance.freeLookPositionSmoothing);
                    var positionToSet = new Vector3(puck.transform.position.x,
                        __instance.transform.position.y + moveVector.y * deltaTime * speed,
                        puck.transform.position.z);
                    __instance.transform.SetPositionAndRotation(positionToSet, __instance.transform.rotation);
                    Plugin.spectatorCamera.transform.LookAt(puck.transform.position);
                }

                return false;
            }

            if (Plugin.client_spectatorWatchThirdPerson)
            {
                var offset = new Vector3(0, 3, -2);
                var smoothSpeed = 10f;
                if (Plugin.thirdPersonPlayerToWatch == null) return true;
                var desiredPosition = Plugin.thirdPersonPlayerToWatch.PlayerBody.transform.position +
                                      Plugin.thirdPersonPlayerToWatch.PlayerBody.transform.TransformDirection(offset);
                __instance.transform.position = Vector3.Lerp(__instance.transform.position, desiredPosition,
                    smoothSpeed * Time.deltaTime);
                // Vector3 positionToSet = new Vector3(player.transform.position.x, __instance.transform.position.y,
                //     puck.transform.position.z);
                // __instance.transform.SetPositionAndRotation(positionToSet, __instance.transform.rotation);
                Plugin.spectatorCamera.transform.LookAt(Plugin.thirdPersonPlayerToWatch.PlayerBody.transform.position +
                                                        Plugin.thirdPersonPlayerToWatch.PlayerBody.transform
                                                            .TransformDirection(new Vector3(0, 0, 2)));
                return false;
            }

            if (Plugin.client_spectatorWatchPuckSmart)
            {
                // void UnlockMouse()
                // {
                //     Cursor.lockState = CursorLockMode.None; // Unlock the cursor
                //     Cursor.visible = true; // Make the cursor visible
                // }
                //
                // UnlockMouse();
                // Ice bounds are (-18.75, -4.98, -38.12) -> (18.75, 0.03, 38.12)
                // float distance = Vector3.Distance(pointA, pointB);

                var puck = PuckManager.Instance.GetPuck();
                if (puck != null)
                {
                    var puckPosition = puck.transform.position;
                    var puckPosX = puckPosition.x;
                    var puckPosZ = puckPosition.z;

                    var pos1 = new Vector3(widthPosition, height, longPosition);
                    var pos2 = new Vector3(widthPosition, height, -longPosition);
                    var pos3 = new Vector3(-widthPosition, height, -longPosition);
                    var pos4 = new Vector3(-widthPosition, height, longPosition);

                    if (puckPosX >= 0 && puckPosZ >= 0 && targetPos != pos1)
                    {
                        targetPos = pos1;
                        elapsedTime = 0f;
                        isMoving = true;
                    }
                    else if (puckPosX >= 0 && puckPosZ <= 0 && targetPos != pos2)
                    {
                        targetPos = pos2;
                        elapsedTime = 0f;
                        isMoving = true;
                    }
                    else if (puckPosX <= 0 && puckPosZ <= 0 && targetPos != pos3)
                    {
                        targetPos = pos3;
                        elapsedTime = 0f;
                        isMoving = true;
                    }
                    else if (puckPosX <= 0 && puckPosZ >= 0 && targetPos != pos4)
                    {
                        targetPos = pos4;
                        elapsedTime = 0f;
                        isMoving = true;
                    }

                    // If the camera is moving, interpolate its position
                    if (isMoving)
                    {
                        // Increment elapsed time
                        elapsedTime += Time.deltaTime;

                        // Calculate the interpolation factor (t)
                        var t = elapsedTime / duration;

                        // Interpolate the camera's position between startPosition and targetPosition
                        __instance.transform.position = Vector3.Lerp(__instance.transform.position, targetPos, t);

                        // Stop moving if the interpolation is complete
                        if (t >= 1f)
                        {
                            __instance.transform.position = targetPos; // Snap to the final position
                            isMoving = false; // Stop moving
                        }
                    }

                    // Vector3 positionToSet = new Vector3(puck.transform.position.x, __instance.transform.position.y, puck.transform.position.z);
                    // __instance.transform.SetPositionAndRotation(positionToSet, __instance.transform.rotation);
                    __instance.transform.LookAt(puck.transform.position);
                }

                return false;
            }

            if (Plugin.client_spectatorWatchPuckSmart2)
            {
                // Ice bounds are (-18.75, -4.98, -38.12) -> (18.75, 0.03, 38.12)
                var pucks = PuckManager.Instance.GetPucks();
                var puck = pucks.ToArray().Length > 0 ? pucks.ToArray()[0] : null;
                if (puck == null)
                {
                    // try to get a replay puck instead
                    pucks = PuckManager.Instance.GetReplayPucks();
                    puck = pucks.ToArray().Length > 0 ? pucks.ToArray()[0] : null;
                }

                Vector3 puckPosition;
                if (puck != null)
                    puckPosition = puck.transform.position;
                else
                    puckPosition = Vector3.zero;

                // Define the list of positions
                var positions = new List<Vector3>
                {
                    new(widthPosition, height, longPosition), // pos1 - corner
                    new(0, height, iceLength - 1), // above goal
                    new(widthPosition, height, -longPosition), // pos2 - corner
                    new(iceWidth - 1, height, 0), // pos2 - sidewall
                    new(widthPosition / 2 - 1, height, longPosition / 2 - 1), // pos2 - diagonal near center
                    new(-widthPosition / 2, height, -longPosition / 2), // pos2 - diagonal near center
                    new(-(iceWidth - 1), height, 0), // pos2 - sidewall
                    new(-widthPosition, height, -longPosition), // pos3 - corner
                    new(0, height, -(iceLength - 1)), // above goal
                    new(-widthPosition, height, longPosition) // pos4 - corner
                };

                timeSinceLastTargetChange += Time.deltaTime;

                // Find the closest position to the puck
                var closestPosition = positions[0];
                var closestDistance = Vector3.Distance(puckPosition, closestPosition);

                foreach (var position in positions)
                {
                    var distance = Vector3.Distance(puckPosition, position);
                    if (distance < closestDistance)
                    {
                        closestPosition = position;
                        closestDistance = distance;
                    }
                }

                // If the closest position is not the current target, update the target
                if (timeSinceLastTargetChange >= cooldownTime && targetPos != closestPosition)
                {
                    targetPos = closestPosition;
                    elapsedTime = 0f;
                    isMoving = true;
                    timeSinceLastTargetChange = 0f; // reset cooldown timer
                }

                // Adjust movement speed based on distance
                var movementSpeedFactor =
                    Mathf.Clamp(closestDistance / 200f, 0.01f, 0.1f); // Scale speed factor (0.1 to 1)

                // If the camera is moving, interpolate its position
                if (isMoving)
                {
                    // Increment elapsed time
                    elapsedTime += Time.deltaTime; // * movementSpeedFactor; // Adjust speed based on distance

                    // Calculate the interpolation factor (t)
                    var t = elapsedTime / duration;

                    __instance.transform.position = Vector3.SmoothDamp(__instance.transform.position, targetPos,
                        ref velocity, cooldownTime);

                    // Interpolate the camera's position between startPosition and targetPosition
                    // __instance.transform.position = Vector3.Lerp(__instance.transform.position, targetPos, t);

                    // Stop moving if the interpolation is complete
                    if (t >= 1f)
                    {
                        __instance.transform.position = targetPos; // Snap to the final position
                        isMoving = false; // Stop moving
                    }
                }

                // if (Plugin.settingsManager != null)
                // {
                //     // Adjust the camera's field of view (FOV) based on distance
                //     float fovFactor = Mathf.Clamp(closestDistance / 20f, 0.5f, 1.5f); // Scale FOV factor (0.5 to 1.5)
                //     Plugin.settingsManager.UpdateFov(60f * fovFactor); // Base FOV is 60, adjust based on distance
                //     Plugin.Log.LogInfo($"Updating fov to {60f * fovFactor}");
                // }

                // Make the camera look at the puck
                __instance.transform.LookAt(puckPosition);
                return false;
            }

            if (Plugin.client_spectatorStaticPositioning)
            {
                if (Plugin.modSettings.cameraPositions.TryGetValue(
                        Plugin.client_spectatorStaticPosition, out var camPos))
                {
                    __instance.transform.position = camPos.GetPosition();
                    __instance.transform.rotation = Quaternion.Euler(camPos.GetRotation());
                }

                return false;
            }

            var isAnyOtherModeActive =
                Plugin.client_spectatorIsPuck ||
                Plugin.client_spectatorWatchPuck ||
                Plugin.client_spectatorWatchPuckAbove ||
                Plugin.client_spectatorWatchThirdPerson ||
                Plugin.client_spectatorWatchPuckSmart ||
                Plugin.client_spectatorWatchPuckSmart2 ||
                Plugin.client_spectatorStaticPositioning;

            if (Plugin.client_cinematicSmoothingEnabled && !isAnyOtherModeActive)
            {
                // Ensure fields are available
                if (_freeLookMovementSpeedField == null || _freeLookPositionField == null ||
                    _freeLookPositionSmoothingField == null)
                {
                    Plugin.Log(
                        "Cinematic Smoothing: Required SpectatorCamera fields are null, falling back to default behavior.");
                    return true; // Fall back to original method
                }

                // Get original movement variables (these define the 'target' for our smoothing)
                var freeLookMovementSpeed = (float)_freeLookMovementSpeedField.GetValue(__instance);
                var freeLookPositionOriginal =
                    (Vector3)_freeLookPositionField.GetValue(__instance); // This is the unsmoothed target position
                var freeLookPositionSmoothingOriginal =
                    (float)_freeLookPositionSmoothingField
                        .GetValue(__instance); // Original smoothing, might be too strong for us

                // --- MOVEMENT INPUT (Identical to original) ---
                var inputVector = new Vector3(
                    (im.TurnRightAction.IsPressed() ? 1 : 0) + (im.TurnLeftAction.IsPressed() ? -1 : 0),
                    (im.MoveForwardAction.IsPressed() ? 1 : 0) +
                    (im.MoveBackwardAction.IsPressed() ? -1 : 0),
                    im.JumpAction.IsPressed() ? 1 : im.SlideAction.IsPressed() ? -1 : 0
                );
                // Check for speed modifiers
                var isSprinting = im.SprintAction.IsPressed();
                var isSlowingDown = Plugin.slowDownAction != null && Plugin.slowDownAction.IsPressed();

                // Calculate speed with modifiers
                var speedMultiplier = 1f;
                if (isSprinting)
                    speedMultiplier = 2f;
                else if (isSlowingDown)
                    speedMultiplier = 0.25f; // Adjust: 0.5 = half speed, 0.25 = quarter speed

                var currentMoveSpeed = freeLookMovementSpeed * speedMultiplier;

                // Calculate target position based on input (using current transform rotation for direction)
                var targetPosition = freeLookPositionOriginal; // Start with the last known position
                targetPosition += __instance.transform.right * inputVector.x * deltaTime * currentMoveSpeed;
                targetPosition += __instance.transform.forward * inputVector.y * deltaTime * currentMoveSpeed;
                targetPosition += __instance.transform.up * inputVector.z * deltaTime * currentMoveSpeed;

                // --- ROTATION INPUT ---
                var lookDelta = im.StickAction.ReadValue<Vector2>();
                var lookSensitivity = MonoBehaviourSingleton<SettingsManager>.Instance.LookSensitivity;

                // Initialize cinematic rotation if needed
                if (Plugin._currentCinematicRotation == Vector3.zero &&
                    __instance.transform.rotation != Quaternion.identity)
                    Plugin._currentCinematicRotation = __instance.transform.rotation.eulerAngles;

                // --- APPLY SMOOTHING ---
                var rotSmoothingFactor = Plugin.modSettings.cinematicSettings.rotationSmoothingFactor;
                var posSmoothingFactor = Plugin.modSettings.cinematicSettings.positionSmoothingFactor;

                // Initialize cinematic position if needed
                if (Plugin._currentCinematicPosition == Vector3.zero)
                    Plugin._currentCinematicPosition = __instance.transform.position;

                // Calculate target VELOCITY based on input (not position)
                var targetVelocity = Vector3.zero;
                targetVelocity += __instance.transform.right * inputVector.x * currentMoveSpeed;
                targetVelocity += __instance.transform.forward * inputVector.y * currentMoveSpeed;
                targetVelocity += __instance.transform.up * inputVector.z * currentMoveSpeed;

                // Smooth the velocity
                Plugin._cinematicPositionVelocity = Vector3.Lerp(
                    Plugin._cinematicPositionVelocity,
                    targetVelocity,
                    1f - Mathf.Exp(-posSmoothingFactor * deltaTime * 10f)
                );

                // Apply velocity to position
                Plugin._currentCinematicPosition += Plugin._cinematicPositionVelocity * deltaTime;

                // For rotation - smooth the rotation VELOCITY (mouse input), not the rotation itself
                var targetRotationVelocity = new Vector3(
                    -lookDelta.y * lookSensitivity,
                    lookDelta.x * lookSensitivity,
                    0f
                );

                // Smooth rotation velocity
                Plugin._cinematicRotationVelocity = Vector3.Lerp(
                    Plugin._cinematicRotationVelocity,
                    targetRotationVelocity,
                    1f - Mathf.Exp(-rotSmoothingFactor * deltaTime * 10f)
                );

                // Apply smoothed rotation velocity to current rotation
                Plugin._currentCinematicRotation += Plugin._cinematicRotationVelocity;

                // Zero out Z-axis and clamp X
                Plugin._currentCinematicRotation.z = 0f;
                Plugin._currentCinematicRotation.x = Mathf.Clamp(Plugin._currentCinematicRotation.x, -80f, 80f);

                // Apply to camera
                __instance.transform.position = Plugin._currentCinematicPosition;
                __instance.transform.rotation = Quaternion.Euler(Plugin._currentCinematicRotation);
                _freeLookPositionField.SetValue(__instance, Plugin._currentCinematicPosition);

                return false;
            }

            return true;
            // Plugin.Log.LogInfo($"Rotation: {__instance.transform.rotation.ToString()}");
            // Plugin.Log.LogInfo($"Position: {__instance.transform.position.ToString()}");
        }
    }

    public static void PrintCameraCoordinates()
    {
        var p = Plugin.spectatorCamera.transform.position;
        var r = Plugin.spectatorCamera.transform.rotation.eulerAngles;
        UIChat.Instance.AddChatMessage($"Position: {p.x} {p.y} {p.z} -- Rotation: {r.x} {r.y} {r.z}");
    }
}
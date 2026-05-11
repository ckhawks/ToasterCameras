// PatchPlayerCamera.cs

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ToasterCameras;

public static class PatchPlayerCamera
{
    // SpectatorCamera fields in b312:
    //   private Vector3 position
    //   [SerializeField] private float movementSpeed
    //   [SerializeField] private float positionSmoothTime
    private static readonly FieldInfo _positionField = typeof(SpectatorCamera)
        .GetField("position", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo _movementSpeedField = typeof(SpectatorCamera)
        .GetField("movementSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo _positionSmoothTimeField = typeof(SpectatorCamera)
        .GetField("positionSmoothTime", BindingFlags.Instance | BindingFlags.NonPublic);

    private static Level _cachedLevel;
    private static Level GetLevel()
    {
        if (_cachedLevel == null) _cachedLevel = Object.FindObjectOfType<Level>();
        return _cachedLevel;
    }

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
        private static float elapsedTime;
        private static bool isMoving;
        public static Vector3 targetPos = Vector3.zero;
        public static readonly float duration = 6.0f;
        private static Vector3 velocity = Vector3.zero;
        private static readonly float cooldownTime = 3.0f;
        private static float timeSinceLastTargetChange;

        [HarmonyPrefix]
        public static bool Prefix(SpectatorCamera __instance, float deltaTime)
        {
            elapsedTime += Time.deltaTime;
            Plugin.spectatorCamera = __instance;

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

            if (_movementSpeedField == null || _positionField == null || _positionSmoothTimeField == null)
            {
                Plugin.Log(
                    "ERROR: FieldInfo for _movementSpeedField, _positionField, or _positionSmoothTimeField is null!");
                return true;
            }

            var freeLookMovementSpeedDefault = (float)_movementSpeedField.GetValue(__instance);
            var freeLookPosition = (Vector3)_positionField.GetValue(__instance);
            var freeLookPositionSmoothing = (float)_positionSmoothTimeField.GetValue(__instance);

            var level = GetLevel();
            var iceWidth = level != null ? level.Bounds.extents.x : 18.75f;
            var iceLength = level != null ? level.Bounds.extents.z : 38.12f;
            var longPosition = iceLength - 5;
            var height = 6f;
            var widthPosition = iceWidth - 3;

            if (Plugin.client_spectatorWatchPuck)
            {
                var puck = PuckManager.Instance.GetPuck();
                if (puck != null) Plugin.spectatorCamera.transform.LookAt(puck.transform.position);
                var isMouseActive = GlobalStateManager.UIState.IsMouseRequired;
                if (!isMouseActive)
                {
                    var moveVector = new Vector3(
                        InputManager.TurnRightAction.ReadValue<float>() - InputManager.TurnLeftAction.ReadValue<float>(),
                        InputManager.MoveForwardAction.ReadValue<float>() - InputManager.MoveBackwardAction.ReadValue<float>(),
                        InputManager.JumpAction.IsPressed() ? 1 : InputManager.SlideAction.IsPressed() ? -1 : 0);
                    var isSprinting = InputManager.SprintAction.IsPressed();
                    var isSlowingDown = Plugin.slowDownAction != null && Plugin.slowDownAction.IsPressed();

                    var speedMultiplier = 1f;
                    if (isSprinting)
                        speedMultiplier = 2f;
                    else if (isSlowingDown)
                        speedMultiplier = 0.25f;

                    var speed = freeLookMovementSpeedDefault * speedMultiplier;
                    freeLookPosition += __instance.transform.right * moveVector.x * deltaTime * speed;
                    freeLookPosition += __instance.transform.forward * moveVector.y * deltaTime * speed;
                    freeLookPosition += __instance.transform.up * moveVector.z * deltaTime * speed;
                    _positionField.SetValue(__instance, freeLookPosition);
                    __instance.transform.position = Vector3.Lerp(__instance.transform.position, freeLookPosition,
                        deltaTime / Mathf.Max(freeLookPositionSmoothing, 0.0001f));
                }

                return false;
            }

            if (Plugin.client_spectatorWatchPuckAbove)
            {
                var puck = PuckManager.Instance.GetPuck();
                if (puck != null)
                {
                    var moveVector = new Vector3(
                        InputManager.TurnRightAction.ReadValue<float>() - InputManager.TurnLeftAction.ReadValue<float>(),
                        InputManager.MoveForwardAction.ReadValue<float>() - InputManager.MoveBackwardAction.ReadValue<float>(),
                        InputManager.JumpAction.IsPressed() ? 1 : InputManager.SlideAction.IsPressed() ? -1 : 0);
                    var isSprinting = InputManager.SprintAction.IsPressed();
                    var isSlowingDown = Plugin.slowDownAction != null && Plugin.slowDownAction.IsPressed();

                    var speedMultiplier = 1f;
                    if (isSprinting)
                        speedMultiplier = 2f;
                    else if (isSlowingDown)
                        speedMultiplier = 0.25f;

                    var speed = freeLookMovementSpeedDefault * speedMultiplier;
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
                Plugin.spectatorCamera.transform.LookAt(Plugin.thirdPersonPlayerToWatch.PlayerBody.transform.position +
                                                        Plugin.thirdPersonPlayerToWatch.PlayerBody.transform
                                                            .TransformDirection(new Vector3(0, 0, 2)));
                return false;
            }

            if (Plugin.client_spectatorWatchPuckSmart)
            {
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

                    if (isMoving)
                    {
                        elapsedTime += Time.deltaTime;
                        var t = elapsedTime / duration;
                        __instance.transform.position = Vector3.Lerp(__instance.transform.position, targetPos, t);

                        if (t >= 1f)
                        {
                            __instance.transform.position = targetPos;
                            isMoving = false;
                        }
                    }

                    __instance.transform.LookAt(puck.transform.position);
                }

                return false;
            }

            if (Plugin.client_spectatorWatchPuckSmart2)
            {
                var pucks = PuckManager.Instance.GetPucks();
                var puck = pucks.ToArray().Length > 0 ? pucks.ToArray()[0] : null;
                if (puck == null)
                {
                    pucks = PuckManager.Instance.GetReplayPucks();
                    puck = pucks.ToArray().Length > 0 ? pucks.ToArray()[0] : null;
                }

                Vector3 puckPosition;
                if (puck != null)
                    puckPosition = puck.transform.position;
                else
                    puckPosition = Vector3.zero;

                var positions = new List<Vector3>
                {
                    new(widthPosition, height, longPosition),
                    new(0, height, iceLength - 1),
                    new(widthPosition, height, -longPosition),
                    new(iceWidth - 1, height, 0),
                    new(widthPosition / 2 - 1, height, longPosition / 2 - 1),
                    new(-widthPosition / 2, height, -longPosition / 2),
                    new(-(iceWidth - 1), height, 0),
                    new(-widthPosition, height, -longPosition),
                    new(0, height, -(iceLength - 1)),
                    new(-widthPosition, height, longPosition)
                };

                timeSinceLastTargetChange += Time.deltaTime;

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

                if (timeSinceLastTargetChange >= cooldownTime && targetPos != closestPosition)
                {
                    targetPos = closestPosition;
                    elapsedTime = 0f;
                    isMoving = true;
                    timeSinceLastTargetChange = 0f;
                }

                if (isMoving)
                {
                    elapsedTime += Time.deltaTime;
                    var t = elapsedTime / duration;

                    __instance.transform.position = Vector3.SmoothDamp(__instance.transform.position, targetPos,
                        ref velocity, cooldownTime);

                    if (t >= 1f)
                    {
                        __instance.transform.position = targetPos;
                        isMoving = false;
                    }
                }

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
                var freeLookMovementSpeed = (float)_movementSpeedField.GetValue(__instance);
                var freeLookPositionOriginal = (Vector3)_positionField.GetValue(__instance);

                var inputVector = new Vector3(
                    (InputManager.TurnRightAction.IsPressed() ? 1 : 0) + (InputManager.TurnLeftAction.IsPressed() ? -1 : 0),
                    (InputManager.MoveForwardAction.IsPressed() ? 1 : 0) +
                    (InputManager.MoveBackwardAction.IsPressed() ? -1 : 0),
                    InputManager.JumpAction.IsPressed() ? 1 : InputManager.SlideAction.IsPressed() ? -1 : 0
                );
                var isSprinting = InputManager.SprintAction.IsPressed();
                var isSlowingDown = Plugin.slowDownAction != null && Plugin.slowDownAction.IsPressed();

                var speedMultiplier = 1f;
                if (isSprinting)
                    speedMultiplier = 2f;
                else if (isSlowingDown)
                    speedMultiplier = 0.25f;

                var currentMoveSpeed = freeLookMovementSpeed * speedMultiplier;

                var lookDelta = InputManager.StickAction.ReadValue<Vector2>();
                var lookSensitivity = SettingsManager.LookSensitivity;

                if (Plugin._currentCinematicRotation == Vector3.zero &&
                    __instance.transform.rotation != Quaternion.identity)
                    Plugin._currentCinematicRotation = __instance.transform.rotation.eulerAngles;

                var rotSmoothingFactor = Plugin.modSettings.cinematicSettings.rotationSmoothingFactor;
                var posSmoothingFactor = Plugin.modSettings.cinematicSettings.positionSmoothingFactor;

                if (Plugin._currentCinematicPosition == Vector3.zero)
                    Plugin._currentCinematicPosition = __instance.transform.position;

                var targetVelocity = Vector3.zero;
                targetVelocity += __instance.transform.right * inputVector.x * currentMoveSpeed;
                targetVelocity += __instance.transform.forward * inputVector.y * currentMoveSpeed;
                targetVelocity += __instance.transform.up * inputVector.z * currentMoveSpeed;

                Plugin._cinematicPositionVelocity = Vector3.Lerp(
                    Plugin._cinematicPositionVelocity,
                    targetVelocity,
                    1f - Mathf.Exp(-posSmoothingFactor * deltaTime * 10f)
                );

                Plugin._currentCinematicPosition += Plugin._cinematicPositionVelocity * deltaTime;

                var targetRotationVelocity = new Vector3(
                    -lookDelta.y * lookSensitivity,
                    lookDelta.x * lookSensitivity,
                    0f
                );

                Plugin._cinematicRotationVelocity = Vector3.Lerp(
                    Plugin._cinematicRotationVelocity,
                    targetRotationVelocity,
                    1f - Mathf.Exp(-rotSmoothingFactor * deltaTime * 10f)
                );

                Plugin._currentCinematicRotation += Plugin._cinematicRotationVelocity;

                Plugin._currentCinematicRotation.z = 0f;
                Plugin._currentCinematicRotation.x = Mathf.Clamp(Plugin._currentCinematicRotation.x, -80f, 80f);

                __instance.transform.position = Plugin._currentCinematicPosition;
                __instance.transform.rotation = Quaternion.Euler(Plugin._currentCinematicRotation);
                _positionField.SetValue(__instance, Plugin._currentCinematicPosition);

                return false;
            }

            return true;
        }
    }

    public static void PrintCameraCoordinates()
    {
        var p = Plugin.spectatorCamera.transform.position;
        var r = Plugin.spectatorCamera.transform.rotation.eulerAngles;
        ChatHelper.AddSystemMessage($"Position: {p.x} {p.y} {p.z} -- Rotation: {r.x} {r.y} {r.z}");
    }
}

// PatchPlayerCamera.cs

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

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
        if (_cachedLevel == null) _cachedLevel = Object.FindFirstObjectByType<Level>();
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

        // Grid-track mode state: angular velocity (deg/sec) for momentum-based tracking.
        private static float gridYawVel;
        private static float gridPitchVel;

        // TEMP /wpg diagnostic: throttle so we log at most ~1/sec.
        private static float gridDiagTimer;

        // The point the camera should watch, plus the velocity to use for lead.
        private struct WatchTarget
        {
            public bool HasTarget;
            public Vector3 Position;
            public Vector3 Velocity;
            public int Count;
        }

        // PuckManager.GetPuck() returns the *first-spawned* puck. During warmup
        // the game spawns many pucks, so that's an arbitrary stray off to the
        // side — and as pucks despawn/respawn across the warmup→play transition
        // the "first" one keeps changing, snapping the camera. Instead, frame the
        // centroid of all live pucks (with averaged velocity for the lead): in
        // warmup the camera watches the middle of the action, and as the warmup
        // pucks despawn the centroid glides smoothly to the single game puck. A
        // single puck reduces to that puck exactly. Falls back to replay pucks
        // (e.g. goal replays) when there are no live pucks.
        private static WatchTarget GetWatchTarget()
        {
            var result = new WatchTarget();
            var pm = PuckManager.Instance;
            if (pm == null) return result;

            var pucks = pm.GetPucks();
            if (pucks == null || pucks.Count == 0) pucks = pm.GetReplayPucks();
            if (pucks == null || pucks.Count == 0) return result;

            var sumPos = Vector3.zero;
            var sumVel = Vector3.zero;
            var n = 0;
            foreach (var puck in pucks)
            {
                if (puck == null) continue; // skip destroyed-but-not-yet-removed
                sumPos += puck.transform.position;
                if (puck.SynchronizedObject != null)
                    sumVel += puck.SynchronizedObject.PredictedLinearVelocity;
                n++;
            }

            if (n == 0) return result;
            result.HasTarget = true;
            result.Count = n;
            result.Position = sumPos / n;
            result.Velocity = sumVel / n;
            return result;
        }

        [HarmonyPrefix]
        public static bool Prefix(SpectatorCamera __instance, float deltaTime)
        {
            elapsedTime += Time.deltaTime;
            Plugin.spectatorCamera = __instance;

            // Dynamic FOV: scale FOV with camera→puck distance. Far = zoomed in
            // (narrow), close = zoomed out (wide), smoothed so it never snaps.
            // BaseCamera.UnityCamera is the spectator's own Camera component;
            // fall back through child + Camera.main in case it's null on b323.
            var fovCam = __instance.UnityCamera;
            if (fovCam == null) fovCam = __instance.GetComponentInChildren<Camera>();
            if (fovCam == null) fovCam = Camera.main;
            if (fovCam != null)
            {
                if (Plugin._dynamicFovOriginal < 0f)
                    Plugin._dynamicFovOriginal = fovCam.fieldOfView;

                if (Plugin.client_scrollZoomEnabled)
                {
                    // Seed the target from the current FOV the first tick after
                    // enabling, so zoom starts where the view currently is.
                    if (Plugin.scrollZoomNeedsInit)
                    {
                        Plugin.scrollZoomTargetFov = Mathf.Clamp(fovCam.fieldOfView,
                            Plugin.scrollZoomMinFov, Plugin.scrollZoomMaxFov);
                        Plugin.scrollZoomNeedsInit = false;
                    }

                    // Ignore the wheel while a menu/chat wants the mouse, so
                    // scrolling UI doesn't also zoom the camera.
                    if (!GlobalStateManager.UIState.IsMouseRequired && Mouse.current != null)
                    {
                        var scrollY = Mouse.current.scroll.ReadValue().y;
                        if (Mathf.Abs(scrollY) > 0.01f)
                            // Sign only: wheel delta magnitude is platform-dependent
                            // (e.g. 120/notch on Windows), so step a fixed amount.
                            Plugin.scrollZoomTargetFov = Mathf.Clamp(
                                Plugin.scrollZoomTargetFov - Mathf.Sign(scrollY) * Plugin.scrollZoomStep,
                                Plugin.scrollZoomMinFov, Plugin.scrollZoomMaxFov);
                    }

                    Plugin._dynamicFovCurrent = Mathf.SmoothDamp(fovCam.fieldOfView,
                        Plugin.scrollZoomTargetFov, ref Plugin._dynamicFovVel,
                        Plugin.scrollZoomSmoothTime, Mathf.Infinity, Time.deltaTime);
                    fovCam.fieldOfView = Plugin._dynamicFovCurrent;
                }
                else if (Plugin.client_dynamicFovEnabled)
                {
                    var target = GetWatchTarget();
                    if (target.HasTarget)
                    {
                        var dist = Vector3.Distance(__instance.transform.position, target.Position);
                        var t = Mathf.InverseLerp(Plugin.dynamicFovNearDistance, Plugin.dynamicFovFarDistance, dist);
                        var targetFov = Mathf.Lerp(Plugin.dynamicFovNearFov, Plugin.dynamicFovFarFov, t);
                        Plugin._dynamicFovCurrent = Mathf.SmoothDamp(fovCam.fieldOfView, targetFov,
                            ref Plugin._dynamicFovVel, Plugin.dynamicFovSmoothTime, Mathf.Infinity, Time.deltaTime);
                        fovCam.fieldOfView = Plugin._dynamicFovCurrent;
                    }
                }
                else if (Plugin._dynamicFovOriginal > 0f &&
                         Mathf.Abs(fovCam.fieldOfView - Plugin._dynamicFovOriginal) > 0.01f)
                {
                    fovCam.fieldOfView = Mathf.SmoothDamp(fovCam.fieldOfView,
                        Plugin._dynamicFovOriginal, ref Plugin._dynamicFovVel, Plugin.dynamicFovSmoothTime,
                        Mathf.Infinity, Time.deltaTime);
                }
            }

            // Guard: if local player is no longer a spectator (e.g. respawned for
            // GamePhase.Faceoff onto Blue/Red), don't apply spectator-only camera
            // overrides. Stale flags would otherwise lock the camera to puck-relative
            // positions/rotations and ignore the game's normal player-follow logic.
            var pm = PlayerManager.Instance;
            var localPlayer = pm != null ? pm.GetLocalPlayer() : null;
            if (localPlayer != null &&
                (localPlayer.Team == PlayerTeam.Blue || localPlayer.Team == PlayerTeam.Red))
            {
                if (__instance.transform.parent != null)
                    __instance.transform.SetParent(null);
                return true;
            }

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

            if (Plugin.client_spectatorWatchPuckGrid)
            {
                // Free movement (same controls as /wp) is always available, with
                // or without a puck.
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

                // Aim target. We frame the *lead point* — where the action will be
                // in N seconds based on its horizontal velocity — instead of the
                // current spot, so the area being moved into gets the breathing
                // room in frame. The target is the centroid of all live pucks (see
                // GetWatchTarget); with one puck that's just the puck. With no
                // pucks on the ice, fall back to a fixed point above center ice.
                Vector3 aimTarget;
                var target = GetWatchTarget();
                if (target.HasTarget)
                {
                    var rawVel = target.Velocity;
                    rawVel.y = 0f;
                    // Exponential smoothing on the velocity vector so the lead
                    // doesn't snap on bounces / network jitter.
                    var alpha = 1f - Mathf.Exp(-deltaTime * Plugin.watchPuckGridLeadVelocitySmoothing);
                    Plugin.watchPuckGridSmoothedVelocity = Vector3.Lerp(
                        Plugin.watchPuckGridSmoothedVelocity, rawVel, alpha);
                    var puckPos = target.Position;
                    var leadPos = puckPos + Plugin.watchPuckGridSmoothedVelocity * Plugin.watchPuckGridLeadSeconds;

                    // Clamp the lead so the puck never gets pushed outside the
                    // center cell of the framing grid. Constraint: the angle
                    // between (camera→puck) and (camera→lead) must be ≤ the cap.
                    var camPos = __instance.transform.position;
                    var camToPuck = puckPos - camPos;
                    var camToLead = leadPos - camPos;
                    if (camToPuck.sqrMagnitude > 0.01f && camToLead.sqrMagnitude > 0.01f)
                    {
                        var angle = Vector3.Angle(camToPuck, camToLead);
                        if (angle > Plugin.watchPuckGridMaxLeadAngleDeg && angle > 0.001f)
                        {
                            var t = Plugin.watchPuckGridMaxLeadAngleDeg / angle;
                            leadPos = Vector3.Lerp(puckPos, leadPos, t);
                        }
                    }
                    aimTarget = leadPos;
                }
                else
                {
                    aimTarget = new Vector3(0f, 2f, 0f);
                    Plugin.watchPuckGridSmoothedVelocity = Vector3.zero;
                }

                var toTarget = aimTarget - __instance.transform.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    // Decompose the aim direction into yaw/pitch with atan2 rather
                    // than Quaternion.LookRotation(...).eulerAngles. The euler
                    // decomposition is discontinuous at the vertical singularity:
                    // when the aim direction passes near straight-down (a high
                    // camera with the puck beneath it, or — between plays, with no
                    // live puck — the center-ice fallback below the camera) Unity
                    // flips the extracted yaw by ~180° and pushes pitch past 90°.
                    // SmoothDampAngle then swings the camera to that bogus heading
                    // for a few frames before it snaps back. atan2 stays continuous.
                    var current = __instance.transform.rotation.eulerAngles;

                    var horizDist = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z);

                    // Yaw is undefined when the target is directly above/below;
                    // hold the current yaw through that degenerate window.
                    float targetYaw;
                    if (horizDist > 0.001f)
                        targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                    else
                        targetYaw = current.y;

                    // Pitch: positive = looking down (matches Unity's euler.x).
                    var targetPitch = Mathf.Atan2(-toTarget.y, horizDist) * Mathf.Rad2Deg;
                    targetPitch = Mathf.Max(targetPitch, -Plugin.watchPuckGridMaxLookUpDeg);

                    // TEMP diagnostic: remember the unclamped heading and whether
                    // the center clamp actually moved it.
                    var diagRawYaw = targetYaw;
                    var diagCenterYaw = float.NaN;
                    var diagClampEngaged = false;

                    var toCenter = -__instance.transform.position; // rink center is (0,0,0)
                    toCenter.y = 0f;
                    if (toCenter.sqrMagnitude > 25f) // > 5m from center
                    {
                        var centerYaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
                        var delta = Mathf.DeltaAngle(centerYaw, targetYaw);
                        var clampedDelta = Mathf.Clamp(delta, -Plugin.watchPuckGridYawDeviationDeg,
                            Plugin.watchPuckGridYawDeviationDeg);
                        targetYaw = centerYaw + clampedDelta;
                        diagCenterYaw = centerYaw;
                        diagClampEngaged = !Mathf.Approximately(delta, clampedDelta);
                    }

                    // TEMP /wpg diagnostic: ~1/sec, report how many pucks are actually
                    // detected (live vs replay) plus the raw→clamped yaw, so we can
                    // confirm there's really one puck and see if the center clamp is
                    // what's pulling the camera off the puck. Remove once diagnosed.
                    gridDiagTimer += deltaTime;
                    if (gridDiagTimer >= 1f)
                    {
                        gridDiagTimer = 0f;
                        var pmDiag = PuckManager.Instance;
                        var liveDiag = pmDiag != null ? pmDiag.GetPucks() : null;
                        var replayDiag = pmDiag != null ? pmDiag.GetReplayPucks() : null;
                        var liveCount = liveDiag != null ? liveDiag.Count : -1;
                        var replayCount = replayDiag != null ? replayDiag.Count : -1;
                        Plugin.Log(
                            $"/wpg diag: live={liveCount} replay={replayCount} targetCount={target.Count} " +
                            $"aim={aimTarget} rawYaw={diagRawYaw:F1} centerYaw={diagCenterYaw:F1} " +
                            $"clampedYaw={targetYaw:F1} clampEngaged={diagClampEngaged} camPos={__instance.transform.position}");
                    }

                    var viewportInside = true;
                    var gridCam = __instance.GetComponentInChildren<Camera>();
                    if (gridCam == null) gridCam = Camera.main;
                    if (gridCam != null)
                    {
                        var viewport = gridCam.WorldToViewportPoint(aimTarget);
                        const float minEdge = 1f / 3f;
                        const float maxEdge = 2f / 3f;
                        viewportInside = viewport.z > 0f &&
                                         viewport.x >= minEdge && viewport.x <= maxEdge &&
                                         viewport.y >= minEdge && viewport.y <= maxEdge;
                    }

                    var smoothTime = viewportInside ? 1.2f : 0.8f;

                    var newPitch = Mathf.SmoothDampAngle(current.x, targetPitch, ref gridPitchVel, smoothTime,
                        Mathf.Infinity, deltaTime);
                    var newYaw = Mathf.SmoothDampAngle(current.y, targetYaw, ref gridYawVel, smoothTime,
                        Mathf.Infinity, deltaTime);

                    __instance.transform.rotation = Quaternion.Euler(newPitch, newYaw, 0f);
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
                Plugin.client_spectatorWatchPuckGrid ||
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

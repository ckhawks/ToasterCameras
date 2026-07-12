// PatchPlayerCamera.cs

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ToasterCameras;

public static class PatchPlayerCamera
{
    // SpectatorCamera private fields we read/write by reflection (verified in b897):
    //   private Vector3 position
    //   private float movementSpeed
    //   private float positionSmoothTime
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

        // /wpg debug: throttle the diagnostic log to ~1/sec (only logs while
        // WatchPuckGridDebug.enabled).
        private static float gridDiagTimer;

        // Harmony prefix entry point. Kept deliberately thin: update the always-on
        // FOV control, bail out when the local player isn't a spectator, then hand
        // off to the one method that owns the active camera mode. Each Tick* method
        // returns the value the prefix should return — true lets SpectatorCamera's
        // own OnTick run, false suppresses it because we've taken over the camera.
        [HarmonyPrefix]
        public static bool Prefix(SpectatorCamera __instance, float deltaTime)
        {
            // SpectatorCamera is a NetworkObject: every player/spectator in the
            // session has their own, all replicated to this client, but only the
            // local player's is IsOwner. The game's own OnTick early-returns for
            // non-owners (see SpectatorCamera.OnTick: `if (!base.IsOwner) return;`)
            // and so must we — for EVERYTHING below, FOV control included.
            //
            // Without this guard we run the whole prefix for every spectator camera
            // in the match, and all the state it drives is static/shared:
            //   * the mode smoothing (gridYawVel / gridPitchVel /
            //     watchPuckGridSmoothedVelocity) — remote cameras at their own
            //     positions stomp the local camera's smoothing every frame, swinging
            //     /wpg (and the other look-at modes) off the puck toward the boards;
            //   * dynamic-FOV smoothing (_dynamicFovCurrent / _dynamicFovVel /
            //     _dynamicFovOriginal) — likewise corrupted, and worse, when a remote
            //     camera's own Camera is null/disabled UpdateFieldOfView falls back to
            //     Camera.main (the LOCAL render camera) and drives its FOV from the
            //     remote camera's distance-to-puck. That's the erratic /dfov behavior.
            if (!__instance.IsOwner) return true;

            elapsedTime += Time.deltaTime;
            Plugin.spectatorCamera = __instance;

            UpdateFieldOfView(__instance);

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

            switch (Plugin.cameraMode)
            {
                case CameraMode.Puck: return TickPuck(__instance);
                case CameraMode.WatchPuck: return TickWatchPuck(__instance, deltaTime);
                case CameraMode.WatchPuckGrid: return TickWatchPuckGrid(__instance, deltaTime);
                case CameraMode.WatchPuckAbove: return TickWatchPuckAbove(__instance, deltaTime);
                case CameraMode.WatchThirdPerson: return TickWatchThirdPerson(__instance);
                case CameraMode.WatchPuckSmart: return TickWatchPuckSmart(__instance);
                case CameraMode.WatchPuckSmart2: return TickWatchPuckSmart2(__instance);
                case CameraMode.StaticPosition: return TickStaticPosition(__instance);
            }

            // No dedicated mode active — cinematic smoothing is the free-fly fallback.
            if (Plugin.client_cinematicSmoothingEnabled)
                return TickCinematic(__instance, deltaTime);

            return true;
        }

        // ---- Shared helpers --------------------------------------------------

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

        // Free-fly speed scaling shared by every manually-flown mode: sprint
        // doubles, the slow-down modifier quarters, otherwise 1×.
        private static float SpeedMultiplier()
        {
            if (InputManager.SprintAction.IsPressed()) return 2f;
            if (Plugin.slowDownAction != null && Plugin.slowDownAction.IsPressed()) return 0.25f;
            return 1f;
        }

        // True when the reflected SpectatorCamera fields resolved. The free-fly and
        // cinematic modes read/write them, so they bail (letting the game's own
        // camera run) if reflection failed rather than NRE every frame.
        private static bool ReflectionReady()
        {
            if (_movementSpeedField != null && _positionField != null && _positionSmoothTimeField != null)
                return true;
            Plugin.Log(
                "ERROR: FieldInfo for _movementSpeedField, _positionField, or _positionSmoothTimeField is null!");
            return false;
        }

        // Center-ice framing anchors used by the "smart" auto-switch modes, derived
        // from the rink bounds (with sane fallbacks when the Level isn't found).
        private static void GetRinkFraming(out float iceWidth, out float iceLength,
            out float longPosition, out float height, out float widthPosition)
        {
            var level = GetLevel();
            iceWidth = level != null ? level.Bounds.extents.x : 18.75f;
            iceLength = level != null ? level.Bounds.extents.z : 38.12f;
            longPosition = iceLength - 5;
            height = 6f;
            widthPosition = iceWidth - 3;
        }

        // Shared WASD/jump/slide free-fly movement used by /wp and /wpg. Advances
        // the camera's smoothed free-look position from input and writes it back to
        // SpectatorCamera's private position field. No-ops while a menu/chat owns
        // the mouse so UI interaction doesn't also fly the camera.
        private static void ApplyFreeLookMovement(SpectatorCamera cam, float deltaTime)
        {
            if (GlobalStateManager.UIState.IsMouseRequired) return;

            var moveSpeedDefault = (float)_movementSpeedField.GetValue(cam);
            var positionSmoothing = (float)_positionSmoothTimeField.GetValue(cam);
            var freeLookPosition = (Vector3)_positionField.GetValue(cam);

            var moveVector = new Vector3(
                InputManager.TurnRightAction.ReadValue<float>() - InputManager.TurnLeftAction.ReadValue<float>(),
                InputManager.MoveForwardAction.ReadValue<float>() - InputManager.MoveBackwardAction.ReadValue<float>(),
                InputManager.JumpAction.IsPressed() ? 1 : InputManager.SlideAction.IsPressed() ? -1 : 0);

            var speed = moveSpeedDefault * SpeedMultiplier();
            freeLookPosition += cam.transform.right * moveVector.x * deltaTime * speed;
            freeLookPosition += cam.transform.forward * moveVector.y * deltaTime * speed;
            freeLookPosition += cam.transform.up * moveVector.z * deltaTime * speed;
            _positionField.SetValue(cam, freeLookPosition);
            cam.transform.position = Vector3.Lerp(cam.transform.position, freeLookPosition,
                deltaTime / Mathf.Max(positionSmoothing, 0.0001f));
        }

        // Always-on FOV control: scroll-wheel zoom (if enabled) takes precedence,
        // else dynamic distance-based FOV (if enabled), else ease back to the FOV
        // the game started with. Independent of the active camera mode.
        private static void UpdateFieldOfView(SpectatorCamera cam)
        {
            // BaseCamera.UnityCamera is the spectator's own Camera component;
            // fall back through child + Camera.main in case it's null on b323.
            var fovCam = cam.UnityCamera;
            if (fovCam == null) fovCam = cam.GetComponentInChildren<Camera>();
            if (fovCam == null) fovCam = Camera.main;
            if (fovCam == null) return;

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
                    var dist = Vector3.Distance(cam.transform.position, target.Position);
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

        // ---- Per-mode handlers ----------------------------------------------

        // /bep: pin the camera to the puck's transform.
        private static bool TickPuck(SpectatorCamera cam)
        {
            if (PuckManager.Instance == null) Plugin.Log("Puckmanager is so dead bro");

            var puck = PuckManager.Instance.GetPuck();
            if (puck != null)
            {
                cam.transform.position = puck.transform.position;
                cam.transform.rotation = puck.transform.rotation;
            }

            return false;
        }

        // /wp: free-fly while always looking at the puck.
        private static bool TickWatchPuck(SpectatorCamera cam, float deltaTime)
        {
            if (!ReflectionReady()) return true;

            var puck = PuckManager.Instance.GetPuck();
            if (puck != null) cam.transform.LookAt(puck.transform.position);
            ApplyFreeLookMovement(cam, deltaTime);

            return false;
        }

        // /wpg: free-fly available, but the camera auto-aims at the lead point of
        // the puck centroid, framed inside the center cell of a 3×3 grid.
        private static bool TickWatchPuckGrid(SpectatorCamera cam, float deltaTime)
        {
            if (!ReflectionReady()) return true;

            // Free movement (same controls as /wp) is always available, with
            // or without a puck.
            ApplyFreeLookMovement(cam, deltaTime);

            // Aim target. We frame the *lead point* — where the action will be
            // in N seconds based on its horizontal velocity — instead of the
            // current spot, so the area being moved into gets the breathing
            // room in frame. The target is the centroid of all live pucks (see
            // GetWatchTarget); with one puck that's just the puck. With no
            // pucks on the ice, fall back to a fixed point above center ice.
            // Diagnostic captures for /wpg debug (only meaningful while the
            // debug overlay is on). Seeded to the no-target values.
            var diagRawVel = Vector3.zero;
            var diagLeadRaw = new Vector3(0f, 2f, 0f);
            var diagLeadAngle = 0f;
            var diagLeadClamp = false;

            Vector3 aimTarget;
            var target = GetWatchTarget();
            if (target.HasTarget)
            {
                var rawVel = target.Velocity;
                rawVel.y = 0f;
                diagRawVel = rawVel;
                // Exponential smoothing on the velocity vector so the lead
                // doesn't snap on bounces / network jitter.
                var alpha = 1f - Mathf.Exp(-deltaTime * Plugin.watchPuckGridLeadVelocitySmoothing);
                Plugin.watchPuckGridSmoothedVelocity = Vector3.Lerp(
                    Plugin.watchPuckGridSmoothedVelocity, rawVel, alpha);
                var puckPos = target.Position;
                var leadPos = puckPos + Plugin.watchPuckGridSmoothedVelocity * Plugin.watchPuckGridLeadSeconds;
                diagLeadRaw = leadPos;

                // Clamp the lead so the puck never gets pushed outside the
                // center cell of the framing grid. Constraint: the angle
                // between (camera→puck) and (camera→lead) must be ≤ the cap.
                var camPos = cam.transform.position;
                var camToPuck = puckPos - camPos;
                var camToLead = leadPos - camPos;
                if (camToPuck.sqrMagnitude > 0.01f && camToLead.sqrMagnitude > 0.01f)
                {
                    var angle = Vector3.Angle(camToPuck, camToLead);
                    diagLeadAngle = angle;
                    if (angle > Plugin.watchPuckGridMaxLeadAngleDeg && angle > 0.001f)
                    {
                        var t = Plugin.watchPuckGridMaxLeadAngleDeg / angle;
                        leadPos = Vector3.Lerp(puckPos, leadPos, t);
                        diagLeadClamp = true;
                    }
                }
                aimTarget = leadPos;
            }
            else
            {
                aimTarget = new Vector3(0f, 2f, 0f);
                diagLeadRaw = aimTarget;
                Plugin.watchPuckGridSmoothedVelocity = Vector3.zero;
            }

            var toTarget = aimTarget - cam.transform.position;
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
                var current = cam.transform.rotation.eulerAngles;

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

                // Diagnostic: remember the unclamped heading and whether the
                // center clamp actually moved it.
                var diagRawYaw = targetYaw;
                var diagCenterYaw = float.NaN;
                var diagYawDev = 0f;
                var diagClampEngaged = false;

                var toCenter = -cam.transform.position; // rink center is (0,0,0)
                toCenter.y = 0f;
                var diagDistFromCenter = toCenter.magnitude;
                if (toCenter.sqrMagnitude > 25f) // > 5m from center
                {
                    var centerYaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
                    var delta = Mathf.DeltaAngle(centerYaw, targetYaw);
                    var clampedDelta = Mathf.Clamp(delta, -Plugin.watchPuckGridYawDeviationDeg,
                        Plugin.watchPuckGridYawDeviationDeg);
                    targetYaw = centerYaw + clampedDelta;
                    diagCenterYaw = centerYaw;
                    diagYawDev = delta;
                    diagClampEngaged = !Mathf.Approximately(delta, clampedDelta);
                }

                var viewportInside = true;
                var gridCam = cam.GetComponentInChildren<Camera>();
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

                // Publish the full internal state to the /wpg debug overlay and
                // emit a throttled log line — but only while debug is on, so this
                // costs nothing (no per-puck iteration, no alloc) in normal play.
                if (WatchPuckGridDebug.enabled)
                {
                    var pmDiag = PuckManager.Instance;
                    var liveDiag = pmDiag != null ? pmDiag.GetPucks() : null;
                    var replayDiag = pmDiag != null ? pmDiag.GetReplayPucks() : null;
                    var usingReplay = liveDiag == null || liveDiag.Count == 0;
                    WatchPuckGridDebug.Publish(
                        liveDiag, replayDiag, usingReplay,
                        target.HasTarget, target.Position, diagRawVel,
                        Plugin.watchPuckGridSmoothedVelocity, diagLeadRaw, aimTarget,
                        diagLeadAngle, diagLeadClamp,
                        diagRawYaw, diagCenterYaw, targetYaw, diagYawDev, diagClampEngaged,
                        targetPitch, viewportInside, cam.transform.position, current.y,
                        current.x, toTarget.magnitude, diagDistFromCenter);

                    gridDiagTimer += deltaTime;
                    if (gridDiagTimer >= 1f)
                    {
                        gridDiagTimer = 0f;
                        Plugin.Log(
                            $"/wpg diag: live={WatchPuckGridDebug.LiveCount} replay={WatchPuckGridDebug.ReplayCount} " +
                            $"targetCount={target.Count} aim={aimTarget} rawYaw={diagRawYaw:F1} " +
                            $"centerYaw={diagCenterYaw:F1} yawDev={diagYawDev:F1} clampedYaw={targetYaw:F1} " +
                            $"centerClamp={diagClampEngaged} leadClamp={diagLeadClamp} camPos={cam.transform.position}");
                    }
                }

                var smoothTime = viewportInside ? 1.2f : 0.8f;

                var newPitch = Mathf.SmoothDampAngle(current.x, targetPitch, ref gridPitchVel, smoothTime,
                    Mathf.Infinity, deltaTime);
                var newYaw = Mathf.SmoothDampAngle(current.y, targetYaw, ref gridYawVel, smoothTime,
                    Mathf.Infinity, deltaTime);

                cam.transform.rotation = Quaternion.Euler(newPitch, newYaw, 0f);
            }

            return false;
        }

        // /wpa: hover directly above the puck; only vertical input adjusts height.
        private static bool TickWatchPuckAbove(SpectatorCamera cam, float deltaTime)
        {
            if (!ReflectionReady()) return true;

            var puck = PuckManager.Instance.GetPuck();
            if (puck != null)
            {
                var moveVector = new Vector3(
                    InputManager.TurnRightAction.ReadValue<float>() - InputManager.TurnLeftAction.ReadValue<float>(),
                    InputManager.MoveForwardAction.ReadValue<float>() - InputManager.MoveBackwardAction.ReadValue<float>(),
                    InputManager.JumpAction.IsPressed() ? 1 : InputManager.SlideAction.IsPressed() ? -1 : 0);

                var speed = (float)_movementSpeedField.GetValue(cam) * SpeedMultiplier();
                var positionToSet = new Vector3(puck.transform.position.x,
                    cam.transform.position.y + moveVector.y * deltaTime * speed,
                    puck.transform.position.z);
                cam.transform.SetPositionAndRotation(positionToSet, cam.transform.rotation);
                cam.transform.LookAt(puck.transform.position);
            }

            return false;
        }

        // /wpl: chase a specific player from behind-and-above.
        private static bool TickWatchThirdPerson(SpectatorCamera cam)
        {
            var offset = new Vector3(0, 3, -2);
            var smoothSpeed = 10f;
            if (Plugin.thirdPersonPlayerToWatch == null) return true;
            var desiredPosition = Plugin.thirdPersonPlayerToWatch.PlayerBody.transform.position +
                                  Plugin.thirdPersonPlayerToWatch.PlayerBody.transform.TransformDirection(offset);
            cam.transform.position = Vector3.Lerp(cam.transform.position, desiredPosition,
                smoothSpeed * Time.deltaTime);
            cam.transform.LookAt(Plugin.thirdPersonPlayerToWatch.PlayerBody.transform.position +
                                 Plugin.thirdPersonPlayerToWatch.PlayerBody.transform
                                     .TransformDirection(new Vector3(0, 0, 2)));
            return false;
        }

        // /wps: snap to whichever of the four corner positions matches the puck's
        // quadrant, easing across when the puck changes quadrant.
        private static bool TickWatchPuckSmart(SpectatorCamera cam)
        {
            var puck = PuckManager.Instance.GetPuck();
            if (puck != null)
            {
                GetRinkFraming(out _, out _, out var longPosition, out var height, out var widthPosition);

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
                    cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, t);

                    if (t >= 1f)
                    {
                        cam.transform.position = targetPos;
                        isMoving = false;
                    }
                }

                cam.transform.LookAt(puck.transform.position);
            }

            return false;
        }

        // /wps2: pick the nearest of ten framing positions to the puck, with a
        // cooldown so the camera doesn't thrash between positions.
        private static bool TickWatchPuckSmart2(SpectatorCamera cam)
        {
            GetRinkFraming(out var iceWidth, out var iceLength, out var longPosition,
                out var height, out var widthPosition);

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

                cam.transform.position = Vector3.SmoothDamp(cam.transform.position, targetPos,
                    ref velocity, cooldownTime);

                if (t >= 1f)
                {
                    cam.transform.position = targetPos;
                    isMoving = false;
                }
            }

            cam.transform.LookAt(puckPosition);
            return false;
        }

        // /cpos: jump to a fixed preset position/rotation.
        private static bool TickStaticPosition(SpectatorCamera cam)
        {
            if (Plugin.modSettings.cameraPositions.TryGetValue(
                    Plugin.client_spectatorStaticPosition, out var camPos))
            {
                cam.transform.position = camPos.GetPosition();
                cam.transform.rotation = Quaternion.Euler(camPos.GetRotation());
            }

            return false;
        }

        // F9: smoothed free-fly with eased look — the fallback when no dedicated
        // camera mode is active.
        private static bool TickCinematic(SpectatorCamera cam, float deltaTime)
        {
            if (!ReflectionReady()) return true;

            var freeLookMovementSpeed = (float)_movementSpeedField.GetValue(cam);

            var inputVector = new Vector3(
                (InputManager.TurnRightAction.IsPressed() ? 1 : 0) + (InputManager.TurnLeftAction.IsPressed() ? -1 : 0),
                (InputManager.MoveForwardAction.IsPressed() ? 1 : 0) +
                (InputManager.MoveBackwardAction.IsPressed() ? -1 : 0),
                InputManager.JumpAction.IsPressed() ? 1 : InputManager.SlideAction.IsPressed() ? -1 : 0
            );
            var currentMoveSpeed = freeLookMovementSpeed * SpeedMultiplier();

            var lookDelta = InputManager.StickAction.ReadValue<Vector2>();
            var lookSensitivity = SettingsManager.LookSensitivity;

            if (Plugin._currentCinematicRotation == Vector3.zero &&
                cam.transform.rotation != Quaternion.identity)
                Plugin._currentCinematicRotation = cam.transform.rotation.eulerAngles;

            var rotSmoothingFactor = Plugin.modSettings.cinematicSettings.rotationSmoothingFactor;
            var posSmoothingFactor = Plugin.modSettings.cinematicSettings.positionSmoothingFactor;

            if (Plugin._currentCinematicPosition == Vector3.zero)
                Plugin._currentCinematicPosition = cam.transform.position;

            var targetVelocity = Vector3.zero;
            targetVelocity += cam.transform.right * inputVector.x * currentMoveSpeed;
            targetVelocity += cam.transform.forward * inputVector.y * currentMoveSpeed;
            targetVelocity += cam.transform.up * inputVector.z * currentMoveSpeed;

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

            cam.transform.position = Plugin._currentCinematicPosition;
            cam.transform.rotation = Quaternion.Euler(Plugin._currentCinematicRotation);
            _positionField.SetValue(cam, Plugin._currentCinematicPosition);

            return false;
        }
    }

    public static void PrintCameraCoordinates()
    {
        var p = Plugin.spectatorCamera.transform.position;
        var r = Plugin.spectatorCamera.transform.rotation.eulerAngles;
        ChatHelper.AddSystemMessage($"Position: {p.x} {p.y} {p.z} -- Rotation: {r.x} {r.y} {r.z}");
    }
}

using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ToasterCameras;

public class PatchPlayerCamera
{
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.OnTick))]
    private class PatchPlayerCameraOnTick
    {
        private static void Postfix(PlayerCamera __instance)
        {
            // Plugin.Log.LogInfo($"Patch: PlayerCamera.OnTick (Postfix) was called.");
            if (Plugin.becomePuckPlayerCameras.Contains(__instance))
            {
                __instance.transform.position = Plugin.puckManager.GetPuck().transform.position;
                __instance.transform.rotation = Plugin.puckManager.GetPuck().transform.rotation;
            }

            // Plugin.Log.LogInfo($"Rotation: {__instance.transform.rotation.ToString()}");
            // Plugin.Log.LogInfo($"Position: {__instance.transform.position.ToString()}");
            // Plugin.Log.LogInfo("Tick!");
            // KeyCode keycode = KeyCode.A;
            // Plugin.Log.LogInfo("Tick2!");
            // Plugin.Log.LogInfo($"Tick called on thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            // if (Input.GetKeyDown(keycode))
            // {
            //     Plugin.Log.LogInfo("Tick3!");
            // }
        }
    }

    private static Il2CppReferenceArray<Object> FindAllPucks()
    {
        // Use Unity's FindObjectsOfType to find all Puck components
        var pucks = Object.FindObjectsOfType(Il2CppType.Of<Puck>());
        Plugin.Log.LogInfo($"Found {pucks.Count} Puck objects in the scene.");

        return pucks;
    }


    [HarmonyPatch(typeof(SpectatorCamera), nameof(SpectatorCamera.OnTick))]
    private class PatchSpectatorCameraOnTick
    {
        private static float elapsedTime; // Tracks the time passed
        private static bool isMoving;
        public static Vector3 targetPos = Vector3.zero;
        public static readonly float duration = 6.0f;
        private static readonly float iceWidth = 18.75f;
        private static readonly float iceLength = 38.12f;
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
            if (Plugin.client_spectatorIsPuck)
            {
                // Il2CppReferenceArray<Object> pucks = FindAllPucks();

                if (Plugin.puckManager == null) Plugin.Log.LogInfo("Puckmanager is so dead bro");

                var puck = Plugin.puckManager.GetPuck();

                // Object puck = pucks[0];
                // GameObject puck = (GameObject) pucks[0];

                if (puck != null)
                {
                    // Plugin.Log.LogInfo("specpuck2");
                    __instance.transform.position = puck.transform.position;
                    // Plugin.Log.LogInfo("specpuck3");
                    __instance.transform.rotation = puck.transform.rotation;
                }

                return false;
            }

            if (Plugin.client_spectatorWatchPuck)
            {
                var puck = Plugin.puckManager.GetPuck();
                if (puck != null) Plugin.spectatorCamera.transform.LookAt(puck.transform.position);
                bool isMouseActive = NetworkBehaviourSingleton<UIManager>.Instance.isMouseActive;
                if (!isMouseActive)
                {
                    Vector3 moveVector = new Vector3(__instance.moveRightAction.ReadValue<float>() - __instance.moveLeftAction.ReadValue<float>(), __instance.moveForwardAction.ReadValue<float>() - __instance.moveBackwardAction.ReadValue<float>(), (float)(__instance.jumpAction.IsPressed() ? 1 : (__instance.slideAction.IsPressed() ? (-1) : 0)));
                    float speed = (__instance.sprintAction.IsPressed() ? (__instance.freeLookMovementSpeed * 2f) : __instance.freeLookMovementSpeed);
                    __instance.freeLookPosition += __instance.transform.right * moveVector.x * deltaTime * speed;
                    __instance.freeLookPosition += __instance.transform.forward * moveVector.y * deltaTime * speed;
                    __instance.freeLookPosition += __instance.transform.up * moveVector.z * deltaTime * speed;
                    // Vector2 lookDelta = this.stickAction.ReadValue<Vector2>();
                    // float mouseSensitivity = MonoBehaviourSingleton<SettingsManager>.Instance.LookSensitivity;
                    // this.freeLookAngle += new Vector3(-lookDelta.y * mouseSensitivity, lookDelta.x * mouseSensitivity, -this.freeLookRotation.eulerAngles.z);
                    // this.freeLookAngle.x = Mathf.Clamp(this.freeLookAngle.x, -80f, 80f);
                    // this.freeLookRotation = Quaternion.Euler(Utils.WrapEulerAngles(this.freeLookAngle));
                    __instance.transform.position = Vector3.Lerp(__instance.transform.position, __instance.freeLookPosition, deltaTime / __instance.freeLookPositionSmoothing);
                    // base.transform.rotation = Quaternion.Lerp(base.transform.rotation, this.freeLookRotation, deltaTime / this.freeLookRotationSmoothing);
                }
                
                return false;
            }

            if (Plugin.client_spectatorWatchPuckAbove)
            {
                var puck = Plugin.puckManager.GetPuck();
                if (puck != null)
                {
                    Vector3 moveVector = new Vector3(__instance.moveRightAction.ReadValue<float>() - __instance.moveLeftAction.ReadValue<float>(), __instance.moveForwardAction.ReadValue<float>() - __instance.moveBackwardAction.ReadValue<float>(), (float)(__instance.jumpAction.IsPressed() ? 1 : (__instance.slideAction.IsPressed() ? (-1) : 0)));
                    float speed = (__instance.sprintAction.IsPressed() ? (__instance.freeLookMovementSpeed * 2f) : __instance.freeLookMovementSpeed);
                    // __instance.freeLookPosition += __instance.transform.right * moveVector.x * deltaTime * speed;
                    // __instance.freeLookPosition += __instance.transform.up * moveVector.z * deltaTime * speed;
                    // __instance.transform.position = Vector3.Lerp(__instance.transform.position, __instance.freeLookPosition, deltaTime / __instance.freeLookPositionSmoothing);
                    var positionToSet = new Vector3(puck.transform.position.x, __instance.transform.position.y + moveVector.y * deltaTime * speed,
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
                var desiredPosition = Plugin.thirdPersonPlayerToWatch.playerBody.transform.position +
                                      Plugin.thirdPersonPlayerToWatch.playerBody.transform.TransformDirection(offset);
                __instance.transform.position = Vector3.Lerp(__instance.transform.position, desiredPosition,
                    smoothSpeed * Time.deltaTime);
                // Vector3 positionToSet = new Vector3(player.transform.position.x, __instance.transform.position.y,
                //     puck.transform.position.z);
                // __instance.transform.SetPositionAndRotation(positionToSet, __instance.transform.rotation);
                Plugin.spectatorCamera.transform.LookAt(Plugin.thirdPersonPlayerToWatch.playerBody.transform.position +
                                                        Plugin.thirdPersonPlayerToWatch.playerBody.transform
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

                var puck = Plugin.puckManager.GetPuck();
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
                var pucks = Plugin.puckManager.GetPucks();
                var puck = pucks.ToArray().Length > 0 ? pucks.ToArray()[0] : null;
                if (puck == null)
                {
                    // try to get a replay puck instead
                    pucks = Plugin.puckManager.GetReplayPucks();
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

            return true;
            // Plugin.Log.LogInfo($"Rotation: {__instance.transform.rotation.ToString()}");
            // Plugin.Log.LogInfo($"Position: {__instance.transform.position.ToString()}");
        }
    }
    
    // [HarmonyPatch(typeof(SpectatorCamera), nameof(SpectatorCamera.OnTick))]
    // public static class PatchSpectatorCameraOnTick2
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(SpectatorCamera __instance, float deltaTime)
    //     {
    //         bool isMouseActive = Plugin.uiManager.isMouseActive;
    //         if (isMouseActive)
    //         {
    //             if (Plugin.client_spectatorWatchPuck) return false;
    //             if (Plugin.client_spectatorWatchThirdPerson) return false;
    //             if (Plugin.client_spectatorWatchPuckSmart) return false;
    //             if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         }
    //
    //         return true;
    //     }
    //
    // }

    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.Client_LookInputRpc))]
    // public static class PatchPlayerInputClientLookInputRpc
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.Client_LookAngleInputRpc))]
    // public static class PatchPlayerInputClientLookAngleInputRpc
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }

    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_152722375))]
    // public static class PatchPlayerInputRpcHandler152722375
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler152722375");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_341272022))]
    // public static class PatchPlayerInputRpcHandler341272022
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler341272022");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_354985997))]
    // public static class PatchPlayerInputRpcHandler354985997
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler354985997");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_778340344))]
    // public static class PatchPlayerInputRpcHandler778340344
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler778340344");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_804686296))]
    // public static class PatchPlayerInputRpcHandler804686296
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler804686296");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_817646686))]
    // public static class PatchPlayerInputRpcHandler8117646686
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler8117646686");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // // round 2
    //
    // // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_1047632353))]
    // // public static class PatchPlayerInputRpcHandler1047632353
    // // {
    // //     [HarmonyPrefix]
    // //     public static bool Prefix(PlayerInput __instance)
    // //     {
    // //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler1047632353");
    // //         if (Plugin.client_spectatorWatchPuck) return false;
    // //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    // //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    // //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    // //         return true;
    // //     }
    // // }
    //

    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_1234853793))]
    // public static class PatchPlayerInputRpcHandler1234853793
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler1234853793");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_1583302350))]
    // public static class PatchPlayerInputRpcHandler1583302350
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler1583302350");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_2333051307))]
    // public static class PatchPlayerInputRpcHandler2333051307
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler2333051307");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_2380271940))]
    // public static class PatchPlayerInputRpcHandler2380271940
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler2380271940");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_2671629003))]
    // public static class PatchPlayerInputRpcHandler2671629003
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler2671629003");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_2722698928))]
    // public static class PatchPlayerInputRpcHandler2722698928
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler2722698928");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_2892078582))]
    // public static class PatchPlayerInputRpcHandler2892078582
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler2892078582");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_2917244568))]
    // public static class PatchPlayerInputRpcHandler2917244568
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler2917244568");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3003669798))]
    // public static class PatchPlayerInputRpcHandler3003669798
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3003669798");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3013974635))]
    // public static class PatchPlayerInputRpcHandler3013974635
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3013974635");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3072819325))]
    // public static class PatchPlayerInputRpcHandler3072819325
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3072819325");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3261073083))]
    // public static class PatchPlayerInputRpcHandler3261073083
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3261073083");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3288109408))]
    // public static class PatchPlayerInputRpcHandler3288109408
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3288109408");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3713736028))]
    // public static class PatchPlayerInputRpcHandler3713736028
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3713736028");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3765825011))]
    // public static class PatchPlayerInputRpcHandler3765825011
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3765825011");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3779091983))]
    // public static class PatchPlayerInputRpcHandler3779091983
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3779091983");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3839358977))]
    // public static class PatchPlayerInputRpcHandler3839358977
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3839358977");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_3995092734))]
    // public static class PatchPlayerInputRpcHandler3995092734
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler3995092734");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_4077849638))]
    // public static class PatchPlayerInputRpcHandler4077849638
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler4077849638");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
    //
    // [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.__rpc_handler_4107840079))]
    // public static class PatchPlayerInputRpcHandler4107840079
    // {
    //     [HarmonyPrefix]
    //     public static bool Prefix(PlayerInput __instance)
    //     {
    //         Plugin.Log.LogInfo("PatchPlayerInputRpcHandler4107840079");
    //         if (Plugin.client_spectatorWatchPuck) return false;
    //         if (Plugin.client_spectatorWatchThirdPerson) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart) return false;
    //         if (Plugin.client_spectatorWatchPuckSmart2) return false;
    //         return true;
    //     }
    // }
}
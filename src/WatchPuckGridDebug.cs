// WatchPuckGridDebug.cs
//
// Diagnostics for the /wpg (WatchPuckGrid) auto-framed broadcast camera. This
// mode occasionally "drifts" — the camera swings off toward the boards instead
// of tracking the puck. There are a handful of independent things inside
// TickWatchPuckGrid that can each cause that, and from the outside they all look
// identical (camera not on the puck). This module makes every one of them
// observable so the failure can be caught in the act:
//
//   * The aim target is the CENTROID of every puck PuckManager reports — during
//     warmup that's a swarm, and a single stray/replay puck off in a corner
//     drags the centroid (and therefore the camera) toward the boards.
//   * The lead point projects the puck forward by its smoothed velocity; a bad
//     velocity spike pushes the lead way out ahead.
//   * The center-yaw clamp (watchPuckGridYawDeviationDeg) forces the heading to
//     within N degrees of "toward rink center" whenever the camera is far from
//     center — so when the puck is on the far side, the camera is *deliberately*
//     pointed off it, at the boards. This is the prime suspect and the hardest
//     to see without instrumentation.
//
// Toggle with /wpgdebug. When on you get: world-space markers for every puck it
// sees (green = live, yellow = replay), the centroid, the raw lead point, and
// the final clamped aim target, plus lines from the camera to each; an on-screen
// HUD dumping all the numeric internals with the active clamps highlighted; and
// throttled log lines. All of it is gated on `enabled`, so there is zero cost
// (no allocations, no extra puck iteration, no GameObjects) when it's off.

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ToasterCameras;

// One puck as /wpg saw it this frame.
public struct WpgPuckSample
{
    public Vector3 Position;
    public Vector3 Velocity;
    public bool IsReplay;
}

// Snapshot of everything TickWatchPuckGrid computed on its most recent frame.
// Written once per /wpg tick (only while `enabled`), read by the overlay's
// Update/OnGUI. Plain static fields rather than a struct so the overlay can read
// it without copying the puck list every frame.
public static class WatchPuckGridDebug
{
    public static bool enabled = false;

    // Set true for exactly one tick right after enabling, so the overlay can
    // warn if /wpg isn't the active mode (nothing is publishing).
    public static bool hasSnapshot;
    public static float snapshotTime;

    // Pucks the mode is averaging over this frame (only populated while enabled).
    public static readonly List<WpgPuckSample> Pucks = new();
    public static int LiveCount;
    public static int ReplayCount;
    public static bool UsingReplayPucks; // target fell back to replay pucks
    public static bool HasTarget;

    // Aim pipeline: centroid -> lead -> angular-clamped aim.
    public static Vector3 Centroid;
    public static Vector3 RawVelocity;      // averaged puck velocity (horizontal)
    public static Vector3 SmoothedVelocity; // after exponential smoothing
    public static Vector3 LeadPointRaw;     // centroid + smoothedVel*leadSeconds
    public static Vector3 AimTarget;        // final aim after the lead-angle clamp
    public static float LeadAngleDeg;       // angle(cam->puck, cam->lead)
    public static bool LeadAngleClampEngaged;

    // Heading pipeline: raw yaw -> center clamp -> smoothed.
    public static float RawYaw;
    public static float CenterYaw;          // NaN when the center clamp was inactive
    public static float ClampedYaw;
    public static float YawDeviationFromCenter; // signed, pre-clamp
    public static bool CenterClampEngaged;      // THE prime-suspect flag
    public static float TargetPitch;
    public static bool ViewportInside;      // aim inside the center 3x3 cell

    public static Vector3 CamPos;
    public static float CamYaw;
    public static float CamPitch;
    public static float DistToAim;
    public static float DistFromCenter;     // camera's horizontal distance to (0,0,0)

    // Called once per /wpg tick from TickWatchPuckGrid. Cheap; the caller only
    // invokes it when `enabled`. `pucks`/`replay` are the raw manager lists so we
    // can record per-puck positions and the live/replay split here (the mode
    // itself only keeps the centroid).
    public static void Publish(
        IReadOnlyList<Puck> pucks, IReadOnlyList<Puck> replay, bool usingReplay,
        bool hasTarget, Vector3 centroid, Vector3 rawVel, Vector3 smoothedVel,
        Vector3 leadRaw, Vector3 aim, float leadAngle, bool leadClamp,
        float rawYaw, float centerYaw, float clampedYaw, float yawDev, bool centerClamp,
        float targetPitch, bool viewportInside, Vector3 camPos, float camYaw,
        float camPitch, float distToAim, float distFromCenter)
    {
        hasSnapshot = true;
        snapshotTime = Time.time;

        Pucks.Clear();
        LiveCount = 0;
        ReplayCount = 0;
        var source = usingReplay ? replay : pucks;
        if (source != null)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var p = source[i];
                if (p == null) continue;
                var isReplay = p.IsReplay != null && p.IsReplay.Value;
                Vector3 vel = p.SynchronizedObject != null
                    ? p.SynchronizedObject.PredictedLinearVelocity
                    : Vector3.zero;
                Pucks.Add(new WpgPuckSample
                {
                    Position = p.transform.position,
                    Velocity = vel,
                    IsReplay = isReplay
                });
                if (isReplay) ReplayCount++; else LiveCount++;
            }
        }

        UsingReplayPucks = usingReplay;
        HasTarget = hasTarget;
        Centroid = centroid;
        RawVelocity = rawVel;
        SmoothedVelocity = smoothedVel;
        LeadPointRaw = leadRaw;
        AimTarget = aim;
        LeadAngleDeg = leadAngle;
        LeadAngleClampEngaged = leadClamp;
        RawYaw = rawYaw;
        CenterYaw = centerYaw;
        ClampedYaw = clampedYaw;
        YawDeviationFromCenter = yawDev;
        CenterClampEngaged = centerClamp;
        TargetPitch = targetPitch;
        ViewportInside = viewportInside;
        CamPos = camPos;
        CamYaw = camYaw;
        CamPitch = camPitch;
        DistToAim = distToAim;
        DistFromCenter = distFromCenter;
    }

    // Turn the toggle on/off. When turning off, hide the world markers immediately
    // so nothing lingers.
    public static void SetEnabled(bool on)
    {
        enabled = on;
        if (!on) WpgDebugOverlay.HideMarkers();
    }
}

// Drives the world-space markers (Update) and the on-screen HUD (OnGUI). Lives
// on the same runner GameObject as the puck indicators. Does nothing unless the
// debug toggle is on AND /wpg is the active camera mode.
public class WpgDebugOverlay : MonoBehaviour
{
    // ---- World-space line markers (pooled LineRenderers) -----------------

    private static readonly List<LineRenderer> LinePool = new();
    private static GameObject markerRoot;
    private static Material lineMaterial;
    private static int linesUsedThisFrame;

    // Colors for the different markers.
    private static readonly Color LiveColor = new(0.2f, 1f, 0.2f, 1f);      // green
    private static readonly Color ReplayColor = new(1f, 0.85f, 0.1f, 1f);   // yellow
    private static readonly Color CentroidColor = new(0.2f, 0.8f, 1f, 1f);  // cyan
    private static readonly Color LeadColor = new(1f, 0.2f, 1f, 1f);        // magenta
    private static readonly Color AimColor = new(1f, 0.25f, 0.25f, 1f);     // red
    private static readonly Color CamLineColor = new(1f, 1f, 1f, 0.6f);     // white
    private static readonly Color SpectatorColor = new(1f, 0.5f, 0.05f, 1f); // orange — other players' cams

    // Other players' spectator cameras found this frame, so OnGUI can label them
    // by owner. These are the remote SpectatorCamera NetworkObjects replicated to
    // us (the ones whose stray ticks used to corrupt /wpg — see PatchPlayerCamera
    // IsOwner guard).
    private struct OtherCam
    {
        public Vector3 Pos;
        public string Name;
    }

    private static readonly List<OtherCam> otherCams = new();

    private static Material GetLineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        lineMaterial = new Material(Shader.Find("Sprites/Default"));
        return lineMaterial;
    }

    private void Update()
    {
        if (!WatchPuckGridDebug.enabled)
        {
            HideMarkers();
            otherCams.Clear();
            return;
        }

        BeginLines();

        // Always (whenever debug is on, regardless of camera mode): mark every
        // OTHER player's spectator camera. These are the remote cameras that used
        // to corrupt /wpg's shared smoothing before the IsOwner guard.
        DrawSpectatorCameras();

        // The /wpg framing markers only make sense while that mode is actually
        // running and has something to frame.
        if (Plugin.cameraMode == CameraMode.WatchPuckGrid &&
            (WatchPuckGridDebug.HasTarget || WatchPuckGridDebug.Pucks.Count > 0))
        {
            // Every puck it's averaging over: a 3-axis cross plus a tall vertical
            // stalk so it's findable from across the rink. Green live, yellow replay.
            foreach (var p in WatchPuckGridDebug.Pucks)
            {
                var c = p.IsReplay ? ReplayColor : LiveColor;
                DrawCross(p.Position, 0.6f, c);
                DrawLine(p.Position, p.Position + Vector3.up * 30f, c, 0.03f);
            }

            // Centroid (what the camera actually frames), the raw lead point, and
            // the final clamped aim — plus a line from the camera to the aim so the
            // heading the camera is chasing is literally drawn in the world.
            DrawCross(WatchPuckGridDebug.Centroid, 0.8f, CentroidColor);
            DrawLine(WatchPuckGridDebug.Centroid, WatchPuckGridDebug.Centroid + Vector3.up * 40f,
                CentroidColor, 0.05f);

            DrawCross(WatchPuckGridDebug.LeadPointRaw, 0.7f, LeadColor);
            DrawCross(WatchPuckGridDebug.AimTarget, 1.0f, AimColor);
            DrawLine(WatchPuckGridDebug.CamPos, WatchPuckGridDebug.AimTarget, AimColor, 0.04f);

            // Camera -> centroid, so you can eyeball the angular gap between "where
            // the camera looks" (aim/red) and "where the puck is" (centroid/cyan) —
            // that gap IS the drift.
            DrawLine(WatchPuckGridDebug.CamPos, WatchPuckGridDebug.Centroid, CamLineColor, 0.02f);
        }

        EndLines();
    }

    // Mark every spectator camera that isn't ours: a cross, a vertical stalk so
    // it's findable, and a short line along its forward vector to show where it's
    // looking. Records each one's position + owner name for the OnGUI labels.
    private void DrawSpectatorCameras()
    {
        otherCams.Clear();
        var cams = Object.FindObjectsByType<SpectatorCamera>(FindObjectsSortMode.None);
        if (cams == null) return;
        foreach (var sc in cams)
        {
            if (sc == null || sc.IsOwner) continue;

            var pos = sc.transform.position;
            DrawCross(pos, 0.8f, SpectatorColor);
            DrawLine(pos, pos + Vector3.up * 25f, SpectatorColor, 0.03f);
            DrawLine(pos, pos + sc.transform.forward * 3f, SpectatorColor, 0.05f);

            string name = null;
            if (sc.Player != null)
            {
                try { name = sc.Player.Username.Value.ToString(); }
                catch { /* username not ready */ }
            }
            otherCams.Add(new OtherCam
            {
                Pos = pos,
                Name = string.IsNullOrEmpty(name) ? "spectator" : name
            });
        }
    }

    // ---- Line pool plumbing ----------------------------------------------

    private static void EnsureRoot()
    {
        if (markerRoot != null) return;
        markerRoot = new GameObject("WpgDebugMarkers") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(markerRoot);
    }

    private void BeginLines() => linesUsedThisFrame = 0;

    private void EndLines()
    {
        // Disable any pooled lines we didn't use this frame.
        for (var i = linesUsedThisFrame; i < LinePool.Count; i++)
            if (LinePool[i] != null && LinePool[i].enabled) LinePool[i].enabled = false;
    }

    private LineRenderer NextLine()
    {
        EnsureRoot();
        if (linesUsedThisFrame < LinePool.Count)
        {
            var existing = LinePool[linesUsedThisFrame++];
            if (existing != null)
            {
                if (!existing.enabled) existing.enabled = true;
                return existing;
            }
        }

        var go = new GameObject("WpgDebugLine") { hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(markerRoot.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCapVertices = 0;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = GetLineMaterial();

        if (linesUsedThisFrame < LinePool.Count) LinePool[linesUsedThisFrame] = lr;
        else LinePool.Add(lr);
        linesUsedThisFrame++;
        return lr;
    }

    private void DrawLine(Vector3 a, Vector3 b, Color color, float width)
    {
        var lr = NextLine();
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    // A 3D "+" of the given full size centered at p.
    private void DrawCross(Vector3 p, float size, Color color)
    {
        var h = size * 0.5f;
        DrawLine(p - Vector3.right * h, p + Vector3.right * h, color, 0.04f);
        DrawLine(p - Vector3.up * h, p + Vector3.up * h, color, 0.04f);
        DrawLine(p - Vector3.forward * h, p + Vector3.forward * h, color, 0.04f);
    }

    public static void HideMarkers()
    {
        for (var i = 0; i < LinePool.Count; i++)
            if (LinePool[i] != null && LinePool[i].enabled) LinePool[i].enabled = false;
        linesUsedThisFrame = 0;
    }

    // ---- On-screen HUD (IMGUI) -------------------------------------------

    private GUIStyle panelStyle;
    private GUIStyle textStyle;
    private GUIStyle labelStyle;
    private Texture2D panelTex;
    private readonly StringBuilder sb = new();

    private void OnGUI()
    {
        if (!WatchPuckGridDebug.enabled) return;

        EnsureStyles();

        // Spectator-camera labels are shown in every mode.
        DrawSpectatorCamLabels();

        if (Plugin.cameraMode != CameraMode.WatchPuckGrid)
        {
            GUI.Label(new Rect(12, 12, 700, 24),
                $"<b>/wpg debug ON</b> — <color=#ff8c1a>{otherCams.Count}</color> other spectator cam(s). Switch to /wpg for framing diagnostics.",
                labelStyle);
            return;
        }

        DrawHudPanel();
        DrawWorldLabels();
    }

    // Orange labels over each other player's spectator camera.
    private void DrawSpectatorCamLabels()
    {
        var cam = GetRenderCamera();
        if (cam == null) return;
        foreach (var oc in otherCams)
            WorldLabel(cam, oc.Pos + Vector3.up * 1.4f, $"<color=#ff8c1a>{oc.Name}'s cam</color>");
    }

    private static Camera GetRenderCamera()
    {
        var cam = Plugin.spectatorCamera != null ? Plugin.spectatorCamera.GetComponentInChildren<Camera>() : null;
        if (cam == null) cam = Camera.main;
        return cam;
    }

    private void DrawHudPanel()
    {
        sb.Length = 0;
        sb.AppendLine("<b>/wpg DIAGNOSTICS</b>");
        sb.AppendLine($"pucks: <b>{WatchPuckGridDebug.LiveCount}</b> live, " +
                      $"<b>{WatchPuckGridDebug.ReplayCount}</b> replay" +
                      (WatchPuckGridDebug.UsingReplayPucks ? "  <color=#ffd21a>[framing REPLAY]</color>" : ""));
        if (!WatchPuckGridDebug.HasTarget)
            sb.AppendLine("<color=#ff6666>NO TARGET — framing center-ice fallback</color>");
        if (WatchPuckGridDebug.LiveCount + WatchPuckGridDebug.ReplayCount > 1)
            sb.AppendLine("<color=#ffd21a>MULTI-PUCK — centroid is averaged (drift risk)</color>");

        sb.AppendLine($"centroid:  {Fmt(WatchPuckGridDebug.Centroid)}");
        sb.AppendLine($"vel raw:   {Fmt(WatchPuckGridDebug.RawVelocity)}  |{WatchPuckGridDebug.RawVelocity.magnitude:F1}|");
        sb.AppendLine($"vel smooth:{Fmt(WatchPuckGridDebug.SmoothedVelocity)}  |{WatchPuckGridDebug.SmoothedVelocity.magnitude:F1}|");
        sb.AppendLine($"lead raw:  {Fmt(WatchPuckGridDebug.LeadPointRaw)}");
        sb.AppendLine($"aim final: {Fmt(WatchPuckGridDebug.AimTarget)}");
        sb.AppendLine($"lead angle: {WatchPuckGridDebug.LeadAngleDeg:F1} / cap {Plugin.watchPuckGridMaxLeadAngleDeg:F0}" +
                      (WatchPuckGridDebug.LeadAngleClampEngaged ? "  <color=#ffd21a>[CLAMPED]</color>" : ""));

        sb.AppendLine("<b>heading</b>");
        sb.AppendLine($"raw yaw:   {WatchPuckGridDebug.RawYaw:F1}");
        sb.AppendLine($"cam dist from center: {WatchPuckGridDebug.DistFromCenter:F1}m" +
                      (WatchPuckGridDebug.DistFromCenter > 5f ? "  (center clamp armed >5m)" : "  (center clamp off <=5m)"));
        if (!float.IsNaN(WatchPuckGridDebug.CenterYaw))
        {
            sb.AppendLine($"center yaw: {WatchPuckGridDebug.CenterYaw:F1}   " +
                          $"deviation {WatchPuckGridDebug.YawDeviationFromCenter:F1} / cap {Plugin.watchPuckGridYawDeviationDeg:F0}");
            if (WatchPuckGridDebug.CenterClampEngaged)
                sb.AppendLine("<color=#ff6666>CENTER-YAW CLAMP ENGAGED — camera pulled toward center, OFF the puck</color>");
        }
        sb.AppendLine($"clamped yaw: {WatchPuckGridDebug.ClampedYaw:F1}   pitch: {WatchPuckGridDebug.TargetPitch:F1}");
        sb.AppendLine($"viewport (center cell): {(WatchPuckGridDebug.ViewportInside ? "<color=#66ff66>INSIDE</color>" : "<color=#ffd21a>OUTSIDE</color>")}");
        sb.AppendLine($"cam pos: {Fmt(WatchPuckGridDebug.CamPos)}  yaw {WatchPuckGridDebug.CamYaw:F1} pitch {WatchPuckGridDebug.CamPitch:F1}");
        sb.AppendLine($"dist cam->aim: {WatchPuckGridDebug.DistToAim:F1}m");
        sb.AppendLine($"other spectator cams: <color=#ff8c1a>{otherCams.Count}</color>");
        sb.AppendLine("<size=11>green=live puck  yellow=replay  cyan=centroid  magenta=lead  red=aim  orange=other cams</size>");

        var content = new GUIContent(sb.ToString());
        var w = 560f;
        var h = textStyle.CalcHeight(content, w - 20f) + 16f;
        GUI.Box(new Rect(10, 10, w, h), GUIContent.none, panelStyle);
        GUI.Label(new Rect(20, 18, w - 20f, h), content, textStyle);
    }

    // Floating world-space labels tag each marker so the numeric HUD and the 3D
    // markers can be cross-referenced at a glance.
    private void DrawWorldLabels()
    {
        var cam = GetRenderCamera();
        if (cam == null) return;

        for (var i = 0; i < WatchPuckGridDebug.Pucks.Count; i++)
        {
            var p = WatchPuckGridDebug.Pucks[i];
            WorldLabel(cam, p.Position + Vector3.up * 1.2f,
                p.IsReplay ? $"replay puck {i}" : $"puck {i}");
        }
        WorldLabel(cam, WatchPuckGridDebug.Centroid + Vector3.up * 1.6f, "centroid");
        WorldLabel(cam, WatchPuckGridDebug.LeadPointRaw + Vector3.up * 1.0f, "lead");
        WorldLabel(cam, WatchPuckGridDebug.AimTarget + Vector3.up * 1.4f, "AIM");
    }

    private void WorldLabel(Camera cam, Vector3 world, string text)
    {
        var sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return; // behind camera
        var r = new Rect(sp.x - 60f, Screen.height - sp.y - 10f, 120f, 20f);
        GUI.Label(r, text, labelStyle);
    }

    private static string Fmt(Vector3 v) => $"({v.x,6:F1},{v.y,5:F1},{v.z,6:F1})";

    private void EnsureStyles()
    {
        if (panelStyle != null) return;

        panelTex = new Texture2D(1, 1);
        panelTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
        panelTex.Apply();

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTex;

        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = true,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };
        textStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            richText = true,
            alignment = TextAnchor.MiddleCenter
        };
        labelStyle.normal.textColor = Color.white;
    }
}

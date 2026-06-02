// PuckPossessionIndicator.cs
//
// Ports the old BepInEx "puck possession circle" feature to b323. Each live puck
// gets a flat filled disc (a triangle-fan mesh) pinned to the ice directly
// beneath it — like the game's puck-elevation indicator circle rather than a
// solid cylinder. The disc is recolored on stick contact to the toucher's team
// color, and fades back to neutral gray after a fixed duration. Radius shrinks
// with puck height so it stays roughly puck-sized at table height and never
// blocks the puck visually when the puck is on the deck.
//
// Important: the client doesn't simulate puck physics, so Unity's OnCollisionEnter
// never fires for the puck on a client. We instead poll the puck's
// NetworkObjectCollisionRecorder.Buffer, which is server-written and synced to
// everyone, to detect new stick contacts. Updates run per-frame from a runner
// MonoBehaviour rather than from FixedUpdate so motion stays smooth.

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ToasterCameras;

public static class PuckPossessionIndicator
{
    public static bool enabled = true;
    public static float maxRadius = 0.4f;
    public static float opacity = 0.75f;   // disc alpha, 0 (invisible) .. 1 (opaque)
    public static float possessionDurationSeconds = 5f;
    public static float smallestFactor = 0.2f;
    public static float maxHeightForSmallest = 18f;
    // Number of segments around the rim. More = smoother circle.
    private const int DiscSegments = 48;

    // Per-puck visual: the disc mesh plus a cached color buffer/last-applied
    // color so the every-frame recolor only re-uploads mesh colors when the
    // team actually changes.
    private class Indicator
    {
        public GameObject go;
        public Mesh mesh;
        public Color[] colors;
        public Color applied;
        public bool hasApplied;
    }

    private static readonly Dictionary<Puck, Indicator> indicatorMap = new();
    private static readonly Dictionary<Puck, float> lastTouchTime = new();
    // Highest collision Time we've already reacted to per puck — used to detect
    // new entries in the synced NetworkObjectCollisionRecorder buffer.
    private static readonly Dictionary<Puck, float> lastSeenCollisionTime = new();
    // Last team to touch this puck. Read by other features (e.g. PuckBeam).
    private static readonly Dictionary<Puck, PlayerTeam> lastTouchTeamMap = new();

    // True while the match is in warmup. The possession ring and puck beam are
    // gameplay-feedback overlays that only make sense during live play, so both
    // features suppress themselves in warmup.
    public static bool IsWarmup()
    {
        var gm = GameManager.Instance;
        return gm != null && gm.Phase == GamePhase.Warmup;
    }

    public static PlayerTeam GetLastTouchTeam(Puck puck)
    {
        if (puck == null) return PlayerTeam.None;
        if (Time.time - (lastTouchTime.TryGetValue(puck, out var t) ? t : -possessionDurationSeconds) >
            possessionDurationSeconds) return PlayerTeam.None;
        return lastTouchTeamMap.TryGetValue(puck, out var team) ? team : PlayerTeam.None;
    }

    private static Shader spritesShader;
    private static Material discMaterial;

    // One material shared across every disc. Per-disc color lives in the mesh's
    // vertex colors, not the material, so a single shared material is safe — and
    // it lets Unity dynamically batch the discs into far fewer draw calls when
    // there are many pucks on the ice.
    private static Material GetDiscMaterial()
    {
        if (discMaterial != null) return discMaterial;
        if (spritesShader == null) spritesShader = Shader.Find("Sprites/Default");
        discMaterial = new Material(spritesShader);
        return discMaterial;
    }

    // Shared geometry for a unit disc (radius 1) in the local XZ plane. Reused to
    // build each puck's mesh; only the per-puck vertex colors differ.
    private static Vector3[] discVerts;
    private static int[] discTris;

    private static void BuildDiscGeometry()
    {
        if (discVerts != null) return;

        // Center vertex + one vertex per rim point.
        discVerts = new Vector3[DiscSegments + 1];
        discVerts[0] = Vector3.zero;
        for (var i = 0; i < DiscSegments; i++)
        {
            var a = (i / (float)DiscSegments) * Mathf.PI * 2f;
            discVerts[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        // Triangle fan: (center, rim[i], rim[i+1]). Sprites/Default has Cull Off
        // so the disc is visible from both sides regardless of winding.
        discTris = new int[DiscSegments * 3];
        for (var i = 0; i < DiscSegments; i++)
        {
            discTris[i * 3] = 0;
            discTris[i * 3 + 1] = i + 1;
            discTris[i * 3 + 2] = (i + 1) % DiscSegments + 1;
        }
    }

    private static Indicator CreateIndicator(Puck puck)
    {
        BuildDiscGeometry();

        var go = new GameObject($"PuckPossessionIndicator_{puck.GetInstanceID()}");

        var mesh = new Mesh { name = "PuckPossessionDisc" };
        mesh.vertices = discVerts;
        mesh.triangles = discTris;
        // No normals/UVs: the sprites shader is unlit and samples its default
        // white texture, so vertex color alone drives the look — fewer vertex
        // attributes also keeps the mesh eligible for dynamic batching.

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetDiscMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var ind = new Indicator
        {
            go = go,
            mesh = mesh,
            colors = new Color[discVerts.Length]
        };
        SetColor(ind, PlayerTeam.None);
        return ind;
    }

    private static void SetColor(Indicator ind, PlayerTeam team)
    {
        if (ind == null) return;

        Color c;
        if (team == PlayerTeam.Blue || team == PlayerTeam.Red)
            c = TRLBridge.GetTeamColor(team);
        else
            c = Color.gray;
        c.a = Mathf.Clamp01(opacity);

        // Skip the mesh upload when the color hasn't changed.
        if (ind.hasApplied && ind.applied == c) return;

        for (var i = 0; i < ind.colors.Length; i++) ind.colors[i] = c;
        ind.mesh.colors = ind.colors;
        ind.applied = c;
        ind.hasApplied = true;
    }

    public static void Tick(Puck puck)
    {
        if (!enabled || puck == null) return;
        if (puck.IsReplay != null && puck.IsReplay.Value) return;

        if (!indicatorMap.TryGetValue(puck, out var ind) || ind == null || ind.go == null)
        {
            ind = CreateIndicator(puck);
            indicatorMap[puck] = ind;
            lastTouchTime[puck] = -possessionDurationSeconds;
            lastSeenCollisionTime[puck] = -1f;
        }

        if (!ind.go.activeSelf) ind.go.SetActive(true);

        var t = ind.go.transform;
        var pos = puck.transform.position;
        pos.y = 0.005f;
        t.position = pos;
        // The disc lies in the local XZ plane, so identity rotation keeps it
        // flat on the ice.

        var h = Mathf.Max(0f, puck.transform.position.y);
        var scale = Mathf.Clamp(1f - h * (1f / maxHeightForSmallest), smallestFactor, 1f);
        var radius = maxRadius * scale;
        t.localScale = new Vector3(radius, 1f, radius);

        PollCollisions(puck, ind);

        // Re-apply color every frame so TRL color/toggle changes take effect live
        // (SetColor no-ops when the color is unchanged, so this is cheap).
        var activeTeam = Time.time - lastTouchTime[puck] > possessionDurationSeconds
            ? PlayerTeam.None
            : (lastTouchTeamMap.TryGetValue(puck, out var team) ? team : PlayerTeam.None);
        SetColor(ind, activeTeam);
    }

    private static void PollCollisions(Puck puck, Indicator ind)
    {
        if (puck.NetworkObjectCollisionRecorder == null) return;

        List<KeyValuePair<Player, float>> list;
        try { list = puck.GetPlayerCollisions(); }
        catch { return; }
        if (list == null || list.Count == 0) return;

        // Pick the most recent player collision.
        var latest = list[0];
        for (var i = 1; i < list.Count; i++)
            if (list[i].Value > latest.Value) latest = list[i];

        var prev = lastSeenCollisionTime.TryGetValue(puck, out var p) ? p : -1f;
        if (latest.Value > prev && latest.Key != null)
        {
            lastSeenCollisionTime[puck] = latest.Value;
            SetColor(ind, latest.Key.Team);
            lastTouchTime[puck] = Time.time;
            lastTouchTeamMap[puck] = latest.Key.Team;
        }
    }

    public static void Cleanup(Puck puck)
    {
        if (puck == null) return;
        if (indicatorMap.TryGetValue(puck, out var ind) && ind != null)
        {
            if (ind.go != null) Object.Destroy(ind.go);
            if (ind.mesh != null) Object.Destroy(ind.mesh);
        }
        indicatorMap.Remove(puck);
        lastTouchTime.Remove(puck);
        lastSeenCollisionTime.Remove(puck);
        lastTouchTeamMap.Remove(puck);
    }

    // Deactivate every live disc without destroying it, so it can be revived
    // instantly when play resumes. Used to suppress the feature during warmup.
    private static void HideAll()
    {
        foreach (var ind in indicatorMap.Values)
            if (ind?.go != null && ind.go.activeSelf) ind.go.SetActive(false);
    }

    public static void TickAll()
    {
        if (PuckManager.Instance == null) return;
        if (IsWarmup()) { HideAll(); return; }
        var pucks = PuckManager.Instance.GetPucks(false);
        if (pucks == null) return;
        for (var i = 0; i < pucks.Count; i++) Tick(pucks[i]);
    }
}

public class PuckIndicatorRunner : MonoBehaviour
{
    private void Update()
    {
        PuckPossessionIndicator.TickAll();
        PuckBeam.TickAll();
    }
}

public static class PuckPossessionIndicatorPatches
{
    [HarmonyPatch(typeof(Puck), "OnDestroy")]
    private class PatchPuckOnDestroy
    {
        private static void Prefix(Puck __instance) => PuckPossessionIndicator.Cleanup(__instance);
    }
}

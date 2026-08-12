// PuckBeam.cs
//
// Vertical tracer beam above each live puck, drawn with a LineRenderer. The
// beam endpoints are pinned to fixed world Y values (not the puck's own Y), so
// the alpha gradient maps cleanly onto absolute world height:
//   y <= fadeStartY  → fully transparent
//   y == fadeEndY    → fully opaque
//   y >= fadeEndY    → fully opaque, up to topY
//
// Color is pulled from PuckPossessionIndicator's "last team to touch" record so
// the beam matches the on-ice possession ring (neutral white when nobody has
// touched the puck recently).
//
// Uses Sprites/Default for the LineRenderer material — that shader honors the
// per-vertex color/alpha that LineRenderer emits from its colorGradient, which
// the borrowed URP barrier material did not.

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ToasterCameras;

public static class PuckBeam
{
    public static bool enabled = true;

    // Visual tunables. Beam runs from (puck.xz, bottomY) up to (puck.xz, topY).
    public static float bottomY = 0f;
    public static float fadeStartY = 2f;   // alpha 0 at or below this world height
    public static float fadeEndY = 4f;     // alpha 1 at or above this world height
    public static float topY = 40f;        // beam terminates here in world space
    public static float topAlpha = 1f;     // peak alpha (1 = fully opaque)
    public static float width = 0.12f;

    private static readonly Dictionary<Puck, LineRenderer> beams = new();
    // Last team color baked into each beam's gradient, so we only rebuild the
    // Gradient (an allocation) when the color actually changes rather than every
    // frame. The beam's geometry tunables are compile-time constants, so color is
    // the only thing that can change the gradient.
    private static readonly Dictionary<Puck, Color> appliedColor = new();
    private static Shader spritesShader;
    private static Material beamMaterial;

    // One material shared across every beam. Per-beam color/alpha lives in the
    // LineRenderer's colorGradient (vertex colors), not the material, so a single
    // shared material is safe — and it lets Unity dynamically batch the beams
    // into far fewer draw calls when there are many pucks on the ice.
    private static Material GetBeamMaterial()
    {
        if (beamMaterial != null) return beamMaterial;
        if (spritesShader == null) spritesShader = Shader.Find("Sprites/Default");
        beamMaterial = new Material(spritesShader);
        return beamMaterial;
    }

    private static Color TeamColor(PlayerTeam team)
    {
        if (team == PlayerTeam.Blue || team == PlayerTeam.Red)
            return TRLBridge.GetTeamColor(team);
        return Color.white;
    }

    private static LineRenderer CreateBeam(Puck puck)
    {
        var go = new GameObject($"PuckBeam_{puck.GetInstanceID()}");
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 0;

        // sharedMaterial (not .material) so we don't fork a per-instance copy.
        lr.sharedMaterial = GetBeamMaterial();
        return lr;
    }

    private static void ApplyGradient(LineRenderer lr, Color teamColor)
    {
        // Map the gradient stops from absolute world Y into the 0..1 line parameter
        // (Y goes from bottomY at the start to topY at the end).
        var range = Mathf.Max(0.0001f, topY - bottomY);
        var fadeStartT = Mathf.Clamp01((fadeStartY - bottomY) / range);
        var fadeEndT = Mathf.Clamp01((fadeEndY - bottomY) / range);

        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(teamColor, 0f),
                new GradientColorKey(teamColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0f, fadeStartT),
                new GradientAlphaKey(topAlpha, fadeEndT),
                new GradientAlphaKey(topAlpha, 1f)
            }
        );
        lr.colorGradient = grad;
    }

    public static void Tick(Puck puck)
    {
        if (!enabled || puck == null) return;
        if (puck.IsReplay != null && puck.IsReplay.Value) return;

        if (!beams.TryGetValue(puck, out var lr) || lr == null)
        {
            lr = CreateBeam(puck);
            beams[puck] = lr;
            // Force the gradient to be (re)applied to this fresh LineRenderer.
            appliedColor.Remove(puck);
        }

        if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);

        var p = puck.transform.position;
        lr.SetPosition(0, new Vector3(p.x, bottomY, p.z));
        lr.SetPosition(1, new Vector3(p.x, topY, p.z));

        var teamColor = TeamColor(PuckPossessionIndicator.GetLastTouchTeam(puck));
        if (!appliedColor.TryGetValue(puck, out var prev) || prev != teamColor)
        {
            ApplyGradient(lr, teamColor);
            appliedColor[puck] = teamColor;
        }
    }

    // Note the reference-null check rather than `puck == null`. Unity's overloaded
    // == reports a destroyed object as null, and this runs from Puck.OnDestroy —
    // so the Unity check would bail before destroying the beam and leave it
    // stranded on the ice. Only a genuine null reference is unusable as a key.
    public static void Cleanup(Puck puck)
    {
        if ((object)puck == null) return;
        if (beams.TryGetValue(puck, out var lr) && lr != null) Object.Destroy(lr.gameObject);
        beams.Remove(puck);
        appliedColor.Remove(puck);
    }

    // Deactivate every live beam without destroying it, so it can be revived
    // instantly when play resumes. Used to suppress the feature during warmup.
    private static void HideAll()
    {
        foreach (var lr in beams.Values)
            if (lr != null && lr.gameObject.activeSelf) lr.gameObject.SetActive(false);
    }

    // Scratch collections for the orphan sweep, reused so the per-frame
    // reconcile doesn't allocate.
    private static readonly HashSet<Puck> liveSet = new();
    private static readonly List<Puck> staleKeys = new();

    // Destroy any beam whose puck is no longer a live, non-replay puck. The
    // OnDestroy patch is the fast path, but this is the backstop: it also covers
    // a puck flipping to replay mid-life and any despawn path that doesn't route
    // through OnDestroy, so a beam can never outlive its puck.
    private static void SweepOrphans(List<Puck> livePucks)
    {
        if (beams.Count == 0) return;

        liveSet.Clear();
        if (livePucks != null)
            for (var i = 0; i < livePucks.Count; i++)
                if ((object)livePucks[i] != null) liveSet.Add(livePucks[i]);

        foreach (var key in beams.Keys)
        {
            if ((object)key == null) continue;
            if (key == null || !liveSet.Contains(key) || (key.IsReplay != null && key.IsReplay.Value))
                staleKeys.Add(key);
        }

        for (var i = 0; i < staleKeys.Count; i++) Cleanup(staleKeys[i]);
        staleKeys.Clear();
    }

    public static void TickAll()
    {
        if (PuckManager.Instance == null) return;
        var pucks = PuckManager.Instance.GetPucks(false);
        SweepOrphans(pucks);
        if (!enabled) { HideAll(); return; }
        if (PuckPossessionIndicator.IsWarmup()) { HideAll(); return; }
        if (pucks == null) return;
        for (var i = 0; i < pucks.Count; i++) Tick(pucks[i]);
    }
}

public static class PuckBeamPatches
{
    [HarmonyPatch(typeof(Puck), "OnDestroy")]
    private class PatchPuckOnDestroy
    {
        private static void Prefix(Puck __instance) => PuckBeam.Cleanup(__instance);
    }

    // Despawn fires before destruction and is the point at which the puck leaves
    // PuckManager's list, so this is what actually retires the beam in play.
    [HarmonyPatch(typeof(Puck), "OnNetworkDespawn")]
    private class PatchPuckOnNetworkDespawn
    {
        private static void Prefix(Puck __instance) => PuckBeam.Cleanup(__instance);
    }
}

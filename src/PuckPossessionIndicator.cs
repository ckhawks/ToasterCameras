// PuckPossessionIndicator.cs
//
// Ports the old BepInEx "puck possession circle" feature to b323. Each live puck
// gets a flat cylinder pinned to the ice directly beneath it. The cylinder is
// recolored on stick contact to the toucher's team color, and fades back to
// neutral gray after a fixed duration. Radius shrinks with puck height so it
// stays roughly puck-sized at table height and never blocks the puck visually
// when the puck is on the deck.
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
    public static float thickness = 0.005f;
    public static float possessionDurationSeconds = 5f;
    public static float smallestFactor = 0.2f;
    public static float maxHeightForSmallest = 18f;

    private static readonly Dictionary<Puck, GameObject> indicatorMap = new();
    private static readonly Dictionary<Puck, float> lastTouchTime = new();
    // Highest collision Time we've already reacted to per puck — used to detect
    // new entries in the synced NetworkObjectCollisionRecorder buffer.
    private static readonly Dictionary<Puck, float> lastSeenCollisionTime = new();
    // Last team to touch this puck. Read by other features (e.g. PuckBeam).
    private static readonly Dictionary<Puck, PlayerTeam> lastTouchTeamMap = new();

    public static PlayerTeam GetLastTouchTeam(Puck puck)
    {
        if (puck == null) return PlayerTeam.None;
        if (Time.time - (lastTouchTime.TryGetValue(puck, out var t) ? t : -possessionDurationSeconds) >
            possessionDurationSeconds) return PlayerTeam.None;
        return lastTouchTeamMap.TryGetValue(puck, out var team) ? team : PlayerTeam.None;
    }

    private static Material sourceMaterial;

    private static Material GetSourceMaterial()
    {
        if (sourceMaterial != null) return sourceMaterial;
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (renderer == null) continue;
            var n = renderer.gameObject.name;
            if (n == "Barrier Top Border" || n == "Barrier Bottom Border")
            {
                sourceMaterial = renderer.sharedMaterial;
                break;
            }
        }
        return sourceMaterial;
    }

    private static GameObject CreateIndicator(Puck puck)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = $"PuckPossessionIndicator_{puck.GetInstanceID()}";

        var rend = go.GetComponent<Renderer>();
        var src = GetSourceMaterial();
        if (src != null)
            rend.material = new Material(src);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        SetColor(rend.material, PlayerTeam.None);
        go.transform.localScale = new Vector3(maxRadius * 2f, thickness, maxRadius * 2f);
        return go;
    }

    private static void SetColor(Material mat, PlayerTeam team)
    {
        if (mat == null) return;
        Color c;
        if (team == PlayerTeam.Blue || team == PlayerTeam.Red)
        {
            c = TRLBridge.GetTeamColor(team);
            c.a = 0.5f;
        }
        else
        {
            c = Color.gray;
            c.a = 0.5f;
        }
        mat.color = c;
    }

    public static void Tick(Puck puck)
    {
        if (!enabled || puck == null) return;
        if (puck.IsReplay != null && puck.IsReplay.Value) return;

        if (!indicatorMap.TryGetValue(puck, out var indicator) || indicator == null)
        {
            indicator = CreateIndicator(puck);
            indicatorMap[puck] = indicator;
            lastTouchTime[puck] = -possessionDurationSeconds;
            lastSeenCollisionTime[puck] = -1f;
        }

        var pos = puck.transform.position;
        pos.y = 0.005f;
        indicator.transform.position = pos;
        indicator.transform.rotation = Quaternion.identity;

        var h = Mathf.Max(0f, puck.transform.position.y);
        var scale = Mathf.Clamp(1f - h * (1f / maxHeightForSmallest), smallestFactor, 1f);
        indicator.transform.localScale = new Vector3(maxRadius * 2f * scale, thickness, maxRadius * 2f * scale);

        PollCollisions(puck, indicator);

        var rend = indicator.GetComponent<Renderer>();
        // Re-apply color every frame so TRL color/toggle changes take effect live.
        var activeTeam = Time.time - lastTouchTime[puck] > possessionDurationSeconds
            ? PlayerTeam.None
            : (lastTouchTeamMap.TryGetValue(puck, out var t) ? t : PlayerTeam.None);
        SetColor(rend.material, activeTeam);
    }

    private static void PollCollisions(Puck puck, GameObject indicator)
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
            SetColor(indicator.GetComponent<Renderer>().material, latest.Key.Team);
            lastTouchTime[puck] = Time.time;
            lastTouchTeamMap[puck] = latest.Key.Team;
        }
    }

    public static void Cleanup(Puck puck)
    {
        if (puck == null) return;
        if (indicatorMap.TryGetValue(puck, out var go) && go != null) Object.Destroy(go);
        indicatorMap.Remove(puck);
        lastTouchTime.Remove(puck);
        lastSeenCollisionTime.Remove(puck);
        lastTouchTeamMap.Remove(puck);
    }

    public static void TickAll()
    {
        if (PuckManager.Instance == null) return;
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

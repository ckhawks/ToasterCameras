// TRLBridge.cs
//
// Soft (reflection-based) access to ToasterReskinLoader's public color API.
// We don't take a hard DLL reference so ToasterCameras still works without TRL
// installed; if TRL isn't loaded we fall back to vanilla-ish defaults.
//
// Colors are cached on first read and refreshed only when TRL fires its
// OnTeamColorsChanged event, so GetTeamColor is a cheap field read suitable
// for per-frame/per-puck calls.
//
// Usage:
//   var c = TRLBridge.GetTeamColor(PlayerTeam.Blue);

using System;
using System.Reflection;
using UnityEngine;

namespace ToasterCameras;

public static class TRLBridge
{
    private static bool _initialized;
    private static PropertyInfo _enabledProp;
    private static PropertyInfo _blueProp;
    private static PropertyInfo _redProp;

    // Vanilla-ish fallbacks (used when TRL isn't installed).
    private static readonly Color DefaultBlue = new Color(23f / 255f, 92f / 255f, 230f / 255f, 1f);
    private static readonly Color DefaultRed = new Color(229f / 255f, 23f / 255f, 23f / 255f, 1f);

    // Cached colors. Refreshed on init and whenever TRL fires OnTeamColorsChanged.
    private static Color _blueColor = DefaultBlue;
    private static Color _redColor = DefaultRed;

    private static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Type api = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                api = asm.GetType("ToasterReskinLoader.ToasterReskinLoaderAPI");
                if (api != null) break;
            }
            if (api == null) return;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
            _enabledProp = api.GetProperty("TeamColorsEnabled", flags);
            _blueProp = api.GetProperty("BlueTeamColor", flags);
            _redProp = api.GetProperty("RedTeamColor", flags);

            RefreshCache();

            // Subscribe to live updates so toggling/recoloring in TRL takes effect immediately.
            var evt = api.GetEvent("OnTeamColorsChanged", flags);
            if (evt != null)
            {
                var handler = Delegate.CreateDelegate(
                    evt.EventHandlerType,
                    typeof(TRLBridge).GetMethod(nameof(RefreshCache), BindingFlags.Static | BindingFlags.NonPublic));
                evt.AddEventHandler(null, handler);
            }
        }
        catch
        {
            // Reflection failed — fall back to defaults silently.
        }
    }

    private static void RefreshCache()
    {
        var enabled = false;
        if (_enabledProp != null)
        {
            try { enabled = (bool)_enabledProp.GetValue(null); }
            catch { enabled = false; }
        }

        if (enabled)
        {
            _blueColor = ReadProp(_blueProp, DefaultBlue);
            _redColor = ReadProp(_redProp, DefaultRed);
        }
        else
        {
            _blueColor = DefaultBlue;
            _redColor = DefaultRed;
        }
    }

    private static Color ReadProp(PropertyInfo prop, Color fallback)
    {
        if (prop == null) return fallback;
        try
        {
            var c = (Color)prop.GetValue(null);
            c.a = 1f;
            return c;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Returns the team color, honoring TRL's custom team color settings when
    /// available. Alpha is always 1 — callers apply their own transparency.
    /// Cheap: returns a cached field; refreshed via TRL's change event.
    /// </summary>
    public static Color GetTeamColor(PlayerTeam team)
    {
        if (!_initialized) Initialize();
        if (team == PlayerTeam.Blue) return _blueColor;
        if (team == PlayerTeam.Red) return _redColor;
        return Color.gray;
    }
}

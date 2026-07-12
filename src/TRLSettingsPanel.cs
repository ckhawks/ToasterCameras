// TRLSettingsPanel.cs
//
// Registers a "Cameras" settings page inside ToasterReskinLoader's Reskin Manager menu,
// if (and only if) TRL is installed. Like TRLBridge, this is a soft dependency: we reach
// TRL's public API over reflection so ToasterCameras still loads and works standalone.
//
// The page is built with TRL's own SettingsPanelUI helpers (also called over reflection)
// so our rows look native. ToasterCameras keeps owning its settings — the widgets read and
// write Plugin.modSettings and call Save(); TRL only hosts the UI.

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ToasterCameras;

public static class TRLSettingsPanel
{
    private const string PanelId = "ToasterCameras";

    // Reflected on first successful resolve; null until TRL's assembly is loaded.
    private static MethodInfo _register;
    private static MethodInfo _uiNote, _uiSeparator, _uiHeader, _uiToggle, _uiSlider;

    // Registration is retried each frame (via TickRegister) until it succeeds, because TRL may
    // load after we do — if we resolved once at OnEnable and TRL wasn't in AppDomain yet, we'd
    // never register. _done latches once we've either registered or given up waiting.
    private static bool _done;
    private static float _waitedSeconds;
    private const float GiveUpAfterSeconds = 30f;

    /// <summary>
    /// Attempts to add the Cameras page to TRL's menu, returning true once registered. Safe to
    /// call when TRL isn't (yet) present — it just returns false. Because TRL can load after us,
    /// call this once from OnEnable for the common case and then drive TickRegister each frame so
    /// a late-loading TRL still gets the panel.
    /// </summary>
    public static bool TryRegister()
    {
        if (_done) return _register != null;

        Resolve();
        if (_register == null) return false; // TRL not loaded yet — caller should retry later.

        try
        {
            // RegisterSettingsPanel(string id, string title, string group, Action<VisualElement> build, int order)
            Action<VisualElement> build = BuildPanel;
            _register.Invoke(null, new object[] { PanelId, "Cameras", "Cameras", build, 0 });
            Plugin.Log("Registered Cameras settings panel with ToasterReskinLoader.");
        }
        catch (Exception e)
        {
            Plugin.LogError($"Failed to register settings panel with TRL: {e.Message}");
        }

        _done = true;
        return true;
    }

    /// <summary>
    /// Per-frame retry driver. Keeps attempting registration until TRL shows up, then stops.
    /// Gives up (and logs once) after <see cref="GiveUpAfterSeconds"/> so we don't scan the
    /// AppDomain forever when TRL simply isn't installed. Cheap no-op once done.
    /// </summary>
    public static void TickRegister()
    {
        if (_done) return;
        if (TryRegister()) return;

        _waitedSeconds += Time.unscaledDeltaTime;
        if (_waitedSeconds >= GiveUpAfterSeconds)
        {
            _done = true;
            Plugin.Log("ToasterReskinLoader not detected; skipping settings panel registration.");
        }
    }

    private static void Resolve()
    {
        if (_register != null) return; // already resolved

        try
        {
            Type api = null, ui = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                api ??= asm.GetType("ToasterReskinLoader.ToasterReskinLoaderAPI");
                ui ??= asm.GetType("ToasterReskinLoader.api.SettingsPanelUI");
                if (api != null && ui != null) break;
            }
            if (api == null) return;

            _register = api.GetMethod("RegisterSettingsPanel", BindingFlags.Public | BindingFlags.Static);

            if (ui != null)
            {
                const BindingFlags f = BindingFlags.Public | BindingFlags.Static;
                _uiNote = ui.GetMethod("Note", f);
                _uiSeparator = ui.GetMethod("Separator", f);
                _uiHeader = ui.GetMethod("Header", f);
                _uiToggle = ui.GetMethod("Toggle", f);
                _uiSlider = ui.GetMethod("Slider", f);
            }
        }
        catch
        {
            // Reflection failed — treat TRL as absent.
        }
    }

    // ── Panel content ───────────────────────────────────────────────────
    // Rebuilt from current settings each time the page is opened.

    private static void BuildPanel(VisualElement root)
    {
        var s = Plugin.modSettings;
        if (s == null)
        {
            Note(root, "Camera settings aren't loaded yet.");
            return;
        }

        Note(root, "Spectator camera tweaks. Changes apply and save immediately.");

        Toggle(root, "Disable quick chats while spectating", s.disableQuickChatsInSpectator, v =>
        {
            s.disableQuickChatsInSpectator = v;
            s.Save();
        });

        Separator(root);
        Header(root, "Features");
        Note(root, "Saved across restarts. Equivalent to /beam, /dfov and /zoom.");

        Toggle(root, "Puck beam", PuckBeam.enabled, v =>
        {
            PuckBeam.enabled = v;
            if (!v && PuckManager.Instance != null)
                foreach (var p in PuckManager.Instance.GetPucks(false))
                    PuckBeam.Cleanup(p);
            s.puckBeamOn = v;
            s.Save();
        });
        Toggle(root, "Dynamic FOV", Plugin.client_dynamicFovEnabled, v =>
        {
            Plugin.client_dynamicFovEnabled = v;
            s.dynamicFovEnabled = v;
            s.Save();
        });
        Toggle(root, "Scroll-wheel zoom", Plugin.client_scrollZoomEnabled, v =>
        {
            Plugin.client_scrollZoomEnabled = v;
            if (v) Plugin.scrollZoomNeedsInit = true;
            s.scrollZoomEnabled = v;
            s.Save();
        });

        Separator(root);
        Header(root, "Possession disc");
        Toggle(root, "Show possession disc", PuckPossessionIndicator.enabled, v =>
        {
            PuckPossessionIndicator.enabled = v;
            s.possessionDiscOn = v;
            s.Save();
        });
        Slider(root, "Opacity", 0f, 1f, s.possessionCircle?.opacity ?? 0.75f,
            v =>
            {
                s.possessionCircle ??= new PossessionCircleSettings();
                s.possessionCircle.opacity = v;
                PuckPossessionIndicator.opacity = Mathf.Clamp01(v);
            },
            s.Save);

        Separator(root);
        Header(root, "Cinematic smoothing");
        Note(root, "Lower = smoother but laggier camera while cinematic smoothing is toggled on.");
        Slider(root, "Rotation smoothing", 0.01f, 1f, s.cinematicSettings?.rotationSmoothingFactor ?? 0.1f,
            v =>
            {
                s.cinematicSettings ??= new CinematicSettings();
                s.cinematicSettings.rotationSmoothingFactor = v;
            },
            s.Save);
        Slider(root, "Position smoothing", 0.01f, 1f, s.cinematicSettings?.positionSmoothingFactor ?? 0.1f,
            v =>
            {
                s.cinematicSettings ??= new CinematicSettings();
                s.cinematicSettings.positionSmoothingFactor = v;
            },
            s.Save);

        // ── Camera mode keybinds ────────────────────────────────────────
        Separator(root);
        Header(root, "Camera mode keybinds");
        Note(root, "Click a binding, then press a key or button to set it. Esc cancels; ✕ unbinds.");

        var m = s.cameraModes ??= new CameraModeKeybinds();
        RebindRow(root, "Become puck", () => m.becomePuck, v => m.becomePuck = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Watch puck", () => m.watchPuck, v => m.watchPuck = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Watch puck (grid)", () => m.watchPuckGrid, v => m.watchPuckGrid = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Watch puck (above)", () => m.watchPuckAbove, v => m.watchPuckAbove = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Watch puck (smart)", () => m.watchPuckSmart, v => m.watchPuckSmart = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Watch puck (smart 2)", () => m.watchPuckSmart2, v => m.watchPuckSmart2 = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Watch off", () => m.watchOff, v => m.watchOff = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Cinematic smoothing toggle", () => m.cinematicSmoothing, v => m.cinematicSmoothing = v, CameraKeybinds.InitializeCameraModeKeybinds);
        RebindRow(root, "Slow down (hold)", () => m.slowDown, v => m.slowDown = v, CameraKeybinds.InitializeCameraModeKeybinds);

        // ── Player watch keybinds ───────────────────────────────────────
        Separator(root);
        Header(root, "Player watch keybinds");
        Note(root, "Watch the Nth player on a team (sorted C, LW, RW, LD, RD, G).");

        var blue = (s.watchPlayer ??= new TeamKeybinds()).blue ??= new PlayerKeybinds();
        var red = s.watchPlayer.red ??= new PlayerKeybinds();

        Subheader(root, "Blue team");
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            RebindRow(root, $"Player {idx + 1}",
                () => GetPlayerBind(blue, idx), v => SetPlayerBind(blue, idx, v),
                CameraKeybinds.InitializePlayerWatchKeybinds);
        }

        Subheader(root, "Red team");
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            RebindRow(root, $"Player {idx + 1}",
                () => GetPlayerBind(red, idx), v => SetPlayerBind(red, idx, v),
                CameraKeybinds.InitializePlayerWatchKeybinds);
        }
    }

    private static string GetPlayerBind(PlayerKeybinds pk, int i) => i switch
    {
        0 => pk.player1, 1 => pk.player2, 2 => pk.player3,
        3 => pk.player4, 4 => pk.player5, _ => pk.player6,
    };

    private static void SetPlayerBind(PlayerKeybinds pk, int i, string v)
    {
        switch (i)
        {
            case 0: pk.player1 = v; break;
            case 1: pk.player2 = v; break;
            case 2: pk.player3 = v; break;
            case 3: pk.player4 = v; break;
            case 4: pk.player5 = v; break;
            default: pk.player6 = v; break;
        }
    }

    // ── Keybind rows (raw UIElements, styled to match TRL) ──────────────
    // Built raw rather than via SettingsPanelUI because rebind capture is InputSystem-specific
    // and belongs here, not in TRL's generic helper set.

    // At most one interactive rebind runs at a time.
    private static bool _rebinding;

    private static void RebindRow(VisualElement root, string label, Func<string> get, Action<string> set, Action afterSet)
    {
        var row = MakeRow();
        row.Add(MakeLabel(label));

        var right = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

        var bindBtn = new Button { text = Humanize(get()) };
        StyleDarkButton(bindBtn);
        bindBtn.style.minWidth = 170;
        bindBtn.style.unityTextAlign = TextAnchor.MiddleCenter;

        var clearBtn = new Button { text = "✕" };
        StyleDarkButton(clearBtn);
        clearBtn.style.marginLeft = 6;

        bindBtn.RegisterCallback<ClickEvent>(_ =>
        {
            if (_rebinding) return;
            _rebinding = true;
            bindBtn.text = "Press a key…  (Esc cancels)";
            StartInteractiveRebind(
                path => { set(path); Persist(afterSet); bindBtn.text = Humanize(get()); _rebinding = false; },
                () => { bindBtn.text = Humanize(get()); _rebinding = false; });
        });

        clearBtn.RegisterCallback<ClickEvent>(_ =>
        {
            if (_rebinding) return;
            set("");
            Persist(afterSet);
            bindBtn.text = Humanize(get());
        });

        right.Add(bindBtn);
        right.Add(clearBtn);
        row.Add(right);
        root.Add(row);
    }

    private static void Persist(Action afterSet)
    {
        Plugin.modSettings?.Save();
        try { afterSet?.Invoke(); }
        catch (Exception e) { Plugin.LogError($"Keybind re-init failed: {e.Message}"); }
    }

    // Raw control path (e.g. "<Keyboard>/f") -> friendly label ("F"); empty -> "Unbound".
    private static string Humanize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Unbound";
        try { return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice); }
        catch { return path; }
    }

    // Listen for the next actuated button/key and report its control path. A throwaway action is
    // used purely as the rebind target so we don't disturb the live keybind actions until we
    // persist + re-init through afterSet.
    private static void StartInteractiveRebind(Action<string> onComplete, Action onCancel)
    {
        try
        {
            var action = new InputAction(type: InputActionType.Button);
            action.AddBinding("<Keyboard>/space"); // placeholder binding to rebind at index 0
            action.Disable();
            action.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(op =>
                {
                    op.Dispose();
                    action.Dispose();
                    onCancel();
                })
                .OnComplete(op =>
                {
                    string path = action.bindings[0].effectivePath;
                    op.Dispose();
                    action.Dispose();
                    onComplete(path);
                })
                .Start();
        }
        catch (Exception e)
        {
            Plugin.LogError($"Failed to start interactive rebind: {e.Message}");
            onCancel();
        }
    }

    private static VisualElement MakeRow() => new VisualElement
    {
        style =
        {
            flexDirection = FlexDirection.Row, alignItems = Align.Center,
            justifyContent = Justify.SpaceBetween, marginTop = 4, marginBottom = 4,
        },
    };

    private static Label MakeLabel(string text) => new Label(text)
    {
        style = { color = Color.white, fontSize = 16, whiteSpace = WhiteSpace.Normal },
    };

    private static void Subheader(VisualElement root, string text)
    {
        var l = new Label(text)
        {
            style =
            {
                color = new Color(0.85f, 0.85f, 0.85f), fontSize = 16,
                unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 2,
            },
        };
        root.Add(l);
    }

    private static void StyleDarkButton(Button b)
    {
        b.style.backgroundColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
        b.style.color = Color.white;
        b.style.fontSize = 14;
        b.style.paddingTop = 6; b.style.paddingBottom = 6; b.style.paddingLeft = 12; b.style.paddingRight = 12;
        b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
        // Don't restyle on hover while a rebind is in flight, so the "Press a key…" button doesn't flicker.
        b.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (_rebinding) return;
            b.style.backgroundColor = Color.white;
            b.style.color = Color.black;
        });
        b.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            b.style.backgroundColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            b.style.color = Color.white;
        });
    }

    // ── SettingsPanelUI wrappers (reflection) ───────────────────────────
    // These run inside BuildPanel, which TRL only calls when it's present, so the methods
    // are resolved. Each falls back to a plain VisualElement if reflection somehow failed.

    private static void Note(VisualElement root, string text)
    {
        if (_uiNote != null) { _uiNote.Invoke(null, new object[] { root, text }); return; }
        var l = new Label(text) { style = { color = new Color(0.7f, 0.7f, 0.7f), whiteSpace = WhiteSpace.Normal } };
        root.Add(l);
    }

    private static void Separator(VisualElement root)
    {
        if (_uiSeparator != null) { _uiSeparator.Invoke(null, new object[] { root }); return; }
        var sep = new VisualElement { style = { height = 1, marginTop = 12, marginBottom = 12,
            backgroundColor = new Color(0.4f, 0.4f, 0.4f) } };
        root.Add(sep);
    }

    private static void Header(VisualElement root, string text)
    {
        if (_uiHeader != null) { _uiHeader.Invoke(null, new object[] { root, text }); return; }
        var l = new Label(text) { style = { color = Color.white, unityFontStyleAndWeight = FontStyle.Bold } };
        root.Add(l);
    }

    private static void Toggle(VisualElement root, string label, bool value, Action<bool> onChange)
    {
        if (_uiToggle != null) { _uiToggle.Invoke(null, new object[] { root, label, value, onChange }); return; }
        var t = new Toggle(label) { value = value };
        t.RegisterValueChangedCallback(e => onChange(e.newValue));
        root.Add(t);
    }

    // onChange applies the value live (every drag step); onSave persists once the slider settles
    // (debounced by TRL) so dragging doesn't rewrite the config file on every tick.
    private static void Slider(VisualElement root, string label, float min, float max, float value,
        Action<float> onChange, Action onSave)
    {
        if (_uiSlider != null)
        {
            // SettingsPanelUI.Slider(root, label, min, max, value, onChange, onCommit, debounceMs)
            _uiSlider.Invoke(null, new object[] { root, label, min, max, value, onChange, onSave, 400L });
            return;
        }

        // Fallback when TRL's helper is unavailable: live on change, save on pointer-up.
        var sl = new Slider(label, min, max) { value = value, showInputField = true };
        sl.RegisterValueChangedCallback(e => onChange(e.newValue));
        if (onSave != null) sl.RegisterCallback<PointerUpEvent>(_ => onSave());
        root.Add(sl);
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Every menu screen, built at runtime. One canvas, one screen visible at a
/// time, a back-stack so Esc/Back always unwinds correctly.
///
/// Screens: Main (title), NewGame (difficulty), Pause, Options (4 tabs),
/// Controls (rebinding), Credits, Changelog (patch notes from
/// Resources/Changelog.txt), Confirm. The layouts these produce are
/// documented in docs/MENU_WIREFRAMES.md — keep the two in sync, that file is
/// what the artist works from.
/// </summary>
public class MenuScreens : MonoBehaviour
{
    public enum Screen { None, Main, NewGame, Pause, Options, Controls, Credits, Confirm, GameOver, NameEntry, Changelog, Information }

    private static MenuScreens instance;
    public static MenuScreens Instance => instance;

    /// <summary>True whenever any menu screen is showing.</summary>
    public static bool AnyOpen => instance != null && instance.current != Screen.None;

    /// <summary>The screen showing right now (None when the menu is closed).</summary>
    public Screen Current => current;

    // What to run once the name popup is confirmed (the opening sequence's next hint).
    private Action nameEntryCallback;

    private Canvas canvas;
    private RectTransform backdrop;
    private RectTransform panel;
    /// <summary>The column the current screen filled — used to size the panel to its content.</summary>
    private VerticalLayoutGroup activeColumn;
    private Screen current = Screen.None;
    private readonly List<Screen> backStack = new List<Screen>();

    /// <summary>True when Back() has somewhere to go rather than closing the menu outright.</summary>
    public bool CanGoBack => backStack.Count > 0;

    private string confirmMessage;
    private Action confirmAction;
    private int optionsTab;
    private bool gameOverVictory;

    // Rebind capture state. Null means nothing is armed.
    private KeyBindings.Action? captureAction;
    private bool captureSecondary;

    /// <summary>
    /// True while the Controls screen is waiting for a key. PauseController
    /// checks this so its Escape handler doesn't eat the cancel gesture — it
    /// runs at execution order -50, i.e. before this component, and would
    /// otherwise back out of the whole screen instead.
    /// </summary>
    public bool IsCapturingKey => captureAction.HasValue;

    // The active scroll region and where it was scrolled to. A rebuild destroys
    // the panel, so without this every rebind would snap a long list back to
    // the top — the row the player just clicked would leave the screen.
    private ScrollRect activeScroll;
    private readonly Dictionary<Screen, float> scrollMemory = new Dictionary<Screen, float>();

    public static MenuScreens Ensure()
    {
        if (instance != null) return instance;
        GameObject go = new GameObject("~Menus");
        instance = go.AddComponent<MenuScreens>();
        return instance;
    }

    private void Awake()
    {
        instance = this;
        GameSettings.Load();
        canvas = MenuBuilder.CreateCanvas("MenuCanvas", 900);
        canvas.transform.SetParent(transform, false);
        backdrop = MenuBuilder.FullScreen(canvas.transform, "Backdrop", MenuStyle.Backdrop);
        backdrop.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (!captureAction.HasValue) return;

        if (KeyBindings.TryCaptureKey(out KeyCode key))
        {
            // Escape cancels rather than binds — it is the one key the player
            // can never own (see the KeyBindings class summary).
            if (key != KeyCode.Escape && !KeyBindings.IsReserved(key))
            {
                KeyBindings.Action a = captureAction.Value;
                KeyBindings.Bind(a, captureSecondary, key);
                KeyBindings.Save();
                if (a == KeyBindings.Action.StanceDefensive || a == KeyBindings.Action.StanceOffensive || a == KeyBindings.Action.StanceFollow)
                    DevQuests.Signal("rebind:militia");
            }
            CancelCapture();
        }
    }

    /// <summary>Drops the armed rebind without changing anything.</summary>
    public void CancelCapture()
    {
        if (!captureAction.HasValue) return;
        captureAction = null;
        Rebuild();
    }

    // ---- navigation -------------------------------------------------------

    public void Show(Screen screen, bool pushHistory = true)
    {
        if (pushHistory && current != Screen.None && current != screen) backStack.Add(current);
        captureAction = null;      // never carry an armed rebind onto another screen
        current = screen;
        Rebuild();
    }

    /// <summary>
    /// Shows the victory or defeat screen. Clears the back-stack: the run is
    /// over, so there is nothing behind this screen to return to.
    /// </summary>
    public void ShowGameOver(bool victory)
    {
        gameOverVictory = victory;
        backStack.Clear();
        Show(Screen.GameOver, pushHistory: false);
    }

    /// <summary>
    /// The "what's your name?" popup at the start of a run. Modal: no Back, no
    /// Esc — the only way out is Begin, which freezes the name for the run
    /// (<see cref="PlayerProfile.BeginRun"/>) and then runs <paramref name="onConfirmed"/>.
    /// </summary>
    public void ShowNameEntry(Action onConfirmed)
    {
        nameEntryCallback = onConfirmed;
        backStack.Clear();
        Show(Screen.NameEntry, pushHistory: false);
    }

    /// <summary>Back one level; closes the menu entirely when the stack is empty.</summary>
    public void Back()
    {
        // The game-over screen has no back. Dismissing it would leave the player
        // looking at a frozen world with no UI and no way to reach one — the
        // game is paused at timeScale 0 and PauseController refuses to unpause
        // while isGameOver is set.
        if (current == Screen.GameOver) return;

        // The name popup has no back either: the run cannot start unnamed, and
        // there is nothing behind it but the frozen opening.
        if (current == Screen.NameEntry) return;

        // Leaving Options or Controls is the natural commit point — a player who
        // backs out expects their changes kept, not discarded.
        if (current == Screen.Options || current == Screen.Controls || current == Screen.NewGame)
        {
            GameSettings.Save();
        }

        if (backStack.Count > 0)
        {
            Screen prev = backStack[backStack.Count - 1];
            backStack.RemoveAt(backStack.Count - 1);
            Show(prev, pushHistory: false);
            return;
        }
        Close();
    }

    public void Close()
    {
        backStack.Clear();
        captureAction = null;
        current = Screen.None;
        Rebuild();
        PauseController.SetPaused(false);
    }

    private void Rebuild()
    {
        // Remember where a scrolling screen was before its panel is destroyed.
        if (activeScroll != null && current != Screen.None)
            scrollMemory[current] = activeScroll.verticalNormalizedPosition;
        activeScroll = null;

        if (panel != null) Destroy(panel.gameObject);
        panel = null;
        activeColumn = null;

        bool open = current != Screen.None;
        backdrop.gameObject.SetActive(open);
        if (!open) return;

        switch (current)
        {
            case Screen.Main: BuildMain(); break;
            case Screen.NewGame: BuildNewGame(); break;
            case Screen.Pause: BuildPause(); break;
            case Screen.Options: BuildOptions(); break;
            case Screen.Controls: BuildControls(); break;
            case Screen.Credits: BuildCredits(); break;
            case Screen.Confirm: BuildConfirm(); break;
            case Screen.GameOver: BuildGameOver(); break;
            case Screen.NameEntry: BuildNameEntry(); break;
            case Screen.Changelog: BuildChangelog(); break;
            case Screen.Information: BuildInformation(); break;
        }

        // The height passed to Panel() is only a starting value — the panel is
        // sized to whatever the screen actually put in it, so adding a row can
        // never push content out through the bottom edge again.
        if (panel != null && activeColumn != null) MenuBuilder.FitPanelHeight(panel, activeColumn);

        RestoreScroll();
    }

    /// <summary>
    /// Puts a rebuilt scroll region back where the player left it.
    ///
    /// Deferred by a layout rebuild because ScrollRect clamps the normalized
    /// position against the content height, and the ContentSizeFitter has not
    /// computed that height yet on the frame the rows are created — setting it
    /// any earlier silently resolves to 1 (the top).
    /// </summary>
    private void RestoreScroll()
    {
        if (activeScroll == null) return;
        if (!scrollMemory.TryGetValue(current, out float pos)) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(activeScroll.content);
        activeScroll.verticalNormalizedPosition = pos;
    }

    // ---- screens ----------------------------------------------------------

    private void BuildMain()
    {
        panel = MenuBuilder.Panel(canvas.transform, "MainMenu", MenuStyle.MenuWidth, 620f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, MenuStyle.ButtonSpacing);

        // One two-line label rather than two stacked ones: the title reads as a
        // single lockup, and TMP's own line spacing keeps the words together
        // instead of the column's spacing pushing them apart.
        TextMeshProUGUI title = MenuBuilder.Label(col.transform, "CASTAWAY\nCOLONY",
            MenuStyle.TitleSize, MenuStyle.TextAccent);
        title.lineSpacing = -12f;
        title.characterSpacing = 6f;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 126f;

        MenuBuilder.Label(col.transform, "thirty days to rescue", MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        MenuBuilder.Spacer(col.transform, 22f);

        MenuBuilder.MenuButton(col.transform, "NEW GAME", () => Show(Screen.NewGame));
        // No save system yet — shown disabled so the artist knows the slot exists.
        MenuBuilder.MenuButton(col.transform, "CONTINUE", null, enabled: false);
        MenuBuilder.MenuButton(col.transform, "OPTIONS", () => Show(Screen.Options));
        MenuBuilder.MenuButton(col.transform, "INFORMATION", () => Show(Screen.Information));
        MenuBuilder.MenuButton(col.transform, "CHANGELOG", () => Show(Screen.Changelog));
        MenuBuilder.MenuButton(col.transform, "CREDITS", () => Show(Screen.Credits));
        MenuBuilder.MenuButton(col.transform, "QUIT", () =>
            AskConfirm("Quit to desktop?", MenuFlow.QuitGame), textColor: MenuStyle.TextDanger);

        MenuBuilder.Spacer(col.transform, 8f);
        // Application.version is ProjectSettings.bundleVersion — bumped per build
        // (0.2.0-alpha.1, alpha.2, ...) and the same string every playtest and
        // feedback report carries, so a report can be matched to its build. The
        // newest changelog date doubles as the build date, never edited by hand.
        string latest = Changelog.LatestDate;
        string version = "v" + Application.version + (latest != null ? " · updated " + latest : "");
        MenuBuilder.Label(col.transform, version, MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
    }

    /// <summary>
    /// Difficulty selection. This screen exists because difficulty is locked for
    /// the run — it has to be asked before the scene loads, not offered in
    /// Options where a player could soften night four mid-game.
    /// </summary>
    private void BuildNewGame()
    {
        panel = MenuBuilder.Panel(canvas.transform, "NewGame", MenuStyle.OptionsWidth, 620f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 6f);

        MenuBuilder.Label(col.transform, "NEW GAME", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 8f);

        Difficulty.Preset p = Difficulty.Get(Difficulty.Selected);

        MenuBuilder.StepperRow(col.transform, "Difficulty", Difficulty.LevelNames,
            (int)Difficulty.Selected,
            i => { Difficulty.Selected = (Difficulty.Level)i; Difficulty.Save(); Rebuild(); });

        TextMeshProUGUI blurb = MenuBuilder.Label(col.transform, p.blurb, MenuStyle.SmallSize,
            MenuStyle.TextMuted, TextAlignmentOptions.TopLeft);
        blurb.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

        MenuBuilder.SectionHeader(col.transform, "Rules");

        if (Difficulty.Selected == Difficulty.Level.Custom)
        {
            // Custom edits the preset in place, so every row writes straight
            // through and the summary below it recomputes on the next rebuild.
            MenuBuilder.RangeSliderRow(col.transform, "Raid size", p.enemyCount, 0.25f, 2.5f,
                Multiplier, v => { p.enemyCount = v; Difficulty.Save(); },
                "How many raiders land when a raid comes, against the standard raid.");

            MenuBuilder.RangeSliderRow(col.transform, "Raid frequency", p.raidFrequency, 0.25f, 2f,
                Multiplier, v => { p.raidFrequency = v; Difficulty.Save(); },
                "How likely each night is to bring a raid. Raids are always announced at dawn.");

            MenuBuilder.RangeSliderRow(col.transform, "Enemy health", p.enemyHealth, 0.25f, 2.5f,
                Multiplier, v => { p.enemyHealth = v; Difficulty.Save(); },
                "How much punishment each raider takes before it goes down.");

            MenuBuilder.RangeSliderRow(col.transform, "Enemy damage", p.enemyDamage, 0.25f, 2.5f,
                Multiplier, v => { p.enemyDamage = v; Difficulty.Save(); },
                "How hard each raider hits your warriors and buildings.");

            MenuBuilder.RangeSliderRow(col.transform, "Night length", p.nightLength, 0.5f, 2f,
                Multiplier, v => { p.nightLength = v; Difficulty.Save(); },
                "Longer nights mean more time under attack. Days are unaffected.");

            MenuBuilder.RangeSliderRow(col.transform, "Starting resources", p.startingResources, 0.25f, 3f,
                Multiplier, v => { p.startingResources = v; Difficulty.Save(); },
                "What washes ashore with you, against the standard 100 wood / 50 food.");

            MenuBuilder.RangeSliderRow(col.transform, "Food consumption", p.foodConsumption, 0.25f, 2f,
                Multiplier, v => { p.foodConsumption = v; Difficulty.Save(); },
                "How much each colonist eats per day, against the standard one food.");

            MenuBuilder.RangeSliderRow(col.transform, "Days to rescue", p.daysToSurvive, 5f, 60f,
                v => Mathf.RoundToInt(v).ToString(),
                v => { p.daysToSurvive = Mathf.RoundToInt(v); Difficulty.Save(); },
                "The rescue ship arrives at dawn after this many days.");
        }
        else
        {
            // A read-only summary of what the preset actually does. Showing the
            // numbers is the difference between picking a label and making an
            // informed choice.
            MenuBuilder.ValueRow(col.transform, "Raid size", Multiplier(p.enemyCount));
            MenuBuilder.ValueRow(col.transform, "Raid frequency", Multiplier(p.raidFrequency));
            MenuBuilder.ValueRow(col.transform, "Enemy health", Multiplier(p.enemyHealth));
            MenuBuilder.ValueRow(col.transform, "Enemy damage", Multiplier(p.enemyDamage));
            MenuBuilder.ValueRow(col.transform, "Night length", Multiplier(p.nightLength));
            MenuBuilder.ValueRow(col.transform, "Starting resources", Multiplier(p.startingResources));
            MenuBuilder.ValueRow(col.transform, "Food consumption", Multiplier(p.foodConsumption));
            MenuBuilder.ValueRow(col.transform, "Days to rescue", p.daysToSurvive.ToString());
        }

        // The world: island size, terrain style, optional seed. Same locking
        // rule as difficulty — TerrainGrid reads the snapshot in its Awake.
        MenuBuilder.SectionHeader(col.transform, "World");

        MenuBuilder.StepperRow(col.transform, "Island size", IslandOptions.SizeNames, (int)IslandOptions.SelectedSize,
            i => { IslandOptions.SelectedSize = (IslandOptions.Size)i; IslandOptions.Save(); Rebuild(); },
            IslandOptions.SizeBlurbs[(int)IslandOptions.SelectedSize]);

        MenuBuilder.StepperRow(col.transform, "Terrain", IslandSettings.StyleNames, (int)IslandOptions.SelectedStyle,
            i => { IslandOptions.SelectedStyle = (IslandSettings.Style)i; IslandOptions.Save(); Rebuild(); },
            IslandSettings.StyleBlurbs[(int)IslandOptions.SelectedStyle]);

        MenuBuilder.InputRow(col.transform, "Seed", IslandOptions.SelectedSeedText, "random",
            v => { IslandOptions.SelectedSeedText = v; IslandOptions.Save(); },
            "Leave empty for a new island every game. A number or a word replays the same one.");

        MenuBuilder.Spacer(col.transform, 10f);
        MenuBuilder.Label(col.transform, "Difficulty and world are locked once the run begins.",
            MenuStyle.SmallSize, MenuStyle.TextMuted).gameObject
            .AddComponent<LayoutElement>().preferredHeight = 22f;

        MenuBuilder.MenuButton(col.transform, "BEGIN", () => { Difficulty.Save(); IslandOptions.Save(); MenuFlow.NewGame(); },
            textColor: MenuStyle.TextAccent);
        MenuBuilder.MenuButton(col.transform, "BACK", () => Back());
    }

    /// <summary>"1.3x" — the readout every difficulty multiplier uses.</summary>
    private static string Multiplier(float v) => v.ToString("0.##") + "x";

    private void BuildPause()
    {
        panel = MenuBuilder.Panel(canvas.transform, "PauseMenu", MenuStyle.MenuWidth, 520f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, MenuStyle.ButtonSpacing);

        MenuBuilder.Label(col.transform, "PAUSED", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        MenuBuilder.Label(col.transform, StatusLine(), MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        // Read-only: the run's rules are fixed, and saying so here is what stops
        // a player hunting for the difficulty setting in Options.
        string island = TerrainGrid.Instance != null
            ? IslandOptions.ActiveName + " island · seed " + TerrainGrid.Instance.seed
            : IslandOptions.ActiveName + " island";
        MenuBuilder.Label(col.transform, Difficulty.ActiveName.ToUpperInvariant() + " · " + island + " · locked for this run",
            MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 10f);

        MenuBuilder.MenuButton(col.transform, "RESUME", Close);
        MenuBuilder.MenuButton(col.transform, "OPTIONS", () => Show(Screen.Options));
        MenuBuilder.MenuButton(col.transform, "CONTROLS", () => Show(Screen.Controls));
        MenuBuilder.MenuButton(col.transform, "INFORMATION", () => Show(Screen.Information));
        MenuBuilder.MenuButton(col.transform, "CHANGELOG", () => Show(Screen.Changelog));
        MenuBuilder.MenuButton(col.transform, "RESTART", () =>
            AskConfirm("Restart? Current progress is lost.", MenuFlow.Restart));
        MenuBuilder.MenuButton(col.transform, "MAIN MENU", () =>
            AskConfirm("Return to menu? Current progress is lost.", MenuFlow.ToMainMenu));
        MenuBuilder.MenuButton(col.transform, "QUIT", () =>
            AskConfirm("Quit to desktop?", MenuFlow.QuitGame), textColor: MenuStyle.TextDanger);
    }

    /// <summary>Reminds the player where they left off — day count and population.</summary>
    private string StatusLine()
    {
        DayNightCycle dn = FindAnyObjectByType<DayNightCycle>();
        BaseBuilding fire = Factions.Player.Campfire;
        if (dn == null) return "";

        string phase = dn.IsNightTime() ? "Night" : "Day";
        int workers = fire != null ? fire.GetTotalWorkers() : 0;
        int warriors = fire != null ? fire.GetWarriorCount() : 0;
        return $"{phase} {dn.GetCurrentDay()}   ·   {workers} workers   ·   {warriors} warriors";
    }

    private void BuildOptions()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Options", MenuStyle.OptionsWidth, 700f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 8f);

        MenuBuilder.Label(col.transform, "OPTIONS", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        BuildTabs(col.transform, new[] { "AUDIO", "VIDEO", "CAMERA", "INTERFACE" }, optionsTab, i =>
        {
            optionsTab = i;
            Rebuild();
        });

        MenuBuilder.Divider(col.transform);

        // Tabs scroll. Without this the panel grows with the longest tab and a
        // 1080p window at UI Scale 1.25 pushes the buttons off the bottom.
        VerticalLayoutGroup body = MenuBuilder.ScrollColumn(col.transform, 4f, 380f);
        activeScroll = body.GetComponentInParent<ScrollRect>();
        Transform t = body.transform;

        switch (optionsTab)
        {
            case 0: BuildAudioTab(t); break;
            case 1: BuildVideoTab(t); break;
            case 2: BuildCameraTab(t); break;
            default: BuildInterfaceTab(t); break;
        }

        MenuBuilder.Spacer(col.transform, 10f);
        MenuBuilder.MenuButton(col.transform, "CONTROLS & KEYBINDINGS", () => Show(Screen.Controls));
        MenuBuilder.MenuButton(col.transform, "RESET TO DEFAULTS", () =>
            AskConfirm("Reset all settings and keybindings?", () =>
            {
                GameSettings.ResetToDefaults();
                Back();
            }), textColor: MenuStyle.TextDanger);
        MenuBuilder.MenuButton(col.transform, "BACK", () => Back());
    }

    private void BuildAudioTab(Transform t)
    {
        MenuBuilder.SliderRow(t, "Master volume", GameSettings.MasterVolume,
            v => { GameSettings.MasterVolume = v; GameSettings.Apply(); },
            "Scales everything below it.");

        MenuBuilder.SliderRow(t, "Music", GameSettings.MusicVolume,
            v => { GameSettings.MusicVolume = v; GameSettings.Apply(); });

        MenuBuilder.SliderRow(t, "Sound effects", GameSettings.SfxVolume,
            v => { GameSettings.SfxVolume = v; GameSettings.Apply(); },
            "Combat, building, gathering.");

        MenuBuilder.SliderRow(t, "Ambience", GameSettings.AmbientVolume,
            v => { GameSettings.AmbientVolume = v; GameSettings.Apply(); },
            "Waves, wind, birds, and the campfire.");

        MenuBuilder.ToggleRow(t, "Mute when unfocused", GameSettings.MuteWhenUnfocused,
            v => { GameSettings.MuteWhenUnfocused = v; GameSettings.Apply(); },
            "Silence the game while another window has focus.");
    }

    private void BuildVideoTab(Transform t)
    {
        MenuBuilder.StepperRow(t, "Display mode",
            new[] { "Fullscreen", "Borderless", "Windowed" }, (int)GameSettings.DisplayMode,
            i => { GameSettings.DisplayMode = (GameSettings.Display)i; GameSettings.Apply(); });

        MenuBuilder.StepperRow(t, "Resolution", GameSettings.ResolutionOptions,
            GameSettings.CurrentResolutionIndex(),
            i => { GameSettings.ResolutionIndex = i; GameSettings.Apply(); },
            Application.isEditor ? "Applies in a built game; the editor ignores it." : null);

        MenuBuilder.ToggleRow(t, "V-Sync", GameSettings.VSync,
            v => { GameSettings.VSync = v; GameSettings.Apply(); Rebuild(); },
            "Matches the display's refresh rate. Removes tearing, adds a little input lag.");

        string[] caps = new string[GameSettings.FrameCapChoices.Length];
        int capIndex = 0;
        for (int i = 0; i < caps.Length; i++)
        {
            int c = GameSettings.FrameCapChoices[i];
            caps[i] = c == 0 ? "Unlimited" : c.ToString();
            if (c == GameSettings.FrameCap) capIndex = i;
        }

        MenuBuilder.StepperRow(t, "Frame rate cap", caps, capIndex,
            i => { GameSettings.FrameCap = GameSettings.FrameCapChoices[i]; GameSettings.Apply(); },
            GameSettings.VSync
                ? "Ignored while V-Sync is on."
                : "Capping below your display's refresh rate saves power and heat.");

        BuildGraphicsRows(t);
    }

    /// <summary>
    /// The Graphics preset and the rows it sets (2026-09-08). A stepper move to
    /// Low..Ultra rewrites every row; a row change re-detects the preset (Custom
    /// unless the rows match one) and moves the stepper silently through the
    /// setter StepperRow returns — a Rebuild mid slider-drag would destroy the
    /// slider under the pointer.
    /// </summary>
    private void BuildGraphicsRows(Transform t)
    {
        MenuBuilder.SectionHeader(t, "GRAPHICS");

        string[] presets = { "Low", "Medium", "High", "Ultra", "Custom" };
        Action<int> setPreset = null;
        setPreset = MenuBuilder.StepperRow(t, "Graphics", presets, (int)GameSettings.Graphics, i =>
        {
            var p = (GameSettings.GraphicsPreset)i;
            if (p == GameSettings.GraphicsPreset.Custom) { setPreset((int)GameSettings.Graphics); return; }
            GameSettings.ApplyGraphicsPreset(p);
            GameSettings.Apply();
            DevQuests.Signal("graphics:preset");
            Rebuild();
        }, "Sets every row below. Change any of them and this reads Custom.");

        void RowChanged(bool rebuild)
        {
            GameSettings.DetectGraphicsPreset();
            GameSettings.Apply();
            if (rebuild) Rebuild(); else setPreset((int)GameSettings.Graphics);
        }

        MenuBuilder.StepperRow(t, "Shadows", new[] { "Off", "Low", "Medium", "High", "Ultra" }, (int)GameSettings.Shadows,
            i => { GameSettings.Shadows = (GameSettings.ShadowLevel)i; RowChanged(true); },
            "Shadow map detail. Off is the biggest saving; Ultra adds a second cascade.");

        MenuBuilder.RangeSliderRow(t, "Shadow distance", GameSettings.ShadowDistanceScale, 0.5f, 1.5f,
            v => Mathf.RoundToInt(v * 100f) + "%",
            v => { GameSettings.ShadowDistanceScale = v; RowChanged(false); },
            "How far past the top of the view shadows are drawn. Raise it if far trees lose theirs.");

        MenuBuilder.ToggleRow(t, "Soft shadows", GameSettings.SoftShadows,
            v => { GameSettings.SoftShadows = v; RowChanged(true); },
            "Blurs shadow edges. A small cost on older graphics cards.");

        int aaIndex = 0;
        string[] aaNames = new string[GameSettings.AntiAliasingChoices.Length];
        for (int i = 0; i < aaNames.Length; i++)
        {
            int a = GameSettings.AntiAliasingChoices[i];
            aaNames[i] = a == 0 ? "Off" : a + "x";
            if (a == GameSettings.AntiAliasing) aaIndex = i;
        }
        MenuBuilder.StepperRow(t, "Anti-aliasing", aaNames, aaIndex,
            i => { GameSettings.AntiAliasing = GameSettings.AntiAliasingChoices[i]; RowChanged(true); },
            "Smooths the edges of the low-poly art (MSAA).");

        MenuBuilder.RangeSliderRow(t, "Resolution scale", GameSettings.RenderScale, 0.5f, 1.5f,
            v => Mathf.RoundToInt(v * 100f) + "%",
            v => { GameSettings.RenderScale = v; RowChanged(false); },
            "Renders the world at a fraction of the window. Below 100% is the biggest frame-rate win.");

        MenuBuilder.StepperRow(t, "Clouds", new[] { "Off", "Shadows only", "Full" }, (int)GameSettings.Clouds,
            i => { GameSettings.Clouds = (GameSettings.CloudMode)i; RowChanged(true); },
            "Drifting clouds and the shade they cast on the island.");
    }

    private void BuildCameraTab(Transform t)
    {
        MenuBuilder.RangeSliderRow(t, "Pan speed", GameSettings.CameraSpeed, 0.25f, 3f,
            Multiplier, v => { GameSettings.CameraSpeed = v; GameSettings.Apply(); },
            "How fast the view moves under WASD, edge pan, and the arrow keys.");

        MenuBuilder.RangeSliderRow(t, "Zoom speed", GameSettings.ZoomSpeed, 0.25f, 3f,
            Multiplier, v => { GameSettings.ZoomSpeed = v; GameSettings.Apply(); },
            "How far one notch of the scroll wheel travels.");

        MenuBuilder.RangeSliderRow(t, "Rotation speed", GameSettings.RotationSpeed, 0.25f, 3f,
            Multiplier, v => { GameSettings.RotationSpeed = v; GameSettings.Apply(); },
            "How fast Q and E swing the camera around.");

        MenuBuilder.ToggleRow(t, "Edge pan", GameSettings.EdgePan,
            v => { GameSettings.EdgePan = v; GameSettings.Apply(); },
            "Move the view by pushing the mouse against the screen edge.");

        MenuBuilder.ToggleRow(t, "Invert tilt", GameSettings.InvertTilt,
            v => { GameSettings.InvertTilt = v; GameSettings.Apply(); },
            "Flips the vertical direction of middle-mouse tilt.");

        MenuBuilder.RangeSliderRow(t, "Screen shake", GameSettings.ScreenShakeStrength, 0f, 1.5f,
            Multiplier, v => { GameSettings.ScreenShakeStrength = v; GameSettings.Apply(); },
            "Camera kick on hits and deaths. Set to 0x to turn it off entirely.");
    }

    private void BuildInterfaceTab(Transform t)
    {
        MenuBuilder.RangeSliderRow(t, "UI scale", GameSettings.UIScale, 0.7f, 1.6f,
            Multiplier, v => { GameSettings.UIScale = v; GameSettings.Apply(); },
            "Size of menus and panels. Takes effect as you drag.");

        MenuBuilder.StepperRow(t, "Health bars",
            new[] { "Always", "When damaged", "Never" }, (int)GameSettings.HealthBarMode,
            i => { GameSettings.HealthBarMode = (GameSettings.HealthBars)i; GameSettings.Apply(); },
            "When the green bars over units and buildings are drawn.");

        MenuBuilder.ToggleRow(t, "Damage numbers", GameSettings.DamageNumbers,
            v => { GameSettings.DamageNumbers = v; GameSettings.Apply(); },
            "Floating numbers on every hit.");

        MenuBuilder.ToggleRow(t, "Unit state labels", GameSettings.UnitStateText,
            v => { GameSettings.UnitStateText = v; GameSettings.Apply(); },
            "Shows what each unit is doing above its head. Useful, busy.");

        MenuBuilder.ToggleRow(t, "Show build grid by default", GameSettings.GridByDefault,
            v => { GameSettings.GridByDefault = v; GameSettings.Apply(); },
            "The grid always appears in build mode regardless.");

        MenuBuilder.ToggleRow(t, "Pause when unfocused", GameSettings.PauseOnFocusLoss,
            v => { GameSettings.PauseOnFocusLoss = v; GameSettings.Apply(); },
            "Opens the pause menu when you switch to another window.");
    }

    private void BuildTabs(Transform parent, string[] names, int active, Action<int> onPick)
    {
        MenuBuilder.TabRow(parent, names, active, onPick);
    }

    /// <summary>
    /// The keybinding list. Every row has two clickable slots (main key and
    /// alternate); clicking one arms capture, and the next key pressed takes it.
    /// </summary>
    private void BuildControls()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Controls", MenuStyle.OptionsWidth, 720f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 6f);

        MenuBuilder.Label(col.transform, "CONTROLS", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        string hint = captureAction.HasValue
            ? "Press any key…   (Esc cancels)"
            : "Click a key to change it. A key already in use is taken from its old action.";
        MenuBuilder.Label(col.transform, hint, MenuStyle.SmallSize,
            captureAction.HasValue ? MenuStyle.TextAccent : MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        MenuBuilder.Divider(col.transform);

        VerticalLayoutGroup body = MenuBuilder.ScrollColumn(col.transform, 2f, 430f);
        activeScroll = body.GetComponentInParent<ScrollRect>();
        Transform t = body.transform;

        string lastGroup = null;
        for (int i = 0; i < KeyBindings.Catalog.Length; i++)
        {
            var entry = KeyBindings.Catalog[i];
            if (entry.group != lastGroup)
            {
                lastGroup = entry.group;
                MenuBuilder.SectionHeader(t, entry.group);
            }

            KeyBindings.Action action = entry.action;
            KeyBindings.Binding b = KeyBindings.Get(action);

            MenuBuilder.KeyBindRow(t, entry.label,
                KeyBindings.Name(b.primary), KeyBindings.Name(b.secondary),
                () => ArmCapture(action, false),
                () => ArmCapture(action, true),
                highlightPrimary: captureAction == action && !captureSecondary,
                highlightSecondary: captureAction == action && captureSecondary,
                modified: !KeyBindings.IsDefault(action));
        }

        // Fixed keys, listed so the screen is a complete reference rather than
        // only the rebindable half. These deliberately have no slots to click.
        MenuBuilder.SectionHeader(t, "Fixed");
        MenuBuilder.ValueRow(t, "Cancel / pause menu", "Esc", MenuStyle.TextMuted);
        MenuBuilder.ValueRow(t, "Select / place", "Left mouse", MenuStyle.TextMuted);
        MenuBuilder.ValueRow(t, "Cancel placement", "Right mouse", MenuStyle.TextMuted);
        MenuBuilder.ValueRow(t, "Tilt / orbit camera", "Middle mouse drag", MenuStyle.TextMuted);
        MenuBuilder.ValueRow(t, "Zoom", "Mouse wheel", MenuStyle.TextMuted);

        MenuBuilder.Spacer(col.transform, 8f);
        MenuBuilder.Label(col.transform, "* marks a binding you have changed.",
            MenuStyle.SmallSize, MenuStyle.TextMuted).gameObject
            .AddComponent<LayoutElement>().preferredHeight = 20f;

        MenuBuilder.MenuButton(col.transform, "RESET KEYS", () =>
            AskConfirm("Reset all keybindings?", () => { KeyBindings.ResetToDefaults(); Back(); }),
            enabled: KeyBindings.AnyCustomised());
        MenuBuilder.MenuButton(col.transform, "BACK", () => Back());
    }

    private void ArmCapture(KeyBindings.Action action, bool secondary)
    {
        captureAction = action;
        captureSecondary = secondary;
        Rebuild();
    }

    private void BuildCredits()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Credits", MenuStyle.MenuWidth + 120f, 520f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 8f);

        MenuBuilder.Label(col.transform, "CREDITS", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 14f);

        MenuBuilder.Label(col.transform,
            "Design & Code\n—\n\nArt\n—\n\nAudio\n—\n\nBuilt with Unity",
            MenuStyle.BodySize, MenuStyle.TextPrimary).gameObject
            .AddComponent<LayoutElement>().preferredHeight = 260f;

        MenuBuilder.MenuButton(col.transform, "BACK", () => Back());
    }

    /// <summary>
    /// Patch notes, newest first, from Resources/Changelog.txt (see
    /// <see cref="Changelog"/> for the format). Shared by the main menu and the
    /// pause menu like Credits, so it must read correctly over a frozen game too.
    ///
    /// Bullets are ordinary wrapping labels with no fixed height: inside a
    /// ScrollColumn the ContentSizeFitter re-measures every layout pass, so TMP's
    /// wrapped height is picked up without the one-line rule the Options
    /// descriptions need (those are sized on the same frame the panel is).
    /// </summary>
    private void BuildChangelog()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Changelog", MenuStyle.OptionsWidth, 720f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 6f);

        MenuBuilder.Label(col.transform, "CHANGELOG", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        MenuBuilder.Label(col.transform, "What changed, newest first.", MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        MenuBuilder.Divider(col.transform);

        VerticalLayoutGroup body = MenuBuilder.ScrollColumn(col.transform, 4f, 480f);
        activeScroll = body.GetComponentInParent<ScrollRect>();
        Transform t = body.transform;

        IReadOnlyList<Changelog.Entry> entries = Changelog.Entries;
        if (entries.Count == 0)
        {
            MenuBuilder.Label(t, "No notes yet.", MenuStyle.BodySize, MenuStyle.TextMuted)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            Changelog.Entry e = entries[i];

            MenuBuilder.Spacer(t, i == 0 ? 4f : 14f);
            TextMeshProUGUI heading = MenuBuilder.Label(t, e.heading, MenuStyle.BodySize,
                MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
            heading.gameObject.name = "EntryHeading";
            MenuBuilder.Divider(t);
            MenuBuilder.Spacer(t, 2f);

            for (int b = 0; b < e.bullets.Count; b++)
            {
                // <indent> holds every wrapped line at the bullet text's left
                // edge — a hanging indent — while the glyph sits in the gutter.
                TextMeshProUGUI line = MenuBuilder.Label(t, "•  <indent=1.2em>" + e.bullets[b] + "</indent>",
                    MenuStyle.SmallSize + 1f, MenuStyle.TextPrimary, TextAlignmentOptions.TopLeft);
                line.gameObject.name = "Bullet";
                line.margin = new Vector4(6f, 0f, 0f, 0f);
            }
        }

        MenuBuilder.Spacer(col.transform, 6f);
        MenuBuilder.MenuButton(col.transform, "BACK", () => Back());
    }

    // ---- Information --------------------------------------------------------
    //
    // The field guide (2026-09-07): prose from Resources/Information.txt
    // (GameInfo) in sub-tabs, with the numbers built from the live catalogs
    // where the text asks for a table. Shared by the main menu and the pause
    // menu like the changelog. The sub-tab and each sub-tab's scroll position
    // are static so they survive a rebuild and a trip back to the game.

    private static int infoTab;
    private static float[] infoScroll;
    private static readonly string MutedHex = ColorUtility.ToHtmlStringRGBA(MenuStyle.TextMuted);

    private void BuildInformation()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Information", MenuStyle.OptionsWidth, 720f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 6f);

        MenuBuilder.Label(col.transform, "INFORMATION", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        MenuBuilder.Label(col.transform, "How the island works.", MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        MenuBuilder.Divider(col.transform);

        DevQuests.Signal("info");

        // The DEV tab (playtest quests) is synthetic: appended after the text
        // asset's tabs in editor and development builds only.
        IReadOnlyList<GameInfo.Tab> tabsList = GameInfo.Tabs;
        int devIndex = DevQuests.Enabled ? tabsList.Count : -1;
        int tabCount = tabsList.Count + (DevQuests.Enabled ? 1 : 0);
        if (tabCount == 0)
        {
            MenuBuilder.Label(col.transform, "No information yet.", MenuStyle.BodySize, MenuStyle.TextMuted)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }
        else
        {
            if (infoScroll == null || infoScroll.Length != tabCount)
            {
                infoScroll = new float[tabCount];
                for (int i = 0; i < infoScroll.Length; i++) infoScroll[i] = 1f;   // 1 = the top
            }
            infoTab = Mathf.Clamp(infoTab, 0, tabCount - 1);

            string[] names = new string[tabCount];
            for (int i = 0; i < tabsList.Count; i++) names[i] = tabsList[i].title;
            if (devIndex >= 0) names[devIndex] = "DEV";
            Button[] tabButtons = MenuBuilder.TabRow(col.transform, names, infoTab, SwitchInfoTab);
            for (int i = 0; i < tabButtons.Length; i++)
            {
                // Six tabs across the panel: the button-size caption would wrap on
                // RESEARCH and BUILDING, so the tab captions are one size down.
                TextMeshProUGUI caption = tabButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (caption == null) continue;
                caption.fontSize = MenuStyle.SmallSize + 2f;
                caption.textWrappingMode = TextWrappingModes.NoWrap;
            }
            MenuBuilder.Spacer(col.transform, 2f);

            VerticalLayoutGroup body = MenuBuilder.ScrollColumn(col.transform, 4f, 470f);
            activeScroll = body.GetComponentInParent<ScrollRect>();
            if (infoTab == devIndex) RenderDevTab(body.transform);
            else RenderInfoTab(body.transform, tabsList[infoTab]);
        }

        MenuBuilder.Spacer(col.transform, 6f);
        MenuBuilder.MenuButton(col.transform, "BACK", () => Back());
    }

    // ---- DEV: playtest quests ---------------------------------------------

    private static string devReportStatus;

    /// <summary>
    /// The playtest log (2026-09-07): every quest with a done toggle, PASS /
    /// FAIL and a note, the tracker toggle, the report notes and SUBMIT REPORT.
    /// Every click rebuilds the screen (the Options pattern); the scroll memory
    /// puts the list back where it was.
    /// </summary>
    private void RenderDevTab(Transform t)
    {
        DevQuests.Signal("info:dev");

        InfoParagraph(t, "Playtest quests for what changed recently. Quests marked auto tick themselves as you play; "
            + "tick the rest once you have checked them. PASS or FAIL each, add a note where something was off, "
            + "and SUBMIT REPORT copies a markdown report to the clipboard and saves it under Playtests/.", MenuStyle.TextMuted);

        MenuBuilder.ToggleRow(t, "HUD tracker", DevQuests.ShowTracker,
            v => { DevQuests.ShowTracker = v; DevQuests.Signal("tracker"); });
        MenuBuilder.ValueRow(t, "Done", DevQuests.DoneCount + " / " + DevQuests.TotalCount);

        IReadOnlyList<DevQuests.Batch> batches = DevQuests.Batches;
        if (batches.Count == 0)
            InfoParagraph(t, "No quests. Add a batch to Resources/DevQuests.txt.", MenuStyle.TextMuted);

        for (int b = 0; b < batches.Count; b++)
        {
            DevQuests.Batch batch = batches[b];
            MenuBuilder.SectionHeader(t, batch.title);
            for (int i = 0; i < batch.quests.Count; i++) DevQuestRow(t, batch.quests[i]);
        }

        MenuBuilder.SectionHeader(t, "Report");
        TMP_InputField notes = MenuBuilder.InputRow(t, "Notes", DevQuests.Notes, "anything else you noticed",
            v => DevQuests.Notes = v);
        notes.characterLimit = 600;

        if (!string.IsNullOrEmpty(devReportStatus))
            InfoParagraph(t, devReportStatus, MenuStyle.TextAccent);

        MenuBuilder.MenuButton(t, "SUBMIT REPORT", () =>
        {
            string path = DevQuests.Submit();
            devReportStatus = path != null
                ? "Copied to the clipboard and saved to " + path
                : "Copied to the clipboard; the file could not be written (see the console).";
            Rebuild();
        });
        MenuBuilder.MenuButton(t, "RESET QUESTS", () =>
            AskConfirm("Clear every result and note?", () => { DevQuests.Reset(); devReportStatus = null; }),
            textColor: MenuStyle.TextDanger);
    }

    /// <summary>One quest: the text (ellipsised), [done] PASS FAIL in the slot, a note field beneath.</summary>
    private void DevQuestRow(Transform t, DevQuests.Quest q)
    {
        string caption = (q.IsAuto ? "<color=#" + MutedHex + "><size=70%>auto</size></color> " : "") + q.text;
        RectTransform row = MenuBuilder.SettingRow(t, caption, out RectTransform slot, 40f);

        TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>();
        label.fontSize = MenuStyle.SmallSize + 1f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.color = q.done ? MenuStyle.TextMuted : MenuStyle.TextPrimary;
        label.rectTransform.anchorMax = new Vector2(0.64f, 1f);
        slot.anchorMin = new Vector2(0.66f, 0f);

        Button done = MenuBuilder.SlotButton(slot, q.done ? "done" : "todo",
            () => { DevQuests.SetDone(q, !q.done); Rebuild(); }, new Vector2(0f, 0.08f), new Vector2(0.32f, 0.92f));
        Button pass = MenuBuilder.SlotButton(slot, "PASS",
            () => { DevQuests.SetResult(q, q.result == DevQuests.Result.Pass ? DevQuests.Result.Untested : DevQuests.Result.Pass); Rebuild(); },
            new Vector2(0.34f, 0.08f), new Vector2(0.66f, 0.92f));
        Button fail = MenuBuilder.SlotButton(slot, "FAIL",
            () => { DevQuests.SetResult(q, q.result == DevQuests.Result.Fail ? DevQuests.Result.Untested : DevQuests.Result.Fail); Rebuild(); },
            new Vector2(0.68f, 0.08f), new Vector2(1f, 0.92f));

        TintVerdict(done, q.done, MenuStyle.TextAccent);
        TintVerdict(pass, q.result == DevQuests.Result.Pass, MenuStyle.TextAccent);
        TintVerdict(fail, q.result == DevQuests.Result.Fail, MenuStyle.TextDanger);

        // The note only takes a row once there is a verdict to explain, or a note already.
        if (q.result != DevQuests.Result.Untested || !string.IsNullOrEmpty(q.note))
        {
            TMP_InputField note = MenuBuilder.InputRow(t, "    note", q.note, "what happened", v => DevQuests.SetNote(q, v));
            note.characterLimit = 200;
        }
    }

    private static void TintVerdict(Button b, bool on, Color onColor)
    {
        b.targetGraphic.color = on ? MenuStyle.ButtonPressed : MenuStyle.ButtonFill;
        TextMeshProUGUI caption = b.GetComponentInChildren<TextMeshProUGUI>();
        if (caption != null)
        {
            caption.fontSize = MenuStyle.SmallSize;
            caption.color = on ? onColor : MenuStyle.TextMuted;
        }
    }

    /// <summary>
    /// Sub-tab switch. Rebuild() would save the OLD tab's scroll position under
    /// this screen and RestoreScroll would then apply it to the new tab, so the
    /// old position is banked per sub-tab here, the screen memory is pointed at
    /// the new tab's, and the live scroll is dropped before the rebuild.
    /// </summary>
    private void SwitchInfoTab(int i)
    {
        if (i == infoTab) return;
        if (activeScroll != null) infoScroll[infoTab] = activeScroll.verticalNormalizedPosition;
        activeScroll = null;
        infoTab = i;
        scrollMemory[Screen.Information] = infoScroll[i];
        Rebuild();
    }

    private void RenderInfoTab(Transform t, GameInfo.Tab tab)
    {
        DevQuests.Signal("info:" + tab.title.ToLowerInvariant());   // info:research, info:building, info:raids ...
        for (int i = 0; i < tab.blocks.Count; i++)
        {
            GameInfo.Block b = tab.blocks[i];
            switch (b.kind)
            {
                case GameInfo.BlockKind.Heading:
                    MenuBuilder.SectionHeader(t, b.text);
                    MenuBuilder.Spacer(t, 2f);
                    break;

                case GameInfo.BlockKind.Paragraph:
                    InfoParagraph(t, b.text, MenuStyle.TextPrimary);
                    break;

                case GameInfo.BlockKind.Bullet:
                    // The changelog's hanging indent: wrapped lines hold the text's left edge.
                    TextMeshProUGUI line = MenuBuilder.Label(t, "•  <indent=1.2em>" + b.text + "</indent>",
                        MenuStyle.SmallSize + 1f, MenuStyle.TextPrimary, TextAlignmentOptions.TopLeft);
                    line.gameObject.name = "Bullet";
                    line.margin = new Vector4(6f, 0f, 0f, 0f);
                    break;

                case GameInfo.BlockKind.Table:
                    RenderInfoTable(t, b.text);
                    break;
            }
        }
        MenuBuilder.Spacer(t, 8f);
    }

    private static TextMeshProUGUI InfoParagraph(Transform t, string text, Color color)
    {
        TextMeshProUGUI p = MenuBuilder.Label(t, text, MenuStyle.SmallSize + 1f, color, TextAlignmentOptions.TopLeft);
        p.gameObject.name = "Paragraph";
        p.margin = new Vector4(6f, 0f, 0f, 4f);
        return p;
    }

    /// <summary>
    /// One catalog entry: an accent title with a muted tag on the same line, an
    /// optional one-line detail (costs, timings), and an optional wrapping note.
    /// </summary>
    private static void InfoEntry(Transform t, string title, string tag, string detail, string note)
    {
        MenuBuilder.Spacer(t, 4f);
        string head = string.IsNullOrEmpty(tag)
            ? title
            : title + "  <size=78%><color=#" + MutedHex + ">" + tag + "</color></size>";
        TextMeshProUGUI h = MenuBuilder.Label(t, head, MenuStyle.BodySize, MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
        h.gameObject.name = "EntryTitle";
        h.textWrappingMode = TextWrappingModes.NoWrap;
        h.margin = new Vector4(6f, 0f, 0f, 0f);
        h.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        if (!string.IsNullOrEmpty(detail))
        {
            TextMeshProUGUI d = MenuBuilder.RowDescription(t, detail);
            d.margin = new Vector4(6f, 0f, 0f, 0f);
        }
        if (!string.IsNullOrEmpty(note)) InfoParagraph(t, note, MenuStyle.TextPrimary);
    }

    private void RenderInfoTable(Transform t, string name)
    {
        switch (name)
        {
            case "research": RenderResearchTable(t); break;
            case "recipes": RenderRecipeTable(t); break;
            case "weapons": RenderWeaponTable(t); break;
            case "buildings": RenderBuildingTable(t); break;
            case "difficulty": RenderDifficultyTable(t); break;
            case "colony": RenderColonyTable(t); break;
            case "raids": RenderRaidTable(t); break;
            default:
                // A typo in the text asset shows as a muted marker, never an error.
                InfoParagraph(t, "[" + name + "]", MenuStyle.TextMuted);
                break;
        }
    }

    private void RenderResearchTable(Transform t)
    {
        var all = ResearchCatalog.All;
        for (int s = 0; s < 2; s++)
        {
            ResearchCatalog.Station station = (ResearchCatalog.Station)s;
            MenuBuilder.SectionHeader(t, station == ResearchCatalog.Station.Campfire ? "Researched at the campfire" : "Researched at the Workshop");
            for (int i = 0; i < all.Length; i++)
            {
                ResearchCatalog.ResearchDef d = all[i];
                if (d.station != station) continue;

                string detail = d.CostText + "  ·  " + Mathf.RoundToInt(d.seconds) + "s";
                if (d.prerequisites.Length > 0)
                {
                    detail += "  ·  after ";
                    for (int p = 0; p < d.prerequisites.Length; p++)
                    {
                        ResearchCatalog.ResearchDef pre = ResearchCatalog.Find(d.prerequisites[p]);
                        if (p > 0) detail += ", ";
                        detail += pre != null ? pre.title : d.prerequisites[p];
                    }
                }
                string note = d.description;
                if (d.tool != null) note += ". Puts the " + d.tool.displayName + " in your hands";
                InfoEntry(t, d.title, "tier " + d.tier, detail, note + ".");
            }
        }
    }

    private void RenderRecipeTable(Transform t)
    {
        MenuBuilder.SectionHeader(t, "Recipes");
        var all = CraftingCatalog.All;
        for (int i = 0; i < all.Length; i++)
        {
            CraftingCatalog.Recipe r = all[i];
            string detail = r.CostText + "  ·  " + Mathf.RoundToInt(r.seconds) + "s";
            string req = r.RequiredTitle;
            if (!string.IsNullOrEmpty(req)) detail += "  ·  needs " + req;
            InfoEntry(t, r.title, r.category.ToString().ToLowerInvariant(), detail, r.description + ".");
        }
    }

    private void RenderWeaponTable(Transform t)
    {
        MenuBuilder.SectionHeader(t, "Weapons");
        var weapons = ItemCatalog.Weapons;
        for (int i = 0; i < weapons.Length; i++)
        {
            ItemDef w = weapons[i];
            EquipmentDef e = w.equipment;
            if (e == null) continue;
            float dps = e.attackInterval > 0f ? e.damage / e.attackInterval : 0f;
            string detail = e.damage.ToString("0") + " damage every " + e.attackInterval.ToString("0.#") + "s  ·  "
                + dps.ToString("0.#") + " a second";
            if (e.ranged) detail += "  ·  range " + e.range.ToString("0");
            InfoEntry(t, w.displayName, e.ranged ? "ranged, an archer" : "melee", detail, null);
        }
    }

    private void RenderBuildingTable(Transform t)
    {
        MenuBuilder.SectionHeader(t, "Buildings");
        BuildingDatabase db = BuildingDatabase.Instance;
        if (db == null || db.buildings == null)
        {
            // The database lives in the game scene; the main menu has none.
            InfoParagraph(t, "Costs and health are listed here during a game (pause menu).", MenuStyle.TextMuted);
            return;
        }
        for (int i = 0; i < db.buildings.Length; i++)
        {
            BuildingData d = db.buildings[i];
            if (d == null) continue;
            string cost = "";
            if (d.woodCost > 0) cost += d.woodCost + " wood";
            if (d.foodCost > 0) cost += (cost.Length > 0 ? " · " : "") + d.foodCost + " food";
            if (d.stoneCost > 0) cost += (cost.Length > 0 ? " · " : "") + d.stoneCost + " stone";
            if (d.metalCost > 0) cost += (cost.Length > 0 ? " · " : "") + d.metalCost + " metal";
            if (cost.Length == 0) cost = "free";
            string tag = d.isWall ? "wall, drawn as a line" : d.requiresShore ? "beach only" : null;
            InfoEntry(t, d.buildingName, tag, cost + "  ·  " + Mathf.RoundToInt(d.maxHealth) + " health", null);
        }
    }

    private void RenderDifficultyTable(Transform t)
    {
        MenuBuilder.SectionHeader(t, "Difficulty");
        for (int i = 0; i < (int)Difficulty.Level.Custom; i++)
        {
            Difficulty.Preset p = Difficulty.Get((Difficulty.Level)i);
            string detail = "raids ×" + p.enemyCount.ToString("0.##") + " size, ×" + p.raidFrequency.ToString("0.##") + " often"
                + "  ·  food ×" + p.foodConsumption.ToString("0.##")
                + "  ·  start ×" + p.startingResources.ToString("0.##")
                + "  ·  night ×" + p.nightLength.ToString("0.##");
            InfoEntry(t, p.name, p.daysToSurvive + " days", detail, p.blurb);
        }
    }

    private void RenderColonyTable(Transform t)
    {
        string[] lines =
        {
            "A survivor lands every " + PopulationManager.DefaultArrivalInterval.ToString("0") + " seconds by day while there is housing and the colony is fed.",
            "Each colonist eats " + PopulationManager.DefaultFoodPerDay.ToString("0.#") + " food per day, times the difficulty's food multiplier.",
            "Hungry after " + PopulationManager.DefaultHungryAfterDays.ToString("0.##") + " of a day without food: work at "
                + Mathf.RoundToInt(PopulationManager.HungryLaborMultiplier * 100f) + "%, and nobody new lands.",
            "Starving after " + PopulationManager.DefaultStarvingAfterDays.ToString("0.#") + " day without food: one colonist leaves each day it lasts.",
        };
        for (int i = 0; i < lines.Length; i++)
        {
            TextMeshProUGUI line = MenuBuilder.Label(t, "•  <indent=1.2em>" + lines[i] + "</indent>",
                MenuStyle.SmallSize + 1f, MenuStyle.TextPrimary, TextAlignmentOptions.TopLeft);
            line.margin = new Vector4(6f, 0f, 0f, 0f);
        }
    }

    private void RenderRaidTable(Transform t)
    {
        // Live values in a game, the code defaults on the main menu — the director
        // is runtime-added, so there is no scene copy to disagree with either.
        RaidDirector rd = RaidDirector.Instance;
        int firstDay = rd != null ? rd.firstRaidDay : RaidDirector.DefaultFirstRaidDay;
        float baseChance = rd != null ? rd.baseChance : RaidDirector.DefaultBaseChance;
        float perQuiet = rd != null ? rd.chancePerQuietDay : RaidDirector.DefaultChancePerQuietDay;
        int maxQuiet = rd != null ? rd.maxQuietDays : RaidDirector.DefaultMaxQuietDays;
        float baseSize = rd != null ? rd.baseSize : RaidDirector.DefaultBaseSize;
        float perDay = rd != null ? rd.sizePerDay : RaidDirector.DefaultSizePerDay;
        float perProsperity = rd != null ? rd.sizePerProsperity : RaidDirector.DefaultSizePerProsperity;
        int minSize = rd != null ? rd.minSize : RaidDirector.DefaultMinSize;

        int Size(int day) => Mathf.Max(minSize, Mathf.RoundToInt(baseSize + perDay * day));

        MenuBuilder.SectionHeader(t, "The raid roll");
        string[] lines =
        {
            "Nothing lands before day " + firstDay + ".",
            "Each dawn after that: " + Mathf.RoundToInt(baseChance * 100f) + "% chance, plus " + Mathf.RoundToInt(perQuiet * 100f)
                + "% for every quiet night since the last raid, times the difficulty. Certain after " + maxQuiet + " quiet nights.",
            "Raiders: " + baseSize.ToString("0.#") + " + " + perDay.ToString("0.##") + " per day + " + perProsperity.ToString("0.##")
                + " per point of prosperity, times the difficulty, never fewer than " + minSize + ". Every colonist, hut, tower, the Workshop and the Shipyard add prosperity.",
            "With nothing built: day " + Mathf.Max(firstDay, 5) + " brings " + Size(Mathf.Max(firstDay, 5)) + ", day 20 brings " + Size(20) + ", day 30 brings " + Size(30) + ".",
        };
        for (int i = 0; i < lines.Length; i++)
        {
            TextMeshProUGUI line = MenuBuilder.Label(t, "•  <indent=1.2em>" + lines[i] + "</indent>",
                MenuStyle.SmallSize + 1f, MenuStyle.TextPrimary, TextAlignmentOptions.TopLeft);
            line.margin = new Vector4(6f, 0f, 0f, 0f);
        }
    }

    /// <summary>
    /// Victory / defeat. One screen with two dressings rather than two screens —
    /// the stats block, the buttons and the layout are identical, and only the
    /// title, subtitle, accent colour and the Keep Playing button differ.
    ///
    /// This replaced a pair of scene-authored uGUI panels (`VictoryDefeatUI`)
    /// that predated the menu system and looked nothing like it. They also could
    /// not return to the main menu at all — the old Quit button called
    /// Application.Quit, which in the editor just stopped Play.
    /// </summary>
    private void BuildGameOver()
    {
        panel = MenuBuilder.Panel(canvas.transform, "GameOver", MenuStyle.OptionsWidth - 120f, 620f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 6f);

        Color accent = gameOverVictory ? MenuStyle.TextAccent : MenuStyle.TextDanger;

        // The escape (2026-09-04, Slice 6) is a victory with its own dressing:
        // the colony sailed for home on day N instead of waiting for the rescue.
        bool escaped = gameOverVictory && GameManager.Instance != null && GameManager.Instance.isEscape;
        string titleText = escaped ? "ESCAPED" : gameOverVictory ? "VICTORY" : "DEFEAT";
        string subtitle = escaped
            ? "You built a ship and sailed for home on day "
              + (GameManager.Instance != null ? GameManager.Instance.currentDay : 0) + "."
            : gameOverVictory ? "The rescue ship has arrived." : "Your camp was overrun.";

        TextMeshProUGUI title = MenuBuilder.Label(col.transform, titleText, MenuStyle.TitleSize - 10f, accent);
        title.characterSpacing = 8f;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        MenuBuilder.Label(col.transform, subtitle, MenuStyle.BodySize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        MenuBuilder.Spacer(col.transform, 6f);
        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 6f);

        BuildRunSummary(col.transform);

        MenuBuilder.Spacer(col.transform, 6f);
        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 10f);

        // Victory only. Defeat has nothing to keep playing — the campfire is
        // gone, which is the lose condition itself.
        if (gameOverVictory)
        {
            MenuBuilder.MenuButton(col.transform, "KEEP PLAYING",
                () => { if (GameManager.Instance != null) GameManager.Instance.ContinuePlaying(); },
                textColor: MenuStyle.TextAccent);
        }

        // No confirm dialogs here: the run is already over, so none of these
        // three can lose the player anything they still have.
        MenuBuilder.MenuButton(col.transform, "RESTART", MenuFlow.Restart);
        MenuBuilder.MenuButton(col.transform, "MAIN MENU", MenuFlow.ToMainMenu);
        MenuBuilder.MenuButton(col.transform, "QUIT TO DESKTOP", MenuFlow.QuitGame,
            textColor: MenuStyle.TextDanger);
    }

    /// <summary>How the run went, as label/value rows.</summary>
    private void BuildRunSummary(Transform parent)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        // GetDaysSurvived already reads "calendar day - 1": the victory dawn is
        // day N+1 so it reports N, and a defeat during day D reports D-1 — the
        // day you lost was not survived.
        MenuBuilder.ValueRow(parent, "Days survived", gm.GetDaysSurvived() + " / " + gm.daysToSurvive);
        RaidDirector director = RaidDirector.Instance;
        if (director != null)
            MenuBuilder.ValueRow(parent, "Raids weathered", director.RaidsSoFar.ToString());
        MenuBuilder.ValueRow(parent, "Enemies defeated", gm.GetEnemiesKilled().ToString());
        MenuBuilder.ValueRow(parent, "Colony at its peak",
            gm.maxWorkers + " workers  ·  " + gm.maxWarriors + " warriors");
        // Starvation departures (2026-09-04) — only when it happened
        Population pm = Factions.Player.Population;
        if (pm != null && pm.ColonistsLeft > 0)
            MenuBuilder.ValueRow(parent, "Colonists who left", pm.ColonistsLeft.ToString(), MenuStyle.TextDanger);

        ResourcePool rm = Factions.Player.Resources;
        {
            MenuBuilder.ValueRow(parent, "Resources on hand",
                rm.wood + "W  ·  " + rm.food + "F  ·  " + rm.stone + "S  ·  " + rm.metal + "M");
        }

        MenuBuilder.ValueRow(parent, "Difficulty", Difficulty.ActiveName, MenuStyle.TextMuted);
    }

    /// <summary>
    /// Name your castaway. Pre-filled with the last name used so a returning
    /// player just presses Enter. Enter in the field and the BEGIN button do
    /// the same thing.
    /// </summary>
    private void BuildNameEntry()
    {
        panel = MenuBuilder.Panel(canvas.transform, "NameEntry", MenuStyle.MenuWidth + 60f, 300f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, 8f);

        MenuBuilder.Label(col.transform, "WASHED ASHORE", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 6f);

        MenuBuilder.Label(col.transform, "You alone survived the wreck. What is your name?",
            MenuStyle.BodySize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        MenuBuilder.Spacer(col.transform, 4f);

        TMP_InputField field = MenuBuilder.InputRow(col.transform, "Name", PlayerProfile.LastName,
            PlayerProfile.DefaultName, null);
        field.characterLimit = PlayerProfile.MaxNameLength;

        MenuBuilder.Spacer(col.transform, 10f);

        Action confirm = () =>
        {
            PlayerProfile.BeginRun(field.text);
            Action cb = nameEntryCallback;
            nameEntryCallback = null;
            Close();
            cb?.Invoke();
        };

        field.onSubmit.AddListener(_ => confirm());
        MenuBuilder.MenuButton(col.transform, "BEGIN", confirm, textColor: MenuStyle.TextAccent);

        // Put the caret in the field so the player can just type
        field.Select();
        field.ActivateInputField();
    }

    private void AskConfirm(string message, Action action)
    {
        confirmMessage = message;
        confirmAction = action;
        Show(Screen.Confirm);
    }

    /// <summary>
    /// The Shipyard's question (2026-09-04, Slice 6): the same Confirm screen
    /// Restart uses, opened from gameplay, so Esc / NO simply closes it.
    /// </summary>
    public void AskSetSail(int day, Action yes)
    {
        AskConfirm("Set sail on day " + day + "? Everyone leaves the island.", yes);
    }

    private void BuildConfirm()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Confirm", MenuStyle.MenuWidth, 260f);
        VerticalLayoutGroup col = activeColumn = MenuBuilder.Column(panel, MenuStyle.ButtonSpacing);

        MenuBuilder.Spacer(col.transform, 12f);
        MenuBuilder.Label(col.transform, confirmMessage, MenuStyle.BodySize, MenuStyle.TextPrimary)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
        MenuBuilder.Spacer(col.transform, 8f);

        Action act = confirmAction;
        MenuBuilder.MenuButton(col.transform, "CONFIRM", () => act?.Invoke(), textColor: MenuStyle.TextDanger);
        MenuBuilder.MenuButton(col.transform, "CANCEL", () => Back());
    }
}

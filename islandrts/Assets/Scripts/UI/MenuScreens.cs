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
/// Controls (rebinding), Credits, Confirm. The layouts these produce are
/// documented in docs/MENU_WIREFRAMES.md — keep the two in sync, that file is
/// what the artist works from.
/// </summary>
public class MenuScreens : MonoBehaviour
{
    public enum Screen { None, Main, NewGame, Pause, Options, Controls, Credits, Confirm, GameOver, NameEntry }

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
                KeyBindings.Bind(captureAction.Value, captureSecondary, key);
                KeyBindings.Save();
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
        MenuBuilder.MenuButton(col.transform, "CREDITS", () => Show(Screen.Credits));
        MenuBuilder.MenuButton(col.transform, "QUIT", () =>
            AskConfirm("Quit to desktop?", MenuFlow.QuitGame), textColor: MenuStyle.TextDanger);

        MenuBuilder.Spacer(col.transform, 8f);
        MenuBuilder.Label(col.transform, "v0.1 · pre-alpha", MenuStyle.SmallSize, MenuStyle.TextMuted)
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
        BaseBuilding fire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
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

        MenuBuilder.StepperRow(t, "Quality", QualitySettings.names, GameSettings.QualityLevel,
            i => { GameSettings.QualityLevel = i; GameSettings.Apply(); },
            "Shadow and texture detail. Lower it first if the game runs rough.");

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

        TextMeshProUGUI title = MenuBuilder.Label(col.transform,
            gameOverVictory ? "VICTORY" : "DEFEAT", MenuStyle.TitleSize - 10f, accent);
        title.characterSpacing = 8f;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        MenuBuilder.Label(col.transform,
            gameOverVictory ? "The rescue ship has arrived." : "Your camp was overrun.",
            MenuStyle.BodySize, MenuStyle.TextMuted)
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

        ResourceManager rm = ResourceManager.Instance;
        if (rm != null)
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

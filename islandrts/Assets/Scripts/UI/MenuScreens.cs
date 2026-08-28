using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Every menu screen, built at runtime. One canvas, one screen visible at a
/// time, a back-stack so Esc/Back always unwinds correctly.
///
/// Screens: Main (title), Pause, Options (4 tabs), Controls, Credits, Confirm.
/// The layouts these produce are documented in docs/MENU_WIREFRAMES.md — keep
/// the two in sync, that file is what the artist works from.
/// </summary>
public class MenuScreens : MonoBehaviour
{
    public enum Screen { None, Main, Pause, Options, Controls, Credits, Confirm }

    private static MenuScreens instance;
    public static MenuScreens Instance => instance;

    /// <summary>True whenever any menu screen is showing.</summary>
    public static bool AnyOpen => instance != null && instance.current != Screen.None;

    private Canvas canvas;
    private RectTransform backdrop;
    private RectTransform panel;
    private Screen current = Screen.None;
    private readonly List<Screen> backStack = new List<Screen>();

    private string confirmMessage;
    private Action confirmAction;
    private int optionsTab;

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

    // ---- navigation -------------------------------------------------------

    public void Show(Screen screen, bool pushHistory = true)
    {
        if (pushHistory && current != Screen.None && current != screen) backStack.Add(current);
        current = screen;
        Rebuild();
    }

    /// <summary>Back one level; closes the menu entirely when the stack is empty.</summary>
    public void Back()
    {
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
        current = Screen.None;
        Rebuild();
        PauseController.SetPaused(false);
    }

    private void Rebuild()
    {
        if (panel != null) Destroy(panel.gameObject);
        panel = null;

        bool open = current != Screen.None;
        backdrop.gameObject.SetActive(open);
        if (!open) return;

        switch (current)
        {
            case Screen.Main: BuildMain(); break;
            case Screen.Pause: BuildPause(); break;
            case Screen.Options: BuildOptions(); break;
            case Screen.Controls: BuildControls(); break;
            case Screen.Credits: BuildCredits(); break;
            case Screen.Confirm: BuildConfirm(); break;
        }
    }

    // ---- screens ----------------------------------------------------------

    private void BuildMain()
    {
        panel = MenuBuilder.Panel(canvas.transform, "MainMenu", MenuStyle.MenuWidth, 620f);
        VerticalLayoutGroup col = MenuBuilder.Column(panel, MenuStyle.ButtonSpacing);

        MenuBuilder.Label(col.transform, "CASTAWAY", MenuStyle.TitleSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 66f;
        MenuBuilder.Label(col.transform, "COLONY", MenuStyle.TitleSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 66f;
        MenuBuilder.Label(col.transform, "survive five nights", MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        MenuBuilder.Spacer(col.transform, 22f);

        MenuBuilder.MenuButton(col.transform, "NEW GAME", () => MenuFlow.NewGame());
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

    private void BuildPause()
    {
        panel = MenuBuilder.Panel(canvas.transform, "PauseMenu", MenuStyle.MenuWidth, 520f);
        VerticalLayoutGroup col = MenuBuilder.Column(panel, MenuStyle.ButtonSpacing);

        MenuBuilder.Label(col.transform, "PAUSED", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        MenuBuilder.Label(col.transform, StatusLine(), MenuStyle.SmallSize, MenuStyle.TextMuted)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

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
        panel = MenuBuilder.Panel(canvas.transform, "Options", MenuStyle.OptionsWidth, 640f);
        VerticalLayoutGroup col = MenuBuilder.Column(panel, 8f);

        MenuBuilder.Label(col.transform, "OPTIONS", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        BuildTabs(col.transform, new[] { "AUDIO", "VIDEO", "GAMEPLAY" }, optionsTab, i =>
        {
            optionsTab = i;
            Rebuild();
        });

        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 6f);

        switch (optionsTab)
        {
            case 0:
                MenuBuilder.SliderRow(col.transform, "Master volume", GameSettings.MasterVolume,
                    v => { GameSettings.MasterVolume = v; GameSettings.Apply(); });
                MenuBuilder.SliderRow(col.transform, "Music", GameSettings.MusicVolume,
                    v => { GameSettings.MusicVolume = v; GameSettings.Apply(); });
                MenuBuilder.SliderRow(col.transform, "Sound effects", GameSettings.SfxVolume,
                    v => { GameSettings.SfxVolume = v; GameSettings.Apply(); });
                MenuBuilder.SliderRow(col.transform, "Ambience", GameSettings.AmbientVolume,
                    v => { GameSettings.AmbientVolume = v; GameSettings.Apply(); });
                break;

            case 1:
                MenuBuilder.ToggleRow(col.transform, "Fullscreen", GameSettings.Fullscreen,
                    v => { GameSettings.Fullscreen = v; GameSettings.Apply(); });
                MenuBuilder.ToggleRow(col.transform, "V-Sync", GameSettings.VSync,
                    v => { GameSettings.VSync = v; GameSettings.Apply(); });
                MenuBuilder.StepperRow(col.transform, "Quality", QualitySettings.names,
                    GameSettings.QualityLevel,
                    i => { GameSettings.QualityLevel = i; GameSettings.Apply(); });
                MenuBuilder.Spacer(col.transform, 6f);
                MenuBuilder.Label(col.transform,
                    "Resolution is a placeholder — needs a proper mode list before ship.",
                    MenuStyle.SmallSize, MenuStyle.TextMuted).gameObject
                    .AddComponent<LayoutElement>().preferredHeight = 22f;
                break;

            default:
                MenuBuilder.SliderRow(col.transform, "Camera speed", GameSettings.CameraSpeed * 0.5f,
                    v => { GameSettings.CameraSpeed = Mathf.Max(0.1f, v * 2f); GameSettings.Apply(); });
                MenuBuilder.ToggleRow(col.transform, "Edge pan", GameSettings.EdgePan,
                    v => { GameSettings.EdgePan = v; GameSettings.Apply(); });
                MenuBuilder.ToggleRow(col.transform, "Screen shake", GameSettings.ScreenShake,
                    v => { GameSettings.ScreenShake = v; GameSettings.Apply(); });
                MenuBuilder.ToggleRow(col.transform, "Damage numbers", GameSettings.DamageNumbers,
                    v => { GameSettings.DamageNumbers = v; GameSettings.Apply(); });
                MenuBuilder.ToggleRow(col.transform, "Show build grid by default", GameSettings.GridByDefault,
                    v => { GameSettings.GridByDefault = v; GameSettings.Apply(); });
                break;
        }

        MenuBuilder.Spacer(col.transform, 14f);
        MenuBuilder.MenuButton(col.transform, "RESET TO DEFAULTS", () =>
            AskConfirm("Reset all settings?", () => { GameSettings.ResetToDefaults(); Back(); }));
        MenuBuilder.MenuButton(col.transform, "BACK", () => { GameSettings.Save(); Back(); });
    }

    private void BuildTabs(Transform parent, string[] names, int active, Action<int> onPick)
    {
        GameObject row = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 44f;

        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = true;
        h.childForceExpandWidth = true;
        h.childControlHeight = true;
        h.childForceExpandHeight = true;

        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            Button b = MenuBuilder.MenuButton(row.transform, names[i], () => onPick(idx));
            b.GetComponent<LayoutElement>().preferredHeight = 40f;
            if (i == active) b.targetGraphic.color = MenuStyle.ButtonPressed;
        }
    }

    private void BuildControls()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Controls", MenuStyle.OptionsWidth, 640f);
        VerticalLayoutGroup col = MenuBuilder.Column(panel, 4f);

        MenuBuilder.Label(col.transform, "CONTROLS", MenuStyle.HeadingSize, MenuStyle.TextAccent)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        MenuBuilder.Divider(col.transform);
        MenuBuilder.Spacer(col.transform, 6f);

        // Read-only for now. Rebinding is a real feature, not a wireframe one —
        // it needs an input-map rewrite, so the artist should design this as a
        // list that will eventually gain clickable binding fields.
        string[,] bindings =
        {
            { "Pan camera", "W A S D  /  Arrows" },
            { "Rotate camera", "Q  /  E" },
            { "Zoom", "Mouse wheel" },
            { "Tilt / orbit", "Middle mouse drag" },
            { "Build mode", "B" },
            { "Select building", "1 - 5" },
            { "Wall to gate", "G" },
            { "Rotate / path flip", "R" },
            { "Staircase walls", "Shift" },
            { "Demolish", "Delete  /  X" },
            { "Build grid", "F2" },
            { "Pause menu", "Esc" },
        };

        for (int i = 0; i < bindings.GetLength(0); i++)
        {
            MenuBuilder.SettingRow(col.transform, bindings[i, 0], out RectTransform slot);
            TextMeshProUGUI v = MenuBuilder.Label(slot, bindings[i, 1], MenuStyle.BodySize,
                MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
            RectTransform vrt = v.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
        }

        MenuBuilder.Spacer(col.transform, 10f);
        MenuBuilder.Label(col.transform, "Rebinding is not implemented yet.",
            MenuStyle.SmallSize, MenuStyle.TextMuted).gameObject
            .AddComponent<LayoutElement>().preferredHeight = 22f;
        MenuBuilder.MenuButton(col.transform, "BACK", () => Back());
    }

    private void BuildCredits()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Credits", MenuStyle.MenuWidth + 120f, 520f);
        VerticalLayoutGroup col = MenuBuilder.Column(panel, 8f);

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

    private void AskConfirm(string message, Action action)
    {
        confirmMessage = message;
        confirmAction = action;
        Show(Screen.Confirm);
    }

    private void BuildConfirm()
    {
        panel = MenuBuilder.Panel(canvas.transform, "Confirm", MenuStyle.MenuWidth, 260f);
        VerticalLayoutGroup col = MenuBuilder.Column(panel, MenuStyle.ButtonSpacing);

        MenuBuilder.Spacer(col.transform, 12f);
        MenuBuilder.Label(col.transform, confirmMessage, MenuStyle.BodySize, MenuStyle.TextPrimary)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
        MenuBuilder.Spacer(col.transform, 8f);

        Action act = confirmAction;
        MenuBuilder.MenuButton(col.transform, "CONFIRM", () => act?.Invoke(), textColor: MenuStyle.TextDanger);
        MenuBuilder.MenuButton(col.transform, "CANCEL", () => Back());
    }
}

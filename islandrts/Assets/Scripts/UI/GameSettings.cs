using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-facing settings, persisted to PlayerPrefs and applied to the systems
/// that own them.
///
/// Deliberately thin: each setting has a default, a PlayerPrefs key, and a way
/// of reaching the thing it affects. The Options screen never touches
/// AudioManager or QualitySettings directly — it edits values here and calls
/// <see cref="Apply"/>, so settings behave identically whether they were
/// changed from the main menu or the in-game pause menu.
///
/// Two things deliberately live elsewhere:
/// <see cref="KeyBindings"/> owns the keyboard map, and <see cref="Difficulty"/>
/// owns the run rules — a difficulty is not a preference, and
/// <see cref="ResetToDefaults"/> must never silently re-roll the rules of a game
/// in progress.
/// </summary>
public static class GameSettings
{
    // ---- keys -------------------------------------------------------------

    private const string KeyMaster = "opt.masterVolume";
    private const string KeyMusic = "opt.musicVolume";
    private const string KeySfx = "opt.sfxVolume";
    private const string KeyAmbient = "opt.ambientVolume";
    private const string KeyMuteUnfocused = "opt.muteUnfocused";

    private const string KeyFullscreen = "opt.fullscreen";      // legacy bool, migrated into KeyDisplayMode
    private const string KeyDisplayMode = "opt.displayMode";
    private const string KeyResolution = "opt.resolution";
    private const string KeyQuality = "opt.quality";
    private const string KeyVSync = "opt.vsync";
    private const string KeyFrameCap = "opt.frameCap";

    private const string KeyCameraSpeed = "opt.cameraSpeed";
    private const string KeyZoomSpeed = "opt.zoomSpeed";
    private const string KeyRotateSpeed = "opt.rotateSpeed";
    private const string KeyEdgePan = "opt.edgePan";
    private const string KeyInvertTilt = "opt.invertTilt";
    private const string KeyShakeStrength = "opt.shakeStrength";

    private const string KeyUIScale = "opt.uiScale";
    private const string KeyDamageNumbers = "opt.damageNumbers";
    private const string KeyHealthBars = "opt.healthBars";
    private const string KeyStateText = "opt.stateText";
    private const string KeyGridDefault = "opt.gridDefault";
    private const string KeyPauseOnFocusLoss = "opt.pauseOnFocusLoss";

    // ---- audio ------------------------------------------------------------

    public static float MasterVolume = 1f;
    public static float MusicVolume = 0.7f;
    public static float SfxVolume = 1f;
    public static float AmbientVolume = 0.5f;
    /// <summary>Silence the game while the window is in the background.</summary>
    public static bool MuteWhenUnfocused = true;

    // ---- video ------------------------------------------------------------

    public enum Display { Fullscreen, Borderless, Windowed }

    public static Display DisplayMode = Display.Fullscreen;
    /// <summary>Index into <see cref="ResolutionOptions"/>; -1 means "whatever the desktop is".</summary>
    public static int ResolutionIndex = -1;
    public static int QualityLevel = 2;
    public static bool VSync = true;
    /// <summary>0 = uncapped. Ignored while V-Sync is on, which is why the row greys out.</summary>
    public static int FrameCap = 0;

    public static readonly int[] FrameCapChoices = { 0, 30, 60, 120, 144, 240 };

    // ---- camera -----------------------------------------------------------

    public static float CameraSpeed = 1f;        // multiplier on pan speed
    public static float ZoomSpeed = 1f;          // multiplier on scroll zoom
    public static float RotationSpeed = 1f;      // multiplier on Q/E rotation
    public static bool EdgePan = false;
    /// <summary>Flip the vertical axis of middle-mouse free-look tilt.</summary>
    public static bool InvertTilt = false;
    /// <summary>0 disables shake entirely; 1 is the tuned default.</summary>
    public static float ScreenShakeStrength = 1f;

    // ---- interface --------------------------------------------------------

    public enum HealthBars { Always, WhenDamaged, Never }

    /// <summary>Multiplier on the menu canvas's reference resolution.</summary>
    public static float UIScale = 1f;
    public static bool DamageNumbers = true;
    public static HealthBars HealthBarMode = HealthBars.WhenDamaged;
    /// <summary>The floating "Gathering" / "Attacking" labels over units.</summary>
    public static bool UnitStateText = false;
    public static bool GridByDefault = false;
    /// <summary>Open the pause menu when the window loses focus.</summary>
    public static bool PauseOnFocusLoss = false;

    private static bool loaded;

    // ---- resolutions ------------------------------------------------------

    private static string[] resolutionNames;
    private static Resolution[] resolutionList;

    /// <summary>
    /// Distinct width×height modes the display supports, largest first.
    ///
    /// Unity reports one entry per refresh rate, so a 165 Hz monitor lists
    /// 1920×1080 four or five times over. Collapsing by size and keeping the
    /// highest refresh rate for each is what turns that into a list a player
    /// can actually read. Built once — Screen.resolutions allocates.
    /// </summary>
    public static string[] ResolutionOptions
    {
        get { EnsureResolutions(); return resolutionNames; }
    }

    private static void EnsureResolutions()
    {
        if (resolutionNames != null) return;

        var best = new Dictionary<long, Resolution>();
        var order = new List<long>();

        Resolution[] all = Screen.resolutions;
        for (int i = 0; i < all.Length; i++)
        {
            Resolution r = all[i];
            long key = ((long)r.width << 20) | (uint)r.height;
            if (!best.ContainsKey(key)) { best[key] = r; order.Add(key); }
            else if (RefreshHz(r) > RefreshHz(best[key])) best[key] = r;
        }

        // Screen.resolutions comes back smallest-first; a player looking for
        // their native mode wants it at the top.
        order.Sort((a, b) => b.CompareTo(a));

        resolutionList = new Resolution[order.Count];
        resolutionNames = new string[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            Resolution r = best[order[i]];
            resolutionList[i] = r;
            resolutionNames[i] = r.width + " x " + r.height;
        }

        // A machine with no reported modes (a headless build, some editors)
        // must still produce a one-entry list rather than an empty stepper.
        if (resolutionList.Length == 0)
        {
            resolutionList = new[] { Screen.currentResolution };
            resolutionNames = new[] { Screen.currentResolution.width + " x " + Screen.currentResolution.height };
        }
    }

    private static double RefreshHz(Resolution r)
    {
        // refreshRateRatio replaced the deprecated int refreshRate in Unity 6.
        return r.refreshRateRatio.value;
    }

    /// <summary>The resolution index matching the current window, for first-run display.</summary>
    public static int CurrentResolutionIndex()
    {
        EnsureResolutions();
        if (ResolutionIndex >= 0 && ResolutionIndex < resolutionList.Length) return ResolutionIndex;

        for (int i = 0; i < resolutionList.Length; i++)
        {
            if (resolutionList[i].width == Screen.width && resolutionList[i].height == Screen.height) return i;
        }
        return 0;
    }

    // ---- load / save ------------------------------------------------------

    public static void Load()
    {
        if (loaded) return;
        loaded = true;

        MasterVolume = PlayerPrefs.GetFloat(KeyMaster, MasterVolume);
        MusicVolume = PlayerPrefs.GetFloat(KeyMusic, MusicVolume);
        SfxVolume = PlayerPrefs.GetFloat(KeySfx, SfxVolume);
        AmbientVolume = PlayerPrefs.GetFloat(KeyAmbient, AmbientVolume);
        MuteWhenUnfocused = GetBool(KeyMuteUnfocused, MuteWhenUnfocused);

        // Display mode used to be a plain fullscreen bool. Carry an existing
        // player's choice across rather than resetting them to fullscreen.
        int legacyFullscreen = PlayerPrefs.GetInt(KeyFullscreen, -1);
        int defaultMode = legacyFullscreen == 0 ? (int)Display.Windowed : (int)DisplayMode;
        DisplayMode = (Display)PlayerPrefs.GetInt(KeyDisplayMode, defaultMode);

        ResolutionIndex = PlayerPrefs.GetInt(KeyResolution, -1);
        QualityLevel = PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel());
        VSync = GetBool(KeyVSync, VSync);
        FrameCap = PlayerPrefs.GetInt(KeyFrameCap, FrameCap);

        CameraSpeed = PlayerPrefs.GetFloat(KeyCameraSpeed, CameraSpeed);
        ZoomSpeed = PlayerPrefs.GetFloat(KeyZoomSpeed, ZoomSpeed);
        RotationSpeed = PlayerPrefs.GetFloat(KeyRotateSpeed, RotationSpeed);
        EdgePan = GetBool(KeyEdgePan, EdgePan);
        InvertTilt = GetBool(KeyInvertTilt, InvertTilt);
        ScreenShakeStrength = PlayerPrefs.GetFloat(KeyShakeStrength, ScreenShakeStrength);

        UIScale = PlayerPrefs.GetFloat(KeyUIScale, UIScale);
        DamageNumbers = GetBool(KeyDamageNumbers, DamageNumbers);
        HealthBarMode = (HealthBars)PlayerPrefs.GetInt(KeyHealthBars, (int)HealthBarMode);
        UnitStateText = GetBool(KeyStateText, UnitStateText);
        GridByDefault = GetBool(KeyGridDefault, GridByDefault);
        PauseOnFocusLoss = GetBool(KeyPauseOnFocusLoss, PauseOnFocusLoss);

        KeyBindings.Load();
        Difficulty.Load();
        IslandOptions.Load();

        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeyMaster, MasterVolume);
        PlayerPrefs.SetFloat(KeyMusic, MusicVolume);
        PlayerPrefs.SetFloat(KeySfx, SfxVolume);
        PlayerPrefs.SetFloat(KeyAmbient, AmbientVolume);
        SetBool(KeyMuteUnfocused, MuteWhenUnfocused);

        PlayerPrefs.SetInt(KeyDisplayMode, (int)DisplayMode);
        PlayerPrefs.SetInt(KeyResolution, ResolutionIndex);
        PlayerPrefs.SetInt(KeyQuality, QualityLevel);
        SetBool(KeyVSync, VSync);
        PlayerPrefs.SetInt(KeyFrameCap, FrameCap);

        PlayerPrefs.SetFloat(KeyCameraSpeed, CameraSpeed);
        PlayerPrefs.SetFloat(KeyZoomSpeed, ZoomSpeed);
        PlayerPrefs.SetFloat(KeyRotateSpeed, RotationSpeed);
        SetBool(KeyEdgePan, EdgePan);
        SetBool(KeyInvertTilt, InvertTilt);
        PlayerPrefs.SetFloat(KeyShakeStrength, ScreenShakeStrength);

        PlayerPrefs.SetFloat(KeyUIScale, UIScale);
        SetBool(KeyDamageNumbers, DamageNumbers);
        PlayerPrefs.SetInt(KeyHealthBars, (int)HealthBarMode);
        SetBool(KeyStateText, UnitStateText);
        SetBool(KeyGridDefault, GridByDefault);
        SetBool(KeyPauseOnFocusLoss, PauseOnFocusLoss);

        KeyBindings.Save();
        Difficulty.Save();

        PlayerPrefs.Save();
    }

    private static bool GetBool(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
    private static void SetBool(string key, bool value) => PlayerPrefs.SetInt(key, value ? 1 : 0);

    // ---- applying ---------------------------------------------------------

    /// <summary>
    /// Pushes the current values into the live systems. Safe to call any time.
    ///
    /// Not every setting is pushed from here: camera speeds, edge pan, tilt
    /// inversion, shake strength, health-bar mode, unit state text and the
    /// build-grid default are read at their point of effect instead
    /// (CameraController, CameraShake, HealthBar, UnitBase, GridToggleHotkey),
    /// the same pattern CraftedUpgrades uses. That keeps them correct across a
    /// scene load without this having to find the new scene's components.
    ///
    /// This runs on every frame of a slider drag, so anything expensive in here
    /// must be guarded by an actual-change check.
    /// </summary>
    public static void Apply()
    {
        AudioListener.volume = MasterVolume;

        AudioManager am = AudioManager.Instance;
        if (am != null)
        {
            am.masterVolume = MasterVolume;
            am.musicVolume = MusicVolume;
            am.sfxVolume = SfxVolume;
            am.ambientVolume = AmbientVolume;
        }

        // A sim run mutes itself; never let a saved setting un-mute it.
        if (SimHooks.Simulating) AudioListener.volume = 0f;

        // SetQualityLevel with applyExpensiveChanges rebuilds render state, and
        // doing that per frame visibly hitches the game behind the options
        // screen. Only touch these when the value actually changed.
        int quality = Mathf.Clamp(QualityLevel, 0, QualitySettings.names.Length - 1);
        if (QualitySettings.GetQualityLevel() != quality) QualitySettings.SetQualityLevel(quality, true);

        int vsync = VSync ? 1 : 0;
        if (QualitySettings.vSyncCount != vsync) QualitySettings.vSyncCount = vsync;

        // targetFrameRate is ignored while V-Sync is on (the swap interval wins),
        // so uncap it in that case rather than leaving a stale cap that would
        // silently take effect the moment V-Sync is turned off.
        int target = VSync || FrameCap <= 0 ? -1 : FrameCap;
        if (Application.targetFrameRate != target) Application.targetFrameRate = target;

        ApplyDisplayMode();

        CombatEffects fx = CombatEffects.Instance;
        if (fx != null) fx.showDamageNumbers = DamageNumbers && !SimHooks.Simulating;

        MenuScaler.Apply(UIScale);
    }

    /// <summary>
    /// Resolution and window mode in one call, because Unity treats them as one
    /// state — setting a mode without a size snaps the window to the desktop
    /// resolution and loses the player's choice.
    ///
    /// Skipped in the editor: Screen.SetResolution cannot resize the Game view,
    /// and calling it there logs warnings while doing nothing useful.
    /// </summary>
    private static void ApplyDisplayMode()
    {
        if (Application.isEditor) return;

        EnsureResolutions();
        int idx = Mathf.Clamp(CurrentResolutionIndex(), 0, resolutionList.Length - 1);
        Resolution r = resolutionList[idx];

        FullScreenMode mode;
        switch (DisplayMode)
        {
            case Display.Borderless: mode = FullScreenMode.FullScreenWindow; break;
            case Display.Windowed: mode = FullScreenMode.Windowed; break;
            default: mode = FullScreenMode.ExclusiveFullScreen; break;
        }

        bool sizeChanged = Screen.width != r.width || Screen.height != r.height;
        if (!sizeChanged && Screen.fullScreenMode == mode) return;

        Screen.SetResolution(r.width, r.height, mode);
    }

    public static void ResetToDefaults()
    {
        MasterVolume = 1f; MusicVolume = 0.7f; SfxVolume = 1f; AmbientVolume = 0.5f;
        MuteWhenUnfocused = true;

        DisplayMode = Display.Fullscreen; ResolutionIndex = -1;
        QualityLevel = 2; VSync = true; FrameCap = 0;

        CameraSpeed = 1f; ZoomSpeed = 1f; RotationSpeed = 1f;
        EdgePan = false; InvertTilt = false; ScreenShakeStrength = 1f;

        UIScale = 1f; DamageNumbers = true; HealthBarMode = HealthBars.WhenDamaged;
        UnitStateText = false; GridByDefault = false; PauseOnFocusLoss = false;

        KeyBindings.ResetToDefaults();

        Apply();
        Save();
    }
}

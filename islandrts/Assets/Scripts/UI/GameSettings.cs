using UnityEngine;

/// <summary>
/// Player-facing settings, persisted to PlayerPrefs and applied to the systems
/// that own them.
///
/// Deliberately thin: each setting has a default, a PlayerPrefs key, and an
/// Apply that pushes it somewhere real. The Options screen never touches
/// AudioManager or QualitySettings directly — it edits values here and calls
/// <see cref="Apply"/>, so settings behave identically whether they were
/// changed from the main menu or the in-game pause menu.
/// </summary>
public static class GameSettings
{
    private const string KeyMaster = "opt.masterVolume";
    private const string KeyMusic = "opt.musicVolume";
    private const string KeySfx = "opt.sfxVolume";
    private const string KeyAmbient = "opt.ambientVolume";
    private const string KeyFullscreen = "opt.fullscreen";
    private const string KeyQuality = "opt.quality";
    private const string KeyVSync = "opt.vsync";
    private const string KeyCameraSpeed = "opt.cameraSpeed";
    private const string KeyEdgePan = "opt.edgePan";
    private const string KeyScreenShake = "opt.screenShake";
    private const string KeyDamageNumbers = "opt.damageNumbers";
    private const string KeyGridDefault = "opt.gridDefault";

    public static float MasterVolume = 1f;
    public static float MusicVolume = 0.7f;
    public static float SfxVolume = 1f;
    public static float AmbientVolume = 0.5f;

    public static bool Fullscreen = true;
    public static int QualityLevel = 2;
    public static bool VSync = true;

    public static float CameraSpeed = 1f;      // multiplier on pan speed
    public static bool EdgePan = false;
    public static bool ScreenShake = true;
    public static bool DamageNumbers = true;
    public static bool GridByDefault = false;

    private static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;

        MasterVolume = PlayerPrefs.GetFloat(KeyMaster, MasterVolume);
        MusicVolume = PlayerPrefs.GetFloat(KeyMusic, MusicVolume);
        SfxVolume = PlayerPrefs.GetFloat(KeySfx, SfxVolume);
        AmbientVolume = PlayerPrefs.GetFloat(KeyAmbient, AmbientVolume);

        Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, Fullscreen ? 1 : 0) == 1;
        QualityLevel = PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel());
        VSync = PlayerPrefs.GetInt(KeyVSync, VSync ? 1 : 0) == 1;

        CameraSpeed = PlayerPrefs.GetFloat(KeyCameraSpeed, CameraSpeed);
        EdgePan = PlayerPrefs.GetInt(KeyEdgePan, EdgePan ? 1 : 0) == 1;
        ScreenShake = PlayerPrefs.GetInt(KeyScreenShake, ScreenShake ? 1 : 0) == 1;
        DamageNumbers = PlayerPrefs.GetInt(KeyDamageNumbers, DamageNumbers ? 1 : 0) == 1;
        GridByDefault = PlayerPrefs.GetInt(KeyGridDefault, GridByDefault ? 1 : 0) == 1;

        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeyMaster, MasterVolume);
        PlayerPrefs.SetFloat(KeyMusic, MusicVolume);
        PlayerPrefs.SetFloat(KeySfx, SfxVolume);
        PlayerPrefs.SetFloat(KeyAmbient, AmbientVolume);

        PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(KeyQuality, QualityLevel);
        PlayerPrefs.SetInt(KeyVSync, VSync ? 1 : 0);

        PlayerPrefs.SetFloat(KeyCameraSpeed, CameraSpeed);
        PlayerPrefs.SetInt(KeyEdgePan, EdgePan ? 1 : 0);
        PlayerPrefs.SetInt(KeyScreenShake, ScreenShake ? 1 : 0);
        PlayerPrefs.SetInt(KeyDamageNumbers, DamageNumbers ? 1 : 0);
        PlayerPrefs.SetInt(KeyGridDefault, GridByDefault ? 1 : 0);

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Pushes the current values into the live systems. Safe to call any time.
    ///
    /// Note that not every setting is pushed from here: camera speed, edge pan,
    /// screen shake and the build-grid default are read at their point of
    /// effect instead (CameraController, CameraShake, GridToggleHotkey), the
    /// same pattern CraftedUpgrades uses. That keeps them correct across a
    /// scene load without this having to find the new scene's components.
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

        // Apply() runs on every frame of a slider drag, and SetQualityLevel
        // with applyExpensiveChanges rebuilds render state — doing that per
        // frame visibly hitches the game behind the options screen. Only touch
        // these when the value actually changed.
        int quality = Mathf.Clamp(QualityLevel, 0, QualitySettings.names.Length - 1);
        if (QualitySettings.GetQualityLevel() != quality) QualitySettings.SetQualityLevel(quality, true);

        int vsync = VSync ? 1 : 0;
        if (QualitySettings.vSyncCount != vsync) QualitySettings.vSyncCount = vsync;

        if (Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;

        CombatEffects fx = CombatEffects.Instance;
        if (fx != null) fx.showDamageNumbers = DamageNumbers && !SimHooks.Simulating;
    }

    public static void ResetToDefaults()
    {
        MasterVolume = 1f; MusicVolume = 0.7f; SfxVolume = 1f; AmbientVolume = 0.5f;
        Fullscreen = true; QualityLevel = 2; VSync = true;
        CameraSpeed = 1f; EdgePan = false; ScreenShake = true;
        DamageNumbers = true; GridByDefault = false;
        Apply();
        Save();
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene transitions the menus trigger. Kept apart from the screens so the
/// button wiring stays declarative and the risky part — loading scenes in a
/// project that deliberately has no DontDestroyOnLoad singletons — lives in one
/// reviewable place.
/// </summary>
public static class MenuFlow
{
    public const string MenuSceneName = "MainMenu";
    public const string GameSceneName = "MainIsland";

    /// <summary>
    /// Starts a run under the currently selected difficulty. The snapshot is
    /// taken here, before the scene loads, because the rules must be in force by
    /// the time the new scene's Awakes run — ResourceManager reads the starting
    /// resource multiplier in its own Awake.
    /// </summary>
    public static void NewGame()
    {
        Difficulty.BeginRun();
        LoadScene(GameSceneName);
    }

    /// <summary>
    /// Replays the current scene under the SAME rules — deliberately no
    /// BeginRun call, so a restart cannot quietly pick up a difficulty the
    /// player changed on the menu since this run started.
    /// </summary>
    public static void Restart()
    {
        LoadScene(SceneManager.GetActiveScene().name);
    }

    public static void ToMainMenu()
    {
        LoadScene(MenuSceneName);
    }

    /// <summary>
    /// Always unpause before loading. Time.timeScale is global and survives a
    /// scene load, so leaving it at 0 would hand the next scene a frozen clock
    /// — the exact failure that stalled the balance sim for an hour.
    /// </summary>
    private static void LoadScene(string name)
    {
        PauseController.SetPaused(false);
        Time.timeScale = 1f;

        if (MenuScreens.Instance != null) MenuScreens.Instance.Close();
        GameSettings.Save();

        SceneManager.LoadScene(name, LoadSceneMode.Single);
    }

    public static void QuitGame()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

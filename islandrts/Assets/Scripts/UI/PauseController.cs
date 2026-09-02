using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns Escape and the paused state.
///
/// Escape is already claimed by five systems (build ghost, wall line, demolish,
/// crafting panel, opening-sequence campfire placement), so this deliberately
/// does NOT grab it globally. It opens the pause menu only when nothing else
/// wants to consume the key — "Esc backs out one level, and the last level is
/// the pause menu". <see cref="ModeActive"/> is that check; anything new that
/// binds Escape must be added to it.
///
/// Self-bootstraps, so there is nothing to wire in a scene.
/// </summary>
[DefaultExecutionOrder(-50)]
public class PauseController : MonoBehaviour
{
    private static PauseController instance;

    /// <summary>True while the game is paused by the menu (not by game-over).</summary>
    public static bool IsPaused { get; private set; }

    /// <summary>
    /// True when gameplay input should be ignored — paused, or any menu open.
    /// Input-driven Updates check this; they still run, they just do nothing.
    /// </summary>
    public static bool BlockGameplayInput => IsPaused || MenuScreens.AnyOpen;

    private float resumeTimeScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // A headless balance run has no player and must never be paused.
        if (SimHooks.Simulating) return;

        // RuntimeInitializeOnLoadMethod fires ONCE per launch, not once per
        // scene load, and this object deliberately has no DontDestroyOnLoad
        // (see the no-stale-singletons rule). So without this subscription the
        // controller died with the menu scene the moment NEW GAME was pressed
        // and Escape did nothing for the rest of the session.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateForActiveScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (SimHooks.Simulating) return;
        if (mode != LoadSceneMode.Single) return;
        CreateForActiveScene();
    }

    private static void CreateForActiveScene()
    {
        if (instance != null) return;

        GameObject go = new GameObject("~PauseController");
        instance = go.AddComponent<PauseController>();
        MenuScreens.Ensure().transform.SetParent(go.transform, false);

        // GameSettings.Load() only applies once; re-push the values so the
        // freshly loaded scene's AudioManager / CombatEffects get them too.
        GameSettings.Apply();

        // The menu scene shows its own title screen; the game scene starts unpaused.
        if (SceneManager.GetActiveScene().name == MenuFlow.MenuSceneName)
        {
            MenuScreens.Instance.Show(MenuScreens.Screen.Main);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            IsPaused = false;
        }
    }

    /// <summary>
    /// Window focus changes drive two Options settings.
    ///
    /// Muting is done on AudioListener.volume rather than by pausing the audio
    /// sources, so music and ambience keep their positions and come back
    /// mid-track instead of restarting. GameSettings.Apply is what restores the
    /// player's real volume, which also means a volume changed while unfocused
    /// (impossible today, but true of any future overlay) lands correctly.
    /// </summary>
    private void OnApplicationFocus(bool focused)
    {
        if (SimHooks.Simulating) return;

        if (GameSettings.MuteWhenUnfocused)
        {
            if (focused) GameSettings.Apply();
            else AudioListener.volume = 0f;
        }

        if (!focused && GameSettings.PauseOnFocusLoss) TryOpenPauseMenu();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        // A menu is open: let it handle Back itself. In the menu scene Escape
        // still unwinds sub-screens (Options, Credits, a confirm dialog) but
        // never closes the title screen — there is nothing behind it.
        if (MenuScreens.AnyOpen)
        {
            // The Controls screen is waiting for a key: Escape means "never
            // mind", not "leave this screen". This component runs at execution
            // order -50, ahead of MenuScreens, so it has to check rather than
            // assume the other Update saw the press first.
            if (MenuScreens.Instance.IsCapturingKey)
            {
                MenuScreens.Instance.CancelCapture();
                return;
            }

            bool inMenuScene = SceneManager.GetActiveScene().name == MenuFlow.MenuSceneName;
            if (!inMenuScene || MenuScreens.Instance.CanGoBack) MenuScreens.Instance.Back();
            return;
        }

        // Something else owns this Escape press (cancelling a ghost, a wall
        // line, demolish mode, the crafting panel, campfire placement).
        if (ModeActive()) return;

        TryOpenPauseMenu();
    }

    /// <summary>
    /// Opens the pause menu if the game is in a state that can be paused.
    /// Shared by the Escape key and the pause-on-focus-loss setting.
    /// </summary>
    private static void TryOpenPauseMenu()
    {
        if (MenuScreens.AnyOpen) return;

        // The menu scene is already showing its title screen; there is nothing
        // to pause behind it.
        if (SceneManager.GetActiveScene().name == MenuFlow.MenuSceneName) return;

        // Don't pause over the victory/defeat screen — it owns timeScale.
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;

        SetPaused(true);
        MenuScreens.Ensure().Show(MenuScreens.Screen.Pause);
    }

    /// <summary>
    /// Is another system about to consume this Escape? Update ordering between
    /// MonoBehaviours is undefined, so this asks about state rather than
    /// relying on running first or last.
    /// </summary>
    private static bool ModeActive()
    {
        if (CraftingUI.CurrentWorkshop != null) return true;
        if (WorkerAssignmentUI.IsOpen) return true;
        if (GameStartController.IntroInProgress) return true;

        BuildPlacement bp = FindAnyObjectByType<BuildPlacement>();
        if (bp != null)
        {
            if (bp.isPlacing) return true;
            if (bp.demolishTool != null && bp.demolishTool.IsActive) return true;
        }
        return false;
    }

    public static void SetPaused(bool paused)
    {
        if (instance == null) return;
        if (IsPaused == paused) return;

        // Never fight the game-over pause: GameManager sets timeScale 0 on
        // victory/defeat and restores it itself.
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;

        IsPaused = paused;

        if (paused)
        {
            instance.resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = instance.resumeTimeScale;
        }
    }
}

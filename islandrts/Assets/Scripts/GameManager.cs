using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("Victory Conditions")]
    // Seeded from the run's difficulty in Start (Peaceful..Normal are 5 nights,
    // Hard 7, Brutal 10). The serialized value is only the fallback for a scene
    // played without going through the menu.
    public int nightsToSurvive = 5;

    [Header("Game State")]
    public bool isGameOver = false;
    public bool isVictory = false;

    [Header("References")]
    // The victoryScreen / defeatScreen object references are gone. Both screens
    // are built at runtime by MenuScreens now, on the same widgets as the pause
    // and options menus, so there is nothing to wire in the scene and nothing
    // that can be left unassigned.
    public BaseBuilding campfire;  // Reference to campfire

    [Header("Statistics")]
    public int currentNight = 0;
    public int totalEnemiesKilled = 0;
    public int maxWorkers = 0;
    public int maxWarriors = 0;

    // Singleton pattern
    public static GameManager Instance { get; private set; }

    private DayNightCycle dayNightCycle;
    private EnemySpawner enemySpawner;
    private bool victoryChecked = false;

    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        // The run's difficulty owns the win condition — except during a sweep,
        // which sets nightsToSurvive itself from sceneLoaded (before this Start)
        // and would have it silently clobbered back to 5 here.
        if (!SimHooks.Simulating) nightsToSurvive = Difficulty.NightsToSurvive;

        // Find references
        dayNightCycle = FindAnyObjectByType<DayNightCycle>();
        enemySpawner = FindAnyObjectByType<EnemySpawner>();

        if (campfire == null)
        {
            campfire = FindAnyObjectByType<BaseBuilding>();
        }

        // Subscribe to day/night events to track progress
        DayNightCycle.OnDayStart += OnDayStart;

        Debug.Log($"GameManager: Initialized. Win condition: Survive {nightsToSurvive} nights");
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        DayNightCycle.OnDayStart -= OnDayStart;
    }

    void Update()
    {
        // Track statistics
        if (campfire != null)
        {
            int workers = campfire.GetTotalWorkers();
            int warriors = campfire.GetWarriorCount();

            if (workers > maxWorkers)
                maxWorkers = workers;

            if (warriors > maxWarriors)
                maxWarriors = warriors;
        }

        // Update current night from DayNightCycle
        if (dayNightCycle != null)
        {
            currentNight = dayNightCycle.GetCurrentDay();
        }

        // Check for victory (after surviving the required nights)
        if (!victoryChecked && !isGameOver && currentNight > nightsToSurvive && !dayNightCycle.IsNightTime())
        {
            // Survived the required nights and it's now day - VICTORY!
            TriggerVictory();
            victoryChecked = true;
        }
    }

    void OnDayStart()
    {
        // Each day that starts means we survived another night
        Debug.Log($"GameManager: Survived night {currentNight - 1}. {nightsToSurvive - currentNight + 1} nights remaining to victory");

        // Check if we've survived enough nights
        if (currentNight > nightsToSurvive && !victoryChecked)
        {
            TriggerVictory();
            victoryChecked = true;
        }
    }

    /// <summary>
    /// Called when campfire is destroyed
    /// </summary>
    public void TriggerDefeat()
    {
        if (isGameOver) return;  // Already game over

        Debug.Log("========================================");
        Debug.Log("GAME OVER - DEFEAT!");
        Debug.Log("========================================");

        isGameOver = true;
        isVictory = false;

        // Play defeat sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayDefeat();
        }

        // Pause the game
        Time.timeScale = 0f;

        ShowEndScreen(victory: false);
    }

    /// <summary>
    /// Called when player survives required nights
    /// </summary>
    public void TriggerVictory()
    {
        if (isGameOver) return;  // Already game over

        Debug.Log("========================================");
        Debug.Log("VICTORY - YOU SURVIVED!");
        Debug.Log("========================================");

        isGameOver = true;
        isVictory = true;

        // Play victory sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayVictory();
        }

        // Pause the game
        Time.timeScale = 0f;

        ShowEndScreen(victory: true);
    }

    /// <summary>
    /// Puts up the victory or defeat screen.
    ///
    /// Skipped entirely during a balance sweep: a headless run ends a game every
    /// few seconds, and MenuScreens.Ensure would build a canvas, an EventSystem
    /// and a full screen of TMP labels each time — for a process with no
    /// display, whose only output is a CSV row. It would also block the sim's
    /// own input path through PauseController.BlockGameplayInput.
    /// </summary>
    private void ShowEndScreen(bool victory)
    {
        if (SimHooks.Simulating) return;
        MenuScreens.Ensure().ShowGameOver(victory);
    }

    /// <summary>
    /// Keep playing after a victory (sandbox mode).
    ///
    /// Clearing isGameOver must happen BEFORE the screen closes: closing routes
    /// through PauseController.SetPaused(false), which refuses to touch
    /// timeScale while the game is over — it deliberately never fights the
    /// game-over pause. With the flag still set, the menu would close onto a
    /// permanently frozen game.
    /// </summary>
    public void ContinuePlaying()
    {
        isGameOver = false;
        Time.timeScale = 1f;

        if (MenuScreens.Instance != null) MenuScreens.Instance.Close();
    }

    // Track enemy kills
    public void NotifyEnemyKilled()
    {
        totalEnemiesKilled++;
    }

    // Public getters for UI
    public int GetNightsSurvived()
    {
        return currentNight;
    }

    public int GetEnemiesKilled()
    {
        return totalEnemiesKilled;
    }
}

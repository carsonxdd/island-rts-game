using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("Victory Conditions")]
    public int nightsToSurvive = 5;  // Win after surviving this many nights

    [Header("Game State")]
    public bool isGameOver = false;
    public bool isVictory = false;

    [Header("References")]
    public GameObject victoryScreen;
    public GameObject defeatScreen;
    public BaseBuilding campfire;  // Reference to campfire

    [Header("Statistics")]
    public int currentNight = 0;
    public int totalEnemiesKilled = 0;
    public int totalResourcesGathered = 0;
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
        // Find references
        dayNightCycle = FindFirstObjectByType<DayNightCycle>();
        enemySpawner = FindFirstObjectByType<EnemySpawner>();

        if (campfire == null)
        {
            campfire = FindFirstObjectByType<BaseBuilding>();
        }

        // Hide victory/defeat screens at start
        if (victoryScreen != null)
            victoryScreen.SetActive(false);

        if (defeatScreen != null)
            defeatScreen.SetActive(false);

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

        // Show defeat screen
        if (defeatScreen != null)
        {
            defeatScreen.SetActive(true);
            UpdateDefeatStats();
        }
        else
        {
            Debug.LogWarning("GameManager: No defeat screen assigned!");
        }
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

        // Show victory screen
        if (victoryScreen != null)
        {
            victoryScreen.SetActive(true);
            UpdateVictoryStats();
        }
        else
        {
            Debug.LogWarning("GameManager: No victory screen assigned!");
        }
    }

    void UpdateDefeatStats()
    {
        // Update defeat screen with statistics
        // This will be connected to UI elements in the defeat screen
        Debug.Log($"Defeat Stats: Survived {currentNight} nights");
    }

    void UpdateVictoryStats()
    {
        // Update victory screen with statistics
        // This will be connected to UI elements in the victory screen
        Debug.Log($"Victory Stats: Survived {nightsToSurvive} nights! Max workers: {maxWorkers}, Max warriors: {maxWarriors}");
    }

    /// <summary>
    /// Restart the game
    /// </summary>
    public void RestartGame()
    {
        Debug.Log("GameManager: Restarting game...");

        // Unpause the game
        Time.timeScale = 1f;

        // Reload the current scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Quit to main menu (or quit application for now)
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("GameManager: Quitting game...");

        // Unpause the game
        Time.timeScale = 1f;

        #if UNITY_EDITOR
        // In editor, stop playing
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        // In build, quit application
        Application.Quit();
        #endif
    }

    /// <summary>
    /// Continue playing after victory (optional sandbox mode)
    /// </summary>
    public void ContinuePlaying()
    {
        Debug.Log("GameManager: Continuing in sandbox mode...");

        // Unpause the game
        Time.timeScale = 1f;

        // Hide victory screen
        if (victoryScreen != null)
            victoryScreen.SetActive(false);

        // Reset game over flag so player can keep playing
        isGameOver = false;
    }

    // Track enemy kills
    public void NotifyEnemyKilled()
    {
        totalEnemiesKilled++;
    }

    // Track resources gathered
    public void NotifyResourceGathered(int amount)
    {
        totalResourcesGathered += amount;
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

    public int GetResourcesGathered()
    {
        return totalResourcesGathered;
    }
}

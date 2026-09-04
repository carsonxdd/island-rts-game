using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the run: tracks the calendar day, decides victory and defeat, and keeps the
/// statistics the end screen reports.
/// </summary>
/// <remarks>
/// Victory is reaching the dawn after day <see cref="daysToSurvive"/> (the rescue ship
/// arrives); defeat is the campfire being destroyed. Raids are not every night any more
/// (see RaidDirector), so the day count — not a wave count — is the run's clock.
/// Both endings freeze the game and hand off to MenuScreens, which builds the end screen -
/// this class owns no UI of its own. The end screen is skipped entirely during a balance
/// sweep, where a run finishes every few seconds and there is nobody to look at it.
/// </remarks>
public class GameManager : MonoBehaviour
{
    [Header("Victory Conditions")]
    // Seeded from the run's difficulty in Start (Normal is 30 days). The serialized
    // value is only the fallback for a scene played without going through the menu.
    // Renamed from nightsToSurvive on 2026-09-02 WITHOUT FormerlySerializedAs on
    // purpose: the scene's old "5" must not carry over as the new day count.
    public int daysToSurvive = 30;

    [Header("Game State")]
    public bool isGameOver = false;
    public bool isVictory = false;
    /// <summary>The run ended by sailing away (2026-09-04, Slice 6): a victory with its own end screen.</summary>
    [System.NonSerialized] public bool isEscape = false;

    /// <summary>
    /// True from "Set sail" until the end screen: gameplay input is blocked and
    /// the clock holds while the ship slides out. Static so PauseController can
    /// read it without a lookup; reset on load like every other static.
    /// </summary>
    public static bool EscapeInProgress { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { EscapeInProgress = false; }

    [Header("References")]
    // The victoryScreen / defeatScreen object references are gone. Both screens
    // are built at runtime by MenuScreens now, on the same widgets as the pause
    // and options menus, so there is nothing to wire in the scene and nothing
    // that can be left unassigned.
    public BaseBuilding campfire;  // Reference to campfire

    [Header("Statistics")]
    public int currentDay = 1;
    public int totalEnemiesKilled = 0;
    public int maxWorkers = 0;
    public int maxWarriors = 0;

    // Singleton pattern
    public static GameManager Instance { get; private set; }

    private DayNightCycle dayNightCycle;
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
        // which sets daysToSurvive itself from sceneLoaded (before this Start)
        // and would have it silently clobbered back here.
        if (!SimHooks.Simulating) daysToSurvive = Difficulty.DaysToSurvive;

        // Find references
        dayNightCycle = FindAnyObjectByType<DayNightCycle>();

        if (campfire == null)
        {
            campfire = FindAnyObjectByType<BaseBuilding>();
        }

        // Subscribe to day/night events to track progress
        DayNightCycle.OnDayStart += OnDayStart;

        Debug.Log($"GameManager: Initialized. Win condition: reach day {daysToSurvive + 1}");
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

        // Mirror the calendar from DayNightCycle
        if (dayNightCycle != null)
        {
            currentDay = dayNightCycle.GetCurrentDay();
        }

        // Victory check here as well as in OnDayStart: the event fires from the
        // cycle's own Update, which may run before this component has mirrored
        // the new day, so whichever sees the crossing first declares it.
        if (!victoryChecked && !isGameOver && dayNightCycle != null
            && currentDay > daysToSurvive && !dayNightCycle.IsNightTime())
        {
            TriggerVictory();
            victoryChecked = true;
        }
    }

    void OnDayStart()
    {
        int day = dayNightCycle != null ? dayNightCycle.GetCurrentDay() : currentDay;
        currentDay = day;

        // One line per dawn — the calendar is the run's clock now
        Debug.Log($"GameManager: Day {day} of {daysToSurvive}.");

        if (day > daysToSurvive && !victoryChecked)
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
    /// Called when the player reaches the dawn after the last required day
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
    /// The colony sails away (2026-09-04, Slice 6). Freezes input and the clock,
    /// slides the ship out from the Shipyard toward open water for a few seconds
    /// with the camera following, then ends the run through the victory path
    /// with <see cref="isEscape"/> set so the end screen says ESCAPED. Under the
    /// balance sim the beat is skipped and the run ends at once.
    /// </summary>
    public void TriggerEscape(Shipyard yard)
    {
        if (isGameOver || EscapeInProgress) return;

        if (SimHooks.Simulating)
        {
            isEscape = true;
            TriggerVictory();
            return;
        }

        EscapeInProgress = true;
        StartCoroutine(DepartureBeat(yard));
    }

    private const float DepartureSeconds = 6f;
    private const float ShipSpeed = 3f;

    private System.Collections.IEnumerator DepartureBeat(Shipyard yard)
    {
        DayNightCycle clock = FindAnyObjectByType<DayNightCycle>();
        if (clock != null) clock.clockPaused = true;

        // The ship starts at the slipway's water edge, on the sea plane, and
        // slides out along the direction the Shipyard found toward open water.
        Vector3 dir = yard != null ? yard.LaunchDirection : Vector3.right;
        Vector3 start = yard != null ? yard.transform.position + dir * 3f : Vector3.zero;
        start.y = 0.05f;

        // The art's bow is +X: rotate so +X points along the launch direction
        GameObject ship = null;
        if (yard != null && yard.shipArtPrefab != null)
            ship = Instantiate(yard.shipArtPrefab, start, Quaternion.FromToRotation(Vector3.right, dir));

        // TriggerVictory plays the fanfare at the end of the beat; the launch is quiet.
        CameraController cam = FindAnyObjectByType<CameraController>();
        float t = 0f;
        while (t < DepartureSeconds)
        {
            t += Time.deltaTime;
            Vector3 pos = start + dir * (ShipSpeed * t);
            pos.y = 0.05f + Mathf.Sin(t * 2.6f) * 0.06f;   // a small bob
            if (ship != null)
            {
                ship.transform.position = pos;
                ship.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir)
                    * Quaternion.Euler(Mathf.Sin(t * 2.6f) * 2f, 0f, Mathf.Cos(t * 1.7f) * 1.5f);
            }
            if (cam != null) cam.CenterOn(pos);
            yield return null;
        }

        isEscape = true;
        TriggerVictory();   // isVictory, timescale 0, the end screen
        EscapeInProgress = false;
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

    /// <summary>
    /// Full days the colony has lived through. On the victory dawn the calendar
    /// reads daysToSurvive + 1, so this is daysToSurvive; on a defeat during day
    /// N it is N - 1 — the day you lost was not survived.
    /// </summary>
    public int GetDaysSurvived()
    {
        return Mathf.Max(0, currentDay - 1);
    }

    public int GetEnemiesKilled()
    {
        return totalEnemiesKilled;
    }
}

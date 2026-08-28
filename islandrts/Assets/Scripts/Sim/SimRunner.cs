#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives a sweep of simulated games and writes the CSVs.
///
/// Self-bootstraps like DebugMenu and PerfLogger — no scene object to wire. It
/// activates only when the process was launched with <c>-simconfig &lt;path&gt;</c>
/// or when an editor menu item queued a sweep, so pressing Play normally is
/// completely unaffected.
///
/// The speed trick that makes this useful is <see cref="Time.captureDeltaTime"/>:
/// it pins game time to a fixed step per frame, so the loop runs flat out in
/// wall-clock while still delivering exactly 60 frames per game-second. That
/// matters because this codebase's AI evaluation budget and NavMesh command
/// throttles are FRAME-based — speeding a run up with Time.timeScale would
/// silently starve every brain and report the resulting losses as "balance".
///
/// It is the second deliberate exception to the project's no-DontDestroyOnLoad
/// rule (DebugMenu is the first): it has to outlive the scene reload between
/// runs. It holds only its own sweep bookkeeping, never game state.
/// </summary>
/// <remarks>
/// Runs very early so run 0's scene knobs land before the components that read
/// them in their own Start (ResourceManager applies startingWood there,
/// GameManager reads nightsToSurvive). Runs 1..n get configured from the
/// sceneLoaded callback instead, which is already ahead of every Start.
/// </remarks>
[DefaultExecutionOrder(-1000)]
public class SimRunner : MonoBehaviour
{
    public const string Arg = "-simconfig";

    /// <summary>Set by the editor menu to queue a sweep on the next Play.</summary>
    public static string QueuedSweepPath;

    private static SimRunner instance;

    private SimSweep sweep;
    private List<SimConfig> queue;
    private int index = -1;
    private string outputDir;
    private string sceneName;

    private SimPolicy policy;
    private SimMetrics metrics;
    private SimMetrics.NightRow night;

    private bool runActive;
    private float runStartGameTime;
    private float runStartRealTime;
    private int runStartFrame;
    private float policyTimer;
    private int lastEnemyCount;

    // ---- bootstrap --------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;

        string path = QueuedSweepPath;
        if (string.IsNullOrEmpty(path)) path = ArgValue(Arg);
        if (string.IsNullOrEmpty(path)) return;

        if (!File.Exists(path))
        {
            Debug.LogError($"[Sim] Sweep file not found: {path}");
            return;
        }

        SimSweep parsed = SimSweep.Parse(File.ReadAllText(path));
        if (parsed == null)
        {
            Debug.LogError($"[Sim] Sweep file has no runs: {path}");
            return;
        }

        // Created inactive on purpose: AddComponent runs Awake immediately, and
        // Awake reads the sweep. Assign the fields first, then let it wake up.
        GameObject go = new GameObject("~SimRunner");
        go.SetActive(false);
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SimRunner>();
        instance.sweep = parsed;
        instance.queue = parsed.Expand();
        instance.outputDir = Path.IsPathRooted(parsed.outputDir)
            ? parsed.outputDir
            : Path.Combine(Directory.GetCurrentDirectory(), parsed.outputDir);

        SimHooks.Simulating = true;
        SimMetrics.EnsureHeaders(instance.outputDir);

        // Run 0's unit and terrain knobs must be live before the very first
        // scene Awake — TerrainGrid builds the island AND the NavMesh there.
        SimOverrides.Active = instance.queue[0];
        Random.InitState(instance.queue[0].seed);

        go.SetActive(true);

        Debug.Log($"[Sim] Sweep loaded: {instance.queue.Count} runs -> {instance.outputDir}");
    }

    private static string ArgValue(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }

    // ---- lifecycle --------------------------------------------------------

    private void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        AudioListener.volume = 0f;
        Time.captureDeltaTime = sweep.captureDeltaTime;

        sceneName = SceneManager.GetActiveScene().name;
        SceneManager.sceneLoaded += OnSceneLoaded;
        DayNightCycle.OnNightStart += OnNightStart;
        DayNightCycle.OnDayStart += OnDayStart;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        DayNightCycle.OnNightStart -= OnNightStart;
        DayNightCycle.OnDayStart -= OnDayStart;
        Time.captureDeltaTime = 0f;
    }

    private void Start()
    {
        // The first scene is already loaded by the time Bootstrap runs, so run 0
        // configures it in place rather than waiting for a sceneLoaded callback.
        BeginNextRun(alreadyLoaded: true);
    }

    /// <summary>
    /// Fires after every Awake in the new scene but before any Start — the one
    /// window where scene singletons can be reconfigured before they read their
    /// own inspector values (ResourceManager.Start applies startingWood, etc.).
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (index >= 0 && index < queue.Count) ConfigureScene(queue[index]);
    }

    // ---- run control ------------------------------------------------------

    private void BeginNextRun(bool alreadyLoaded)
    {
        index++;
        if (index >= queue.Count)
        {
            Finish();
            return;
        }

        SimConfig cfg = queue[index];
        SimOverrides.Active = cfg;
        Random.InitState(cfg.seed);

        // GameManager pauses the game on victory/defeat with Time.timeScale = 0,
        // and timeScale is a global that survives a scene load. Without this
        // reset every run after the first spins forever at 100% CPU with a
        // frozen clock — and a frozen clock means the Time.time-based timeout
        // below can never fire either.
        Time.timeScale = 1f;

        metrics = new SimMetrics
        {
            configId = cfg.Label(index),
            strategy = cfg.strategy,
            seed = cfg.seed,
            nightsToSurvive = cfg.nightsToSurvive
        };
        policy = SimPolicy.Create(cfg.strategy);
        metrics.strategy = policy.Name;
        night = null;
        lastEnemyCount = 0;
        policyTimer = 0f;

        if (alreadyLoaded)
        {
            ConfigureScene(cfg);
            StartCoroutine(RunRoutine(cfg));
        }
        else
        {
            // ConfigureScene runs from the sceneLoaded callback.
            StartCoroutine(LoadThenRun(cfg));
        }
    }

    private IEnumerator LoadThenRun(SimConfig cfg)
    {
        yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        yield return null;
        yield return RunRoutine(cfg);
    }

    /// <summary>
    /// Applies the config's scene-level knobs. Unit-level knobs can't be applied
    /// here (prefab values win and unit Starts copy them into the blackboard) —
    /// those go through SimOverrides, called from each unit's Start.
    /// </summary>
    private void ConfigureScene(SimConfig cfg)
    {
        ResourceManager rm = FindAnyObjectByType<ResourceManager>();
        if (rm != null)
        {
            if (cfg.startingWood >= 0) rm.startingWood = cfg.startingWood;
            if (cfg.startingFood >= 0) rm.startingFood = cfg.startingFood;
            if (cfg.startingStone >= 0) rm.startingStone = cfg.startingStone;
        }

        EnemySpawner es = FindAnyObjectByType<EnemySpawner>();
        if (es != null)
        {
            if (cfg.baseEnemiesPerNight >= 0) es.baseEnemiesPerNight = cfg.baseEnemiesPerNight;
            if (cfg.enemyIncreasePerNight >= 0f) es.enemyIncreasePerNight = cfg.enemyIncreasePerNight;
            if (cfg.spawnInterval >= 0f) es.spawnInterval = cfg.spawnInterval;
            if (cfg.spawnDelay >= 0f) es.spawnDelay = cfg.spawnDelay;
            if (cfg.spawnDistance > 0f) es.spawnDistance = cfg.spawnDistance;
        }

        DayNightCycle dn = FindAnyObjectByType<DayNightCycle>();
        if (dn != null)
        {
            if (cfg.dayLengthSeconds > 0f) dn.dayLengthInSeconds = cfg.dayLengthSeconds;
            if (cfg.nightLengthSeconds > 0f) dn.nightLengthInSeconds = cfg.nightLengthSeconds;
        }

        GameManager gm = FindAnyObjectByType<GameManager>();
        if (gm != null) gm.nightsToSurvive = cfg.nightsToSurvive;
    }

    private IEnumerator RunRoutine(SimConfig cfg)
    {
        runActive = true;
        runStartGameTime = Time.time;
        runStartRealTime = Time.realtimeSinceStartup;
        runStartFrame = Time.frameCount;

        // 1. Get past the opening sequence and get a campfire on the ground.
        GameStartController gsc = FindAnyObjectByType<GameStartController>();
        if (gsc != null && GameStartController.IntroInProgress)
        {
            gsc.DebugForceColonyStart();
        }

        float deadline = Time.time + 20f;
        while (SimBuilder.Campfire == null && Time.time < deadline) yield return null;

        if (SimBuilder.Campfire == null)
        {
            metrics.outcome = "error";
            metrics.note = "no campfire after 20s";
            EndRun();
            yield break;
        }

        // Campfire knobs (maxWarriors, warrior costs, HP) are applied by
        // SimOverrides.Apply(BaseBuilding) from its own Start, so they are live
        // even though the opening sequence spawns the campfire at runtime.

        yield return null;   // let Hut/BaseBuilding Start register housing

        // 2. Play until the game ends, times out, or the campfire falls.
        GameManager gm = GameManager.Instance;
        while (true)
        {
            float elapsed = Time.time - runStartGameTime;
            if (elapsed > cfg.maxGameSeconds)
            {
                metrics.outcome = "timeout";
                metrics.note = $"exceeded {cfg.maxGameSeconds:F0}s game time";
                break;
            }

            // Failsafe in REAL seconds: the check above is measured in game
            // time, so anything that freezes the game clock would spin here
            // forever. Never let one bad run eat a whole sweep.
            float wall = Time.realtimeSinceStartup - runStartRealTime;
            if (wall > sweep.maxWallSecondsPerRun)
            {
                metrics.outcome = "timeout";
                metrics.note = $"wall-clock guard at {wall:F0}s real ({elapsed:F0}s game) " +
                               $"- game clock may be frozen (timeScale {Time.timeScale:0.##})";
                break;
            }
            if (gm != null && gm.isGameOver)
            {
                metrics.outcome = gm.isVictory ? "victory" : "defeat";
                break;
            }
            if (SimBuilder.Campfire == null)
            {
                metrics.outcome = "defeat";
                metrics.note = "campfire destroyed";
                break;
            }

            Sample();

            policyTimer += Time.deltaTime;
            if (policyTimer >= 1f)
            {
                policyTimer = 0f;
                policy.Tick(BuildState());
            }

            yield return null;
        }

        EndRun();
    }

    private SimState BuildState()
    {
        BaseBuilding fire = SimBuilder.Campfire;
        ResourceManager rm = ResourceManager.Instance;
        DayNightCycle dn = FindAnyObjectByType<DayNightCycle>();

        return new SimState
        {
            Campfire = fire,
            Day = dn != null ? dn.GetCurrentDay() : 1,
            IsNight = dn != null && dn.IsNightTime(),
            Workers = fire != null ? fire.GetTotalWorkers() : 0,
            Warriors = fire != null ? fire.GetWarriorCount() : 0,
            Enemies = Enemy.ActiveList.Count,
            Wood = rm != null ? rm.wood : 0,
            Food = rm != null ? rm.food : 0,
            Stone = rm != null ? rm.stone : 0
        };
    }

    private void Sample()
    {
        BaseBuilding fire = SimBuilder.Campfire;
        if (fire == null) return;

        int workers = fire.GetTotalWorkers();
        int warriors = fire.GetWarriorCount();
        if (workers > metrics.peakWorkers) metrics.peakWorkers = workers;
        if (warriors > metrics.peakWarriors) metrics.peakWarriors = warriors;

        // Enemy spawns arrive staggered and get killed in between, so count
        // upward deltas rather than trusting a single peak reading.
        int enemies = Enemy.ActiveList.Count;
        if (night != null && enemies > lastEnemyCount) night.enemiesSpawned += enemies - lastEnemyCount;
        lastEnemyCount = enemies;

        if (night != null)
        {
            float hp = fire.GetCurrentHealth();
            if (hp < night.campfireHpMin) night.campfireHpMin = hp;
        }
    }

    private void OnNightStart()
    {
        if (!runActive) return;

        BaseBuilding fire = SimBuilder.Campfire;
        ResourceManager rm = ResourceManager.Instance;
        DayNightCycle dn = FindAnyObjectByType<DayNightCycle>();

        night = new SimMetrics.NightRow
        {
            night = dn != null ? dn.GetCurrentDay() : metrics.nights.Count + 1,
            wood = rm != null ? rm.wood : 0,
            food = rm != null ? rm.food : 0,
            stone = rm != null ? rm.stone : 0,
            workers = fire != null ? fire.GetTotalWorkers() : 0,
            warriors = fire != null ? fire.GetWarriorCount() : 0,
            huts = SimBuilder.HutCount,
            walls = SimBuilder.WallCount,
            towers = SimBuilder.TowerCount,
            campfireHpStart = fire != null ? fire.GetCurrentHealth() : 0f,
            campfireHpMin = fire != null ? fire.GetCurrentHealth() : 0f
        };
        lastEnemyCount = Enemy.ActiveList.Count;
        metrics.nightReached = night.night;
    }

    private void OnDayStart()
    {
        if (!runActive || night == null) return;

        CaptureDawn();
        night.survived = SimBuilder.Campfire != null;

        metrics.nights.Add(night);
        night = null;
    }

    /// <summary>
    /// Fills the dawn half of the current night row.
    ///
    /// Shared with <see cref="EndRun"/> on purpose: the night a run LOSES is
    /// the most interesting row in the file, and it never reaches OnDayStart.
    /// Leaving these at their defaults wrote a row of zeroes that reads as
    /// "every wall, hut and worker was destroyed and nothing was killed" —
    /// which is not what happened, and is exactly the row you go looking at
    /// when you want to know why a run fell over.
    /// </summary>
    private void CaptureDawn()
    {
        BaseBuilding fire = SimBuilder.Campfire;
        ResourceManager rm = ResourceManager.Instance;

        night.woodDawn = rm != null ? rm.wood : 0;
        night.foodDawn = rm != null ? rm.food : 0;
        night.stoneDawn = rm != null ? rm.stone : 0;
        night.workersDawn = fire != null ? fire.GetTotalWorkers() : 0;
        night.warriorsDawn = fire != null ? fire.GetWarriorCount() : 0;
        night.hutsDawn = SimBuilder.HutCount;
        night.wallsDawn = SimBuilder.WallCount;
        night.towersDawn = SimBuilder.TowerCount;
        night.campfireHpDawn = fire != null ? fire.GetCurrentHealth() : 0f;
        night.enemiesKilledTotal = GameManager.Instance != null ? GameManager.Instance.totalEnemiesKilled : 0;
    }

    private void EndRun()
    {
        runActive = false;

        // A night in progress when the run ended still deserves its row —
        // with its dawn side filled in, since this is the losing night.
        if (night != null)
        {
            CaptureDawn();
            night.survived = false;
            metrics.nights.Add(night);
            night = null;
        }

        ResourceManager rm = ResourceManager.Instance;
        if (rm != null)
        {
            metrics.finalWood = rm.wood;
            metrics.finalFood = rm.food;
            metrics.finalStone = rm.stone;
        }
        metrics.totalEnemiesKilled = GameManager.Instance != null ? GameManager.Instance.totalEnemiesKilled : 0;
        metrics.gameSeconds = Time.time - runStartGameTime;
        metrics.wallClockSeconds = Time.realtimeSinceStartup - runStartRealTime;
        metrics.frames = Time.frameCount - runStartFrame;

        metrics.Append(outputDir);
        Debug.Log(metrics.Summary());

        BeginNextRun(alreadyLoaded: false);
    }

    private void Finish()
    {
        Debug.Log($"[Sim] Sweep complete: {queue.Count} runs written to {outputDir}");
        SimOverrides.Active = null;
        SimHooks.Simulating = false;
        QueuedSweepPath = null;
        Time.captureDeltaTime = 0f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(0);
#endif
    }
}
#endif

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
/// them in their own Start (GameManager reads daysToSurvive there; RaidDirector
/// rolls day 1 there). Runs 1..n
/// get configured from the sceneLoaded callback instead, which is already ahead
/// of every Start — but NOT ahead of any Awake, which is why ConfigureScene has
/// to write the live pool (Factions.Player.Resources) rather than the component's starting amounts.
/// </remarks>
[DefaultExecutionOrder(-1000)]
public class SimRunner : MonoBehaviour
{
    public const string Arg = "-simconfig";

    /// <summary>
    /// Flag arg: render the run in a window instead of running headless.
    /// Implied whenever the process was not launched with -batchmode, so the
    /// editor's "Run Sweep In Editor" is visual without asking for it.
    /// </summary>
    public const string VisualArg = "-simvisual";

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
    private SimMetrics.DayRow night;   // the row for the night in progress (dusk → dawn)

    private SimVisualOverlay.Frame lastFrame;
    private float lastStatusWriteReal = -10f;

    private bool runActive;
    private float sweepStartRealTime;
    private float lastGameTimeSeen = -1f;
    private float lastGameAdvanceReal;
    private const float FrozenClockSeconds = 60f;
    private float runStartGameTime;
    private float runStartRealTime;
    private int runStartFrame;
    private float policyTimer;
    private int lastEnemyCount;
    private int warriorsLostThisNight;   // Warrior.OnAnyWarriorDied between dusk and dawn (2026-09-10)

    private void OnWarriorDied(Vector3 at) { if (night != null) warriorsLostThisNight++; }

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
        // Headless is a CAPABILITY flag, never a policy one: it only ever turns
        // drawing off. A visual run therefore takes the same decisions as the
        // sweep it is explaining - see SimHooks for the split.
        SimHooks.Headless = Application.isBatchMode && !HasArg(VisualArg);
        SimMetrics.EnsureHeaders(instance.outputDir);

        // Run 0's unit and terrain knobs must be live before the very first
        // scene Awake — TerrainGrid builds the island AND the NavMesh there.
        Activate(instance.queue[0]);
        Random.InitState(instance.queue[0].seed);

        go.SetActive(true);

        Debug.Log($"[Sim] Sweep loaded: {instance.queue.Count} runs -> {instance.outputDir}");
    }

    private static bool HasArg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name) return true;
        }
        return false;
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
        // Silence, but not the thing that keeps nine windows off the audio driver:
        // by the time this runs the engine has already opened an output device.
        // That is switched off at BUILD time instead - SimTools flips the project's
        // "Disable Unity Audio" for the sim player build (2026-09-10).
        AudioListener.volume = 0f;
        AudioListener.pause = true;
        Time.captureDeltaTime = sweep.captureDeltaTime;
        sweepStartRealTime = Time.realtimeSinceStartup;

        // A visual run still SIMULATES every frame - captureDeltaTime pins the
        // step, so the frame-based AI budget and NavMesh throttles are untouched.
        // What it does not do is DRAW every frame: OnDemandRendering skips the
        // render loop on all but every Nth frame, which is the only honest way to
        // buy speed back. Never reach for Time.timeScale here (see SIMULATION.md).
        UnityEngine.Rendering.OnDemandRendering.renderFrameInterval =
            SimHooks.Headless ? 1 : Mathf.Max(1, sweep.renderFrameInterval);

        if (SimHooks.Visual) SimVisualOverlay.Ensure();

        sceneName = SceneManager.GetActiveScene().name;
        SceneManager.sceneLoaded += OnSceneLoaded;
        DayNightCycle.OnNightStart += OnNightStart;
        DayNightCycle.OnDayStart += OnDayStart;
        Warrior.OnAnyWarriorDied += OnWarriorDied;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        DayNightCycle.OnNightStart -= OnNightStart;
        DayNightCycle.OnDayStart -= OnDayStart;
        Warrior.OnAnyWarriorDied -= OnWarriorDied;
        Time.captureDeltaTime = 0f;
        UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
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
    /// own inspector values in Start. Anything a component consumes in AWAKE is
    /// already too late here and must be written directly — see the
    /// ResourceManager case in ConfigureScene.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (index >= 0 && index < queue.Count) ConfigureScene(queue[index]);
    }

    // ---- run control ------------------------------------------------------

    /// <summary>
    /// Makes a config the live one: the unit/terrain knobs through
    /// <see cref="SimOverrides.Active"/> and the rule-set names through
    /// <see cref="SimHooks"/> (2026-09-11). Must run BEFORE the run's scene
    /// loads - ResourceManager.Awake reads the difficulty, TerrainGrid.Awake the
    /// island - which is why run 0 does it from Bootstrap.
    /// </summary>
    private static void Activate(SimConfig cfg)
    {
        SimOverrides.Active = cfg;
        SimHooks.Difficulty = cfg.difficulty ?? "";
        SimHooks.IslandSize = cfg.islandSize ?? "";
        SimHooks.IslandStyle = cfg.islandStyle ?? "";
        SimHooks.RivalCount = cfg.rivalCount;
    }

    private void BeginNextRun(bool alreadyLoaded)
    {
        index++;
        if (index >= queue.Count)
        {
            Finish();
            return;
        }

        SimConfig cfg = queue[index];
        Activate(cfg);
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
            daysToSurvive = cfg.daysToSurvive
        };
        policy = SimPolicy.Create(cfg.strategy);
        metrics.strategy = policy.Name;
        night = null;
        lastEnemyCount = 0;
        SimBuilder.ResetRun();
        SimPlayerDriver.ResetRun();
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
        {
            // ResourceManager copies the scene's starting amounts into the live
            // pool in AWAKE, not Start — and sceneLoaded runs after every Awake.
            // So the knobs write the live pool itself (Factions.Player.Resources
            // since lap step 1), not the component's starting fields.
            ResourcePool pool = Factions.Player.Resources;
            if (cfg.startingWood >= 0) pool.wood = cfg.startingWood;
            if (cfg.startingFood >= 0) pool.food = cfg.startingFood;
            if (cfg.startingStone >= 0) pool.stone = cfg.startingStone;
        }

        EnemySpawner es = FindAnyObjectByType<EnemySpawner>();
        if (es != null)
        {
            if (cfg.spawnInterval >= 0f) es.spawnInterval = cfg.spawnInterval;
            if (cfg.spawnDelay >= 0f) es.spawnDelay = cfg.spawnDelay;
            if (cfg.spawnDistance > 0f) es.spawnDistance = cfg.spawnDistance;

            // The director is added by EnemySpawner.Awake, so it exists by now
            // (sceneLoaded is after every Awake; run 0 configures from a Start
            // that precedes the director's own Start, where day 1 is rolled).
            RaidDirector rd = es.GetComponent<RaidDirector>();
            if (rd != null)
            {
                if (cfg.raidFirstDay >= 0) rd.firstRaidDay = cfg.raidFirstDay;
                if (cfg.raidBaseChance >= 0f) rd.baseChance = cfg.raidBaseChance;
                if (cfg.raidChancePerQuietDay >= 0f) rd.chancePerQuietDay = cfg.raidChancePerQuietDay;
                if (cfg.raidMaxQuietDays >= 0) rd.maxQuietDays = cfg.raidMaxQuietDays;
                if (cfg.raidMinQuietNights >= 0) rd.minQuietNights = cfg.raidMinQuietNights;
                if (cfg.raidBaseSize >= 0f) rd.baseSize = cfg.raidBaseSize;
                if (cfg.raidSizePerDay >= 0f) rd.sizePerDay = cfg.raidSizePerDay;
                if (cfg.raidSizePerProsperity >= 0f) rd.sizePerProsperity = cfg.raidSizePerProsperity;
            }
        }

        DayNightCycle dn = FindAnyObjectByType<DayNightCycle>();
        if (dn != null)
        {
            if (cfg.dayLengthSeconds > 0f) dn.dayLengthInSeconds = cfg.dayLengthSeconds;
            if (cfg.nightLengthSeconds > 0f) dn.nightLengthInSeconds = cfg.nightLengthSeconds;
        }

        GameManager gm = FindAnyObjectByType<GameManager>();
        if (gm != null) gm.daysToSurvive = cfg.daysToSurvive;

        // Food (2026-09-04): the manager is a scene object (or created on demand);
        // it reads the field every frame, so writing it here is enough.
        if (cfg.foodPerDay >= 0f)
        {
            PopulationManager pm = PopulationManager.EnsureExists();
            // 0 is "nobody eats" here, but a non-positive field falls back to
            // the default in the manager (missing-YAML-key rule), so off is a flag
            // on the player's Population (the sim drives only that one).
            Factions.Player.Population.foodDisabled = cfg.foodPerDay <= 0f;
            pm.foodPerColonistPerDay = cfg.foodPerDay;
        }
    }

    private IEnumerator RunRoutine(SimConfig cfg)
    {
        runActive = true;
        runStartGameTime = Time.time;
        runStartRealTime = Time.realtimeSinceStartup;
        lastGameTimeSeen = -1f;
        lastGameAdvanceReal = Time.realtimeSinceStartup;
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

        // The camera rig is a scene object, so it is only findable now. Headless
        // runs skip this entirely; Ensure no-ops off the visual path anyway.
        SimSpectatorCamera.Ensure();

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

            // Failsafe for a FROZEN clock (2026-09-10): the check above is in game
            // time, so a stray timeScale 0 or a paused DayNightCycle would spin
            // here forever. The old flat wall-clock cap (900 s) could not tell a
            // frozen run from a slow one - it cut five winning day-25 colonies
            // out of the first 4x lab. Now: game time that has not advanced for
            // FrozenClockSeconds of real time is frozen; anything else is allowed
            // to take as long as it takes, under maxWallSecondsPerRun as a
            // last-ditch ceiling.
            float wall = Time.realtimeSinceStartup - runStartRealTime;
            if (Time.time > lastGameTimeSeen + 0.001f)
            {
                lastGameTimeSeen = Time.time;
                lastGameAdvanceReal = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - lastGameAdvanceReal > FrozenClockSeconds)
            {
                metrics.outcome = "timeout";
                metrics.note = $"game clock frozen for {FrozenClockSeconds:F0}s real at {elapsed:F0}s game " +
                               $"(timeScale {Time.timeScale:0.##})";
                break;
            }
            if (wall > sweep.maxWallSecondsPerRun)
            {
                metrics.outcome = "timeout";
                metrics.note = $"wall-clock ceiling at {wall:F0}s real ({elapsed:F0}s game)";
                break;
            }
            if (gm != null && gm.isGameOver)
            {
                metrics.outcome = gm.isEscape ? "escape" : gm.isVictory ? "victory" : "defeat";   // escape (2026-09-04)
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
                SimState state = BuildState();
                SimPlayerDriver.Tick(state);   // the character's legs: materials + bench labor
                policy.Tick(state);
                PushOverlay(cfg, state);
                if (SimHooks.Visual)
                {
                    // Skim the quiet day, watch the fight (2026-09-10): the draw
                    // interval is the ONE speed knob that leaves decisions alone,
                    // so it is the one that follows the raid. Once a second is
                    // plenty; raiders take seconds to land and to die.
                    UnityEngine.Rendering.OnDemandRendering.renderFrameInterval =
                        state.Enemies > 0 ? sweep.renderFrameIntervalRaid : sweep.renderFrameInterval;
                }
            }

            yield return null;
        }

        EndRun();
    }

    /// <summary>
    /// Mirror the second's state onto the on-screen caption and the launcher's
    /// heartbeat file. Both modes since 2026-09-11 (the overnight batch shows the
    /// same dashboard as the lab); it reads nothing the policy did not already
    /// read. The FILE write is throttled to one per real second: this runs once
    /// per GAME second, and headless that is 15-30 times a real second per
    /// process, eight processes at a time.
    /// </summary>
    private void PushOverlay(SimConfig cfg, SimState state)
    {
        BaseBuilding fire = SimBuilder.Campfire;
        ResourcePool pool = Factions.Player.Resources;

        lastFrame = new SimVisualOverlay.Frame
        {
            runId = metrics.configId,
            strategy = metrics.strategy,
            seed = cfg.seed,
            runIndex = index,
            runCount = queue.Count,
            day = state.Day,
            daysToSurvive = cfg.daysToSurvive,
            raidTonight = state.RaidTonight,
            colonists = state.Colonists,
            workers = state.Workers,
            warriors = state.Warriors,
            enemies = state.Enemies,
            wood = pool.wood,
            food = pool.food,
            stone = pool.stone,
            metal = pool.metal,
            campfireHp = fire != null ? fire.GetCurrentHealth() : 0f,
            campfireHpMax = fire != null ? fire.maxHealth : 0f,
            hunger = state.Hunger,
            goal = SimPolicy.Goal,
            intent = SimPolicy.Intent,
            castaway = CastawayLine(),
            nextRaidSize = state.NextRaidSize,
        };

        SimVisualOverlay.Push(lastFrame);
        // The launcher's dashboard reads this: neither CSV exists until the run
        // is over, so a live view has nowhere else to read from.
        float now = Time.realtimeSinceStartup;
        if (now - lastStatusWriteReal >= 1f)
        {
            lastStatusWriteReal = now;
            SimStatus.Write(outputDir, lastFrame, "");
        }
    }

    private SimState BuildState()
    {
        BaseBuilding fire = SimBuilder.Campfire;
        ResourcePool rm = Factions.Player.Resources;
        DayNightCycle dn = FindAnyObjectByType<DayNightCycle>();

        return new SimState
        {
            Campfire = fire,
            Day = dn != null ? dn.GetCurrentDay() : 1,
            RaidTonight = RaidDirector.Instance != null && RaidDirector.Instance.RaidTonight,
            RaidLurking = RaidDirector.Instance != null && RaidDirector.Instance.RaidLurking,
            NextRaidSize = NextRaidSize(dn != null ? dn.GetCurrentDay() : 1),
            Workers = fire != null ? fire.GetTotalWorkers() : 0,
            Warriors = fire != null ? fire.GetWarriorCount() : 0,
            Enemies = Enemy.ActiveList.Count,
            Wood = rm.wood,
            Food = rm.food,
            Stone = rm.stone,
            Colonists = Factions.Player.Population != null ? Factions.Player.Population.GetColonistCount() : 0,
            Hunger = Factions.Player.Population != null ? (int)Factions.Player.Population.Hunger : 0,
        };
    }

    /// <summary>What the castaway is doing right now, for the caption (2026-09-10).</summary>
    private static string CastawayLine()
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        if (pc == null) return "";
        if (pc.IsKnockedOut) return "knocked out";
        if (pc.WorkingStation != null)
        {
            CraftStation.QueueEntry e = pc.WorkingStation.Active;
            return e != null ? $"working: {e.Def.title} {Mathf.RoundToInt(e.Progress01 * 100f)}%" : "at the bench";
        }
        if (pc.BuildingSite != null) return "building " + pc.BuildingSite.buildingType;
        if (pc.WalkingToStation != null) return "walking to the bench";
        string a = pc.Activity;
        return string.IsNullOrEmpty(a) ? "idle" : a.ToLowerInvariant();
    }

    /// <summary>
    /// The raid the policy should be standing ready for (2026-09-10): tonight's
    /// committed size when the dawn roll said raiders land, else what a roll
    /// tomorrow would land against the colony as it stands. A player reads the
    /// same two things off the banner and the day counter.
    /// </summary>
    private static int NextRaidSize(int day)
    {
        RaidDirector rd = RaidDirector.Instance;
        if (rd == null) return 0;
        return rd.RaidTonight ? rd.PlannedSize : rd.EstimateRaidSize(day + 1);
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
        ResourcePool rm = Factions.Player.Resources;
        DayNightCycle dn = FindAnyObjectByType<DayNightCycle>();

        RaidDirector rd = RaidDirector.Instance;
        night = new SimMetrics.DayRow
        {
            day = dn != null ? dn.GetCurrentDay() : metrics.days.Count + 1,
            raid = rd != null && rd.RaidTonight,
            raidSize = rd != null && rd.RaidTonight ? rd.PlannedSize : 0,
            wood = rm.wood,
            food = rm.food,
            stone = rm.stone,
            workers = fire != null ? fire.GetTotalWorkers() : 0,
            warriors = fire != null ? fire.GetWarriorCount() : 0,
            huts = SimBuilder.HutCount,
            walls = SimBuilder.WallCount,
            towers = SimBuilder.TowerCount,
            campfireHpStart = fire != null ? fire.GetCurrentHealth() : 0f,
            campfireHpMin = fire != null ? fire.GetCurrentHealth() : 0f
        };
        lastEnemyCount = Enemy.ActiveList.Count;
        warriorsLostThisNight = 0;
        metrics.dayReached = night.day;
        if (night.raid) metrics.raids++;
    }

    private void OnDayStart()
    {
        if (!runActive || night == null) return;

        CaptureDawn();
        night.survived = SimBuilder.Campfire != null;

        metrics.days.Add(night);
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
        ResourcePool rm = Factions.Player.Resources;

        night.woodDawn = rm.wood;
        night.foodDawn = rm.food;
        night.stoneDawn = rm.stone;
        night.workersDawn = fire != null ? fire.GetTotalWorkers() : 0;
        night.warriorsDawn = fire != null ? fire.GetWarriorCount() : 0;
        night.hutsDawn = SimBuilder.HutCount;
        night.wallsDawn = SimBuilder.WallCount;
        night.towersDawn = SimBuilder.TowerCount;
        night.campfireHpDawn = fire != null ? fire.GetCurrentHealth() : 0f;
        night.enemiesKilledTotal = GameManager.Instance != null ? GameManager.Instance.totalEnemiesKilled : 0;
        Population pm = Factions.Player.Population;
        night.hungerDawn = pm != null ? (int)pm.Hunger : 0;
        night.leftTotal = pm != null ? pm.ColonistsLeft : 0;
        if (pm != null) metrics.colonistsLeft = pm.ColonistsLeft;

        int archers = 0;
        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++) if (warriors[i] != null && warriors[i].IsRanged) archers++;
        night.archersDawn = archers;

        // What a wiped colony has to rebuild with (2026-09-10)
        night.idleDawn = pm != null ? pm.GetIdleCount() : 0;
        night.weaponsDawn = fire != null ? fire.WeaponsInStock() : 0;
        night.sticksDawn = fire != null ? fire.Stockpile.Count(ItemCatalog.Stick) : 0;
        night.chunksDawn = fire != null ? fire.Stockpile.Count(ItemCatalog.StoneChunk) : 0;
        night.queueDawn = fire != null && fire.Station != null ? fire.Station.Status : "";
        night.warriorsLost = warriorsLostThisNight;
        night.ringHoles = SimBuilder.RingHoles;
        night.chunksLoose = LooseChunks();
        CaptureRivals();
    }

    /// <summary>
    /// The neighbours at dawn (2026-09-11). All zeroes on a run with no rivals,
    /// which is every sweep taken before lap step 3 and every one that leaves
    /// <c>SimConfig.rivalCount</c> at 0.
    /// </summary>
    private void CaptureRivals()
    {
        RivalLandingDirector dir = RivalLandingDirector.Instance;
        if (dir == null || dir.Landed.Count == 0) return;

        night.rivalArrivalDay = dir.FirstArrivalDay;
        night.rivalContact = dir.Contacted ? 1 : 0;
        night.rivalOpinion = (int)Factions.Player.Toward(dir.Landed[0]);

        int warriors = 0;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Warrior w = list[i];
            if (w == null || w.Faction == null) continue;
            if (w.Faction.Type == Faction.Kind.Rival) warriors++;
        }
        night.rivalWarriors = warriors;
    }

    /// <summary>
    /// Stone chunks lying on the island (2026-09-11). Nothing can MAKE one without
    /// the Stone Pick that Quarrying grants, and Quarrying costs three, so a run
    /// that finds none can never quarry, never re-arm and never win.
    /// </summary>
    static int LooseChunks()
    {
        int n = 0;
        var list = GroundPickup.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            GroundPickup p = list[i];
            if (p != null && p.Item == ItemCatalog.StoneChunk) n++;
        }
        return n;
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
            metrics.days.Add(night);
            night = null;
        }

        ResourcePool rm = Factions.Player.Resources;
        metrics.finalWood = rm.wood;
        metrics.finalFood = rm.food;
        metrics.finalStone = rm.stone;
        metrics.totalEnemiesKilled = GameManager.Instance != null ? GameManager.Instance.totalEnemiesKilled : 0;
        metrics.gameSeconds = Time.time - runStartGameTime;
        metrics.wallClockSeconds = Time.realtimeSinceStartup - runStartRealTime;
        metrics.frames = Time.frameCount - runStartFrame;

        metrics.Append(outputDir);
        Debug.Log(metrics.Summary());

        // Leave the outcome standing in the heartbeat: the next run overwrites it
        // a second after it starts, and if this was the last one the dashboard's
        // final redraw shows how the process ended rather than a stale mid-run row.
        lastFrame.runId = metrics.configId;
        lastFrame.strategy = metrics.strategy;
        lastFrame.day = metrics.dayReached;
        SimStatus.Write(outputDir, lastFrame, metrics.outcome);

        QueueRespawn(queue[index], metrics.outcome);
        BeginNextRun(alreadyLoaded: false);
    }

    /// <summary>
    /// A lost cell plays again while the sweep is inside its respawn budget
    /// (2026-09-10): the same config, one attempt higher, queued right behind
    /// itself. Only a DEFEAT respawns — a victory, escape, timeout or error is
    /// the cell's answer.
    /// </summary>
    private void QueueRespawn(SimConfig cfg, string outcome)
    {
        if (sweep.respawnWallMinutes <= 0f || outcome != "defeat") return;
        float minutes = (Time.realtimeSinceStartup - sweepStartRealTime) / 60f;
        if (minutes >= sweep.respawnWallMinutes) return;

        SimConfig again = JsonUtility.FromJson<SimConfig>(JsonUtility.ToJson(cfg));
        again.attempt = cfg.attempt + 1;
        string baseId = cfg.attempt > 1 ? cfg.id.Substring(0, cfg.id.LastIndexOf("_try", System.StringComparison.Ordinal)) : cfg.id;
        again.id = $"{baseId}_try{again.attempt}";
        queue.Insert(index + 1, again);
        Debug.Log($"[Sim] {cfg.id} lost at {minutes:F1} min into the sweep - respawning as {again.id}");
    }

    private void Finish()
    {
        Debug.Log($"[Sim] Sweep complete: {queue.Count} runs written to {outputDir}");
        SimOverrides.Active = null;
        SimHooks.Simulating = false;
        SimHooks.Headless = false;
        UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
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

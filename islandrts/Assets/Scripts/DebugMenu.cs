#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// F4 cheat/debug menu for playtesting. Compiled ONLY in the editor and
/// Development Builds — release builds ship without it (whole file is
/// #if-wrapped, like AIDebugOverlay).
///
/// Sections: live status readout, resource grants, an adjustable quick-start
/// colony (skips the intro, places campfire + huts + workers + warriors),
/// time controls (skip to night/day, pause clock, game speed), and combat /
/// building cheats (spawn wave, kill enemies, heal everything, finish
/// construction).
///
/// Self-bootstrapping via [RuntimeInitializeOnLoadMethod] — no scene object
/// to wire. Uses DontDestroyOnLoad, which is a deliberate exception to the
/// project's no-DDOL rule: the menu holds no game state (every action looks
/// up the live singletons/registries at click time), so nothing stale can
/// survive a scene reload — and the stepper values persisting across
/// restarts is a feature.
///
/// IMGUI + GUILayout allocates a little GC per frame while OPEN — that's
/// fine for a debug tool; it costs nothing while closed.
/// </summary>
public class DebugMenu : MonoBehaviour
{
    private static DebugMenu instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("[DebugMenu]");
        instance = go.AddComponent<DebugMenu>();
        DontDestroyOnLoad(go);
    }

    private bool isVisible;

    // Quick-start steppers (defaults: small working base)
    private int hutCount = 2;
    private int woodWorkerCount = 4;
    private int foodWorkerCount = 2;
    private int stoneWorkerCount = 1;
    private int warriorCount = 3;
    private bool spawningColony;

    // Cached scene refs — re-found when destroyed (scene reload)
    private DayNightCycle dayNight;
    private EnemySpawner enemySpawner;

    private GUIStyle headerStyle;
    private GUIStyle sectionStyle;
    private bool stylesInit;
    private Vector2 scroll;

    private const float PanelWidth = 270f;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F4))
        {
            isVisible = !isVisible;
        }
    }

    // --- Live lookups (never cached across reloads in a stale way) ---

    DayNightCycle DayNight
    {
        get
        {
            if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();
            return dayNight;
        }
    }

    EnemySpawner Spawner
    {
        get
        {
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
            return enemySpawner;
        }
    }

    BaseBuilding Campfire => BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;

    // ------------------------------------------------------------------
    // GUI
    // ------------------------------------------------------------------

    void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = Color.white;

        sectionStyle = new GUIStyle(GUI.skin.label);
        sectionStyle.fontSize = 12;
        sectionStyle.fontStyle = FontStyle.Bold;
        sectionStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
    }

    void OnGUI()
    {
        if (!isVisible || !Application.isPlaying) return;
        InitStyles();

        // Left side — the F3 AI overlay owns the right side
        GUILayout.BeginArea(new Rect(10, 10, PanelWidth, Screen.height - 20), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("DEBUG MENU  (F4 to close)", headerStyle);
        StatusSection();
        ResourceSection();
        QuickStartSection();
        TimeSection();
        CheatSection();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void StatusSection()
    {
        var pm = PopulationManager.Instance;
        var fire = Campfire;

        string clock = DayNight != null
            ? (DayNight.IsNightTime() ? "Night " : "Day ") + DayNight.GetCurrentDay()
              + "  t=" + DayNight.GetTimeOfDay().ToString("F2")
              + (DayNight.clockPaused ? "  [PAUSED]" : "")
            : "(no DayNightCycle)";
        GUILayout.Label(clock);
        GUILayout.Label("Phase: " + GameStartController.Phase
            + "   Speed: " + Time.timeScale.ToString("F0") + "x");
        // Pressing Play straight into MainIsland skips the menu, so the run uses
        // whatever difficulty was last chosen there. Showing it here is what
        // stops "why is this wave enormous" turning into a bug hunt.
        GUILayout.Label("Difficulty: " + Difficulty.ActiveName
            + "   (raids " + Difficulty.EnemyCountMultiplier.ToString("0.##") + "x"
            + ", nights to win " + Difficulty.NightsToSurvive + ")");
        // The island seed is what reproduces a layout bug report — restart
        // keeps it, NEW GAME rolls a fresh one
        if (TerrainGrid.Instance != null)
            GUILayout.Label("Island seed: " + TerrainGrid.Instance.seed);
        GUILayout.Label("Pop " + (pm != null ? pm.GetColonistCount() + "/" + pm.GetHousingCapacity() : "?/?")
            + "   Idle " + (pm != null ? pm.GetIdleCount() : 0)
            + "   Warriors " + (fire != null ? fire.GetWarriorCount() : 0)
            + "   Enemies " + Enemy.ActiveList.Count);
    }

    void ResourceSection()
    {
        GUILayout.Space(6);
        GUILayout.Label("Resources", sectionStyle);

        var rm = ResourceManager.Instance;
        if (rm == null)
        {
            GUILayout.Label("(no ResourceManager)");
            return;
        }

        ResourceRow("Wood " + rm.wood, amt => rm.AddWood(amt));
        ResourceRow("Food " + rm.food, amt => rm.AddFood(amt));
        ResourceRow("Stone " + rm.stone, amt => rm.AddStone(amt));
        ResourceRow("Metal " + rm.metal, amt => rm.AddMetal(amt));

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+1000 all"))
        {
            rm.AddWood(1000); rm.AddFood(1000); rm.AddStone(1000); rm.AddMetal(1000);
        }
        if (GUILayout.Button("Zero all"))
        {
            rm.wood = 0; rm.food = 0; rm.stone = 0; rm.metal = 0;
        }
        GUILayout.EndHorizontal();
    }

    void ResourceRow(string label, System.Action<int> add)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90));
        if (GUILayout.Button("+100", GUILayout.Width(55))) add(100);
        if (GUILayout.Button("+1000", GUILayout.Width(60))) add(1000);
        GUILayout.EndHorizontal();
    }

    void QuickStartSection()
    {
        GUILayout.Space(6);
        GUILayout.Label("Quick-Start Colony", sectionStyle);

        hutCount = Stepper("Huts", hutCount, 0, 8);
        woodWorkerCount = Stepper("Wood workers", woodWorkerCount, 0, 10);
        foodWorkerCount = Stepper("Food workers", foodWorkerCount, 0, 10);
        stoneWorkerCount = Stepper("Stone workers", stoneWorkerCount, 0, 10);
        warriorCount = Stepper("Warriors", warriorCount, 0, 5);

        GUI.enabled = !spawningColony;
        string buttonLabel = GameStartController.IntroInProgress
            ? "Spawn Colony (skips intro, +1000 res)"
            : "Spawn Colony (+1000 res)";
        if (GUILayout.Button(buttonLabel))
        {
            StartCoroutine(SpawnColonyRoutine());
        }
        GUI.enabled = true;
        GUILayout.Label("Workers are capped by housing.", GUI.skin.label);
    }

    int Stepper(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(105));
        if (GUILayout.Button("-", GUILayout.Width(26))) value--;
        GUILayout.Label(value.ToString(), GUILayout.Width(26));
        if (GUILayout.Button("+", GUILayout.Width(26))) value++;
        GUILayout.EndHorizontal();
        return Mathf.Clamp(value, min, max);
    }

    void TimeSection()
    {
        GUILayout.Space(6);
        GUILayout.Label("Time", sectionStyle);

        var cycle = DayNight;
        if (cycle == null)
        {
            GUILayout.Label("(no DayNightCycle)");
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Skip to Night") && !cycle.IsNightTime())
        {
            cycle.currentTimeOfDay = 0.76f;  // just past dusk — OnNightStart fires next Update
        }
        if (GUILayout.Button("Skip to Day") && cycle.IsNightTime())
        {
            // The day counter increments at the midnight wrap; skipping from
            // late night (t > 0.75) crosses that wrap, early morning doesn't.
            if (cycle.currentTimeOfDay > 0.75f) cycle.currentDay++;
            cycle.currentTimeOfDay = 0.26f;  // just past dawn — OnDayStart fires next Update
        }
        GUILayout.EndHorizontal();

        cycle.clockPaused = GUILayout.Toggle(cycle.clockPaused, " Clock paused");

        bool gameOver = GameManager.Instance != null && GameManager.Instance.isGameOver;
        GUI.enabled = !gameOver;  // don't fight the game-over timeScale = 0
        GUILayout.BeginHorizontal();
        GUILayout.Label("Speed", GUILayout.Width(50));
        if (GUILayout.Button("1x")) Time.timeScale = 1f;
        if (GUILayout.Button("2x")) Time.timeScale = 2f;
        if (GUILayout.Button("4x")) Time.timeScale = 4f;
        GUILayout.EndHorizontal();
        GUI.enabled = true;
    }

    void CheatSection()
    {
        GUILayout.Space(6);
        GUILayout.Label("Cheats", sectionStyle);

        // Waves need a campfire to march on — no target during the intro
        GUI.enabled = Spawner != null && Campfire != null;
        if (GUILayout.Button("Spawn Enemy Wave"))
        {
            Spawner.DebugSpawnWave();
        }
        GUI.enabled = true;

        // Skip the arrival timer: one survivor lands at the cove now (needs free housing)
        GUI.enabled = Campfire != null && PopulationManager.Instance != null && PopulationManager.Instance.HasAvailableHousing();
        if (GUILayout.Button("Land a Survivor (at the cove)"))
        {
            PopulationManager.Instance.SpawnArrival(false);
        }
        GUI.enabled = true;

        if (GUILayout.Button("Kill All Enemies"))
        {
            // TakeDamage (not Destroy) so the normal death path runs: kill
            // stats, spawner notify, fade-out. Destroy is deferred to end of
            // frame, so iterating the registry list here is safe.
            for (int i = Enemy.ActiveList.Count - 1; i >= 0; i--)
            {
                Enemy e = Enemy.ActiveList[i];
                if (e != null && e.CachedHealth != null) e.CachedHealth.TakeDamage(999999f);
            }
        }

        if (GUILayout.Button("Heal Everything (friendly)"))
        {
            HealList(Worker.ActiveList);
            HealList(Warrior.ActiveList);
            HealList(BaseBuilding.ActiveList);
            HealList(Hut.ActiveList);
            HealList(Watchtower.ActiveList);
            HealList(Workshop.ActiveList);
            HealList(Wall.ActiveList);
            HealList(Gate.ActiveList);
        }

        GUI.enabled = ConstructionSite.ActiveList.Count > 0;
        if (GUILayout.Button("Finish All Construction (" + ConstructionSite.ActiveList.Count + ")"))
        {
            // Complete() destroys the site (deferred) and spawns the finished
            // building — registry unregisters in OnDestroy, so the list is
            // stable during this loop.
            for (int i = ConstructionSite.ActiveList.Count - 1; i >= 0; i--)
            {
                ConstructionSite site = ConstructionSite.ActiveList[i];
                if (site != null) site.AddProgress(1f);
            }
        }
        GUI.enabled = true;
    }

    void HealList<T>(System.Collections.Generic.IReadOnlyList<T> list) where T : ITargetable
    {
        for (int i = 0; i < list.Count; i++)
        {
            T entry = list[i];
            if (entry == null) continue;
            Health h = entry.CachedHealth;
            if (h != null && h.IsAlive) h.Heal(999999f);
        }
    }

    // ------------------------------------------------------------------
    // Quick-start colony
    // ------------------------------------------------------------------

    IEnumerator SpawnColonyRoutine()
    {
        spawningColony = true;

        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            rm.AddWood(1000); rm.AddFood(1000); rm.AddStone(1000); rm.AddMetal(1000);
        }

        // 1. Campfire — skip the intro if it's still running
        if (GameStartController.IntroInProgress && GameStartController.Instance != null)
        {
            GameStartController.Instance.DebugForceColonyStart();

            // Wait for the campfire to exist AND its Start() to have run
            // (housing registers there — AssignWorker no-ops without it)
            float deadline = Time.unscaledTime + 3f;
            while (Campfire == null && Time.unscaledTime < deadline) yield return null;
            yield return null;
        }

        BaseBuilding fire = Campfire;
        if (fire == null)
        {
            spawningColony = false;
            yield break;
        }

        // 2. Huts on a ring around the campfire (two laps of candidate spots)
        int placed = 0;
        BuildingData hutData = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(BuildingType.Hut) : null;
        if (hutCount > 0 && hutData != null && hutData.finishedBuildingPrefab != null)
        {
            int buildingsLayer = LayerMask.NameToLayer("Buildings");
            for (int i = 0; i < 16 && placed < hutCount; i++)
            {
                float angle = i * (360f / 8f) * Mathf.Deg2Rad;   // 8 spots per lap
                float radius = 7f + 4f * (i / 8);                 // lap 2 farther out
                Vector3 pos = fire.transform.position
                    + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                pos = GridSnap.SnapXZ(pos, 1f);
                pos.y = TerrainGrid.Instance != null ? TerrainGrid.Instance.SampleHeight(pos) : 0f;

                if (!IsClearForHut(pos)) continue;

                // Terrain T2: level a pad like real placement does
                if (TerrainGrid.Instance != null)
                {
                    TerrainGrid.Instance.FlattenArea(pos, 1.8f, 1.4f);
                    pos.y = TerrainGrid.Instance.SampleHeight(pos);
                }

                GameObject hut = Instantiate(hutData.finishedBuildingPrefab, pos, Quaternion.identity);
                if (buildingsLayer >= 0) hut.layer = buildingsLayer;
                placed++;
            }
        }
        if (placed > 0) yield return null;  // let Hut.Start() register housing

        // 3. People. Colonists are a pool now: land as many survivors as housing
        //    allows (beside the fire, not at the cove), then arm warriors from the
        //    idle pool and hand out jobs. Each step is capped by supply inside.
        var pm = PopulationManager.Instance;
        if (pm != null)
        {
            int wanted = woodWorkerCount + foodWorkerCount + stoneWorkerCount + warriorCount;
            for (int i = 0; i < wanted && pm.SpawnArrival(true) != null; i++) { }
            yield return null;   // let the colonists' Start() run before they are converted/assigned
        }
        for (int i = 0; i < warriorCount; i++) fire.SpawnWarrior();
        for (int i = 0; i < woodWorkerCount; i++) fire.AssignWorker(ResourceNode.ResourceType.Wood);
        for (int i = 0; i < foodWorkerCount; i++) fire.AssignWorker(ResourceNode.ResourceType.Food);
        for (int i = 0; i < stoneWorkerCount; i++) fire.AssignWorker(ResourceNode.ResourceType.Stone);

        spawningColony = false;
    }

    bool IsClearForHut(Vector3 pos)
    {
        // Dry land + on the NavMesh (terrain-aware when the island exists)
        if (TerrainGrid.Instance != null)
        {
            if (!TerrainGrid.Instance.IsBuildable(pos)) return false;
        }
        else if (Mathf.Abs(pos.x) > 63f || Mathf.Abs(pos.z) > 63f) return false;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(pos, out hit, 1f, NavMesh.AllAreas)) return false;

        // Clear of existing buildings, sites, and resource nodes (simple radial
        // checks — debug spawning, not the full placement validator)
        if (!ClearOf(BaseBuilding.ActiveList, pos, 5f)) return false;
        if (!ClearOf(Hut.ActiveList, pos, 4.5f)) return false;
        if (!ClearOf(Watchtower.ActiveList, pos, 4.5f)) return false;
        if (!ClearOf(ConstructionSite.ActiveList, pos, 4f)) return false;
        if (!ClearOf(ResourceNode.ActiveList, pos, 3f)) return false;
        return true;
    }

    bool ClearOf<T>(System.Collections.Generic.IReadOnlyList<T> list, Vector3 pos, float minDist) where T : MonoBehaviour
    {
        float minSqr = minDist * minDist;
        for (int i = 0; i < list.Count; i++)
        {
            T entry = list[i];
            if (entry == null) continue;
            Vector3 d = entry.transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude < minSqr) return false;
        }
        return true;
    }
}
#endif

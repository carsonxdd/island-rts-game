using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton that caches global world state for AI evaluation.
/// Updated periodically to avoid redundant calculations across units.
/// Zero GC: all arrays pre-allocated.
/// </summary>
public class AIWorldState : MonoBehaviour
{
    public static AIWorldState Instance { get; private set; }

    // --- Time of day ---
    public float timeOfDay { get; private set; }
    public bool isNight { get; private set; }
    public float dayProgress { get; private set; } // 0 = full night, 1 = full day

    // --- Enemy density grid ---
    // Divides the world into cells and counts enemies per cell for O(1) threat lookups
    private const float CELL_SIZE = 10f;
    private const int GRID_SIZE = 30; // 300x300 world units
    private const int GRID_OFFSET = GRID_SIZE / 2;
    private readonly int[,] enemyDensityGrid = new int[GRID_SIZE, GRID_SIZE];
    private int densityUpdateFrame = -1;
    private int densityUpdateInterval = 10; // Rebuild every 10 frames

    // --- Campfire state ---
    public float campfireHealthPercent { get; private set; }
    public Vector3 campfirePosition { get; private set; }
    public bool campfireExists { get; private set; }

    // --- Walls-under-attack cache ---
    // Tracks which walls/gates are being attacked by enemies (refreshed periodically)
    private readonly List<Transform> wallsUnderAttack = new List<Transform>();
    private float wallAttackCheckTimer = 0f;
    private float wallAttackCheckInterval = 1f;

    // Cached DayNightCycle reference
    private DayNightCycle cachedDayNight;
    private bool dayNightCached = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Auto-create singleton if not present in scene.
    /// Called by AIBrain when first unit initializes.
    /// </summary>
    public static void EnsureExists()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("AIWorldState");
            go.AddComponent<AIWorldState>();
        }
    }

    void Update()
    {
        UpdateTimeOfDay();
        UpdateCampfireState();

        // Rebuild enemy density grid periodically
        if (Time.frameCount - densityUpdateFrame >= densityUpdateInterval)
        {
            densityUpdateFrame = Time.frameCount;
            RebuildEnemyDensityGrid();
        }

        // Check walls under attack
        wallAttackCheckTimer -= Time.deltaTime;
        if (wallAttackCheckTimer <= 0f)
        {
            wallAttackCheckTimer = wallAttackCheckInterval;
            UpdateWallsUnderAttack();
        }
    }

    void UpdateTimeOfDay()
    {
        if (!dayNightCached)
        {
            cachedDayNight = FindFirstObjectByType<DayNightCycle>();
            dayNightCached = true;
        }
        DayNightCycle cycle = cachedDayNight;
        if (cycle != null)
        {
            timeOfDay = cycle.GetTimeOfDay();
            isNight = cycle.IsNightTime();

            // Calculate day progress (0 = night, 1 = full day)
            if (timeOfDay < 0.25f)
                dayProgress = 0f;
            else if (timeOfDay < 0.3f)
                dayProgress = (timeOfDay - 0.25f) / 0.05f;
            else if (timeOfDay < 0.7f)
                dayProgress = 1f;
            else if (timeOfDay < 0.75f)
                dayProgress = 1f - ((timeOfDay - 0.7f) / 0.05f);
            else
                dayProgress = 0f;
        }
    }

    void UpdateCampfireState()
    {
        if (BaseBuilding.ActiveList.Count > 0)
        {
            BaseBuilding campfire = BaseBuilding.ActiveList[0];
            if (campfire != null)
            {
                campfireExists = true;
                campfirePosition = campfire.transform.position;
                campfireHealthPercent = campfire.GetHealthPercentage();
            }
            else
            {
                campfireExists = false;
            }
        }
        else
        {
            campfireExists = false;
        }
    }

    void RebuildEnemyDensityGrid()
    {
        // Clear grid
        System.Array.Clear(enemyDensityGrid, 0, GRID_SIZE * GRID_SIZE);

        // Count enemies per cell
        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;

            Vector3 pos = enemy.transform.position;
            int gx = Mathf.Clamp(Mathf.FloorToInt(pos.x / CELL_SIZE) + GRID_OFFSET, 0, GRID_SIZE - 1);
            int gz = Mathf.Clamp(Mathf.FloorToInt(pos.z / CELL_SIZE) + GRID_OFFSET, 0, GRID_SIZE - 1);
            enemyDensityGrid[gx, gz]++;
        }
    }

    /// <summary>
    /// Get the number of enemies in a 3x3 cell area around the given position.
    /// O(9) operation replaces O(n) distance scans.
    /// </summary>
    public int GetNearbyEnemyCount(Vector3 position)
    {
        int cx = Mathf.Clamp(Mathf.FloorToInt(position.x / CELL_SIZE) + GRID_OFFSET, 0, GRID_SIZE - 1);
        int cz = Mathf.Clamp(Mathf.FloorToInt(position.z / CELL_SIZE) + GRID_OFFSET, 0, GRID_SIZE - 1);

        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            int gx = cx + dx;
            if (gx < 0 || gx >= GRID_SIZE) continue;
            for (int dz = -1; dz <= 1; dz++)
            {
                int gz = cz + dz;
                if (gz < 0 || gz >= GRID_SIZE) continue;
                count += enemyDensityGrid[gx, gz];
            }
        }
        return count;
    }

    void UpdateWallsUnderAttack()
    {
        wallsUnderAttack.Clear();

        // Check enemies that are attacking walls
        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;

            // We check via the enemy's agent destination against WallGrid
            UnityEngine.AI.NavMeshAgent enemyAgent = enemy.CachedAgent;
            if (enemyAgent == null || !enemyAgent.hasPath) continue;

            if (WallGrid.Instance != null)
            {
                Vector2Int destGrid = WallGrid.Instance.WorldToGrid(enemyAgent.destination);
                if (WallGrid.Instance.HasWallAt(destGrid))
                {
                    // Find the wall/gate transform at that position
                    MonoBehaviour wallAtPos = WallGrid.Instance.GetWallAt(destGrid);
                    if (wallAtPos != null && !wallsUnderAttack.Contains(wallAtPos.transform))
                    {
                        wallsUnderAttack.Add(wallAtPos.transform);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get the nearest wall/gate that is currently under attack by enemies.
    /// Returns null if no walls are under attack.
    /// </summary>
    public Transform GetNearestWallUnderAttack(Vector3 fromPosition, out float distance)
    {
        Transform nearest = null;
        distance = float.MaxValue;

        for (int i = 0; i < wallsUnderAttack.Count; i++)
        {
            Transform wall = wallsUnderAttack[i];
            if (wall == null) continue;

            float dist = Vector3.Distance(fromPosition, wall.position);
            if (dist < distance)
            {
                distance = dist;
                nearest = wall;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Check if any walls/gates are currently under attack.
    /// </summary>
    public bool AreWallsUnderAttack()
    {
        return wallsUnderAttack.Count > 0;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
    }
}

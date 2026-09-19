using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton cache of the world facts that many units would otherwise each compute for
/// themselves every brain tick: time of day, where fighters are massed, and which walls
/// are being hit.
/// </summary>
/// <remarks>
/// Everything here is refreshed on its own schedule and read for free by considerations,
/// which is the point: one shared scan per interval instead of one per unit per tick.
/// All storage is pre-allocated, so the refreshes allocate nothing.
/// Auto-created by the first AIBrain via EnsureExists, so no scene wiring is needed.
///
/// Lap step 1 commit 5 (2026-09-09): the density grid is one grid PER FACTION
/// (raiders and every colony's warriors), and <see cref="GetNearbyHostileCount"/> sums
/// the grids of the factions hostile to the asker. The walls-under-attack list carries
/// each wall's owner so a warrior only defends its own colony's walls. The campfire
/// state that lived here moved to <c>Faction.Campfire</c>. Time of day stays global.
/// </remarks>
public class AIWorldState : MonoBehaviour
{
    public static AIWorldState Instance { get; private set; }

    // --- Time of day ---
    public float timeOfDay { get; private set; }
    public bool isNight { get; private set; }
    public float dayProgress { get; private set; } // 0 = full night, 1 = full day

    // --- Fighter density grids, one per faction id ---
    // A coarse bucket count per cell. GetNearbyHostileCount then answers "how
    // dangerous is it here for me" by summing a 3x3 block (~30x30 world units) of
    // every hostile faction's grid instead of distance-testing every fighter.
    private const float CELL_SIZE = 10f;
    private const int GRID_SIZE = 30; // 300x300 world units
    private const int GRID_OFFSET = GRID_SIZE / 2;
    private readonly int[] densityGrids = new int[Relations.MaxFactions * GRID_SIZE * GRID_SIZE];
    private int densityUpdateFrame = -1;
    private int densityUpdateInterval = 10; // Rebuild every 10 frames - threat shifts slowly

    // --- Walls-under-attack cache ---
    // Drives the warrior DefendWall action. Inferred from where hostile fighters are
    // HEADING (their agent destination lands on a wall cell) rather than from damage
    // events, so warriors start moving while the wall is still being approached.
    private struct WallHit { public Transform wall; public Faction owner; }
    private readonly List<WallHit> wallsUnderAttack = new List<WallHit>();
    private float wallAttackCheckTimer = 0f;
    private float wallAttackCheckInterval = 1f;

    // Looked up once - there is exactly one cycle and it lives for the whole scene.
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

        // Rebuild the density grids periodically
        if (Time.frameCount - densityUpdateFrame >= densityUpdateInterval)
        {
            densityUpdateFrame = Time.frameCount;
            RebuildDensityGrids();
        }

        // Check walls under attack
        wallAttackCheckTimer -= Time.deltaTime;
        if (wallAttackCheckTimer <= 0f)
        {
            wallAttackCheckTimer = wallAttackCheckInterval;
            UpdateWallsUnderAttack();
        }
    }

    /// <summary>
    /// Mirrors the day/night clock and derives dayProgress: 1 during the day, 0 at night,
    /// with short ramps at dawn and dusk so light-sensitive actions ease across the
    /// boundary instead of snapping. These windows are deliberately narrower than the
    /// visual dawn/dusk blend - AI behaviour should not drift when the lighting is retuned.
    /// </summary>
    void UpdateTimeOfDay()
    {
        if (!dayNightCached)
        {
            cachedDayNight = FindAnyObjectByType<DayNightCycle>();
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

    static int Cell(Vector3 pos, out int gx, out int gz)
    {
        gx = Mathf.Clamp(Mathf.FloorToInt(pos.x / CELL_SIZE) + GRID_OFFSET, 0, GRID_SIZE - 1);
        gz = Mathf.Clamp(Mathf.FloorToInt(pos.z / CELL_SIZE) + GRID_OFFSET, 0, GRID_SIZE - 1);
        return gx * GRID_SIZE + gz;
    }

    /// <summary>Re-buckets every live fighter into its faction's grid. Cheap: two passes, no allocation.</summary>
    void RebuildDensityGrids()
    {
        System.Array.Clear(densityGrids, 0, densityGrids.Length);

        var enemies = Enemy.ActiveList;
        for (int i = 0; i < enemies.Count; i++)
        {
            Enemy e = enemies[i];
            if (e == null) continue;
            int gx, gz;
            densityGrids[e.Faction.Id * GRID_SIZE * GRID_SIZE + Cell(e.transform.position, out gx, out gz)]++;
        }
        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++)
        {
            Warrior w = warriors[i];
            if (w == null) continue;
            int gx, gz;
            densityGrids[w.Faction.Id * GRID_SIZE * GRID_SIZE + Cell(w.transform.position, out gx, out gz)]++;
        }
    }

    /// <summary>
    /// Fighters hostile to <paramref name="me"/> in the 3x3 cell block around a
    /// position. O(9 × factions) replaces O(n) distance scans.
    /// </summary>
    public int GetNearbyHostileCount(Vector3 position, Faction me)
    {
        int cx, cz;
        Cell(position, out cx, out cz);

        int count = 0;
        var all = Factions.All;
        for (int f = 0; f < all.Count; f++)
        {
            Faction other = all[f];
            if (!me.IsHostileTo(other)) continue;
            int baseIndex = other.Id * GRID_SIZE * GRID_SIZE;
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx;
                if (gx < 0 || gx >= GRID_SIZE) continue;
                for (int dz = -1; dz <= 1; dz++)
                {
                    int gz = cz + dz;
                    if (gz < 0 || gz >= GRID_SIZE) continue;
                    count += densityGrids[baseIndex + gx * GRID_SIZE + gz];
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Rebuilds the walls-under-attack list by asking where each raider is walking: if its
    /// agent destination maps to a cell holding a wall or gate, that piece is treated as
    /// under attack, tagged with the wall's owner.
    /// </summary>
    void UpdateWallsUnderAttack()
    {
        wallsUnderAttack.Clear();
        if (WallGrid.Instance == null) return;

        var enemies = Enemy.ActiveList;
        for (int i = 0; i < enemies.Count; i++)
        {
            Enemy enemy = enemies[i];
            if (enemy == null) continue;
            AddWallTarget(enemy.CachedAgent);
        }
        // Another colony's warriors walking at a wall count too (rival landings, step 3)
        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++)
        {
            Warrior w = warriors[i];
            if (w == null) continue;
            AddWallTarget(w.CachedAgent);
        }
    }

    void AddWallTarget(UnityEngine.AI.NavMeshAgent agent)
    {
        if (agent == null || !agent.hasPath) return;
        Vector2Int destGrid = WallGrid.Instance.WorldToGrid(agent.destination);
        if (!WallGrid.Instance.HasWallAt(destGrid)) return;

        MonoBehaviour wallAtPos = WallGrid.Instance.GetWallAt(destGrid);
        if (wallAtPos == null) return;
        for (int i = 0; i < wallsUnderAttack.Count; i++)
            if (wallsUnderAttack[i].wall == wallAtPos.transform) return;

        IOwned owned = wallAtPos as IOwned;
        wallsUnderAttack.Add(new WallHit { wall = wallAtPos.transform, owner = owned != null ? owned.Faction : null });
    }

    /// <summary>
    /// The nearest wall/gate of <paramref name="me"/>'s that something is walking at.
    /// Returns null if none of that colony's walls are under attack.
    /// </summary>
    public Transform GetNearestWallUnderAttack(Vector3 fromPosition, Faction me, out float distance)
    {
        Transform nearest = null;
        distance = float.MaxValue;

        for (int i = 0; i < wallsUnderAttack.Count; i++)
        {
            WallHit hit = wallsUnderAttack[i];
            if (hit.wall == null || hit.owner != me) continue;

            float dist = Vector3.Distance(fromPosition, hit.wall.position);
            if (dist < distance)
            {
                distance = dist;
                nearest = hit.wall;
            }
        }

        return nearest;
    }

    /// <summary>Whether any of <paramref name="me"/>'s walls/gates are currently under attack.</summary>
    public bool AreWallsUnderAttack(Faction me)
    {
        for (int i = 0; i < wallsUnderAttack.Count; i++)
            if (wallsUnderAttack[i].owner == me && wallsUnderAttack[i].wall != null) return true;
        return false;
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

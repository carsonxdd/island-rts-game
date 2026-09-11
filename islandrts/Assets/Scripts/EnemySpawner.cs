using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Lands a raid: given a head count from <see cref="RaidDirector"/>, spawns that many
/// enemies offshore so the raid wades in from one direction, and clears whatever is
/// left at dawn.
/// </summary>
/// <remarks>
/// This class no longer decides WHEN or HOW MANY (2026-09-02) — the director rolls
/// that at dawn from the calendar and the colony's prosperity. It only owns the
/// mechanics: the ring, the clustering, the stagger, the dawn despawn.
///
/// A raid arrives as one body rather than a trickle - spawns are only fractions of a
/// second apart and clustered around a single randomly chosen bearing - because a trickle
/// lets a couple of warriors defeat a whole raid in detail.
/// Anything still alive at dawn is despawned; raids never overlap.
/// </remarks>
public class EnemySpawner : MonoBehaviour
{
    [Header("Enemy Prefab")]
    public GameObject enemyPrefab;

    [Header("Spawn Settings")]
    public float spawnDistance = 45f;           // Distance from center to spawn enemies (scene: 45)
    public float spawnHeight = 1f;              // Y position to spawn at

    [Header("Spawn Timing")]
    public float spawnDelay = 2f;               // Delay after night starts before spawning
    public float spawnInterval = 0.4f;          // Time between spawns - low enough that a raid lands as one body (scene: 0.4)

    [Header("Group Spawning")]
    public float groupSpreadAngle = 15f;        // Max angle spread within a raid group (degrees)
    public float groupSpreadDistance = 3f;       // Max distance spread within a raid group

    // Private
    private List<GameObject> activeEnemies = new List<GameObject>();
    private float waveBaseAngle = 0f;  // Chosen direction for current raid group
    private int pendingCount;          // Head count handed over by SpawnRaid, consumed by StartSpawning
    private int pendingRaidIndex;
    private bool landedThisRaid;       // OnRaidLanded fires once per raid, on the first body ashore

    /// <summary>
    /// The first raider of a raid has landed, with where. The minimap pings it, fog or
    /// not: a landing is heard along the coast (2026-09-09).
    /// </summary>
    public static event System.Action<Vector3> OnRaidLanded;

    void Awake()
    {
        // The director rides on this object so it needs no scene wiring and its
        // code defaults are the live values (see RaidDirector's remarks).
        if (GetComponent<RaidDirector>() == null) gameObject.AddComponent<RaidDirector>();
    }

    void OnEnable()
    {
        DayNightCycle.OnDayStart += HandleDayStart;
    }

    void OnDisable()
    {
        DayNightCycle.OnDayStart -= HandleDayStart;
    }

    void HandleDayStart()
    {
        CancelInvoke();  // Stop any pending spawns
        DespawnAllEnemies();
    }

    /// <summary>
    /// Land <paramref name="count"/> raiders after the usual delay. Called by the
    /// director at nightfall on raid nights, and by the F4 cheat.
    /// </summary>
    public void SpawnRaid(int count, int raidIndex)
    {
        DevQuests.Signal("raid");
        pendingCount = Mathf.Max(1, count);
        pendingRaidIndex = raidIndex;
        Invoke(nameof(StartSpawning), spawnDelay);
    }

    void StartSpawning()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("EnemySpawner: No enemy prefab assigned!");
            return;
        }

        int enemiesToSpawn = pendingCount;

        // Pick a random direction for this raid — all enemies cluster around it
        waveBaseAngle = Random.Range(0f, 360f);
        landedThisRaid = false;

        Debug.Log($"EnemySpawner: Raid {pendingRaidIndex} — {enemiesToSpawn} raiders landing from direction {waveBaseAngle:F0}°");

        // Start combat music when enemies begin spawning
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayCombatMusic();
        }

        // Spawn enemies with intervals
        for (int i = 0; i < enemiesToSpawn; i++)
        {
            Invoke(nameof(SpawnSingleEnemy), i * spawnInterval);
        }
    }

    void SpawnSingleEnemy()
    {
        // Random position around the map edge
        Vector3 spawnPos = GetRandomSpawnPosition();

        // Spawn the enemy
        GameObject enemy = Spawn.Owned(enemyPrefab, spawnPos, Quaternion.identity, Factions.Raiders);
        enemy.name = $"Enemy_{activeEnemies.Count + 1}_Raid{pendingRaidIndex}";
        enemy.transform.parent = transform;  // Organize under spawner

        // Track active enemies
        activeEnemies.Add(enemy);

        if (!landedThisRaid)
        {
            landedThisRaid = true;
            OnRaidLanded?.Invoke(spawnPos);
        }
    }

    Vector3 GetRandomSpawnPosition()
    {
        // Spawn enemies clustered together around the raid's chosen direction
        float angle = waveBaseAngle + Random.Range(-groupSpreadAngle, groupSpreadAngle);
        // spawnDistance is authored for the 150 m map; scale with the island
        float distance = spawnDistance * TerrainGrid.SizeScale + Random.Range(-groupSpreadDistance, groupSpreadDistance);

        Vector3 position = new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad) * distance,
            spawnHeight,
            Mathf.Sin(angle * Mathf.Deg2Rad) * distance
        );

        // Terrain: stand on the island surface, snapped to the NavMesh so no
        // one spawns hovering over deep water or inside a slope. The island
        // is a different shape every run, so the ring can land in the sea
        // or on a cut-off outcrop on the short axis — walk the point inward
        // toward the campfire site until it is on reachable ground.
        // "Reachable" here is a real NavMesh path to the campfire (2026-09-10),
        // not the terrain flood fill: IsReachable joins cells across 0.9 m steps
        // while the bake stops at 45°, so a cliff-face vertex read reachable and
        // the 4 m NavMesh snap dropped raiders on a disconnected sliver of mesh.
        // They stood there all night, held dawn to the cap and parked the sim's
        // camera on empty ground.
        BaseBuilding fire = Factions.Player.Campfire;
        Vector3 toward = fire != null ? fire.transform.position : Vector3.zero;
        Vector3 found;
        if (FindReachableToward(position, toward, out found)) return found;

        return position;
    }

    // ---- ground a raider can fight from (2026-09-10) -----------------------

    private static NavMeshPath reachPath;

    /// <summary>
    /// True when a NavMesh path runs from <paramref name="from"/> to the player's
    /// campfire (its nearest NavMesh point; the fire carves). No campfire = true,
    /// so the opening sequence and a dead colony never refuse every spot.
    /// </summary>
    public static bool CanReachFire(Vector3 from)
    {
        BaseBuilding fire = Factions.Player.Campfire;
        if (fire == null) return true;
        NavMeshHit fireHit;
        if (!NavMesh.SamplePosition(fire.transform.position, out fireHit, 8f, NavMesh.AllAreas)) return true;
        if (reachPath == null) reachPath = new NavMeshPath();
        return NavMesh.CalculatePath(from, fireHit.position, NavMesh.AllAreas, reachPath)
               && reachPath.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>
    /// Walks 4 m steps from <paramref name="from"/> toward <paramref name="toward"/>
    /// (twelve at most) and returns the first NavMesh point on dry ground with a
    /// path to the fire. Used by the spawn ring and by a raider that has given
    /// up on where it stands (<see cref="Enemy"/>'s progress watchdog).
    /// </summary>
    public static bool FindReachableToward(Vector3 from, Vector3 toward, out Vector3 result)
    {
        result = from;
        TerrainGrid terrain = TerrainGrid.Instance;
        if (terrain == null) return false;

        Vector3 dir = toward - from;
        dir.y = 0f;
        Vector3 step = dir.sqrMagnitude > 0.01f ? dir.normalized * 4f : Vector3.zero;
        Vector3 position = from;
        for (int i = 0; i < 12; i++)
        {
            position.y = terrain.SampleHeight(position) + 0.1f;
            NavMeshHit navHit;
            if (terrain.SampleHeight(position) > TerrainGrid.DeepWaterY
                && NavMesh.SamplePosition(position, out navHit, 4f, NavMesh.AllAreas)
                && CanReachFire(navHit.position))
            {
                result = navHit.position;
                return true;
            }
            if (step == Vector3.zero) return false;
            position += step;
        }
        return false;
    }

    void DespawnAllEnemies()
    {
        // Clean up null references first
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
            if (activeEnemies[i] == null) activeEnemies.RemoveAt(i);

        // Stagger destruction to avoid NavMesh carving spike and GC spike
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            if (activeEnemies[i] != null)
                Destroy(activeEnemies[i], i * 0.15f);
        }

        activeEnemies.Clear();
    }

    // Called when an enemy is killed (for tracking)
    public void NotifyEnemyKilled(GameObject enemy)
    {
        activeEnemies.Remove(enemy);

        // Check if all enemies are dead
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
            if (activeEnemies[i] == null) activeEnemies.RemoveAt(i);
        if (activeEnemies.Count == 0)
        {
            // Return to appropriate music based on time of day
            if (AudioManager.Instance != null)
            {
                DayNightCycle dayNight = FindAnyObjectByType<DayNightCycle>();
                if (dayNight != null && dayNight.IsNightTime())
                {
                    // Still night - return to night ambience only
                    AudioManager.Instance.PlayNightAmbience();
                }
                else
                {
                    // Day has broken - play day music
                    AudioManager.Instance.PlayDayMusic();
                }
            }
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Debug-menu hook: land a raid right now, sized exactly as the director
    /// would size one today. Does not count as a scheduled raid, so the
    /// director's quiet-night streak and tonight's roll are untouched.
    /// </summary>
    public void DebugSpawnWave()
    {
        RaidDirector director = RaidDirector.Instance;
        int count = director != null ? director.ComputeRaidSize() : 5;
        SpawnRaid(count, director != null ? director.RaidsSoFar + 1 : 1);
    }
#endif

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        // Draw spawn radius
        Gizmos.color = Color.red;
        for (int i = 0; i < 32; i++)
        {
            float angle1 = (i / 32f) * 360f;
            float angle2 = ((i + 1) / 32f) * 360f;

            Vector3 p1 = new Vector3(
                Mathf.Cos(angle1 * Mathf.Deg2Rad) * spawnDistance,
                spawnHeight,
                Mathf.Sin(angle1 * Mathf.Deg2Rad) * spawnDistance
            );

            Vector3 p2 = new Vector3(
                Mathf.Cos(angle2 * Mathf.Deg2Rad) * spawnDistance,
                spawnHeight,
                Mathf.Sin(angle2 * Mathf.Deg2Rad) * spawnDistance
            );

            Gizmos.DrawLine(p1, p2);
        }
    }
}

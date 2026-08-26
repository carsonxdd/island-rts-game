using UnityEngine;
using System.Collections.Generic;

public class EnemySpawner : MonoBehaviour
{
    [Header("Enemy Prefab")]
    public GameObject enemyPrefab;

    [Header("Spawn Settings")]
    public int baseEnemiesPerNight = 3;         // Starting number of enemies
    public float enemyIncreasePerNight = 1f;    // How many more enemies each night
    public float spawnDistance = 30f;           // Distance from center to spawn enemies
    public float spawnHeight = 1f;              // Y position to spawn at

    [Header("Spawn Timing")]
    public float spawnDelay = 2f;               // Delay after night starts before spawning
    public float spawnInterval = 1f;            // Time between each enemy spawn

    [Header("Group Spawning")]
    public float groupSpreadAngle = 15f;        // Max angle spread within a wave group (degrees)
    public float groupSpreadDistance = 3f;       // Max distance spread within a wave group

    [Header("Difficulty Scaling")]
    public bool increaseDifficulty = true;      // Increase enemies each night

    // Private
    private List<GameObject> activeEnemies = new List<GameObject>();
    private int currentNight = 0;
    private float waveBaseAngle = 0f;  // Chosen direction for current wave group

    void OnEnable()
    {
        // Subscribe to day/night events
        DayNightCycle.OnNightStart += HandleNightStart;
        DayNightCycle.OnDayStart += HandleDayStart;
    }

    void OnDisable()
    {
        // Unsubscribe from events
        DayNightCycle.OnNightStart -= HandleNightStart;
        DayNightCycle.OnDayStart -= HandleDayStart;
    }

    void HandleNightStart()
    {
        currentNight++;

        // Start spawning enemies after a delay (StartSpawning computes the count)
        Invoke(nameof(StartSpawning), spawnDelay);
    }

    void HandleDayStart()
    {
        CancelInvoke();  // Stop any pending spawns
        DespawnAllEnemies();
    }

    int CalculateEnemyCount()
    {
        if (!increaseDifficulty)
        {
            return baseEnemiesPerNight;
        }

        // Increase enemies each night
        int count = Mathf.RoundToInt(baseEnemiesPerNight + (currentNight - 1) * enemyIncreasePerNight);
        return Mathf.Max(1, count);  // At least 1 enemy
    }

    void StartSpawning()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("EnemySpawner: No enemy prefab assigned!");
            return;
        }

        int enemiesToSpawn = CalculateEnemyCount();

        // Pick a random direction for this wave — all enemies cluster around it
        waveBaseAngle = Random.Range(0f, 360f);

        Debug.Log($"EnemySpawner: Spawning {enemiesToSpawn} enemies for night {currentNight} from direction {waveBaseAngle:F0}°");

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
        GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        enemy.name = $"Enemy_{activeEnemies.Count + 1}_Night{currentNight}";
        enemy.transform.parent = transform;  // Organize under spawner

        // Track active enemies
        activeEnemies.Add(enemy);
    }

    Vector3 GetRandomSpawnPosition()
    {
        // Spawn enemies clustered together around the wave's chosen direction
        float angle = waveBaseAngle + Random.Range(-groupSpreadAngle, groupSpreadAngle);
        float distance = spawnDistance + Random.Range(-groupSpreadDistance, groupSpreadDistance);

        Vector3 position = new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad) * distance,
            spawnHeight,
            Mathf.Sin(angle * Mathf.Deg2Rad) * distance
        );

        // Terrain (T1): stand on the island surface, snapped to the NavMesh
        // so no one spawns hovering over deep water or inside a slope
        if (TerrainGrid.Instance != null)
        {
            position.y = TerrainGrid.Instance.SampleHeight(position) + 0.1f;
            UnityEngine.AI.NavMeshHit navHit;
            if (UnityEngine.AI.NavMesh.SamplePosition(position, out navHit, 8f, UnityEngine.AI.NavMesh.AllAreas))
            {
                position = navHit.position;
            }
        }

        return position;
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

    // Public getters
    public int GetCurrentNight()
    {
        return currentNight;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Debug-menu hook: spawn a wave right now, scaled as if it were at
    /// least night 1. currentNight is restored immediately — StartSpawning
    /// computes the count synchronously (the Invokes only stagger the
    /// per-enemy instantiation), so real night scaling is unaffected.
    /// </summary>
    public void DebugSpawnWave()
    {
        int saved = currentNight;
        currentNight = Mathf.Max(1, currentNight);
        StartSpawning();
        currentNight = saved;
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

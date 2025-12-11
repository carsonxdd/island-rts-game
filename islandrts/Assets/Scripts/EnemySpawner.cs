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

    [Header("Difficulty Scaling")]
    public bool increaseDifficulty = true;      // Increase enemies each night

    // Private
    private List<GameObject> activeEnemies = new List<GameObject>();
    private int currentNight = 0;

    void OnEnable()
    {
        // Subscribe to day/night events
        DayNightCycle.OnNightStart += HandleNightStart;
        DayNightCycle.OnDayStart += HandleDayStart;
        Debug.Log("EnemySpawner: Subscribed to day/night events");
    }

    void Start()
    {
        Debug.Log($"EnemySpawner: Initialized. Enemy prefab assigned: {enemyPrefab != null}");
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
        Debug.Log($"EnemySpawner: Night {currentNight} - Spawning enemies...");

        // Calculate how many enemies to spawn this night
        int enemiesToSpawn = CalculateEnemyCount();

        // Start spawning enemies after a delay
        Invoke(nameof(StartSpawning), spawnDelay);
    }

    void HandleDayStart()
    {
        Debug.Log($"EnemySpawner: Day breaks - Despawning remaining enemies");
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

        Debug.Log($"EnemySpawner: Spawning {enemiesToSpawn} enemies for night {currentNight}");

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

        Debug.Log($"EnemySpawner: Spawned enemy at {spawnPos}. Total active: {activeEnemies.Count}");
    }

    Vector3 GetRandomSpawnPosition()
    {
        // Spawn enemies in a circle around the center
        float angle = Random.Range(0f, 360f);
        float distance = Random.Range(spawnDistance * 0.8f, spawnDistance * 1.2f);

        Vector3 position = new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad) * distance,
            spawnHeight,
            Mathf.Sin(angle * Mathf.Deg2Rad) * distance
        );

        return position;
    }

    void DespawnAllEnemies()
    {
        // Clean up null references first
        activeEnemies.RemoveAll(enemy => enemy == null);

        // Destroy all remaining enemies
        foreach (GameObject enemy in activeEnemies)
        {
            if (enemy != null)
            {
                Destroy(enemy);
            }
        }

        activeEnemies.Clear();
        Debug.Log("EnemySpawner: All enemies despawned");
    }

    // Called when an enemy is killed (for tracking)
    public void NotifyEnemyKilled(GameObject enemy)
    {
        activeEnemies.Remove(enemy);
        Debug.Log($"EnemySpawner: Enemy killed. {activeEnemies.Count} enemies remaining");

        // Check if all enemies are dead
        activeEnemies.RemoveAll(e => e == null);
        if (activeEnemies.Count == 0)
        {
            Debug.Log("EnemySpawner: All enemies defeated! Returning to ambient sounds.");

            // Return to appropriate music based on time of day
            if (AudioManager.Instance != null)
            {
                DayNightCycle dayNight = FindFirstObjectByType<DayNightCycle>();
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
    public int GetActiveEnemyCount()
    {
        activeEnemies.RemoveAll(enemy => enemy == null);
        return activeEnemies.Count;
    }

    public int GetCurrentNight()
    {
        return currentNight;
    }

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

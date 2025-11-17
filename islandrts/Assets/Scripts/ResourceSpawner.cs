using UnityEngine;
using System.Collections.Generic;

public class ResourceSpawner : MonoBehaviour
{
    [Header("Resource Prefabs")]
    public GameObject treePrefab;
    public GameObject berryBushPrefab;
    public GameObject rockNodePrefab;

    [Header("Spawn Counts")]
    public int treeCount = 15;
    public int berryBushCount = 10;
    public int rockNodeCount = 8;

    [Header("Spawn Area")]
    public Vector2 spawnAreaMin = new Vector2(-20f, -20f);  // Min X/Z
    public Vector2 spawnAreaMax = new Vector2(20f, 20f);    // Max X/Z
    public float spawnHeight = 0f;  // Y position

    [Header("Spacing")]
    public float minDistanceBetweenNodes = 2f;  // Minimum space between resources
    public float minDistanceFromCampfire = 5f;  // Keep resources away from campfire
    public float minDistanceFromBuildings = 3f;  // Keep resources away from buildings

    [Header("Respawning")]
    public bool enableRespawning = true;  // Toggle resource respawning
    public float respawnDelay = 10f;  // Seconds before respawning a depleted resource

    private List<Vector3> spawnedPositions = new List<Vector3>();
    private Vector3 campfirePosition = Vector3.zero;  // Campfire is usually at world origin

    // Singleton pattern for easy access from ResourceNode
    public static ResourceSpawner Instance { get; private set; }

    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("ResourceSpawner: Multiple ResourceSpawners detected! Destroying duplicate.");
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Find the campfire position using BaseBuilding component
        BaseBuilding campfire = FindFirstObjectByType<BaseBuilding>();
        if (campfire != null)
        {
            campfirePosition = campfire.transform.position;
            Debug.Log($"ResourceSpawner: Found campfire at {campfirePosition}");
        }
        else
        {
            Debug.LogWarning("ResourceSpawner: Campfire not found! Using world origin (0,0,0) as default.");
            campfirePosition = Vector3.zero;
        }

        SpawnAllResources();
    }

    void SpawnAllResources()
    {
        Debug.Log("ResourceSpawner: Starting resource spawn...");

        // Spawn trees
        SpawnResourceType(treePrefab, treeCount, "Tree");

        // Spawn berry bushes
        SpawnResourceType(berryBushPrefab, berryBushCount, "BerryBush");

        // Spawn rocks
        SpawnResourceType(rockNodePrefab, rockNodeCount, "RockNode");

        Debug.Log($"ResourceSpawner: Spawned {spawnedPositions.Count} total resource nodes!");
    }

    void SpawnResourceType(GameObject prefab, int count, string resourceName)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"ResourceSpawner: {resourceName} prefab is not assigned!");
            return;
        }

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = count * 10;  // Try 10x the count to find valid positions

        while (spawned < count && attempts < maxAttempts)
        {
            attempts++;

            // Generate random position within spawn area
            Vector3 randomPos = new Vector3(
                Random.Range(spawnAreaMin.x, spawnAreaMax.x),
                spawnHeight,
                Random.Range(spawnAreaMin.y, spawnAreaMax.y)
            );

            // Check if position is far enough from other resources
            if (IsPositionValid(randomPos))
            {
                // Spawn the resource
                GameObject spawnedNode = Instantiate(prefab, randomPos, Quaternion.identity);
                spawnedNode.name = $"{resourceName}_{spawned + 1}";
                spawnedNode.transform.parent = transform;  // Organize under spawner

                // Track this position
                spawnedPositions.Add(randomPos);
                spawned++;
            }
        }

        if (spawned < count)
        {
            Debug.LogWarning($"ResourceSpawner: Only spawned {spawned}/{count} {resourceName}s (ran out of valid positions)");
        }
        else
        {
            Debug.Log($"ResourceSpawner: Spawned {spawned} {resourceName}s");
        }
    }

    bool IsPositionValid(Vector3 position)
    {
        // Check distance from campfire first
        float distanceFromCampfire = Vector3.Distance(position, campfirePosition);
        if (distanceFromCampfire < minDistanceFromCampfire)
        {
            return false;  // Too close to campfire
        }

        // Check distance from all previously spawned resources
        foreach (Vector3 spawnedPos in spawnedPositions)
        {
            float distance = Vector3.Distance(position, spawnedPos);
            if (distance < minDistanceBetweenNodes)
            {
                return false;  // Too close to another resource
            }
        }

        // Check distance from all buildings (BaseBuilding, Hut, ConstructionSite)
        if (!IsPositionClearOfBuildings(position))
        {
            return false;  // Too close to a building
        }

        return true;  // Position is valid
    }

    bool IsPositionClearOfBuildings(Vector3 position)
    {
        // Check BaseBuilding (Campfire)
        BaseBuilding[] baseBuildings = FindObjectsByType<BaseBuilding>(FindObjectsSortMode.None);
        foreach (BaseBuilding building in baseBuildings)
        {
            if (building == null) continue;
            float distance = Vector3.Distance(position, building.transform.position);
            if (distance < minDistanceFromBuildings)
            {
                return false;
            }
        }

        // Check Huts
        Hut[] huts = FindObjectsByType<Hut>(FindObjectsSortMode.None);
        foreach (Hut hut in huts)
        {
            if (hut == null) continue;
            float distance = Vector3.Distance(position, hut.transform.position);
            if (distance < minDistanceFromBuildings)
            {
                return false;
            }
        }

        // Check ConstructionSites
        ConstructionSite[] constructionSites = FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None);
        foreach (ConstructionSite site in constructionSites)
        {
            if (site == null) continue;
            float distance = Vector3.Distance(position, site.transform.position);
            if (distance < minDistanceFromBuildings)
            {
                return false;
            }
        }

        return true;  // Clear of all buildings
    }

    // Called by ResourceNode when it's about to be destroyed
    public void NotifyResourceDepleted(ResourceNode.ResourceType resourceType, Vector3 depletedPosition)
    {
        if (!enableRespawning)
        {
            Debug.Log($"ResourceSpawner: {resourceType} depleted but respawning is disabled.");
            return;
        }

        // Remove the depleted position from tracking
        spawnedPositions.Remove(depletedPosition);

        Debug.Log($"ResourceSpawner: {resourceType} depleted at {depletedPosition}. Scheduling respawn in {respawnDelay}s...");

        // Schedule respawn after delay
        Invoke(nameof(RespawnResource), respawnDelay);
    }

    // Respawn a single resource of random type
    void RespawnResource()
    {
        // Randomly pick a resource type to respawn
        int randomType = Random.Range(0, 3);
        GameObject prefabToSpawn = null;
        string resourceName = "";

        switch (randomType)
        {
            case 0:
                prefabToSpawn = treePrefab;
                resourceName = "Tree";
                break;
            case 1:
                prefabToSpawn = berryBushPrefab;
                resourceName = "BerryBush";
                break;
            case 2:
                prefabToSpawn = rockNodePrefab;
                resourceName = "RockNode";
                break;
        }

        if (prefabToSpawn == null)
        {
            Debug.LogWarning($"ResourceSpawner: Cannot respawn - {resourceName} prefab not assigned!");
            return;
        }

        // Try to find a valid spawn position
        int attempts = 0;
        int maxAttempts = 50;  // Try 50 times to find a valid position

        while (attempts < maxAttempts)
        {
            attempts++;

            // Generate random position within spawn area
            Vector3 randomPos = new Vector3(
                Random.Range(spawnAreaMin.x, spawnAreaMax.x),
                spawnHeight,
                Random.Range(spawnAreaMin.y, spawnAreaMax.y)
            );

            // Check if position is valid
            if (IsPositionValid(randomPos))
            {
                // Spawn the resource
                GameObject spawnedNode = Instantiate(prefabToSpawn, randomPos, Quaternion.identity);
                spawnedNode.name = $"{resourceName}_Respawned";
                spawnedNode.transform.parent = transform;  // Organize under spawner

                // Track this position
                spawnedPositions.Add(randomPos);

                Debug.Log($"ResourceSpawner: Respawned {resourceName} at {randomPos}");
                return;  // Success!
            }
        }

        // Failed to find valid position after max attempts
        Debug.LogWarning($"ResourceSpawner: Failed to respawn {resourceName} - no valid positions found after {maxAttempts} attempts");
    }

    // Visual helper in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw spawn area boundaries
        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3(
            (spawnAreaMin.x + spawnAreaMax.x) / 2f,
            spawnHeight,
            (spawnAreaMin.y + spawnAreaMax.y) / 2f
        );
        Vector3 size = new Vector3(
            spawnAreaMax.x - spawnAreaMin.x,
            0.1f,
            spawnAreaMax.y - spawnAreaMin.y
        );
        Gizmos.DrawWireCube(center, size);
    }
}

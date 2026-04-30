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
    public float minDistanceFromBuildings = 5f;  // Keep resources away from buildings

    [Header("Tree Clustering")]
    public int treeClusters = 3;              // Number of forest clusters
    public float clusterRadius = 6f;          // How spread out trees are within a cluster
    public float minTreeSpacing = 1.5f;       // Minimum distance between trees in a cluster
    public float minClusterDistFromCampfire = 8f;  // Keep forests away from campfire

    [Header("Scattered Trees")]
    public int scatteredTreeCount = 6;        // Extra trees randomly placed between clusters
    public float minScatteredTreeSpacing = 3f;  // Minimum distance between scattered trees

    [Header("Respawning")]
    public bool enableRespawning = true;  // Toggle resource respawning
    public float respawnDelay = 10f;  // Seconds before respawning a depleted resource

    private List<Vector3> spawnedPositions = new List<Vector3>();
    private List<Vector3> clusterCenters = new List<Vector3>();  // Track forest centers for respawning
    private Vector3 campfirePosition = Vector3.zero;

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
        // Spawn trees in clusters (forests)
        SpawnTreeClusters();

        // Spawn scattered individual trees between clusters
        SpawnScatteredTrees();

        // Spawn berry bushes (scattered)
        SpawnResourceType(berryBushPrefab, berryBushCount, "BerryBush");

        // Spawn rocks (scattered)
        SpawnResourceType(rockNodePrefab, rockNodeCount, "RockNode");
    }

    void SpawnTreeClusters()
    {
        if (treePrefab == null)
        {
            Debug.LogWarning("ResourceSpawner: Tree prefab is not assigned!");
            return;
        }

        int treesPerCluster = Mathf.CeilToInt((float)treeCount / treeClusters);
        int totalTreesSpawned = 0;

        for (int cluster = 0; cluster < treeClusters; cluster++)
        {
            // Find a valid cluster center
            Vector3 clusterCenter = FindClusterCenter();
            if (clusterCenter == Vector3.zero && cluster > 0)
            {
                Debug.LogWarning($"ResourceSpawner: Could not find valid center for tree cluster {cluster + 1}");
                continue;
            }

            clusterCenters.Add(clusterCenter);

            // Determine how many trees this cluster gets
            int treesForThisCluster = Mathf.Min(treesPerCluster, treeCount - totalTreesSpawned);

            int spawned = 0;
            int attempts = 0;
            int maxAttempts = treesForThisCluster * 20;

            while (spawned < treesForThisCluster && attempts < maxAttempts)
            {
                attempts++;

                // Generate position within cluster radius using gaussian-like distribution
                // Trees closer to center are more likely (tighter clusters)
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = Random.Range(0f, 1f) * Random.Range(0f, 1f) * clusterRadius;  // Bias toward center
                Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                Vector3 treePos = clusterCenter + offset;
                treePos.y = spawnHeight;

                // Clamp within spawn area
                treePos.x = Mathf.Clamp(treePos.x, spawnAreaMin.x, spawnAreaMax.x);
                treePos.z = Mathf.Clamp(treePos.z, spawnAreaMin.y, spawnAreaMax.y);

                // Validate: not too close to other trees, not on buildings
                if (IsPositionValidForTree(treePos))
                {
                    GameObject spawnedNode = Instantiate(treePrefab, treePos, Quaternion.identity);
                    // Add slight random rotation for visual variety
                    spawnedNode.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    spawnedNode.name = $"Tree_{totalTreesSpawned + 1}";
                    spawnedNode.transform.parent = transform;

                    spawnedPositions.Add(treePos);
                    spawned++;
                    totalTreesSpawned++;
                }
            }

        }
    }

    void SpawnScatteredTrees()
    {
        if (treePrefab == null || scatteredTreeCount <= 0) return;

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = scatteredTreeCount * 15;

        while (spawned < scatteredTreeCount && attempts < maxAttempts)
        {
            attempts++;

            Vector3 randomPos = new Vector3(
                Random.Range(spawnAreaMin.x, spawnAreaMax.x),
                spawnHeight,
                Random.Range(spawnAreaMin.y, spawnAreaMax.y)
            );

            // Must be away from campfire
            if (Vector3.Distance(randomPos, campfirePosition) < minDistanceFromCampfire)
                continue;

            // Must be away from cluster centers (scattered, not in forests)
            bool tooCloseToCluster = false;
            foreach (Vector3 center in clusterCenters)
            {
                if (Vector3.Distance(randomPos, center) < clusterRadius + 2f)
                {
                    tooCloseToCluster = true;
                    break;
                }
            }
            if (tooCloseToCluster) continue;

            // Must be away from other resources
            bool tooCloseToResource = false;
            foreach (Vector3 pos in spawnedPositions)
            {
                if (Vector3.Distance(randomPos, pos) < minScatteredTreeSpacing)
                {
                    tooCloseToResource = true;
                    break;
                }
            }
            if (tooCloseToResource) continue;

            // Must be away from buildings
            if (!IsPositionClearOfBuildings(randomPos))
                continue;

            GameObject tree = Instantiate(treePrefab, randomPos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            tree.name = $"Tree_Scattered_{spawned + 1}";
            tree.transform.parent = transform;

            spawnedPositions.Add(randomPos);
            spawned++;
        }
    }

    Vector3 FindClusterCenter()
    {
        int attempts = 0;
        int maxAttempts = 100;

        while (attempts < maxAttempts)
        {
            attempts++;

            Vector3 candidate = new Vector3(
                Random.Range(spawnAreaMin.x + clusterRadius, spawnAreaMax.x - clusterRadius),
                spawnHeight,
                Random.Range(spawnAreaMin.y + clusterRadius, spawnAreaMax.y - clusterRadius)
            );

            // Must be far from campfire
            if (Vector3.Distance(candidate, campfirePosition) < minClusterDistFromCampfire)
                continue;

            // Must be far from other cluster centers (spread forests out)
            bool tooCloseToOtherCluster = false;
            foreach (Vector3 existingCenter in clusterCenters)
            {
                if (Vector3.Distance(candidate, existingCenter) < clusterRadius * 2.5f)
                {
                    tooCloseToOtherCluster = true;
                    break;
                }
            }
            if (tooCloseToOtherCluster) continue;

            // Must not overlap with buildings
            if (!IsPositionClearOfBuildings(candidate))
                continue;

            return candidate;
        }

        // Fallback: just pick a random position far from campfire
        return new Vector3(
            Random.Range(spawnAreaMin.x + clusterRadius, spawnAreaMax.x - clusterRadius),
            spawnHeight,
            Random.Range(spawnAreaMin.y + clusterRadius, spawnAreaMax.y - clusterRadius)
        );
    }

    bool IsPositionValidForTree(Vector3 position)
    {
        // Check distance from campfire
        if (Vector3.Distance(position, campfirePosition) < minDistanceFromCampfire)
            return false;

        // Check distance from other spawned resources (use tighter tree spacing)
        foreach (Vector3 spawnedPos in spawnedPositions)
        {
            if (Vector3.Distance(position, spawnedPos) < minTreeSpacing)
                return false;
        }

        // Check distance from buildings
        if (!IsPositionClearOfBuildings(position))
            return false;

        return true;
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

        // Check distance from all buildings
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
            float clearance = Mathf.Max(minDistanceFromBuildings, building.noBuildRadius);
            if (Vector3.Distance(position, building.transform.position) < clearance)
                return false;
        }

        // Check Huts
        Hut[] huts = FindObjectsByType<Hut>(FindObjectsSortMode.None);
        foreach (Hut hut in huts)
        {
            if (hut == null) continue;
            float clearance = Mathf.Max(minDistanceFromBuildings, hut.noBuildRadius);
            if (Vector3.Distance(position, hut.transform.position) < clearance)
                return false;
        }

        // Check ConstructionSites
        ConstructionSite[] constructionSites = FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None);
        foreach (ConstructionSite site in constructionSites)
        {
            if (site == null) continue;
            float clearance = Mathf.Max(minDistanceFromBuildings, site.noBuildRadius);
            if (Vector3.Distance(position, site.transform.position) < clearance)
                return false;
        }

        // Check Walls
        Wall[] walls = FindObjectsByType<Wall>(FindObjectsSortMode.None);
        foreach (Wall wall in walls)
        {
            if (wall == null) continue;
            if (Vector3.Distance(position, wall.transform.position) < minDistanceFromBuildings)
                return false;
        }

        // Check Watchtowers
        Watchtower[] watchtowers = FindObjectsByType<Watchtower>(FindObjectsSortMode.None);
        foreach (Watchtower tower in watchtowers)
        {
            if (tower == null) continue;
            if (Vector3.Distance(position, tower.transform.position) < minDistanceFromBuildings)
                return false;
        }

        return true;  // Clear of all buildings
    }

    // Called by ResourceNode when it's about to be destroyed
    public void NotifyResourceDepleted(ResourceNode.ResourceType resourceType, Vector3 depletedPosition)
    {
        if (!enableRespawning)
        {
            return;
        }

        // Remove the depleted position from tracking
        spawnedPositions.Remove(depletedPosition);

        // Store the resource type for respawning the same type
        pendingRespawns.Enqueue(resourceType);
        Invoke(nameof(RespawnResource), respawnDelay);
    }

    private Queue<ResourceNode.ResourceType> pendingRespawns = new Queue<ResourceNode.ResourceType>();

    // Respawn a resource of the same type that was depleted
    void RespawnResource()
    {
        if (pendingRespawns.Count == 0) return;

        ResourceNode.ResourceType typeToSpawn = pendingRespawns.Dequeue();
        GameObject prefabToSpawn = null;
        string resourceName = "";

        switch (typeToSpawn)
        {
            case ResourceNode.ResourceType.Wood:
                prefabToSpawn = treePrefab;
                resourceName = "Tree";
                break;
            case ResourceNode.ResourceType.Food:
                prefabToSpawn = berryBushPrefab;
                resourceName = "BerryBush";
                break;
            case ResourceNode.ResourceType.Stone:
                prefabToSpawn = rockNodePrefab;
                resourceName = "RockNode";
                break;
        }

        if (prefabToSpawn == null)
        {
            Debug.LogWarning($"ResourceSpawner: Cannot respawn - {resourceName} prefab not assigned!");
            return;
        }

        // For trees, try to respawn near an existing cluster center
        if (typeToSpawn == ResourceNode.ResourceType.Wood && clusterCenters.Count > 0)
        {
            if (TryRespawnNearCluster(prefabToSpawn, resourceName))
                return;
        }

        // Fallback: spawn at random valid position
        int attempts = 0;
        int maxAttempts = 50;

        while (attempts < maxAttempts)
        {
            attempts++;

            Vector3 randomPos = new Vector3(
                Random.Range(spawnAreaMin.x, spawnAreaMax.x),
                spawnHeight,
                Random.Range(spawnAreaMin.y, spawnAreaMax.y)
            );

            if (IsPositionValid(randomPos))
            {
                GameObject spawnedNode = Instantiate(prefabToSpawn, randomPos, Quaternion.identity);
                spawnedNode.name = $"{resourceName}_Respawned";
                spawnedNode.transform.parent = transform;

                spawnedPositions.Add(randomPos);
                return;
            }
        }

        Debug.LogWarning($"ResourceSpawner: Failed to respawn {resourceName} - no valid positions found after {maxAttempts} attempts");
    }

    bool TryRespawnNearCluster(GameObject prefab, string resourceName)
    {
        // Pick a random cluster center to respawn near
        Vector3 cluster = clusterCenters[Random.Range(0, clusterCenters.Count)];

        int attempts = 0;
        int maxAttempts = 30;

        while (attempts < maxAttempts)
        {
            attempts++;

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(0f, 1f) * Random.Range(0f, 1f) * clusterRadius;
            Vector3 treePos = cluster + new Vector3(Mathf.Cos(angle) * dist, spawnHeight, Mathf.Sin(angle) * dist);

            treePos.x = Mathf.Clamp(treePos.x, spawnAreaMin.x, spawnAreaMax.x);
            treePos.z = Mathf.Clamp(treePos.z, spawnAreaMin.y, spawnAreaMax.y);

            if (IsPositionValidForTree(treePos))
            {
                GameObject spawnedNode = Instantiate(prefab, treePos, Quaternion.identity);
                spawnedNode.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                spawnedNode.name = $"{resourceName}_Respawned";
                spawnedNode.transform.parent = transform;

                spawnedPositions.Add(treePos);
                return true;
            }
        }

        return false;  // Couldn't find a spot near any cluster
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

        // Draw cluster centers
        Gizmos.color = new Color(0f, 0.8f, 0f, 0.5f);
        foreach (Vector3 clusterCenter in clusterCenters)
        {
            Gizmos.DrawWireSphere(clusterCenter, clusterRadius);
        }
    }
}

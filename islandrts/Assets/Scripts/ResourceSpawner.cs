using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Populates the island with resource nodes at startup and respawns them after they are
/// depleted, so a colony can never permanently exhaust the map.
/// </summary>
/// <remarks>
/// Placement is terrain-purposed (2026-09-01): each node type has a <see cref="Habitat"/>
/// — a height band, a slope band and a grass-tone band — so forests fill the low, dark
/// valleys, berry bushes sit on the open meadows, stone shows on high or broken ground,
/// and ore veins are found up on the plateaus and at cliff feet. Trees still cluster into
/// a few large forests; everything else is spread with generous spacing so the island
/// reads as places rather than a sprinkle. If a habitat cannot be satisfied on a given
/// island (a Rolling island has little high ground), the rule relaxes to "any dry,
/// gentle, reachable ground" rather than leaving the colony without that resource.
/// Every candidate also clears the campfire and existing buildings.
///
/// Counts and distances are authored for the standard 150 m map and scale with
/// <see cref="TerrainGrid.SizeScale"/> (counts by area, distances linearly).
/// </remarks>
public class ResourceSpawner : MonoBehaviour
{
    [Header("Resource Prefabs")]
    public GameObject treePrefab;
    public GameObject berryBushPrefab;
    public GameObject rockNodePrefab;
    public GameObject oreNodePrefab;

    [Header("Spawn Counts (150 m map; scaled by area)")]
    public int treeCount = 150;
    public int berryBushCount = 60;
    public int rockNodeCount = 55;
    public int oreNodeCount = 24;

    [Header("Spawn Area (150 m map; scaled)")]
    public Vector2 spawnAreaMin = new Vector2(-70f, -70f);  // Min X/Z
    public Vector2 spawnAreaMax = new Vector2(70f, 70f);    // Max X/Z
    public float spawnHeight = 0f;  // Y position on the legacy flat world

    [Header("Spacing")]
    public float minDistanceBetweenNodes = 3.5f;  // Minimum space between resources
    public float minDistanceFromCampfire = 6f;    // Keep resources away from campfire
    public float minDistanceFromBuildings = 5f;   // Keep resources away from buildings

    [Header("Tree Clustering")]
    public int treeClusters = 5;              // Number of forest clusters
    public float clusterRadius = 12f;         // How spread out trees are within a cluster
    public float minTreeSpacing = 2.2f;       // Minimum distance between trees in a cluster
    public float minClusterDistFromCampfire = 14f;  // Keep forests away from campfire

    [Header("Scattered Trees")]
    public int scatteredTreeCount = 20;       // Extra trees randomly placed between clusters
    public float minScatteredTreeSpacing = 6f;  // Minimum distance between scattered trees

    [Header("Habitats (terrain rules per node type)")]
    public Habitat treeHabitat = new Habitat { minHeight = 0.5f, maxHeight = 4.2f, minSlope = 0f, maxSlope = 0.5f, minTone = 0f, maxTone = 0.62f };
    public Habitat bushHabitat = new Habitat { minHeight = 0.6f, maxHeight = 4.5f, minSlope = 0f, maxSlope = 0.35f, minTone = 0.42f, maxTone = 1f };
    public Habitat rockHabitat = new Habitat { minHeight = 1.8f, maxHeight = 8f, minSlope = 0f, maxSlope = 0.55f, minTone = 0f, maxTone = 1f, orSlopeAbove = 0.28f };
    public Habitat oreHabitat = new Habitat { minHeight = 3.4f, maxHeight = 8f, minSlope = 0f, maxSlope = 0.55f, minTone = 0f, maxTone = 1f, orSlopeAbove = 0.32f, orSlopeMinHeight = 1.5f };

    [Header("Respawning")]
    public bool enableRespawning = true;  // Toggle resource respawning
    public float respawnDelay = 10f;  // Seconds before respawning a depleted resource

    /// <summary>Where a node type likes to live. Any band can be left wide open.</summary>
    [System.Serializable]
    public class Habitat
    {
        public float minHeight, maxHeight;
        public float minSlope, maxSlope;
        [Tooltip("Grass tone band 0..1 (0 = dark valley grass, 1 = dry plateau meadow).")]
        public float minTone, maxTone;
        [Tooltip("Alternatively accept any ground steeper than this (cliff feet / broken ground), 0 = off.")]
        public float orSlopeAbove;
        [Tooltip("...as long as it is at least this high.")]
        public float orSlopeMinHeight;

        public bool Accepts(TerrainGrid t, Vector3 pos)
        {
            float h = t.SampleHeight(pos);
            float slope = t.SlopeAt(pos);
            if (orSlopeAbove > 0f && slope >= orSlopeAbove && h >= orSlopeMinHeight) return true;
            if (h < minHeight || h > maxHeight) return false;
            if (slope < minSlope || slope > maxSlope) return false;
            if (minTone > 0f || maxTone < 1f)
            {
                float tone = t.ToneAt(pos);
                if (tone < minTone || tone > maxTone) return false;
            }
            return true;
        }
    }

    private List<Vector3> spawnedPositions = new List<Vector3>();
    private List<Vector3> clusterCenters = new List<Vector3>();  // Track forest centers for respawning
    private Vector3 campfirePosition = Vector3.zero;

    // Effective (scaled) values for this island
    private float scale = 1f;
    private Vector2 areaMin, areaMax;
    private float clusterR;

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
        scale = TerrainGrid.Instance != null ? TerrainGrid.SizeScale : 1f;
        areaMin = spawnAreaMin * scale;
        areaMax = spawnAreaMax * scale;
        clusterR = clusterRadius * scale;

        // Find the campfire position using BaseBuilding component
        BaseBuilding campfire = FindAnyObjectByType<BaseBuilding>();
        if (campfire != null)
        {
            campfirePosition = campfire.transform.position;
        }
        else if (GameStartController.IntroInProgress)
        {
            // Opening sequence: the campfire is placed by the player later.
            // Spawn around the campfire site; the placer keeps its own
            // clearance from resource nodes instead of the reverse.
            campfirePosition = TerrainGrid.Instance != null ? TerrainGrid.Instance.CampfireSite : Vector3.zero;
        }
        else
        {
            Debug.LogWarning("ResourceSpawner: Campfire not found! Using world origin (0,0,0) as default.");
            campfirePosition = Vector3.zero;
        }

        SpawnAllResources();
    }

    int Scaled(int count) => Mathf.RoundToInt(count * scale * scale);

    void SpawnAllResources()
    {
        // Spawn trees in clusters (forests)
        SpawnTreeClusters();

        // Spawn scattered individual trees between clusters
        SpawnScatteredTrees();

        // Bushes on the meadows, stone on the high ground, ore up on the plateaus
        SpawnResourceType(berryBushPrefab, Scaled(berryBushCount), "BerryBush", bushHabitat);
        SpawnResourceType(rockNodePrefab, Scaled(rockNodeCount), "RockNode", rockHabitat);
        SpawnResourceType(oreNodePrefab, Scaled(oreNodeCount), "OreNode", oreHabitat);
    }

    void SpawnTreeClusters()
    {
        if (treePrefab == null)
        {
            Debug.LogWarning("ResourceSpawner: Tree prefab is not assigned!");
            return;
        }

        int wantedTrees = Scaled(treeCount);
        int clusters = Mathf.Max(1, Mathf.RoundToInt(treeClusters * scale));
        int treesPerCluster = Mathf.CeilToInt((float)wantedTrees / clusters);
        int totalTreesSpawned = 0;

        for (int cluster = 0; cluster < clusters; cluster++)
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
            int treesForThisCluster = Mathf.Min(treesPerCluster, wantedTrees - totalTreesSpawned);

            int spawned = 0;
            int attempts = 0;
            int maxAttempts = treesForThisCluster * 20;

            while (spawned < treesForThisCluster && attempts < maxAttempts)
            {
                attempts++;

                // Generate position within cluster radius using gaussian-like distribution
                // Trees closer to center are more likely (tighter clusters)
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = Random.Range(0f, 1f) * Random.Range(0f, 1f) * clusterR;  // Bias toward center
                Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                Vector3 treePos = clusterCenter + offset;
                treePos.y = spawnHeight;

                // Clamp within spawn area, sit on the ground
                treePos.x = Mathf.Clamp(treePos.x, areaMin.x, areaMax.x);
                treePos.z = Mathf.Clamp(treePos.z, areaMin.y, areaMax.y);
                treePos.y = GroundY(treePos);

                // Validate: on good ground, not too close to other trees, not on buildings.
                // Inside a forest the habitat is relaxed after the first half of the
                // attempts so a cluster on a hillside still fills in.
                bool relaxed = attempts > maxAttempts / 2;
                if (IsPositionValidForTree(treePos, relaxed))
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

        int wanted = Scaled(scatteredTreeCount);
        int spawned = 0;
        int attempts = 0;
        int maxAttempts = wanted * 20;

        while (spawned < wanted && attempts < maxAttempts)
        {
            attempts++;

            Vector3 randomPos = RandomInArea();
            if (!IsTerrainOk(randomPos)) continue;
            if (attempts <= maxAttempts / 2 && !HabitatOk(treeHabitat, randomPos)) continue;

            // Must be away from campfire
            if (Vector3.Distance(randomPos, campfirePosition) < minDistanceFromCampfire)
                continue;

            // Must be away from cluster centers (scattered, not in forests)
            bool tooCloseToCluster = false;
            foreach (Vector3 center in clusterCenters)
            {
                if (Vector3.Distance(randomPos, center) < clusterR + 2f)
                {
                    tooCloseToCluster = true;
                    break;
                }
            }
            if (tooCloseToCluster) continue;

            // Must be away from other resources
            if (TooClose(randomPos, minScatteredTreeSpacing)) continue;

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
        int maxAttempts = 160;

        while (attempts < maxAttempts)
        {
            attempts++;

            Vector3 candidate = new Vector3(
                Random.Range(areaMin.x + clusterR, areaMax.x - clusterR),
                spawnHeight,
                Random.Range(areaMin.y + clusterR, areaMax.y - clusterR)
            );
            candidate.y = GroundY(candidate);
            if (!IsTerrainOk(candidate)) continue;
            // Forests grow in the low, dark ground; give up on that only late
            if (attempts <= maxAttempts * 2 / 3 && !HabitatOk(treeHabitat, candidate)) continue;

            // Must be far from campfire
            if (Vector3.Distance(candidate, campfirePosition) < minClusterDistFromCampfire * scale)
                continue;

            // Must be far from other cluster centers (spread forests out)
            bool tooCloseToOtherCluster = false;
            foreach (Vector3 existingCenter in clusterCenters)
            {
                if (Vector3.Distance(candidate, existingCenter) < clusterR * 2.5f)
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
            Random.Range(areaMin.x + clusterR, areaMax.x - clusterR),
            spawnHeight,
            Random.Range(areaMin.y + clusterR, areaMax.y - clusterR)
        );
    }

    bool IsPositionValidForTree(Vector3 position, bool relaxedHabitat)
    {
        // Terrain first — no trees in the water or on cliff faces
        if (!IsTerrainOk(position))
            return false;
        if (!relaxedHabitat && !HabitatOk(treeHabitat, position))
            return false;

        // Check distance from campfire
        if (Vector3.Distance(position, campfirePosition) < minDistanceFromCampfire)
            return false;

        // Check distance from other spawned resources (use tighter tree spacing)
        if (TooClose(position, minTreeSpacing))
            return false;

        // Check distance from buildings
        if (!IsPositionClearOfBuildings(position))
            return false;

        return true;
    }

    void SpawnResourceType(GameObject prefab, int count, string resourceName, Habitat habitat)
    {
        if (prefab == null)
        {
            if (count > 0) Debug.LogWarning($"ResourceSpawner: {resourceName} prefab is not assigned!");
            return;
        }

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = count * 24;

        while (spawned < count && attempts < maxAttempts)
        {
            attempts++;

            Vector3 randomPos = RandomInArea();
            if (!IsTerrainOk(randomPos)) continue;

            // Habitat first; relax it for the last third of the attempts so a
            // resource never goes missing on an island that lacks its ground
            bool relaxed = attempts > maxAttempts * 2 / 3;
            if (!relaxed && !HabitatOk(habitat, randomPos)) continue;

            // Check if position is far enough from other resources
            if (IsPositionValid(randomPos))
            {
                // Spawn the resource
                GameObject spawnedNode = Instantiate(prefab, randomPos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
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

    // --- Terrain integration: resources sit on and validate against the island surface ---

    Vector3 RandomInArea()
    {
        Vector3 p = new Vector3(Random.Range(areaMin.x, areaMax.x), spawnHeight, Random.Range(areaMin.y, areaMax.y));
        p.y = GroundY(p);
        return p;
    }

    float GroundY(Vector3 pos)
    {
        return TerrainGrid.Instance != null ? TerrainGrid.Instance.SampleHeight(pos) : spawnHeight;
    }

    /// <summary>Always true on the legacy flat world. On terrain: dry land, gentle slope, reachable.</summary>
    bool IsTerrainOk(Vector3 pos)
    {
        if (TerrainGrid.Instance == null) return true;
        // Dry, gentle, and reachable from the campfire — a node on a cut-off
        // outcrop would only ever feed the unreachable-node fallback
        return TerrainGrid.Instance.SampleHeight(pos) > 0.15f
            && TerrainGrid.Instance.SlopeAt(pos) < 0.55f
            && TerrainGrid.Instance.IsReachable(pos);
    }

    bool HabitatOk(Habitat habitat, Vector3 pos)
    {
        if (TerrainGrid.Instance == null || habitat == null) return true;
        return habitat.Accepts(TerrainGrid.Instance, pos);
    }

    bool TooClose(Vector3 position, float spacing)
    {
        float sq = spacing * spacing;
        for (int i = 0; i < spawnedPositions.Count; i++)
        {
            Vector3 d = spawnedPositions[i] - position;
            d.y = 0f;
            if (d.sqrMagnitude < sq) return true;
        }
        return false;
    }

    bool IsPositionValid(Vector3 position)
    {
        // Terrain first — no resources in the water or on cliff faces
        if (!IsTerrainOk(position))
            return false;

        // Check distance from campfire first
        if (Vector3.Distance(position, campfirePosition) < minDistanceFromCampfire)
            return false;  // Too close to campfire

        // Check distance from all previously spawned resources
        if (TooClose(position, minDistanceBetweenNodes))
            return false;

        // Check distance from all buildings
        if (!IsPositionClearOfBuildings(position))
            return false;  // Too close to a building

        return true;  // Position is valid
    }

    bool IsPositionClearOfBuildings(Vector3 position)
    {
        // Check BaseBuilding (Campfire)
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            BaseBuilding building = BaseBuilding.ActiveList[i];
            if (building == null) continue;
            float clearance = Mathf.Max(minDistanceFromBuildings, building.noBuildRadius);
            if (Vector3.Distance(position, building.transform.position) < clearance)
                return false;
        }

        // Check Huts
        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;
            float clearance = Mathf.Max(minDistanceFromBuildings, hut.noBuildRadius);
            if (Vector3.Distance(position, hut.transform.position) < clearance)
                return false;
        }

        // Check ConstructionSites
        for (int i = 0; i < ConstructionSite.ActiveList.Count; i++)
        {
            ConstructionSite site = ConstructionSite.ActiveList[i];
            if (site == null) continue;
            float clearance = Mathf.Max(minDistanceFromBuildings, site.noBuildRadius);
            if (Vector3.Distance(position, site.transform.position) < clearance)
                return false;
        }

        // Check Walls
        for (int i = 0; i < Wall.ActiveList.Count; i++)
        {
            Wall wall = Wall.ActiveList[i];
            if (wall == null) continue;
            if (Vector3.Distance(position, wall.transform.position) < minDistanceFromBuildings)
                return false;
        }

        // Check Watchtowers
        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            Watchtower tower = Watchtower.ActiveList[i];
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

    GameObject PrefabFor(ResourceNode.ResourceType type, out string name, out Habitat habitat)
    {
        switch (type)
        {
            case ResourceNode.ResourceType.Food: name = "BerryBush"; habitat = bushHabitat; return berryBushPrefab;
            case ResourceNode.ResourceType.Stone: name = "RockNode"; habitat = rockHabitat; return rockNodePrefab;
            case ResourceNode.ResourceType.Metal: name = "OreNode"; habitat = oreHabitat; return oreNodePrefab;
            default: name = "Tree"; habitat = treeHabitat; return treePrefab;
        }
    }

    // Respawn a resource of the same type that was depleted
    void RespawnResource()
    {
        if (pendingRespawns.Count == 0) return;

        ResourceNode.ResourceType typeToSpawn = pendingRespawns.Dequeue();
        string resourceName;
        Habitat habitat;
        GameObject prefabToSpawn = PrefabFor(typeToSpawn, out resourceName, out habitat);

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

        // Fallback: spawn at random valid position (habitat first, then anywhere)
        int attempts = 0;
        int maxAttempts = 80;

        while (attempts < maxAttempts)
        {
            attempts++;

            Vector3 randomPos = RandomInArea();
            if (!IsTerrainOk(randomPos)) continue;
            if (attempts <= maxAttempts / 2 && !HabitatOk(habitat, randomPos)) continue;

            if (IsPositionValid(randomPos))
            {
                GameObject spawnedNode = Instantiate(prefabToSpawn, randomPos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
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
            float dist = Random.Range(0f, 1f) * Random.Range(0f, 1f) * clusterR;
            Vector3 treePos = cluster + new Vector3(Mathf.Cos(angle) * dist, spawnHeight, Mathf.Sin(angle) * dist);

            treePos.x = Mathf.Clamp(treePos.x, areaMin.x, areaMax.x);
            treePos.z = Mathf.Clamp(treePos.z, areaMin.y, areaMax.y);
            treePos.y = GroundY(treePos);

            if (IsPositionValidForTree(treePos, attempts > maxAttempts / 2))
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
            Gizmos.DrawWireSphere(clusterCenter, clusterR);
        }
    }
}

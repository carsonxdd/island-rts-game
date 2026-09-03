using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Scatters stick/stone ground pickups across the island at start and trickles
/// replacements in over time. Prefab references are wired by
/// Tools &gt; Island RTS &gt; Session Content &gt; Setup Pickups + Workshop.
///
/// Placement rules: on land (terrain-aware), gentle slope, on the NavMesh so a
/// worker can actually reach it, spaced apart from other pickups, outside the
/// colony heart.
///
/// A second, denser cluster sits on the landing beach around the cove
/// (2026-09-02) so the player's character has sticks and stones in reach
/// from the first minute — the first tools are crafted from these. Cove
/// pickups count against the same budgets; when taken, replacements trickle
/// in anywhere on the island like the rest.
/// </summary>
public class PickupSpawner : MonoBehaviour
{
    public static PickupSpawner Instance { get; private set; }

    [Header("Prefabs (wired by the setup tool)")]
    public GameObject stickPrefab;
    public GameObject stonePrefab;

    [Header("Counts")]
    public int stickCount = 26;
    public int stoneCount = 16;

    [Header("Landing beach cluster")]
    [Tooltip("Extra sticks placed within coveRadius of the cove at start (on top of stickCount).")]
    public int coveSticks = 8;
    [Tooltip("Extra stone chunks placed within coveRadius of the cove at start (on top of stoneCount).")]
    public int coveStones = 5;
    [Tooltip("Radius of the landing cluster around TerrainGrid.CoveCenter, on the 150 m map.")]
    public float coveRadius = 12f;

    [Header("Respawn")]
    [Tooltip("One missing pickup respawns per interval (sticks first).")]
    public float respawnInterval = 18f;

    [Header("Placement")]
    public float minRadius = 8f;
    public float maxRadius = 66f;
    public float minSpacing = 3f;
    public float maxSlope = 0.6f;

    private int aliveSticks;
    private int aliveStones;
    private float respawnTimer;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        // TerrainGrid (execution order -100) has already built the NavMesh by now.
        // The beach cluster goes first so it is never squeezed out by the spacing rule.
        Vector3 cove = TerrainGrid.Instance != null
            ? TerrainGrid.Instance.CoveCenter
            : new Vector3(-69f, 0f, 3f);
        float coveR = coveRadius * TerrainGrid.SizeScale;
        int coveStickCap = Mathf.Max(0, coveSticks);
        int coveStoneCap = Mathf.Max(0, coveStones);
        for (int i = 0; i < coveStickCap; i++) if (TrySpawnNear(stickPrefab, cove, coveR)) aliveSticks++;
        for (int i = 0; i < coveStoneCap; i++) if (TrySpawnNear(stonePrefab, cove, coveR)) aliveStones++;

        for (int i = 0; i < stickCount; i++) if (TrySpawn(stickPrefab)) aliveSticks++;
        for (int i = 0; i < stoneCount; i++) if (TrySpawn(stonePrefab)) aliveStones++;
    }

    void Update()
    {
        respawnTimer += Time.deltaTime;
        if (respawnTimer < respawnInterval) return;
        respawnTimer = 0f;

        // The cove extras are part of the budget: the island settles back to the
        // authored total, wherever the replacements land
        if (aliveSticks < stickCount + coveSticks)
        {
            if (TrySpawn(stickPrefab)) aliveSticks++;
        }
        else if (aliveStones < stoneCount + coveStones)
        {
            if (TrySpawn(stonePrefab)) aliveStones++;
        }
    }

    public void NotifyCollected(GroundPickup pickup)
    {
        if (pickup.resourceType == ResourceNode.ResourceType.Wood)
            aliveSticks = Mathf.Max(0, aliveSticks - 1);
        else
            aliveStones = Mathf.Max(0, aliveStones - 1);
    }

    /// <summary>Island-wide band around the origin (area-uniform).</summary>
    bool TrySpawn(GameObject prefab)
    {
        if (prefab == null) return false;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            // sqrt keeps the distribution area-uniform across the band
            float t = Mathf.Sqrt(Random.value);
            float radius = Mathf.Lerp(minRadius, maxRadius, t) * TerrainGrid.SizeScale;   // band authored for the 150 m map
            float angle = Random.value * Mathf.PI * 2f;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (TryPlace(prefab, pos)) return true;
        }
        return false;
    }

    /// <summary>A disc around <paramref name="center"/> (the landing beach).</summary>
    bool TrySpawnNear(GameObject prefab, Vector3 center, float radius)
    {
        if (prefab == null) return false;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            float r = Mathf.Sqrt(Random.value) * radius;
            float angle = Random.value * Mathf.PI * 2f;
            Vector3 pos = center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            if (TryPlace(prefab, pos)) return true;
        }
        return false;
    }

    /// <summary>The shared validity gauntlet: land, gentle, reachable, on the NavMesh, spaced.</summary>
    bool TryPlace(GameObject prefab, Vector3 pos)
    {
        if (TerrainGrid.Instance != null)
        {
            if (!TerrainGrid.Instance.IsLand(pos)) return false;
            if (TerrainGrid.Instance.SlopeAt(pos) > maxSlope) return false;
            if (!TerrainGrid.Instance.IsReachable(pos)) return false;
            pos.y = TerrainGrid.Instance.SampleHeight(pos);
        }

        // Must be reachable
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(pos, out hit, 1.5f, NavMesh.AllAreas)) return false;
        pos = hit.position;

        // Spacing vs other pickups
        var list = GroundPickup.ActiveList;
        float sqrSpacing = minSpacing * minSpacing;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null) continue;
            Vector3 d = list[i].transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude < sqrSpacing) return false;
        }

        GameObject spawned = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        // Only what this spawner placed counts against its respawn budget —
        // salvage crates are finite and must not feed the trickle.
        GroundPickup pickup = spawned.GetComponent<GroundPickup>();
        if (pickup != null) pickup.spawnerOwned = true;
        return true;
    }
}

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
    public int stickCount = 44;
    public int stoneCount = 30;

    [Header("Sizes")]
    [Tooltip("Chance a placed pickup is a large one - visibly bigger and worth largeMultiplier times as much.")]
    [Range(0f, 1f)] public float largeChance = 0.28f;
    [Tooltip("Visual scale of a large pickup. The click collider is on the root, so it grows with it.")]
    public float largeScale = 1.6f;
    [Tooltip("Yield multiplier of a large pickup, for both the worker carry and the item in hand.")]
    public int largeMultiplier = 3;

    [Header("Landing beach cluster")]
    [Tooltip("Extra sticks placed within coveRadius of the cove at start (on top of stickCount).")]
    public int coveSticks = 8;
    [Tooltip("Extra stone chunks placed within coveRadius of the cove at start (on top of stoneCount).")]
    public int coveStones = 5;
    [Tooltip("Radius of the landing cluster around TerrainGrid.CoveCenter, on the 150 m map.")]
    public float coveRadius = 12f;

    [Header("Respawn")]
    [Tooltip("One missing pickup respawns per interval (sticks first).")]
    public float respawnInterval = 12f;

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

    /// <summary>
    /// Drop one shed pickup beside a worked resource node (<see cref="ResourceNode"/>
    /// sheds sticks off trees and bushes and chunks off rocks). Never counted against
    /// the respawn budget: these are made by work, not placed by this spawner, so the
    /// island's trickle must not treat one as a replacement it owes.
    /// </summary>
    public bool DropByproduct(ResourceNode.ResourceType type, Vector3 near, float radius)
    {
        GameObject prefab = (type == ResourceNode.ResourceType.Stone || type == ResourceNode.ResourceType.Metal)
            ? stonePrefab : stickPrefab;
        if (prefab == null) return false;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float r = Mathf.Lerp(radius * 0.6f, radius, Random.value);
            Vector3 pos = near + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            // Tight spacing: a stick right beside the tree it fell from is the point
            if (TryPlace(prefab, pos, owned: false, spacing: 1.1f)) return true;
        }
        return false;
    }

    /// <summary>The shared validity gauntlet: land, gentle, reachable, on the NavMesh, spaced.</summary>
    bool TryPlace(GameObject prefab, Vector3 pos, bool owned = true, float spacing = -1f)
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
        float useSpacing = spacing > 0f ? spacing : minSpacing;
        float sqrSpacing = useSpacing * useSpacing;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null) continue;
            Vector3 d = list[i].transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude < sqrSpacing) return false;
        }

        GameObject spawned = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        // Only what this spawner placed counts against its respawn budget —
        // salvage crates and shed byproducts are finite and must not feed the trickle.
        GroundPickup pickup = spawned.GetComponent<GroundPickup>();
        if (pickup != null)
        {
            pickup.spawnerOwned = owned;
            if (Random.value < largeChance) MakeLarge(pickup);
        }
        return true;
    }

    /// <summary>
    /// Turn a placed pickup into the large variant: bigger on the ground and worth
    /// several of the small one. Scaling the ROOT is deliberate - the click collider
    /// GroundPickup adds in Awake lives there, so a big stone is as easy to click as it
    /// looks.
    /// </summary>
    void MakeLarge(GroundPickup pickup)
    {
        pickup.transform.localScale *= Mathf.Max(1f, largeScale);
        int mult = Mathf.Max(1, largeMultiplier);
        pickup.amount *= mult;
        pickup.itemAmount *= mult;
    }
}

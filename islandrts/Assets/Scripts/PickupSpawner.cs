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
        // TerrainGrid (execution order -100) has already built the NavMesh by now
        for (int i = 0; i < stickCount; i++) if (TrySpawn(stickPrefab)) aliveSticks++;
        for (int i = 0; i < stoneCount; i++) if (TrySpawn(stonePrefab)) aliveStones++;
    }

    void Update()
    {
        respawnTimer += Time.deltaTime;
        if (respawnTimer < respawnInterval) return;
        respawnTimer = 0f;

        if (aliveSticks < stickCount)
        {
            if (TrySpawn(stickPrefab)) aliveSticks++;
        }
        else if (aliveStones < stoneCount)
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

            if (TerrainGrid.Instance != null)
            {
                if (!TerrainGrid.Instance.IsLand(pos)) continue;
                if (TerrainGrid.Instance.SlopeAt(pos) > maxSlope) continue;
                if (!TerrainGrid.Instance.IsReachable(pos)) continue;
                pos.y = TerrainGrid.Instance.SampleHeight(pos);
            }

            // Must be reachable
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(pos, out hit, 1.5f, NavMesh.AllAreas)) continue;
            pos = hit.position;

            // Spacing vs other pickups
            bool tooClose = false;
            var list = GroundPickup.ActiveList;
            float sqrSpacing = minSpacing * minSpacing;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                Vector3 d = list[i].transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude < sqrSpacing) { tooClose = true; break; }
            }
            if (tooClose) continue;

            Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
            return true;
        }
        return false;
    }
}

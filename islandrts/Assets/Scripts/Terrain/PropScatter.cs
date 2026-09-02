using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime environment set dressing: palms, rocks, bushes, ferns, grass
/// tufts and flotsam placed on the generated island at load, from
/// <see cref="ScatterSettings"/>' terrain rules. Replaces the editor-time
/// scatter that baked props into the scene — a random island per run means
/// the props have to be placed after the island exists.
///
/// Runs in Start (TerrainGrid builds the world in Awake, so the field and
/// its NavMesh are complete). Seeded from the island seed with its own
/// System.Random, so the layout is reproducible per island and never
/// touches UnityEngine.Random's global state. Props carry no colliders, so
/// they never block pathing, intercept clicks, or affect the NavMesh; they
/// are combined into static batches once placed.
///
/// Skipped under the balance sim (pure cosmetics).
/// </summary>
public class PropScatter : MonoBehaviour
{
    public ScatterSettings settings;

    private const string RootName = "_Scatter";

    void Start()
    {
        if (SimHooks.Simulating) return;
        if (settings == null || settings.rules == null)
        {
            Debug.LogError("PropScatter: no ScatterSettings assigned — run Tools > Island RTS > Terrain > Setup Terrain Scene.");
            return;
        }
        TerrainGrid terrain = TerrainGrid.Instance;
        if (terrain == null || terrain.Field == null)
        {
            Debug.LogError("PropScatter: no TerrainGrid in the scene — nothing to scatter onto.");
            return;
        }

        Scatter(terrain);
    }

    void Scatter(TerrainGrid terrain)
    {
        // Own root, never under the Terrain object (its NavMeshSurface collects children)
        GameObject root = new GameObject(RootName);
        System.Random rng = new System.Random(terrain.seed ^ 0x5CA77E2);

        // Anchors from the FIELD (already scaled to this map's size)
        Vector3 campfire = new Vector3(terrain.Field.campfireSite.x, 0f, terrain.Field.campfireSite.y);
        Vector3 cove = new Vector3(terrain.Field.coveCenter.x, 0f, terrain.Field.coveCenter.y);
        float campClear2 = settings.campfireClearing * settings.campfireClearing;
        float coveClear2 = settings.coveClearing * settings.coveClearing;

        float half = (TerrainGrid.VertsPerSide - 1) * TerrainGrid.Spacing * 0.5f - 2f;
        var placed = new SpatialHash(5f);
        int total = 0;

        for (int r = 0; r < settings.rules.Length; r++)
        {
            ScatterSettings.Rule rule = settings.rules[r];
            if (rule == null || rule.prefab == null) continue;

            Transform group = new GameObject(rule.prefab.name).transform;
            group.SetParent(root.transform, false);

            // Counts are authored for the 150 m map; keep the same DENSITY on other sizes
            int wanted = Mathf.RoundToInt(rule.count * TerrainGrid.SizeScale * TerrainGrid.SizeScale);
            for (int i = 0; i < wanted; i++)
            {
                for (int attempt = 0; attempt < settings.maxTriesPerProp; attempt++)
                {
                    Vector3 p = new Vector3(
                        (float)(rng.NextDouble() * 2.0 - 1.0) * half, 0f,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * half);

                    if ((p - campfire).sqrMagnitude < campClear2) continue;
                    if ((p - cove).sqrMagnitude < coveClear2) continue;

                    float h = terrain.SampleHeight(p);
                    if (h < settings.minGroundHeight || h < rule.minHeight || h > rule.maxHeight) continue;

                    float slope = terrain.SlopeAt(p);
                    if (slope < rule.minSlope || slope > rule.maxSlope) continue;

                    if (rule.minTone > 0f || rule.maxTone < 1f)
                    {
                        float tone = terrain.ToneAt(p);
                        if (tone < rule.minTone || tone > rule.maxTone) continue;
                    }

                    if (placed.TooClose(p, rule.spacing)) continue;

                    p.y = h;
                    GameObject inst = Instantiate(rule.prefab, p, Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f), group);
                    float scale = Mathf.Lerp(rule.minScale, rule.maxScale, (float)rng.NextDouble());
                    inst.transform.localScale = new Vector3(scale, scale, scale);

                    placed.Add(p);
                    total++;
                    break;
                }
            }
        }

        if (settings.staticBatch && total > 0)
        {
            StaticBatchingUtility.Combine(root);
        }
    }

    /// <summary>Grid-bucketed positions for the spacing check (O(1) per query instead of O(n)).</summary>
    class SpatialHash
    {
        readonly float cell;
        readonly Dictionary<long, List<Vector3>> buckets = new Dictionary<long, List<Vector3>>();

        public SpatialHash(float cellSize) { cell = cellSize; }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(Vector3 p)
        {
            long k = Key(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell));
            if (!buckets.TryGetValue(k, out List<Vector3> list))
            {
                list = new List<Vector3>();
                buckets[k] = list;
            }
            list.Add(p);
        }

        public bool TooClose(Vector3 p, float spacing)
        {
            float sq = spacing * spacing;
            int reach = Mathf.CeilToInt(spacing / cell);
            int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (!buckets.TryGetValue(Key(cx + dx, cz + dz), out List<Vector3> list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        float ddx = list[i].x - p.x, ddz = list[i].z - p.z;
                        if (ddx * ddx + ddz * ddz < sq) return true;
                    }
                }
            }
            return false;
        }
    }
}

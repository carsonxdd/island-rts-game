using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
/// touches UnityEngine.Random's global state. Decor props carry no colliders,
/// so they never block pathing, intercept clicks, or affect the NavMesh; they
/// are combined into static batches once placed.
///
/// A rule may be marked gatherable, which is how palms become choppable trees
/// rather than scenery - every tree on the island is a resource node. Those
/// props are built as real nodes (see SpawnNode), live under their own root,
/// and are never static-batched, because they shrink and are destroyed as
/// they deplete.
///
/// A rule may instead be marked salvage, which is how washed-up crates and
/// barrels become supplies: each is a one-shot GroundPickup a worker carries
/// home (see SpawnSalvage). Salvage is finite - it is placed once per island
/// and PickupSpawner never replaces it.
///
/// Under the balance sim only the gameplay rules run: decor is skipped, but
/// wood the simulated colony can chop and salvage it can carry are not.
/// </summary>
public class PropScatter : MonoBehaviour
{
    public ScatterSettings settings;

    private const string RootName = "_Scatter";

    void Start()
    {
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
        // Gameplay props are not decor: nodes shrink as they deplete and salvage is
        // destroyed when collected, so both stay out of the static batch.
        GameObject nodeRoot = null;
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
            bool gameplay = rule.gatherable || rule.salvage;
            // Under the balance sim only the gameplay rules matter - the rest is decor,
            // and the sim has no camera to see it with.
            if (SimHooks.Simulating && !gameplay) continue;

            if (gameplay && nodeRoot == null) nodeRoot = new GameObject(RootName + "_Nodes");

            Transform group = new GameObject(rule.prefab.name).transform;
            group.SetParent(gameplay ? nodeRoot.transform : root.transform, false);

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

                    // A node or a crate on a cut-off outcrop is one no worker can ever
                    // reach; decor is welcome there.
                    if (gameplay && !terrain.IsReachable(p)) continue;

                    if (placed.TooClose(p, rule.spacing)) continue;

                    p.y = h;
                    float yaw = (float)(rng.NextDouble() * 360.0);
                    float scale = Mathf.Lerp(rule.minScale, rule.maxScale, (float)rng.NextDouble());

                    if (rule.gatherable)
                        SpawnNode(rule, group, p, yaw, scale);
                    else if (rule.salvage)
                    {
                        if (!SpawnSalvage(rule, group, p, yaw, scale)) continue;
                    }
                    else
                        Instantiate(rule.prefab, p, Quaternion.Euler(0f, yaw, 0f), group)
                            .transform.localScale = new Vector3(scale, scale, scale);

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

    /// <summary>
    /// Build a harvestable node around an art prefab, matching the layout the plumber
    /// gives Tree.prefab: an empty root carrying the gameplay components, with the art
    /// mounted on a child called "Model".
    /// </summary>
    /// <remarks>
    /// The name matters. ResourceNode finds "Model" to shrink on depletion and to wobble
    /// on the gather beat, and both must happen on the child - the root drives the
    /// carving NavMeshObstacle, so scaling or rotating it would re-carve the NavMesh on
    /// every gather tick. Yaw and size jitter therefore go on the Model too, which is
    /// also where TreeVariance puts them.
    ///
    /// The click hitbox takes its HEIGHT from the art's renderer bounds (one rule covers
    /// palms from 2.2 m to 5 m tall) but its footprint is trunk-sized, not canopy-sized:
    /// a palm's fronds span 3-4 m, and a box that wide was a huge rectangle that caught
    /// every click and hover metres from the trunk (2026-09-03). Bounds are read in the
    /// model's LOCAL space too — the world AABB of a yawed model is inflated by the yaw.
    /// </remarks>
    void SpawnNode(ScatterSettings.Rule rule, Transform group, Vector3 pos, float yaw, float scale)
    {
        GameObject node = new GameObject(rule.prefab.name + "Node");
        node.transform.SetParent(group, false);
        node.transform.position = pos;

        GameObject model = Instantiate(rule.prefab, node.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        model.transform.localScale = new Vector3(scale, scale, scale);

        BoxCollider box = node.AddComponent<BoxCollider>();
        MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>();
        bool measured = false;
        if (filters.Length > 0)
        {
            // Mesh bounds in the model's own frame (unrotated), scaled by the jitter
            Bounds b = new Bounds();
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                Bounds mb = mesh.bounds;
                // Local-to-model transform of this piece (art prefabs are shallow; yaw-free)
                Vector3 c = model.transform.InverseTransformPoint(filters[i].transform.TransformPoint(mb.center));
                Vector3 s = Vector3.Scale(mb.size, filters[i].transform.lossyScale) / Mathf.Max(scale, 0.01f);
                if (!measured) { b = new Bounds(c, s); measured = true; }
                else b.Encapsulate(new Bounds(c, s));
            }
            if (measured)
            {
                float height = b.size.y * scale;
                float canopy = Mathf.Max(b.size.x, b.size.z) * scale;
                float footprint = Mathf.Clamp(canopy * 0.35f, 0.9f, 1.6f);   // trunk plus a hand's width of canopy centre
                box.center = new Vector3(0f, height * 0.5f, 0f);
                box.size = new Vector3(footprint, height, footprint);
            }
        }
        if (!measured)
        {
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(1.2f, 3f, 1.2f);
        }

        // Fields are assigned before Start runs, which is where ResourceNode sizes its
        // NavMeshObstacle from the resource type and fills the node.
        ResourceNode resource = node.AddComponent<ResourceNode>();
        resource.resourceType = rule.resourceType;
        resource.maxResourceAmount = rule.resourceAmount;
    }

    /// <summary>
    /// Build a one-shot salvage pickup around an art prefab: a crate of supplies or a
    /// barrel washed up on the shore, which a worker of the matching job carries home.
    /// Returns false when the spot has no NavMesh under it, so the caller can try
    /// elsewhere rather than leave an unreachable crate on the sand.
    /// </summary>
    /// <remarks>
    /// Layout mirrors the pickup prefabs the session-content tool builds (Stick,
    /// StonePickup): gameplay component on an empty root, art on a "Model" child, and
    /// NO collider — a pickup is an AI target, never a click target, and must not
    /// intercept building-placement raycasts or block pathing.
    ///
    /// The flotsam bands reach into the wading band, where the ground is walkable but
    /// the NavMesh edge is ragged, hence the sample-and-snap.
    /// </remarks>
    bool SpawnSalvage(ScatterSettings.Rule rule, Transform group, Vector3 pos, float yaw, float scale)
    {
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(pos, out hit, 2f, NavMesh.AllAreas)) return false;
        pos = hit.position;

        GameObject salvage = new GameObject(rule.prefab.name + "Salvage");
        salvage.transform.SetParent(group, false);
        salvage.transform.position = pos;

        GameObject model = Instantiate(rule.prefab, salvage.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        model.transform.localScale = new Vector3(scale, scale, scale);

        GroundPickup pickup = salvage.AddComponent<GroundPickup>();
        pickup.resourceType = rule.resourceType;
        pickup.amount = rule.resourceAmount;
        pickup.allowOverfill = true;
        return true;
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

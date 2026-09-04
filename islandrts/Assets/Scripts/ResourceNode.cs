using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// A harvestable tree, bush or rock. Owns how much is left in it, how many workers can
/// work it at once, and the visual feedback while they do.
/// </summary>
/// <remarks>
/// Nodes carve the NavMesh, so units path around them rather than through them. Two
/// consequences follow and must be preserved: the standing ring workers gather from sits
/// at the edge of the carve hole rather than at an arbitrary radius, and the depletion
/// shrink scales the Model child rather than the root - scaling the root would move the
/// obstacle and force a NavMesh recarve on every gather tick.
///
/// Crowding is managed by the node, not the worker: a node advertises how many workers fit
/// around it and tracks both claims (on the way) and registrations (arrived), so the rest
/// spill over to the next node instead of orbiting a full one.
/// </remarks>
public class ResourceNode : MonoBehaviour
{
    /// <summary>Order is serialized (prefabs store the int) — append, never reorder.</summary>
    public enum ResourceType { Wood, Food, Stone, Metal }

    [Header("Resource Type")]
    public ResourceType resourceType = ResourceType.Wood;

    [Header("Resource Amount")]
    public int maxResourceAmount = 10;  // Total resources when full
    public float currentAmount = 10f;   // Current resources remaining (can be fractional)

    [Header("Visual Feedback")]
    [Tooltip("Glow strength while the mouse is over this node. The colour comes from the node's own materials (see HoverGlow).")]
    public float hoverGlow = 1.7f;
    public bool scaleWithDepletion = true;  // Shrink as resources deplete
    public float shakeDegrees = 1f;         // Gather-shake pulse amplitude (small tree sway)
    public float shakeFrequency = 7f;       // Wobble speed within a pulse, in Hz
    public float minShakeSpacing = 0.15f;   // Minimum seconds between pulses (stops several workers stacking pulses into a jitter)

    [Header("Byproducts")]
    [Tooltip("Resource units that must come out of this node before it sheds one ground pickup - a stick from a tree or bush, a chunk from a rock. 0 disables.")]
    public float byproductEvery = 4f;
    [Tooltip("How many shed pickups may lie around this node at once, so a well-worked forest litters instead of drowning.")]
    public int maxLooseByproducts = 2;

    public static IReadOnlyList<ResourceNode> ActiveList => ActiveRegistry<ResourceNode>.List;

    /// <summary>
    /// The layer every node's click hitbox lives on (2026-09-03). Every ground
    /// raycast in the game is mask Default (BuildPlacement.groundLayer,
    /// GameStartController.RaycastGround), so a node collider on Default is hit
    /// INSTEAD of the terrain and the placement ghost parks at the tree's base —
    /// the ghost-collider gotcha, wearing bark. Hover and click still work: Unity's
    /// OnMouse* events use the camera's eventMask, not the ground mask. Same
    /// pattern as GroundPickup.ClickLayer (7). Set at runtime so prefabs and
    /// PropScatter nodes alike get it with no editor step.
    /// </summary>
    public const int ClickLayer = 8;
    public const int ClickMask = 1 << ClickLayer;

    void Awake()
    {
        ActiveRegistry<ResourceNode>.Register(this);
        gameObject.layer = ClickLayer;   // root only: the collider lives here, the art child has none
    }

    private Material[] nodeMaterials;
    private HoverGlow glow;
    private float byproductProgress;
    private List<Worker> activeWorkers = new List<Worker>();
    private List<Worker> claimedWorkers = new List<Worker>();
    private Vector3 originalScale;
    private NavMeshObstacle cachedObstacle;

    // --- Gather shake: wobble the visual "Model" child while workers are chipping away ---
    // Rotation-only, on the Model child only. The ROOT must never move/rotate: it drives
    // the NavMeshObstacle, the gather ring, and worker anchors. Baseline is captured
    // lazily at first shake so it composes with TreeVariance's Start-time yaw jitter.
    private Transform shakeModel;
    private Quaternion shakeBaseRotation;
    private bool shakeBaselineCaptured = false;
    private bool isShaking = false;
    private float pulseStartTime = -999f;  // when the current shake pulse began
    private float nextPulseTime = 0f;      // earliest time the next pulse may fire
    private const float PulseDuration = 0.3f;  // length of one shake pulse

    void Start()
    {
        // Initialize current amount to max
        currentAmount = maxResourceAmount;

        // Setup NavMeshObstacle for enemy pathfinding
        SetupNavMeshObstacle();
        cachedObstacle = GetComponent<NavMeshObstacle>();

        // Instanced material copies for hover feedback and the occlusion fade.
        EnsureNodeMaterials();

        // Trees are tall enough to hide a unit from the RTS camera, so they fade while
        // one is behind them. Added here rather than on the prefab so it also covers
        // trees built at runtime by PropScatter.
        if (resourceType == ResourceType.Wood && GetComponent<OcclusionFade>() == null)
            gameObject.AddComponent<OcclusionFade>();

        // Save original scale for depletion visual
        originalScale = transform.localScale;

        // Gather shake target: the plumbed art child. Null pre-plumb — shake just no-ops.
        shakeModel = transform.Find("Model");
    }

    /// <summary>
    /// The instanced material copies for every renderer slot on this node, created on
    /// first use. Shared with <see cref="OcclusionFade"/>: reading renderer.materials
    /// instantiates, so two collectors would end up writing to different copies of the
    /// same material and the hover highlight and the fade would fight.
    /// </summary>
    public Material[] EnsureNodeMaterials()
    {
        if (nodeMaterials == null)
        {
            // ALL renderers - the low-poly art meshes are multi-submesh, so tinting only
            // slot 0 would highlight a fraction of the node.
            nodeMaterials = RendererTint.Collect(GetComponentsInChildren<Renderer>());

            // Hover feedback is an emissive glow, not a colour tint: the tint fought the
            // occlusion fade for the same colour channel and washed the node out.
            glow = HoverGlow.Attach(gameObject, nodeMaterials, 0f, hoverGlow, 0.4f);
        }
        return nodeMaterials;
    }

    void SetupNavMeshObstacle()
    {
        // Get or add NavMeshObstacle component
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle == null)
        {
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
        }

        // CARVING ON (2026-08-26). Non-carving obstacles do not affect pathfinding
        // at all — paths ran straight THROUGH trees, and agents' local avoidance then
        // fought the obstacle: units visibly slowed when brushing past nodes and
        // enemies chasing warriors froze dead behind trunks. Carving makes the
        // pathfinder route around nodes like it already does for buildings.
        //
        // The old "carving caused constant rebuilds" rationale predates the runtime
        // NavMeshSurface: nodes never move (carveOnlyStationary), so the only carve
        // churn left is the one-off local patch when a node depletes/respawns —
        // rare, and every destination in the codebase is already carve-safe
        // (SamplePosition / TrySetDestination-with-retry everywhere).
        //
        // IMPORTANT: depletion shrink must NOT touch this obstacle — Update() scales
        // the Model child, never the root, or every gather tick would re-carve.
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        obstacle.shape = NavMeshObstacleShape.Capsule;

        // Trunk-tight radii (the canopy is above agent height): big enough that
        // paths clear the visible trunk, small enough that the carve hole plus
        // bake-erosion (~0.5) keeps the gather ring reachable.
        switch (resourceType)
        {
            case ResourceType.Wood:  // Trees — trunk only, not the old 0.8 canopy-sized blob
                obstacle.radius = 0.45f;
                obstacle.height = 2f;
                break;
            case ResourceType.Food:  // Bushes
                obstacle.radius = 0.45f;
                obstacle.height = 1f;
                break;
            case ResourceType.Stone:  // Rocks
            case ResourceType.Metal:  // Ore boulders — same footprint as rocks
                obstacle.radius = 0.5f;
                obstacle.height = 1.0f;
                break;
        }
    }

    // Depletion shrink target: the Model child, NEVER the root. The root drives the
    // (now carving) NavMeshObstacle — scaling it would re-carve the NavMesh every
    // gather tick. Model's baseline scale is captured lazily at first shrink so it
    // composes with TreeVariance's Start-time scale jitter (Start order is undefined).
    private Vector3 modelOriginalScale;
    private bool modelScaleCaptured = false;

    void Update()
    {
        // Update visual scale based on depletion
        if (scaleWithDepletion && currentAmount < maxResourceAmount)
        {
            float percentRemaining = Mathf.Clamp01(currentAmount / maxResourceAmount);
            // Don't shrink below 50% size
            float scaleFactor = Mathf.Lerp(0.5f, 1f, percentRemaining);

            if (shakeModel != null)
            {
                if (!modelScaleCaptured)
                {
                    modelOriginalScale = shakeModel.localScale;
                    modelScaleCaptured = true;
                }
                shakeModel.localScale = modelOriginalScale * scaleFactor;
            }
            else
            {
                // Pre-plumb fallback (no Model child): legacy root scaling
                transform.localScale = originalScale * scaleFactor;
            }
        }

        UpdateGatherShake();
    }

    /// <summary>
    /// Fire one shake pulse. Called by a Worker every time its gathering sound ticks, so
    /// the node visibly reacts on the beat of the chop/mining sound rather than on a timer
    /// of its own. Spacing-guarded so several workers on one node can't stack pulses into
    /// a continuous jitter.
    /// </summary>
    public void TriggerShakePulse()
    {
        if (shakeModel == null || Time.time < nextPulseTime) return;

        if (!shakeBaselineCaptured)
        {
            shakeBaseRotation = shakeModel.localRotation;
            shakeBaselineCaptured = true;
        }
        pulseStartTime = Time.time;
        nextPulseTime = Time.time + Mathf.Max(0.05f, minShakeSpacing);
    }

    /// <summary>
    /// Plays out the pulse started by TriggerShakePulse: a short damped wobble on the
    /// Model child. The sin(pi*x) envelope starts and ends at zero so the pulse never
    /// snaps. Restores the exact baseline rotation when the pulse ends.
    /// Zero GC; a single transform write per frame while a pulse is playing.
    /// </summary>
    void UpdateGatherShake()
    {
        if (shakeModel == null) return;

        float elapsed = Time.time - pulseStartTime;
        if (elapsed < PulseDuration)
        {
            isShaking = true;
            // Envelope 0 -> 1 -> 0 across the pulse; two out-of-phase sines inside it
            // so the wobble reads organic rather than metronomic.
            float envelope = Mathf.Sin(Mathf.PI * (elapsed / PulseDuration)) * shakeDegrees;
            float t = elapsed * shakeFrequency * 2f * Mathf.PI;
            float pitch = Mathf.Sin(t) * envelope;
            float roll = Mathf.Sin(t * 1.37f + 1.7f) * envelope;
            shakeModel.localRotation = shakeBaseRotation * Quaternion.Euler(pitch, 0f, roll);
        }
        else if (isShaking)
        {
            shakeModel.localRotation = shakeBaseRotation;
            isShaking = false;
        }
    }

    void OnMouseEnter()
    {
        if (glow != null) glow.SetHovered(true);
    }

    void OnMouseExit()
    {
        if (glow != null) glow.SetHovered(false);
    }

    // ---- Worker capacity: how many workers physically fit around this node ----
    // Derived from the standing-ring circumference and how much of the surrounding
    // NavMesh is actually open (a node against walls/water fits fewer workers).
    // Cached briefly because building walls changes the answer.
    private int cachedMaxWorkers = -1;
    private float maxWorkersCacheTime = -999f;
    private const float MaxWorkersCacheDuration = 5f;
    private const float WorkerSlotArc = Worker.AgentRadius * 2f + 0.25f;  // ring arc-length one worker occupies (agent diameter + margin — derives from the worker's avoidance radius)

    // Where workers stand: just outside the avoidance obstacle. Shared by
    // GetGatherPoint, the capacity math, and GatherExecutor's arrival check
    // so they can never drift apart.
    public float GatherRingRadius
    {
        get
        {
            // Nodes carve the NavMesh now: the hole is the obstacle radius expanded
            // by the bake agent radius (~0.5), so the ring must sit at the hole edge,
            // not hug the obstacle. GetGatherPoint additionally snaps ring points
            // through NavMesh.SamplePosition, so a slightly-inside point self-heals.
            float obstacleRadius = cachedObstacle != null ? cachedObstacle.radius : 0.5f;
            return obstacleRadius + 0.55f;
        }
    }

    public int GetMaxWorkers()
    {
        if (cachedMaxWorkers > 0 && Time.time - maxWorkersCacheTime < MaxWorkersCacheDuration)
            return cachedMaxWorkers;

        // Standing radius = gather ring + the stop distance workers leave themselves.
        float standRadius = GatherRingRadius + Worker.GatherStopDistance + 0.15f;
        int ringSlots = Mathf.FloorToInt(2f * Mathf.PI * standRadius / WorkerSlotArc);

        // Scale the cap by the fraction of the surroundings that is walkable.
        int open = 0;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 candidate = transform.position +
                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * standRadius;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 0.75f, NavMesh.AllAreas))
                open++;
        }

        cachedMaxWorkers = Mathf.Max(1, Mathf.RoundToInt(ringSlots * (open / 8f)));
        maxWorkersCacheTime = Time.time;
        return cachedMaxWorkers;
    }

    /// <summary>
    /// True if this worker may head for / gather at this node. Workers already
    /// registered or claiming here always keep their slot.
    /// </summary>
    public bool HasWorkerRoom(Worker forWorker)
    {
        if (forWorker != null &&
            (activeWorkers.Contains(forWorker) || claimedWorkers.Contains(forWorker)))
            return true;

        // Clean dead workers so they can't hold slots forever (same pattern as GetClaimCount)
        for (int i = activeWorkers.Count - 1; i >= 0; i--)
        {
            if (activeWorkers[i] == null)
                activeWorkers.RemoveAt(i);
        }

        return activeWorkers.Count + GetClaimCount() < GetMaxWorkers();
    }

    // Register a worker to gather from this node
    public bool RegisterWorker(Worker worker)
    {
        if (currentAmount <= 0)
        {
            return false;  // Node is depleted
        }

        if (!HasWorkerRoom(worker))
        {
            return false;  // No room around the node - caller moves on to another one
        }

        if (!activeWorkers.Contains(worker))
        {
            activeWorkers.Add(worker);
            // Debug.Log($"ResourceNode: Worker registered. {activeWorkers.Count} workers now gathering {resourceType}");
        }

        return true;
    }

    // Unregister a worker from this node
    public void UnregisterWorker(Worker worker)
    {
        if (activeWorkers.Contains(worker))
        {
            activeWorkers.Remove(worker);
            // Debug.Log($"ResourceNode: Worker unregistered. {activeWorkers.Count} workers remaining");
        }
    }

    // Worker attempts to gather resources
    // Returns amount actually gathered.
    // shedByproducts=false is the player's hand-harvest: it takes the stick in hand
    // rather than leaving it on the ground, so the two paths never double up.
    public float GatherResources(float requestedAmount, bool shedByproducts = true)
    {
        if (currentAmount <= 0)
        {
            // Node is empty
            return 0f;
        }

        // Can't gather more than what's available
        float amountGathered = Mathf.Min(requestedAmount, currentAmount);

        // Deplete the node
        currentAmount -= amountGathered;

        if (shedByproducts) ShedByproduct(amountGathered);

        // Destroy node if empty
        if (currentAmount <= 0.01f)  // Small threshold for floating point
        {
            // Notify the spawner so it can respawn this resource
            if (ResourceSpawner.Instance != null)
            {
                ResourceSpawner.Instance.NotifyResourceDepleted(resourceType, transform.position);
            }

            Destroy(gameObject);
        }

        return amountGathered;
    }

    // ---- Byproducts: what falls off a node while it is being worked ----

    /// <summary>
    /// The pooled resource this node yields, as an item in the hand. Only the player's
    /// character uses this - workers deliver straight into the pool.
    /// </summary>
    public ItemDef PrimaryItem => ItemCatalog.ResourceItem(resourceType);

    /// <summary>
    /// The crafting material this node also yields: sticks off a tree or a bush, chunks
    /// off a rock or an ore boulder. Sticks off bushes are deliberate - a bush is a
    /// meaningful early source of both food and firewood.
    /// </summary>
    public ItemDef ByproductItem =>
        (resourceType == ResourceType.Stone || resourceType == ResourceType.Metal)
            ? ItemCatalog.StoneChunk
            : ItemCatalog.Stick;

    /// <summary>
    /// Count <paramref name="units"/> toward the next shed pickup and drop one on the
    /// ground when the node has given up enough. This is what puts sticks under a worked
    /// tree and chunks around a worked rock, so the island stays stocked with the
    /// materials the player's character crafts from without the spawner having to
    /// carpet it at start.
    /// </summary>
    void ShedByproduct(float units)
    {
        if (byproductEvery <= 0f || PickupSpawner.Instance == null) return;

        byproductProgress += units;
        if (byproductProgress < byproductEvery) return;
        byproductProgress -= byproductEvery;

        ShedOneByproduct();
    }

    /// <summary>
    /// Drop one byproduct beside this node right now, respecting the loose cap. The
    /// worked-node path counts units first and calls this; the player's hand-harvest
    /// calls it directly when their hands are too full to take the material, so a full
    /// inventory costs them the walk home rather than the stick itself (2026-09-03).
    /// </summary>
    public bool ShedOneByproduct()
    {
        if (PickupSpawner.Instance == null) return false;
        if (CountLooseByproducts() >= maxLooseByproducts) return false;

        // Rock throws off more slabs than pebbles; a worked tree mostly sheds twigs.
        float bigChance = (resourceType == ResourceType.Stone || resourceType == ResourceType.Metal) ? 0.65f : 0.15f;
        return PickupSpawner.Instance.DropByproduct(resourceType, transform.position, GatherRingRadius + 1.2f, bigChance);
    }

    /// <summary>
    /// Pickups already lying within arm's reach of this node. Only ever called on a shed
    /// event (once per <see cref="byproductEvery"/> units), so the linear scan is fine.
    /// </summary>
    int CountLooseByproducts()
    {
        var list = GroundPickup.ActiveList;
        float rangeSq = (GatherRingRadius + 2.5f) * (GatherRingRadius + 2.5f);
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null) continue;
            Vector3 d = list[i].transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= rangeSq) n++;
        }
        return n;
    }

    // Check if node has resources remaining
    public bool HasResources()
    {
        return currentAmount > 0;
    }

    // Claim system: track workers heading TO this node (not just gathering)
    public void ClaimNode(Worker worker)
    {
        if (!claimedWorkers.Contains(worker))
            claimedWorkers.Add(worker);
    }

    public void UnclaimNode(Worker worker)
    {
        claimedWorkers.Remove(worker);
    }

    public int GetClaimCount()
    {
        // Clean up null references (manual loop — no lambda allocation)
        for (int i = claimedWorkers.Count - 1; i >= 0; i--)
        {
            if (claimedWorkers[i] == null)
                claimedWorkers.RemoveAt(i);
        }
        return claimedWorkers.Count;
    }

    /// <summary>
    /// Returns a valid NavMesh position on the nearest side of this resource node
    /// to the given worker position. This prevents workers from pathing to the far
    /// side of large obstacles like stone nodes.
    /// </summary>
    public Vector3 GetGatherPoint(Vector3 workerPosition)
    {
        float offset = GatherRingRadius;  // Just outside the obstacle

        // Try direct approach: nearest side from worker
        Vector3 dirToWorker = (workerPosition - transform.position).normalized;
        Vector3 gatherPoint = transform.position + dirToWorker * offset;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(gatherPoint, out hit, 2f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Fallback: try 8 cardinal directions and pick the one closest to worker
        float bestDist = float.MaxValue;
        Vector3 bestPoint = transform.position;
        bool found = false;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 candidate = transform.position + dir * offset;

            if (NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas))
            {
                float dist = Vector3.Distance(hit.position, workerPosition);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPoint = hit.position;
                    found = true;
                }
            }
        }

        if (found)
            return bestPoint;

        // Last resort: just return the node center
        return transform.position;
    }

    void OnDestroy()
    {
        ActiveRegistry<ResourceNode>.Unregister(this);
        // A carving obstacle disappearing patches the NavMesh locally — worth
        // attributing when a frame spikes.
        PerfCounters.Hit(PerfCounters.K.CarveChange);
    }
}

using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class ResourceNode : MonoBehaviour
{
    public enum ResourceType { Wood, Food, Stone }

    [Header("Resource Type")]
    public ResourceType resourceType = ResourceType.Wood;

    [Header("Resource Amount")]
    public int maxResourceAmount = 10;  // Total resources when full
    public float currentAmount = 10f;   // Current resources remaining (can be fractional)

    [Header("Gathering")]

    [Header("Visual Feedback")]
    public Color highlightColor = Color.yellow;
    public bool scaleWithDepletion = true;  // Shrink as resources deplete

    public static IReadOnlyList<ResourceNode> ActiveList => ActiveRegistry<ResourceNode>.List;

    void Awake() { ActiveRegistry<ResourceNode>.Register(this); }

    private Material[] nodeMaterials;
    private Color[] originalColors;
    private bool isHighlighted = false;
    private List<Worker> activeWorkers = new List<Worker>();
    private List<Worker> claimedWorkers = new List<Worker>();
    private Vector3 originalScale;
    private NavMeshObstacle cachedObstacle;

    void Start()
    {
        // Initialize current amount to max
        currentAmount = maxResourceAmount;

        // Setup NavMeshObstacle for enemy pathfinding
        SetupNavMeshObstacle();
        cachedObstacle = GetComponent<NavMeshObstacle>();

        // Get ALL renderers for visual feedback (tree has trunk + leaves), and instance every
        // material slot - the low-poly art meshes are multi-submesh, so tinting only slot 0
        // would highlight a fraction of the node.
        nodeMaterials = RendererTint.Collect(GetComponentsInChildren<Renderer>());
        originalColors = RendererTint.CaptureColors(nodeMaterials);

        // Save original scale for depletion visual
        originalScale = transform.localScale;
    }

    void SetupNavMeshObstacle()
    {
        // Get or add NavMeshObstacle component
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle == null)
        {
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
        }

        // NO CARVING — resource nodes use local avoidance only.
        // Carving caused constant NavMesh rebuilds every time a resource depleted/respawned,
        // which invalidated paths for ALL nearby agents and caused synchronized stuttering.
        // Workers already path to offset gather points, so carving is unnecessary.
        obstacle.carving = false;
        obstacle.shape = NavMeshObstacleShape.Capsule;

        // Size based on resource type
        switch (resourceType)
        {
            case ResourceType.Wood:  // Trees
                obstacle.radius = 0.8f;
                obstacle.height = 2f;
                break;
            case ResourceType.Food:  // Bushes
                obstacle.radius = 0.5f;
                obstacle.height = 1f;
                break;
            case ResourceType.Stone:  // Rocks
                obstacle.radius = 0.5f;
                obstacle.height = 1.0f;
                break;
        }
    }

    void Update()
    {
        // Update visual scale based on depletion
        if (scaleWithDepletion && currentAmount < maxResourceAmount)
        {
            float percentRemaining = Mathf.Clamp01(currentAmount / maxResourceAmount);
            // Don't shrink below 50% size
            float scaleFactor = Mathf.Lerp(0.5f, 1f, percentRemaining);
            transform.localScale = originalScale * scaleFactor;
        }
    }

    void OnMouseEnter()
    {
        // Highlight all parts when mouse hovers over
        if (!isHighlighted)
        {
            RendererTint.SetColor(nodeMaterials, highlightColor);
            isHighlighted = true;
        }
    }

    void OnMouseExit()
    {
        // Remove highlight from all parts when mouse leaves
        if (isHighlighted)
        {
            RendererTint.RestoreColors(nodeMaterials, originalColors);
            isHighlighted = false;
        }
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
            float obstacleRadius = cachedObstacle != null ? cachedObstacle.radius : 0.5f;
            return obstacleRadius * 1.1f;
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
    // Returns amount actually gathered
    public float GatherResources(float requestedAmount)
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

    // Check if node has resources remaining
    public bool HasResources()
    {
        return currentAmount > 0;
    }

    // Get number of workers currently gathering
    public int GetWorkerCount()
    {
        return activeWorkers.Count;
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
    }
}

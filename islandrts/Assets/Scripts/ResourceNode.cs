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
    public float gatherRatePerWorker = 1f;  // Resources per second per worker
    public float speedMultiplier = 1f;      // Bonus speed when multiple workers (1 = no bonus)

    [Header("Visual Feedback")]
    public Color highlightColor = Color.yellow;
    public bool scaleWithDepletion = true;  // Shrink as resources deplete

    // Static registry for O(1) lookup instead of FindObjectsByType
    private static readonly List<ResourceNode> activeList = new List<ResourceNode>();
    public static IReadOnlyList<ResourceNode> ActiveList => activeList;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeList.Clear(); }

    void Awake() { activeList.Add(this); }

    private Renderer[] nodeRenderers;
    private Color[] originalColors;
    private bool isHighlighted = false;
    private List<Worker> activeWorkers = new List<Worker>();
    private Vector3 originalScale;
    private float lastUpdateTime;

    void Start()
    {
        // Initialize current amount to max
        currentAmount = maxResourceAmount;

        // Setup NavMeshObstacle for enemy pathfinding
        SetupNavMeshObstacle();

        // Get ALL renderers for visual feedback (tree has trunk + leaves)
        nodeRenderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[nodeRenderers.Length];

        for (int i = 0; i < nodeRenderers.Length; i++)
        {
            originalColors[i] = nodeRenderers[i].material.color;
        }

        // Save original scale for depletion visual
        originalScale = transform.localScale;
        lastUpdateTime = Time.time;
    }

    void SetupNavMeshObstacle()
    {
        // Get or add NavMeshObstacle component
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle == null)
        {
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
        }

        // Configure obstacle based on resource type
        obstacle.carving = true;  // Enable carving so workers path around resource nodes
        obstacle.carveOnlyStationary = true;  // Only carve when not moving (performance)
        obstacle.shape = NavMeshObstacleShape.Capsule;  // Capsule works well for trees/rocks

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

        obstacle.carvingMoveThreshold = 0.1f;
        obstacle.carvingTimeToStationary = 0.5f;

        Debug.Log($"ResourceNode: NavMeshObstacle setup for {resourceType} - Radius: {obstacle.radius}, Carving: {obstacle.carving}");
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
            foreach (var renderer in nodeRenderers)
            {
                renderer.material.color = highlightColor;
            }
            isHighlighted = true;
        }
    }

    void OnMouseExit()
    {
        // Remove highlight from all parts when mouse leaves
        if (isHighlighted)
        {
            for (int i = 0; i < nodeRenderers.Length; i++)
            {
                nodeRenderers[i].material.color = originalColors[i];
            }
            isHighlighted = false;
        }
    }

    // Register a worker to gather from this node
    public bool RegisterWorker(Worker worker)
    {
        if (currentAmount <= 0)
        {
            return false;  // Node is depleted
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

        // Calculate gather rate bonus from multiple workers
        float workerBonus = 1f + (activeWorkers.Count - 1) * speedMultiplier;
        float actualGatherRate = gatherRatePerWorker * workerBonus;

        // Can't gather more than what's available
        float amountGathered = Mathf.Min(requestedAmount, currentAmount);

        // Deplete the node
        currentAmount -= amountGathered;

        // Debug.Log($"ResourceNode: Gathered {amountGathered:F2} {resourceType}. {currentAmount:F2} remaining. ({activeWorkers.Count} workers, {workerBonus:F1}x speed)");

        // Destroy node if empty
        if (currentAmount <= 0.01f)  // Small threshold for floating point
        {
            Debug.Log($"ResourceNode: {resourceType} depleted! Destroying...");

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

    /// <summary>
    /// Returns a valid NavMesh position on the nearest side of this resource node
    /// to the given worker position. This prevents workers from pathing to the far
    /// side of large obstacles like stone nodes.
    /// </summary>
    public Vector3 GetGatherPoint(Vector3 workerPosition)
    {
        // Get the obstacle radius for offset calculation
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        float obstacleRadius = obstacle != null ? obstacle.radius : 0.5f;
        float offset = obstacleRadius * 1.3f;  // Just outside the obstacle

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
        activeList.Remove(this);
    }

    // Manual gathering removed - workers only now!
}

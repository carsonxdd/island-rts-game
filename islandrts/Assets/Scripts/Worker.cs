using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class Worker : MonoBehaviour
{
    [Header("Assignment")]
    public ResourceNode.ResourceType assignedResourceType = ResourceNode.ResourceType.Wood;
    public BaseBuilding baseBuilding;  // Reference to campfire

    [Header("Gathering Settings")]
    public float gatherRatePerSecond = 1f;  // How fast worker gathers (resources/sec)
    public float carryCapacity = 5.01f;  // Maximum resources worker can carry (slightly over 5 to avoid floating point issues)
    public float searchRadius = 50f;  // How far to search for resources
    public float gatherDistance = 2.5f;  // How close worker needs to be to start gathering
    public float deliveryDistance = 3.5f;  // How close worker needs to be to deliver resources

    [Header("Current State")]
    public float carryAmount = 0f;  // Resources currently carrying (can be fractional)

    [Header("State")]
    public WorkerState currentState = WorkerState.Idle;

    [Header("Visual Feedback")]
    public bool showStateText = true;
    public float textHeightOffset = 2f;  // How high above worker to show text

    public enum WorkerState
    {
        Idle,
        MovingToResource,
        Gathering,
        ReturningToBase
    }

    private NavMeshAgent agent;
    private ResourceNode targetResource;
    private bool isInitialized = false;
    private bool isSearchingForResource = false;
    private bool isRegisteredAtNode = false;  // Track if we're registered at current node

    // Stuck detection
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private float stuckCheckInterval = 2f;  // Check every 2 seconds
    private float minMoveDistance = 0.5f;   // Must move at least this far to not be stuck
    private int consecutiveStuckCount = 0;   // How many times stuck in a row
    private int maxStuckAttempts = 3;        // Give up after this many stuck attempts

    // Unstuck behavior
    private bool isUnsticking = false;       // Currently moving to unstuck position
    private WorkerState stateBeforeUnstuck;  // State to return to after unsticking
    private ResourceNode targetBeforeUnstuck; // Target to resume after unsticking

    // Visual state display
    private GameObject stateTextObject;
    private TextMeshPro stateText;
    private Camera mainCamera;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            Debug.LogError("Worker: No NavMeshAgent found!");
        }
        else
        {
            // Configure NavMeshAgent for smooth navigation around obstacles
            agent.stoppingDistance = gatherDistance * 0.8f;  // Stop slightly before gather distance
            agent.acceleration = 8f;  // Quick acceleration
            agent.angularSpeed = 180f;  // Very fast turning to avoid obstacles
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;  // Best avoidance
            agent.avoidancePriority = 50;  // Medium priority (0-99, lower = higher priority)
            agent.radius = 0.6f;  // Larger radius to stay further from obstacles
        }

        // Find main camera for text billboard effect
        mainCamera = Camera.main;

        // Create state text display
        if (showStateText)
        {
            CreateStateText();
        }

        // Wait a moment for NavMeshAgent to fully initialize
        currentState = WorkerState.Idle;
        Invoke(nameof(Initialize), 0.5f);  // Small delay before starting work
    }

    void Initialize()
    {
        isInitialized = true;
        lastPosition = transform.position;
        Debug.Log($"Worker: Initialized and ready to work on {assignedResourceType}");
    }

    void Update()
    {
        // Don't do anything until initialized
        if (!isInitialized) return;

        // Update state text display
        if (showStateText && stateText != null)
        {
            UpdateStateText();
        }

        // Stuck detection for moving states
        if (currentState == WorkerState.MovingToResource || currentState == WorkerState.ReturningToBase)
        {
            CheckIfStuck();
        }

        // Handle unsticking behavior first
        if (isUnsticking)
        {
            CheckIfReachedUnstuckPosition();
            return;
        }

        // State machine
        switch (currentState)
        {
            case WorkerState.Idle:
                // Only search once per idle state
                if (!isSearchingForResource)
                {
                    isSearchingForResource = true;
                    FindNearestResource();
                }
                break;

            case WorkerState.MovingToResource:
                CheckIfReachedResource();
                break;

            case WorkerState.Gathering:
                GatherResource();
                break;

            case WorkerState.ReturningToBase:
                CheckIfReachedBase();
                break;
        }
    }

    void CheckIfReachedUnstuckPosition()
    {
        // Check if we've reached the unstuck position or stopped moving
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            Debug.Log("Worker: Reached unstuck position, resuming previous task");
            isUnsticking = false;

            // Resume previous state
            currentState = stateBeforeUnstuck;
            targetResource = targetBeforeUnstuck;

            // Reset path for clean resume
            agent.ResetPath();

            // Resume based on what we were doing
            if (currentState == WorkerState.MovingToResource && targetResource != null)
            {
                agent.SetDestination(targetResource.transform.position);
            }
            else if (currentState == WorkerState.ReturningToBase)
            {
                ReturnToBase();
            }
            else
            {
                // Default to idle if state is unclear
                currentState = WorkerState.Idle;
                isSearchingForResource = false;
            }
        }
    }

    void CheckIfStuck()
    {
        // Skip stuck detection if we're already unsticking
        if (isUnsticking) return;

        stuckTimer += Time.deltaTime;

        // Check every X seconds if we've moved
        if (stuckTimer >= stuckCheckInterval)
        {
            float distanceMoved = Vector3.Distance(transform.position, lastPosition);

            if (distanceMoved < minMoveDistance)
            {
                // Worker hasn't moved much - STUCK!
                consecutiveStuckCount++;
                Debug.LogWarning($"Worker: STUCK! Only moved {distanceMoved:F2} units in {stuckCheckInterval}s (attempt {consecutiveStuckCount}/{maxStuckAttempts})");

                if (consecutiveStuckCount >= maxStuckAttempts)
                {
                    // Stuck too many times - give up and reset
                    Debug.LogError($"Worker: Failed to unstuck after {maxStuckAttempts} attempts. Resetting...");
                    UnregisterFromNode();
                    targetResource = null;
                    currentState = WorkerState.Idle;
                    isSearchingForResource = false;
                    isUnsticking = false;
                    consecutiveStuckCount = 0;
                }
                else
                {
                    // Try to unstuck by moving to nearby random position
                    AttemptUnstuck();
                }
            }
            else
            {
                // Worker moved successfully - reset stuck counter
                consecutiveStuckCount = 0;
            }

            // Reset for next check
            lastPosition = transform.position;
            stuckTimer = 0f;
        }
    }

    void AttemptUnstuck()
    {
        // Save current state and target
        stateBeforeUnstuck = currentState;
        targetBeforeUnstuck = targetResource;
        isUnsticking = true;

        // Find a random nearby position to move to
        Vector3 unstuckPosition = GetRandomNearbyPosition();

        // Move to unstuck position
        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.SetDestination(unstuckPosition);
            Debug.Log($"Worker: Attempting to unstuck by moving to nearby position {unstuckPosition}");
        }
    }

    Vector3 GetRandomNearbyPosition()
    {
        // Try to find a valid position 1-2 units away in a random direction
        for (int attempts = 0; attempts < 10; attempts++)
        {
            float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float randomDistance = Random.Range(1f, 2f);

            Vector3 randomDirection = new Vector3(
                Mathf.Cos(randomAngle),
                0f,
                Mathf.Sin(randomAngle)
            );

            Vector3 targetPosition = transform.position + randomDirection * randomDistance;

            // Check if this position is valid on the NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(targetPosition, out hit, 3f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        // Fallback: just move 2 units in a random direction from current position
        float fallbackAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return transform.position + new Vector3(Mathf.Cos(fallbackAngle) * 2f, 0f, Mathf.Sin(fallbackAngle) * 2f);
    }

    void FindNearestResource()
    {
        // Find all resource nodes in scene
        ResourceNode[] allResources = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);

        ResourceNode closestResource = null;
        float closestDistance = searchRadius;

        foreach (ResourceNode resource in allResources)
        {
            // Only look for our assigned resource type
            if (resource.resourceType != assignedResourceType)
                continue;

            float distance = Vector3.Distance(transform.position, resource.transform.position);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestResource = resource;
            }
        }

        if (closestResource != null)
        {
            // Found a resource! Go to it
            targetResource = closestResource;

            // Make sure agent is ready before setting destination
            if (agent.isOnNavMesh && agent.enabled)
            {
                agent.SetDestination(targetResource.transform.position);
                currentState = WorkerState.MovingToResource;
                isSearchingForResource = false;  // Reset flag

                // Reset stuck detection for new path
                lastPosition = transform.position;
                stuckTimer = 0f;

                Debug.Log($"Worker: Found {assignedResourceType} at {targetResource.transform.position}");
            }
            else
            {
                Debug.LogWarning("Worker: Agent not on NavMesh yet, waiting...");
                isSearchingForResource = false;  // Reset flag
                Invoke(nameof(FindNearestResource), 0.5f);
            }
        }
        else
        {
            // No resources found, wait a bit and try again
            Debug.Log($"Worker: No {assignedResourceType} resources found nearby. Waiting...");
            isSearchingForResource = false;  // Reset flag
            Invoke(nameof(FindNearestResource), 3f);  // Try again in 3 seconds
        }
    }

    void CheckIfReachedResource()
    {
        // Check if target still exists
        if (targetResource == null)
        {
            Debug.Log("Worker: Target resource disappeared!");
            currentState = WorkerState.Idle;
            isSearchingForResource = false;  // Reset so we find a new target
            return;
        }

        // Check actual distance to resource (more reliable than agent.remainingDistance)
        float distanceToResource = Vector3.Distance(transform.position, targetResource.transform.position);

        // Arrived when within gather distance
        if (distanceToResource <= gatherDistance)
        {
            // Stop moving
            agent.ResetPath();

            // Register with the resource node
            if (targetResource.RegisterWorker(this))
            {
                isRegisteredAtNode = true;
                currentState = WorkerState.Gathering;

                // Reset stuck detection - we reached our destination
                stuckTimer = 0f;

                Debug.Log("Worker: Arrived at resource, starting to gather...");
            }
            else
            {
                // Node is empty, find another
                Debug.Log("Worker: Node is empty!");
                targetResource = null;
                currentState = WorkerState.Idle;
                isSearchingForResource = false;
            }
        }
    }

    void GatherResource()
    {
        // Check if target still exists
        if (targetResource == null)
        {
            Debug.Log("Worker: Resource disappeared while gathering!");
            UnregisterFromNode();
            currentState = WorkerState.Idle;
            isSearchingForResource = false;
            return;
        }

        // Check if we're full (with small threshold for floating point precision)
        if (carryAmount >= carryCapacity - 0.01f)
        {
            // Snap to exact capacity to avoid 4.99 issues
            carryAmount = carryCapacity;
            Debug.Log($"Worker: Inventory full ({carryAmount}/{carryCapacity})! Returning to base.");
            UnregisterFromNode();
            ReturnToBase();
            return;
        }

        // Check if node is empty
        if (!targetResource.HasResources())
        {
            Debug.Log("Worker: Node depleted while gathering!");
            UnregisterFromNode();
            targetResource = null;

            // If carrying anything, return to base. Otherwise find new resource
            if (carryAmount > 0)
            {
                ReturnToBase();
            }
            else
            {
                currentState = WorkerState.Idle;
                isSearchingForResource = false;
            }
            return;
        }

        // Gather incrementally
        float spaceInInventory = carryCapacity - carryAmount;
        float wantToGather = gatherRatePerSecond * Time.deltaTime;
        float requestAmount = Mathf.Min(wantToGather, spaceInInventory);

        // Ask the node for resources
        float actuallyGathered = targetResource.GatherResources(requestAmount);
        carryAmount += actuallyGathered;

        // Snap to capacity if very close (fix floating point issues)
        if (carryAmount >= carryCapacity - 0.01f)
        {
            carryAmount = carryCapacity;
        }

        // Check conditions after gathering
        bool isFull = carryAmount >= carryCapacity - 0.01f;  // Small threshold
        bool nodeEmpty = !targetResource.HasResources();

        if (isFull || nodeEmpty)
        {
            // Done gathering from this node
            UnregisterFromNode();

            if (nodeEmpty)
            {
                targetResource = null;
            }

            ReturnToBase();
        }
    }

    void UnregisterFromNode()
    {
        if (isRegisteredAtNode && targetResource != null)
        {
            targetResource.UnregisterWorker(this);
            isRegisteredAtNode = false;
        }
    }


    void ReturnToBase()
    {
        if (baseBuilding != null && agent.isOnNavMesh && agent.enabled)
        {
            // Calculate the closest point on a ring around the campfire
            // This distributes workers evenly instead of all going to the same side
            Vector3 targetPosition = GetNearestDropoffPoint();

            agent.SetDestination(targetPosition);
            currentState = WorkerState.ReturningToBase;

            // Reset stuck detection for new path
            lastPosition = transform.position;
            stuckTimer = 0f;

            Debug.Log($"Worker: Returning to base with {carryAmount:F2} {assignedResourceType}");
        }
        else
        {
            // No base or can't navigate, just deliver and continue
            DeliverResources();
            currentState = WorkerState.Idle;
            isSearchingForResource = false;
        }
    }

    Vector3 GetNearestDropoffPoint()
    {
        // Calculate direction from campfire to worker
        Vector3 directionToWorker = (transform.position - baseBuilding.transform.position).normalized;

        // Place target point on a ring around the campfire
        // Use a comfortable distance that gives space to navigate around obstacles
        float ringRadius = deliveryDistance * 0.9f;  // ~3.15 units from center (more space to maneuver)
        Vector3 dropoffPoint = baseBuilding.transform.position + (directionToWorker * ringRadius);

        // Keep the Y coordinate at ground level
        dropoffPoint.y = baseBuilding.transform.position.y;

        return dropoffPoint;
    }

    void CheckIfReachedBase()
    {
        if (baseBuilding == null)
        {
            // No base, just deliver and find next resource
            DeliverResources();
            currentState = WorkerState.Idle;
            return;
        }

        // Check actual distance to base - this is the primary delivery condition
        float distanceToBase = Vector3.Distance(transform.position, baseBuilding.transform.position);

        // Check if worker is moving very slowly (rubbing against obstacles)
        if (agent.velocity.magnitude < 0.5f && distanceToBase <= deliveryDistance * 1.5f)
        {
            // Moving very slow and reasonably close - just deliver
            agent.ResetPath();
            DeliverResources();
            currentState = WorkerState.Idle;
            isSearchingForResource = false;
            stuckTimer = 0f;
            consecutiveStuckCount = 0;  // Reset stuck count on successful delivery
            return;
        }

        // Primary delivery condition: close enough to campfire
        if (distanceToBase <= deliveryDistance)
        {
            // Within range - deliver!
            agent.ResetPath();
            DeliverResources();
            currentState = WorkerState.Idle;
            isSearchingForResource = false;
            stuckTimer = 0f;
            consecutiveStuckCount = 0;  // Reset stuck count on successful delivery
            return;
        }

        // Secondary condition: path finished but not quite at target
        // This handles NavMesh limitations (can't path exactly to center)
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            // We've reached as close as we can get
            if (distanceToBase <= deliveryDistance * 2.0f)  // Within 7.0 units (more forgiving)
            {
                // Close enough - NavMesh brought us as close as possible
                Debug.Log($"Worker: Arrived at closest reachable point ({distanceToBase:F2}), delivering");
                agent.ResetPath();
                DeliverResources();
                currentState = WorkerState.Idle;
                isSearchingForResource = false;
                stuckTimer = 0f;
                consecutiveStuckCount = 0;  // Reset stuck count on successful delivery
            }
        }
    }

    void DeliverResources()
    {
        if (carryAmount <= 0) return;

        // Check if ResourceManager exists
        if (ResourceManager.Instance == null)
        {
            Debug.LogError("Worker: Cannot deliver resources - ResourceManager.Instance is null! Make sure ResourceManager exists in scene.");
            carryAmount = 0f;  // Clear carry amount to prevent repeated errors
            return;
        }

        // Round to nearest integer for delivery
        int amountToDeliver = Mathf.RoundToInt(carryAmount);

        // Add resources to ResourceManager
        switch (assignedResourceType)
        {
            case ResourceNode.ResourceType.Wood:
                ResourceManager.Instance.AddWood(amountToDeliver);
                break;
            case ResourceNode.ResourceType.Food:
                ResourceManager.Instance.AddFood(amountToDeliver);
                break;
            case ResourceNode.ResourceType.Stone:
                ResourceManager.Instance.AddStone(amountToDeliver);
                break;
        }

        Debug.Log($"Worker: Delivered {amountToDeliver} {assignedResourceType} to base! (carried {carryAmount:F2})");
        carryAmount = 0f;
    }

    void OnDestroy()
    {
        // Unregister from node if we're destroyed while gathering
        UnregisterFromNode();

        // Clean up state text object
        if (stateTextObject != null)
        {
            Destroy(stateTextObject);
        }
    }

    void CreateStateText()
    {
        // Create a new GameObject for the text
        stateTextObject = new GameObject("WorkerStateText");
        stateTextObject.transform.SetParent(transform);
        stateTextObject.transform.localPosition = Vector3.up * textHeightOffset;

        // Add TextMeshPro component
        stateText = stateTextObject.AddComponent<TextMeshPro>();

        // Configure text properties
        stateText.text = "Idle";
        stateText.fontSize = 3;
        stateText.alignment = TextAlignmentOptions.Center;
        stateText.color = Color.white;

        // Make text face camera (billboard effect handled in Update)
        stateText.enableAutoSizing = false;

        // Set sorting to render on top
        stateText.sortingOrder = 100;
    }

    void UpdateStateText()
    {
        if (stateText == null || stateTextObject == null) return;

        // Update text position to follow worker
        stateTextObject.transform.position = transform.position + Vector3.up * textHeightOffset;

        // Billboard effect - make text face camera
        if (mainCamera != null)
        {
            stateTextObject.transform.LookAt(mainCamera.transform);
            stateTextObject.transform.Rotate(0, 180, 0);  // Flip because LookAt faces away
        }

        // Update text based on current state
        string stateMessage = "";
        switch (currentState)
        {
            case WorkerState.Idle:
                stateMessage = "Searching...";
                stateText.color = Color.gray;
                break;

            case WorkerState.MovingToResource:
                stateMessage = $"Moving to {assignedResourceType}";
                stateText.color = Color.yellow;
                break;

            case WorkerState.Gathering:
                stateMessage = $"Collecting {assignedResourceType}\n({carryAmount:F1}/{carryCapacity:F0})";
                stateText.color = Color.green;
                break;

            case WorkerState.ReturningToBase:
                stateMessage = $"Returning to base\n({carryAmount:F1} {assignedResourceType})";
                stateText.color = Color.cyan;
                break;
        }

        stateText.text = stateMessage;
    }

    // Visual debug in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw search radius
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, searchRadius);

        // Draw line to target
        if (targetResource != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, targetResource.transform.position);
        }
    }
}

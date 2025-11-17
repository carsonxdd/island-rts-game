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

    // Smart approach to base (try different angles when stuck)
    private int approachAttempts = 0;
    private int maxApproachAttempts = 8;  // Try 8 different angles around campfire
    private float approachOffset = 3f;    // How far from campfire to create waypoint

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
            // Set stopping distance to prevent getting too close to targets
            agent.stoppingDistance = 1.5f;
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

    void CheckIfStuck()
    {
        stuckTimer += Time.deltaTime;

        // Check every X seconds if we've moved
        if (stuckTimer >= stuckCheckInterval)
        {
            float distanceMoved = Vector3.Distance(transform.position, lastPosition);

            if (distanceMoved < minMoveDistance)
            {
                // Worker hasn't moved much
                Debug.LogWarning($"Worker: STUCK! Only moved {distanceMoved:F2} units in {stuckCheckInterval} seconds.");

                if (currentState == WorkerState.MovingToResource)
                {
                    // Stuck going to resource - find a different resource
                    UnregisterFromNode();
                    targetResource = null;
                    currentState = WorkerState.Idle;
                    isSearchingForResource = false;
                }
                else if (currentState == WorkerState.ReturningToBase)
                {
                    // Stuck returning to base - try different angle
                    if (baseBuilding != null)
                    {
                        float distanceToBase = Vector3.Distance(transform.position, baseBuilding.transform.position);
                        Debug.Log($"Worker: Stuck at distance {distanceToBase:F2} from base, trying alternate path...");
                        TryAlternateApproachToBase();
                    }
                }
            }

            // Reset for next check
            lastPosition = transform.position;
            stuckTimer = 0f;
        }
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

        // Arrived when within 2.5 units (gives more space around obstacles)
        if (distanceToResource <= 2.5f)
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

    void TryAlternateApproachToBase()
    {
        if (baseBuilding == null || !agent.isOnNavMesh) return;

        float distanceToBase = Vector3.Distance(transform.position, baseBuilding.transform.position);

        // First check: if we're already close, just deliver
        if (distanceToBase <= deliveryDistance * 1.8f)  // Within 6.3 units
        {
            Debug.Log($"Worker: Stuck but reasonably close ({distanceToBase:F2}), delivering from here");
            agent.ResetPath();
            DeliverResources();
            currentState = WorkerState.Idle;
            isSearchingForResource = false;
            approachAttempts = 0;
            return;
        }

        approachAttempts++;

        if (approachAttempts < maxApproachAttempts)
        {
            // Calculate direction from campfire to worker's current position
            Vector3 directionToWorker = (transform.position - baseBuilding.transform.position).normalized;

            // Try different angles around the campfire
            float angleOffset = (360f / maxApproachAttempts) * approachAttempts;
            float radians = angleOffset * Mathf.Deg2Rad;

            // Rotate the direction vector
            Vector3 rotatedDirection = new Vector3(
                directionToWorker.x * Mathf.Cos(radians) - directionToWorker.z * Mathf.Sin(radians),
                0f,
                directionToWorker.x * Mathf.Sin(radians) + directionToWorker.z * Mathf.Cos(radians)
            );

            // Calculate waypoint position at this angle
            Vector3 waypointPosition = baseBuilding.transform.position + (rotatedDirection * approachOffset);
            waypointPosition.y = baseBuilding.transform.position.y;

            // Try to path to this waypoint
            agent.ResetPath();
            agent.SetDestination(waypointPosition);

            Debug.Log($"Worker: Trying alternate approach angle (attempt {approachAttempts}/{maxApproachAttempts})");
        }
        else
        {
            // All angles failed - emergency deliver regardless of distance
            Debug.LogWarning($"Worker: All approach attempts failed! Emergency delivery from {distanceToBase:F2} units");
            agent.ResetPath();
            DeliverResources();
            currentState = WorkerState.Idle;
            isSearchingForResource = false;
            approachAttempts = 0;
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

            // Reset approach attempts for new return trip
            approachAttempts = 0;

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
        // Use slightly less than deliveryDistance so workers can actually reach it
        float ringRadius = deliveryDistance * 0.7f;  // ~2.45 units from center
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

        // Primary delivery condition: close enough to campfire
        if (distanceToBase <= deliveryDistance)
        {
            // Within range - deliver!
            agent.ResetPath();
            DeliverResources();
            currentState = WorkerState.Idle;
            isSearchingForResource = false;
            stuckTimer = 0f;
            approachAttempts = 0;
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
                approachAttempts = 0;
            }
            else
            {
                // Path ended but we're still far - this shouldn't happen, retry
                Debug.LogWarning($"Worker: Path ended too far from base ({distanceToBase:F2}), retrying...");
                approachAttempts++;

                // If we've tried too many times, just deliver anyway
                if (approachAttempts >= 3)
                {
                    Debug.LogWarning($"Worker: Giving up after {approachAttempts} attempts, delivering from {distanceToBase:F2} units");
                    agent.ResetPath();
                    DeliverResources();
                    currentState = WorkerState.Idle;
                    isSearchingForResource = false;
                    stuckTimer = 0f;
                    approachAttempts = 0;
                }
                else
                {
                    ReturnToBase();
                }
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

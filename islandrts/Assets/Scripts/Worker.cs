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
    private float stuckCheckInterval = 5f;  // Check every 5 seconds (reduced frequency)
    private float minMoveDistance = 1.0f;   // Must move at least this far to not be stuck
    private int consecutiveStuckCount = 0;   // How many times stuck in a row
    private int maxStuckAttempts = 2;        // Give up after 2 attempts (reduced from 3)

    // Unstuck behavior
    private bool isUnsticking = false;       // Currently moving to unstuck position
    private WorkerState stateBeforeUnstuck;  // State to return to after unsticking
    private ResourceNode targetBeforeUnstuck; // Target to resume after unsticking

    // Visual state display
    private GameObject stateTextObject;
    private TextMeshPro stateText;
    private Camera mainCamera;

    // Audio - 3D Spatial Sound
    private AudioSource gatheringAudioSource;
    private Coroutine gatheringSoundCoroutine;

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

        // Setup 3D spatial audio for gathering sounds
        SetupGatheringAudioSource();

        // Wait a moment for NavMeshAgent to fully initialize
        currentState = WorkerState.Idle;
        Invoke(nameof(Initialize), 0.5f);  // Small delay before starting work
    }

    void Initialize()
    {
        isInitialized = true;
        lastPosition = transform.position;
        // Debug.Log($"Worker: Initialized and ready to work on {assignedResourceType}");
    }

    void Update()
    {
        // Don't do anything until initialized
        if (!isInitialized) return;

        // Update gathering audio volume based on AudioManager SFX slider
        if (gatheringAudioSource != null && AudioManager.Instance != null)
        {
            gatheringAudioSource.volume = 0.15f * AudioManager.Instance.sfxVolume * AudioManager.Instance.masterVolume;
        }

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
            // Debug.Log("Worker: Reached unstuck position, resuming previous task");
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

                if (consecutiveStuckCount >= maxStuckAttempts)
                {
                    // Stuck too many times - completely reset and find new task
                    Debug.LogWarning($"Worker: Stuck after {maxStuckAttempts} attempts. Resetting to find new target...");

                    // Clean up current task
                    UnregisterFromNode();
                    targetResource = null;

                    // Reset agent
                    agent.ResetPath();
                    agent.isStopped = false;

                    // Reset stuck detection
                    isUnsticking = false;
                    consecutiveStuckCount = 0;

                    // Return to idle to find new resource
                    currentState = WorkerState.Idle;
                    isSearchingForResource = false;
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
        Debug.LogWarning($"Worker: Stuck detected (attempt {consecutiveStuckCount}/{maxStuckAttempts}). Attempting to unstuck...");

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
            agent.isStopped = false;  // Make sure agent can move
            agent.SetDestination(unstuckPosition);
        }
    }

    Vector3 GetRandomNearbyPosition()
    {
        // Try to find a valid position 2-4 units away in a random direction (increased distance)
        for (int attempts = 0; attempts < 10; attempts++)
        {
            float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float randomDistance = Random.Range(2f, 4f);  // Increased from 1-2 to 2-4

            Vector3 randomDirection = new Vector3(
                Mathf.Cos(randomAngle),
                0f,
                Mathf.Sin(randomAngle)
            );

            Vector3 targetPosition = transform.position + randomDirection * randomDistance;

            // Check if this position is valid on the NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(targetPosition, out hit, 5f, NavMesh.AllAreas))  // Increased search radius
            {
                return hit.position;
            }
        }

        // Fallback: just move 3 units in a random direction from current position
        float fallbackAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return transform.position + new Vector3(Mathf.Cos(fallbackAngle) * 3f, 0f, Mathf.Sin(fallbackAngle) * 3f);
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

                // Debug.Log($"Worker: Found {assignedResourceType} at {targetResource.transform.position}");
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
            // Debug.Log($"Worker: No {assignedResourceType} resources found nearby. Waiting...");
            isSearchingForResource = false;  // Reset flag
            Invoke(nameof(FindNearestResource), 3f);  // Try again in 3 seconds
        }
    }

    void CheckIfReachedResource()
    {
        // Check if target still exists
        if (targetResource == null)
        {
            // Debug.Log("Worker: Target resource disappeared!");
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

                // Start gathering sound (3D spatial audio on this worker)
                StartGatheringSound();

                // Debug.Log("Worker: Arrived at resource, starting to gather...");
            }
            else
            {
                // Node is empty, find another
                // Debug.Log("Worker: Node is empty!");
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
            // Debug.Log("Worker: Resource disappeared while gathering!");
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
            // Debug.Log($"Worker: Inventory full ({carryAmount}/{carryCapacity})! Returning to base.");

            // Stop gathering sound
            StopGatheringSound();

            UnregisterFromNode();
            ReturnToBase();
            return;
        }

        // Check if node is empty
        if (!targetResource.HasResources())
        {
            // Debug.Log("Worker: Node depleted while gathering!");

            // Stop gathering sound
            StopGatheringSound();

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
            // Stop gathering sound
            StopGatheringSound();

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

            // Debug.Log($"Worker: Returning to base with {carryAmount:F2} {assignedResourceType}");
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

        // Debug.Log($"Worker: Delivered {amountToDeliver} {assignedResourceType} to base! (carried {carryAmount:F2})");
        carryAmount = 0f;
    }

    void SetupGatheringAudioSource()
    {
        // Add an AudioSource component for 3D spatial gathering sounds
        gatheringAudioSource = gameObject.AddComponent<AudioSource>();

        // Configure for 3D spatial audio
        gatheringAudioSource.spatialBlend = 1.0f;  // Full 3D (0 = 2D, 1 = 3D)
        gatheringAudioSource.playOnAwake = false;
        gatheringAudioSource.loop = false;  // We'll handle looping manually with delays

        // Distance settings (how far you can hear the sound)
        gatheringAudioSource.minDistance = 5f;   // Full volume within 5 units
        gatheringAudioSource.maxDistance = 25f;  // Can't hear beyond 25 units
        gatheringAudioSource.rolloffMode = AudioRolloffMode.Linear;  // Linear falloff

        // Volume settings - will be updated dynamically in Update() based on AudioManager SFX volume
        gatheringAudioSource.volume = 0.15f;  // Base volume (will be multiplied by SFX slider)

        // Doppler effect (slight pitch change when moving)
        gatheringAudioSource.dopplerLevel = 0.1f;  // Subtle doppler
    }

    void StartGatheringSound()
    {
        // Stop any existing sound coroutine
        if (gatheringSoundCoroutine != null)
        {
            StopCoroutine(gatheringSoundCoroutine);
        }

        // Start the delayed looping coroutine
        gatheringSoundCoroutine = StartCoroutine(PlayGatheringSoundLoop());
    }

    void StopGatheringSound()
    {
        // Stop the looping coroutine
        if (gatheringSoundCoroutine != null)
        {
            StopCoroutine(gatheringSoundCoroutine);
            gatheringSoundCoroutine = null;
        }

        // Stop any currently playing sound IMMEDIATELY (no fade)
        if (gatheringAudioSource != null)
        {
            gatheringAudioSource.Stop();
            gatheringAudioSource.clip = null;  // Clear the clip to ensure it stops
        }
    }

    System.Collections.IEnumerator PlayGatheringSoundLoop()
    {
        while (true)
        {
            // CRITICAL: Only play sound if we're still in Gathering state
            if (currentState != WorkerState.Gathering)
            {
                // Worker left gathering state, stop the loop
                yield break;
            }

            // Get the appropriate sound clip from AudioManager
            AudioClip clipToPlay = GetGatheringClip();

            if (clipToPlay != null && gatheringAudioSource != null)
            {
                gatheringAudioSource.clip = clipToPlay;
                gatheringAudioSource.Play();

                // Wait for clip to finish
                yield return new WaitForSeconds(clipToPlay.length);

                // Check again after clip finishes (worker might have stopped gathering)
                if (currentState != WorkerState.Gathering)
                {
                    yield break;
                }

                // Add delay between loops (1-2 seconds)
                yield return new WaitForSeconds(Random.Range(1f, 2f));
            }
            else
            {
                // No clip available, wait and try again
                yield return new WaitForSeconds(1f);
            }
        }
    }

    AudioClip GetGatheringClip()
    {
        if (AudioManager.Instance == null) return null;

        switch (assignedResourceType)
        {
            case ResourceNode.ResourceType.Wood:
                return AudioManager.Instance.gatherWoodSound;
            case ResourceNode.ResourceType.Food:
                return AudioManager.Instance.gatherFoodSound;
            case ResourceNode.ResourceType.Stone:
                return AudioManager.Instance.gatherStoneSound;
            default:
                return null;
        }
    }

    void OnDestroy()
    {
        // Stop any gathering sound
        StopGatheringSound();

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

using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Worker : MonoBehaviour
{
    // Static registry for O(1) lookup instead of FindObjectsByType
    private static readonly List<Worker> activeList = new List<Worker>();
    public static IReadOnlyList<Worker> ActiveList => activeList;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeList.Clear(); }

    void Awake() { activeList.Add(this); }

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
    private float lastRemainingDistance;      // Track progress toward goal, not just raw movement
    private float stuckTimer = 0f;
    private float stuckCheckInterval = 2f;   // Check every 2 seconds for faster detection
    private float minMoveDistance = 0.5f;    // Must move at least this far to not be stuck
    private int consecutiveStuckCount = 0;   // How many times stuck in a row
    private int maxStuckAttempts = 3;        // Give up after 3 attempts
    private bool stuckCheckReady = false;    // Skip first stuck check to establish baseline

    // Unstuck behavior
    private bool isUnsticking = false;       // Currently moving to unstuck position
    private float unstickTimer = 0f;         // Timeout for unsticking attempts
    private float maxUnstickTime = 4f;       // Max seconds to spend trying to unstick
    private WorkerState stateBeforeUnstuck;  // State to return to after unsticking
    private ResourceNode targetBeforeUnstuck; // Target to resume after unsticking

    // Thinking delay - brief pause before picking next task
    private float thinkTimer = 0f;
    private float thinkDuration = 0f;  // Randomized each time
    private bool isThinking = false;

    // Phase-through (face-to-face stuck resolution)
    private float faceToFaceTimer = 0f;
    private float phaseThreshold = 2f;
    private bool isPhasing = false;
    private float phaseActiveTimer = 0f;     // How long phasing has been active
    private float phaseMinDuration = 3f;     // Minimum time to stay phased (prevents oscillation)
    private float savedRadius;
    private ObstacleAvoidanceType savedAvoidance;

    // Visual state display
    private GameObject stateTextObject;
    private TextMeshPro stateText;
    private Camera mainCamera;
    private WorkerState lastDisplayedState = (WorkerState)(-1);
    private float lastDisplayedCarry = -1f;
    private bool lastDisplayedThinking = false;

    // Audio - 3D Spatial Sound
    private AudioSource gatheringAudioSource;
    private Coroutine gatheringSoundCoroutine;

    // Smooth gate traversal
    private bool isTraversingLink = false;
    private Vector3 linkEndPos;

    // Pooled NavMeshPath to avoid GC allocations
    private NavMeshPath reusablePath;

    // Resource re-evaluation during movement
    private float resourceRecheckTimer = 0f;
    private float resourceRecheckInterval = 4f;

    void Start()
    {
        reusablePath = new NavMeshPath();
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
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;  // Reduced from High for performance with many walls
            agent.avoidancePriority = 50;  // Medium priority (0-99, lower = higher priority)
            agent.radius = 0.5f;  // Reduced from 0.6 to match warriors/enemies, less pathfinding complexity
            agent.autoTraverseOffMeshLink = false;  // Manual traversal for smooth gate walking
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
        lastRemainingDistance = 0f;
        stuckCheckReady = false;
        // Debug.Log($"Worker: Initialized and ready to work on {assignedResourceType}");
    }

    void Update()
    {
        // Don't do anything until initialized
        if (!isInitialized) return;

        // Smooth gate traversal — handle OffMeshLink before all other logic
        if (agent != null && agent.isOnOffMeshLink)
        {
            HandleOffMeshLinkTraversal();
            return;
        }

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

        // Phase-through check for moving states
        if (currentState == WorkerState.MovingToResource || currentState == WorkerState.ReturningToBase)
        {
            CheckFaceToFaceStuck();
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
                // Brief thinking pause before picking next task
                if (isThinking)
                {
                    thinkTimer -= Time.deltaTime;
                    if (thinkTimer > 0f) break;  // Still thinking
                    isThinking = false;
                }

                // Only search once per idle state
                if (!isSearchingForResource)
                {
                    isSearchingForResource = true;
                    // Stagger resource search with random delay so not all workers search on same frame
                    Invoke(nameof(FindNearestResource), Random.Range(0f, 0.3f));
                }
                break;

            case WorkerState.MovingToResource:
                CheckResourceRecheck();
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

    /// <summary>
    /// Smoothly walk through gate OffMeshLinks instead of warping.
    /// </summary>
    void HandleOffMeshLinkTraversal()
    {
        OffMeshLinkData data = agent.currentOffMeshLinkData;

        if (!isTraversingLink)
        {
            // First frame on link — determine walk direction
            isTraversingLink = true;
            Vector3 start = data.startPos;
            Vector3 end = data.endPos;

            // Walk toward whichever endpoint is farther from us (we're near the start)
            if (Vector3.Distance(transform.position, end) < Vector3.Distance(transform.position, start))
            {
                linkEndPos = start;
            }
            else
            {
                linkEndPos = end;
            }
        }

        // Walk toward link end at normal agent speed
        transform.position = Vector3.MoveTowards(transform.position, linkEndPos, agent.speed * Time.deltaTime);

        // Smooth rotation toward movement direction
        Vector3 dir = (linkEndPos - transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
        }

        // Complete when close enough
        if (Vector3.Distance(transform.position, linkEndPos) < 0.1f)
        {
            agent.CompleteOffMeshLink();
            isTraversingLink = false;
        }
    }

    /// <summary>
    /// Phase-through system: after 2s stuck at low velocity, temporarily shrink agent
    /// radius and disable avoidance so workers can pass through each other.
    /// Enforces a minimum phase duration to prevent oscillation.
    /// </summary>
    void CheckFaceToFaceStuck()
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;
        if (isUnsticking) return;

        bool tryingToMove = agent.remainingDistance > agent.stoppingDistance + 1f;
        bool movingSlowly = agent.velocity.magnitude < 0.3f;

        if (tryingToMove && movingSlowly && !isPhasing)
        {
            faceToFaceTimer += Time.deltaTime;
            if (faceToFaceTimer >= phaseThreshold)
            {
                // Enable phase-through
                savedRadius = agent.radius;
                savedAvoidance = agent.obstacleAvoidanceType;
                agent.radius = 0.1f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                isPhasing = true;
                phaseActiveTimer = 0f;
            }
        }
        else if (!isPhasing)
        {
            // Reset the trigger timer when not stuck (but only when not phasing)
            faceToFaceTimer = 0f;
        }

        // While phasing, track how long we've been phased
        if (isPhasing)
        {
            phaseActiveTimer += Time.deltaTime;

            // Only restore after minimum duration AND moving well
            if (phaseActiveTimer >= phaseMinDuration && agent.velocity.magnitude > 0.5f)
            {
                agent.radius = savedRadius;
                agent.obstacleAvoidanceType = savedAvoidance;
                faceToFaceTimer = 0f;
                phaseActiveTimer = 0f;
                isPhasing = false;
            }
        }
    }

    void CheckIfReachedUnstuckPosition()
    {
        unstickTimer += Time.deltaTime;

        bool reached = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
        bool timedOut = unstickTimer >= maxUnstickTime;
        bool pathInvalid = agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid;

        if (reached || timedOut || pathInvalid)
        {
            isUnsticking = false;
            unstickTimer = 0f;

            if (timedOut || pathInvalid)
            {
                // Unstick attempt failed — stop and reset cleanly
                agent.ResetPath();
            }

            // Resume previous state
            currentState = stateBeforeUnstuck;
            targetResource = targetBeforeUnstuck;

            // Reset path for clean resume
            agent.ResetPath();

            // Resume based on what we were doing
            if (currentState == WorkerState.MovingToResource && targetResource != null)
            {
                agent.SetDestination(targetResource.GetGatherPoint(transform.position));
                lastRemainingDistance = agent.remainingDistance;
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

            // Reset stuck tracking for the resumed path
            lastPosition = transform.position;
            stuckTimer = 0f;
        }
    }

    void CheckIfStuck()
    {
        // Skip stuck detection if we're already unsticking
        if (isUnsticking) return;
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;

        // Immediate check: if path is invalid, reset right away
        if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning("Worker: Path is invalid, resetting to idle");
            HandleFullReset();
            return;
        }

        stuckTimer += Time.deltaTime;

        // Check every interval if we've made progress
        if (stuckTimer >= stuckCheckInterval)
        {
            // Skip first check interval to establish a baseline position/distance
            if (!stuckCheckReady)
            {
                stuckCheckReady = true;
                lastPosition = transform.position;
                lastRemainingDistance = agent.remainingDistance;
                stuckTimer = 0f;
                return;
            }

            float distanceMoved = Vector3.Distance(transform.position, lastPosition);

            // Check progress toward destination (remaining distance should decrease)
            float currentRemaining = agent.remainingDistance;
            float progressMade = lastRemainingDistance - currentRemaining;

            // Worker is stuck if it hasn't moved much AND hasn't made progress toward goal
            // This catches oscillating workers that move side-to-side without making real progress
            bool barelyMoved = distanceMoved < minMoveDistance;
            bool noProgress = progressMade < 0.3f;
            bool isStuck = barelyMoved && noProgress;

            // Also stuck if velocity is near zero while having a valid destination
            bool velocityStuck = agent.velocity.magnitude < 0.3f
                && agent.remainingDistance > agent.stoppingDistance + 1f
                && !agent.pathPending;

            if (isStuck || velocityStuck)
            {
                consecutiveStuckCount++;

                if (consecutiveStuckCount >= maxStuckAttempts)
                {
                    HandleFullReset();
                }
                else
                {
                    // Try to unstuck by moving to nearby random position
                    AttemptUnstuck();
                }
            }
            else
            {
                // Worker made progress — decay stuck counter instead of full reset
                consecutiveStuckCount = Mathf.Max(0, consecutiveStuckCount - 1);
            }

            // Reset for next check
            lastPosition = transform.position;
            lastRemainingDistance = agent.remainingDistance;
            stuckTimer = 0f;
        }
    }

    /// <summary>
    /// Full reset: clean up everything and return worker to idle to find a new task.
    /// </summary>
    void HandleFullReset()
    {
        Debug.LogWarning($"Worker: Stuck after {consecutiveStuckCount} attempts. Resetting to find new target...");

        // Clean up current task
        UnregisterFromNode();
        targetResource = null;

        // Reset agent
        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = false;
        }

        // Reset stuck detection
        isUnsticking = false;
        unstickTimer = 0f;
        consecutiveStuckCount = 0;
        stuckTimer = 0f;
        lastPosition = transform.position;
        lastRemainingDistance = 0f;
        stuckCheckReady = false;

        // Restore phase-through if active
        if (isPhasing)
        {
            agent.radius = savedRadius;
            agent.obstacleAvoidanceType = savedAvoidance;
            isPhasing = false;
            phaseActiveTimer = 0f;
            faceToFaceTimer = 0f;
        }

        // Return to idle to find new resource
        currentState = WorkerState.Idle;
        isSearchingForResource = false;
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

    /// <summary>
    /// Transition to Idle with a brief thinking pause before picking the next task.
    /// </summary>
    void GoIdleAndThink()
    {
        currentState = WorkerState.Idle;
        isSearchingForResource = false;
        isThinking = true;
        thinkDuration = Random.Range(0.2f, 0.6f);
        thinkTimer = thinkDuration;
        agent.ResetPath();
    }

    /// <summary>
    /// Sum the distance between path corners to get actual walking distance.
    /// Returns float.MaxValue if path is not complete.
    /// </summary>
    float GetPathDistance(NavMeshPath path)
    {
        if (path.status != NavMeshPathStatus.PathComplete) return float.MaxValue;
        float dist = 0f;
        Vector3[] corners = path.corners;
        for (int i = 1; i < corners.Length; i++)
            dist += Vector3.Distance(corners[i - 1], corners[i]);
        return dist;
    }

    void FindNearestResource()
    {
        // Find top 3 closest resources by Euclidean distance (cheap filter), then pick shortest path
        ResourceNode best1 = null, best2 = null, best3 = null;
        float dist1 = searchRadius, dist2 = searchRadius, dist3 = searchRadius;

        for (int i = 0; i < ResourceNode.ActiveList.Count; i++)
        {
            ResourceNode resource = ResourceNode.ActiveList[i];
            if (resource == null) continue;
            if (resource.resourceType != assignedResourceType) continue;

            float distance = Vector3.Distance(transform.position, resource.transform.position);

            if (distance < dist1)
            {
                best3 = best2; dist3 = dist2;
                best2 = best1; dist2 = dist1;
                best1 = resource; dist1 = distance;
            }
            else if (distance < dist2)
            {
                best3 = best2; dist3 = dist2;
                best2 = resource; dist2 = distance;
            }
            else if (distance < dist3)
            {
                best3 = resource; dist3 = distance;
            }
        }

        // Evaluate path distances for candidates
        ResourceNode closestResource = null;
        float closestPathDist = float.MaxValue;
        ResourceNode euclideanFallback = best1;  // fallback if no complete paths

        ResourceNode[] candidates = { best1, best2, best3 };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] == null) continue;
            NavMesh.CalculatePath(transform.position, candidates[i].GetGatherPoint(transform.position), NavMesh.AllAreas, reusablePath);
            float pathDist = GetPathDistance(reusablePath);
            if (pathDist < closestPathDist)
            {
                closestPathDist = pathDist;
                closestResource = candidates[i];
            }
        }

        // Fall back to Euclidean closest if no complete path found
        if (closestResource == null) closestResource = euclideanFallback;

        if (closestResource != null)
        {
            // Found a resource! Go to it
            targetResource = closestResource;

            // Make sure agent is ready before setting destination
            if (agent.isOnNavMesh && agent.enabled)
            {
                agent.SetDestination(targetResource.GetGatherPoint(transform.position));
                currentState = WorkerState.MovingToResource;
                isSearchingForResource = false;  // Reset flag

                // Reset stuck detection for new path
                lastPosition = transform.position;
                lastRemainingDistance = 0f;
                stuckCheckReady = false;
                stuckTimer = 0f;
                consecutiveStuckCount = 0;

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

    /// <summary>
    /// Periodically re-evaluate resource target during movement.
    /// If a significantly shorter path to a same-type resource exists, switch to it.
    /// </summary>
    void CheckResourceRecheck()
    {
        resourceRecheckTimer += Time.deltaTime;
        if (resourceRecheckTimer < resourceRecheckInterval) return;
        resourceRecheckTimer = 0f;

        if (targetResource == null || agent == null || !agent.isOnNavMesh) return;
        if (!agent.hasPath || agent.pathPending) return;

        float currentRemaining = agent.remainingDistance;
        if (currentRemaining <= gatherDistance) return;  // Almost there, don't switch

        // Check nearby same-type resources whose Euclidean distance is less than our remaining path
        for (int i = 0; i < ResourceNode.ActiveList.Count; i++)
        {
            ResourceNode candidate = ResourceNode.ActiveList[i];
            if (candidate == null || candidate == targetResource) continue;
            if (candidate.resourceType != assignedResourceType) continue;

            float euclidean = Vector3.Distance(transform.position, candidate.transform.position);
            if (euclidean >= currentRemaining) continue;  // Can't possibly be shorter

            // Potential shortcut — calculate actual path distance
            NavMesh.CalculatePath(transform.position, candidate.GetGatherPoint(transform.position), NavMesh.AllAreas, reusablePath);
            float pathDist = GetPathDistance(reusablePath);

            // Switch only if new path is 20%+ shorter (prevents oscillation)
            if (pathDist < currentRemaining * 0.8f)
            {
                targetResource = candidate;
                agent.SetDestination(targetResource.GetGatherPoint(transform.position));

                // Reset stuck detection for new path
                lastPosition = transform.position;
                lastRemainingDistance = 0f;
                stuckCheckReady = false;
                stuckTimer = 0f;
                consecutiveStuckCount = 0;
                return;
            }
        }
    }

    void CheckIfReachedResource()
    {
        // Check if target still exists
        if (targetResource == null)
        {
            // Debug.Log("Worker: Target resource disappeared!");
            GoIdleAndThink();
            return;
        }

        // Check actual distance to resource — use whichever is closer: node center or gather point
        float distToCenter = Vector3.Distance(transform.position, targetResource.transform.position);
        float distToGatherPt = Vector3.Distance(transform.position, targetResource.GetGatherPoint(transform.position));
        float distanceToResource = Mathf.Min(distToCenter, distToGatherPt);

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
                GoIdleAndThink();
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
            GoIdleAndThink();
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
                GoIdleAndThink();
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
            lastRemainingDistance = 0f;
            stuckCheckReady = false;
            stuckTimer = 0f;
            consecutiveStuckCount = 0;

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

        // Ring at full delivery distance so the point is outside any NavMesh carve zone
        float ringRadius = deliveryDistance;
        Vector3 dropoffPoint = baseBuilding.transform.position + (directionToWorker * ringRadius);
        dropoffPoint.y = baseBuilding.transform.position.y;

        // Validate on NavMesh — the point must be on walkable surface
        NavMeshHit hit;
        if (NavMesh.SamplePosition(dropoffPoint, out hit, 3f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Fallback: try 8 directions at the same radius
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 candidate = baseBuilding.transform.position + dir * ringRadius;
            candidate.y = baseBuilding.transform.position.y;

            if (NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

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

        float distanceToBase = Vector3.Distance(transform.position, baseBuilding.transform.position);

        // Primary: within delivery distance — deliver immediately, no other checks needed
        if (distanceToBase <= deliveryDistance)
        {
            DeliverResources();
            GoIdleAndThink();
            stuckTimer = 0f;
            consecutiveStuckCount = 0;
            return;
        }

        // Secondary: agent path finished and within generous range
        // Handles NavMesh carve edges where agent can't get any closer
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
        {
            if (distanceToBase <= deliveryDistance * 2.0f)
            {
                DeliverResources();
                GoIdleAndThink();
                stuckTimer = 0f;
                consecutiveStuckCount = 0;
                return;
            }
        }

        // Tertiary: moving very slowly near campfire (rubbing against obstacle)
        if (agent.velocity.magnitude < 0.5f && distanceToBase <= deliveryDistance * 1.5f)
        {
            DeliverResources();
            GoIdleAndThink();
            stuckTimer = 0f;
            consecutiveStuckCount = 0;
            return;
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

    IEnumerator PlayGatheringSoundLoop()
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
        activeList.Remove(this);

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

        // Billboard effect - make text face camera (zero allocations)
        if (mainCamera != null)
        {
            stateTextObject.transform.LookAt(mainCamera.transform);
            stateTextObject.transform.Rotate(0, 180, 0);
        }

        // Early-out if nothing changed
        // For Gathering state, only rebuild when carry changes by >= 0.5
        bool carryChanged = (currentState == WorkerState.Gathering || currentState == WorkerState.ReturningToBase)
            && Mathf.Abs(carryAmount - lastDisplayedCarry) >= 0.5f;
        bool stateChanged = currentState != lastDisplayedState;
        bool thinkingChanged = (currentState == WorkerState.Idle) && (isThinking != lastDisplayedThinking);

        if (!stateChanged && !carryChanged && !thinkingChanged) return;

        lastDisplayedState = currentState;
        lastDisplayedCarry = carryAmount;
        lastDisplayedThinking = isThinking;

        // Update text based on current state
        string stateMessage = "";
        switch (currentState)
        {
            case WorkerState.Idle:
                if (isThinking)
                {
                    stateMessage = "Deciding...";
                    stateText.color = new Color(0.8f, 0.8f, 0.6f);
                }
                else
                {
                    stateMessage = "Searching...";
                    stateText.color = Color.gray;
                }
                break;

            case WorkerState.MovingToResource:
                stateMessage = "Moving to " + assignedResourceType;
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

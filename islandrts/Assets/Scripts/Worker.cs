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

    [Header("AI Mode")]
    public bool useUtilityAI = true;  // Toggle between old state machine and new Utility AI

    [Header("Visual Feedback")]
    public bool showStateText = true;
    public float textHeightOffset = 2f;  // How high above worker to show text

    // Utility AI components (populated when useUtilityAI = true)
    private AIBrain aiBrain;

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
    private float stuckCheckInterval = 3f;   // Check every 3 seconds
    private bool wasStuckLastCheck = false;   // Two consecutive stuck checks = reset

    // Cached gather point - prevents per-frame recalculation
    private Vector3 cachedGatherPoint;
    private bool hasValidGatherPoint = false;

    // Path invalidation grace period
    private float pathInvalidTimer = 0f;
    private float pathInvalidGracePeriod = 0.5f;  // Wait before resetting on invalid path

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

    // Frame staggering — spreads expensive checks across frames to prevent synchronized spikes
    private int frameOffset;
    private float lastStaggeredCheckTime; // Bug 7: track real elapsed between staggered checks

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
            agent.acceleration = 5f;  // Moderate acceleration for smoother movement (reduced from 8)
            agent.angularSpeed = 120f;  // Smooth turning (reduced from 180 to prevent jitter)
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;  // Reduced from High for performance with many walls
            agent.avoidancePriority = Random.Range(30, 70);  // Randomized priority to prevent synchronized yielding
            agent.radius = 0.5f;  // Reduced from 0.6 to match warriors/enemies, less pathfinding complexity
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

        // Randomize stuck timer so workers don't all hit their interval on the same frame
        stuckTimer = Random.Range(0f, stuckCheckInterval);

        // Assign frame offset for staggered per-frame checks (spread across 5 frames)
        frameOffset = activeList.IndexOf(this) % 5;
        lastStaggeredCheckTime = Time.time;

        // Initialize Utility AI if enabled
        if (useUtilityAI)
        {
            InitializeUtilityAI();
        }

        // Debug.Log($"Worker: Initialized and ready to work on {assignedResourceType}");
    }

    void InitializeUtilityAI()
    {
        aiBrain = gameObject.AddComponent<AIBrain>();

        // Create blackboard
        var bb = new AIBlackboard();
        bb.transform = transform;
        bb.agent = agent;
        bb.health = GetComponent<Health>();
        bb.baseBuilding = baseBuilding;
        bb.worker = this;
        bb.assignedResourceType = assignedResourceType;
        bb.carryCapacity = carryCapacity;
        bb.gatherDistance = gatherDistance;
        bb.deliveryDistance = deliveryDistance;
        bb.searchRadius = searchRadius;
        bb.gatherRatePerSecond = gatherRatePerSecond;
        bb.carryAmount = carryAmount;

        // Setup StuckResolver
        var stuckResolver = gameObject.AddComponent<StuckResolver>();
        stuckResolver.Initialize(agent, activeList.IndexOf(this));
        stuckResolver.onStuckReset = () =>
        {
            // Release claims and reset
            if (bb.targetResource != null)
            {
                bb.targetResource.UnclaimNode(this);
                if (bb.isRegisteredAtNode)
                {
                    bb.targetResource.UnregisterWorker(this);
                    bb.isRegisteredAtNode = false;
                }
            }
            bb.targetResource = null;
            aiBrain.ForceReeval();
        };
        bb.stuckResolver = stuckResolver;
        bb.brain = aiBrain;

        // First-damage ForceReeval: immediately re-evaluate on first hit
        bool hasBeenDamaged = false;
        if (bb.health != null)
        {
            bb.health.onDamaged.AddListener(() =>
            {
                if (!hasBeenDamaged)
                {
                    hasBeenDamaged = true;
                    aiBrain.ForceReeval();
                }
            });
        }

        // Ally death: re-evaluate when a nearby warrior dies (workers may need to flee)
        Warrior.OnAnyWarriorDied += OnAllyDiedUtilityAI;

        // Enemy death: re-evaluate so fleeing workers can stop fleeing
        Enemy.OnAnyEnemyDied += OnEnemyDiedUtilityAI;

        // Define actions
        var actions = new ActionOption[]
        {
            // Gather Resource
            new ActionOption("Gather", new Consideration[]
            {
                new ResourceAvailability(ResponseCurve.Linear(1f, 0.1f)),  // Need a resource node
                new CrowdPenalty(4f, ResponseCurve.Linear(0.8f, 0.2f)),  // Spread across nodes (4+ workers = max penalty)
                new ResourceCarry(ResponseCurve.InverseLinear(0.8f, 0.2f)),  // Empty inventory preferred
                new TimeOfDay(false, ResponseCurve.Linear(0.6f, 0.4f)),  // Prefer daytime
                new ThreatNearby(5f, ResponseCurve.InverseLinear(0.8f, 0.2f))  // Low threat preferred
            }, new GatherExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Return to Base — compound urgency score:
            //   No pressure: only returns near-full (carry^15: 0.9→0.21, 0.95→0.46, 1.0→1.0)
            //   Enemies nearby: returns early proportional to carry × threat level
            //   Night approaching: returns early proportional to carry × (1-dayProgress)
            //   Empty inventory always scores 0 (nothing to deliver)
            //   Crossover at ~4.6 carry → RoundToInt = 5 (ensures full delivery)
            new ActionOption("Return", new Consideration[]
            {
                new ReturnUrgency(15f, 3f, ResponseCurve.Linear(1f, 0f))
            }, new ReturnToBaseExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Idle at Base
            new ActionOption("Idle", new Consideration[]
            {
                new ResourceAvailability(ResponseCurve.Constant(0.1f))  // Always-low constant
            }, new IdleExecutor(), basePriority: 0.1f, momentumBonus: 0.05f),

            // Flee to Hut (Phase 7)
            new ActionOption("Flee", new Consideration[]
            {
                new TimeOfDay(true, ResponseCurve.Logistic(12f, 0.6f)),  // Night approaching
                new ThreatNearby(3f, ResponseCurve.Linear(0.9f, 0f)),   // Enemies nearby (0 enemies = 0 score, prevents stuck flee)
                new HealthPercent(ResponseCurve.InverseLinear(0.5f, 0.3f))  // Low health
            }, new FleeToHutExecutor(), basePriority: 0.8f, momentumBonus: 0.2f)
        };

        aiBrain.Initialize(actions, bb);
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

        // Utility AI mode: AIBrain drives behavior, we just update visuals
        if (useUtilityAI && aiBrain != null)
        {
            // Sync carry amount from blackboard
            if (aiBrain.blackboard != null)
            {
                carryAmount = aiBrain.blackboard.carryAmount;
            }

            // Update state text for Utility AI
            if (showStateText && stateText != null)
            {
                UpdateStateTextUtilityAI();
            }
            return;
        }

        // --- Original state machine (when useUtilityAI = false) ---

        // Update state text display
        if (showStateText && stateText != null)
        {
            UpdateStateText();
        }

        // Expensive NavMeshAgent checks only on our designated frame (spreads 30 workers across 5 frames)
        if ((Time.frameCount + frameOffset) % 5 == 0)
        {
            if (currentState == WorkerState.MovingToResource || currentState == WorkerState.ReturningToBase)
            {
                // Bug 7: compute real elapsed time since last staggered check
                float elapsed = Time.time - lastStaggeredCheckTime;
                lastStaggeredCheckTime = Time.time;
                CheckFaceToFaceStuck(elapsed);
                CheckIfStuck(elapsed);
            }
        }
        else if (isPhasing)
        {
            // Still track phase duration even on off-frames so we don't hold phase too long
            TrackPhaseTimer();
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
    /// Phase-through system: after 2s stuck at low velocity, temporarily shrink agent
    /// radius and disable avoidance so workers can pass through each other.
    /// Enforces a minimum phase duration to prevent oscillation.
    /// </summary>
    void CheckFaceToFaceStuck(float elapsed)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;

        bool tryingToMove = agent.remainingDistance > agent.stoppingDistance + 1f;
        bool movingSlowly = agent.velocity.magnitude < 0.3f;

        if (tryingToMove && movingSlowly && !isPhasing)
        {
            faceToFaceTimer += elapsed; // Bug 7: real elapsed, not Time.deltaTime
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

        // While phasing, check for exit condition
        if (isPhasing)
        {
            TrackPhaseTimer();
        }
    }

    /// <summary>
    /// Tracks phase-through duration and restores normal agent settings when safe.
    /// Called both on staggered frames (from CheckFaceToFaceStuck) and off-frames
    /// to ensure timely phase exit.
    /// </summary>
    void TrackPhaseTimer()
    {
        if (!isPhasing || agent == null || !agent.isOnNavMesh) return;

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

    void CheckIfStuck(float elapsed)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;

        // Path invalid grace period — allows NavMesh to stabilize after recarving
        if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            pathInvalidTimer += elapsed; // Bug 7: real elapsed, not Time.deltaTime
            if (pathInvalidTimer >= pathInvalidGracePeriod)
            {
#if UNITY_EDITOR
                Debug.LogWarning("Worker: Path invalid for too long, resetting to idle");
#endif
                pathInvalidTimer = 0f;
                HandleFullReset();
                return;
            }
        }
        else
        {
            pathInvalidTimer = 0f;
        }

        stuckTimer += elapsed; // Bug 7: real elapsed

        if (stuckTimer >= stuckCheckInterval)
        {
            float distanceMoved = Vector3.Distance(transform.position, lastPosition);
            bool isStuck = distanceMoved < 0.5f;

            if (isStuck)
            {
                if (wasStuckLastCheck)
                {
                    // Two consecutive stuck checks (6s total) — reset
                    HandleFullReset();
                }
                else
                {
                    wasStuckLastCheck = true;
                }
            }
            else
            {
                wasStuckLastCheck = false;
            }

            lastPosition = transform.position;
            stuckTimer = 0f;
        }
    }

    /// <summary>
    /// Full reset: clean up everything and return worker to idle to find a new task.
    /// </summary>
    void HandleFullReset()
    {
#if UNITY_EDITOR
        Debug.LogWarning("Worker: Stuck for 6s. Resetting to find new target...");
#endif

        // Clean up current task
        if (targetResource != null)
        {
            targetResource.UnclaimNode(this);
        }
        UnregisterFromNode();
        targetResource = null;

        // Clear cached gather point
        hasValidGatherPoint = false;

        // Reset agent
        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = false;
        }

        // Reset stuck detection
        ResetStuckDetection();

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

    /// <summary>
    /// Transition to Idle with a brief thinking pause before picking the next task.
    /// </summary>
    void GoIdleAndThink()
    {
        // Release claim if we had one
        if (targetResource != null)
        {
            targetResource.UnclaimNode(this);
        }

        // Clear cached gather point
        hasValidGatherPoint = false;

        currentState = WorkerState.Idle;
        isSearchingForResource = false;
        isThinking = true;
        thinkDuration = Random.Range(0.2f, 0.6f);
        thinkTimer = thinkDuration;
        agent.ResetPath();
    }

    void ResetStuckDetection()
    {
        lastPosition = transform.position;
        stuckTimer = 0f;
        wasStuckLastCheck = false;
        pathInvalidTimer = 0f;
        lastStaggeredCheckTime = Time.time; // Bug 7: reset elapsed tracking
    }

    void FindNearestResource()
    {
        ResourceNode bestNode = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < ResourceNode.ActiveList.Count; i++)
        {
            ResourceNode node = ResourceNode.ActiveList[i];
            if (node == null) continue;
            if (node.resourceType != assignedResourceType) continue;
            if (!node.HasResources()) continue;

            float distance = Vector3.Distance(transform.position, node.transform.position);
            if (distance > searchRadius) continue;

            // Score = distance + claim penalty (load balancing)
            // Each claimant adds 5 units equivalent distance
            float score = distance + (node.GetClaimCount() * 5f);

            if (score < bestScore)
            {
                bestScore = score;
                bestNode = node;
            }
        }

        if (bestNode != null)
        {
            targetResource = bestNode;
            targetResource.ClaimNode(this);

            if (agent.isOnNavMesh && agent.enabled)
            {
                // Cache the gather point - don't recalculate during movement
                cachedGatherPoint = targetResource.GetGatherPoint(transform.position);
                hasValidGatherPoint = true;

                agent.SetDestination(cachedGatherPoint);
                currentState = WorkerState.MovingToResource;
                isSearchingForResource = false;
                ResetStuckDetection();
            }
            else
            {
                isSearchingForResource = false;
                Invoke(nameof(FindNearestResource), 0.5f);
            }
        }
        else
        {
            isSearchingForResource = false;
            Invoke(nameof(FindNearestResource), 3f);
        }
    }

    void CheckIfReachedResource()
    {
        // Check if target still exists
        if (targetResource == null)
        {
            // Debug.Log("Worker: Target resource disappeared!");
            hasValidGatherPoint = false;
            GoIdleAndThink();
            return;
        }

        // Use cached gather point to avoid per-frame NavMesh queries
        // Fall back to node center if no cached point
        float distToCenter = Vector3.Distance(transform.position, targetResource.transform.position);
        float distToGatherPt = hasValidGatherPoint
            ? Vector3.Distance(transform.position, cachedGatherPoint)
            : distToCenter;
        float distanceToResource = Mathf.Min(distToCenter, distToGatherPt);

        // Arrived when within gather distance
        if (distanceToResource <= gatherDistance)
        {
            // Stop moving
            agent.ResetPath();

            // We've arrived, release the claim and clear cached point
            targetResource.UnclaimNode(this);
            hasValidGatherPoint = false;

            // Register with the resource node
            if (targetResource.RegisterWorker(this))
            {
                isRegisteredAtNode = true;
                currentState = WorkerState.Gathering;

                // Reset stuck detection - we reached our destination
                ResetStuckDetection();

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
            ResetStuckDetection();

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
            DeliverResources();
            currentState = WorkerState.Idle;
            return;
        }

        float distanceToBase = Vector3.Distance(transform.position, baseBuilding.transform.position);

        // Single threshold with path completion fallback
        bool withinRange = distanceToBase <= deliveryDistance;
        bool pathFinishedNearBase = !agent.pathPending
            && agent.remainingDistance <= agent.stoppingDistance + 0.5f
            && distanceToBase <= deliveryDistance * 2f;

        if (withinRange || pathFinishedNearBase)
        {
            DeliverResources();
            GoIdleAndThink();
            ResetStuckDetection();
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

    // --- Utility AI ForceReeval handlers ---

    void OnAllyDiedUtilityAI(Vector3 deathPos)
    {
        if (useUtilityAI && aiBrain != null && transform != null
            && Vector3.Distance(transform.position, deathPos) < 20f)
        {
            aiBrain.ForceReeval();
        }
    }

    void OnEnemyDiedUtilityAI(Vector3 deathPos)
    {
        if (useUtilityAI && aiBrain != null && transform != null
            && Vector3.Distance(transform.position, deathPos) < 30f)
        {
            aiBrain.ForceReeval();
        }
    }

    // --- Public sound methods for Utility AI executors ---
    public void StartGatheringSoundPublic() { StartGatheringSound(); }
    public void StopGatheringSoundPublic() { StopGatheringSound(); }

    // --- Utility AI state text ---
    private string lastUtilityDisplayName = "";

    void UpdateStateTextUtilityAI()
    {
        if (stateText == null || stateTextObject == null) return;

        // Update text position
        stateTextObject.transform.position = transform.position + Vector3.up * textHeightOffset;

        // Billboard effect
        if (mainCamera != null)
        {
            stateTextObject.transform.LookAt(mainCamera.transform);
            stateTextObject.transform.Rotate(0, 180, 0);
        }

        // Get display name from brain
        string displayName = aiBrain.blackboard != null ? aiBrain.blackboard.stateDisplayName : "Thinking";
        if (displayName == null) displayName = "Thinking";

        // Include carry info if carrying
        string fullText;
        if (carryAmount > 0.5f)
        {
            fullText = displayName + "\n(" + carryAmount.ToString("F1") + "/" + carryCapacity.ToString("F0") + ")";
        }
        else
        {
            fullText = displayName;
        }

        // Early-out if nothing changed
        if (fullText == lastUtilityDisplayName) return;
        lastUtilityDisplayName = fullText;

        stateText.text = fullText;

        // Color based on action
        if (displayName.Contains("Collecting") || displayName.Contains("Gathering"))
            stateText.color = Color.green;
        else if (displayName.Contains("Moving"))
            stateText.color = Color.yellow;
        else if (displayName.Contains("Returning"))
            stateText.color = Color.cyan;
        else if (displayName.Contains("Fleeing"))
            stateText.color = Color.red;
        else
            stateText.color = Color.gray;
    }

    void OnDestroy()
    {
        activeList.Remove(this);

        // Unsubscribe from static events to prevent memory leaks
        Warrior.OnAnyWarriorDied -= OnAllyDiedUtilityAI;
        Enemy.OnAnyEnemyDied -= OnEnemyDiedUtilityAI;

        // Stop any gathering sound
        StopGatheringSound();

        // Release claim and unregister from node
        if (targetResource != null)
        {
            targetResource.UnclaimNode(this);
        }
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

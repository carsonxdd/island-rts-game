using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Warrior : MonoBehaviour
{
    // Static registry for O(1) lookup instead of FindObjectsByType
    private static readonly List<Warrior> activeList = new List<Warrior>();
    public static IReadOnlyList<Warrior> ActiveList => activeList;

    // Static event: fires when any warrior dies (with death position for proximity checks)
    public static event System.Action<Vector3> OnAnyWarriorDied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeList.Clear(); OnAnyWarriorDied = null; }

    void Awake() { activeList.Add(this); }

    [Header("Stats")]
    public float maxHealth = 75f;
    public float damage = 15f;
    public float attackRange = 4.5f;  // Increased for better reach
    public float attackCooldown = 1.2f;

    [Header("Movement")]
    public float moveSpeed = 3.5f;  // Faster than enemies, slower than workers
    public float destinationUpdateThreshold = 3.0f;  // Only update destination if target moved this far

    [Header("Behavior")]
    public float searchRadius = 50f;  // How far to search for enemies
    public float patrolRadius = 8f;  // Patrol radius around campfire when idle
    public float patrolWaitTime = 3f;  // Wait at patrol point before moving to next

    [Header("AI Mode")]
    public bool useUtilityAI = true;  // Toggle between old state machine and new Utility AI

    [Header("State Display")]
    public bool showStateText = true;
    public float textHeightOffset = 2.5f;

    [Header("References")]
    public BaseBuilding baseBuilding;  // Reference to campfire

    // Utility AI components
    private AIBrain aiBrain;

    // Private
    private NavMeshAgent agent;
    private Transform target;
    private string targetName = "";
    private float lastAttackTime = 0f;
    private bool hasTarget = false;
    private Health healthComponent;
    public Health CachedHealth => healthComponent;
    private Health cachedTargetHealth;
    private Transform cachedTargetTransform;
    private TextMeshPro stateText;
    private GameObject stateTextObject;
    private string currentState = "Spawning";
    private string lastRenderedState = "";
    private Vector3 idlePosition;
    private Vector3 lastTargetPosition;  // Track last known target position to reduce path updates
    private Vector3 currentPatrolPoint;
    private float patrolWaitTimer = 0f;
    private bool isWaitingAtPatrol = false;
    private bool patrolDestinationSet = false;  // Track if patrol destination already set
    private bool initialPatrolReady = false;  // Staggered start delay

    // Target-switching hysteresis
    private float targetAcquiredTime = 0f;       // When current target was acquired
    private float minTargetLockDuration = 1.0f;  // Don't switch targets within this window
    private float targetSwitchThreshold = 0.7f;  // New target must be 30%+ closer to switch

    // Attack range hysteresis to prevent oscillation at boundary
    private bool isInAttackRange = false;
    private float attackRangeBuffer = 0.5f;  // Exit threshold is attackRange + this buffer

    // Caching timers to avoid FindObjectsByType every frame
    private float enemySearchTimer = 0f;
    private float enemySearchInterval = 0.5f;  // Search for enemies every 0.5s (increased from 0.3 to reduce path spam)
    private float wallCheckTimer = 0f;
    private float wallCheckInterval = 2f;  // Check for walls every 2s
    private bool cachedHasWalls = false;
    private BaseBuilding cachedCampfire;
    private bool campfireCached = false;

    // Phase-through (face-to-face stuck resolution)
    private float faceToFaceTimer = 0f;
    private float phaseThreshold = 2f;
    private bool isPhasing = false;
    private float phaseActiveTimer = 0f;     // How long phasing has been active
    private float phaseMinDuration = 3f;     // Minimum time to stay phased (prevents oscillation)
    private float savedRadius;
    private ObstacleAvoidanceType savedAvoidance;

    // Audio - 3D Spatial Sound
    private AudioSource combatAudioSource;
    private Camera cachedCamera;

    // Pooled building list for patrol point calculation
    private readonly List<(Transform t, float radius)> buildingBuffer = new List<(Transform, float)>();

    // SetDestination throttle — max 3 per frame across all warriors
    private static int setDestFrame = -1;
    private static int setDestCount = 0;

    bool TrySetDestination(Vector3 destination)
    {
        if (Time.frameCount != setDestFrame) { setDestFrame = Time.frameCount; setDestCount = 0; }
        if (setDestCount >= 3) return false;
        setDestCount++;
        agent.SetDestination(destination);
        return true;
    }

    void Start()
    {
        // Get NavMeshAgent component
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError("Warrior: No NavMeshAgent found!");
            return;
        }

        // Configure NavMeshAgent for warrior movement
        agent.speed = moveSpeed;
        agent.acceleration = 5f;  // Moderate acceleration for smoother movement (reduced from 8)
        agent.angularSpeed = 120f;  // Smooth turning (reduced from 180 to prevent jitter)
        agent.stoppingDistance = attackRange - 1.0f;  // Stop before attack range for smoother movement
        agent.autoBraking = true;
        agent.radius = 0.5f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;  // Reduced from High for performance with many walls
        agent.avoidancePriority = Random.Range(30, 70);  // Randomized priority to prevent synchronized yielding
        agent.updateRotation = true;  // Smooth rotation

        Debug.Log($"Warrior: NavMeshAgent configured - Speed: {agent.speed}, Accel: {agent.acceleration}");

        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.onDeath.AddListener(Die);

        // Create floating state text
        if (showStateText)
        {
            CreateStateText();
        }

        cachedCamera = Camera.main;

        // Setup 3D spatial audio for combat sounds
        SetupCombatAudioSource();

        // Store idle position (spawn position)
        idlePosition = transform.position;
        currentPatrolPoint = transform.position;  // Will be set properly after staggered delay

        // Stagger timers so not all warriors search/check on the same frame
        enemySearchTimer = Random.Range(0f, enemySearchInterval);
        wallCheckTimer = Random.Range(0f, wallCheckInterval);

        // Stagger initial patrol start so not all warriors path at the same time
        float startDelay = Random.Range(0.5f, 2f);
        Invoke(nameof(StartInitialPatrol), startDelay);

        Debug.Log($"Warrior: Spawned with {maxHealth} health at {transform.position}");
    }

    void StartInitialPatrol()
    {
        initialPatrolReady = true;
        currentPatrolPoint = GetRandomPatrolPoint();

        // Initialize Utility AI after staggered delay
        if (useUtilityAI)
        {
            InitializeUtilityAI();
        }
    }

    void InitializeUtilityAI()
    {
        aiBrain = gameObject.AddComponent<AIBrain>();

        var bb = new AIBlackboard();
        bb.transform = transform;
        bb.agent = agent;
        bb.health = healthComponent;
        bb.baseBuilding = baseBuilding;
        bb.warrior = this;
        bb.attackRange = attackRange;
        bb.attackCooldown = attackCooldown;
        bb.damage = damage;
        bb.warriorSearchRadius = searchRadius;
        bb.patrolRadius = patrolRadius;

        // Setup StuckResolver
        var stuckResolver = gameObject.AddComponent<StuckResolver>();
        stuckResolver.Initialize(agent, activeList.IndexOf(this));
        stuckResolver.onStuckReset = () =>
        {
            bb.currentTarget = null;
            bb.currentTargetHealth = null;
            bb.isInAttackRange = false;
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = false;
            }
            aiBrain.ForceReeval();
        };
        bb.stuckResolver = stuckResolver;

        // EnemyPresence is evaluated first in every action that needs it,
        // so bb.nearestEnemy is always populated before other considerations read it.
        var enemyScanner = new EnemyPresence(0f, ResponseCurve.Linear(1f, 0f));

        var actions = new ActionOption[]
        {
            // Engage Enemy — enemies are CLOSE (within ~20m), charge in and fight
            // EnemyProximity returns high when enemies are close, low when far
            new ActionOption("Engage", new Consideration[]
            {
                enemyScanner,  // Populates bb.nearestEnemy, returns 1 if any enemy alive
                new EnemyProximity(searchRadius, ResponseCurve.Logistic(8f, 0.4f)),  // Close = high
                new HealthPercent(ResponseCurve.Linear(0.7f, 0.3f))  // Healthy = more willing
            }, new EngageEnemyExecutor(), basePriority: 1.0f, momentumBonus: 0.25f),

            // Intercept — enemies detected but FAR, rally at colony edge as a group
            // EnemyProximity inverted: high when enemies are far, drops as they get close
            new ActionOption("Intercept", new Consideration[]
            {
                enemyScanner,  // Enemies must exist
                new EnemyProximity(searchRadius, ResponseCurve.InverseLinear(0.6f, 0.3f))  // Far = high
            }, new InterceptExecutor(), basePriority: 0.7f, momentumBonus: 0.15f),

            // Defend Wall — walls under attack, rush to defend
            new ActionOption("DefendWall", new Consideration[]
            {
                new WallIntegrity(ResponseCurve.Linear(1f, 0f)),
                new DistanceTo(DistanceTo.TargetType.WallUnderAttack, searchRadius, ResponseCurve.Linear(0.5f, 0.3f))
            }, new DefendWallExecutor(), basePriority: 0.9f, momentumBonus: 0.15f),

            // Patrol — no enemies at all, peacetime
            // InverseLinear on EnemyPresence: 1 when no enemies, 0 when enemies exist
            new ActionOption("Patrol", new Consideration[]
            {
                new EnemyPresence(0f, ResponseCurve.InverseLinear(0.8f, 0.2f))
            }, new PatrolExecutor(), basePriority: 0.3f, momentumBonus: 0.1f),

            // Retreat — low health, outnumbered, fall back to base
            new ActionOption("Retreat", new Consideration[]
            {
                new HealthPercent(ResponseCurve.InverseLinear(1.5f, -0.2f)),
                new ThreatNearby(2f, ResponseCurve.Linear(0.8f, 0.1f))
            }, new RetreatExecutor(), basePriority: 0.7f, momentumBonus: 0.25f)
        };

        bb.brain = aiBrain;
        aiBrain.Initialize(actions, bb);

        // First-damage ForceReeval: immediately re-evaluate on first hit
        bool hasBeenDamaged = false;
        healthComponent.onDamaged.AddListener(() =>
        {
            if (!hasBeenDamaged)
            {
                hasBeenDamaged = true;
                aiBrain.ForceReeval();
            }
        });

        // Ally death: re-evaluate when a nearby warrior dies
        OnAnyWarriorDied += OnAllyDiedUtilityAI;

        // Wall/gate break: re-evaluate when walls are destroyed
        Wall.OnAnyWallDestroyed += OnWallDestroyedUtilityAI;
        Gate.OnAnyGateDestroyed += OnWallDestroyedUtilityAI;
    }

    void Update()
    {
        // Utility AI mode: AIBrain drives behavior, we just update visuals
        if (useUtilityAI && aiBrain != null)
        {
            if (showStateText && stateText != null)
            {
                UpdateStateTextUtilityAI();
            }
            return;
        }

        // --- Original state machine (when useUtilityAI = false) ---

        // Update state text
        if (showStateText && stateText != null)
        {
            UpdateStateText();
        }

        // Wait for staggered start
        if (!initialPatrolReady && !hasTarget)
        {
            currentState = "Initializing...";
            return;
        }

        // Phase-through check when moving (patrol or combat)
        if (agent != null && agent.isOnNavMesh && agent.enabled && agent.hasPath)
        {
            CheckFaceToFaceStuck();
        }

        // Search for enemies on a timer (not every frame)
        enemySearchTimer -= Time.deltaTime;
        if (!hasTarget || target == null)
        {
            if (enemySearchTimer <= 0f)
            {
                enemySearchTimer = enemySearchInterval;
                FindNearestEnemy();
            }

            if (!hasTarget)
            {
                // No enemies - patrol (prefer wall positions if walls exist)
                // Use cached wall check to avoid FindObjectsByType every frame
                wallCheckTimer -= Time.deltaTime;
                if (wallCheckTimer <= 0f)
                {
                    wallCheckTimer = wallCheckInterval;
                    cachedHasWalls = WallGrid.Instance != null && HasRegisteredWalls();
                }
                currentState = cachedHasWalls ? "Guarding Walls" : "Patrolling";
                PatrolBehavior();
                return;
            }
            else
            {
                // Found a new target - but verify it still exists (could have been destroyed between find and use)
                if (target == null)
                {
                    hasTarget = false;
                    return;
                }
                // Reset last position and resume movement
                lastTargetPosition = target.position;
                agent.isStopped = false;  // Resume movement toward new target
                TrySetDestination(target.position);  // Throttled to prevent frame spikes
                patrolDestinationSet = false;  // Reset so patrol can set destination when returning
            }
        }

        // Check if target is still alive (use cached Health to avoid GetComponent every frame)
        if (cachedTargetTransform != target)
        {
            cachedTargetHealth = target.GetComponent<Health>();
            cachedTargetTransform = target;
        }
        if (cachedTargetHealth != null && !cachedTargetHealth.IsAlive)
        {
            // Target is dead, clear target and resume movement
            currentState = "Enemy defeated! Searching...";
            hasTarget = false;
            target = null;
            cachedTargetHealth = null;
            cachedTargetTransform = null;
            isInAttackRange = false;  // Reset attack range state
            agent.isStopped = false;
            agent.ResetPath();
            patrolDestinationSet = false;
            // Stagger search to prevent all warriors pathing simultaneously
            enemySearchTimer = Random.Range(0f, enemySearchInterval * 0.5f);
            return;
        }

        // Move toward target
        MoveTowardTarget();

        // Check if in attack range (with hysteresis to prevent oscillation)
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // Hysteresis: use different thresholds for entering vs exiting attack range
        // This prevents stuttering when at the boundary
        if (isInAttackRange)
        {
            // Already attacking - only exit if moved beyond buffer
            if (distanceToTarget > attackRange + attackRangeBuffer)
            {
                isInAttackRange = false;
            }
        }
        else
        {
            // Not attacking - enter attack range at normal threshold
            if (distanceToTarget <= attackRange)
            {
                isInAttackRange = true;
            }
        }

        if (isInAttackRange)
        {
            // In range - stop and attack
            agent.isStopped = true;

            // Face the target
            Vector3 lookDirection = (target.position - transform.position).normalized;
            lookDirection.y = 0;  // Keep on horizontal plane
            if (lookDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDirection), Time.deltaTime * 5f);
            }

            if (!currentState.StartsWith("Attacking"))
                currentState = "Attacking " + targetName + "!";
            AttemptAttack();
        }
        else
        {
            // Out of range - move closer
            agent.isStopped = false;
            if (!currentState.StartsWith("Engaging"))
                currentState = "Engaging " + targetName;
        }
    }

    /// <summary>
    /// Phase-through system: after 2s stuck at low velocity, temporarily shrink agent
    /// radius and disable avoidance so warriors can pass through each other.
    /// Enforces a minimum phase duration to prevent oscillation.
    /// </summary>
    void CheckFaceToFaceStuck()
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;

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

    void FindNearestEnemy()
    {
        if (Enemy.ActiveList.Count == 0)
        {
            hasTarget = false;
            return;
        }

        Transform nearestEnemy = null;
        float nearestDistance = searchRadius;

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;

            // Check if enemy is alive (use CachedHealth to avoid GetComponent)
            Health enemyHealth = enemy.CachedHealth;
            if (enemyHealth != null && !enemyHealth.IsAlive)
                continue;

            float distance = Vector3.Distance(transform.position, enemy.transform.position);

            // Enemies attacking walls get a distance bonus (treated as closer)
            // This makes warriors rush to defend walls under attack
            if (IsEnemyAttackingWall(enemy))
            {
                distance *= 0.5f; // Halve perceived distance
            }

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestEnemy = enemy.transform;
            }
        }

        if (nearestEnemy != null)
        {
            // Hysteresis: if we already have a valid living target, don't switch too eagerly
            if (hasTarget && target != null)
            {
                // Use cached target health to avoid GetComponent
                bool currentAlive = cachedTargetHealth != null && cachedTargetHealth.IsAlive;

                if (currentAlive)
                {
                    // Don't switch if we've held this target for less than minTargetLockDuration
                    if (Time.time - targetAcquiredTime < minTargetLockDuration)
                        return;

                    // Don't switch unless new target is significantly closer (30%+ closer)
                    float currentDist = Vector3.Distance(transform.position, target.position);
                    if (nearestDistance > currentDist * targetSwitchThreshold)
                        return;
                }
            }

            target = nearestEnemy;
            targetName = nearestEnemy.gameObject.name;
            hasTarget = true;
            targetAcquiredTime = Time.time;
        }
        else
        {
            hasTarget = false;
        }
    }

    /// <summary>
    /// Check if an enemy is currently targeting a wall or gate.
    /// Uses reflection-free approach by checking the enemy's NavMeshAgent destination
    /// against wall positions in the grid.
    /// </summary>
    // Cached NavMeshAgent references for enemy wall-attack checks
    private readonly Dictionary<Enemy, NavMeshAgent> enemyAgentCache = new Dictionary<Enemy, NavMeshAgent>();
    private float enemyAgentCacheCleanTimer = 0f;

    bool IsEnemyAttackingWall(Enemy enemy)
    {
        // Clear entire cache every 10 seconds to prevent unbounded growth from dead enemies
        enemyAgentCacheCleanTimer += Time.deltaTime;
        if (enemyAgentCacheCleanTimer >= 10f)
        {
            enemyAgentCacheCleanTimer = 0f;
            enemyAgentCache.Clear();
        }
        // Check the enemy's NavMeshAgent destination against wall grid (cached lookup)
        NavMeshAgent enemyAgent;
        if (!enemyAgentCache.TryGetValue(enemy, out enemyAgent))
        {
            enemyAgent = enemy.GetComponent<NavMeshAgent>();
            enemyAgentCache[enemy] = enemyAgent;
        }
        if (enemyAgent == null || !enemyAgent.hasPath) return false;

        // Check if enemy destination is near a wall
        if (WallGrid.Instance != null)
        {
            Vector2Int destGrid = WallGrid.Instance.WorldToGrid(enemyAgent.destination);
            return WallGrid.Instance.HasWallAt(destGrid);
        }

        return false;
    }

    void MoveTowardTarget()
    {
        if (agent != null && target != null)
        {
            // Don't interrupt path calculation - this causes stuttering!
            if (agent.pathPending) return;

            // Only update destination if target has moved significantly OR if path is invalid/missing
            float distanceMoved = Vector3.Distance(target.position, lastTargetPosition);
            bool needsNewPath = !agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid;

            if (distanceMoved > destinationUpdateThreshold || needsNewPath)
            {
                if (TrySetDestination(target.position))
                {
                    lastTargetPosition = target.position;
                }
            }
        }
    }

    void PatrolBehavior()
    {
        // Patrol around the campfire in a circular pattern
        float distanceToPatrolPoint = Vector3.Distance(transform.position, currentPatrolPoint);

        if (isWaitingAtPatrol)
        {
            // Waiting at patrol point
            agent.isStopped = true;
            patrolWaitTimer += Time.deltaTime;

            if (patrolWaitTimer >= patrolWaitTime)
            {
                // Done waiting, pick new patrol point
                currentPatrolPoint = GetRandomPatrolPoint();
                isWaitingAtPatrol = false;
                patrolDestinationSet = false;
                patrolWaitTimer = 0f;
            }
        }
        else if (distanceToPatrolPoint < 1.5f)
        {
            // Reached patrol point, start waiting
            isWaitingAtPatrol = true;
            patrolDestinationSet = false;
            patrolWaitTimer = 0f;
        }
        else
        {
            // Moving to patrol point - only set destination ONCE
            agent.isStopped = false;
            if (!patrolDestinationSet)
            {
                if (TrySetDestination(currentPatrolPoint))
                {
                    patrolDestinationSet = true;
                }
            }
        }
    }

    Vector3 GetRandomPatrolPoint()
    {
        // Prefer patrolling near walls if they exist
        if (HasRegisteredWalls())
        {
            Vector3 wallPatrolPoint;
            if (TryGetWallPatrolPoint(out wallPatrolPoint))
            {
                return wallPatrolPoint;
            }
        }

        // Fallback: patrol around building perimeters
        return GetBuildingPerimeterPatrolPoint();
    }

    /// <summary>
    /// Check if there are any walls registered in the WallGrid.
    /// </summary>
    bool HasRegisteredWalls()
    {
        if (WallGrid.Instance == null) return false;
        return Wall.ActiveList.Count > 0 || Gate.ActiveList.Count > 0;
    }

    /// <summary>
    /// Pick a random wall position and generate a patrol point near it (offset 1-2 units interior side).
    /// </summary>
    bool TryGetWallPatrolPoint(out Vector3 point)
    {
        point = Vector3.zero;

        int wallCount = Wall.ActiveList.Count;
        int gateCount = Gate.ActiveList.Count;
        int totalCount = wallCount + gateCount;
        if (totalCount == 0) return false;

        // Cache campfire reference
        if (!campfireCached)
        {
            cachedCampfire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
            campfireCached = true;
        }

        for (int attempts = 0; attempts < 10; attempts++)
        {
            // Pick a random wall or gate
            Vector3 wallPos;
            int index = Random.Range(0, totalCount);
            if (index < wallCount)
            {
                if (Wall.ActiveList[index] == null) continue;
                wallPos = Wall.ActiveList[index].transform.position;
            }
            else
            {
                int gateIndex = index - wallCount;
                if (Gate.ActiveList[gateIndex] == null) continue;
                wallPos = Gate.ActiveList[gateIndex].transform.position;
            }

            // Offset 1-2 units toward the base interior (toward campfire)
            Vector3 interiorDir = Vector3.zero;
            if (cachedCampfire != null)
            {
                interiorDir = (cachedCampfire.transform.position - wallPos).normalized;
            }
            else
            {
                // Random direction if no campfire
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                interiorDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            float offset = Random.Range(1f, 2f);
            Vector3 patrolPos = wallPos + interiorDir * offset;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(patrolPos, out hit, 3f, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Original building-perimeter patrol logic (fallback when no walls exist).
    /// </summary>
    Vector3 GetBuildingPerimeterPatrolPoint()
    {
        // Build list of all buildings with their no-build radii (reuse pooled list)
        buildingBuffer.Clear();

        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            var campfire = BaseBuilding.ActiveList[i];
            if (campfire != null)
                buildingBuffer.Add((campfire.transform, campfire.noBuildRadius));
        }

        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            var hut = Hut.ActiveList[i];
            if (hut != null)
                buildingBuffer.Add((hut.transform, hut.noBuildRadius));
        }

        // If we have buildings, patrol their outer perimeter
        if (buildingBuffer.Count > 0)
        {
            for (int attempts = 0; attempts < 20; attempts++)
            {
                // Pick a random building
                var randomBuilding = buildingBuffer[Random.Range(0, buildingBuffer.Count)];
                Transform buildingTransform = randomBuilding.t;
                float noBuildRadius = randomBuilding.radius;

                // Generate random point on the building's no-build perimeter
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 perimeterPoint = buildingTransform.position + new Vector3(
                    Mathf.Cos(angle) * noBuildRadius,
                    0f,
                    Mathf.Sin(angle) * noBuildRadius
                );

                // Check if this point is OUTSIDE all other buildings' no-build zones (outer perimeter only)
                bool isOuterPerimeter = true;
                foreach (var otherBuilding in buildingBuffer)
                {
                    if (otherBuilding.t == buildingTransform) continue; // Skip same building

                    float distanceToOther = Vector3.Distance(perimeterPoint, otherBuilding.t.position);
                    if (distanceToOther < otherBuilding.radius)
                    {
                        // This point is inside another building's no-build zone - not outer perimeter
                        isOuterPerimeter = false;
                        break;
                    }
                }

                if (!isOuterPerimeter) continue; // Try another point

                // Validate it's on the NavMesh
                NavMeshHit hit;
                if (NavMesh.SamplePosition(perimeterPoint, out hit, 3f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }
        }

        // Fallback: patrol around spawn position if no valid outer perimeter found
        for (int attempts = 0; attempts < 5; attempts++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(patrolRadius * 0.5f, patrolRadius);

            Vector3 randomPoint = idlePosition + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 2f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        // Last resort: stay at current position
        return transform.position;
    }

    void AttemptAttack()
    {
        // Check cooldown
        if (Time.time - lastAttackTime < attackCooldown)
        {
            return;
        }

        lastAttackTime = Time.time;

        // Check for watchtower damage buff
        float towerMultiplier = Watchtower.GetDamageMultiplier(transform.position);
        float finalDamage = damage * towerMultiplier;
        bool hasTowerBuff = towerMultiplier > 1f;

        // Update state text with buff indicator
        if (hasTowerBuff)
        {
            currentState = "Attacking " + targetName + "! (Tower Buff)";
        }

        // Attack the target
#if UNITY_EDITOR
        Debug.Log($"Warrior: Attacking {target.name} for {finalDamage:F0} damage!{(hasTowerBuff ? " (Tower Buff)" : "")}");
#endif

        // Spawn attack visual effect
        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(transform.position, target.position, true);
        }

        // Play attack sound (3D spatial audio)
        PlayAttackSound();

        // Apply damage to target's Health component (use cached reference)
        if (cachedTargetTransform != target)
        {
            cachedTargetHealth = target.GetComponent<Health>();
            cachedTargetTransform = target;
        }
        if (cachedTargetHealth != null)
        {
            cachedTargetHealth.TakeDamage(finalDamage);
        }
        else
        {
            Debug.LogWarning($"Warrior: Target {target.name} has no Health component!");
        }
    }

    // Take damage (passes to Health component)
    public void TakeDamage(float damageAmount)
    {
        if (healthComponent != null)
        {
            healthComponent.TakeDamage(damageAmount);
        }
    }

    void Die()
    {
        Debug.Log("Warrior: Defeated!");

        // Fire static death event for nearby allies to react
        OnAnyWarriorDied?.Invoke(transform.position);

        // Play death sound (3D spatial audio)
        PlayDeathSound();

        // Notify base building to update warrior count
        if (baseBuilding != null)
        {
            baseBuilding.NotifyWarriorKilled(gameObject);
        }

        // Health component will handle destruction
    }

    // --- Utility AI ForceReeval handlers ---

    void OnAllyDiedUtilityAI(Vector3 deathPos)
    {
        if (useUtilityAI && aiBrain != null && transform != null
            && Vector3.Distance(transform.position, deathPos) < 25f)
        {
            aiBrain.ForceReeval();
        }
    }

    void OnWallDestroyedUtilityAI()
    {
        if (useUtilityAI && aiBrain != null)
            aiBrain.ForceReeval();
    }

    // --- Public sound methods for Utility AI executors ---
    public void PlayAttackSoundPublic() { PlayAttackSound(); }
    public void PlayDeathSoundPublic() { PlayDeathSound(); }

    // --- Utility AI state text ---
    private string lastUtilityState = "";

    void UpdateStateTextUtilityAI()
    {
        if (stateText == null) return;

        // Billboard effect
        if (cachedCamera != null)
        {
            stateTextObject.transform.LookAt(cachedCamera.transform);
            stateTextObject.transform.Rotate(0, 180, 0);
        }

        string displayName = aiBrain.blackboard != null ? aiBrain.blackboard.stateDisplayName : "Initializing...";
        if (displayName == null) displayName = "Initializing...";

        if (displayName == lastUtilityState) return;
        lastUtilityState = displayName;

        stateText.text = displayName;

        // Color based on action
        if (displayName.Contains("Attacking") && displayName.Contains("Tower Buff"))
            stateText.color = new Color(1f, 0.8f, 0f);
        else if (displayName.Contains("Attacking"))
            stateText.color = Color.red;
        else if (displayName.Contains("Engaging") || displayName.Contains("defeated"))
            stateText.color = Color.yellow;
        else if (displayName.Contains("Defending") || displayName.Contains("Guarding"))
            stateText.color = new Color(0.3f, 0.8f, 1f);
        else if (displayName.Contains("Intercepting"))
            stateText.color = new Color(1f, 0.6f, 0f);  // Orange for intercept/rally
        else if (displayName.Contains("Retreating"))
            stateText.color = new Color(1f, 0.4f, 0.4f);
        else if (displayName.Contains("Patrolling"))
            stateText.color = Color.cyan;
        else
            stateText.color = new Color(0.5f, 0.5f, 1f);
    }

    void OnDestroy()
    {
        activeList.Remove(this);

        // Unsubscribe from static events to prevent memory leaks
        OnAnyWarriorDied -= OnAllyDiedUtilityAI;
        Wall.OnAnyWallDestroyed -= OnWallDestroyedUtilityAI;
        Gate.OnAnyGateDestroyed -= OnWallDestroyedUtilityAI;
    }

    void SetupCombatAudioSource()
    {
        // Add an AudioSource component for 3D spatial combat sounds
        combatAudioSource = gameObject.AddComponent<AudioSource>();

        // Configure for 3D spatial audio
        combatAudioSource.spatialBlend = 1.0f;  // Full 3D (0 = 2D, 1 = 3D)
        combatAudioSource.playOnAwake = false;
        combatAudioSource.loop = false;

        // Distance settings (combat sounds heard from farther away than gathering)
        combatAudioSource.minDistance = 8f;   // Full volume within 8 units
        combatAudioSource.maxDistance = 35f;  // Can't hear beyond 35 units
        combatAudioSource.rolloffMode = AudioRolloffMode.Linear;

        // Volume settings (combat louder than gathering)
        combatAudioSource.volume = 0.5f;

        // No doppler for combat sounds
        combatAudioSource.dopplerLevel = 0f;
    }

    void PlayAttackSound()
    {
        if (combatAudioSource != null && AudioManager.Instance != null && AudioManager.Instance.warriorAttackSound != null)
        {
            combatAudioSource.PlayOneShot(AudioManager.Instance.warriorAttackSound, 0.6f);
        }
    }

    void PlayDeathSound()
    {
        if (combatAudioSource != null && AudioManager.Instance != null && AudioManager.Instance.warriorDeathSound != null)
        {
            combatAudioSource.PlayOneShot(AudioManager.Instance.warriorDeathSound, 1f);
        }
    }

    void CreateStateText()
    {
        // Create a child GameObject for floating text
        stateTextObject = new GameObject("StateText");
        stateTextObject.transform.parent = transform;
        stateTextObject.transform.localPosition = new Vector3(0, textHeightOffset, 0);

        // Add TextMeshPro component
        stateText = stateTextObject.AddComponent<TextMeshPro>();
        stateText.text = currentState;
        stateText.fontSize = 2;
        stateText.alignment = TextAlignmentOptions.Center;
        stateText.color = Color.blue;

        // Make sure text renders on top
        stateText.GetComponent<MeshRenderer>().sortingOrder = 100;

        Debug.Log("Warrior: State text created");
    }

    void UpdateStateText()
    {
        if (stateText == null) return;

        // Billboard effect - always face camera (zero allocations)
        if (cachedCamera != null)
        {
            stateTextObject.transform.LookAt(cachedCamera.transform);
            stateTextObject.transform.Rotate(0, 180, 0);
        }

        // Only update text content when state string changed
        if (currentState == lastRenderedState) return;
        lastRenderedState = currentState;

        stateText.text = currentState;

        // Color based on state
        if (currentState.StartsWith("Attacking") && currentState.Contains("Tower Buff"))
        {
            stateText.color = new Color(1f, 0.8f, 0f);  // Gold for tower-buffed attacks
        }
        else if (currentState.StartsWith("Attacking"))
        {
            stateText.color = Color.red;
        }
        else if (currentState.StartsWith("Engaging") || currentState.Contains("defeated"))
        {
            stateText.color = Color.yellow;
        }
        else if (currentState.StartsWith("Guarding"))
        {
            stateText.color = new Color(0.3f, 0.8f, 1f);  // Light blue-green for wall guard
        }
        else if (currentState.StartsWith("Idle") || currentState.StartsWith("Initializing"))
        {
            stateText.color = new Color(0.5f, 0.5f, 1f);  // Light blue
        }
        else
        {
            stateText.color = Color.cyan;
        }
    }

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        // Draw attack range
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Draw search radius
        Gizmos.color = new Color(0, 0, 1, 0.2f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
}

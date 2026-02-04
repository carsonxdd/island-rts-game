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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeList.Clear(); }

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

    [Header("State Display")]
    public bool showStateText = true;
    public float textHeightOffset = 2.5f;

    [Header("References")]
    public BaseBuilding baseBuilding;  // Reference to campfire

    // Private
    private NavMeshAgent agent;
    private Transform target;
    private string targetName = "";
    private float lastAttackTime = 0f;
    private bool hasTarget = false;
    private Health healthComponent;
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

    // Caching timers to avoid FindObjectsByType every frame
    private float enemySearchTimer = 0f;
    private float enemySearchInterval = 0.3f;  // Search for enemies every 0.3s
    private float wallCheckTimer = 0f;
    private float wallCheckInterval = 2f;  // Check for walls every 2s
    private bool cachedHasWalls = false;
    private BaseBuilding cachedCampfire;
    private bool campfireCached = false;

    // Phase-through (face-to-face stuck resolution)
    private float faceToFaceTimer = 0f;
    private float phaseThreshold = 2f;
    private bool isPhasing = false;
    private float savedRadius;
    private ObstacleAvoidanceType savedAvoidance;

    // Audio - 3D Spatial Sound
    private AudioSource combatAudioSource;

    // Pooled building list for patrol point calculation
    private readonly List<(Transform t, float radius)> buildingBuffer = new List<(Transform, float)>();

    // Smooth gate traversal
    private bool isTraversingLink = false;
    private Vector3 linkEndPos;

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
        agent.acceleration = 8f;
        agent.angularSpeed = 180f;  // Fast turning for combat
        agent.stoppingDistance = attackRange - 1.0f;  // Stop before attack range for smoother movement
        agent.autoBraking = true;
        agent.radius = 0.5f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;  // Reduced from High for performance with many walls
        agent.updateRotation = true;  // Smooth rotation
        agent.autoTraverseOffMeshLink = false;  // Manual traversal for smooth gate walking

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
    }

    void Update()
    {
        // Update state text
        if (showStateText && stateText != null)
        {
            UpdateStateText();
        }

        // Smooth gate traversal — handle OffMeshLink before all other logic
        if (agent != null && agent.isOnOffMeshLink)
        {
            HandleOffMeshLinkTraversal();
            return;
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
                // Found a new target - reset last position and resume movement
                lastTargetPosition = target.position;
                agent.isStopped = false;  // Resume movement toward new target
                agent.SetDestination(target.position);  // CRITICAL: Set path to new target
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
            agent.isStopped = false;
            agent.ResetPath();
            patrolDestinationSet = false;
            enemySearchTimer = 0f;  // Search immediately for next enemy
            return;
        }

        // Move toward target
        MoveTowardTarget();

        // Check if in attack range
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        if (distanceToTarget <= attackRange)
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
                // We're closer to end, swap so we walk start->end correctly
                linkEndPos = start;
            }
            else
            {
                linkEndPos = end;
            }
        }

        // Walk toward link end at normal move speed
        transform.position = Vector3.MoveTowards(transform.position, linkEndPos, moveSpeed * Time.deltaTime);

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
    /// radius and disable avoidance so warriors can pass through each other.
    /// </summary>
    void CheckFaceToFaceStuck()
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;

        bool tryingToMove = agent.remainingDistance > agent.stoppingDistance + 1f;
        bool movingSlowly = agent.velocity.magnitude < 0.3f;

        if (tryingToMove && movingSlowly)
        {
            faceToFaceTimer += Time.deltaTime;
            if (faceToFaceTimer >= phaseThreshold && !isPhasing)
            {
                // Enable phase-through
                savedRadius = agent.radius;
                savedAvoidance = agent.obstacleAvoidanceType;
                agent.radius = 0.1f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                isPhasing = true;
            }
        }

        // Restore when moving normally again
        if (isPhasing && agent.velocity.magnitude > 0.5f)
        {
            agent.radius = savedRadius;
            agent.obstacleAvoidanceType = savedAvoidance;
            faceToFaceTimer = 0f;
            isPhasing = false;
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

            // Check if enemy is alive
            Health enemyHealth = enemy.GetComponent<Health>();
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
                Health currentTargetHealth = target.GetComponent<Health>();
                bool currentAlive = currentTargetHealth != null && currentTargetHealth.IsAlive;

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
    bool IsEnemyAttackingWall(Enemy enemy)
    {
        // Check the enemy's NavMeshAgent destination against wall grid
        NavMeshAgent enemyAgent = enemy.GetComponent<NavMeshAgent>();
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
            // Only update destination if target has moved significantly OR if path is invalid
            // This reduces stuttering when enemy moves slightly
            float distanceMoved = Vector3.Distance(target.position, lastTargetPosition);
            bool needsNewPath = !agent.hasPath || agent.pathPending || agent.pathStatus == NavMeshPathStatus.PathInvalid;

            if (distanceMoved > destinationUpdateThreshold || needsNewPath)
            {
                agent.SetDestination(target.position);
                lastTargetPosition = target.position;
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
                agent.SetDestination(currentPatrolPoint);
                patrolDestinationSet = true;
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

        // Play death sound (3D spatial audio)
        PlayDeathSound();

        // Notify base building to update warrior count
        if (baseBuilding != null)
        {
            baseBuilding.NotifyWarriorKilled(gameObject);
        }

        // Health component will handle destruction
    }

    void OnDestroy()
    {
        activeList.Remove(this);
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
        if (Camera.main != null)
        {
            stateTextObject.transform.LookAt(Camera.main.transform);
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

using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;
public class Enemy : MonoBehaviour
{
    // Static registry for O(1) lookup instead of FindObjectsByType
    private static readonly List<Enemy> activeList = new List<Enemy>();
    public static IReadOnlyList<Enemy> ActiveList => activeList;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeList.Clear(); wallTargetCounts.Clear(); }

    void Awake() { activeList.Add(this); }

    [Header("Stats")]
    public float maxHealth = 50f;
    public float damage = 10f;
    public float attackRange = 3.5f;  // Increased to account for building size
    public float attackCooldown = 1.5f;

    [Header("Movement")]
    public float moveSpeed = 2f;  // Slow, shambling speed for enemies
    public float destinationUpdateThreshold = 1.5f;  // Only update destination if target moved this far

    [Header("Targeting")]
    public float warriorDetectionRange = 15f;  // Only engage warriors within this range
    public float buildingEngagementRange = 20f;  // Only engage buildings within this range
    public float retargetInterval = 1.5f;  // Re-evaluate targets this often

    [Header("State Display")]
    public bool showStateText = true;
    public float textHeightOffset = 2.5f;

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
    private Vector3 lastTargetPosition;  // Track last known target position to reduce path updates
    private float retargetTimer;
    private bool isAttackingWall = false;  // True when committed to attacking a wall/gate
    private float handleBlockedCooldown = 0f;  // Prevents HandleBlockedPath from thrashing every frame

    // Faster breach check when attacking walls
    private float breachCheckTimer = 0f;
    private float breachCheckInterval = 0.5f;  // Check for breaches every 0.5s when attacking walls

    // Post-breach building hunt
    private bool justBreached = false;
    private float postBreachBuildingRange = 25f;

    // Static wall-target coordination: tracks how many enemies are targeting each wall/gate
    private static Dictionary<Transform, int> wallTargetCounts = new Dictionary<Transform, int>();
    private static readonly List<Transform> staleKeyBuffer = new List<Transform>();

    // Pooled NavMeshPath to avoid GC allocations
    private NavMeshPath reusablePath;

    // Pooled candidate list for HandleBlockedPath
    private readonly List<(Transform t, float score, string name)> blockedCandidates = new List<(Transform, float, string)>();

    // Audio - 3D Spatial Sound
    private AudioSource combatAudioSource;

    // NavMesh.CalculatePath throttle — max 2 per frame across all enemies
    private static int calcPathFrame = -1;
    private static int calcPathCount = 0;

    static bool TryCalculatePath(Vector3 src, Vector3 dst, int mask, NavMeshPath path)
    {
        if (Time.frameCount != calcPathFrame) { calcPathFrame = Time.frameCount; calcPathCount = 0; }
        if (calcPathCount >= 2) return false;
        calcPathCount++;
        NavMesh.CalculatePath(src, dst, mask, path);
        return true;
    }

    // --- Static wall-target coordination helpers ---

    static void RegisterWallTarget(Transform wallTransform)
    {
        CleanupStaleEntries();
        if (wallTransform == null) return;
        if (wallTargetCounts.ContainsKey(wallTransform))
            wallTargetCounts[wallTransform]++;
        else
            wallTargetCounts[wallTransform] = 1;
    }

    static void UnregisterWallTarget(Transform wallTransform)
    {
        if (wallTransform == null) return;
        if (wallTargetCounts.ContainsKey(wallTransform))
        {
            wallTargetCounts[wallTransform]--;
            if (wallTargetCounts[wallTransform] <= 0)
                wallTargetCounts.Remove(wallTransform);
        }
    }

    static int GetWallTargetCount(Transform wallTransform)
    {
        if (wallTransform == null) return 0;
        return wallTargetCounts.ContainsKey(wallTransform) ? wallTargetCounts[wallTransform] : 0;
    }

    static void CleanupStaleEntries()
    {
        staleKeyBuffer.Clear();
        foreach (var kvp in wallTargetCounts)
            if (kvp.Key == null) staleKeyBuffer.Add(kvp.Key);
        for (int i = 0; i < staleKeyBuffer.Count; i++)
            wallTargetCounts.Remove(staleKeyBuffer[i]);
    }

    void Start()
    {
        reusablePath = new NavMeshPath();

        // Get NavMeshAgent component
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError("Enemy: No NavMeshAgent found!");
            return;
        }

        // Configure NavMeshAgent for combat movement
        agent.speed = moveSpeed;
        agent.acceleration = 4f;         // Low acceleration to prevent ice skating
        agent.angularSpeed = 90f;        // Slower turning for more weight
        agent.stoppingDistance = attackRange - 1f;  // Stop a bit before attack range
        agent.autoBraking = true;
        agent.radius = 0.5f;             // Agent size for collision
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;  // Reduced from High for performance

        // Exclude gate passage area — enemies cannot walk through gates, only attack them
        agent.areaMask = NavMesh.AllAreas & ~(1 << Gate.GateAreaIndex);

        Debug.Log($"Enemy: NavMeshAgent configured - Speed: {agent.speed}, Accel: {agent.acceleration}");

        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;  // Enemies are destroyed on death
        healthComponent.onDeath.AddListener(Die);

        // Create floating state text
        if (showStateText)
        {
            CreateStateText();
        }

        // Setup 3D spatial audio for combat sounds
        SetupCombatAudioSource();

        // Stagger timers so not all enemies retarget/check on the same frame
        retargetTimer = Random.Range(0f, retargetInterval);
        breachCheckTimer = Random.Range(0f, breachCheckInterval);
        handleBlockedCooldown = Random.Range(0f, 2f);

        // Subscribe to wall/gate destroy events for immediate breach detection
        Wall.OnAnyWallDestroyed += OnWallOrGateDestroyed;
        Gate.OnAnyGateDestroyed += OnWallOrGateDestroyed;

        // Find the best target
        FindTarget();

        // Initialize last target position if we have a target
        if (hasTarget && target != null)
        {
            lastTargetPosition = target.position;
        }

        currentState = hasTarget ? $"Moving to {targetName}" : "Searching";

        Debug.Log($"Enemy: Spawned with {maxHealth} health. Target: {(hasTarget ? targetName : "None")}");
    }

    void OnDestroy()
    {
        activeList.Remove(this);

        // Unsubscribe from static events to prevent memory leaks
        Wall.OnAnyWallDestroyed -= OnWallOrGateDestroyed;
        Gate.OnAnyGateDestroyed -= OnWallOrGateDestroyed;

        // Unregister from wall target tracking
        if (isAttackingWall && target != null)
            UnregisterWallTarget(target);
    }

    /// <summary>
    /// Called when any wall or gate is destroyed. Forces immediate path recheck
    /// so ALL enemies can funnel through breaches — not just wall-attackers.
    /// </summary>
    void OnWallOrGateDestroyed()
    {
        // Unregister from wall target tracking if we were attacking a wall
        if (isAttackingWall && target != null)
            UnregisterWallTarget(target);

        // ALL enemies re-evaluate on wall destruction — a breach may have opened
        isAttackingWall = false;
        justBreached = true;  // Enable extended building search range post-breach
        breachCheckTimer = 0f;
        handleBlockedCooldown = 0f;  // Allow immediate path-around check
        retargetTimer = retargetInterval;  // Force retarget on next Update
    }

    void Update()
    {
        // Update state text
        if (showStateText && stateText != null)
        {
            UpdateStateText();
        }

        if (!hasTarget || target == null)
        {
            // Try to find target again if we lost it
            currentState = "Searching for target";
            FindTarget();
            ApplyNewTarget();
            return;
        }

        // Check if target is still alive (use cached Health to avoid GetComponent every frame)
        if (cachedTargetTransform != target)
        {
            cachedTargetHealth = target.GetComponent<Health>();
            cachedTargetTransform = target;
        }
        if (cachedTargetHealth != null && !cachedTargetHealth.IsAlive)
        {
            // Target is dead, find a new target
            currentState = "Target destroyed! Searching...";
            if (isAttackingWall && target != null)
                UnregisterWallTarget(target);
            isAttackingWall = false;
            agent.isStopped = true;
            FindTarget();
            ApplyNewTarget();
            return;
        }

        // Periodic retargeting - re-evaluate while moving
        retargetTimer += Time.deltaTime;
        if (retargetTimer >= retargetInterval)
        {
            retargetTimer = 0f;

            // If committed to attacking a wall, use faster breach check instead
            if (isAttackingWall)
            {
                // Check if wall target is still valid
                if (target == null || !IsTargetAlive(target))
                {
                    if (target != null) UnregisterWallTarget(target);
                    isAttackingWall = false;
                    FindTarget();
                    ApplyNewTarget();
                    return;
                }
                // Breach check handled separately below with faster timer
                return;
            }

            // Normal retarget for non-wall targets
            Transform previousTarget = target;
            FindTarget();
            if (target != previousTarget)
            {
                ApplyNewTarget();
            }
        }

        // Faster breach checking when attacking walls (every 0.5s instead of 1.5s)
        if (isAttackingWall)
        {
            breachCheckTimer += Time.deltaTime;
            if (breachCheckTimer >= breachCheckInterval)
            {
                breachCheckTimer = 0f;

                // Check if wall target is still valid
                if (target == null || !IsTargetAlive(target))
                {
                    if (target != null) UnregisterWallTarget(target);
                    isAttackingWall = false;
                    FindTarget();
                    ApplyNewTarget();
                    return;
                }

                // Breach funneling: check if a complete path to campfire now exists
                BaseBuilding campfire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
                if (campfire != null)
                {
                    if (TryCalculatePath(transform.position, campfire.transform.position, NavMesh.AllAreas, reusablePath)
                        && reusablePath.status == NavMeshPathStatus.PathComplete)
                    {
                        // Breach found! Stop attacking wall and go through
                        UnregisterWallTarget(target);
                        isAttackingWall = false;
                        justBreached = true;
                        FindTarget();
                        ApplyNewTarget();
#if UNITY_EDITOR
                        Debug.Log($"Enemy: Wall breach detected! Routing through gap");
#endif
                    }
                }
            }
        }

        // Move toward target
        MoveTowardTarget();

        // Check if in attack range
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // For walls/gates: use a more generous attack range since NavMesh carving
        // prevents the agent from reaching the wall center. The agent stops at the
        // edge of the carved hole, so we add extra range to compensate.
        float effectiveAttackRange = attackRange;
        if (isAttackingWall)
        {
            effectiveAttackRange = attackRange + 1.5f;  // Extra range to reach past carved NavMesh hole
        }

#if UNITY_EDITOR
        // Debug: Log distance and agent status occasionally
        if (Time.frameCount % 60 == 0)
        {
            bool hasPath = agent.hasPath;
            bool pathPending = agent.pathPending;
            float remainingDistance = agent.remainingDistance;
            Debug.Log($"Enemy {gameObject.name}: Distance to {targetName}: {distanceToTarget:F2}m (Attack range: {effectiveAttackRange}m) | HasPath: {hasPath} | PathPending: {pathPending} | RemainingDist: {remainingDistance:F2}m | WallMode: {isAttackingWall}");
        }

        // Stuck detection — only for non-wall targets (wall targets naturally stop at carved edge)
        if (!isAttackingWall && agent.hasPath && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (distanceToTarget > effectiveAttackRange)
            {
                Debug.LogWarning($"Enemy {gameObject.name}: STUCK! At stopping distance but outside attack range. Distance: {distanceToTarget:F2}m");
            }
        }
#endif

        if (distanceToTarget <= effectiveAttackRange)
        {
            // Stop moving and attack
            agent.isStopped = true;
            if (!currentState.StartsWith("Attacking"))
                currentState = "Attacking " + targetName + "!";
            AttemptAttack();
        }
        else
        {
            // Resume moving if we were stopped
            agent.isStopped = false;
            if (!currentState.StartsWith("Moving"))
                currentState = "Moving to " + targetName;
        }
    }

    void FindTarget()
    {
        // Priority System:
        // 1. Warriors within warriorDetectionRange -> attack nearest
        // 2. Buildings/huts within buildingEngagementRange -> attack nearest
        // 3. Campfire -> always the default objective

        Transform bestWarrior = null;
        float bestWarriorDistance = float.MaxValue;

        Transform bestBuilding = null;
        float bestBuildingDistance = float.MaxValue;
        string bestBuildingName = "";

        BaseBuilding campfire = null;

        // Scan warriors
        for (int i = 0; i < Warrior.ActiveList.Count; i++)
        {
            Warrior warrior = Warrior.ActiveList[i];
            if (warrior == null) continue;
            Health h = warrior.GetComponent<Health>();
            if (h != null && !h.IsAlive) continue;

            float distance = Vector3.Distance(transform.position, warrior.transform.position);
            if (distance <= warriorDetectionRange && distance < bestWarriorDistance)
            {
                bestWarriorDistance = distance;
                bestWarrior = warrior.transform;
            }
        }

        // Scan huts and watchtowers (buildings of opportunity)
        float effectiveBuildingRange = justBreached ? postBreachBuildingRange : buildingEngagementRange;

        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;
            Health h = hut.GetComponent<Health>();
            if (h != null && !h.IsAlive) continue;

            float distance = Vector3.Distance(transform.position, hut.transform.position);
            if (distance <= effectiveBuildingRange && distance < bestBuildingDistance)
            {
                bestBuildingDistance = distance;
                bestBuilding = hut.transform;
                bestBuildingName = hut.gameObject.name;
            }
        }

        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            Watchtower tower = Watchtower.ActiveList[i];
            if (tower == null) continue;
            Health h = tower.GetComponent<Health>();
            if (h != null && !h.IsAlive) continue;

            float distance = Vector3.Distance(transform.position, tower.transform.position);
            if (distance <= effectiveBuildingRange && distance < bestBuildingDistance)
            {
                bestBuildingDistance = distance;
                bestBuilding = tower.transform;
                bestBuildingName = tower.gameObject.name;
            }
        }

        // Campfire
        if (BaseBuilding.ActiveList.Count > 0)
        {
            campfire = BaseBuilding.ActiveList[0];
            if (campfire != null)
            {
                Health h = campfire.GetComponent<Health>();
                if (h != null && !h.IsAlive) campfire = null;
            }
        }

        // Decision Logic:
        // 1. Nearby warriors get highest priority
        if (bestWarrior != null)
        {
            target = bestWarrior;
            targetName = bestWarrior.gameObject.name;
            hasTarget = true;
#if UNITY_EDITOR
            Debug.Log($"Enemy: Targeting nearby WARRIOR - {targetName} at {bestWarriorDistance:F1}m");
#endif
        }
        // 2. Nearby buildings are targets of opportunity
        else if (bestBuilding != null)
        {
            target = bestBuilding;
            targetName = bestBuildingName;
            hasTarget = true;
#if UNITY_EDITOR
            Debug.Log($"Enemy: Targeting nearby building - {targetName} at {bestBuildingDistance:F1}m");
#endif
        }
        // 3. Campfire is always the ultimate objective
        else if (campfire != null)
        {
            target = campfire.transform;
            targetName = "Campfire";
            hasTarget = true;
#if UNITY_EDITOR
            Debug.Log($"Enemy: Heading for Campfire");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("Enemy: No valid targets found!");
#endif
            hasTarget = false;
        }

        // Reset post-breach flag after one use
        justBreached = false;
    }

    bool IsTargetAlive(Transform t)
    {
        if (t == null) return false;
        Health h = t.GetComponent<Health>();
        return h != null && h.IsAlive;
    }

    void ApplyNewTarget()
    {
        if (hasTarget && target != null)
        {
            lastTargetPosition = target.position;
            agent.isStopped = false;
            agent.SetDestination(target.position);
        }
    }

    void MoveTowardTarget()
    {
        if (agent != null && target != null)
        {
            // If attacking a wall, don't re-pathfind (wall center is carved out of NavMesh)
            if (isAttackingWall)
            {
                // Just keep moving toward the wall position; attack range check handles the rest
                if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
                {
                    agent.SetDestination(target.position);
                    lastTargetPosition = target.position;
                }
                return;
            }

            // Only update destination if target has moved significantly OR if path is invalid
            float distanceMoved = Vector3.Distance(target.position, lastTargetPosition);
            bool needsNewPath = !agent.hasPath || agent.pathPending || agent.pathStatus == NavMeshPathStatus.PathInvalid;

            if (distanceMoved > destinationUpdateThreshold || needsNewPath)
            {
                agent.SetDestination(target.position);
                lastTargetPosition = target.position;
            }

            // If path is partial (blocked by wall), try to find a way around
            // Use a cooldown so this doesn't thrash every frame
            handleBlockedCooldown -= Time.deltaTime;
            if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathPartial && handleBlockedCooldown <= 0f)
            {
                handleBlockedCooldown = 2f;  // Only re-evaluate every 2 seconds
                HandleBlockedPath();
            }
        }
    }

    void HandleBlockedPath()
    {
        // No complete path to current target — walls are in the way.
        // First check: is there a complete path to campfire going AROUND the walls?
        BaseBuilding campfire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
        if (campfire != null)
        {
            if (TryCalculatePath(transform.position, campfire.transform.position, NavMesh.AllAreas, reusablePath)
                && reusablePath.status == NavMeshPathStatus.PathComplete)
            {
                // A route around exists — use normal targeting (will pick campfire or closer building)
                if (isAttackingWall && target != null)
                    UnregisterWallTarget(target);
                isAttackingWall = false;
                FindTarget();
                ApplyNewTarget();
#if UNITY_EDITOR
                Debug.Log($"Enemy: Path blocked to target, but found route around walls");
#endif
                return;
            }
        }

        // No complete path exists — walls truly block all routes.
        // Build scored candidate list with crowd penalty to distribute enemies across walls.
        blockedCandidates.Clear();

        for (int i = 0; i < Wall.ActiveList.Count; i++)
        {
            Wall wall = Wall.ActiveList[i];
            if (wall == null) continue;
            Health wallHealth = wall.GetComponent<Health>();
            if (wallHealth == null || !wallHealth.IsAlive) continue;

            float dist = Vector3.Distance(transform.position, wall.transform.position);
            int attackers = GetWallTargetCount(wall.transform);
            float score = dist * (1f + attackers * 0.5f);
            blockedCandidates.Add((wall.transform, score, wall.gameObject.name));
        }

        // Also consider gates (enemies can attack them to destroy them)
        for (int i = 0; i < Gate.ActiveList.Count; i++)
        {
            Gate gate = Gate.ActiveList[i];
            if (gate == null) continue;
            Health gateHealth = gate.GetComponent<Health>();
            if (gateHealth == null || !gateHealth.IsAlive) continue;

            float dist = Vector3.Distance(transform.position, gate.transform.position);
            dist *= 0.3f;  // Strong preference for gates — treat as much closer (weak points)
            int attackers = GetWallTargetCount(gate.transform);
            float score = dist * (1f + attackers * 0.5f);
            blockedCandidates.Add((gate.transform, score, gate.gameObject.name));
        }

        if (blockedCandidates.Count > 0)
        {
            // Sort by score (lowest = best target)
            blockedCandidates.Sort((a, b) => a.score.CompareTo(b.score));
            var best = blockedCandidates[0];

            // Unregister from previous wall target if switching
            if (isAttackingWall && target != null && target != best.t)
                UnregisterWallTarget(target);

            target = best.t;
            targetName = best.name;
            hasTarget = true;
            isAttackingWall = true;  // Commit to this wall — don't let retarget override
            RegisterWallTarget(target);
            lastTargetPosition = target.position;
            agent.SetDestination(target.position);
#if UNITY_EDITOR
            Debug.Log($"Enemy: All paths blocked! Committed to attacking {targetName} (score: {best.score:F1}, attackers: {GetWallTargetCount(target)})");
#endif
        }
    }

    void AttemptAttack()
    {
        // Check cooldown
        if (Time.time - lastAttackTime < attackCooldown)
        {
            return;
        }

        lastAttackTime = Time.time;

        // Attack the target
#if UNITY_EDITOR
        Debug.Log($"Enemy: Attacking {target.name} for {damage} damage!");
#endif

        // Spawn attack visual effect
        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(transform.position, target.position, false);
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
            cachedTargetHealth.TakeDamage(damage);
        }
        else
        {
            Debug.LogWarning($"Enemy: Target {target.name} has no Health component!");
        }
    }

    // Take damage from player/warriors (passes to Health component)
    public void TakeDamage(float damageAmount)
    {
        if (healthComponent != null)
        {
            healthComponent.TakeDamage(damageAmount);
        }
    }

    void Die()
    {
        Debug.Log("Enemy: Defeated!");

        // Unregister from wall target tracking
        if (isAttackingWall && target != null)
            UnregisterWallTarget(target);

        // Play death sound (3D spatial audio)
        PlayDeathSound();

        // Notify spawner
        EnemySpawner spawner = FindFirstObjectByType<EnemySpawner>();
        if (spawner != null)
        {
            spawner.NotifyEnemyKilled(gameObject);
        }

        // Notify GameManager for statistics
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyEnemyKilled();
        }

        // Health component will handle destruction
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

        // Volume settings (enemies slightly quieter than warriors)
        combatAudioSource.volume = 0.45f;

        // No doppler for combat sounds
        combatAudioSource.dopplerLevel = 0f;
    }

    void PlayAttackSound()
    {
        if (combatAudioSource != null && AudioManager.Instance != null && AudioManager.Instance.enemyAttackSound != null)
        {
            combatAudioSource.PlayOneShot(AudioManager.Instance.enemyAttackSound, 0.6f);
        }
    }

    void PlayDeathSound()
    {
        if (combatAudioSource != null && AudioManager.Instance != null && AudioManager.Instance.enemyDeathSound != null)
        {
            combatAudioSource.PlayOneShot(AudioManager.Instance.enemyDeathSound, 1f);
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
        stateText.color = Color.red;

        // Make sure text renders on top
        stateText.GetComponent<MeshRenderer>().sortingOrder = 100;

        Debug.Log("Enemy: State text created");
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
        if (currentState.StartsWith("Attacking"))
        {
            stateText.color = Color.red;
        }
        else if (currentState.StartsWith("Moving"))
        {
            stateText.color = Color.yellow;
        }
        else if (currentState.StartsWith("Searching"))
        {
            stateText.color = Color.gray;
        }
        else
        {
            stateText.color = Color.white;
        }
    }

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        // Draw attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Draw warrior detection range
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, warriorDetectionRange);

        // Draw building engagement range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, buildingEngagementRange);
    }
}

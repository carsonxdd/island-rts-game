using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class Enemy : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 50f;
    public float damage = 10f;
    public float attackRange = 3.5f;  // Increased to account for building size
    public float attackCooldown = 1.5f;

    [Header("Movement")]
    public float moveSpeed = 2f;  // Slow, shambling speed for enemies
    public float destinationUpdateThreshold = 1.5f;  // Only update destination if target moved this far

    [Header("Targeting")]
    public float campfireAvoidDistance = 15f;  // Prefer other buildings if campfire is further than this

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
    private TextMeshPro stateText;
    private GameObject stateTextObject;
    private string currentState = "Spawning";
    private Vector3 lastTargetPosition;  // Track last known target position to reduce path updates

    void Start()
    {
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

            // Initialize last position if we found a new target
            if (hasTarget && target != null)
            {
                lastTargetPosition = target.position;
            }
            return;
        }

        // Check if target is still alive
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null && !targetHealth.IsAlive)
        {
            // Target is dead, find a new target
            currentState = "Target destroyed! Searching...";
            agent.isStopped = true;
            FindTarget();

            // Initialize last position if we found a new target
            if (hasTarget && target != null)
            {
                lastTargetPosition = target.position;
            }
            return;
        }

        // Move toward target
        MoveTowardTarget();

        // Check if in attack range
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // Debug: Log distance and agent status occasionally
        if (Time.frameCount % 60 == 0)  // Every 60 frames (~1 second)
        {
            bool hasPath = agent.hasPath;
            bool pathPending = agent.pathPending;
            float remainingDistance = agent.remainingDistance;
            Debug.Log($"Enemy {gameObject.name}: Distance to {targetName}: {distanceToTarget:F2}m (Attack range: {attackRange}m) | HasPath: {hasPath} | PathPending: {pathPending} | RemainingDist: {remainingDistance:F2}m");
        }

        // Also check if agent can't reach target (stuck)
        if (agent.hasPath && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            // Agent has stopped moving
            if (distanceToTarget > attackRange)
            {
                // Stopped but not in attack range = stuck
                Debug.LogWarning($"Enemy {gameObject.name}: STUCK! At stopping distance but outside attack range. Distance: {distanceToTarget:F2}m");
            }
        }

        if (distanceToTarget <= attackRange)
        {
            // Stop moving and attack
            agent.isStopped = true;
            currentState = $"Attacking {targetName}!";
            AttemptAttack();
        }
        else
        {
            // Resume moving if we were stopped
            agent.isStopped = false;
            currentState = $"Moving to {targetName} ({distanceToTarget:F1}m)";
        }
    }

    void FindTarget()
    {
        // Priority System:
        // 1. Warriors (highest priority - defend against defenders!)
        // 2. Buildings (huts, etc.) if closer than campfire
        // 3. Campfire (only if it's the last thing or very close)

        Transform bestWarrior = null;
        float bestWarriorDistance = float.MaxValue;

        Transform bestBuilding = null;
        float bestBuildingDistance = float.MaxValue;
        string bestBuildingName = "";

        BaseBuilding campfire = null;
        float campfireDistance = float.MaxValue;

        // Check for all objects with Health components
        Health[] allHealthObjects = FindObjectsByType<Health>(FindObjectsSortMode.None);

        foreach (Health healthObj in allHealthObjects)
        {
            // Skip if not alive
            if (!healthObj.IsAlive)
                continue;

            // Skip self
            if (healthObj.transform == transform)
                continue;

            // Skip other enemies
            if (healthObj.GetComponent<Enemy>() != null)
                continue;

            float distance = Vector3.Distance(transform.position, healthObj.transform.position);

            // Check if this is a warrior (HIGHEST PRIORITY)
            Warrior warrior = healthObj.GetComponent<Warrior>();
            if (warrior != null)
            {
                if (distance < bestWarriorDistance)
                {
                    bestWarriorDistance = distance;
                    bestWarrior = warrior.transform;
                }
                continue;
            }

            // Check if this is the campfire
            BaseBuilding baseBuildingComponent = healthObj.GetComponent<BaseBuilding>();
            if (baseBuildingComponent != null)
            {
                campfire = baseBuildingComponent;
                campfireDistance = distance;
                continue;  // Don't target yet
            }

            // Regular building (hut, etc.) - medium priority
            if (distance < bestBuildingDistance)
            {
                bestBuildingDistance = distance;
                bestBuilding = healthObj.transform;
                bestBuildingName = healthObj.gameObject.name;
            }
        }

        // Decision Logic - Priority order:
        // 1. Attack warriors if any exist (they're defending!)
        if (bestWarrior != null)
        {
            target = bestWarrior;
            targetName = bestWarrior.gameObject.name;
            hasTarget = true;
            Debug.Log($"Enemy: Targeting WARRIOR - {targetName} at {bestWarriorDistance:F1}m");
        }
        // 2. Attack buildings if they exist and are closer than campfire
        else if (bestBuilding != null && (campfire == null || bestBuildingDistance < campfireDistance))
        {
            target = bestBuilding;
            targetName = bestBuildingName;
            hasTarget = true;
            Debug.Log($"Enemy: Targeting building - {targetName} at {bestBuildingDistance:F1}m");
        }
        // 3. Attack campfire if it's very close (within avoid distance)
        else if (campfire != null && campfireDistance < campfireAvoidDistance)
        {
            target = campfire.transform;
            targetName = "Campfire";
            hasTarget = true;
            Debug.Log($"Enemy: Campfire is close ({campfireDistance:F1}m). Targeting it.");
        }
        // 4. Attack campfire if it's the only thing left
        else if (campfire != null && bestBuilding == null && bestWarrior == null)
        {
            target = campfire.transform;
            targetName = "Campfire";
            hasTarget = true;
            Debug.Log($"Enemy: No other targets. Going for Campfire at {campfireDistance:F1}m");
        }
        else
        {
            Debug.LogWarning("Enemy: No valid targets found!");
            hasTarget = false;
        }
    }

    void MoveTowardTarget()
    {
        if (agent != null && target != null)
        {
            // Only update destination if target has moved significantly OR if path is invalid
            // This reduces stuttering when target moves slightly
            float distanceMoved = Vector3.Distance(target.position, lastTargetPosition);
            bool needsNewPath = !agent.hasPath || agent.pathPending || agent.pathStatus == NavMeshPathStatus.PathInvalid;

            if (distanceMoved > destinationUpdateThreshold || needsNewPath)
            {
                agent.SetDestination(target.position);
                lastTargetPosition = target.position;
            }
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
        Debug.Log($"Enemy: Attacking {target.name} for {damage} damage!");

        // Spawn attack visual effect
        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(transform.position, target.position, false);
        }

        // Apply damage to target's Health component
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null)
        {
            targetHealth.TakeDamage(damage);
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
        if (stateText != null)
        {
            // Update text content
            stateText.text = currentState;

            // Color based on state
            if (currentState.Contains("Attacking"))
            {
                stateText.color = Color.red;
            }
            else if (currentState.Contains("Moving"))
            {
                stateText.color = Color.yellow;
            }
            else if (currentState.Contains("Searching"))
            {
                stateText.color = Color.gray;
            }
            else
            {
                stateText.color = Color.white;
            }

            // Billboard effect - always face camera
            if (Camera.main != null)
            {
                stateTextObject.transform.LookAt(Camera.main.transform);
                stateTextObject.transform.Rotate(0, 180, 0);  // Flip to face correctly
            }
        }
    }

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        // Draw attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}

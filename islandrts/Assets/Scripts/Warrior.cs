using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class Warrior : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 75f;
    public float damage = 15f;
    public float attackRange = 4.5f;  // Increased for better reach
    public float attackCooldown = 1.2f;

    [Header("Movement")]
    public float moveSpeed = 3.5f;  // Faster than enemies, slower than workers
    public float destinationUpdateThreshold = 1.5f;  // Only update destination if target moved this far

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
    private TextMeshPro stateText;
    private GameObject stateTextObject;
    private string currentState = "Spawning";
    private Vector3 idlePosition;
    private Vector3 lastTargetPosition;  // Track last known target position to reduce path updates
    private Vector3 currentPatrolPoint;
    private float patrolWaitTimer = 0f;
    private bool isWaitingAtPatrol = false;

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
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
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

        // Store idle position (spawn position)
        idlePosition = transform.position;
        currentPatrolPoint = GetRandomPatrolPoint();

        Debug.Log($"Warrior: Spawned with {maxHealth} health at {transform.position}");
    }

    void Update()
    {
        // Update state text
        if (showStateText && stateText != null)
        {
            UpdateStateText();
        }

        // Search for enemies
        if (!hasTarget || target == null)
        {
            FindNearestEnemy();

            if (!hasTarget)
            {
                // No enemies - patrol around campfire
                currentState = "Patrolling";
                PatrolBehavior();
                return;
            }
            else
            {
                // Found a new target - reset last position
                lastTargetPosition = target.position;
            }
        }

        // Check if target is still alive
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null && !targetHealth.IsAlive)
        {
            // Target is dead, find a new target
            currentState = "Enemy defeated! Searching...";
            agent.isStopped = true;
            hasTarget = false;
            target = null;
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

            currentState = $"Attacking {targetName}!";
            AttemptAttack();
        }
        else
        {
            // Out of range - move closer
            agent.isStopped = false;
            currentState = $"Engaging {targetName} ({distanceToTarget:F1}m)";
        }
    }

    void FindNearestEnemy()
    {
        // Find all enemies in the scene
        Enemy[] allEnemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);

        if (allEnemies.Length == 0)
        {
            hasTarget = false;
            return;
        }

        Transform nearestEnemy = null;
        float nearestDistance = searchRadius;

        foreach (Enemy enemy in allEnemies)
        {
            // Check if enemy is alive
            Health enemyHealth = enemy.GetComponent<Health>();
            if (enemyHealth != null && !enemyHealth.IsAlive)
                continue;

            float distance = Vector3.Distance(transform.position, enemy.transform.position);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestEnemy = enemy.transform;
            }
        }

        if (nearestEnemy != null)
        {
            target = nearestEnemy;
            targetName = nearestEnemy.gameObject.name;
            hasTarget = true;
            Debug.Log($"Warrior: Targeting enemy - {targetName} at {nearestDistance:F1}m");
        }
        else
        {
            hasTarget = false;
        }
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
                Debug.Log($"Warrior: Finished waiting {patrolWaitTime}s at patrol point, moving to next position");
                currentPatrolPoint = GetRandomPatrolPoint();
                isWaitingAtPatrol = false;
                patrolWaitTimer = 0f;
            }
        }
        else if (distanceToPatrolPoint < 1.5f)
        {
            // Reached patrol point, start waiting
            Debug.Log($"Warrior: Reached patrol point, waiting for {patrolWaitTime}s");
            isWaitingAtPatrol = true;
            patrolWaitTimer = 0f;
        }
        else
        {
            // Moving to patrol point
            agent.isStopped = false;
            agent.SetDestination(currentPatrolPoint);
        }
    }

    Vector3 GetRandomPatrolPoint()
    {
        // Find all buildings to patrol around their perimeter
        BaseBuilding[] campfires = FindObjectsByType<BaseBuilding>(FindObjectsSortMode.None);
        Hut[] huts = FindObjectsByType<Hut>(FindObjectsSortMode.None);

        // Build list of all buildings with their no-build radii
        System.Collections.Generic.List<System.Tuple<Transform, float>> buildingsWithRadius =
            new System.Collections.Generic.List<System.Tuple<Transform, float>>();

        foreach (var campfire in campfires)
            buildingsWithRadius.Add(new System.Tuple<Transform, float>(campfire.transform, campfire.noBuildRadius));

        foreach (var hut in huts)
            buildingsWithRadius.Add(new System.Tuple<Transform, float>(hut.transform, hut.noBuildRadius));

        // If we have buildings, patrol their outer perimeter
        if (buildingsWithRadius.Count > 0)
        {
            for (int attempts = 0; attempts < 20; attempts++)
            {
                // Pick a random building
                var randomBuilding = buildingsWithRadius[Random.Range(0, buildingsWithRadius.Count)];
                Transform buildingTransform = randomBuilding.Item1;
                float noBuildRadius = randomBuilding.Item2;

                // Generate random point on the building's no-build perimeter
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 perimeterPoint = buildingTransform.position + new Vector3(
                    Mathf.Cos(angle) * noBuildRadius,
                    0f,
                    Mathf.Sin(angle) * noBuildRadius
                );

                // Check if this point is OUTSIDE all other buildings' no-build zones (outer perimeter only)
                bool isOuterPerimeter = true;
                foreach (var otherBuilding in buildingsWithRadius)
                {
                    if (otherBuilding.Item1 == buildingTransform) continue; // Skip same building

                    float distanceToOther = Vector3.Distance(perimeterPoint, otherBuilding.Item1.position);
                    if (distanceToOther < otherBuilding.Item2)
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

        // Attack the target
        Debug.Log($"Warrior: Attacking {target.name} for {damage} damage!");

        // Spawn attack visual effect
        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(transform.position, target.position, true);
        }

        // Apply damage to target's Health component
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null)
        {
            targetHealth.TakeDamage(damage);
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

        // Notify base building to update warrior count
        if (baseBuilding != null)
        {
            baseBuilding.NotifyWarriorKilled(gameObject);
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
        stateText.color = Color.blue;

        // Make sure text renders on top
        stateText.GetComponent<MeshRenderer>().sortingOrder = 100;

        Debug.Log("Warrior: State text created");
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
            else if (currentState.Contains("Engaging") || currentState.Contains("defeated"))
            {
                stateText.color = Color.yellow;
            }
            else if (currentState.Contains("Idle"))
            {
                stateText.color = new Color(0.5f, 0.5f, 1f);  // Light blue
            }
            else
            {
                stateText.color = Color.cyan;
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
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Draw search radius
        Gizmos.color = new Color(0, 0, 1, 0.2f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
}

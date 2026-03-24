using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class Enemy : MonoBehaviour
{
    public static IReadOnlyList<Enemy> ActiveList => ActiveRegistry<Enemy>.List;

    // Static event: fires when any enemy dies (with death position for proximity checks)
    public static event System.Action<Vector3> OnAnyEnemyDied;

    // Cached EnemySpawner reference (avoids FindFirstObjectByType on every death)
    private static EnemySpawner cachedSpawner;
    private static bool spawnerCached = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cachedSpawner = null; spawnerCached = false; OnAnyEnemyDied = null; }

    void Awake() { ActiveRegistry<Enemy>.Register(this); }

    [Header("Stats")]
    public float maxHealth = 50f;
    public float damage = 10f;
    public float attackRange = 3.5f;  // Increased to account for building size
    public float attackCooldown = 1.5f;

    [Header("Movement")]
    public float moveSpeed = 2f;  // Slow, shambling speed for enemies

    [Header("Targeting")]
    public float warriorDetectionRange = 15f;  // Only engage warriors within this range
    public float buildingEngagementRange = 20f;  // Only engage buildings within this range

    [Header("State Display")]
    public bool showStateText = true;
    public float textHeightOffset = 2.5f;

    // Utility AI components
    private AIBrain aiBrain;

    // Private
    private NavMeshAgent agent;
    private Health healthComponent;
    public Health CachedHealth => healthComponent;
    private FloatingText floatingText;

    // Audio - 3D Spatial Sound
    private AudioSource combatAudioSource;

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
        agent.avoidancePriority = Random.Range(30, 70);  // Randomized priority to prevent synchronized yielding

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
            floatingText = gameObject.AddComponent<FloatingText>();
            floatingText.heightOffset = textHeightOffset;
            floatingText.fontSize = 2f;
            floatingText.initialText = "Searching";
            floatingText.initialColor = Color.red;
        }

        // Setup 3D spatial audio for combat sounds
        SetupCombatAudioSource();

        // Subscribe to wall/gate destroy events for immediate breach detection
        Wall.OnAnyWallDestroyed += OnWallOrGateDestroyed;
        Gate.OnAnyGateDestroyed += OnWallOrGateDestroyed;

        Debug.Log($"Enemy: Spawned with {maxHealth} health at {transform.position}");

        // Initialize Utility AI
        InitializeUtilityAI();
    }

    void InitializeUtilityAI()
    {
        aiBrain = gameObject.AddComponent<AIBrain>();

        var bb = new AIBlackboard();
        bb.transform = transform;
        bb.agent = agent;
        bb.health = healthComponent;
        bb.enemy = this;
        bb.attackRange = attackRange;
        bb.attackCooldown = attackCooldown;
        bb.damage = damage;
        bb.warriorDetectionRange = warriorDetectionRange;
        bb.buildingEngagementRange = buildingEngagementRange;

        // Setup StuckResolver (same pattern as Worker/Warrior)
        var stuckResolver = gameObject.AddComponent<StuckResolver>();
        stuckResolver.Initialize(agent, ActiveRegistry<Enemy>.IndexOf(this));
        stuckResolver.onStuckReset = () =>
        {
            bb.currentTarget = null;
            bb.currentTargetHealth = null;
            bb.isInAttackRange = false;
            bb.isAttackingWall = false;
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = false;
            }
            aiBrain.ForceReeval();
        };
        bb.stuckResolver = stuckResolver;

        // Damage ForceReeval: re-evaluate when hit (cooldown-based, max once per second)
        float lastDamageReeval = 0f;
        healthComponent.onDamaged.AddListener(() =>
        {
            if (Time.time - lastDamageReeval > 1f)
            {
                lastDamageReeval = Time.time;
                aiBrain.ForceReeval();
            }
        });

        // Enemy-specific considerations for path-blocked detection
        var pathBlockedConsideration = new PathBlocked(bb);

        var actions = new ActionOption[]
        {
            // Attack Warrior (highest priority when warriors nearby — must beat building momentum + commitment)
            new ActionOption("AttackWarrior", new Consideration[]
            {
                new EnemyHasTarget(EnemyHasTarget.TargetCategory.Warrior, ResponseCurve.Linear(1f, 0f)),
                new DistanceTo(DistanceTo.TargetType.NearestEnemy, warriorDetectionRange, ResponseCurve.Constant(1f))
            }, new AttackTargetExecutor(AttackTargetExecutor.TargetCategory.Warrior),
            basePriority: 1.2f, momentumBonus: 0.2f),

            // Attack Building (when no warriors, buildings reachable)
            new ActionOption("AttackBuilding", new Consideration[]
            {
                new EnemyHasTarget(EnemyHasTarget.TargetCategory.Building, ResponseCurve.Linear(1f, 0f))
            }, new AttackTargetExecutor(AttackTargetExecutor.TargetCategory.Building),
            basePriority: 0.7f, momentumBonus: 0.15f),

            // Breach Wall (when path is blocked by walls)
            new ActionOption("BreachWall", new Consideration[]
            {
                pathBlockedConsideration
            }, new BreachWallExecutor(), basePriority: 0.8f, momentumBonus: 0.3f),

            // Attack Campfire (ultimate fallback objective)
            new ActionOption("AttackCampfire", new Consideration[]
            {
                new EnemyHasTarget(EnemyHasTarget.TargetCategory.Campfire, ResponseCurve.Constant(0.3f))
            }, new AttackTargetExecutor(AttackTargetExecutor.TargetCategory.Campfire),
            basePriority: 0.3f, momentumBonus: 0.1f)
        };

        bb.brain = aiBrain;
        aiBrain.Initialize(actions, bb);
    }

    void OnDestroy()
    {
        ActiveRegistry<Enemy>.Unregister(this);

        // Unsubscribe from static events to prevent memory leaks
        Wall.OnAnyWallDestroyed -= OnWallOrGateDestroyed;
        Gate.OnAnyGateDestroyed -= OnWallOrGateDestroyed;
    }

    /// <summary>
    /// Called when any wall or gate is destroyed. Forces immediate path recheck
    /// so ALL enemies can funnel through breaches.
    /// </summary>
    void OnWallOrGateDestroyed()
    {
        if (aiBrain != null)
        {
            var bb = aiBrain.blackboard;
            if (bb != null)
            {
                bb.isAttackingWall = false;
            }
            aiBrain.ForceReeval();
        }
    }

    void Update()
    {
        // AIBrain drives behavior, we just update visuals
        if (showStateText && floatingText != null)
        {
            UpdateStateText();
        }
    }

    void Die()
    {
        Debug.Log("Enemy: Defeated!");

        // Fire static death event for nearby units to react
        OnAnyEnemyDied?.Invoke(transform.position);

        // Play death sound (3D spatial audio)
        PlayDeathSound();

        // Notify spawner (cached reference — no scene scan)
        if (!spawnerCached)
        {
            cachedSpawner = FindFirstObjectByType<EnemySpawner>();
            spawnerCached = true;
        }
        if (cachedSpawner != null)
        {
            cachedSpawner.NotifyEnemyKilled(gameObject);
        }

        // Notify GameManager for statistics
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyEnemyKilled();
        }

        // Health component will handle destruction
    }

    /// <summary>
    /// Called by Gate trigger when enemy walks into a gate.
    /// Forces the enemy to stop and attack the gate.
    /// </summary>
    public void ForceAttackGate(Gate gate)
    {
        if (gate == null) return;

        // Check if gate is still alive
        Health gateHealth = gate.CachedHealth;
        if (gateHealth == null || !gateHealth.IsAlive) return;

        if (aiBrain != null)
        {
            var bb = aiBrain.blackboard;
            if (bb != null)
            {
                bb.currentTarget = gate.transform;
                bb.currentTargetName = gate.gameObject.name;
                bb.currentTargetHealth = gateHealth;
                bb.isAttackingWall = true;
            }
            aiBrain.ForceReeval();
        }
    }

    // --- Public sound methods for Utility AI executors ---
    public void PlayAttackSoundPublic() { PlayAttackSound(); }
    public void PlayDeathSoundPublic() { PlayDeathSound(); }

    // --- State text ---

    void UpdateStateText()
    {
        string displayName = aiBrain != null && aiBrain.blackboard != null ? aiBrain.blackboard.stateDisplayName : "Searching";
        if (displayName == null) displayName = "Searching";

        Color color;
        if (displayName.Contains("Attacking"))
            color = Color.red;
        else if (displayName.Contains("Moving") || displayName.Contains("Breaching"))
            color = Color.yellow;
        else
            color = Color.gray;

        floatingText.SetText(displayName, color);
    }

    // --- Audio ---

    void SetupCombatAudioSource()
    {
        combatAudioSource = AudioHelper.CreateSpatialAudioSource(gameObject, 0.45f, 8f, 35f);
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

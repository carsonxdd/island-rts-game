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
    public float attackRange = 4f;  // Increased to account for building size (Phase 6.21: bumped from 3.5 to fix enemies stuck outside hut attack range)
    public float attackCooldown = 1.5f;

    [Header("Movement")]
    public float moveSpeed = 2f;  // Slow, shambling speed for enemies

    [Header("Targeting")]
    public float warriorDetectionRange = 15f;  // Only engage warriors within this range

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
        agent.stoppingDistance = 0.5f;  // Minimal — EnemyAttackExecutor uses ClosestPoint edge-distance to trip attack state
        agent.autoBraking = true;
        agent.radius = 0.5f;             // Agent size for collision
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;  // Reduced from High for performance
        agent.avoidancePriority = Random.Range(30, 70);  // Randomized priority to prevent synchronized yielding

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

        // Setup StuckResolver (same pattern as Worker/Warrior)
        var stuckResolver = gameObject.AddComponent<StuckResolver>();
        stuckResolver.Initialize(agent, ActiveRegistry<Enemy>.IndexOf(this));
        stuckResolver.onStuckReset = () =>
        {
            // Clear target and let PickTarget choose fresh. Reachability is tested
            // at pick-time, so the truly unreachable target won't be re-picked
            // immediately — no blacklist needed.
            bb.currentTarget = null;
            bb.currentTargetHealth = null;
            bb.currentTargetCollider = null;
            bb.isInAttackRange = false;
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = false;
            }
            // Re-roll avoidance priority to break ORCA ties between stacked enemies.
            agent.avoidancePriority = Random.Range(30, 70);
            aiBrain.ForceReeval();
        };
        bb.stuckResolver = stuckResolver;

        // Single "Attack" action. Priority-based target selection lives inside the
        // executor (see EnemyAttackExecutor.PickTarget), not in competing
        // ActionOptions. Empty consideration array → basePriority is the final
        // score, so this action is always selected.
        var actions = new ActionOption[]
        {
            new ActionOption("Attack", new Consideration[0],
                new EnemyAttackExecutor(),
                basePriority: 1f, momentumBonus: 0f)
        };

        bb.brain = aiBrain;
        aiBrain.Initialize(actions, bb);
    }

    void OnDestroy()
    {
        ActiveRegistry<Enemy>.Unregister(this);
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
    /// Called by Gate trigger when enemy walks into a gate. Gates don't carve
    /// the NavMesh, so without this override enemies can path through a live
    /// gate toward the campfire. Stamp forcedTarget with a short expiry; the
    /// executor's PickTarget honors it. Trigger fires repeatedly while the enemy
    /// is inside the gate volume, refreshing the lock naturally.
    /// </summary>
    public void ForceAttackGate(Gate gate)
    {
        if (gate == null) return;
        Health gateHealth = gate.CachedHealth;
        if (gateHealth == null || !gateHealth.IsAlive) return;
        if (aiBrain == null || aiBrain.blackboard == null) return;

        var bb = aiBrain.blackboard;
        // Only stamp + reeval if the forced target actually changed, so repeated
        // trigger ticks don't hammer ForceReeval every frame.
        bool isNew = bb.forcedTarget != gate.transform;
        bb.forcedTarget = gate.transform;
        bb.forcedTargetExpiry = Time.time + 1.5f;
        if (isNew) aiBrain.ForceReeval();
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
    }
}

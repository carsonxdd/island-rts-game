using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// A night raider. Spawns offshore at nightfall, wades in and works its way toward the
/// campfire, fighting warriors and chewing through whatever the colony put in its way.
/// </summary>
/// <remarks>
/// Enemies have a single AI action, Attack. Everything about their behaviour comes from
/// the priority order in which its executor picks a target - warriors first, then huts and
/// towers, then walls and gates, then the campfire. That is deliberate: an earlier version
/// had four competing actions for those cases, and momentum plus the commitment threshold
/// made them fight each other every time a target died, which read as a group freeze.
/// </remarks>
public class Enemy : UnitBase<Enemy>
{
    // Static event: fires when any enemy dies (with death position for proximity checks)
    public static event System.Action<Vector3> OnAnyEnemyDied;

    // Cached EnemySpawner reference (avoids FindAnyObjectByType on every death)
    private static EnemySpawner cachedSpawner;
    private static bool spawnerCached = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cachedSpawner = null; spawnerCached = false; OnAnyEnemyDied = null; }

    [Header("Stats")]
    public float maxHealth = 50f;
    public float damage = 10f;
    public float attackRange = 4f;  // Increased to account for building size (Phase 6.21: bumped from 3.5 to fix enemies stuck outside hut attack range)
    public float attackCooldown = 1.5f;

    [Header("Movement")]
    public float moveSpeed = 2.6f;  // Shambling; still the slowest thing on the field (warrior 3.5 outruns it cleanly). Raised from 2.0 in the snap pass: the island went to 150x150 and EnemySpawner.spawnDistance to 45, so at 2.0 a third of every 60s night was commute

    [Header("Targeting")]
    public float warriorDetectionRange = 15f;  // Only engage warriors within this range

    void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Balance-sim knobs, if a sweep is running. Must land before these
        // values are copied into the AI blackboard below.
        SimOverrides.Apply(this);
#endif
        // The run's difficulty, applied the same way and for the same reason:
        // these values are copied into the AI blackboard below, so patching an
        // enemy after it spawns is too late. Difficulty.Active is a snapshot
        // taken when the run began, so a wave never changes mid-game.
        maxHealth *= Difficulty.EnemyHealthMultiplier;
        damage *= Difficulty.EnemyDamageMultiplier;

        // Get NavMeshAgent component
        if (!FetchAgent())
        {
            return;
        }

        // Configure NavMeshAgent for combat movement
        agent.speed = moveSpeed;
        agent.acceleration = 9f;         // Snap pass: 2.6 / 9 = ~0.29s spin-up (was 4 = 0.65s). Deliberately the longest ramp of the three so enemies keep visible mass
        agent.angularSpeed = 200f;       // Snap pass: 180-deg pivot in 0.90s (was 90 = 2.00s). Still twice as slow to turn as a warrior -- that gap is where the lumbering read lives
        agent.stoppingDistance = 0.5f;  // Minimal — EnemyAttackExecutor uses ClosestPoint edge-distance to trip attack state
        agent.autoBraking = true;
        agent.radius = 0.5f;             // Agent size for collision
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;  // Reduced from High for performance
        agent.avoidancePriority = Random.Range(30, 70);  // Randomized priority to prevent synchronized yielding

        // Setup Health component
        SetupHealth(maxHealth, Die);

        // Create floating state text
        // 1.4, not 2: root scale went 0.45/0.7/0.45 -> 1 when the art moved to a Model child,
        // so the text child no longer inherits the squash. 2 * 0.7 preserves on-screen size.
        CreateStateText(1.4f, "Searching", Color.red);

        // Setup 3D spatial audio for combat sounds
        SetupCombatAudio(0.45f);

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
        var stuckResolver = CreateStuckResolver();
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
            cachedSpawner = FindAnyObjectByType<EnemySpawner>();
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
        string displayName = StateDisplayName("Searching");

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

    void PlayAttackSound()
    {
        if (AudioManager.Instance != null)
        {
            PlayCombatClip(AudioManager.Instance.enemyAttackSound, 0.6f);
        }
    }

    void PlayDeathSound()
    {
        if (AudioManager.Instance != null)
        {
            PlayCombatClip(AudioManager.Instance.enemyDeathSound, 1f);
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

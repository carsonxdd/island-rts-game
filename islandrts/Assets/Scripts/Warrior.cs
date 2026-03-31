using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class Warrior : MonoBehaviour
{
    public static IReadOnlyList<Warrior> ActiveList => ActiveRegistry<Warrior>.List;

    // Static event: fires when any warrior dies (with death position for proximity checks)
    public static event System.Action<Vector3> OnAnyWarriorDied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { OnAnyWarriorDied = null; }

    void Awake() { ActiveRegistry<Warrior>.Register(this); }

    [Header("Stats")]
    public float maxHealth = 75f;
    public float damage = 15f;
    public float attackRange = 4.5f;  // Increased for better reach
    public float attackCooldown = 1.2f;

    [Header("Movement")]
    public float moveSpeed = 3.5f;  // Faster than enemies, slower than workers

    [Header("Behavior")]
    public float searchRadius = 50f;  // How far to search for enemies
    public float patrolRadius = 8f;  // Patrol radius around campfire when idle

    [Header("State Display")]
    public bool showStateText = true;
    public float textHeightOffset = 2.5f;

    [Header("References")]
    public BaseBuilding baseBuilding;  // Reference to campfire

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
            floatingText = gameObject.AddComponent<FloatingText>();
            floatingText.heightOffset = textHeightOffset;
            floatingText.fontSize = 2f;
            floatingText.initialText = "Initializing...";
            floatingText.initialColor = Color.blue;
        }

        // Setup 3D spatial audio for combat sounds
        SetupCombatAudioSource();

        Debug.Log($"Warrior: Spawned with {maxHealth} health at {transform.position}");

        // Stagger initial AI start so not all warriors path at the same time
        float startDelay = Random.Range(0.5f, 2f);
        Invoke(nameof(InitializeUtilityAI), startDelay);
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
        stuckResolver.Initialize(agent, ActiveRegistry<Warrior>.IndexOf(this));
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

            // Retreat — low health + enemies nearby, fall back to base
            // ThreatNearby yShift=0 so Retreat zeroes out when enemies are gone
            new ActionOption("Retreat", new Consideration[]
            {
                new HealthPercent(ResponseCurve.InverseLinear(1.5f, -0.2f)),
                new ThreatNearby(2f, ResponseCurve.Linear(0.8f, 0f))  // 0 enemies = 0 score, clean exit
            }, new RetreatExecutor(), basePriority: 0.7f, momentumBonus: 0.15f),

            // Heal at Campfire — any damage + no enemies alive, regen 5 HP/sec at campfire
            // EnemyPresence inverted: 1.0 when no enemies, 0 when enemies exist
            // HealthPercent InverseLinear(2.0, 0.0):
            //   100% HP → 0.0 → early-out, exits immediately (no momentum to fight)
            //   90% HP → 0.2 → Heal = 0.9 * 0.2 = 0.18 (borderline vs Patrol 0.3)
            //   80% HP → 0.4 → Heal = 0.9 * 0.4 = 0.36 (beats Patrol 0.3)
            //   50% HP → 1.0 → Heal = 0.9 * 1.0 = 0.9 (strong heal)
            // Zero momentum ensures clean exit at full HP — no stickiness
            new ActionOption("Heal", new Consideration[]
            {
                new EnemyPresence(0f, ResponseCurve.InverseLinear(1f, 0f)),   // No enemies = 1.0, any enemies = 0
                new HealthPercent(ResponseCurve.InverseLinear(2f, 0f))        // 100%→0 (exits), 80%→0.4, 50%→1.0
            }, new HealAtCampfireExecutor(), basePriority: 0.9f, momentumBonus: 0f)
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
        // AIBrain drives behavior, we just update visuals
        if (showStateText && floatingText != null)
        {
            UpdateStateText();
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
        if (aiBrain != null && transform != null
            && Vector3.Distance(transform.position, deathPos) < 25f)
        {
            aiBrain.ForceReeval();
        }
    }

    void OnWallDestroyedUtilityAI()
    {
        if (aiBrain != null)
            aiBrain.ForceReeval();
    }

    // --- Public sound methods for Utility AI executors ---
    public void PlayAttackSoundPublic() { PlayAttackSound(); }
    public void PlayDeathSoundPublic() { PlayDeathSound(); }

    // --- State text ---

    void UpdateStateText()
    {
        string displayName = aiBrain != null && aiBrain.blackboard != null ? aiBrain.blackboard.stateDisplayName : "Initializing...";
        if (displayName == null) displayName = "Initializing...";

        // Color based on action
        Color color;
        if (displayName.Contains("Attacking") && displayName.Contains("Tower Buff"))
            color = new Color(1f, 0.8f, 0f);
        else if (displayName.Contains("Attacking"))
            color = Color.red;
        else if (displayName.Contains("Engaging") || displayName.Contains("defeated"))
            color = Color.yellow;
        else if (displayName.Contains("Defending") || displayName.Contains("Guarding"))
            color = new Color(0.3f, 0.8f, 1f);
        else if (displayName.Contains("Intercepting"))
            color = new Color(1f, 0.6f, 0f);  // Orange for intercept/rally
        else if (displayName.Contains("Retreating"))
            color = new Color(1f, 0.4f, 0.4f);
        else if (displayName.Contains("Healing"))
            color = new Color(0.4f, 1f, 0.4f);  // Green for healing
        else if (displayName.Contains("Patrolling"))
            color = Color.cyan;
        else
            color = new Color(0.5f, 0.5f, 1f);

        floatingText.SetText(displayName, color);
    }

    void OnDestroy()
    {
        ActiveRegistry<Warrior>.Unregister(this);

        // Unsubscribe from static events to prevent memory leaks
        OnAnyWarriorDied -= OnAllyDiedUtilityAI;
        Wall.OnAnyWallDestroyed -= OnWallDestroyedUtilityAI;
        Gate.OnAnyGateDestroyed -= OnWallDestroyedUtilityAI;
    }

    // --- Audio ---

    void SetupCombatAudioSource()
    {
        combatAudioSource = AudioHelper.CreateSpatialAudioSource(gameObject, 0.5f, 8f, 35f);
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

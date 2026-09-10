using UnityEngine;
using UnityEngine.AI;

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
    protected override Faction DefaultFaction => Factions.Raiders;

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

    /// <summary>The fog hider on this raider (2026-09-09): shown only while something of the colony's sees it.</summary>
    [System.NonSerialized] public FogVisibility fog;

    void Start()
    {
        // Raiders are hidden until seen. Attached before anything else so the very first
        // check runs this frame and a fresh landing never flashes on screen.
        fog = FogVisibility.Attach(gameObject, FogVisibility.Rule.Visible);

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
        bb.faction = Faction;
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

    /// <summary>
    /// Walking toward a wall or gate cell (its agent destination lands on one).
    /// The warrior scan treats such an enemy as half as far away, and the
    /// Defensive stance counts it as the colony's business wherever it stands.
    /// </summary>
    public bool IsHeadingForWall()
    {
        if (agent == null || !agent.hasPath || WallGrid.Instance == null) return false;
        return WallGrid.Instance.HasWallAt(WallGrid.Instance.WorldToGrid(agent.destination));
    }

    // --- Attack slots (2026-09-07) ---
    // Six bearings around this enemy, 60 degrees apart, that warriors claim so
    // they come at it from different sides on different paths instead of
    // queueing on one line. A slot is free when its owner is gone, dead, or no
    // longer targeting this enemy — so a warrior that switches, retreats or
    // dies without releasing frees its slot on the next claim, and there is
    // nothing to leak. With every slot taken the nearest one is shared: an
    // outnumbered raider still gets mobbed, it just gets mobbed from six sides.

    public const int AttackSlotCount = 6;
    private Warrior[] slotOwners;   // allocated on first claim; most enemies never get one

    /// <summary>
    /// The slot this warrior should attack from: the one it already holds, else
    /// the free bearing closest to its own line of approach, else the closest
    /// bearing regardless. Returns the slot index for <see cref="AttackSlotPoint"/>.
    /// </summary>
    public int ClaimAttackSlot(Warrior warrior, Vector3 from)
    {
        if (slotOwners == null) slotOwners = new Warrior[AttackSlotCount];

        for (int i = 0; i < AttackSlotCount; i++)
            if (slotOwners[i] == warrior) return i;

        Vector3 dir = from - transform.position;
        dir.y = 0f;
        float wanted = dir.sqrMagnitude > 0.001f ? Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg : 0f;

        int bestFree = -1, bestAny = -1;
        float bestFreeDelta = float.MaxValue, bestAnyDelta = float.MaxValue;
        for (int i = 0; i < AttackSlotCount; i++)
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(wanted, SlotAngle(i)));
            if (delta < bestAnyDelta) { bestAnyDelta = delta; bestAny = i; }
            if (SlotFree(i) && delta < bestFreeDelta) { bestFreeDelta = delta; bestFree = i; }
        }

        int slot = bestFree >= 0 ? bestFree : bestAny;
        if (bestFree >= 0)
        {
            slotOwners[slot] = warrior;
            int held = 0;   // playtest: three warriors on one raider from three bearings
            for (int i = 0; i < AttackSlotCount; i++) if (slotOwners[i] != null) held++;
            if (held >= 3) DevQuests.Signal("attack_slots:3");
        }
        return slot;
    }

    /// <summary>Give the slot back (target switch). Not required for correctness, see above.</summary>
    public void ReleaseAttackSlot(Warrior warrior)
    {
        if (slotOwners == null) return;
        for (int i = 0; i < AttackSlotCount; i++)
            if (slotOwners[i] == warrior) slotOwners[i] = null;
    }

    /// <summary>World point <paramref name="radius"/> out from this enemy along the slot's bearing.</summary>
    public Vector3 AttackSlotPoint(int slot, float radius)
    {
        float a = SlotAngle(slot) * Mathf.Deg2Rad;
        return transform.position + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
    }

    static float SlotAngle(int slot) => slot * (360f / AttackSlotCount);

    bool SlotFree(int i)
    {
        Warrior owner = slotOwners[i];
        if (owner == null) return true;                       // destroyed, or never claimed
        Health h = owner.CachedHealth;
        if (h == null || !h.IsAlive) return true;
        return owner.CurrentTarget != transform;              // moved on without releasing
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

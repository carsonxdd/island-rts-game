using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Per-unit data cache that stores references and scratch data for AI evaluation.
/// Created once per unit (zero GC per frame). Considerations and Executors read from this.
/// </summary>
public class AIBlackboard
{
    // --- Core references (set once) ---
    public Transform transform;
    public NavMeshAgent agent;
    public Health health;
    public BaseBuilding baseBuilding;

    // --- Unit-type-specific references (set once by the unit's setup) ---

    // Worker fields
    public Worker worker;
    public ResourceNode.ResourceType assignedResourceType;
    public float carryCapacity;
    public float gatherDistance;
    public float deliveryDistance;
    public float searchRadius;
    public float gatherRatePerSecond;

    // Warrior fields
    public Warrior warrior;
    public float attackRange;
    public float attackCooldown;
    public float damage;
    public float warriorSearchRadius;
    public float patrolRadius;

    // Enemy fields
    public Enemy enemy;
    public float warriorDetectionRange;

    // --- Per-evaluation cached data (updated by AIBrain before scoring) ---

    // Carry amount (workers)
    public float carryAmount;

    // Current target (shared across executors)
    public Transform currentTarget;
    public Health currentTargetHealth;
    public string currentTargetName;
    public Collider currentTargetCollider;  // Cached for ClosestPoint edge-distance checks (Phase 6.21)

    // Resource node (workers)
    public ResourceNode targetResource;
    public bool isRegisteredAtNode;

    // Combat state
    public float lastAttackTime;
    public bool isInAttackRange;

    // Enemy gate-trigger override: Gate.OnTriggerEnter calls Enemy.ForceAttackGate,
    // which stamps this forcedTarget with a short expiry. EnemyAttackExecutor's
    // PickTarget honors it before the normal priority scan. Gates don't carve the
    // NavMesh, so without this hint enemies walk past live gates.
    public Transform forcedTarget;
    public float forcedTargetExpiry;

    // Nearest enemy cache (refreshed by AIBrain)
    public Transform nearestEnemy;
    public float nearestEnemyDistance;
    public int nearbyEnemyCount;

    // Nearest resource cache (refreshed periodically)
    public ResourceNode bestResource;
    public float bestResourceScore;

    // Wall-under-attack cache
    public Transform wallUnderAttack;
    public float wallUnderAttackDistance;

    // Stuck resolution
    public StuckResolver stuckResolver;

    // Brain reference (for executors to call ForceReeval)
    public AIBrain brain;

    // State text (for display)
    public string stateDisplayName;
}

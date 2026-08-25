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

    // Nearest enemy cache (refreshed by the EnemyPresence consideration)
    public Transform nearestEnemy;
    public float nearestEnemyDistance;

    // Frame-stamped full enemy scan (see EnemyPresence). Warriors evaluate up to
    // four EnemyPresence instances per brain tick — this lets them share one scan.
    public int enemyScanFrame = -1;
    public Transform scannedNearestEnemy;
    public float scannedNearestEnemyDist = float.MaxValue;

    // Nearest resource cache (refreshed periodically)
    public ResourceNode bestResource;

    // Wall-under-attack cache
    public Transform wallUnderAttack;

    // --- Unreachable-node memory (workers) ---
    // Nodes a worker failed to path to (walled off, NavMesh island). ResourceAvailability
    // skips these until the entry expires, so the worker picks a different node instead of
    // marching into the same wall forever. Fixed-size ring - zero GC.
    public readonly ResourceNode[] unreachableNodes = new ResourceNode[4];
    public readonly float[] unreachableNodeExpiry = new float[4];
    private int unreachableRing;

    public void MarkNodeUnreachable(ResourceNode node, float duration = 15f)
    {
        if (node == null) return;
        unreachableNodes[unreachableRing] = node;
        unreachableNodeExpiry[unreachableRing] = Time.time + duration;
        unreachableRing = (unreachableRing + 1) % unreachableNodes.Length;
    }

    public bool IsNodeUnreachable(ResourceNode node)
    {
        if (node == null) return false;
        for (int i = 0; i < unreachableNodes.Length; i++)
        {
            if (unreachableNodes[i] == node && Time.time < unreachableNodeExpiry[i])
                return true;
        }
        return false;
    }

    // Stuck resolution
    public StuckResolver stuckResolver;

    // Brain reference (for executors to call ForceReeval)
    public AIBrain brain;

    // State text (for display)
    public string stateDisplayName;
}

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
    // False for an idle colonist (the builders). Gather/Pickup score 0 without a job;
    // Build/Repair score 0 with one. Kept in sync by Worker.SetJob / ClearJob.
    public bool hasJob;
    // What is actually in the worker's hands. Normally the assigned type, but a job
    // change mid-trip must still deliver what was picked up under the old job.
    public ResourceNode.ResourceType carryType;
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

    // Nearest matching ground pickup (refreshed by PickupAvailability)
    public GroundPickup bestPickup;

    // Nearest construction site with a free builder slot (refreshed by ConstructionAvailable)
    public ConstructionSite bestSite;

    // Nearest damaged, affordable building (refreshed by RepairAvailable)
    public Transform bestRepair;
    public Health bestRepairHealth;
    public BuildingType bestRepairType;

    // Wall-under-attack cache
    public Transform wallUnderAttack;

    // --- Unreachable-node memory (workers) ---
    // Nodes a worker failed to path to (walled off, NavMesh island). ResourceAvailability
    // skips these until the entry expires, so the worker picks a different node instead of
    // marching into the same wall forever. Fixed-size ring - zero GC.
    public readonly ResourceNode[] unreachableNodes = new ResourceNode[4];
    public readonly float[] unreachableNodeExpiry = new float[4];
    private int unreachableRing;

    /// <summary>Remember that this worker could not path to <paramref name="node"/>, so
    /// node selection skips it for <paramref name="duration"/> seconds.</summary>
    public void MarkNodeUnreachable(ResourceNode node, float duration = 15f)
    {
        if (node == null) return;
        unreachableNodes[unreachableRing] = node;
        unreachableNodeExpiry[unreachableRing] = Time.time + duration;
        unreachableRing = (unreachableRing + 1) % unreachableNodes.Length;
    }

    /// <summary>True while a MarkNodeUnreachable entry for this node is still unexpired.</summary>
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

    // --- Shared target bookkeeping (Phase 6.25) ---
    // One implementation of set/clear/alive-check for every executor. Executors
    // decide what extra state to reset when SetTarget reports a change.

    /// <summary>
    /// Point currentTarget at t, caching its Health and Collider for alive and
    /// edge-distance checks. Returns true only if the target actually changed.
    /// Passing null clears the target (prefer ClearTarget for readability).
    /// </summary>
    public bool SetTarget(Transform t, string name)
    {
        if (currentTarget == t) return false;
        currentTarget = t;
        currentTargetName = name;
        if (t != null)
        {
            currentTargetHealth = t.GetComponent<Health>();
            currentTargetCollider = t.GetComponent<Collider>();
        }
        else
        {
            currentTargetHealth = null;
            currentTargetCollider = null;
        }
        return true;
    }

    /// <summary>Drop the current target and every cached reference derived from it.</summary>
    public void ClearTarget()
    {
        currentTarget = null;
        currentTargetHealth = null;
        currentTargetName = null;
        currentTargetCollider = null;
        isInAttackRange = false;
    }

    /// <summary>
    /// Robust alive check handling Unity destroyed-object null semantics:
    /// re-fetches Health if the cached reference is gone (never "return true on
    /// null" — see gotchas). A target with no Health component is treated as dead.
    /// </summary>
    public bool IsTargetAlive()
    {
        if (currentTarget == null) return false;
        if (currentTargetHealth == null)
        {
            currentTargetHealth = currentTarget.GetComponent<Health>();
            if (currentTargetHealth == null) return false;
        }
        return currentTargetHealth.IsAlive;
    }

    /// <summary>
    /// Distance from this unit to currentTarget's collider edge (center distance
    /// if no collider was cached; float.MaxValue with no target so range checks
    /// read "out of range" instead of throwing).
    /// </summary>
    public float TargetEdgeDistance()
    {
        if (currentTarget == null) return float.MaxValue;
        return TargetingUtil.EdgeDistance(transform.position, currentTarget, currentTargetCollider);
    }

    // Stuck resolution
    public StuckResolver stuckResolver;

    // Brain reference (for executors to call ForceReeval)
    public AIBrain brain;

    // State text (for display)
    public string stateDisplayName;
}

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
    /// <summary>Where the current Return trip hands in (2026-09-16): the nearest drop-off, the fire or a Storehouse. Set by ReturnToBaseExecutor, null outside it.</summary>
    public IDropoff dropoff;
    /// <summary>
    /// Who this unit fights for (lap step 1): the unit's own <c>Faction</c>, copied
    /// in its Start. Considerations and executors read this, never <c>Factions.Player</c>.
    /// </summary>
    public Faction faction;

    // --- Unit-type-specific references (set once by the unit's setup) ---

    // Worker fields
    public Worker worker;
    public ResourceNode.ResourceType assignedResourceType;
    // False for a jobless colonist (the utility labor). Gather/Pickup score 0 without
    // a job; Build/Craft/Repair/Forage score 0 with one. Kept in sync by Worker.SetJob / ClearJob.
    public bool hasJob;
    // What a jobless colonist may do (2026-09-07): Any = everything, else one trade.
    // Read by SpecialtyAllows. Kept in sync by Worker.SetSpecialty / SetJob / ClearJob.
    public Worker.Specialty specialty;
    // The bench a colonist is walking to or standing at (refreshed by StationWorkAvailable).
    public CraftStation targetStation;
    // A starving colonist walking out on the colony (2026-09-04). Set once by
    // Worker.Leave; the Leave action outranks everything and ends in Destroy.
    public bool leaving;
    // A job change owes the campfire a visit (2026-09-07). Set by Worker.OnJobChanged,
    // read by IsGearingUp, cleared by GearUpExecutor once the colonist has delivered
    // and stood the gear-up beat.
    public bool gearingUp;
    // What is actually in the worker's hands. Normally the assigned type, but a job
    // change mid-trip must still deliver what was picked up under the old job.
    public ResourceNode.ResourceType carryType;
    public float carryCapacity;
    public float gatherDistance;
    public float deliveryDistance;
    public float searchRadius;
    public float gatherRatePerSecond;
    // Sleep (2026-09-16): the hours this colonist keeps, copied from their Persona in
    // Worker.InitializeUtilityAI. SleepUrge reads the clock against them; IdleExecutor
    // scales its standing time by standScale. asleepInHut is set ONLY by SleepExecutor
    // while the body is garrisoned in a hut: a sleeper indoors ignores raiders outside.
    public float bedtime;
    public float wakeTime = 0.25f;
    public float standScale = 1f;
    public bool asleepInHut;
    // Levied (2026-09-16): a colonist with a claim on a spare weapon in the stockpile,
    // or the warrior body they stand in once mustered. Mirrored from Worker.levied /
    // Warrior.levied; MusterCall and StandDownDue gate on it beside the faction's alarm.
    public bool levied;

    // Warrior fields
    public Warrior warrior;
    // The post this warrior holds under Defensive (2026-09-17): its slot in the
    // colony's line, written by InterceptExecutor each time it places the rally and
    // read by GuardStance.Allows — a raider is fought when it comes within reach of
    // the post or gets behind it, never chased across the island. holdLineRadius is
    // the held gate's distance from the fire (float.MaxValue with no gate line): a
    // spearman refuses anything outside the wall, an archer shoots over it.
    public Vector3 holdPost;
    public bool hasHoldPost;
    public float holdLineRadius = float.MaxValue;
    // Armed with a bow (2026-09-04): Engage looses an arrow instead of striking. Set by Warrior.ApplyWeapon.
    public bool isRanged;
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

    // The MATERIAL a hauled pickup was made of, carried alongside the resource
    // (2026-09-03). A stick a colonist brings home is worth its wood in the pool
    // AND one Stick in the campfire stockpile — without the second half, nothing
    // colonists gathered could ever pay for research or a spear, which is exactly
    // what the stockpile is for. Set by GroundPickup.Collect, banked and cleared
    // by ReturnToBaseExecutor. Node gathering never sets it.
    public ItemDef carryItem;
    public int carryItemCount;

    // Current target (shared across executors)
    public Transform currentTarget;
    public Health currentTargetHealth;
    public string currentTargetName;
    public Collider currentTargetCollider;  // Cached for ClosestPoint edge-distance checks (Phase 6.21)
    public Faction currentTargetFaction;    // The target's owner (commit 5): the hit sites refuse a non-hostile

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

    // --- Unreachable-pickup memory (workers, 2026-09-09) ---
    // Same ring for ground pickups. Before it existed, a pickup the agent could not get
    // within reach of (inside a bush's carve, on a ledge lip) was dropped by the stuck
    // reset and re-acquired the next tick, forever: the "metre forward, metre back" forage
    // stutter. Both pickup considerations skip these until they expire.
    public readonly GroundPickup[] unreachablePickups = new GroundPickup[4];
    public readonly float[] unreachablePickupExpiry = new float[4];
    private int unreachablePickupRing;

    public void MarkPickupUnreachable(GroundPickup pickup, float duration = 15f)
    {
        if (pickup == null) return;
        unreachablePickups[unreachablePickupRing] = pickup;
        unreachablePickupExpiry[unreachablePickupRing] = Time.time + duration;
        unreachablePickupRing = (unreachablePickupRing + 1) % unreachablePickups.Length;
    }

    public bool IsPickupUnreachable(GroundPickup pickup)
    {
        if (pickup == null) return false;
        for (int i = 0; i < unreachablePickups.Length; i++)
        {
            if (unreachablePickups[i] == pickup && Time.time < unreachablePickupExpiry[i])
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
            IOwned owned = t.GetComponent<IOwned>();
            currentTargetFaction = owned != null ? owned.Faction : null;
        }
        else
        {
            currentTargetHealth = null;
            currentTargetCollider = null;
            currentTargetFaction = null;
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
        currentTargetFaction = null;
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

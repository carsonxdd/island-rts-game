using UnityEngine;

/// <summary>
/// Colonist executor: walk to a damaged building and restore its HP, paying a
/// fraction of the build cost as it goes (<see cref="RepairCosts"/>). Repair
/// pauses, never cancels, while the pool cannot cover the next whole unit.
/// </summary>
/// <remarks>
/// Same shape as <see cref="BuildExecutor"/>: carve-safe approach point, edge-distance
/// arrival, stationary avoidance while working, rubber band if shoved away. Cost is
/// accrued as fractional debt per resource and charged one whole unit at a time, so a
/// wall that costs 15 wood is repaired for about 4 wood in total, one unit every few
/// seconds rather than all up front.
/// </remarks>
public class RepairExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Repairing";

    private const float WorkDistance = 1.2f;
    private const float DriftSlack = 0.8f;

    private Transform target;
    private Health health;
    private Collider targetCollider;
    private RepairCosts.PerHp cost;
    private bool hasCost;
    private float debtWood, debtFood, debtStone;
    private bool working;
    private bool destinationQueued;

    public override void OnEnter(AIBlackboard bb)
    {
        if (bb.targetResource != null)
        {
            bb.targetResource.UnclaimNode(bb.worker);
            if (bb.isRegisteredAtNode)
            {
                bb.targetResource.UnregisterWorker(bb.worker);
                bb.isRegisteredAtNode = false;
            }
        }
        bb.worker.StopGatheringSoundPublic();

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        target = null;
        health = null;
        working = false;
        destinationQueued = false;
        debtWood = debtFood = debtStone = 0f;
        displayName = "Heading to repair";
        Worker.RollMovingAvoidance(bb.agent);
        Acquire(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (target == null || health == null || !health.IsAlive || IsRepaired())
        {
            target = null;
            working = false;
            Acquire(bb);
            if (target == null)
            {
                if (bb.brain != null) bb.brain.ForceReeval();
                return;
            }
        }

        if (!working)
        {
            if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            {
                target = null;
                return;
            }

            if (!destinationQueued) IssueMove(bb);

            float edge = TargetingUtil.EdgeDistance(bb.transform.position, target, targetCollider);
            if (edge <= WorkDistance) StartWorking(bb);
            return;
        }

        float dist = TargetingUtil.EdgeDistance(bb.transform.position, target, targetCollider);
        if (dist > WorkDistance + DriftSlack)
        {
            working = false;
            displayName = "Heading to repair";
            Worker.RollMovingAvoidance(bb.agent);
            IssueMove(bb);
            return;
        }

        Work(Time.deltaTime);
    }

    bool IsRepaired() => health != null && health.currentHealth >= health.maxHealth - 0.01f;

    void Acquire(AIBlackboard bb)
    {
        // Refreshed by RepairAvailable on every brain evaluation
        if (bb.bestRepair == null || bb.bestRepairHealth == null) return;

        target = bb.bestRepair;
        health = bb.bestRepairHealth;
        targetCollider = target.GetComponent<Collider>();
        hasCost = RepairCosts.TryGetPerHp(bb.bestRepairType, health.maxHealth, out cost);
        debtWood = debtFood = debtStone = 0f;
        destinationQueued = false;
        IssueMove(bb);
    }

    void IssueMove(AIBlackboard bb)
    {
        if (target == null) return;
        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;

        Vector3 approach = TargetingUtil.GetApproachPoint(bb.transform.position, target, targetCollider);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, approach);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void StartWorking(AIBlackboard bb)
    {
        if (bb.agent != null && bb.agent.isOnNavMesh) bb.agent.ResetPath();
        Worker.SetStationaryAvoidance(bb.agent);
        working = true;
        displayName = "Repairing";
        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();
    }

    /// <summary>One tick of repair: charge what this tick's HP costs, then heal. Pauses when unaffordable.</summary>
    void Work(float dt)
    {
        float missing = health.maxHealth - health.currentHealth;
        float hp = Mathf.Min(RepairCosts.RepairRate * dt, missing);
        if (hp <= 0f) return;

        if (hasCost && cost.Any)
        {
            float newWood = debtWood + cost.wood * hp;
            float newFood = debtFood + cost.food * hp;
            float newStone = debtStone + cost.stone * hp;
            int dueWood = Mathf.FloorToInt(newWood);
            int dueFood = Mathf.FloorToInt(newFood);
            int dueStone = Mathf.FloorToInt(newStone);

            if (dueWood > 0 || dueFood > 0 || dueStone > 0)
            {
                ResourceManager rm = ResourceManager.Instance;
                if (rm == null || !rm.SpendResources(dueWood, dueFood, dueStone))
                {
                    displayName = "Repairing (no materials)";
                    return;   // pause — the debt is not committed, so nothing was lost
                }
                newWood -= dueWood;
                newFood -= dueFood;
                newStone -= dueStone;
            }

            debtWood = newWood;
            debtFood = newFood;
            debtStone = newStone;
            displayName = "Repairing";
        }

        health.Heal(hp);
    }

    public override void OnExit(AIBlackboard bb)
    {
        target = null;
        health = null;
        targetCollider = null;
        working = false;
        destinationQueued = false;

        if (bb.agent != null && bb.agent.isOnNavMesh) bb.agent.isStopped = false;
        Worker.RollMovingAvoidance(bb.agent);
    }
}

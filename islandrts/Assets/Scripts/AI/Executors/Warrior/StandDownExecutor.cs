using UnityEngine;

/// <summary>
/// Warrior executor: a mustered levy stands down once the alarm has been quiet
/// (2026-09-16). Walks to the fire's edge, where the campfire puts the weapon back
/// in the stockpile and hands back the job they had
/// (<see cref="BaseBuilding.StandDownLevy"/>). 0.8 with zero momentum: below
/// Engage and a wounded warrior's Heal, above Patrol and Rearm, and the gate
/// (<see cref="StandDownDue"/>) is 0 for every full-time warrior.
/// </summary>
/// <remarks>
/// Any executor walking to a point swaps <c>stoppingDistance</c> to 0.5 and restores
/// it (the default <c>attackRange − 1</c> parks an archer a reach short). A 20 s cap
/// stands them down where they are rather than never. The swap destroys this body,
/// so OnExit after it touches nothing.
/// </remarks>
public class StandDownExecutor : ActionExecutor
{
    public override string DisplayName => "Standing down";

    private const float ArriveEdge = 1.5f;
    private const float MaxWalkSeconds = 20f;

    private Transform rack;
    private Collider rackCollider;
    private bool destinationQueued;
    private bool done;
    private float walkTimer;
    private float savedStoppingDistance;

    public override void OnEnter(AIBlackboard bb)
    {
        done = false;
        destinationQueued = false;
        walkTimer = 0f;
        bb.ClearTarget();
        FormationSlots.Release(bb.warrior);   // the rank passes on while we walk home

        if (bb.baseBuilding != null) { rack = bb.baseBuilding.transform; rackCollider = bb.baseBuilding.GetComponent<Collider>(); }
        else { rack = null; rackCollider = null; }

        if (!AgentReady(bb) || rack == null || RackEdge(bb) <= ArriveEdge)
        {
            Disarm(bb);
            return;
        }

        savedStoppingDistance = bb.agent.stoppingDistance;
        bb.agent.stoppingDistance = 0.5f;
        UnitSpacing.SetMoving(bb.agent, carrying: false);
        IssueMove(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (done) return;
        if (!AgentReady(bb) || rack == null)
        {
            Disarm(bb);
            return;
        }

        walkTimer += Time.deltaTime;

        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            destinationQueued = false;
            return;
        }
        if (!destinationQueued) IssueMove(bb);

        bool pathDone = destinationQueued && !bb.agent.pathPending && !bb.agent.hasPath;
        bool stopped = bb.agent.velocity.sqrMagnitude < 0.05f;
        float edge = RackEdge(bb);
        if (edge <= ArriveEdge || ((pathDone || stopped) && edge <= ArriveEdge + 1.5f) || walkTimer > MaxWalkSeconds)
            Disarm(bb);
    }

    void IssueMove(AIBlackboard bb)
    {
        if (!AgentReady(bb) || rack == null) return;
        Vector3 dest = TargetingUtil.GetApproachPoint(bb.transform.position, rack, rackCollider);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, dest);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    float RackEdge(AIBlackboard bb)
        => rack != null ? TargetingUtil.EdgeDistance(bb.transform.position, rack, rackCollider) : 0f;

    /// <summary>At the fire: weapon back in the stockpile, back to the job. Ends this body.</summary>
    void Disarm(AIBlackboard bb)
    {
        if (done) return;
        done = true;
        BaseBuilding fire = bb.warrior != null && bb.warrior.baseBuilding != null ? bb.warrior.baseBuilding : (bb.faction != null ? bb.faction.Campfire : null);
        if (fire == null || fire.StandDownLevy(bb.warrior) == null) done = false;   // could not swap: try again next tick
    }

    static bool AgentReady(AIBlackboard bb)
        => bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh;

    public override void OnExit(AIBlackboard bb)
    {
        if (done) return;   // the body is being destroyed; nothing to restore
        rack = null;
        rackCollider = null;
        if (AgentReady(bb))
        {
            bb.agent.stoppingDistance = savedStoppingDistance > 0f ? savedStoppingDistance : bb.attackRange - 1f;
            bb.agent.isStopped = false;
        }
    }
}

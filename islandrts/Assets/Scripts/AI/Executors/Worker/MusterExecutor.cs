using UnityEngine;

/// <summary>
/// Worker executor: a levied colonist answers the alarm (2026-09-16). They drop the
/// node claim, walk HOME — their own hut, or the fire for anyone homed to it or
/// homeless (2026-09-17: the weapons are kept in the homes, the stockpile is only
/// the count) — and at its edge the campfire hands them a spare weapon and swaps
/// them into a warrior body (<see cref="BaseBuilding.MusterLevy"/>). Above Flee
/// (1.2 + momentum) and Sleep (1.45) so a raid arms them instead of hiding them.
/// </summary>
/// <remarks>
/// The load in hand is banked through <see cref="ReturnToBaseExecutor.Deliver"/>
/// before the swap: losing five wood to a body swap reads as a bug, a levy stacking
/// it at the fire does not. The walk claims a drop-off slot like any delivery, with
/// the same edge rule and a 20 s cap — past it they arm where they stand rather than
/// never at all. A claim the campfire cannot honour (the rack went bare on the way,
/// a recruit took the last spear) is dropped here so the ladder gets them back;
/// the militia hands out a fresh one next tick if the stock allows. The swap
/// destroys this body, so OnExit after it touches nothing.
/// </remarks>
public class MusterExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "To arms!";

    private const float ArriveEdge = 1.5f;        // at the fire's edge
    private const float MaxWalkSeconds = 20f;     // then arm where we stand

    private Transform rack;           // where this colonist arms: the home hut, else the fire
    private Collider rackCollider;
    private bool rackIsFire;
    private bool destinationQueued;
    private bool done;
    private float walkTimer;

    public override void OnEnter(AIBlackboard bb)
    {
        done = false;
        destinationQueued = false;
        walkTimer = 0f;
        displayName = "To arms!";

        if (bb.targetResource != null)
        {
            bb.targetResource.UnclaimNode(bb.worker);
            if (bb.isRegisteredAtNode)
            {
                bb.targetResource.UnregisterWorker(bb.worker);
                bb.isRegisteredAtNode = false;
            }
            bb.targetResource = null;
        }
        bb.worker.StopGatheringSoundPublic();
        if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();

        PickRack(bb);

        if (!AgentReady(bb) || rack == null || RackEdge(bb) <= ArriveEdge)
        {
            Arm(bb);
            return;
        }

        bb.agent.stoppingDistance = 0.5f;
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        IssueMove(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (done) return;
        if (!AgentReady(bb) || rack == null)
        {
            Arm(bb);
            return;
        }

        walkTimer += Time.deltaTime;

        // A stuck reset nulls blackboard fields and ForceReevals — bail for this tick, re-issue next
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
            Arm(bb);
    }

    /// <summary>The home hut when they have one that stands, else the fire (2026-09-17).</summary>
    void PickRack(AIBlackboard bb)
    {
        Hut home = bb.faction != null && bb.faction.Population != null ? bb.faction.Population.HomeOf(bb.worker) as Hut : null;
        if (home != null && home.CachedHealth != null && home.CachedHealth.IsAlive)
        {
            rack = home.transform;
            rackCollider = home.GetComponent<Collider>();
            rackIsFire = false;
            return;
        }
        rack = bb.baseBuilding != null ? bb.baseBuilding.transform : null;
        rackCollider = bb.baseBuilding != null ? bb.baseBuilding.GetComponent<Collider>() : null;
        rackIsFire = true;
    }

    void IssueMove(AIBlackboard bb)
    {
        if (!AgentReady(bb) || rack == null) return;
        // The fire's edge is shared with every delivery, so that walk claims a slot; a hut's edge is ours
        Vector3 dest = rackIsFire
            ? bb.baseBuilding.DropoffPoint(bb.baseBuilding.ClaimDropoffSlot(bb.worker, bb.transform.position))
            : TargetingUtil.GetApproachPoint(bb.transform.position, rack, rackCollider);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, dest);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    /// <summary>Distance to the rack's collider edge, or 0 with nowhere to walk to.</summary>
    float RackEdge(AIBlackboard bb)
        => rack != null ? TargetingUtil.EdgeDistance(bb.transform.position, rack, rackCollider) : 0f;

    /// <summary>At the rack: bank the load, take a weapon, stand in the warrior body. Ends this body.</summary>
    void Arm(AIBlackboard bb)
    {
        if (done) return;
        done = true;
        displayName = "Taking up arms";
        if (bb.baseBuilding != null) bb.baseBuilding.ReleaseDropoffSlot(bb.worker);
        ReturnToBaseExecutor.Deliver(bb);   // the load banked, not lost to the swap (from a hut too: invisible, and a levy is in a hurry)
        if (!rackIsFire && rack != null && bb.faction != null && bb.faction.IsPlayer) DevQuests.Signal("muster:home");
        BaseBuilding fire = bb.baseBuilding != null ? bb.baseBuilding : (bb.faction != null ? bb.faction.Campfire : null);
        if (fire == null) { done = false; return; }   // nowhere to take arms from — try again next tick
        if (fire.MusterLevy(bb.worker) == null)
        {
            // Nothing on the rack for us: drop the claim, back to the ladder
            done = false;
            bb.worker.SetLevied(false);
            return;
        }
        Militia.NoteMustered(bb.faction, !rackIsFire);
    }

    static bool AgentReady(AIBlackboard bb)
        => bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh;

    public override void OnExit(AIBlackboard bb)
    {
        if (done) return;   // the body is being destroyed; nothing to restore
        if (bb.baseBuilding != null) bb.baseBuilding.ReleaseDropoffSlot(bb.worker);
        else if (bb.worker != null) bb.worker.dropoffSlot = -1;
        rack = null;
        rackCollider = null;
        if (AgentReady(bb))
        {
            bb.agent.stoppingDistance = Worker.GatherStopDistance;
            bb.agent.isStopped = false;
            Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        }
    }
}

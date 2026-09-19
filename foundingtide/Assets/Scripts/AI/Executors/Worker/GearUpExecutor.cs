using UnityEngine;

/// <summary>
/// Worker executor: the trip home a job change costs (2026-09-07). The colonist
/// walks to the campfire's edge, hands in whatever they were carrying under the
/// old job, stands a moment "gearing up" for the new one, and only then goes out
/// as that unit. Runs for every change — job, specialty, or back to idle — and
/// from wherever they are: a colonist already beside the fire just takes the
/// pause. Above everything but Flee and Leave, and gated to 0 by ThreatNearby
/// so a raid still sends them to a hut first; the flag survives and the trip
/// resumes afterwards.
/// </summary>
/// <remarks>
/// Arrival reuses ReturnToBase's edge-distance checks (the campfire carves the
/// NavMesh, so the destination is a sampled approach point and "there" is
/// measured to the collider edge) and its fallbacks, so a colonist can never be
/// stranded on "Heading to the fire". Delivery goes through
/// <see cref="ReturnToBaseExecutor.Deliver"/>, the one place carried resources
/// and hauled materials are banked.
/// </remarks>
public class GearUpExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Heading to the fire";

    private const float PauseSeconds = 1.5f;   // the "picking up the gear" beat at the fire
    private const float GiveUpSeconds = 8f;    // ReturnToBase's nuclear fallback, same reason

    private bool arrived;
    private float walkTimer;
    private float pauseTimer;
    private Collider campfireCollider;

    public override void OnEnter(AIBlackboard bb)
    {
        arrived = false;
        walkTimer = 0f;
        pauseTimer = 0f;
        displayName = "Heading to the fire";
        campfireCollider = bb.baseBuilding != null ? bb.baseBuilding.GetComponent<Collider>() : null;

        // The old job's node claim goes now, not at the fire
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

        if (bb.baseBuilding == null || bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh)
        {
            Arrive(bb);   // nowhere to go — take the pause where we stand
            return;
        }

        bb.agent.stoppingDistance = 0.5f;
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        if (AINavHelper.TrySetDestination(bb.agent, ApproachPoint(bb)))
            bb.agent.isStopped = false;
        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (arrived)
        {
            pauseTimer += Time.deltaTime;
            if (pauseTimer >= PauseSeconds) Finish(bb);
            return;
        }

        if (bb.baseBuilding == null || bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh)
        {
            Arrive(bb);
            return;
        }

        walkTimer += Time.deltaTime;

        // A stuck reset nulls blackboard fields and ForceReevals — bail for this tick
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            return;

        float edge = TargetingUtil.EdgeDistance(bb.transform.position, bb.baseBuilding.transform, campfireCollider);
        bool withinRange = edge <= bb.deliveryDistance;
        bool pathDone = !bb.agent.pathPending && bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.5f;
        bool stopped = bb.agent.velocity.sqrMagnitude < 0.05f;
        bool nearEnough = edge <= bb.deliveryDistance + 1.5f && (pathDone || stopped);
        bool gaveUp = walkTimer > GiveUpSeconds;

        if (withinRange || nearEnough || gaveUp)
        {
            Arrive(bb);
            return;
        }

        if (!bb.agent.hasPath && !bb.agent.pathPending)
        {
            if (AINavHelper.TrySetDestination(bb.agent, ApproachPoint(bb)))
                bb.agent.isStopped = false;
        }
    }

    /// <summary>A claimed drop-off slot on the fire's edge (2026-09-08), so a gearing-up colonist never parks on the face the deliverers use.</summary>
    Vector3 ApproachPoint(AIBlackboard bb)
    {
        int slot = bb.baseBuilding.ClaimDropoffSlot(bb.worker, bb.transform.position);
        return bb.baseBuilding.DropoffPoint(slot);
    }

    /// <summary>At the fire: hand everything in and stand for the gear-up beat.</summary>
    void Arrive(AIBlackboard bb)
    {
        arrived = true;
        pauseTimer = 0f;
        DevQuests.Signal("gearup");
        DevQuests.Signal(bb.hasJob ? "gearup:job" : "gearup:idle");
        if (bb.carryAmount > 0.01f) DevQuests.Signal("gearup:delivered");   // the old trade's load lands in the pool first
        ReturnToBaseExecutor.Deliver(bb);
        displayName = "Gearing up: " + bb.worker.RoleTitle();
        if (bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh)
        {
            bb.agent.ResetPath();
            bb.agent.isStopped = true;
            Worker.SetStationaryAvoidance(bb.agent);
        }
    }

    void Finish(AIBlackboard bb)
    {
        bb.gearingUp = false;
        bb.worker.gearingUp = false;
        if (bb.brain != null) bb.brain.ForceReeval();
    }

    public override void OnExit(AIBlackboard bb)
    {
        arrived = false;
        if (bb.baseBuilding != null) bb.baseBuilding.ReleaseDropoffSlot(bb.worker);
        else if (bb.worker != null) bb.worker.dropoffSlot = -1;
        if (bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh)
        {
            bb.agent.stoppingDistance = Worker.GatherStopDistance;
            bb.agent.isStopped = false;
            Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        }
    }
}

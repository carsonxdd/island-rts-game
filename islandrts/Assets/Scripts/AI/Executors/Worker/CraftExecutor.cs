using UnityEngine;

/// <summary>
/// Crafter executor (2026-09-04, Slice 3): walk to a bench with a queue and
/// work it until the queue runs dry. The <see cref="BuildExecutor"/> shape with
/// a <see cref="CraftStation"/> in place of the site — approach point against the
/// station's collider, edge-distance arrival, stationary avoidance while working,
/// a rubber band if the crowd shoves the crafter off the bench.
/// </summary>
/// <remarks>
/// The crafter claims a seat when it sets out (<see cref="CraftStation.Claim"/>):
/// a bench has one seat per queued repeat, up to <see cref="CraftStation.MaxLaborers"/>,
/// so five spears draw up to four colonists and one spear draws one (2026-09-13).
/// The player's character always wins a place: <see cref="CraftStation.AddLabor"/>
/// returns false to a crafter whose repeat the player took over, and the crafter
/// waits beside the bench rather than leaving — the queue is still its job the
/// moment a repeat frees up. Labor is passed with <c>hands = null</c>, so the
/// costs come from the campfire stockpile alone.
/// </remarks>
public class CraftExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Crafting";

    private const float WorkDistance = 1.2f;   // collider-edge distance that counts as "at the bench"
    private const float DriftSlack = 0.8f;     // pushed this much further away → walk back

    private CraftStation station;
    private Collider stationCollider;
    private bool working;
    private bool destinationQueued;   // false = throttle/NavMesh rejected, retry next frame

    public override void OnEnter(AIBlackboard bb)
    {
        ReleaseNodeClaims(bb);
        bb.worker.StopGatheringSoundPublic();

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        station = null;
        stationCollider = null;
        working = false;
        destinationQueued = false;
        displayName = "Heading to the bench";
        Worker.RollMovingAvoidance(bb.agent);
        Acquire(bb);

        // Playtest: an unpinned colonist chose a bench over a waiting site because Craft outranks Build.
        if (station != null && bb.specialty == Worker.Specialty.Any
            && ConstructionSite.ActiveList.Count > 0 && bb.faction.Priorities.Craft > bb.faction.Priorities.Build)
            DevQuests.Signal("priority:craft_wins");
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Bench gone (building destroyed) or its queue finished
        if (station == null || !station.IsAlive || !station.HasWork)
        {
            if (station != null) station.Release(bb.worker);
            station = null;
            working = false;
            Acquire(bb);
            if (station == null)
            {
                if (bb.brain != null) bb.brain.ForceReeval();   // nothing to craft — Idle takes over
                return;
            }
        }

        if (!working)
        {
            // Honor the stuck-reset bool (Phase 6.25 gotcha): the callback already
            // ForceReeval'd; drop the bench and bail for this tick.
            if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            {
                station.Release(bb.worker);
                station = null;
                return;
            }

            if (!destinationQueued) IssueMove(bb);

            float edge = TargetingUtil.EdgeDistance(bb.transform.position, station.transform, stationCollider);
            if (edge <= WorkDistance) StartWorking(bb);
            return;
        }

        // Working. Rubber band: shoved off the bench → walk back (labor pauses meanwhile).
        float dist = TargetingUtil.EdgeDistance(bb.transform.position, station.transform, stationCollider);
        if (dist > WorkDistance + DriftSlack)
        {
            working = false;
            displayName = "Heading to the bench";
            Worker.RollMovingAvoidance(bb.agent);
            IssueMove(bb);
            return;
        }

        // Every repeat has hands on it (the player took ours, or the queue is
        // shorter than the crowd): wait. Two string literals, so the assignment
        // allocates nothing.
        displayName = station.AddLabor(Time.deltaTime, bb.worker, null) ? "Crafting" : "Waiting for the bench";

        // Playtest: a pinned Crafter keeps the bench while a site waits for hands.
        if (bb.specialty == Worker.Specialty.Crafter && ConstructionSite.ActiveList.Count > 0)
            DevQuests.Signal("craft:specialist_holds");
    }

    void Acquire(AIBlackboard bb)
    {
        // bb.targetStation is refreshed by StationWorkAvailable on every brain evaluation
        CraftStation candidate = bb.targetStation;
        if (candidate == null || !candidate.IsAlive || !candidate.HasWork) return;
        if (!candidate.Claim(bb.worker)) return;   // another crafter took it since the scan

        station = candidate;
        stationCollider = station.ApproachCollider;
        destinationQueued = false;
        IssueMove(bb);
    }

    void IssueMove(AIBlackboard bb)
    {
        if (station == null) return;
        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;

        Vector3 approach = TargetingUtil.GetApproachPoint(bb.transform.position, station.transform, stationCollider);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, approach);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void StartWorking(AIBlackboard bb)
    {
        if (bb.agent != null && bb.agent.isOnNavMesh) bb.agent.ResetPath();
        Worker.SetStationaryAvoidance(bb.agent);   // a stander cannot yield — make movers route around
        working = true;
        displayName = "Crafting";
        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();
    }

    static void ReleaseNodeClaims(AIBlackboard bb)
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
    }

    public override void OnExit(AIBlackboard bb)
    {
        if (station != null) station.Release(bb.worker);
        station = null;
        stationCollider = null;
        working = false;
        destinationQueued = false;

        if (bb.agent != null && bb.agent.isOnNavMesh) bb.agent.isStopped = false;
        Worker.RollMovingAvoidance(bb.agent);
    }
}

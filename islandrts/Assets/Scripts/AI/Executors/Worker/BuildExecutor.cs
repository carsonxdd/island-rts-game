using UnityEngine;

/// <summary>
/// Colonist executor: walk to a construction site and work it until it completes.
/// Construction advances only through labour (2026-09-02) — a site with nobody on
/// it just waits — so this is where buildings actually get built.
/// </summary>
/// <remarks>
/// Sites carve the NavMesh, so the destination is the carve-safe approach point and
/// arrival is an edge distance, the same pattern as every other building interaction.
/// The colonist claims a builder slot on the site when it sets out (up to
/// <see cref="ConstructionSite.MaxBuilders"/>), so a crowd of idle colonists spreads
/// over several sites instead of all walking to the nearest one. While working it is
/// stationary-avoidance "furniture"; if the crowd shoves it off the site it walks back.
/// </remarks>
public class BuildExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Building";

    private const float WorkDistance = 1.2f;   // collider-edge distance that counts as "at the site"
    private const float DriftSlack = 0.8f;     // pushed this much further away → walk back

    private ConstructionSite site;
    private Collider siteCollider;
    private bool working;
    private bool destinationQueued;   // false = throttle/NavMesh rejected, retry next frame

    public override void OnEnter(AIBlackboard bb)
    {
        ReleaseNodeClaims(bb);
        bb.worker.StopGatheringSoundPublic();

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        site = null;
        siteCollider = null;
        working = false;
        destinationQueued = false;
        displayName = "Heading to build";
        Worker.RollMovingAvoidance(bb.agent);
        Acquire(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Site finished or destroyed (demolished, or killed by enemies)
        if (site == null || site.IsComplete)
        {
            site = null;
            working = false;
            Acquire(bb);
            if (site == null)
            {
                if (bb.brain != null) bb.brain.ForceReeval();   // nothing to build — Idle/Repair takes over
                return;
            }
        }

        if (!working)
        {
            // Honor the stuck-reset bool (Phase 6.25 gotcha): the callback already
            // ForceReeval'd; drop the site and bail for this tick.
            if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            {
                site.UnregisterBuilder(bb.worker);
                site = null;
                return;
            }

            if (!destinationQueued) IssueMove(bb);

            float edge = TargetingUtil.EdgeDistance(bb.transform.position, site.transform, siteCollider);
            if (edge <= WorkDistance) StartWorking(bb);
            return;
        }

        // Working. Rubber band: shoved off the site → walk back (labour pauses meanwhile).
        float dist = TargetingUtil.EdgeDistance(bb.transform.position, site.transform, siteCollider);
        if (dist > WorkDistance + DriftSlack)
        {
            working = false;
            displayName = "Heading to build";
            Worker.RollMovingAvoidance(bb.agent);
            IssueMove(bb);
            return;
        }

        site.AddLabor(Time.deltaTime);
    }

    void Acquire(AIBlackboard bb)
    {
        // bb.bestSite is refreshed by ConstructionAvailable on every brain evaluation
        ConstructionSite candidate = bb.bestSite;
        if (candidate == null || candidate.IsComplete) return;
        if (!candidate.RegisterBuilder(bb.worker)) return;   // filled up since the scan

        site = candidate;
        siteCollider = site.GetComponent<Collider>();
        destinationQueued = false;
        IssueMove(bb);
    }

    void IssueMove(AIBlackboard bb)
    {
        if (site == null) return;
        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;

        Vector3 approach = TargetingUtil.GetApproachPoint(bb.transform.position, site.transform, siteCollider);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, approach);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void StartWorking(AIBlackboard bb)
    {
        if (bb.agent != null && bb.agent.isOnNavMesh) bb.agent.ResetPath();
        Worker.SetStationaryAvoidance(bb.agent);   // a stander cannot yield — make movers route around
        working = true;
        displayName = "Building";
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
        // Unregister by worker: the site keeps its own list, so this is safe whether
        // or not we were ever registered on the current site.
        if (site != null) site.UnregisterBuilder(bb.worker);
        site = null;
        siteCollider = null;
        working = false;
        destinationQueued = false;

        if (bb.agent != null && bb.agent.isOnNavMesh) bb.agent.isStopped = false;
        Worker.RollMovingAvoidance(bb.agent);
    }
}

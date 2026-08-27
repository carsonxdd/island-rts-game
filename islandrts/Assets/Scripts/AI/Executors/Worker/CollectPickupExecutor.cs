using UnityEngine;

/// <summary>
/// Worker executor: walk to a nearby ground pickup (stick/stone) and scoop it
/// up — an instant top-up alongside normal node gathering. Claims the pickup so
/// two workers never chase the same one; the claim is released on exit or when
/// the pickup is consumed.
/// </summary>
public class CollectPickupExecutor : ActionExecutor
{
    public override string DisplayName => "Collecting pickup";

    private const float CollectDistance = 0.9f;

    private GroundPickup target;
    private bool destinationQueued;  // false = throttle/NavMesh rejected, retry next frame

    public override void OnEnter(AIBlackboard bb)
    {
        // Release node claims — we're going for a pickup instead
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
        destinationQueued = false;
        Worker.RollMovingAvoidance(bb.agent);
        AcquireTarget(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Pickup gone (grabbed by someone else / despawned)?
        if (target == null)
        {
            AcquireTarget(bb);
            if (target == null)
            {
                // Nothing to collect — hand control back to the brain
                if (bb.brain != null) bb.brain.ForceReeval();
                return;
            }
        }

        // Honor the stuck-reset bool (Phase 6.25 gotcha): the callback already
        // ForceReeval'd; drop our target and bail for this tick.
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            ReleaseClaim(bb);
            target = null;
            return;
        }

        // Retry a rejected destination rather than ghost-standing
        if (!destinationQueued) IssueMove(bb);

        float dist = Vector3.Distance(bb.transform.position, target.transform.position);
        if (dist <= CollectDistance)
        {
            if (bb.agent.isOnNavMesh) bb.agent.ResetPath();
            target.claimedBy = null;
            target.Collect(bb);  // grants carry + destroys the pickup
            target = null;
            destinationQueued = false;
            if (bb.brain != null) bb.brain.ForceReeval();  // Return/Gather takes over
        }
    }

    void AcquireTarget(AIBlackboard bb)
    {
        // bb.bestPickup is populated by PickupAvailability during brain evaluation
        if (bb.bestPickup == null || bb.bestPickup.IsClaimedByOther(bb.worker)) return;

        target = bb.bestPickup;
        target.claimedBy = bb.worker;
        destinationQueued = false;
        IssueMove(bb);
    }

    void IssueMove(AIBlackboard bb)
    {
        if (target == null) return;
        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;

        destinationQueued = AINavHelper.TrySetDestination(bb.agent, target.transform.position);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void ReleaseClaim(AIBlackboard bb)
    {
        if (target != null && target.claimedBy == bb.worker)
            target.claimedBy = null;
    }

    public override void OnExit(AIBlackboard bb)
    {
        ReleaseClaim(bb);
        target = null;
        destinationQueued = false;
    }
}

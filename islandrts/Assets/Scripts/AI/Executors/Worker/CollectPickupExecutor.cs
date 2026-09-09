using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: walk to a nearby ground pickup (stick/stone) and scoop it
/// up — an instant top-up alongside normal node gathering. Claims the pickup so
/// two workers never chase the same one; the claim is released on exit or when
/// the pickup is consumed.
/// </summary>
/// <remarks>
/// Arrival has two rules (2026-09-09). Within <see cref="CollectDistance"/> of the
/// pickup, horizontally, it is collected outright. Failing that, an agent whose path
/// is done and who has stood still for <see cref="StallSeconds"/> is "as close as the
/// NavMesh allows": within <see cref="StallCollectDistance"/> it still collects (a
/// stick at the lip of a bush's carve), beyond it the pickup is marked unreachable
/// on the blackboard for a while and the brain moves on. The same mark follows a
/// stuck reset and a partial path. Before this, the stuck resolver dropped the target
/// and the next tick re-acquired the same pickup, forever — the "metre forward,
/// metre back" forage stutter, which also caught anyone who wandered into the pile.
/// </remarks>
public class CollectPickupExecutor : ActionExecutor
{
    public override string DisplayName => "Collecting pickup";

    private const float CollectDistance = 0.9f;
    private const float StallCollectDistance = 1.5f;
    private const float StallSeconds = 0.5f;
    private const float SampleRadius = 2f;

    private GroundPickup target;
    private bool destinationQueued;  // false = throttle/NavMesh rejected, retry next frame
    private float stallTimer;

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
        stallTimer = 0f;
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
        // ForceReeval'd. Remember the pickup as unreachable so the next tick does not
        // simply pick it again.
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            GiveUp(bb);
            return;
        }

        // Retry a rejected destination rather than ghost-standing
        if (!destinationQueued) IssueMove(bb);
        if (target == null) return;   // IssueMove found no NavMesh near it

        Vector3 toPickup = target.transform.position - bb.transform.position;
        toPickup.y = 0f;   // a pickup on a slope: the reach is a ground distance
        float dist = toPickup.magnitude;
        if (dist <= CollectDistance)
        {
            Collect(bb);
            return;
        }

        NavMeshAgent agent = bb.agent;
        if (destinationQueued && agent != null && agent.isOnNavMesh && !agent.pathPending)
        {
            // A computed path that does not reach the pickup will never get closer.
            if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                GiveUp(bb);
                return;
            }

            bool pathDone = !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.3f;
            bool still = agent.velocity.sqrMagnitude < 0.01f;
            if (pathDone && still)
            {
                stallTimer += Time.deltaTime;
                if (stallTimer >= StallSeconds)
                {
                    if (dist <= StallCollectDistance) Collect(bb);
                    else GiveUp(bb);
                }
            }
            else
            {
                stallTimer = 0f;
            }
        }
    }

    void Collect(AIBlackboard bb)
    {
        if (bb.agent.isOnNavMesh) bb.agent.ResetPath();
        target.claimedBy = null;
        target.Collect(bb);  // grants carry + destroys the pickup
        target = null;
        destinationQueued = false;
        stallTimer = 0f;
        if (bb.brain != null) bb.brain.ForceReeval();  // Return/Gather takes over
    }

    /// <summary>Drop the pickup, remember it as unreachable, and let the brain choose again.</summary>
    void GiveUp(AIBlackboard bb)
    {
        bb.MarkPickupUnreachable(target);
        ReleaseClaim(bb);
        target = null;
        destinationQueued = false;
        stallTimer = 0f;
        if (bb.brain != null) bb.brain.ForceReeval();
    }

    void AcquireTarget(AIBlackboard bb)
    {
        // bb.bestPickup is populated by Pickup/ForageAvailability during brain evaluation
        if (bb.bestPickup == null || bb.bestPickup.IsClaimedByOther(bb.worker)) return;
        if (bb.IsPickupUnreachable(bb.bestPickup)) return;

        target = bb.bestPickup;
        target.claimedBy = bb.worker;
        destinationQueued = false;
        stallTimer = 0f;
        if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();
        IssueMove(bb);
    }

    void IssueMove(AIBlackboard bb)
    {
        if (target == null) return;
        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;

        // The nearest walkable point to the pickup, never its raw position: a stick shed
        // at a trunk sits on a carve edge, and SetDestination on a point off the mesh
        // resolves to whatever Unity finds nearby.
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(target.transform.position, out hit, SampleRadius, NavMesh.AllAreas))
        {
            GiveUp(bb);
            return;
        }

        destinationQueued = AINavHelper.TrySetDestination(bb.agent, hit.position);
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
        stallTimer = 0f;
    }
}

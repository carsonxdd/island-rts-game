using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Warrior executor: Fall back to campfire when outmatched.
/// Activates when health is low and outnumbered.
/// Phase 6.25: destination goes through AINavHelper.TrySetDestination and the
/// return is honored — a rejected set retries next frame instead of leaving
/// the warrior in a ghost-moving state.
/// </summary>
public class RetreatExecutor : ActionExecutor
{
    public override string DisplayName => "Retreating!";

    private bool destinationSet = false;

    public override void OnEnter(AIBlackboard bb)
    {
        destinationSet = false;

        if (bb.baseBuilding != null && bb.agent.isOnNavMesh && bb.agent.enabled)
        {
            bb.agent.isStopped = false;
            destinationSet = AINavHelper.TrySetDestination(bb.agent, bb.baseBuilding.transform.position);

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (bb.baseBuilding == null) return;

        // Retry if the throttle/NavMesh rejected the destination on entry
        if (!destinationSet && bb.agent.isOnNavMesh && bb.agent.enabled)
        {
            destinationSet = AINavHelper.TrySetDestination(bb.agent, bb.baseBuilding.transform.position);
            if (destinationSet)
                bb.agent.isStopped = false;
        }

        // Check if we reached base
        float distToBase = Vector3.Distance(bb.transform.position, bb.baseBuilding.transform.position);
        if (distToBase < 5f)
        {
            bb.agent.isStopped = true;
            // Stay near base until health recovers or threat subsides
            // Brain will switch to another action when conditions improve
        }
        else
        {
            // Only run stuck resolution while still moving toward base
            if (bb.stuckResolver != null)
            {
                bb.stuckResolver.UpdateMoving();
            }
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        destinationSet = false;
        bb.agent.isStopped = false;
    }
}

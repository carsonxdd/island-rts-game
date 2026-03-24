using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Warrior executor: Fall back to campfire when outmatched (NEW behavior).
/// Activates when health is low and outnumbered.
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
            bb.agent.SetDestination(bb.baseBuilding.transform.position);
            destinationSet = true;

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Check if we reached base
        if (bb.baseBuilding != null)
        {
            float distToBase = Vector3.Distance(bb.transform.position, bb.baseBuilding.transform.position);
            if (distToBase < 5f)
            {
                bb.agent.isStopped = true;
                // Stay near base until health recovers or threat subsides
                // Brain will switch to another action when conditions improve
            }
            else
            {
                // Bug 2: Only run stuck resolution while still moving toward base
                if (bb.stuckResolver != null)
                {
                    bb.stuckResolver.UpdateMoving();
                }
            }
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        destinationSet = false;
        bb.agent.isStopped = false;
    }
}

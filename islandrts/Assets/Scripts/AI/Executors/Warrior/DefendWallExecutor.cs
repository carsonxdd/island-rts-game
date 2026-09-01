using UnityEngine;

/// <summary>
/// Warrior executor: Rush to defend a wall that is under attack by enemies.
/// High-priority action when walls are being attacked and warrior is nearby.
/// Phase 6.25: TrySetDestination's return is honored — a rejected set
/// (throttled or unmappable) retries next frame instead of being dropped.
/// </summary>
public class DefendWallExecutor : ActionExecutor
{
    public override string DisplayName => "Defending Wall!";

    /// <summary>Guard post just inside the wall, computed once on entry and then held.</summary>
    private Vector3 lastDefendPosition;
    private bool hasDefendPosition = false;
    private bool destinationSet = false;   // false = the move was rejected; retry next frame

    public override void OnEnter(AIBlackboard bb)
    {
        destinationSet = false;
        hasDefendPosition = false;

        if (bb.wallUnderAttack != null)
        {
            // Move to the wall under attack
            Vector3 wallPos = bb.wallUnderAttack.position;

            // Stand on the colony side of the wall, not out in the open with the enemies:
            // offset from the wall toward the campfire.
            Vector3 interiorDir = Vector3.zero;
            if (bb.baseBuilding != null)
            {
                interiorDir = (bb.baseBuilding.transform.position - wallPos).normalized;
            }

            Vector3 defendPos = wallPos + interiorDir * 2f;
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(defendPos, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            {
                defendPos = hit.position;
            }

            lastDefendPosition = defendPos;
            hasDefendPosition = true;
            bb.agent.isStopped = false;
            destinationSet = AINavHelper.TrySetDestination(bb.agent, defendPos);

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // WallIntegrity clears wallUnderAttack once the wall dies or stops taking hits.
        // Do nothing this tick; the brain re-scores and moves on to Engage or Patrol.
        if (bb.wallUnderAttack == null)
        {
            return; // Wall destroyed or no longer under attack, brain will re-evaluate
        }

        if (!hasDefendPosition) return;

        // Retry if the throttle/NavMesh rejected the destination on entry
        if (!destinationSet)
        {
            destinationSet = AINavHelper.TrySetDestination(bb.agent, lastDefendPosition);
        }

        float distToDefendPos = Vector3.Distance(bb.transform.position, lastDefendPosition);

        if (distToDefendPos < 3f)
        {
            // In position. This executor deliberately does no fighting of its own - holding
            // still here puts enemies inside the warrior's detection range, and Engage
            // outscores Defend as soon as one is close enough to hit.
            bb.agent.isStopped = true;
        }
        else
        {
            // Only run stuck resolution while still moving toward defend position
            if (bb.stuckResolver != null)
            {
                bb.stuckResolver.UpdateMoving();
            }
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        destinationSet = false;
        hasDefendPosition = false;
        bb.agent.isStopped = false;
    }
}

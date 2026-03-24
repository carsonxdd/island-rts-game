using UnityEngine;

/// <summary>
/// Warrior executor: Rush to defend a wall that is under attack by enemies.
/// High-priority action when walls are being attacked and warrior is nearby.
/// </summary>
public class DefendWallExecutor : ActionExecutor
{
    public override string DisplayName => "Defending Wall!";

    private Vector3 lastDefendPosition;
    private bool destinationSet = false;

    public override void OnEnter(AIBlackboard bb)
    {
        destinationSet = false;

        if (bb.wallUnderAttack != null)
        {
            // Move to the wall under attack
            Vector3 wallPos = bb.wallUnderAttack.position;

            // Get campfire direction to position on interior side
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
            bb.agent.isStopped = false;
            AINavHelper.TrySetDestination(bb.agent, defendPos);
            destinationSet = true;

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // If we've arrived near the wall, engage any nearby enemies
        if (bb.wallUnderAttack == null)
        {
            return; // Wall destroyed or no longer under attack, brain will re-evaluate
        }

        // If the wall target changed, update destination
        if (bb.wallUnderAttack != null && destinationSet)
        {
            float distToDefendPos = Vector3.Distance(bb.transform.position, lastDefendPosition);

            if (distToDefendPos < 3f)
            {
                // Near the wall - look for enemies to fight
                // The brain will likely switch to EngageEnemy if enemies are in range
                bb.agent.isStopped = true;
            }
            else
            {
                // Bug 2: Only run stuck resolution while still moving toward defend position
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

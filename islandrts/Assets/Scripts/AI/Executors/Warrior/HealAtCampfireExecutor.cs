using UnityEngine;

/// <summary>
/// Warrior executor: Walk to the campfire and slowly regenerate health.
/// Only active when no enemies are alive (wave is over).
/// Heals 5 HP/sec while within range of the campfire. Stops at full HP.
/// </summary>
public class HealAtCampfireExecutor : ActionExecutor
{
    public override string DisplayName => isHealing ? "Healing..." : "Moving to Campfire";

    private const float HealRate = 5f;         // HP per second
    private const float HealRange = 5f;        // Distance to campfire to start healing
    private const float StoppingDistance = 2f;  // NavMesh stopping distance near campfire

    private bool isHealing = false;
    private bool destinationSet = false;
    private float originalStoppingDist;

    public override void OnEnter(AIBlackboard bb)
    {
        isHealing = false;
        destinationSet = false;

        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            originalStoppingDist = bb.agent.stoppingDistance;
            bb.agent.stoppingDistance = StoppingDistance;
        }

        // Head to campfire
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
        if (bb.baseBuilding == null || bb.health == null) return;

        float distToCampfire = Vector3.Distance(bb.transform.position, bb.baseBuilding.transform.position);

        if (distToCampfire < HealRange)
        {
            // Close enough — stop and heal
            if (!isHealing)
            {
                isHealing = true;
                bb.agent.isStopped = true;
            }

            // Regenerate health
            bb.health.Heal(HealRate * Time.deltaTime);

            // Full HP — force re-eval so brain switches to Patrol
            if (bb.health.GetHealthPercentage() >= 1f)
            {
                if (bb.brain != null)
                    bb.brain.ForceReeval();
            }
        }
        else
        {
            // Still walking to campfire
            isHealing = false;

            if (!destinationSet && bb.agent.isOnNavMesh && bb.agent.enabled)
            {
                bb.agent.isStopped = false;
                bb.agent.SetDestination(bb.baseBuilding.transform.position);
                destinationSet = true;
            }

            if (bb.stuckResolver != null)
                bb.stuckResolver.UpdateMoving();
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        isHealing = false;
        destinationSet = false;

        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.stoppingDistance = originalStoppingDist;
            bb.agent.isStopped = false;
        }
    }
}

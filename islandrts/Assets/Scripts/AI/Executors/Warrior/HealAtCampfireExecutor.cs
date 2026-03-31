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
    private const float HealRange = 3f;        // Must be close to campfire to heal
    private const float StoppingDistance = 1.5f; // NavMesh stopping distance near campfire

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
        if (!bb.agent.isOnNavMesh || !bb.agent.enabled) return;

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
            // Still walking to campfire — ensure agent is moving
            isHealing = false;
            bb.agent.isStopped = false;

            // Retry destination if agent has no path or stopped moving
            bool needsPath = !destinationSet
                || !bb.agent.hasPath
                || bb.agent.velocity.sqrMagnitude < 0.01f;

            if (needsPath)
            {
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

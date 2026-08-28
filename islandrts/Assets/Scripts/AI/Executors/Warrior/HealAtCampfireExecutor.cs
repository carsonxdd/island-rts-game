using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Warrior executor: Walk to the campfire and slowly regenerate health.
/// Only active when no enemies are alive (wave is over).
/// Heals 5 HP/sec while within range of the campfire. Stops at full HP.
///
/// The campfire carves the NavMesh, so its CENTER is unreachable — destination
/// and arrival checks both use the collider edge via the shared
/// TargetingUtil.GetApproachPoint / EdgeDistance helpers (Phase 6.25).
/// Measuring from the center made warriors stall outside HealRange at the
/// carve boundary, stuck on "Moving to Campfire" forever.
/// </summary>
public class HealAtCampfireExecutor : ActionExecutor
{
    public override string DisplayName => isHealing ? "Healing..." : "Moving to Campfire";

    private const float HealRate = 1.5f;       // HP/sec. Deliberately slow: at 5 a warrior fully reset between
                                               // every wave, so cross-night attrition could never build up.
    private const float HealRange = 3f;        // From the campfire's collider edge, not its center
    private const float StoppingDistance = 1.5f; // NavMesh stopping distance near campfire

    private bool isHealing = false;
    private bool destinationSet = false;
    private float originalStoppingDist;
    private Collider campfireCollider;

    public override void OnEnter(AIBlackboard bb)
    {
        isHealing = false;
        destinationSet = false;
        campfireCollider = bb.baseBuilding != null ? bb.baseBuilding.GetComponent<Collider>() : null;

        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            originalStoppingDist = bb.agent.stoppingDistance;
            bb.agent.stoppingDistance = StoppingDistance;
        }

        // Head to campfire
        if (bb.baseBuilding != null && bb.agent.isOnNavMesh && bb.agent.enabled)
        {
            bb.agent.isStopped = false;
            destinationSet = AINavHelper.TrySetDestination(bb.agent, GetHealSpot(bb));

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (bb.baseBuilding == null || bb.health == null) return;
        if (!bb.agent.isOnNavMesh || !bb.agent.enabled) return;

        float distToCampfire = TargetingUtil.EdgeDistance(
            bb.transform.position, bb.baseBuilding.transform, campfireCollider);

        if (distToCampfire < HealRange)
        {
            // Close enough — stop and heal
            if (!isHealing)
            {
                isHealing = true;
                bb.agent.isStopped = true;
            }

            // Regenerate health
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bb.health.Heal(SimOverrides.HealRate(HealRate) * Time.deltaTime);
#else
            bb.health.Heal(HealRate * Time.deltaTime);
#endif

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

            // Retry destination if it was never set (throttled/rejected), the
            // agent lost its path, or it stopped moving short of the heal ring
            bool needsPath = !destinationSet
                || !bb.agent.hasPath
                || bb.agent.velocity.sqrMagnitude < 0.01f;

            if (needsPath && !bb.agent.pathPending)
            {
                destinationSet = AINavHelper.TrySetDestination(bb.agent, GetHealSpot(bb));
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

    /// <summary>Walkable point at the campfire's edge nearest this warrior (shared carve-safe pattern).</summary>
    Vector3 GetHealSpot(AIBlackboard bb)
    {
        return TargetingUtil.GetApproachPoint(
            bb.transform.position, bb.baseBuilding.transform, campfireCollider);
    }
}

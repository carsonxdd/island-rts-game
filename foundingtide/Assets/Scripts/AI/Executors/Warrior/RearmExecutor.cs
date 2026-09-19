using UnityEngine;

/// <summary>
/// Warrior executor (2026-09-04, Slice 3): walk to the campfire and swap the
/// weapon in hand for the better one in the stockpile. The
/// <see cref="HealAtCampfireExecutor"/> walk — carve-safe approach point,
/// edge-distance arrival — then one call to <see cref="BaseBuilding.RearmWarrior"/>
/// and a re-evaluation, so a warrior is never stuck here once armed.
/// </summary>
public class RearmExecutor : ActionExecutor
{
    public override string DisplayName => "Rearming";

    private const float ReachRange = 3f;           // from the campfire's collider edge
    private const float StoppingDistance = 1.5f;

    private bool destinationSet;
    private float originalStoppingDist;
    private Collider campfireCollider;

    public override void OnEnter(AIBlackboard bb)
    {
        destinationSet = false;
        campfireCollider = bb.baseBuilding != null ? bb.baseBuilding.GetComponent<Collider>() : null;

        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            originalStoppingDist = bb.agent.stoppingDistance;
            bb.agent.stoppingDistance = StoppingDistance;
        }

        if (bb.baseBuilding != null && bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
            destinationSet = AINavHelper.TrySetDestination(bb.agent, Spot(bb));
            if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (bb.baseBuilding == null || bb.warrior == null) return;
        if (bb.agent == null || !bb.agent.isOnNavMesh || !bb.agent.enabled) return;

        float dist = TargetingUtil.EdgeDistance(bb.transform.position, bb.baseBuilding.transform, campfireCollider);
        if (dist < ReachRange)
        {
            bb.baseBuilding.RearmWarrior(bb.warrior);   // no-op if the better weapon went while we walked
            if (bb.brain != null) bb.brain.ForceReeval();
            return;
        }

        bb.agent.isStopped = false;
        bool needsPath = !destinationSet || !bb.agent.hasPath || bb.agent.velocity.sqrMagnitude < 0.01f;
        if (needsPath && !bb.agent.pathPending)
            destinationSet = AINavHelper.TrySetDestination(bb.agent, Spot(bb));

        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving()) return;
    }

    public override void OnExit(AIBlackboard bb)
    {
        destinationSet = false;
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.stoppingDistance = originalStoppingDist;
            bb.agent.isStopped = false;
        }
    }

    Vector3 Spot(AIBlackboard bb)
    {
        return TargetingUtil.GetApproachPoint(bb.transform.position, bb.baseBuilding.transform, campfireCollider);
    }
}

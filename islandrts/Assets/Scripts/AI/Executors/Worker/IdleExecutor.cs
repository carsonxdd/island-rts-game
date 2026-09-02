using UnityEngine;

/// <summary>
/// Worker executor: nothing to do — walk home and wait there.
/// Low-priority fallback action.
/// </summary>
/// <remarks>
/// "Home" is the hut or campfire the colonist is homed to (2026-09-02). A survivor
/// who has just come ashore is homed to the building that had room, so this is also
/// what walks them in from the cove; an idle colonist standing beside a hut is a
/// filled slot with no job. Far from home → path to its carve-safe approach point and
/// stop a few metres short (huts and the campfire carve, and a crowd of idlers must
/// not block the delivery edge); near home, or homeless → stand where we are.
/// </remarks>
public class IdleExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Idle";

    private const float HomeRadius = 6f;     // further than this from home's edge → walk back
    private const float ArriveRadius = 3.5f; // close enough to stop (leaves the building edge clear)

    private IHousing home;
    private bool moving;
    private bool destinationQueued;

    public override void OnEnter(AIBlackboard bb)
    {
        moving = false;
        destinationQueued = false;
        home = PopulationManager.Instance != null ? PopulationManager.Instance.HomeOf(bb.worker) : null;

        if (home != null && bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh
            && TargetingUtil.EdgeDistance(bb.transform.position, home.transform, home.HousingCollider) > HomeRadius)
        {
            moving = true;
            displayName = "Heading home";
            Worker.RollMovingAvoidance(bb.agent);
            IssueMove(bb);
        }
        else
        {
            Stand(bb);
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (!moving) return;   // standing — the brain picks something better when there is something

        if (home == null || !home.HousingAlive)
        {
            Stand(bb);
            return;
        }

        // A stuck reset already ForceReeval'd — bail for this tick
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            return;

        if (!destinationQueued) IssueMove(bb);

        float edge = TargetingUtil.EdgeDistance(bb.transform.position, home.transform, home.HousingCollider);
        bool pathDone = bb.agent != null && bb.agent.isOnNavMesh && destinationQueued
            && !bb.agent.pathPending && !bb.agent.hasPath;
        if (edge <= ArriveRadius || pathDone)
        {
            Stand(bb);
        }
    }

    void IssueMove(AIBlackboard bb)
    {
        if (home == null || bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;
        Vector3 approach = TargetingUtil.GetApproachPoint(bb.transform.position, home.transform, home.HousingCollider);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, approach);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void Stand(AIBlackboard bb)
    {
        moving = false;
        displayName = "Idle";
        if (bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh)
        {
            bb.agent.ResetPath();
            bb.agent.isStopped = true;
            // Standing still: max-importance so deliverers route around idlers at the
            // campfire instead of shoving them (a stander has no path and can't yield)
            Worker.SetStationaryAvoidance(bb.agent);
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        moving = false;
        home = null;
        if (bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
            Worker.RollMovingAvoidance(bb.agent);  // about to move — drop stationary-importance
        }
    }
}

using UnityEngine;

/// <summary>
/// Worker executor: Idle/wander near base when nothing else to do.
/// Low-priority fallback action.
/// </summary>
public class IdleExecutor : ActionExecutor
{
    public override string DisplayName => "Idle";

    public override void OnEnter(AIBlackboard bb)
    {
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.ResetPath();
            bb.agent.isStopped = true;
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Just idle near base. Brain will re-evaluate and pick a better action
        // when resources become available or threats appear.
    }

    public override void OnExit(AIBlackboard bb)
    {
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor (2026-09-04, Slice 4): a starving colonist walks down to the
/// cove and leaves the island. Destroying the body on arrival is the whole
/// bookkeeping — <c>Worker.OnDestroy → BaseBuilding.NotifyWorkerRemoved →
/// PopulationManager.RemoveColonist</c> is the single removal path, so the
/// roster, the job counts and the housing slot all update themselves.
/// </summary>
/// <remarks>
/// A leaver that cannot reach the cove (walled in, off-mesh) still leaves: a
/// stuck reset or <see cref="GiveUpAfter"/> seconds destroys the body where it
/// stands. Never stays in the roster as a ghost.
/// </remarks>
public class LeaveExecutor : ActionExecutor
{
    public override string DisplayName => "Leaving";

    private const float ArriveDistance = 2f;
    private const float GiveUpAfter = 60f;

    private Vector3 shore;
    private bool destinationQueued;
    private float startedAt;
    private bool gone;

    public override void OnEnter(AIBlackboard bb)
    {
        gone = false;
        destinationQueued = false;
        startedAt = Time.time;

        // Drop any node claim so the tree does not stay reserved for a ghost
        if (bb.targetResource != null)
        {
            bb.targetResource.UnclaimNode(bb.worker);
            if (bb.isRegisteredAtNode)
            {
                bb.targetResource.UnregisterWorker(bb.worker);
                bb.isRegisteredAtNode = false;
            }
            bb.targetResource = null;
        }
        bb.worker.StopGatheringSoundPublic();
        if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();

        shore = bb.transform.position;
        if (TerrainGrid.Instance != null)
        {
            Vector3 cove = TerrainGrid.Instance.CoveCenter + new Vector3(1f, 0f, 0f);   // where arrivals land
            NavMeshHit hit;
            shore = NavMesh.SamplePosition(cove, out hit, 6f, NavMesh.AllAreas) ? hit.position : cove;
        }

        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        IssueMove(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (gone) return;

        // The stuck callback already ForceReeval'd; a leaver that cannot get
        // there leaves anyway.
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            Depart(bb);
            return;
        }

        if (!destinationQueued) IssueMove(bb);

        Vector3 p = bb.transform.position;
        float dx = p.x - shore.x, dz = p.z - shore.z;
        bool arrived = dx * dx + dz * dz <= ArriveDistance * ArriveDistance;
        bool pathDone = bb.agent != null && bb.agent.isOnNavMesh && destinationQueued
            && !bb.agent.pathPending && !bb.agent.hasPath;
        if (arrived || pathDone || Time.time - startedAt > GiveUpAfter)
            Depart(bb);
    }

    void IssueMove(AIBlackboard bb)
    {
        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, shore);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void Depart(AIBlackboard bb)
    {
        gone = true;
        if (bb.worker != null) Object.Destroy(bb.worker.gameObject);   // the single removal path
    }

    public override void OnExit(AIBlackboard bb) { }
}

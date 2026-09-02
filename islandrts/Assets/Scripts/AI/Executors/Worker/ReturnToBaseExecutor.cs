using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: Deliver carried resources back to the campfire.
/// Uses multiple delivery checks with a timer-based fallback to prevent
/// workers from getting stuck near the campfire due to NavMesh carving
/// or agent stoppingDistance edge cases.
///
/// Phase 6.25: delivery is measured from the campfire's collider EDGE
/// (bb.deliveryDistance = 1.5 from the edge), not its center. The campfire
/// carves the NavMesh, so center distance never gets small — the old
/// center-based check only ever succeeded via the timer fallbacks. The
/// dropoff destination uses the shared ClosestPoint -> SamplePosition
/// approach-point pattern (TargetingUtil.GetApproachPoint).
/// </summary>
public class ReturnToBaseExecutor : ActionExecutor
{
    public override string DisplayName => "Returning to base";

    // Timer to detect when the worker has been trying to return for too long
    private float returnTimer;

    // Campfire collider, cached on entry for edge-distance checks
    private Collider campfireCollider;

    public override void OnEnter(AIBlackboard bb)
    {
        returnTimer = 0f;
        campfireCollider = bb.baseBuilding != null ? bb.baseBuilding.GetComponent<Collider>() : null;

        if (bb.baseBuilding == null || !bb.agent.isOnNavMesh || !bb.agent.enabled) return;

        // Release resource claim if any
        if (bb.targetResource != null)
        {
            bb.targetResource.UnclaimNode(bb.worker);
            if (bb.isRegisteredAtNode)
            {
                bb.targetResource.UnregisterWorker(bb.worker);
                bb.isRegisteredAtNode = false;
            }
        }

        bb.worker.StopGatheringSoundPublic();

        // Temporarily reduce stopping distance so the worker walks
        // right up to the dropoff point instead of stopping short
        bb.agent.stoppingDistance = 0.5f;

        // Moving errand — drop stationary-importance if we were just gathering
        Worker.RollMovingAvoidance(bb.agent);

        // If the throttle/NavMesh rejects this, OnUpdate's !hasPath retry self-heals
        if (AINavHelper.TrySetDestination(bb.agent, GetDropoffPoint(bb)))
        {
            bb.agent.isStopped = false;
        }

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (bb.baseBuilding == null)
        {
            DeliverResources(bb);
            return;
        }

        returnTimer += Time.deltaTime;

        // Stuck resolution
        if (bb.stuckResolver != null)
        {
            bb.stuckResolver.UpdateMoving();
        }

        // Distance to the campfire's collider edge (center distance if no collider)
        float edgeDistance = TargetingUtil.EdgeDistance(
            bb.transform.position, bb.baseBuilding.transform, campfireCollider);

        // --- Delivery checks (from most specific to most generous) ---

        // 1. Within delivery distance of the campfire edge
        bool withinRange = edgeDistance <= bb.deliveryDistance;

        // 2. Agent finished its path and is reasonably close to the campfire
        bool pathFinished = bb.agent.isOnNavMesh
            && !bb.agent.pathPending
            && bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.5f;
        bool pathFinishedNearBase = pathFinished && edgeDistance <= bb.deliveryDistance + 1.5f;

        // 3. Agent has stopped moving and is in the general area of the campfire
        bool agentStopped = bb.agent.isOnNavMesh && bb.agent.velocity.sqrMagnitude < 0.05f;
        bool stoppedNearBase = agentStopped && edgeDistance <= bb.deliveryDistance + 1.5f;

        // 4. Timer fallback: been trying to return for 3+ seconds and within generous range
        bool timerFallback = returnTimer > 3f && edgeDistance <= bb.deliveryDistance + 3f;

        // 5. Nuclear fallback: been trying for 8+ seconds, deliver from anywhere
        //    (handles pathfinding failures, NavMesh issues, etc.)
        bool nuclearFallback = returnTimer > 8f && bb.carryAmount > 0f;

        if (withinRange || pathFinishedNearBase || stoppedNearBase || timerFallback || nuclearFallback)
        {
            DeliverResources(bb);
            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
            return;
        }

        // Only retry path if agent has lost its path
        if (bb.agent.isOnNavMesh && bb.agent.enabled && !bb.agent.hasPath && !bb.agent.pathPending)
        {
            if (AINavHelper.TrySetDestination(bb.agent, GetDropoffPoint(bb)))
            {
                bb.agent.isStopped = false;
            }
        }
    }

    void DeliverResources(AIBlackboard bb)
    {
        if (bb.carryAmount <= 0) return;

        if (ResourceManager.Instance == null)
        {
            bb.carryAmount = 0f;
            bb.worker.carryAmount = 0f;
            return;
        }

        int amountToDeliver = Mathf.RoundToInt(bb.carryAmount);

        ResourceManager.Instance.Add(bb.assignedResourceType, amountToDeliver);

        bb.carryAmount = 0f;
        bb.worker.carryAmount = 0f;

        // Force immediate re-evaluation so the brain switches away
        // instead of idling at base for up to 0.3s
        if (bb.brain != null)
            bb.brain.ForceReeval();
    }

    /// <summary>
    /// Walkable point at the campfire's collider edge nearest the worker.
    /// The campfire carves the NavMesh, so this must go through the shared
    /// ClosestPoint -> SamplePosition pattern.
    /// </summary>
    Vector3 GetDropoffPoint(AIBlackboard bb)
    {
        return TargetingUtil.GetApproachPoint(
            bb.transform.position, bb.baseBuilding.transform, campfireCollider);
    }

    public override void OnExit(AIBlackboard bb)
    {
        // Restore stopping distance for gathering
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.stoppingDistance = Worker.GatherStopDistance;
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: Deliver carried resources back to the campfire.
/// Uses multiple delivery checks with a timer-based fallback to prevent
/// workers from getting stuck near the campfire due to NavMesh carving
/// or agent stoppingDistance edge cases.
/// </summary>
public class ReturnToBaseExecutor : ActionExecutor
{
    public override string DisplayName => "Returning to base";

    // Timer to detect when the worker has been trying to return for too long
    private float returnTimer;

    public override void OnEnter(AIBlackboard bb)
    {
        returnTimer = 0f;

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
        // right up to the dropoff point instead of stopping 2m short
        bb.agent.stoppingDistance = 0.5f;

        // If the throttle/NavMesh rejects this, OnUpdate's !hasPath retry self-heals
        Vector3 dropoffPoint = GetNearestDropoffPoint(bb);
        if (AINavHelper.TrySetDestination(bb.agent, dropoffPoint))
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

        float distanceToBase = Vector3.Distance(bb.transform.position, bb.baseBuilding.transform.position);

        // --- Delivery checks (from most specific to most generous) ---

        // 1. Within delivery distance of base center
        bool withinRange = distanceToBase <= bb.deliveryDistance;

        // 2. Agent finished its path and is reasonably close to base
        bool pathFinished = bb.agent.isOnNavMesh
            && !bb.agent.pathPending
            && bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.5f;
        bool pathFinishedNearBase = pathFinished && distanceToBase <= bb.deliveryDistance + 1.5f;

        // 3. Agent has stopped moving and is in the general area of base
        bool agentStopped = bb.agent.isOnNavMesh && bb.agent.velocity.sqrMagnitude < 0.05f;
        bool stoppedNearBase = agentStopped && distanceToBase <= bb.deliveryDistance + 1.5f;

        // 4. Timer fallback: been trying to return for 3+ seconds and within generous range
        bool timerFallback = returnTimer > 3f && distanceToBase <= bb.deliveryDistance + 3f;

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
            if (AINavHelper.TrySetDestination(bb.agent, GetNearestDropoffPoint(bb)))
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

        switch (bb.assignedResourceType)
        {
            case ResourceNode.ResourceType.Wood:
                ResourceManager.Instance.AddWood(amountToDeliver);
                break;
            case ResourceNode.ResourceType.Food:
                ResourceManager.Instance.AddFood(amountToDeliver);
                break;
            case ResourceNode.ResourceType.Stone:
                ResourceManager.Instance.AddStone(amountToDeliver);
                break;
        }

        bb.carryAmount = 0f;
        bb.worker.carryAmount = 0f;

        // Force immediate re-evaluation so the brain switches away
        // instead of idling at base for up to 0.3s
        if (bb.brain != null)
            bb.brain.ForceReeval();
    }

    Vector3 GetNearestDropoffPoint(AIBlackboard bb)
    {
        Vector3 directionToWorker = (bb.transform.position - bb.baseBuilding.transform.position).normalized;
        float ringRadius = bb.deliveryDistance;
        Vector3 dropoffPoint = bb.baseBuilding.transform.position + (directionToWorker * ringRadius);
        dropoffPoint.y = bb.baseBuilding.transform.position.y;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(dropoffPoint, out hit, 3f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Fallback: try 8 directions
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 candidate = bb.baseBuilding.transform.position + dir * ringRadius;
            candidate.y = bb.baseBuilding.transform.position.y;

            if (NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        return dropoffPoint;
    }

    public override void OnExit(AIBlackboard bb)
    {
        // Restore stopping distance for gathering
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.stoppingDistance = bb.gatherDistance * 0.8f;
        }
    }
}

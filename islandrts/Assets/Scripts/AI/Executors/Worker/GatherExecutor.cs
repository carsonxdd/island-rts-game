using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: Move to best resource node, gather until full or node depleted.
/// Ports existing Worker gather logic with resource scoring, claim system, and gather points.
/// </summary>
public class GatherExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Gathering";

    private enum GatherPhase { MovingToResource, Gathering }
    private GatherPhase phase;

    // Cached gather point
    private Vector3 cachedGatherPoint;
    private bool hasValidGatherPoint = false;
    private bool destinationQueued = false;  // false = throttle/NavMesh rejected the set, retry next frame
    private float unreachableTimer = 0f;     // accumulates while stopped at the end of a dead-end path

    public override void OnEnter(AIBlackboard bb)
    {
        phase = GatherPhase.MovingToResource;
        hasValidGatherPoint = false;
        destinationQueued = false;
        unreachableTimer = 0f;
        headingToBase = false;

        // Find and claim best resource (already cached by ResourceAvailability consideration)
        if (bb.bestResource != null)
        {
            // Release previous claim if any
            if (bb.targetResource != null && bb.targetResource != bb.bestResource)
            {
                bb.targetResource.UnclaimNode(bb.worker);
            }

            bb.targetResource = bb.bestResource;
            bb.targetResource.ClaimNode(bb.worker);

            // Cache gather point
            cachedGatherPoint = bb.targetResource.GetGatherPoint(bb.transform.position);
            hasValidGatherPoint = true;

            destinationQueued = AINavHelper.TrySetDestination(bb.agent, cachedGatherPoint);
            if (destinationQueued)
            {
                bb.agent.isStopped = false;
            }

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();

            displayName = "Moving to " + bb.assignedResourceType;
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        switch (phase)
        {
            case GatherPhase.MovingToResource:
                UpdateMovingToResource(bb);
                break;
            case GatherPhase.Gathering:
                UpdateGathering(bb);
                break;
        }
    }

    void UpdateMovingToResource(AIBlackboard bb)
    {
        // Check if target still exists
        if (bb.targetResource == null)
        {
            hasValidGatherPoint = false;
            // Target destroyed — try to seamlessly pick up next best resource
            if (TryPickupNewResource(bb)) return;
            // No resource available — head toward base if carrying anything
            StartHeadingToBase(bb);
            return;
        }

        // Check if target depleted while we were walking to it
        if (!bb.targetResource.HasResources())
        {
            bb.targetResource.UnclaimNode(bb.worker);
            bb.targetResource = null;
            hasValidGatherPoint = false;
            if (TryPickupNewResource(bb)) return;
            StartHeadingToBase(bb);
            return;
        }

        // Retry if the throttle or NavMesh rejected the destination earlier —
        // never pretend success, that's the "ghost moving" freeze
        if (!destinationQueued && hasValidGatherPoint)
        {
            destinationQueued = AINavHelper.TrySetDestination(bb.agent, cachedGatherPoint);
            if (destinationQueued)
            {
                bb.agent.isStopped = false;
            }
        }

        // Stuck resolution. A stuck reset fires Worker's onStuckReset callback,
        // which unclaims and NULLS bb.targetResource mid-call — dereferencing it
        // below would NRE (seen repeatedly in the 2026-08-24 playtest log). The
        // callback already ForceReeval'd; bail out and let the next tick re-pick.
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            return;

        // Check if we've arrived. Anti-orbit guarantee (Phase 6.25): the tolerance is
        // floored at AgentRadius + 0.25, which always exceeds how far ORCA avoidance can
        // hold the agent off its (on-NavMesh) gather point — so the worker can never be
        // asked to reach a spot it physically can't occupy and circle the node forever.
        // The center check (standing ring + tolerance) additionally accepts a worker that
        // avoidance pushed to a different spot on the ring than the one it pathed to.
        float arrivalTol = Mathf.Max(bb.gatherDistance, Worker.AgentRadius + 0.25f);
        float distToCenter = Vector3.Distance(bb.transform.position, bb.targetResource.transform.position);
        bool arrived = distToCenter <= bb.targetResource.GatherRingRadius + arrivalTol
            || (hasValidGatherPoint
                && Vector3.Distance(bb.transform.position, cachedGatherPoint) <= arrivalTol);

        if (arrived)
        {
            // Arrived at resource
            bb.agent.ResetPath();
            bb.targetResource.UnclaimNode(bb.worker);
            hasValidGatherPoint = false;

            if (bb.targetResource.RegisterWorker(bb.worker))
            {
                bb.isRegisteredAtNode = true;
                phase = GatherPhase.Gathering;

                if (bb.stuckResolver != null)
                    bb.stuckResolver.ResetStuckDetection();

                displayName = "Collecting " + bb.assignedResourceType;

                // Start gathering sound
                bb.worker.StartGatheringSoundPublic();
            }
            else
            {
                // Node empty or full on arrival — try next best resource
                bb.targetResource = null;
                if (!TryPickupNewResource(bb))
                    StartHeadingToBase(bb);
            }
        }
        else if (destinationQueued && !bb.agent.pathPending)
        {
            // Unreachable-node handling: the path resolved but cannot actually reach the
            // node (walled off / NavMesh island). PathPartial means Unity pathed us to the
            // closest reachable point — if we have walked to the end of that path and
            // are still not within gatherDistance, give up and look for a different node.
            bool pathDeadEnd =
                bb.agent.pathStatus == NavMeshPathStatus.PathInvalid ||
                (bb.agent.pathStatus == NavMeshPathStatus.PathPartial &&
                 bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.1f);

            if (pathDeadEnd)
            {
                unreachableTimer += Time.deltaTime;
                if (unreachableTimer >= 0.6f)
                    GiveUpOnUnreachableNode(bb);
            }
            else
            {
                unreachableTimer = 0f;
            }
        }
    }

    void UpdateGathering(AIBlackboard bb)
    {
        if (bb.targetResource == null)
        {
            UnregisterFromNode(bb);
            bb.worker.StopGatheringSoundPublic();
            // Node destroyed — try to seamlessly pick up next best resource
            if (!TryPickupNewResource(bb))
                StartHeadingToBase(bb);
            return;
        }

        // Check if full
        if (bb.carryAmount >= bb.carryCapacity - 0.01f)
        {
            bb.carryAmount = bb.carryCapacity;
            bb.worker.carryAmount = bb.carryAmount;
            bb.worker.StopGatheringSoundPublic();
            UnregisterFromNode(bb);
            // Don't just return and wait — start walking toward base immediately
            // so the worker isn't idle while the brain re-evaluates
            StartHeadingToBase(bb);
            return;
        }

        // Check if node empty
        if (!bb.targetResource.HasResources())
        {
            bb.worker.StopGatheringSoundPublic();
            UnregisterFromNode(bb);
            bb.targetResource = null;
            // Node depleted — try to seamlessly pick up next best resource
            if (!TryPickupNewResource(bb))
                StartHeadingToBase(bb);
            return;
        }

        // Gather incrementally (same logic as original Worker)
        float spaceInInventory = bb.carryCapacity - bb.carryAmount;
        float wantToGather = bb.gatherRatePerSecond * Time.deltaTime;
        float requestAmount = Mathf.Min(wantToGather, spaceInInventory);

        float actuallyGathered = bb.targetResource.GatherResources(requestAmount);
        bb.carryAmount += actuallyGathered;
        bb.worker.carryAmount = bb.carryAmount;

        // Snap to capacity if very close
        if (bb.carryAmount >= bb.carryCapacity - 0.01f)
        {
            bb.carryAmount = bb.carryCapacity;
            bb.worker.carryAmount = bb.carryAmount;
        }

        // Check post-gather conditions
        bool isFull = bb.carryAmount >= bb.carryCapacity - 0.01f;
        bool nodeEmpty = !bb.targetResource.HasResources();

        if (isFull || nodeEmpty)
        {
            bb.worker.StopGatheringSoundPublic();
            UnregisterFromNode(bb);

            if (nodeEmpty)
            {
                bb.targetResource = null;
            }

            if (isFull)
            {
                // Full — head toward base, brain will switch to ReturnToBase
                StartHeadingToBase(bb);
            }
            else if (nodeEmpty)
            {
                // Not full but node empty — find another resource
                if (!TryPickupNewResource(bb))
                    StartHeadingToBase(bb);
            }
        }
    }

    /// <summary>
    /// The node cannot be pathed to (walled off, NavMesh island). Remember it so
    /// ResourceAvailability skips it for a while, then move on to another node.
    /// </summary>
    void GiveUpOnUnreachableNode(AIBlackboard bb)
    {
        unreachableTimer = 0f;
        bb.MarkNodeUnreachable(bb.targetResource);
        if (bb.targetResource != null)
            bb.targetResource.UnclaimNode(bb.worker);
        bb.targetResource = null;
        hasValidGatherPoint = false;
        destinationQueued = false;
        bb.agent.ResetPath();

        // bb.bestResource may still be the node we just gave up on — TryPickupNewResource
        // filters it, and ForceReeval makes ResourceAvailability rescan without it.
        if (!TryPickupNewResource(bb))
        {
            StartHeadingToBase(bb);
            if (bb.brain != null)
                bb.brain.ForceReeval();
        }
    }

    /// <summary>
    /// When the current target is lost, try to seamlessly transition to the next best
    /// resource node. bb.bestResource is populated by ResourceAvailability during brain
    /// evaluation. Returns true if a new resource was picked up.
    /// Will NOT pick up a resource if inventory is full (worker should return to base).
    /// </summary>
    bool TryPickupNewResource(AIBlackboard bb)
    {
        // Don't pick up new resources when inventory is full — worker should return to base
        if (bb.carryAmount >= bb.carryCapacity - 0.01f)
            return false;

        if (bb.bestResource == null || !bb.bestResource.HasResources())
            return false;

        // A node we just failed to reach may still be cached as bestResource
        if (bb.IsNodeUnreachable(bb.bestResource))
            return false;

        // Respect per-node capacity - full nodes spill workers to the next one
        if (!bb.bestResource.HasWorkerRoom(bb.worker))
            return false;

        // Don't pick up the same depleted node
        if (bb.targetResource == bb.bestResource)
            return false;

        bb.targetResource = bb.bestResource;
        bb.targetResource.ClaimNode(bb.worker);

        cachedGatherPoint = bb.targetResource.GetGatherPoint(bb.transform.position);
        hasValidGatherPoint = true;

        destinationQueued = AINavHelper.TrySetDestination(bb.agent, cachedGatherPoint);
        if (destinationQueued)
        {
            bb.agent.isStopped = false;
        }

        phase = GatherPhase.MovingToResource;
        unreachableTimer = 0f;
        displayName = "Moving to " + bb.assignedResourceType;

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        return true;
    }

    /// <summary>
    /// Start walking toward base as a fallback so the worker isn't idle.
    /// Only sets destination once (tracks via headingToBase flag).
    /// Forces brain re-eval so ReturnToBase executor takes over properly.
    /// </summary>
    private bool headingToBase = false;

    void StartHeadingToBase(AIBlackboard bb)
    {
        if (bb.carryAmount <= 0f) return; // Nothing to deliver, brain will switch to Idle
        if (bb.baseBuilding == null) return;
        if (headingToBase) return; // Already started heading to base

        // Only latch headingToBase on success so a rejected set can retry;
        // the ForceReeval below hands over to ReturnToBase either way
        if (AINavHelper.TrySetDestination(bb.agent, bb.baseBuilding.transform.position))
        {
            headingToBase = true;
            bb.agent.isStopped = false;
            displayName = "Returning to base";
        }

        // Force brain to re-evaluate immediately so ReturnToBase takes over
        if (bb.brain != null)
            bb.brain.ForceReeval();
    }

    void UnregisterFromNode(AIBlackboard bb)
    {
        if (bb.isRegisteredAtNode && bb.targetResource != null)
        {
            bb.targetResource.UnregisterWorker(bb.worker);
            bb.isRegisteredAtNode = false;
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        bb.worker.StopGatheringSoundPublic();

        // Release claim
        if (bb.targetResource != null)
        {
            bb.targetResource.UnclaimNode(bb.worker);
        }
        UnregisterFromNode(bb);
        hasValidGatherPoint = false;
        unreachableTimer = 0f;
        headingToBase = false;
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: Deliver carried resources to the nearest drop-off.
/// Uses multiple delivery checks with a timer-based fallback to prevent
/// workers from getting stuck near the campfire due to NavMesh carving
/// or agent stoppingDistance edge cases.
///
/// Phase 6.25: delivery is measured from the building's collider EDGE
/// (bb.deliveryDistance = 1.5 from the edge), not its center. The campfire
/// carves the NavMesh, so center distance never gets small — the old
/// center-based check only ever succeeded via the timer fallbacks.
///
/// 2026-09-08: the drop-off destination is a claimed slot on the building's
/// edge ring (<see cref="DropoffRing"/>), so a group returning from one forest
/// fans out over its sides instead of queueing on the one closest face. A
/// worker stopped behind someone else is treated as arrived after a short
/// stall rather than the old 3 s / 8 s waits.
///
/// 2026-09-16: the destination is the NEAREST drop-off of the colony
/// (<see cref="Dropoff.Nearest"/>) — the campfire or a <see cref="Storehouse"/>
/// — chosen on entry and held for the trip (<see cref="AIBlackboard.dropoff"/>).
/// One colony store: the hand-in itself is the same wherever it happens.
/// </summary>
public class ReturnToBaseExecutor : ActionExecutor
{
    public override string DisplayName => "Returning to base";

    // Timer to detect when the worker has been trying to return for too long
    private float returnTimer;

    // Drop-off collider, cached on entry for edge-distance checks
    private Collider dropoffCollider;

    // Stopped short behind another colonist: after this long standing still within
    // StallReach of the edge, hand over from where we are (the fire is a big warm target)
    private const float StallSeconds = 1.5f;
    private const float StallReach = 3f;
    private float stallTimer;

    public override void OnEnter(AIBlackboard bb)
    {
        returnTimer = 0f;
        stallTimer = 0f;

        // The nearest place to hand in, chosen once per trip. Falls back to the
        // home fire so a colonist with no living drop-off still delivers in place.
        float unused;
        bb.dropoff = Dropoff.Nearest(bb.faction, bb.transform.position, out unused);
        if (bb.dropoff == null) bb.dropoff = bb.baseBuilding;
        dropoffCollider = bb.dropoff != null ? bb.dropoff.ApproachCollider : null;

        if (bb.dropoff == null || !bb.agent.isOnNavMesh || !bb.agent.enabled) return;

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
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);

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
        // The drop-off fell mid-trip (a raid burned the Storehouse): pick again
        // rather than deliver into thin air; with nothing left, hand in where we stand.
        if (bb.dropoff != null && (!bb.dropoff.IsAlive || (bb.dropoff as Object) == null))
        {
            ReleaseSlot(bb);
            float unused;
            bb.dropoff = Dropoff.Nearest(bb.faction, bb.transform.position, out unused);
            dropoffCollider = bb.dropoff != null ? bb.dropoff.ApproachCollider : null;
            returnTimer = 0f;
            if (bb.dropoff != null && bb.agent.isOnNavMesh && bb.agent.enabled
                && AINavHelper.TrySetDestination(bb.agent, GetDropoffPoint(bb)))
                bb.agent.isStopped = false;
        }
        if (bb.dropoff == null)
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

        // Distance to the drop-off's collider edge (center distance if no collider)
        float edgeDistance = TargetingUtil.EdgeDistance(
            bb.transform.position, bb.dropoff.transform, dropoffCollider);

        // --- Delivery checks (from most specific to most generous) ---

        // 1. Within delivery distance of the edge
        bool withinRange = edgeDistance <= bb.deliveryDistance;

        // 2. Agent finished its path and is reasonably close
        bool pathFinished = bb.agent.isOnNavMesh
            && !bb.agent.pathPending
            && bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.5f;
        bool pathFinishedNearBase = pathFinished && edgeDistance <= bb.deliveryDistance + 1.5f;

        // 3. Agent has stopped moving and is in the general area
        bool agentStopped = bb.agent.isOnNavMesh && bb.agent.velocity.sqrMagnitude < 0.05f;
        bool stoppedNearBase = agentStopped && edgeDistance <= bb.deliveryDistance + 1.5f;

        // 3b. Stalled behind someone: standing still a little further out for
        //     StallSeconds (a queue, not a lost path — a lost path re-issues below)
        stallTimer = agentStopped && edgeDistance <= bb.deliveryDistance + StallReach
            ? stallTimer + Time.deltaTime : 0f;
        bool stalledNearBase = stallTimer >= StallSeconds;

        // 4. Timer fallback: been trying to return for 3+ seconds and within generous range
        bool timerFallback = returnTimer > 3f && edgeDistance <= bb.deliveryDistance + 3f;

        // 5. Nuclear fallback: been trying for 8+ seconds, deliver from anywhere
        //    (handles pathfinding failures, NavMesh issues, etc.)
        bool nuclearFallback = returnTimer > 8f && bb.carryAmount > 0f;

        if (withinRange || pathFinishedNearBase || stoppedNearBase || stalledNearBase || timerFallback || nuclearFallback)
        {
            if (bb.dropoff is Storehouse && (withinRange || pathFinishedNearBase || stoppedNearBase || stalledNearBase))
                DevQuests.Signal("dropoff:storehouse");
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
        Deliver(bb);
    }

    /// <summary>
    /// Hand in everything in the colonist's hands: the pooled resource under
    /// bb.carryType and the hauled materials on bb.carryItem. The ONE delivery
    /// path (2026-09-07: GearUpExecutor banks through here too). Safe with
    /// empty hands. Ends in a ForceReeval so the brain moves on at once.
    /// Where the colonist stands does not matter — one colony store.
    /// </summary>
    public static void Deliver(AIBlackboard bb)
    {
        BankMaterials(bb);
        if (bb.carryAmount <= 0f)
        {
            if (bb.brain != null) bb.brain.ForceReeval();
            return;
        }

        int amountToDeliver = Mathf.RoundToInt(bb.carryAmount);

        // carryType, not assignedResourceType: a job change mid-trip still delivers
        // what was actually gathered under the old job
        bb.faction.Resources.Add(bb.carryType, amountToDeliver);

        bb.carryAmount = 0f;
        bb.worker.carryAmount = 0f;

        // Force immediate re-evaluation so the brain switches away
        // instead of idling at base for up to 0.3s
        if (bb.brain != null)
            bb.brain.ForceReeval();
    }

    /// <summary>
    /// Put the sticks and chunks a hauled pickup was made of into the campfire
    /// stockpile, on top of the pooled resource they are also worth
    /// (2026-09-03). This is what lets colonists pay for research and spears;
    /// before it, every stick a colonist carried home dissolved into wood and
    /// the stockpile only ever filled by the player's own hands. Whatever does
    /// not fit is lost, which is the stockpile cap doing its job. The campfire
    /// stockpile IS the colony store, so a Storehouse hand-in lands here too.
    /// </summary>
    static void BankMaterials(AIBlackboard bb)
    {
        if (bb.carryItem == null || bb.carryItemCount <= 0) return;

        BaseBuilding fire = bb.baseBuilding != null ? bb.baseBuilding : bb.faction.Campfire;
        if (fire != null) fire.Stockpile.Add(bb.carryItem, bb.carryItemCount);

        bb.carryItem = null;
        bb.carryItemCount = 0;
    }

    /// <summary>
    /// Walkable point on the drop-off's edge at this worker's claimed slot (the
    /// free bearing nearest its line of approach). The building carves the
    /// NavMesh, so the slot point goes through the shared approach-point pattern.
    /// </summary>
    Vector3 GetDropoffPoint(AIBlackboard bb)
    {
        int slot = bb.dropoff.ClaimDropoffSlot(bb.worker, bb.transform.position);
        return bb.dropoff.DropoffPoint(slot);
    }

    static void ReleaseSlot(AIBlackboard bb)
    {
        if (bb.dropoff != null) bb.dropoff.ReleaseDropoffSlot(bb.worker);
        else if (bb.worker != null) bb.worker.dropoffSlot = -1;
    }

    public override void OnExit(AIBlackboard bb)
    {
        ReleaseSlot(bb);
        bb.dropoff = null;

        // Restore stopping distance for gathering
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.stoppingDistance = Worker.GatherStopDistance;
        }
    }
}

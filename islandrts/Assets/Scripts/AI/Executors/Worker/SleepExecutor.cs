using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: bed (2026-09-16). At their hour (see <see cref="SleepUrge"/>) a
/// colonist hands in whatever they carry at the fire, then walks home: a hut dweller
/// slips inside (the garrison hide, exactly as Flee does), anyone homed to the fire
/// or homeless lies down on the ground a few metres off its edge. They stay put until
/// the urge drops — their wake hour, or a raider at the fire-side bed — and OnExit
/// stands them up again.
/// </summary>
/// <remarks>
/// Three phases. <b>ToFire</b> reuses GearUp's arrival rules (drop-off slot, edge
/// distance, stopped-near-enough, an 8 s give-up) and delivers through the one
/// delivery path, <see cref="ReturnToBaseExecutor.Deliver"/>. <b>ToBed</b> walks to
/// the home hut's approach point or to a fire-side bed spot: eight bearings off the
/// worker's own entity id so a crowd spreads round the fire, <see cref="BedClearance"/>
/// off the fire's collider edge so a sleeper never lies on a delivery slot, NavMesh
/// sampled and <see cref="Loiter.IsClear"/>. <b>Asleep</b> does nothing but watch the
/// hut. The lying pose tilts the art's <c>Model</c> child about its base pivot; it is
/// display only, skipped headless, and restored on exit.
///
/// Only <see cref="Worker.SetGarrisoned"/>'s two callers exist: Flee and this. Both
/// restore in OnExit before any other executor runs. <c>bb.asleepInHut</c> is written
/// here only, and only while the body is hidden.
/// </remarks>
public class SleepExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Turning in";

    private const float GarrisonEdgeDistance = 1.1f;   // same as Flee: close enough to slip inside
    private const float BedArrive = 1.2f;              // arrival radius at a fire-side bed
    private const float BedClearance = 3f;             // bed spot this far off the fire's collider edge (delivery slots stay free)
    private const float BedJitter = 1.5f;              // ...plus up to this
    private const float MaxWalkSeconds = 20f;          // then sleep where we stand
    private const float GiveUpSeconds = 8f;            // ToFire: ReturnToBase's nuclear fallback, same reason
    private const float LieAngle = 82f;                // tilt of the Model child when lying by the fire

    private enum Phase { ToFire, ToBed, Asleep }

    private Phase phase;
    private IHousing home;
    private Hut hut;
    private Collider hutCollider;
    private Collider fireCollider;
    private Vector3 bedPoint;
    private bool destinationQueued;
    private bool garrisoned;
    private bool lying;
    private float walkTimer;
    private Transform model;
    private Quaternion modelRestRotation;

    public override void OnEnter(AIBlackboard bb)
    {
        garrisoned = false;
        lying = false;
        destinationQueued = false;
        walkTimer = 0f;
        hut = null;
        hutCollider = null;
        fireCollider = bb.baseBuilding != null ? bb.baseBuilding.GetComponent<Collider>() : null;

        // The day's node claim ends now
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

        bool carrying = bb.carryAmount > 0.01f || (bb.carryItem != null && bb.carryItemCount > 0);
        if (carrying && bb.baseBuilding != null && AgentReady(bb))
            StartToFire(bb);
        else
            StartToBed(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        switch (phase)
        {
            case Phase.ToFire: UpdateToFire(bb); break;
            case Phase.ToBed: UpdateToBed(bb); break;
            case Phase.Asleep: UpdateAsleep(bb); break;
        }
    }

    // ---- Deliver first ----

    void StartToFire(AIBlackboard bb)
    {
        phase = Phase.ToFire;
        displayName = "Turning in";
        walkTimer = 0f;
        bb.agent.stoppingDistance = 0.5f;
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        IssueFireMove(bb);
    }

    void IssueFireMove(AIBlackboard bb)
    {
        int slot = bb.baseBuilding.ClaimDropoffSlot(bb.worker, bb.transform.position);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, bb.baseBuilding.DropoffPoint(slot));
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void UpdateToFire(AIBlackboard bb)
    {
        if (bb.baseBuilding == null || !AgentReady(bb))
        {
            ArriveAtFire(bb);
            return;
        }

        walkTimer += Time.deltaTime;

        // A stuck reset nulls blackboard fields and ForceReevals — bail for this tick, re-issue next
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            destinationQueued = false;
            return;
        }
        if (!destinationQueued) IssueFireMove(bb);

        float edge = TargetingUtil.EdgeDistance(bb.transform.position, bb.baseBuilding.transform, fireCollider);
        bool withinRange = edge <= bb.deliveryDistance;
        bool pathDone = destinationQueued && !bb.agent.pathPending && bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.5f;
        bool stopped = bb.agent.velocity.sqrMagnitude < 0.05f;
        bool nearEnough = edge <= bb.deliveryDistance + 1.5f && (pathDone || stopped);
        if (withinRange || nearEnough || walkTimer > GiveUpSeconds) ArriveAtFire(bb);
    }

    void ArriveAtFire(AIBlackboard bb)
    {
        DevQuests.Signal("sleep:delivered");   // the load lands in the pool before bed
        ReturnToBaseExecutor.Deliver(bb);      // ForceReevals; Sleep still wins, so we carry on
        if (bb.baseBuilding != null) bb.baseBuilding.ReleaseDropoffSlot(bb.worker);
        StartToBed(bb);
    }

    // ---- Walk home ----

    void StartToBed(AIBlackboard bb)
    {
        phase = Phase.ToBed;
        displayName = "Heading to bed";
        walkTimer = 0f;
        destinationQueued = false;
        home = bb.faction != null && bb.faction.Population != null ? bb.faction.Population.HomeOf(bb.worker) : null;
        hut = home as Hut;
        hutCollider = hut != null ? hut.GetComponent<Collider>() : null;

        if (!AgentReady(bb))
        {
            FallAsleep(bb);   // nowhere to walk — sleep where we stand
            return;
        }
        bb.agent.stoppingDistance = 0.5f;
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        if (hut == null) bedPoint = PickBedPoint(bb);
        IssueBedMove(bb);
    }

    void IssueBedMove(AIBlackboard bb)
    {
        if (!AgentReady(bb)) return;
        Vector3 dest = hut != null
            ? TargetingUtil.GetApproachPoint(bb.transform.position, hut.transform, hutCollider)
            : bedPoint;
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, dest);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    void UpdateToBed(AIBlackboard bb)
    {
        if (!AgentReady(bb))
        {
            FallAsleep(bb);
            return;
        }

        if (hut != null && !HutAlive())
        {
            StartToBed(bb);   // the hut fell while we walked: re-read the home (a new hut, or the fire)
            return;
        }

        walkTimer += Time.deltaTime;

        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            destinationQueued = false;
            return;
        }
        if (!destinationQueued) IssueBedMove(bb);

        if (hut != null)
        {
            float edge = TargetingUtil.EdgeDistance(bb.transform.position, hut.transform, hutCollider);
            bool pathDone = destinationQueued && !bb.agent.pathPending && !bb.agent.hasPath;
            if (edge <= GarrisonEdgeDistance || (pathDone && edge <= GarrisonEdgeDistance + 1.5f) || walkTimer > MaxWalkSeconds)
                FallAsleep(bb);
            return;
        }

        float dist = Vector3.Distance(bb.transform.position, bedPoint);
        bool done = destinationQueued && !bb.agent.pathPending
            && (!bb.agent.hasPath || bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.3f);
        if (dist <= BedArrive || done || walkTimer > MaxWalkSeconds) FallAsleep(bb);
    }

    /// <summary>
    /// A spot off the fire's edge for a colonist with no hut: a bearing from the
    /// worker's own instance id (stable, spreads a crowd), stepping round by 45°
    /// until one is on the NavMesh, off the delivery edge and clear of gateways.
    /// Falls back to where we stand.
    /// </summary>
    Vector3 PickBedPoint(AIBlackboard bb)
    {
        Vector3 here = bb.transform.position;
        BaseBuilding fire = bb.baseBuilding != null ? bb.baseBuilding : (bb.faction != null ? bb.faction.Campfire : null);
        if (fire == null) return here;

        Collider col = fire.GetComponent<Collider>();
        float radius = col != null ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) : 2f;
        int id = bb.worker != null ? bb.worker.GetEntityId().GetHashCode() : 0;
        float baseAngle = ((id * 0.618034f) % 1f + 1f) % 1f * 360f;
        float jitter = ((id * 0.381966f) % 1f + 1f) % 1f * BedJitter;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = (baseAngle + attempt * 45f) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 candidate = fire.transform.position + dir * (radius + BedClearance + jitter);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas)) continue;
            if (col != null && TargetingUtil.EdgeDistance(hit.position, fire.transform, col) < BedClearance - 0.5f) continue;
            if (!Loiter.IsClear(hit.position)) continue;
            return hit.position;
        }
        return here;
    }

    // ---- Asleep ----

    void FallAsleep(AIBlackboard bb)
    {
        phase = Phase.Asleep;
        displayName = "Sleeping";
        destinationQueued = false;

        if (AgentReady(bb))
        {
            bb.agent.ResetPath();
            bb.agent.isStopped = true;
            Worker.SetStationaryAvoidance(bb.agent);
        }

        if (hut != null && HutAlive())
        {
            bb.worker.SetGarrisoned(true);
            garrisoned = true;
            bb.asleepInHut = true;
            DevQuests.Signal("sleep:hut");
        }
        else
        {
            LieDown(bb);
            DevQuests.Signal("sleep:fire");
        }
        DevQuests.Signal("sleep");
        Persona p = bb.worker != null ? bb.worker.Persona : null;
        if (p != null) DevQuests.Signal("trait:" + p.TraitSlug);
    }

    void UpdateAsleep(AIBlackboard bb)
    {
        if (!garrisoned) return;
        if (HutAlive()) return;

        // The hut fell out from under us: up, and find somewhere else to lie
        bb.worker.SetGarrisoned(false);
        garrisoned = false;
        bb.asleepInHut = false;
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        StartToBed(bb);
    }

    bool HutAlive() => hut != null && hut.CachedHealth != null && hut.CachedHealth.IsAlive;

    /// <summary>The body on its side by the fire: the art's Model child tilted about its base pivot. Display only.</summary>
    void LieDown(AIBlackboard bb)
    {
        if (SimHooks.Headless || lying) return;
        model = bb.transform.Find("Model");
        if (model == null) return;
        modelRestRotation = model.localRotation;
        model.localRotation = modelRestRotation * Quaternion.Euler(0f, 0f, LieAngle);
        lying = true;
    }

    void StandUp()
    {
        if (!lying) return;
        lying = false;
        if (model != null) model.localRotation = modelRestRotation;
        model = null;
    }

    static bool AgentReady(AIBlackboard bb)
        => bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh;

    public override void OnExit(AIBlackboard bb)
    {
        // Why we are up: the hour, or a raider at the bedside
        if (phase == Phase.Asleep)
        {
            AIWorldState ws = AIWorldState.Instance;
            if (ws != null && ws.GetNearbyHostileCount(bb.transform.position, bb.faction) > 0) DevQuests.Signal("sleep:raid");
            else DevQuests.Signal("sleep:wake");
        }

        if (garrisoned)
        {
            bb.worker.SetGarrisoned(false);
            garrisoned = false;
        }
        bb.asleepInHut = false;
        StandUp();

        if (bb.baseBuilding != null) bb.baseBuilding.ReleaseDropoffSlot(bb.worker);
        else if (bb.worker != null) bb.worker.dropoffSlot = -1;

        hut = null;
        hutCollider = null;
        home = null;
        if (AgentReady(bb))
        {
            bb.agent.stoppingDistance = Worker.GatherStopDistance;
            bb.agent.isStopped = false;
            Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        }
    }
}

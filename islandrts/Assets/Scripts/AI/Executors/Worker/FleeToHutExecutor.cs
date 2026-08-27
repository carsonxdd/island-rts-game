using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: run to the nearest hut and GARRISON inside it until the
/// threat passes (2026-08-26 redesign).
///
/// Replaces the old "run 15u directly away from the enemy" behavior, which
/// routinely ran workers into the water — and whose hut-shelter branch pathed
/// to hut CENTERS: huts carve the NavMesh, so those destinations were silently
/// rejected and the worker just stood there while enemies closed in.
///
/// Behavior:
///  - Nearest alive hut → move to its carve-safe approach point
///    (TargetingUtil.GetApproachPoint) → at collider-edge arrival, slip inside
///    (Worker.SetGarrisoned: renderers/agent/collider off). Enemies never
///    target workers, so garrison is visual shelter + removes the worker from
///    the crowd sim while hiding.
///  - No huts alive → gather at the campfire edge (no hiding).
///  - Neither → run directly away from the nearest enemy (legacy last resort).
///  - When the threat clears, Flee's considerations drop to 0, the brain exits
///    this action, and OnExit pops the worker back out at the hut edge.
///  - If the sheltering hut is destroyed, the worker pops out immediately and
///    re-picks a shelter.
/// </summary>
public class FleeToHutExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Fleeing!";

    private const float GarrisonEdgeDistance = 1.1f;  // close enough to slip inside
    private const float RepickInterval = 0.75f;       // re-validate shelter this often
    private const float FleeDistance = 15f;           // legacy run-away fallback

    private Hut shelterHut;
    private Collider shelterCollider;
    private bool garrisoned;
    private float repickTimer;
    private bool destinationQueued;

    public override void OnEnter(AIBlackboard bb)
    {
        // Release resource claims
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

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        garrisoned = false;
        shelterHut = null;
        shelterCollider = null;
        repickTimer = 0f;
        destinationQueued = false;

        // Moving errand — drop stationary-importance if we were gathering/idle
        Worker.RollMovingAvoidance(bb.agent);

        PickShelterAndMove(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (garrisoned)
        {
            // Shelter died out from under us → pop out and re-pick
            if (shelterHut == null || shelterHut.CachedHealth == null || !shelterHut.CachedHealth.IsAlive)
            {
                bb.worker.SetGarrisoned(false);
                garrisoned = false;
                shelterHut = null;
                shelterCollider = null;
                Worker.RollMovingAvoidance(bb.agent);
                PickShelterAndMove(bb);
            }
            // Safe inside; the brain exits Flee when the threat clears.
            return;
        }

        // Stuck resolution while moving. Honor the bool: a reset already
        // ForceReeval'd — bail out for this tick (see Phase 6.25 gotcha).
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            return;

        // Re-validate the shelter pick periodically, and retry immediately if
        // the last TrySetDestination was rejected (throttle/NavMesh recalc)
        repickTimer -= Time.deltaTime;
        if (repickTimer <= 0f || !destinationQueued)
        {
            PickShelterAndMove(bb);
        }

        // Arrived at the hut edge? Slip inside.
        if (shelterHut != null)
        {
            if (shelterHut.CachedHealth == null || !shelterHut.CachedHealth.IsAlive)
            {
                shelterHut = null;
                shelterCollider = null;
                return;
            }

            float edge = TargetingUtil.EdgeDistance(bb.transform.position, shelterHut.transform, shelterCollider);
            if (edge <= GarrisonEdgeDistance)
            {
                if (bb.agent.isOnNavMesh) bb.agent.ResetPath();
                bb.worker.SetGarrisoned(true);
                garrisoned = true;
                displayName = "Hiding";
            }
        }
    }

    /// <summary>
    /// Pick the best shelter (hut → campfire → run-away) and issue movement.
    /// Sets destinationQueued=false when the set was rejected so OnUpdate
    /// retries next frame instead of standing still.
    /// </summary>
    void PickShelterAndMove(AIBlackboard bb)
    {
        repickTimer = RepickInterval;

        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh)
        {
            destinationQueued = false;
            return;
        }

        float unused;
        Hut hut = TargetingUtil.FindNearest(Hut.ActiveList, bb.transform.position, 0f, out unused);
        if (hut != null)
        {
            if (hut != shelterHut)
            {
                shelterHut = hut;
                shelterCollider = hut.GetComponent<Collider>();
            }
            // Huts carve the NavMesh — destination MUST be the approach point
            Vector3 approach = TargetingUtil.GetApproachPoint(bb.transform.position, hut.transform, shelterCollider);
            destinationQueued = AINavHelper.TrySetDestination(bb.agent, approach);
            if (destinationQueued) bb.agent.isStopped = false;
            displayName = "Fleeing to hut!";
            return;
        }

        shelterHut = null;
        shelterCollider = null;

        // No huts: crowd at the campfire (no hiding)
        if (bb.baseBuilding != null)
        {
            Collider fireCollider = bb.baseBuilding.GetComponent<Collider>();
            Vector3 approach = TargetingUtil.GetApproachPoint(
                bb.transform.position, bb.baseBuilding.transform, fireCollider);
            destinationQueued = AINavHelper.TrySetDestination(bb.agent, approach);
            if (destinationQueued) bb.agent.isStopped = false;
            displayName = "Fleeing to camp!";
            return;
        }

        // Last resort: run away from the nearest enemy (populated by EnemyPresence)
        displayName = "Fleeing!";
        if (bb.nearestEnemy == null)
        {
            destinationQueued = false;
            return;
        }

        Vector3 threatDir = (bb.nearestEnemy.position - bb.transform.position).normalized;
        Vector3 fleePoint = bb.transform.position - threatDir * FleeDistance;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(fleePoint, out hit, FleeDistance, NavMesh.AllAreas))
        {
            destinationQueued = AINavHelper.TrySetDestination(bb.agent, hit.position);
            if (destinationQueued) bb.agent.isStopped = false;
        }
        else
        {
            destinationQueued = false;
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        if (garrisoned)
        {
            bb.worker.SetGarrisoned(false);
            garrisoned = false;
        }
        shelterHut = null;
        shelterCollider = null;

        if (bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
            Worker.RollMovingAvoidance(bb.agent);
        }
    }
}

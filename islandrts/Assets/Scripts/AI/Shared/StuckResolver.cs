using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared unstick component for every unit type (Worker, Warrior, Enemy). Sits alongside
/// the NavMeshAgent and is driven by whichever executor is currently moving the unit.
/// </summary>
/// <remarks>
/// Two escalating remedies, cheapest first:
/// 1. Phase-through - a unit that has somewhere to be but is barely moving is usually
///    nose-to-nose with another agent. Shrinking its radius and switching avoidance off
///    lets the pair slide past each other; the original values are restored once it is
///    moving again, or unconditionally after phaseMaxDuration so a unit can never be
///    left permanently ghosted.
/// 2. Stuck reset - two consecutive checks with almost no movement, or a path that stays
///    PathInvalid past the grace period, fires onStuckReset so the unit drops its target
///    and the brain picks something else.
///
/// The expensive checks run on one frame in five (staggered per unit via frameOffset) so
/// a large population never evaluates them all on the same frame. Because of that, every
/// timer accumulates the REAL elapsed time between staggered checks rather than
/// Time.deltaTime, which would only count one frame in five.
///
/// Gotcha: onStuckReset is invoked mid-call and nulls blackboard targets, so a caller
/// must either return for the tick when UpdateMoving reports true, or re-null-check
/// everything the callback touches.
/// </remarks>
public class StuckResolver : MonoBehaviour
{
    // --- Phase-through (remedy 1: two agents jammed face to face) ---
    private float faceToFaceTimer = 0f;      // Time spent trying to move while barely moving
    private float phaseThreshold = 2f;       // Seconds of that before phasing kicks in
    private bool isPhasing = false;
    private float phaseActiveTimer = 0f;
    private float phaseMinDuration = 3f;     // Hold the phase this long even if speed recovers early
    private float phaseMaxDuration = 10f;    // Hard cap: always restore, so phasing can never stick
    private float savedRadius;               // Agent settings captured before phasing, restored after
    private ObstacleAvoidanceType savedAvoidance;

    // --- Stuck detection (remedy 2: give up and let the brain re-decide) ---
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private float stuckCheckInterval = 1.5f; // Two consecutive failed checks fire the reset, so ~3s total
    private bool wasStuckLastCheck = false;

    // A path can read PathInvalid for a frame or two while the NavMesh rebuilds (every
    // building placement flattens terrain and kicks an async rebake), so an invalid path
    // only counts as stuck once it persists past this grace period.
    private float pathInvalidTimer = 0f;
    private float pathInvalidGracePeriod = 0.5f;

    // Spreads the expensive checks over 5 frames so a whole population never evaluates
    // them together. lastStaggeredCheckTime supplies the real elapsed time between those
    // checks (Time.deltaTime would undercount it by 5x).
    private int frameOffset;
    private float lastStaggeredCheckTime;

    private NavMeshAgent agent;

    /// <summary>
    /// Fired when the unit has been stuck long enough to give up. Units use it to drop
    /// their current target and force a brain re-evaluation. Invoked from inside
    /// UpdateMoving - see the gotcha in the class remarks.
    /// </summary>
    public System.Action onStuckReset;

    /// <summary>
    /// Wires up the agent and picks this unit's staggered check frame. The initial
    /// stuckTimer is randomized so units that spawn together do not reach their first
    /// check on the same frame either.
    /// </summary>
    /// <param name="unitIndex">Any per-unit number; only its value mod 5 matters.</param>
    public void Initialize(NavMeshAgent navAgent, int unitIndex)
    {
        agent = navAgent;
        frameOffset = unitIndex % 5;
        lastPosition = transform.position;
        stuckTimer = Random.Range(0f, stuckCheckInterval);
        lastStaggeredCheckTime = Time.time;
    }

    /// <summary>
    /// Call every frame from the executor that is currently moving this unit (never while
    /// it stands still, or standing would read as being stuck). Returns true if a stuck
    /// reset fired this frame - the caller must stop touching blackboard targets for the
    /// rest of the tick when it does.
    /// </summary>
    public bool UpdateMoving()
    {
        if (agent == null || !agent.enabled) return false;

        // Off-mesh recovery: an agent that ends up off the NavMesh (terrain rebuilt under
        // it, spawned on a seam) cannot path at all, so warp it to the nearest valid point,
        // falling back to the campfire if nothing near it is walkable.
        if (!agent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else if (BaseBuilding.ActiveList.Count > 0)
            {
                Vector3 campfirePos = BaseBuilding.ActiveList[0].transform.position;
                if (NavMesh.SamplePosition(campfirePos, out hit, 5f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
            }
            return false;
        }

        // Expensive checks only on this unit's designated frame (one in five).
        if ((Time.frameCount + frameOffset) % 5 == 0)
        {
            float elapsed = Time.time - lastStaggeredCheckTime;
            lastStaggeredCheckTime = Time.time;

            CheckFaceToFaceStuck(elapsed);
            return CheckIfStuck(elapsed);
        }
        else if (isPhasing)
        {
            // Phasing is tracked every frame, not one in five, so the restore lands
            // promptly once the unit is moving again.
            TrackPhaseTimer();
        }

        return false;
    }

    /// <summary>
    /// Detects "has somewhere to go but is not moving" and starts phasing after
    /// phaseThreshold seconds of it.
    /// </summary>
    void CheckFaceToFaceStuck(float elapsed)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;

        bool tryingToMove = agent.remainingDistance > agent.stoppingDistance + 1f;
        bool movingSlowly = agent.velocity.magnitude < 0.3f;

        if (tryingToMove && movingSlowly && !isPhasing)
        {
            faceToFaceTimer += elapsed;
            if (faceToFaceTimer >= phaseThreshold)
            {
                // Shrink and stop avoiding, so the jammed pair can slide through each other.
                savedRadius = agent.radius;
                savedAvoidance = agent.obstacleAvoidanceType;
                agent.radius = 0.1f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                isPhasing = true;
                phaseActiveTimer = 0f;
            }
        }
        else if (!isPhasing)
        {
            faceToFaceTimer = 0f;
        }

        if (isPhasing)
        {
            TrackPhaseTimer();
        }
    }

    /// <summary>Restores the agent's real radius and avoidance once it is moving again, or
    /// unconditionally at phaseMaxDuration so phasing can never stick.</summary>
    void TrackPhaseTimer()
    {
        if (!isPhasing || agent == null || !agent.isOnNavMesh) return;

        phaseActiveTimer += Time.deltaTime;

        if (phaseActiveTimer >= phaseMaxDuration)
        {
            RestorePhasing();
            ResetStuckDetection();
            return;
        }

        if (phaseActiveTimer >= phaseMinDuration && agent.velocity.magnitude > 0.5f)
        {
            agent.radius = savedRadius;
            agent.obstacleAvoidanceType = savedAvoidance;
            faceToFaceTimer = 0f;
            phaseActiveTimer = 0f;
            isPhasing = false;
        }
    }

    /// <summary>
    /// The give-up check. Fires onStuckReset on a sustained invalid path, or on two
    /// consecutive intervals during which the unit moved less than half a metre. Requiring
    /// two in a row keeps one slow interval (a crowd, a sharp turn) from cancelling a
    /// perfectly good errand.
    /// </summary>
    bool CheckIfStuck(float elapsed)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return false;

        if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            pathInvalidTimer += elapsed;
            if (pathInvalidTimer >= pathInvalidGracePeriod)
            {
                pathInvalidTimer = 0f;
                RestorePhasing();
                ResetStuckDetection();
                onStuckReset?.Invoke();
                return true;
            }
        }
        else
        {
            pathInvalidTimer = 0f;
        }

        stuckTimer += elapsed;
        if (stuckTimer >= stuckCheckInterval)
        {
            float distanceMoved = Vector3.Distance(transform.position, lastPosition);
            bool isStuck = distanceMoved < 0.5f;

            if (isStuck)
            {
                if (wasStuckLastCheck)
                {
                    RestorePhasing();
                    ResetStuckDetection();
                    onStuckReset?.Invoke();
                    return true;
                }
                else
                {
                    wasStuckLastCheck = true;
                }
            }
            else
            {
                wasStuckLastCheck = false;
            }

            lastPosition = transform.position;
            stuckTimer = 0f;
        }
        return false;
    }

    /// <summary>
    /// Clears every timer and re-anchors the reference position. Call it whenever the unit
    /// is deliberately given a new destination, or the ground it covered on the old errand
    /// gets scored against the new one.
    /// </summary>
    public void ResetStuckDetection()
    {
        lastPosition = transform.position;
        stuckTimer = 0f;
        wasStuckLastCheck = false;
        pathInvalidTimer = 0f;
        lastStaggeredCheckTime = Time.time;
    }

    /// <summary>
    /// Puts the agent's real radius and avoidance back if it is mid-phase. Safe to call at
    /// any time; a unit must never be left phasing when it stops or changes action.
    /// </summary>
    public void RestorePhasing()
    {
        if (isPhasing && agent != null && agent.isOnNavMesh)
        {
            agent.radius = savedRadius;
            agent.obstacleAvoidanceType = savedAvoidance;
            isPhasing = false;
            phaseActiveTimer = 0f;
            faceToFaceTimer = 0f;
        }
    }
}

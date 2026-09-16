using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared stuck detection and recovery for every moving unit (extracted from the
/// Worker / Warrior phase-through logic in Phase 6.11). Two detectors:
/// <list type="bullet">
/// <item>the RECOVERY LADDER (2026-09-16): speed under 15% of the desired speed
/// for 0.75 s while a path is being walked starts a stall, and each rung fires
/// once per stall — 1 (0.75 s) a lateral nudge to the right with avoidance
/// ignored for 0.5 s; 2 (2.0 s) a fresh path to the same destination; 3 (4.0 s)
/// pass-through, the solid radius and no avoidance for 1.5 s; 4 (8.0 s) abandon,
/// <see cref="onStuckReset"/> so the executor re-picks, plus a rate-limited
/// warning. <see cref="Escalations"/>[n] counts each rung for the F3 overlay; in
/// a healthy build 3 is rare and 4 never fires. The old face-to-face phasing
/// (2 s → radius 0.1 for 3-10 s) is rung 3 of this ladder;</item>
/// <item>the DISTANCE check: two consecutive 1.5 s windows moved under 0.5 m, or
/// an invalid path held past a grace period, fire <see cref="onStuckReset"/>
/// directly — the ladder's rung 4 for a unit whose agent reports no crawl.</item>
/// </list>
/// Any executor calling <see cref="UpdateMoving"/> must early-return when it
/// reports a reset: the callback nulls blackboard fields mid-call (CLAUDE.md).
/// </summary>
public class StuckResolver : MonoBehaviour
{
    // ---- the ladder ---------------------------------------------------------
    public static readonly int[] Escalations = new int[5];
    public static void ResetCounters() { for (int i = 0; i < Escalations.Length; i++) Escalations[i] = 0; }
    static float lastAbandonWarning = -100f;

    private const float SlowFraction = 0.15f;
    private const float Step1At = 0.75f, Step2At = 2f, Step3At = 4f, Step4At = 8f;
    private const float NudgeMetres = 0.3f, NudgeIgnoreSeconds = 0.5f, PassThroughSeconds = 1.5f;

    private float slowTimer = 0f;            // seconds of trying-to-move while crawling
    private int step = 0;                    // ladder rung reached this stall
    private bool isPhasing = false;          // agent radius / avoidance are altered right now
    private float restoreAt = -1f;           // Time.time to restore them
    private float savedRadius;               // Agent settings captured before altering, restored after
    private ObstacleAvoidanceType savedAvoidance;

    // ---- the distance check ---------------------------------------------------
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private float stuckCheckInterval = 1.5f; // Two consecutive failed checks fire the reset, so ~3s total
    private bool wasStuckLastCheck = false;
    private float pathInvalidTimer = 0f;
    private float pathInvalidGracePeriod = 0.5f;

    // Stagger: the expensive checks run on one frame in five per unit
    private int frameOffset;
    private float lastStaggeredCheckTime;

    private NavMeshAgent agent;

    BaseBuilding OwnCampfire()
    {
        IOwned o = GetComponent<IOwned>();
        return o != null ? o.Faction.Campfire : null;
    }

    /// <summary>Fired when the unit gives up on its current movement; the owner re-picks.</summary>
    public System.Action onStuckReset;

    public void Initialize(NavMeshAgent navAgent, int unitIndex)
    {
        agent = navAgent;
        UnitGrid.Ensure();   // the shared spatial layer rides with the first unit (2026-09-16)
        frameOffset = unitIndex % 5;
        lastPosition = transform.position;
        stuckTimer = Random.Range(0f, stuckCheckInterval);
        lastStaggeredCheckTime = Time.time;
    }

    /// <summary>
    /// Call every frame while moving. Returns true when a reset fired this call
    /// (the caller must early-return: blackboard fields were just nulled).
    /// </summary>
    public bool UpdateMoving()
    {
        if (agent == null || !agent.enabled) return false;

        // Off the mesh entirely: warp back on, near here or at the home fire.
        if (!agent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else if (OwnCampfire() != null)
            {
                Vector3 campfirePos = OwnCampfire().transform.position;
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

            if (Ladder(elapsed)) return true;
            return CheckIfStuck(elapsed);
        }
        else if (isPhasing)
        {
            // The restore is tracked every frame, not one in five, so it lands promptly.
            TrackPhaseTimer();
        }

        return false;
    }

    /// <summary>The recovery ladder; true when rung 4 abandoned the task this call.</summary>
    bool Ladder(float elapsed)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return false;
        TrackPhaseTimer();

        bool tryingToMove = agent.hasPath && !agent.isStopped && agent.remainingDistance > agent.stoppingDistance + 0.5f;
        bool crawling = agent.velocity.magnitude < SlowFraction * Mathf.Max(0.1f, agent.speed);
        if (!(tryingToMove && crawling))
        {
            if (!isPhasing) { slowTimer = 0f; step = 0; }
            return false;
        }

        slowTimer += elapsed;
        if (step < 1 && slowTimer >= Step1At)
        {
            step = 1; Escalations[1]++;
            Vector3 heading = agent.velocity.sqrMagnitude > 0.01f ? agent.velocity : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;   // yield to the right, by convention
            agent.Move(right * NudgeMetres);
            Alter(agent.radius, ObstacleAvoidanceType.NoObstacleAvoidance, NudgeIgnoreSeconds);
        }
        if (step < 2 && slowTimer >= Step2At)
        {
            step = 2; Escalations[2]++;
            AINavHelper.TrySetDestination(agent, agent.destination);   // a fresh path to the same place, throttled like every other
        }
        if (step < 3 && slowTimer >= Step3At)
        {
            step = 3; Escalations[3]++;
            Alter(0.1f, ObstacleAvoidanceType.NoObstacleAvoidance, PassThroughSeconds);
        }
        if (step < 4 && slowTimer >= Step4At)
        {
            step = 4; Escalations[4]++;
            if (Time.time - lastAbandonWarning > 5f)
            {
                lastAbandonWarning = Time.time;
                Debug.LogWarning("StuckResolver: " + name + " abandoned its task after " + Step4At + " s stalled at " + transform.position
                    + " (abandons this scene: " + Escalations[4] + ").");
            }
            RestorePhasing();
            ResetStuckDetection();
            onStuckReset?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>Alter the agent's radius and avoidance until <paramref name="seconds"/> from now; the saved values are the ones from BEFORE the first alteration of this stall.</summary>
    void Alter(float radius, ObstacleAvoidanceType avoidance, float seconds)
    {
        if (!isPhasing)
        {
            savedRadius = agent.radius;
            savedAvoidance = agent.obstacleAvoidanceType;
            isPhasing = true;
        }
        agent.radius = radius;
        agent.obstacleAvoidanceType = avoidance;
        restoreAt = Time.time + seconds;
    }

    /// <summary>Restores the agent's real radius and avoidance once the current rung's window has passed.</summary>
    void TrackPhaseTimer()
    {
        if (!isPhasing || agent == null || !agent.isOnNavMesh) return;
        if (Time.time < restoreAt) return;
        agent.radius = savedRadius;
        agent.obstacleAvoidanceType = savedAvoidance;
        isPhasing = false;
    }

    /// <summary>
    /// The distance check: an invalid path held past the grace period, or two
    /// consecutive windows with under 0.5 m of movement, fire the reset.
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

    /// <summary>Call after any deliberate re-path so a legitimate pause is not read as a stall.</summary>
    public void ResetStuckDetection()
    {
        lastPosition = transform.position;
        stuckTimer = 0f;
        wasStuckLastCheck = false;
        pathInvalidTimer = 0f;
        lastStaggeredCheckTime = Time.time;
    }

    /// <summary>Put the agent's real radius and avoidance back and end the stall.</summary>
    public void RestorePhasing()
    {
        if (isPhasing && agent != null && agent.isOnNavMesh)
        {
            agent.radius = savedRadius;
            agent.obstacleAvoidanceType = savedAvoidance;
            isPhasing = false;
        }
        slowTimer = 0f;
        step = 0;
    }
}

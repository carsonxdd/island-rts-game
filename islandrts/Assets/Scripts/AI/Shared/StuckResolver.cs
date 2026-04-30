using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared component that handles phase-through stuck resolution for all unit types.
/// Extracted from duplicate logic in Worker.cs and Warrior.cs.
/// Attach as a component alongside NavMeshAgent.
/// </summary>
public class StuckResolver : MonoBehaviour
{
    // Phase-through (face-to-face stuck resolution)
    private float faceToFaceTimer = 0f;
    private float phaseThreshold = 2f;
    private bool isPhasing = false;
    private float phaseActiveTimer = 0f;
    private float phaseMinDuration = 3f;
    private float phaseMaxDuration = 10f; // Bug 6: auto-restore after this duration
    private float savedRadius;
    private ObstacleAvoidanceType savedAvoidance;

    // Stuck detection
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private float stuckCheckInterval = 1.5f;  // Phase 6.21: tightened from 3s (triggers in ~3s with 2 consecutive stuck checks)
    private bool wasStuckLastCheck = false;

    // Path invalidation grace period
    private float pathInvalidTimer = 0f;
    private float pathInvalidGracePeriod = 0.5f;

    // Frame staggering
    private int frameOffset;
    private float lastStaggeredCheckTime; // Bug 1: track real elapsed between staggered checks

    private NavMeshAgent agent;

    // Event for when unit is stuck for too long
    public System.Action onStuckReset;

    public void Initialize(NavMeshAgent navAgent, int unitIndex)
    {
        agent = navAgent;
        frameOffset = unitIndex % 5;
        lastPosition = transform.position;
        stuckTimer = Random.Range(0f, stuckCheckInterval);
        lastStaggeredCheckTime = Time.time;
    }

    /// <summary>
    /// Call each frame from the unit's Update when the unit is moving.
    /// Returns true if a full reset was triggered.
    /// </summary>
    public bool UpdateMoving()
    {
        if (agent == null || !agent.enabled) return false;

        // Bug 3: Off-mesh recovery — warp unit back onto NavMesh
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

        // Expensive checks only on designated frame
        if ((Time.frameCount + frameOffset) % 5 == 0)
        {
            // Bug 1: compute real elapsed time since last staggered check
            float elapsed = Time.time - lastStaggeredCheckTime;
            lastStaggeredCheckTime = Time.time;

            CheckFaceToFaceStuck(elapsed);
            return CheckIfStuck(elapsed);
        }
        else if (isPhasing)
        {
            TrackPhaseTimer();
        }

        return false;
    }

    void CheckFaceToFaceStuck(float elapsed)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return;

        bool tryingToMove = agent.remainingDistance > agent.stoppingDistance + 1f;
        bool movingSlowly = agent.velocity.magnitude < 0.3f;

        if (tryingToMove && movingSlowly && !isPhasing)
        {
            faceToFaceTimer += elapsed; // Bug 1: real elapsed, not Time.deltaTime
            if (faceToFaceTimer >= phaseThreshold)
            {
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

    void TrackPhaseTimer()
    {
        if (!isPhasing || agent == null || !agent.isOnNavMesh) return;

        phaseActiveTimer += Time.deltaTime;

        // Bug 6: force restore after max duration to prevent permanent phase-through
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

    bool CheckIfStuck(float elapsed)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return false;

        // Path invalid grace period
        if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            pathInvalidTimer += elapsed; // Bug 1: real elapsed
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

        stuckTimer += elapsed; // Bug 1: real elapsed
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

    public void ResetStuckDetection()
    {
        lastPosition = transform.position;
        stuckTimer = 0f;
        wasStuckLastCheck = false;
        pathInvalidTimer = 0f;
        lastStaggeredCheckTime = Time.time; // Bug 1: reset elapsed tracking
    }

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

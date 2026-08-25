using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: Flee away from enemies.
/// Continuously recalculates a flee point in the opposite direction from the nearest threat.
/// Prefers huts as shelter if one is roughly in the flee direction; otherwise just runs away.
/// </summary>
public class FleeToHutExecutor : ActionExecutor
{
    public override string DisplayName => "Fleeing!";

    private const float FleeDistance = 15f;        // How far ahead to pick the flee point
    private const float RecalcInterval = 0.5f;     // How often to recalculate flee direction
    private const float HutPreferAngle = 70f;      // Max angle from flee dir to still prefer a hut
    private const float HutArrivalDist = 3f;       // Close enough to hut to count as sheltered
    private const float ShelterSlowDist = 2f;      // Stop at shelter

    private float recalcTimer;
    private Transform shelterTarget; // hut we're heading toward (if any)

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

        shelterTarget = null;
        recalcTimer = 0f; // calculate immediately

        // Moving errand — drop stationary-importance if we were gathering/idle
        Worker.RollMovingAvoidance(bb.agent);

        SetFleeDestination(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (bb.stuckResolver != null)
            bb.stuckResolver.UpdateMoving();

        // If we reached a hut shelter, stop and stay safe
        if (shelterTarget != null)
        {
            float dist = Vector3.Distance(bb.transform.position, shelterTarget.position);
            if (dist < ShelterSlowDist)
            {
                if (bb.agent.isOnNavMesh) bb.agent.ResetPath();
                // Sheltering = standing still; let other fleers route around us
                Worker.SetStationaryAvoidance(bb.agent);
                return;
            }
        }

        // Periodically recalculate flee direction as enemies move.
        // If the throttle/NavMesh rejected the destination, retry next frame
        // instead of standing still for the full recalc interval.
        recalcTimer -= Time.deltaTime;
        if (recalcTimer <= 0f)
        {
            recalcTimer = SetFleeDestination(bb) ? RecalcInterval : 0f;
        }
    }

    /// <summary>
    /// Pick and set a flee destination. Returns false only when
    /// AINavHelper.TrySetDestination rejected the set (throttled or
    /// unmappable) so the caller can retry next frame.
    /// </summary>
    bool SetFleeDestination(AIBlackboard bb)
    {
        if (!bb.agent.isOnNavMesh || !bb.agent.enabled) return false;

        Vector3 myPos = bb.transform.position;

        // Find the average threat direction from nearby enemies
        Vector3 threatDir = GetThreatDirection(bb, myPos);

        if (threatDir == Vector3.zero)
        {
            // No enemies visible — flee toward base as fallback
            if (bb.baseBuilding != null)
            {
                if (!AINavHelper.TrySetDestination(bb.agent, bb.baseBuilding.transform.position))
                    return false;
                bb.agent.isStopped = false;
                shelterTarget = bb.baseBuilding.transform;
            }
            return true;
        }

        // Flee direction is opposite of threat
        Vector3 fleeDir = -threatDir.normalized;

        // Check if any hut is roughly in the flee direction
        Transform bestHut = FindHutInFleeDirection(bb, myPos, fleeDir);

        if (bestHut != null)
        {
            if (!AINavHelper.TrySetDestination(bb.agent, bestHut.position))
                return false;
            shelterTarget = bestHut;
            bb.agent.isStopped = false;
            return true;
        }

        shelterTarget = null;

        // Pick a point on the NavMesh in the flee direction
        Vector3 fleePoint = myPos + fleeDir * FleeDistance;

        if (NavMesh.SamplePosition(fleePoint, out NavMeshHit hit, FleeDistance, NavMesh.AllAreas))
        {
            if (!AINavHelper.TrySetDestination(bb.agent, hit.position))
                return false;
            bb.agent.isStopped = false;
        }
        else
        {
            // Can't find a point in the ideal direction — try a shorter distance
            fleePoint = myPos + fleeDir * (FleeDistance * 0.5f);
            if (NavMesh.SamplePosition(fleePoint, out hit, FleeDistance * 0.5f, NavMesh.AllAreas))
            {
                if (!AINavHelper.TrySetDestination(bb.agent, hit.position))
                    return false;
                bb.agent.isStopped = false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the average direction FROM the worker TOWARD nearby enemies.
    /// </summary>
    Vector3 GetThreatDirection(AIBlackboard bb, Vector3 myPos)
    {
        Vector3 threatSum = Vector3.zero;
        int count = 0;
        float detectRange = bb.searchRadius > 0f ? bb.searchRadius : 30f;

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;
            Health h = enemy.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            Vector3 toEnemy = enemy.transform.position - myPos;
            float dist = toEnemy.magnitude;
            if (dist < detectRange && dist > 0.1f)
            {
                // Weight closer enemies more heavily (inverse distance)
                threatSum += toEnemy.normalized * (1f / dist);
                count++;
            }
        }

        return count > 0 ? threatSum.normalized : Vector3.zero;
    }

    /// <summary>
    /// Find a hut that is roughly in the flee direction (within HutPreferAngle degrees).
    /// Returns the nearest qualifying hut, or null.
    /// </summary>
    Transform FindHutInFleeDirection(AIBlackboard bb, Vector3 myPos, Vector3 fleeDir)
    {
        Transform best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;
            if (hut.CachedHealth != null && !hut.CachedHealth.IsAlive) continue;

            Vector3 toHut = hut.transform.position - myPos;
            float dist = toHut.magnitude;
            if (dist < 1f) continue; // already here

            float angle = Vector3.Angle(fleeDir, toHut);
            if (angle < HutPreferAngle && dist < bestDist)
            {
                bestDist = dist;
                best = hut.transform;
            }
        }

        return best;
    }

    public override void OnExit(AIBlackboard bb)
    {
        shelterTarget = null;
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
        }
    }
}

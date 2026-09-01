using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Warrior executor: the idle-time behaviour. Walks a loop of guard posts so warriors
/// are spread over the colony's edge instead of clumping at the campfire.
/// </summary>
/// <remarks>
/// Post selection falls through three tiers, best first:
/// 1. Just inside a random wall or gate (only while walls exist) - the defensive line.
/// 2. On the outer perimeter of a random campfire/hut, skipping points that fall inside
///    another building's no-build radius, so warriors ring the colony rather than
///    standing between two overlapping buildings.
/// 3. A random point within patrolRadius of wherever the warrior is standing.
/// Every candidate is NavMesh.SamplePosition-snapped before it is accepted, so a post
/// is never handed to the agent inside a building's carve hole.
/// </remarks>
public class PatrolExecutor : ActionExecutor
{
    public override string DisplayName => hasWalls ? "Guarding Walls" : "Patrolling";

    private Vector3 currentPatrolPoint;
    private bool isWaitingAtPatrol = false;
    private float patrolWaitTimer = 0f;
    private float patrolWaitTime = 3f;   // Seconds spent standing at a post before picking the next one.
    // TrySetDestination can be throttled or rejected by Unity, so the move is retried
    // every frame until it takes rather than assumed to have happened.
    private bool patrolDestinationSet = false;
    private bool hasWalls = false;       // Re-checked on enter and at every post; walls get built mid-run.

    // Campfire, looked up once: it only supplies an "which way is inward" direction,
    // and the null check below covers it being destroyed later.
    private BaseBuilding cachedCampfire;
    private bool campfireCached = false;

    // Reused across calls so building up the candidate list allocates nothing per patrol point.
    private readonly List<(Transform t, float radius)> buildingBuffer = new List<(Transform, float)>();

    public override void OnEnter(AIBlackboard bb)
    {
        isWaitingAtPatrol = false;
        patrolWaitTimer = 0f;
        patrolDestinationSet = false;

        hasWalls = WallGrid.Instance != null && (Wall.ActiveList.Count > 0 || Gate.ActiveList.Count > 0);
        currentPatrolPoint = GetRandomPatrolPoint(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        float distanceToPatrolPoint = Vector3.Distance(bb.transform.position, currentPatrolPoint);

        // Patrol has no target to null out, so unlike Gather/Engage/EnemyAttack it can
        // ignore UpdateMoving's reset return and keep going in the same tick.
        if (!isWaitingAtPatrol && bb.stuckResolver != null)
        {
            bb.stuckResolver.UpdateMoving();
        }

        if (isWaitingAtPatrol)
        {
            bb.agent.isStopped = true;
            patrolWaitTimer += Time.deltaTime;

            if (patrolWaitTimer >= patrolWaitTime)
            {
                hasWalls = WallGrid.Instance != null && (Wall.ActiveList.Count > 0 || Gate.ActiveList.Count > 0);
                currentPatrolPoint = GetRandomPatrolPoint(bb);
                isWaitingAtPatrol = false;
                patrolDestinationSet = false;
                patrolWaitTimer = 0f;
            }
        }
        else if (distanceToPatrolPoint < 1.5f)
        {
            isWaitingAtPatrol = true;
            patrolDestinationSet = false;
            patrolWaitTimer = 0f;
        }
        else
        {
            bb.agent.isStopped = false;
            if (!patrolDestinationSet)
            {
                if (AINavHelper.TrySetDestination(bb.agent, currentPatrolPoint))
                {
                    patrolDestinationSet = true;
                }
            }
        }
    }

    /// <summary>Picks the next guard post, walls first and the spawn-area wander last.</summary>
    Vector3 GetRandomPatrolPoint(AIBlackboard bb)
    {
        if (hasWalls)
        {
            Vector3 wallPoint;
            if (TryGetWallPatrolPoint(bb, out wallPoint))
                return wallPoint;
        }
        return GetBuildingPerimeterPatrolPoint(bb);
    }

    /// <summary>
    /// Tries to find a standing spot just inside a random wall or gate. Offsetting along
    /// (wall - campfire) puts the warrior on the colony side of the line, not outside it.
    /// Ten attempts, then gives up so a walled-off or unsampleable line can't stall the tick.
    /// </summary>
    bool TryGetWallPatrolPoint(AIBlackboard bb, out Vector3 point)
    {
        point = Vector3.zero;

        int wallCount = Wall.ActiveList.Count;
        int gateCount = Gate.ActiveList.Count;
        int totalCount = wallCount + gateCount;
        if (totalCount == 0) return false;

        if (!campfireCached)
        {
            cachedCampfire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
            campfireCached = true;
        }

        for (int attempts = 0; attempts < 10; attempts++)
        {
            Vector3 wallPos;
            int index = Random.Range(0, totalCount);
            if (index < wallCount)
            {
                if (Wall.ActiveList[index] == null) continue;
                wallPos = Wall.ActiveList[index].transform.position;
            }
            else
            {
                int gateIndex = index - wallCount;
                if (Gate.ActiveList[gateIndex] == null) continue;
                wallPos = Gate.ActiveList[gateIndex].transform.position;
            }

            Vector3 interiorDir;
            if (cachedCampfire != null)
            {
                interiorDir = (wallPos - cachedCampfire.transform.position).normalized;
            }
            else
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                interiorDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            float offset = Random.Range(1f, 2f);
            Vector3 patrolPos = wallPos + interiorDir * offset;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(patrolPos, out hit, 3f, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Fallback post for a colony with no walls: a point on the no-build ring of a random
    /// campfire or hut. Points that land inside another building's ring are rejected, which
    /// keeps the patrol on the colony's outer edge instead of in the gaps between buildings.
    /// </summary>
    Vector3 GetBuildingPerimeterPatrolPoint(AIBlackboard bb)
    {
        buildingBuffer.Clear();

        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            var campfire = BaseBuilding.ActiveList[i];
            if (campfire != null)
                buildingBuffer.Add((campfire.transform, campfire.noBuildRadius));
        }

        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            var hut = Hut.ActiveList[i];
            if (hut != null)
                buildingBuffer.Add((hut.transform, hut.noBuildRadius));
        }

        if (buildingBuffer.Count > 0)
        {
            for (int attempts = 0; attempts < 20; attempts++)
            {
                var randomBuilding = buildingBuffer[Random.Range(0, buildingBuffer.Count)];
                float noBuildRadius = randomBuilding.radius;

                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 perimeterPoint = randomBuilding.t.position + new Vector3(
                    Mathf.Cos(angle) * noBuildRadius,
                    0f,
                    Mathf.Sin(angle) * noBuildRadius
                );

                // Reject the point if it sits inside another building's no-build radius:
                // that means it is interior to the colony, not on the outer edge.
                bool isOuterPerimeter = true;
                for (int j = 0; j < buildingBuffer.Count; j++)
                {
                    if (buildingBuffer[j].t == randomBuilding.t) continue;
                    float distanceToOther = Vector3.Distance(perimeterPoint, buildingBuffer[j].t.position);
                    if (distanceToOther < buildingBuffer[j].radius)
                    {
                        isOuterPerimeter = false;
                        break;
                    }
                }

                if (!isOuterPerimeter) continue;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(perimeterPoint, out hit, 3f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }
        }

        // Last resort (no buildings at all, or every perimeter candidate was off-NavMesh):
        // wander within patrolRadius of the warrior's current position.
        for (int attempts = 0; attempts < 5; attempts++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(bb.patrolRadius * 0.5f, bb.patrolRadius);

            Vector3 randomPoint = bb.transform.position + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 2f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        return bb.transform.position;
    }

    public override void OnExit(AIBlackboard bb)
    {
        isWaitingAtPatrol = false;
        patrolDestinationSet = false;
        bb.agent.isStopped = false;
    }
}

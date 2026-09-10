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
    private float patrolWaitTime = 3f;   // Seconds at this post; re-rolled per post so a group never moves in step.
    // TrySetDestination can be throttled or rejected by Unity, so the move is retried
    // every frame until it takes rather than assumed to have happened.
    private bool patrolDestinationSet = false;
    private bool hasWalls = false;       // Re-checked on enter and at every post; walls get built mid-run.

    // 2026-09-07: the "standing still, blocking everyone" freeze. A warrior that
    // could not get within ArriveRadius of its post (an archer's 8 u stopping
    // distance, a post on the far side of a carve, five others already standing
    // on it) was reset by the stuck resolver — which ResetPaths and ForceReevals —
    // but Patrol stayed the best action, so no OnEnter ran, patrolDestinationSet
    // stayed true, and the warrior stood on "Patrolling" forever. With more
    // warriors and archers the majority ended up frozen on the fire's ring.
    // Now: the stopping distance is dropped to PostStoppingDistance for the walk,
    // a finished path counts as arrival, a walk that takes too long counts as
    // arrival, and a stuck reset picks a fresh post.
    private const float ArriveRadius = 1.5f;
    private const float PostStoppingDistance = 0.5f;
    private const float MaxWalkSeconds = 15f;
    // Guard posts keep this far off the campfire's collider edge so idle warriors
    // never stand on the delivery edge the workers need.
    private const float FireClearance = 4f;
    // Ring radius around the fire and the huts: their no-build radius plus this.
    private const float PerimeterPadding = 4f;
    private float walkTimer;
    private float originalStoppingDistance;
    private bool stoppingSwapped;

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

        if (bb.agent != null && bb.agent.isOnNavMesh && !stoppingSwapped)
        {
            // The warrior default (attackRange - 1: 8 u for a bow) parks an archer
            // outside ArriveRadius of every post it is ever given.
            originalStoppingDistance = bb.agent.stoppingDistance;
            bb.agent.stoppingDistance = PostStoppingDistance;
            stoppingSwapped = true;
        }

        NextPost(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        if (isWaitingAtPatrol)
        {
            bb.agent.isStopped = true;
            patrolWaitTimer += Time.deltaTime;
            if (patrolWaitTimer >= patrolWaitTime) NextPost(bb);
            return;
        }

        // A stuck reset has ResetPath'd the agent: the old destination is gone, so
        // this post is abandoned for a fresh one rather than waited on forever.
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            NextPost(bb);
            return;
        }

        walkTimer += Time.deltaTime;
        float distanceToPatrolPoint = Vector3.Distance(bb.transform.position, currentPatrolPoint);
        bool pathDone = patrolDestinationSet && bb.agent.isOnNavMesh && !bb.agent.pathPending
            && (!bb.agent.hasPath || bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.3f);

        if (distanceToPatrolPoint < ArriveRadius || pathDone || walkTimer > MaxWalkSeconds)
        {
            // As close as the crowd and the mesh allow: stand here
            isWaitingAtPatrol = true;
            patrolDestinationSet = false;
            patrolWaitTimer = 0f;
            return;
        }

        bb.agent.isStopped = false;
        if (!patrolDestinationSet)
        {
            if (AINavHelper.TrySetDestination(bb.agent, currentPatrolPoint))
            {
                patrolDestinationSet = true;
                if (bb.isRanged) DevQuests.Signal("patrol:archer");   // an archer walks to a post like a spearman
            }
        }
    }

    /// <summary>Pick the next post and start walking; the wait at it is re-rolled so posts never change in step.</summary>
    void NextPost(AIBlackboard bb)
    {
        hasWalls = WallGrid.Instance != null && (Wall.ActiveList.Count > 0 || Gate.ActiveList.Count > 0);
        currentPatrolPoint = GetRandomPatrolPoint(bb);
        isWaitingAtPatrol = false;
        patrolDestinationSet = false;
        patrolWaitTimer = 0f;
        patrolWaitTime = Random.Range(2f, 5f);
        walkTimer = 0f;
        if (bb.agent != null && bb.agent.isOnNavMesh) bb.agent.isStopped = false;
        if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();
    }

    /// <summary>A post this close to the fire's collider edge would stand on the delivery edge.</summary>
    static bool TooCloseToFire(Vector3 point)
    {
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            BaseBuilding fire = BaseBuilding.ActiveList[i];
            if (fire == null) continue;
            Collider col = fire.GetComponent<Collider>();
            if (TargetingUtil.EdgeDistance(point, fire.transform, col) < FireClearance) return true;
        }
        return false;
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
            cachedCampfire = bb.faction.Campfire;
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
            if (NavMesh.SamplePosition(patrolPos, out hit, 3f, NavMesh.AllAreas) && !TooCloseToFire(hit.position))
            {
                point = hit.position;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Fallback post for a colony with no walls: a point on a ring PerimeterPadding
    /// outside the no-build radius of a random campfire or hut. Points that land inside
    /// another building's ring are rejected, which keeps the patrol on the colony's
    /// outer edge instead of in the gaps between buildings — and anything on the
    /// fire's delivery edge is rejected too. The old ring WAS the no-build radius
    /// (2.5 u at the fire), which put every idle warrior on the workers' drop-off.
    /// </summary>
    Vector3 GetBuildingPerimeterPatrolPoint(AIBlackboard bb)
    {
        buildingBuffer.Clear();

        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            var campfire = BaseBuilding.ActiveList[i];
            if (campfire != null)
                buildingBuffer.Add((campfire.transform, campfire.noBuildRadius + PerimeterPadding));
        }

        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            var hut = Hut.ActiveList[i];
            if (hut != null)
                buildingBuffer.Add((hut.transform, hut.noBuildRadius + PerimeterPadding));
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
                if (NavMesh.SamplePosition(perimeterPoint, out hit, 3f, NavMesh.AllAreas) && !TooCloseToFire(hit.position))
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
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
            if (stoppingSwapped) bb.agent.stoppingDistance = originalStoppingDistance;
        }
        stoppingSwapped = false;
    }
}

using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Warrior executor: Form up at colony perimeter facing approaching enemies.
/// Warriors rally at the building edge between base and enemy cluster,
/// waiting for enemies to close in before the brain switches to EngageEnemy.
/// Produces natural grouping behavior — all warriors converge to the same intercept point.
/// </summary>
public class InterceptExecutor : ActionExecutor
{
    public override string DisplayName => "Intercepting";

    private Vector3 rallyPoint;
    private bool destinationSet = false;
    private float recalcTimer = 0f;
    private float recalcInterval = 2f; // Recalculate rally point every 2s as enemies move

    // Spread warriors slightly so they don't all stack on the exact same spot
    private float spreadOffset;

    public InterceptExecutor()
    {
        // Each instance gets a random lateral offset for formation spread
        spreadOffset = Random.Range(-3f, 3f);
    }

    public override void OnEnter(AIBlackboard bb)
    {
        destinationSet = false;
        recalcTimer = 0f;
        CalculateRallyPoint(bb);
        MoveToRally(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Periodically recalculate as enemies move
        recalcTimer += Time.deltaTime;
        if (recalcTimer >= recalcInterval)
        {
            recalcTimer = 0f;
            CalculateRallyPoint(bb);

            // Only re-path if rally point moved significantly
            float driftDistance = Vector3.Distance(rallyPoint, bb.agent.destination);
            if (driftDistance > 3f)
            {
                MoveToRally(bb);
            }
        }

        // Once near rally point, stop and face enemies
        float distToRally = Vector3.Distance(bb.transform.position, rallyPoint);
        if (distToRally < 2f)
        {
            bb.agent.isStopped = true;

            // Face toward enemies
            if (bb.nearestEnemy != null)
            {
                Vector3 lookDir = (bb.nearestEnemy.position - bb.transform.position).normalized;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    bb.transform.rotation = Quaternion.Slerp(
                        bb.transform.rotation,
                        Quaternion.LookRotation(lookDir),
                        Time.deltaTime * 3f);
                }
            }
        }
        else
        {
            bb.agent.isStopped = false;

            // Bug 2: Only run stuck resolution while still moving toward rally point
            if (bb.stuckResolver != null)
            {
                bb.stuckResolver.UpdateMoving();
            }
        }
    }

    void CalculateRallyPoint(AIBlackboard bb)
    {
        // Find enemy centroid (average position of all living enemies)
        Vector3 enemyCentroid = Vector3.zero;
        int enemyCount = 0;

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;
            Health h = enemy.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            enemyCentroid += enemy.transform.position;
            enemyCount++;
        }

        if (enemyCount == 0 || bb.baseBuilding == null)
        {
            // No enemies, just stay near base
            rallyPoint = bb.baseBuilding != null ? bb.baseBuilding.transform.position : bb.transform.position;
            return;
        }

        enemyCentroid /= enemyCount;

        // Direction from base toward enemies
        Vector3 basePos = bb.baseBuilding.transform.position;
        Vector3 dirToEnemies = (enemyCentroid - basePos).normalized;

        // Calculate colony perimeter radius — use campfire noBuildRadius + buffer
        float perimeterRadius = bb.baseBuilding.noBuildRadius + 2f;

        // Check for walls — if walls exist, position just inside the wall line
        if (Wall.ActiveList.Count > 0 || Gate.ActiveList.Count > 0)
        {
            // Find the furthest wall in the enemy direction as the defense line
            float furthestWallDist = 0f;
            for (int i = 0; i < Wall.ActiveList.Count; i++)
            {
                Wall wall = Wall.ActiveList[i];
                if (wall == null) continue;

                // Only consider walls roughly in the enemy direction
                Vector3 wallDir = (wall.transform.position - basePos).normalized;
                if (Vector3.Dot(wallDir, dirToEnemies) > 0.3f)
                {
                    float dist = Vector3.Distance(basePos, wall.transform.position);
                    if (dist > furthestWallDist)
                        furthestWallDist = dist;
                }
            }

            if (furthestWallDist > 0f)
            {
                // Position just inside the wall line (2 units interior)
                perimeterRadius = furthestWallDist - 2f;
            }
        }

        // Rally point: base + direction_to_enemies * perimeterRadius
        Vector3 interceptPoint = basePos + dirToEnemies * perimeterRadius;

        // Add lateral spread so warriors form a line, not a stack
        Vector3 lateral = Vector3.Cross(dirToEnemies, Vector3.up).normalized;
        interceptPoint += lateral * spreadOffset;

        // Validate on NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(interceptPoint, out hit, 5f, NavMesh.AllAreas))
        {
            rallyPoint = hit.position;
        }
        else
        {
            // Fallback: just get close to base on the enemy side
            interceptPoint = basePos + dirToEnemies * (perimeterRadius * 0.5f);
            if (NavMesh.SamplePosition(interceptPoint, out hit, 5f, NavMesh.AllAreas))
            {
                rallyPoint = hit.position;
            }
            else
            {
                rallyPoint = basePos;
            }
        }
    }

    void MoveToRally(AIBlackboard bb)
    {
        if (bb.agent.isOnNavMesh && bb.agent.enabled)
        {
            bb.agent.isStopped = false;
            AINavHelper.TrySetDestination(bb.agent, rallyPoint);
            destinationSet = true;

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        destinationSet = false;
        if (bb.agent.isOnNavMesh)
            bb.agent.isStopped = false;
    }
}

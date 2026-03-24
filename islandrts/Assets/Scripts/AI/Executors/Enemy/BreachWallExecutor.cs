using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Enemy executor: Score walls, attack weakest/nearest, detect breaches.
/// Ports existing Enemy wall breach logic with crowd penalty and gate preference.
/// Preserves static wallTargetCounts coordination.
/// </summary>
public class BreachWallExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Breaching Wall";

    // Static wall-target coordination (preserved from Enemy.cs)
    private static readonly Dictionary<Transform, int> wallTargetCounts = new Dictionary<Transform, int>();
    private static readonly List<Transform> staleKeyBuffer = new List<Transform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { wallTargetCounts.Clear(); }

    // Pooled candidate list
    private readonly List<(Transform t, float score, string name)> candidates
        = new List<(Transform, float, string)>();

    // Reusable NavMeshPath
    private NavMeshPath reusablePath;

    // Breach check timer
    private float breachCheckTimer = 0f;
    private float breachCheckInterval = 0.5f;

    // Attack range hysteresis
    private float attackRangeBuffer = 0.5f;

    // Registered wall target
    private Transform registeredWallTarget;

    public override void OnEnter(AIBlackboard bb)
    {
        if (reusablePath == null)
            reusablePath = new NavMeshPath();

        bb.isAttackingWall = true;
        bb.isInAttackRange = false;
        breachCheckTimer = Random.Range(0f, breachCheckInterval);

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        FindBestWallTarget(bb);

        if (bb.currentTarget != null)
        {
            bb.agent.isStopped = false;
            AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
            displayName = "Breaching " + bb.currentTargetName;
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Check if wall target is still alive
        if (bb.currentTarget == null || !IsTargetAlive(bb))
        {
            UnregisterWallTarget();
            bb.isAttackingWall = false;
            return; // Brain will re-evaluate
        }

        // Breach detection: periodically check if a clear path to campfire opened
        breachCheckTimer += Time.deltaTime;
        if (breachCheckTimer >= breachCheckInterval)
        {
            breachCheckTimer = 0f;

            BaseBuilding campfire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
            if (campfire != null)
            {
                if (AINavHelper.TryCalculatePath(
                    bb.transform.position, campfire.transform.position,
                    NavMesh.AllAreas, reusablePath)
                    && reusablePath.status == NavMeshPathStatus.PathComplete)
                {
                    // Breach found! Stop attacking wall
                    UnregisterWallTarget();
                    bb.isAttackingWall = false;
                    return; // Brain will switch to attack building/campfire
                }
            }
        }

        // Move toward wall (wall center is carved, so just keep trying)
        if (!bb.agent.hasPath || bb.agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
        }

        // Stuck resolution (when moving, not attacking)
        if (bb.stuckResolver != null && !bb.isInAttackRange)
            bb.stuckResolver.UpdateMoving();

        // Attack range check (extra range for walls due to NavMesh carving)
        float distanceToTarget = Vector3.Distance(bb.transform.position, bb.currentTarget.position);
        float effectiveAttackRange = bb.attackRange + 1.5f;

        if (bb.isInAttackRange)
        {
            if (distanceToTarget > effectiveAttackRange + attackRangeBuffer)
            {
                bb.isInAttackRange = false;
            }
        }
        else
        {
            if (distanceToTarget <= effectiveAttackRange)
            {
                bb.isInAttackRange = true;
            }
        }

        if (bb.isInAttackRange)
        {
            bb.agent.isStopped = true;
            displayName = "Attacking " + bb.currentTargetName + "!";
            AttemptAttack(bb);
        }
        else
        {
            bb.agent.isStopped = false;
            displayName = "Breaching " + bb.currentTargetName;
        }
    }

    void FindBestWallTarget(AIBlackboard bb)
    {
        candidates.Clear();

        // Score walls with crowd penalty
        for (int i = 0; i < Wall.ActiveList.Count; i++)
        {
            Wall wall = Wall.ActiveList[i];
            if (wall == null) continue;
            Health wallHealth = wall.CachedHealth;
            if (wallHealth == null || !wallHealth.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, wall.transform.position);
            int attackers = GetWallTargetCount(wall.transform);
            float score = dist * (1f + attackers * 0.5f);
            candidates.Add((wall.transform, score, wall.gameObject.name));
        }

        // Gates get strong preference (0.3x distance)
        for (int i = 0; i < Gate.ActiveList.Count; i++)
        {
            Gate gate = Gate.ActiveList[i];
            if (gate == null) continue;
            Health gateHealth = gate.CachedHealth;
            if (gateHealth == null || !gateHealth.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, gate.transform.position);
            dist *= 0.3f; // Strong preference for gates
            int attackers = GetWallTargetCount(gate.transform);
            float score = dist * (1f + attackers * 0.5f);
            candidates.Add((gate.transform, score, gate.gameObject.name));
        }

        if (candidates.Count > 0)
        {
            candidates.Sort((a, b) => a.score.CompareTo(b.score));
            var best = candidates[0];

            UnregisterWallTarget(); // Unregister from previous

            bb.currentTarget = best.t;
            bb.currentTargetName = best.name;
            bb.currentTargetHealth = best.t.GetComponent<Health>();
            RegisterWallTarget(best.t);
        }
    }

    bool IsTargetAlive(AIBlackboard bb)
    {
        if (bb.currentTarget == null) return false;
        // Bug 4: re-fetch Health if null instead of returning true
        if (bb.currentTargetHealth == null)
            bb.currentTargetHealth = bb.currentTarget.GetComponent<Health>();
        if (bb.currentTargetHealth == null) return false;
        return bb.currentTargetHealth.IsAlive;
    }

    void AttemptAttack(AIBlackboard bb)
    {
        if (Time.time - bb.lastAttackTime < bb.attackCooldown) return;
        bb.lastAttackTime = Time.time;

        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(bb.transform.position, bb.currentTarget.position, false);
        }

        bb.enemy.PlayAttackSoundPublic();

        if (bb.currentTargetHealth != null)
        {
            bb.currentTargetHealth.TakeDamage(bb.damage);
        }
    }

    // --- Static wall-target coordination ---

    void RegisterWallTarget(Transform wallTransform)
    {
        CleanupStaleEntries();
        if (wallTransform == null) return;
        registeredWallTarget = wallTransform;
        if (wallTargetCounts.ContainsKey(wallTransform))
            wallTargetCounts[wallTransform]++;
        else
            wallTargetCounts[wallTransform] = 1;
    }

    void UnregisterWallTarget()
    {
        if (registeredWallTarget == null) return;
        if (wallTargetCounts.ContainsKey(registeredWallTarget))
        {
            wallTargetCounts[registeredWallTarget]--;
            if (wallTargetCounts[registeredWallTarget] <= 0)
                wallTargetCounts.Remove(registeredWallTarget);
        }
        registeredWallTarget = null;
    }

    static int GetWallTargetCount(Transform wallTransform)
    {
        if (wallTransform == null) return 0;
        return wallTargetCounts.ContainsKey(wallTransform) ? wallTargetCounts[wallTransform] : 0;
    }

    static void CleanupStaleEntries()
    {
        staleKeyBuffer.Clear();
        foreach (var kvp in wallTargetCounts)
            if (kvp.Key == null) staleKeyBuffer.Add(kvp.Key);
        for (int i = 0; i < staleKeyBuffer.Count; i++)
            wallTargetCounts.Remove(staleKeyBuffer[i]);
    }

    public override void OnExit(AIBlackboard bb)
    {
        UnregisterWallTarget();
        bb.isAttackingWall = false;
        bb.isInAttackRange = false;
        bb.agent.isStopped = false;
    }
}

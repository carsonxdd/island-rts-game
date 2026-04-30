using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Single enemy attack executor. Replaces AttackTargetExecutor + BreachWallExecutor.
///
/// Target selection is an imperative priority function (PickTarget), not a scored
/// Utility-AI competition between sibling actions. This eliminates the 4-way
/// momentum/commitment fighting that caused stutters when a target died.
///
/// Priority (checked in order, first hit wins):
///   1. Nearest warrior within warriorDetectionRange
///   2. Nearest reachable building (Hut / Watchtower) — NavMesh path complete
///   3. Nearest wall/gate (gates preferred 0.3x distance)
///   4. Campfire
///
/// Retarget triggers — only two:
///   - 1s periodic tick inside this executor
///   - bb.currentTarget died (detected on next OnUpdate via IsTargetAlive)
///
/// Gate trigger override: Enemy.ForceAttackGate sets bb.forcedTarget with an
/// expiry. PickTarget honors forced targets first.
/// </summary>
public class EnemyAttackExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Searching";

    // Target selection cadence
    private const float RetargetInterval = 1f;
    private float retargetTimer;

    // Attack range hysteresis
    private const float AttackRangeBuffer = 0.5f;

    // Destination update tracking
    private const float DestinationUpdateThreshold = 1.5f;
    private Vector3 lastTargetPosition;

    // Campfire proximity commitment: if within this many meters of the campfire,
    // the enemy commits to the campfire over nearby huts. Otherwise huts are
    // preferred so the base isn't skipped en route.
    private const float CampfireCommitRange = 5f;

    // Reachability path (reusable, zero GC)
    private NavMeshPath reachabilityPath;

    public override void OnEnter(AIBlackboard bb)
    {
        if (reachabilityPath == null) reachabilityPath = new NavMeshPath();

        bb.isInAttackRange = false;
        retargetTimer = 0f;

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        PickTarget(bb);
        IssueMove(bb, force: true);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Target died or missing: pick a new one immediately.
        if (bb.currentTarget == null || !IsTargetAlive(bb))
        {
            PickTarget(bb);
            if (bb.currentTarget == null)
            {
                displayName = "Searching";
                bb.agent.isStopped = false;
                return;
            }
            // Re-roll ORCA avoidance priority so multiple enemies that just
            // finished the same target don't mutually yield into a stuck dance.
            if (bb.agent != null)
                bb.agent.avoidancePriority = Random.Range(30, 70);
            IssueMove(bb, force: true);
        }

        // 1s retarget tick: priority may have shifted (warrior in range, building freed, etc.)
        retargetTimer += Time.deltaTime;
        if (retargetTimer >= RetargetInterval)
        {
            retargetTimer = 0f;
            Transform previous = bb.currentTarget;
            PickTarget(bb);
            if (bb.currentTarget != previous)
                IssueMove(bb, force: true);
        }

        // Target moved: push a new destination (throttled).
        IssueMove(bb, force: false);

        // Stuck resolution while moving
        if (bb.stuckResolver != null && !bb.isInAttackRange)
            bb.stuckResolver.UpdateMoving();

        // Edge-distance attack range check with hysteresis
        UpdateAttackState(bb);

        if (bb.isInAttackRange)
        {
            bb.agent.isStopped = true;
            displayName = "Attacking " + bb.currentTargetName;
            AttemptAttack(bb);
        }
        else
        {
            bb.agent.isStopped = false;
            displayName = "Moving to " + bb.currentTargetName;
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        bb.isInAttackRange = false;
        if (bb.agent != null) bb.agent.isStopped = false;
    }

    // ---- Target selection (priority-ordered) ----

    void PickTarget(AIBlackboard bb)
    {
        // 0. Gate-trigger override: Enemy.ForceAttackGate stamped a short-lived lock.
        if (bb.forcedTarget != null && Time.time < bb.forcedTargetExpiry)
        {
            Health fh = bb.forcedTarget.GetComponent<Health>();
            if (fh != null && fh.IsAlive)
            {
                SetTarget(bb, bb.forcedTarget, bb.forcedTarget.gameObject.name);
                return;
            }
            // Expired/dead: clear and fall through.
            bb.forcedTarget = null;
        }

        // 1. Warriors
        Transform warrior = FindNearestLiveWarrior(bb);
        if (warrior != null) { SetTarget(bb, warrior, warrior.gameObject.name); return; }

        // Cache campfire — checked twice: once for proximity commit (pri 2),
        // once as the no-huts fallback (pri 5).
        Transform campfire = FindLiveCampfire();

        // 2. Campfire proximity commit: if we're right on top of the campfire,
        // stop bothering with huts and finish the job.
        if (campfire != null)
        {
            float campfireDist = Vector3.Distance(bb.transform.position, campfire.position);
            if (campfireDist <= CampfireCommitRange)
            {
                SetTarget(bb, campfire, "Campfire");
                return;
            }
        }

        // 3. Nearest reachable hut/tower. Preferred over the campfire so enemies
        // destroy structures en route instead of jogging past them.
        Transform building = FindNearestReachableBuilding(bb);
        if (building != null) { SetTarget(bb, building, building.gameObject.name); return; }

        // 4. Wall/gate (gates preferred)
        Transform wallOrGate = FindNearestWallOrGate(bb);
        if (wallOrGate != null) { SetTarget(bb, wallOrGate, wallOrGate.gameObject.name); return; }

        // 5. Campfire fallback — no huts alive, no walls to breach. Converge on the base.
        if (campfire != null) { SetTarget(bb, campfire, "Campfire"); return; }

        // Nothing to attack
        SetTarget(bb, null, null);
    }

    Transform FindNearestLiveWarrior(AIBlackboard bb)
    {
        Transform best = null;
        float bestDist = float.MaxValue;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Warrior w = list[i];
            if (w == null) continue;
            Health h = w.CachedHealth;
            if (h == null || !h.IsAlive) continue;
            float d = Vector3.Distance(bb.transform.position, w.transform.position);
            if (d <= bb.warriorDetectionRange && d < bestDist)
            {
                bestDist = d;
                best = w.transform;
            }
        }
        return best;
    }

    Transform FindNearestReachableBuilding(AIBlackboard bb)
    {
        // Scan huts + watchtowers, pick the straight-line nearest, then verify reachability.
        // No distance gate — enemies should engage any hut/tower they can reach, so they
        // destroy structures on the way to the campfire instead of jogging past them.
        // Campfire (priority 4) only wins when no huts/towers are alive + reachable.
        Transform nearest = null;
        float bestDist = float.MaxValue;

        var huts = Hut.ActiveList;
        for (int i = 0; i < huts.Count; i++)
        {
            Hut h = huts[i];
            if (h == null) continue;
            Health hp = h.CachedHealth;
            if (hp == null || !hp.IsAlive) continue;
            float d = Vector3.Distance(bb.transform.position, h.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = h.transform;
            }
        }

        var towers = Watchtower.ActiveList;
        for (int i = 0; i < towers.Count; i++)
        {
            Watchtower t = towers[i];
            if (t == null) continue;
            Health hp = t.CachedHealth;
            if (hp == null || !hp.IsAlive) continue;
            float d = Vector3.Distance(bb.transform.position, t.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = t.transform;
            }
        }

        if (nearest == null) return null;

        // Reachability check. Huts have NavMeshObstacle.carving=true, which creates
        // a non-walkable hole at the hut's position. Path-testing to hut.position OR
        // Collider.ClosestPoint lands on the carve boundary and returns PathPartial
        // even for trivially reachable huts.
        //
        // NavMesh.SamplePosition asks Unity for the nearest actually-walkable point
        // within 2m of the hut — guaranteed outside the carve. Path-testing to that
        // point gives a clean PathComplete/PathPartial/PathInvalid answer.
        //
        // CalculatePath is throttled (2/frame globally). If throttled, accept the
        // target optimistically — worst case enemy gets stuck and StuckResolver
        // re-picks.
        NavMeshHit walkableHit;
        if (!NavMesh.SamplePosition(nearest.position, out walkableHit, 3f, NavMesh.AllAreas))
            return null; // No NavMesh within 3m of hut — truly unreachable island

        if (AINavHelper.TryCalculatePath(bb.transform.position, walkableHit.position, NavMesh.AllAreas, reachabilityPath))
        {
            if (reachabilityPath.status != NavMeshPathStatus.PathComplete)
                return null;
        }
        return nearest;
    }

    Transform FindNearestWallOrGate(AIBlackboard bb)
    {
        Transform best = null;
        float bestScore = float.MaxValue;

        var walls = Wall.ActiveList;
        for (int i = 0; i < walls.Count; i++)
        {
            Wall w = walls[i];
            if (w == null) continue;
            Health hp = w.CachedHealth;
            if (hp == null || !hp.IsAlive) continue;
            float d = Vector3.Distance(bb.transform.position, w.transform.position);
            if (d < bestScore) { bestScore = d; best = w.transform; }
        }

        var gates = Gate.ActiveList;
        for (int i = 0; i < gates.Count; i++)
        {
            Gate g = gates[i];
            if (g == null) continue;
            Health hp = g.CachedHealth;
            if (hp == null || !hp.IsAlive) continue;
            // Gates get strong preference: 0.3x effective distance so swarms funnel through.
            float d = Vector3.Distance(bb.transform.position, g.transform.position) * 0.3f;
            if (d < bestScore) { bestScore = d; best = g.transform; }
        }

        return best;
    }

    Transform FindLiveCampfire()
    {
        var list = BaseBuilding.ActiveList;
        if (list.Count == 0) return null;
        BaseBuilding c = list[0];
        if (c == null) return null;
        Health h = c.CachedHealth;
        if (h == null || !h.IsAlive) return null;
        return c.transform;
    }

    void SetTarget(AIBlackboard bb, Transform t, string name)
    {
        if (bb.currentTarget == t) return;
        bb.currentTarget = t;
        bb.currentTargetName = name;
        if (t != null)
        {
            bb.currentTargetHealth = t.GetComponent<Health>();
            bb.currentTargetCollider = t.GetComponent<Collider>();
        }
        else
        {
            bb.currentTargetHealth = null;
            bb.currentTargetCollider = null;
        }
        bb.isInAttackRange = false;
    }

    // ---- Movement + combat ----

    Vector3 GetApproachPoint(AIBlackboard bb)
    {
        // Start with collider's closest-point (the "edge" of the target).
        Vector3 raw = bb.currentTargetCollider != null
            ? bb.currentTargetCollider.ClosestPoint(bb.transform.position)
            : bb.currentTarget.position;

        // Snap to NavMesh. ClosestPoint on a carving obstacle's collider sits
        // on the boundary of the NavMesh hole. When an adjacent hut uncarves
        // (another enemy just killed it), the NavMesh is briefly in recalc and
        // this boundary point isn't recognized as walkable — SetDestination
        // silently fails and the enemy freezes for ~3s until StuckResolver
        // rescues it. SamplePosition snaps to a guaranteed-walkable NavMesh
        // point within 2m, so SetDestination always succeeds.
        NavMeshHit hit;
        if (NavMesh.SamplePosition(raw, out hit, 2f, NavMesh.AllAreas))
            return hit.position;
        return raw; // Fallback: if even a 2m search misses, SetDestination may
                    // fail and IssueMove will retry next frame (now that
                    // TrySetDestination returns the real result).
    }

    float GetEdgeDistance(AIBlackboard bb)
    {
        if (bb.currentTargetCollider == null)
            return Vector3.Distance(bb.transform.position, bb.currentTarget.position);
        Vector3 edge = bb.currentTargetCollider.ClosestPoint(bb.transform.position);
        return Vector3.Distance(bb.transform.position, edge);
    }

    void IssueMove(AIBlackboard bb, bool force)
    {
        if (bb.currentTarget == null) return;
        if (bb.agent == null || !bb.agent.isOnNavMesh) return;
        if (!force && bb.agent.pathPending) return;

        bool needNew = force
            || !bb.agent.hasPath
            || bb.agent.pathStatus == NavMeshPathStatus.PathInvalid
            || Vector3.Distance(bb.currentTarget.position, lastTargetPosition) > DestinationUpdateThreshold;

        if (!needNew) return;

        if (force && bb.agent.hasPath) bb.agent.ResetPath();

        if (AINavHelper.TrySetDestination(bb.agent, GetApproachPoint(bb)))
        {
            lastTargetPosition = bb.currentTarget.position;
            bb.agent.isStopped = false;
        }
        // If throttled, needNew stays true next frame and we retry.
    }

    void UpdateAttackState(AIBlackboard bb)
    {
        float d = GetEdgeDistance(bb);
        float range = bb.attackRange;
        if (bb.isInAttackRange)
        {
            if (d > range + AttackRangeBuffer) bb.isInAttackRange = false;
        }
        else
        {
            if (d <= range) bb.isInAttackRange = true;
        }
    }

    void AttemptAttack(AIBlackboard bb)
    {
        if (Time.time - bb.lastAttackTime < bb.attackCooldown) return;
        bb.lastAttackTime = Time.time;

        if (CombatEffects.Instance != null)
            CombatEffects.Instance.SpawnAttackEffect(bb.transform.position, bb.currentTarget.position, false);

        bb.enemy.PlayAttackSoundPublic();

        if (bb.currentTargetHealth != null)
            bb.currentTargetHealth.TakeDamage(bb.damage);
    }

    bool IsTargetAlive(AIBlackboard bb)
    {
        if (bb.currentTarget == null) return false;
        if (bb.currentTargetHealth == null)
            bb.currentTargetHealth = bb.currentTarget.GetComponent<Health>();
        if (bb.currentTargetHealth == null) return false;
        return bb.currentTargetHealth.IsAlive;
    }
}

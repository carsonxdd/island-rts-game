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
///   - bb.currentTarget died (detected on next OnUpdate via bb.IsTargetAlive)
///
/// Gate trigger override: Enemy.ForceAttackGate sets bb.forcedTarget with an
/// expiry. PickTarget honors forced targets first.
///
/// Phase 6.25: scans, target bookkeeping, and approach geometry moved to
/// TargetingUtil / AIBlackboard so warriors and workers share the same code.
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
    // A forced move that the NavMesh hasn't accepted yet. Stays true until
    // TrySetDestination succeeds, so a throttled/rejected destination is always
    // retried — even when the new target happens to sit near the old one.
    private bool moveQueued;

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
        moveQueued = false;
        // Stagger the retarget tick per enemy — the warrior Engage executor already
        // does this. With every enemy in a wave ticking on the same frame, any shared
        // priority shift made the whole group re-path in one frame; Unity's path
        // queue then drained over several frames and the group froze in lockstep.
        retargetTimer = Random.Range(0f, RetargetInterval);

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        PickTarget(bb);
        IssueMove(bb, force: true);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Target died or missing: pick a new one immediately.
        if (bb.currentTarget == null || !bb.IsTargetAlive())
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

        // Stuck resolution while moving. A stuck reset fires Enemy's onStuckReset
        // callback, which clears bb.currentTarget mid-call — bail out for this tick
        // (the callback already ForceReeval'd; next OnUpdate re-picks a target).
        if (bb.stuckResolver != null && !bb.isInAttackRange && bb.stuckResolver.UpdateMoving())
            return;

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
        moveQueued = false;
        if (bb.agent != null) bb.agent.isStopped = false;
    }

    // ---- Target selection (priority-ordered) ----

    void PickTarget(AIBlackboard bb)
    {
        Vector3 myPos = bb.transform.position;
        float unused;

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

        // 1. Warriors within detection range
        Warrior warrior = TargetingUtil.FindNearest(Warrior.ActiveList, myPos, bb.warriorDetectionRange, out unused);
        if (warrior != null) { SetTarget(bb, warrior.transform, warrior.gameObject.name); return; }

        // Cache campfire — checked twice: once for proximity commit (pri 2),
        // once as the no-huts fallback (pri 5).
        Transform campfire = FindLiveCampfire();

        // 2. Campfire proximity commit: if we're right on top of the campfire,
        // stop bothering with huts and finish the job.
        if (campfire != null)
        {
            float campfireDist = Vector3.Distance(myPos, campfire.position);
            if (campfireDist <= CampfireCommitRange)
            {
                SetTarget(bb, campfire, "Campfire");
                return;
            }
        }

        // 3. Nearest reachable hut/tower. Preferred over the campfire so enemies
        // destroy structures en route instead of jogging past them.
        Transform building = FindNearestReachableBuilding(bb, myPos);
        if (building != null) { SetTarget(bb, building, building.gameObject.name); return; }

        // 4. Wall/gate (gates preferred)
        Transform wallOrGate = FindNearestWallOrGate(myPos);
        if (wallOrGate != null) { SetTarget(bb, wallOrGate, wallOrGate.gameObject.name); return; }

        // 5. Campfire fallback — no huts alive, no walls to breach. Converge on the base.
        if (campfire != null) { SetTarget(bb, campfire, "Campfire"); return; }

        // Nothing to attack
        bb.ClearTarget();
    }

    Transform FindNearestReachableBuilding(AIBlackboard bb, Vector3 myPos)
    {
        // Nearest hut or tower by straight-line distance, then verify reachability.
        // No distance gate — enemies should engage any hut/tower they can reach, so
        // they destroy structures on the way to the campfire instead of jogging past
        // them. Campfire (priority 5) only wins when no huts/towers are alive + reachable.
        float hutDist, towerDist, shopDist;
        Hut hut = TargetingUtil.FindNearest(Hut.ActiveList, myPos, 0f, out hutDist);
        Watchtower tower = TargetingUtil.FindNearest(Watchtower.ActiveList, myPos, 0f, out towerDist);
        Workshop shop = TargetingUtil.FindNearest(Workshop.ActiveList, myPos, 0f, out shopDist);

        Transform nearest = null;
        float nearestDist = float.MaxValue;
        if (hut != null) { nearest = hut.transform; nearestDist = hutDist; }
        if (tower != null && towerDist < nearestDist) { nearest = tower.transform; nearestDist = towerDist; }
        if (shop != null && shopDist < nearestDist) { nearest = shop.transform; }

        if (nearest == null) return null;

        // Reachability check. Huts have NavMeshObstacle.carving=true, which creates
        // a non-walkable hole at the hut's position. Path-testing to hut.position OR
        // Collider.ClosestPoint lands on the carve boundary and returns PathPartial
        // even for trivially reachable huts.
        //
        // NavMesh.SamplePosition asks Unity for the nearest actually-walkable point
        // within 3m of the hut — guaranteed outside the carve. Path-testing to that
        // point gives a clean PathComplete/PathPartial/PathInvalid answer.
        //
        // CalculatePath is throttled (2/frame globally). If throttled, accept the
        // target optimistically — worst case enemy gets stuck and StuckResolver
        // re-picks.
        NavMeshHit walkableHit;
        if (!NavMesh.SamplePosition(nearest.position, out walkableHit, 3f, NavMesh.AllAreas))
            return null; // No NavMesh within 3m of hut — truly unreachable island

        if (AINavHelper.TryCalculatePath(myPos, walkableHit.position, NavMesh.AllAreas, reachabilityPath))
        {
            if (reachabilityPath.status != NavMeshPathStatus.PathComplete)
                return null;
        }
        return nearest;
    }

    Transform FindNearestWallOrGate(Vector3 myPos)
    {
        // Gates get strong preference: 0.3x effective distance so swarms funnel through.
        float wallDist, gateDist;
        Wall wall = TargetingUtil.FindNearest(Wall.ActiveList, myPos, 0f, out wallDist);
        Gate gate = TargetingUtil.FindNearest(Gate.ActiveList, myPos, 0f, out gateDist);

        if (gate != null && (wall == null || gateDist * 0.3f < wallDist)) return gate.transform;
        return wall != null ? wall.transform : null;
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
        // Enemies drop out of attack state whenever the target changes so the
        // edge-distance check re-runs against the new target's collider.
        if (bb.SetTarget(t, name))
            bb.isInAttackRange = false;
    }

    // ---- Movement + combat ----

    void IssueMove(AIBlackboard bb, bool force)
    {
        if (bb.currentTarget == null) return;
        if (bb.agent == null || !bb.agent.isOnNavMesh) return;
        if (force) moveQueued = true;
        if (!moveQueued && bb.agent.pathPending) return;

        bool needNew = moveQueued
            || !bb.agent.hasPath
            || bb.agent.pathStatus == NavMeshPathStatus.PathInvalid
            || Vector3.Distance(bb.currentTarget.position, lastTargetPosition) > DestinationUpdateThreshold;

        if (!needNew) return;

        // Deliberately NO ResetPath() here. ResetPath drops the agent's path and
        // zeroes its velocity immediately, so the enemy stands still until the new
        // path is computed — with a whole wave retargeting at once that reads as a
        // synchronized freeze. SetDestination swaps the path in place; the agent keeps
        // walking the old one until the new one is ready.
        Vector3 approach = TargetingUtil.GetApproachPoint(
            bb.transform.position, bb.currentTarget, bb.currentTargetCollider);

        if (AINavHelper.TrySetDestination(bb.agent, approach))
        {
            lastTargetPosition = bb.currentTarget.position;
            bb.agent.isStopped = false;
            moveQueued = false;
        }
        // If throttled/rejected, moveQueued stays true and we retry next frame.
    }

    void UpdateAttackState(AIBlackboard bb)
    {
        float d = bb.TargetEdgeDistance();
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
}

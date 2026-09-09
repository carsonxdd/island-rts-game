using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Warrior executor: Target selection with hysteresis, move to enemy, attack.
/// Ports existing Warrior combat logic with wall-attack bonus and target locking.
///
/// Phase 6.25: target bookkeeping (set/clear/alive) moved to AIBlackboard,
/// range checks use collider edge distance via bb.TargetEdgeDistance, enemy
/// agent lookups use Enemy.CachedAgent (no more local dictionary), and every
/// TrySetDestination return is honored (a rejected set retries via the
/// !hasPath branch in MoveTowardTarget instead of being silently dropped).
///
/// 2026-09-07: the target scan honours <see cref="GuardStance.Allows"/> (the
/// same filter the StanceTargetAvailable consideration scored with), and the
/// warrior walks to its own ATTACK SLOT on the enemy — one of
/// <see cref="Enemy.AttackSlotCount"/> bearings, claimed through
/// <see cref="Enemy.ClaimAttackSlot"/> — instead of the enemy's centre, so
/// three warriors on one raider come in from three sides on three paths rather
/// than queueing on the same line. The stopping distance drops to 0.5 for the
/// duration (the warrior's default, attackRange - 1, would park it a full
/// reach short of a slot point); the edge-distance range check still decides
/// when to stop and swing.
/// </summary>
public class EngageEnemyExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Engaging";

    private const float SlotStoppingDistance = 0.5f;

    // The attack slot on the current target (2026-09-07)
    private Enemy slotEnemy;
    private int slotIndex = -1;
    private float originalStoppingDistance;
    private bool stoppingDistanceSwapped;

    // Archer kiting (2026-09-07). Offensive: back off to GuardStance.KiteRange
    // whenever a raider is inside KiteTrigger. Defensive / Follow: hold the spot
    // and shoot; one BackStep toward the fire (or the castaway) when a raider is
    // inside HoldTrigger, never further than HoldLeash from where the fight
    // started, so an archer never walks out of the wall line with a raider on
    // its heels. Arrows keep flying while backing off.
    private bool kiting;
    private float kiteTimer;
    private Vector3 engageAnchor;
    private const float KiteMaxSeconds = 2.5f;   // a kite leg that has not arrived by now is abandoned

    // Target-switching hysteresis (ported from Warrior)
    private float targetAcquiredTime = 0f;
    private float minTargetLockDuration = 1.0f;
    private float targetSwitchThreshold = 0.7f;

    // Attack range hysteresis
    private float attackRangeBuffer = 0.5f;

    // Destination update throttle
    private float destinationUpdateThreshold = 3.0f;
    private Vector3 lastTargetPosition;

    // Retarget timer — don't scan every frame, scan every 0.5s
    private float retargetTimer = 0f;
    private float retargetInterval = 0.5f;

    public override void OnEnter(AIBlackboard bb)
    {
        if (bb.agent != null && bb.agent.isOnNavMesh && !stoppingDistanceSwapped)
        {
            originalStoppingDistance = bb.agent.stoppingDistance;
            bb.agent.stoppingDistance = SlotStoppingDistance;
            stoppingDistanceSwapped = true;
        }
        kiting = false;
        engageAnchor = bb.transform.position;
        switch (GuardStance.Effective)   // playtest: which order the fight started under
        {
            case GuardStance.Mode.Follow: DevQuests.Signal("engage:follow"); break;
            case GuardStance.Mode.Offensive: DevQuests.Signal("engage:offensive"); break;
        }

        // Don't reset isInAttackRange if we already had a target — preserve state for smooth re-entry
        if (bb.currentTarget == null || !bb.IsTargetAlive())
        {
            bb.isInAttackRange = false;
            FindBestTarget(bb);
        }

        if (bb.currentTarget != null)
        {
            bb.agent.isStopped = false;
            if (AINavHelper.TrySetDestination(bb.agent, ApproachPoint(bb)))
                lastTargetPosition = bb.currentTarget.position;
            // On rejection MoveTowardTarget's !hasPath branch retries next frame.
            targetAcquiredTime = Time.time;
            displayName = "Engaging " + bb.currentTargetName;
        }

        retargetTimer = Random.Range(0f, retargetInterval * 0.5f);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // --- Dead target cleanup (robust Unity null check) ---
        if (!bb.IsTargetAlive())
        {
            bb.ClearTarget();
            FindBestTarget(bb);
            if (bb.currentTarget == null) return; // No enemies, brain will switch action

            bb.agent.isStopped = false;
            if (AINavHelper.TrySetDestination(bb.agent, ApproachPoint(bb)))
                lastTargetPosition = bb.currentTarget.position;
            targetAcquiredTime = Time.time;
        }

        // --- Periodic retargeting with hysteresis ---
        retargetTimer -= Time.deltaTime;
        if (retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            FindBestTarget(bb); // Hysteresis inside prevents unnecessary switching
        }

        // Safety: if target got cleared by something
        if (bb.currentTarget == null) return;

        // --- Stuck resolution (only when moving, not attacking) ---
        // A stuck reset fires Warrior's onStuckReset callback, which clears
        // bb.currentTarget mid-call (this was the EngageEnemyExecutor:92 NRE in
        // the 2026-08-24 playtest log). Bail out; the callback ForceReeval'd.
        if (bb.stuckResolver != null && !bb.isInAttackRange && bb.stuckResolver.UpdateMoving())
            return;

        // --- Archer kiting (2026-09-07): backing off wins over closing in ---
        if (bb.isRanged && KiteStep(bb))
        {
            if (bb.TargetEdgeDistance() <= bb.attackRange) AttemptAttack(bb);   // shoot on the move
            displayName = "Falling back from " + bb.currentTargetName;
            return;
        }

        // --- Movement ---
        MoveTowardTarget(bb);

        // --- Attack range check with hysteresis (collider edge distance) ---
        float distanceToTarget = bb.TargetEdgeDistance();

        if (bb.isInAttackRange)
        {
            if (distanceToTarget > bb.attackRange + attackRangeBuffer)
            {
                bb.isInAttackRange = false;
                bb.agent.isStopped = false; // Resume movement only on range exit
            }
        }
        else
        {
            if (distanceToTarget <= bb.attackRange)
            {
                bb.isInAttackRange = true;
                bb.agent.isStopped = true; // Stop only on range entry
                bb.agent.ResetPath();
            }
        }

        if (bb.isInAttackRange)
        {
            // Face the target smoothly
            Vector3 lookDirection = (bb.currentTarget.position - bb.transform.position).normalized;
            lookDirection.y = 0;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                bb.transform.rotation = Quaternion.Slerp(
                    bb.transform.rotation,
                    Quaternion.LookRotation(lookDirection),
                    Time.deltaTime * 5f);
            }

            AttemptAttack(bb);
            displayName = "Attacking " + bb.currentTargetName + "!";
        }
        else
        {
            displayName = "Engaging " + bb.currentTargetName;
        }
    }

    void FindBestTarget(AIBlackboard bb)
    {
        if (Enemy.ActiveList.Count == 0)
        {
            bb.ClearTarget();
            return;
        }

        Vector3 from = bb.transform.position;
        Enemy nearest = null;
        float nearestDistance = float.MaxValue;   // the stance decides reach, not warriorSearchRadius

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;

            Health enemyHealth = enemy.CachedHealth;
            if (enemyHealth == null || !enemyHealth.IsAlive) continue;

            float distance = Vector3.Distance(from, enemy.transform.position);
            bool headingForWall = enemy.IsHeadingForWall();

            // Wall-attack bonus: enemies attacking walls treated as closer
            if (headingForWall) distance *= 0.5f;
            if (distance >= nearestDistance) continue;

            // The stance's filter, after the cheap distance cull (2026-09-07)
            if (!GuardStance.Allows(enemy, from, bb.baseBuilding)) continue;

            nearestDistance = distance;
            nearest = enemy;
        }

        // Also update bb.nearestEnemy for considerations to read
        bb.nearestEnemy = nearest != null ? nearest.transform : null;
        bb.nearestEnemyDistance = nearest != null ? nearestDistance : float.MaxValue;

        if (nearest != null)
        {
            // Hysteresis: don't switch if we have a valid living target — unless the
            // stance no longer allows the one we have (the player changed orders).
            if (bb.currentTarget != null && bb.IsTargetAlive()
                && (slotEnemy == null || GuardStance.Allows(slotEnemy, from, bb.baseBuilding)))
            {
                if (Time.time - targetAcquiredTime < minTargetLockDuration)
                    return;

                float currentDist = Vector3.Distance(from, bb.currentTarget.position);
                if (nearestDistance > currentDist * targetSwitchThreshold)
                    return;
            }

            if (bb.SetTarget(nearest.transform, nearest.gameObject.name))
            {
                targetAcquiredTime = Time.time;
                TakeSlot(bb, nearest);
            }
        }
        else
        {
            bb.ClearTarget();
            ReleaseSlot(bb);
        }
    }

    // --- Archer kiting (2026-09-07) ---

    /// <summary>
    /// Runs the kite state; true while the archer is backing off (the caller
    /// skips the approach for that tick). Starts a leg when the nearest raider —
    /// not necessarily the target — is inside the stance's trigger; ends it on
    /// arrival, on a timeout, or once the raider has dropped back.
    /// </summary>
    bool KiteStep(AIBlackboard bb)
    {
        bool offensive = GuardStance.Effective == GuardStance.Mode.Offensive;
        float trigger = offensive ? GuardStance.KiteTrigger : GuardStance.HoldTrigger;

        float threatDist;
        Enemy threat = TargetingUtil.FindNearest(Enemy.ActiveList, bb.transform.position, trigger + 1.5f, out threatDist);

        if (kiting)
        {
            kiteTimer += Time.deltaTime;
            bool arrived = bb.agent.isOnNavMesh && !bb.agent.pathPending
                && (!bb.agent.hasPath || bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.3f);
            bool clear = threat == null || threatDist >= trigger + 1.5f;
            if (arrived || clear || kiteTimer > KiteMaxSeconds)
            {
                kiting = false;
                // MoveTowardTarget re-paths to the slot: a finished kite leg has no path
                return false;
            }
            return true;
        }

        if (threat == null || threatDist > trigger) return false;
        if (bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return false;

        Vector3 pos = bb.transform.position;
        Vector3 away = pos - threat.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -bb.transform.forward;
        away.Normalize();

        float step;
        if (offensive)
        {
            step = GuardStance.KiteRange - threatDist;
        }
        else
        {
            // Hold stances: a short step, bent toward home, and only inside the leash
            if ((pos - engageAnchor).sqrMagnitude >= GuardStance.HoldLeash * GuardStance.HoldLeash)
                return false;   // stand and shoot; the spearmen have it
            Vector3 home = Vector3.zero;
            if (GuardStance.Effective == GuardStance.Mode.Follow && PlayerCharacter.Instance != null)
                home = PlayerCharacter.Instance.transform.position - pos;
            else if (bb.baseBuilding != null)
                home = bb.baseBuilding.transform.position - pos;
            home.y = 0f;
            if (home.sqrMagnitude > 0.01f) away = (away + home.normalized * 0.6f).normalized;
            step = GuardStance.BackStep;
        }

        NavMeshHit hit;
        Vector3 dest = pos + away * step;
        if (!NavMesh.SamplePosition(dest, out hit, 2.5f, NavMesh.AllAreas)) return false;
        if (!AINavHelper.TrySetDestination(bb.agent, hit.position)) return false;   // throttled: try again next tick

        kiting = true;
        DevQuests.Signal(offensive ? "kite:offensive" : "kite:hold");
        kiteTimer = 0f;
        bb.isInAttackRange = false;   // moving again; the range hold re-trips on arrival
        bb.agent.isStopped = false;
        return true;
    }

    // --- Attack slots (2026-09-07) ---

    /// <summary>Claim a bearing on the new target and drop the one on the old.</summary>
    void TakeSlot(AIBlackboard bb, Enemy enemy)
    {
        ReleaseSlot(bb);
        slotEnemy = enemy;
        slotIndex = enemy.ClaimAttackSlot(bb.warrior, bb.transform.position);
    }

    void ReleaseSlot(AIBlackboard bb)
    {
        if (slotEnemy != null) slotEnemy.ReleaseAttackSlot(bb.warrior);
        slotEnemy = null;
        slotIndex = -1;
    }

    /// <summary>
    /// Where to walk: this warrior's slot on the target, one weapon reach short
    /// of the centre and snapped to the NavMesh, or the centre itself when the
    /// slot is stale (target changed under us) or off the mesh.
    /// </summary>
    Vector3 ApproachPoint(AIBlackboard bb)
    {
        if (bb.currentTarget == null) return bb.transform.position;

        if (slotEnemy == null || slotEnemy.transform != bb.currentTarget)
        {
            Enemy e = bb.currentTarget.GetComponent<Enemy>();
            if (e != null) TakeSlot(bb, e);
        }
        if (slotEnemy == null || slotIndex < 0) return bb.currentTarget.position;

        float reach = Mathf.Max(1f, bb.attackRange - 1f);
        Vector3 want = slotEnemy.AttackSlotPoint(slotIndex, reach);
        NavMeshHit hit;
        if (NavMesh.SamplePosition(want, out hit, 2f, NavMesh.AllAreas))
            return hit.position;
        return bb.currentTarget.position;
    }

    void MoveTowardTarget(AIBlackboard bb)
    {
        if (bb.currentTarget == null) return;
        if (bb.isInAttackRange) return; // Don't re-path while attacking
        if (bb.agent.pathPending) return;

        float distanceMoved = Vector3.Distance(bb.currentTarget.position, lastTargetPosition);
        bool needsNewPath = !bb.agent.hasPath || bb.agent.pathStatus == NavMeshPathStatus.PathInvalid;

        if (distanceMoved > destinationUpdateThreshold || needsNewPath)
        {
            if (AINavHelper.TrySetDestination(bb.agent, ApproachPoint(bb)))
            {
                lastTargetPosition = bb.currentTarget.position;
            }
        }
    }

    void AttemptAttack(AIBlackboard bb)
    {
        if (Time.time - bb.lastAttackTime < bb.attackCooldown) return;

        // Final alive check before dealing damage
        if (!bb.IsTargetAlive())
        {
            bb.ClearTarget();
            return;
        }

        bb.lastAttackTime = Time.time;

        // Watchtower damage buff (bb.damage already carries the weapon's stats)
        float towerMultiplier = Watchtower.GetDamageMultiplier(bb.transform.position);
        float finalDamage = bb.damage * towerMultiplier;
        bool hasTowerBuff = towerMultiplier > 1f;

        if (hasTowerBuff)
        {
            displayName = "Attacking " + bb.currentTargetName + "! (Tower Buff)";
        }

        // Audio
        bb.warrior.PlayAttackSoundPublic();

        // An archer (2026-09-04) looses an arrow that carries the damage to the
        // target; the range check above already holds the agent at the weapon's
        // reach, which IS the range hold. No line of sight — over the wall is the point.
        if (bb.isRanged)
        {
            if (CombatEffects.Instance != null)
            {
                CombatEffects.Instance.FireArrow(bb.transform.position + Vector3.up * 1.2f,
                    bb.currentTarget, bb.currentTargetHealth, finalDamage);
            }
            else if (bb.currentTargetHealth != null)
            {
                bb.currentTargetHealth.TakeDamage(finalDamage);   // headless: no effects manager
            }
            return;
        }

        // Visual effect
        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(bb.transform.position, bb.currentTarget.position, true);
        }

        // Apply damage
        if (bb.currentTargetHealth != null)
        {
            bb.currentTargetHealth.TakeDamage(finalDamage);
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        // Don't clear target — keep it so re-entering this action is seamless.
        // The slot stays claimed with it; Enemy treats a slot whose owner is no
        // longer targeting it as free, so nothing leaks if we never come back.
        bb.isInAttackRange = false;
        kiting = false;
        if (bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
            if (stoppingDistanceSwapped) bb.agent.stoppingDistance = originalStoppingDistance;
        }
        stoppingDistanceSwapped = false;
    }
}

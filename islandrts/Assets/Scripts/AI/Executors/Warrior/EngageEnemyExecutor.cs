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
/// </summary>
public class EngageEnemyExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Engaging";

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
        // Don't reset isInAttackRange if we already had a target — preserve state for smooth re-entry
        if (bb.currentTarget == null || !bb.IsTargetAlive())
        {
            bb.isInAttackRange = false;
            FindBestTarget(bb);
        }

        if (bb.currentTarget != null)
        {
            bb.agent.isStopped = false;
            if (AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position))
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
            if (AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position))
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

        Transform nearestEnemy = null;
        float nearestDistance = bb.warriorSearchRadius;

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;

            Health enemyHealth = enemy.CachedHealth;
            if (enemyHealth == null || !enemyHealth.IsAlive) continue;

            float distance = Vector3.Distance(bb.transform.position, enemy.transform.position);

            // Wall-attack bonus: enemies attacking walls treated as closer
            if (IsEnemyAttackingWall(enemy))
            {
                distance *= 0.5f;
            }

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestEnemy = enemy.transform;
            }
        }

        // Also update bb.nearestEnemy for considerations to read
        bb.nearestEnemy = nearestEnemy;
        bb.nearestEnemyDistance = nearestEnemy != null ? nearestDistance : float.MaxValue;

        if (nearestEnemy != null)
        {
            // Hysteresis: don't switch if we have a valid living target
            if (bb.currentTarget != null && bb.IsTargetAlive())
            {
                if (Time.time - targetAcquiredTime < minTargetLockDuration)
                    return;

                float currentDist = Vector3.Distance(bb.transform.position, bb.currentTarget.position);
                if (nearestDistance > currentDist * targetSwitchThreshold)
                    return;
            }

            if (bb.SetTarget(nearestEnemy, nearestEnemy.gameObject.name))
                targetAcquiredTime = Time.time;
        }
        else
        {
            bb.ClearTarget();
        }
    }

    bool IsEnemyAttackingWall(Enemy enemy)
    {
        NavMeshAgent enemyAgent = enemy.CachedAgent;
        if (enemyAgent == null || !enemyAgent.hasPath) return false;

        if (WallGrid.Instance != null)
        {
            Vector2Int destGrid = WallGrid.Instance.WorldToGrid(enemyAgent.destination);
            return WallGrid.Instance.HasWallAt(destGrid);
        }
        return false;
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
            if (AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position))
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

        // Watchtower damage buff × Workshop "Forged Blades" upgrade
        float towerMultiplier = Watchtower.GetDamageMultiplier(bb.transform.position);
        float finalDamage = bb.damage * towerMultiplier * CraftedUpgrades.WarriorDamageMult;
        bool hasTowerBuff = towerMultiplier > 1f;

        if (hasTowerBuff)
        {
            displayName = "Attacking " + bb.currentTargetName + "! (Tower Buff)";
        }

        // Visual effect
        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(bb.transform.position, bb.currentTarget.position, true);
        }

        // Audio
        bb.warrior.PlayAttackSoundPublic();

        // Apply damage
        if (bb.currentTargetHealth != null)
        {
            bb.currentTargetHealth.TakeDamage(finalDamage);
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        // Don't clear target — keep it so re-entering this action is seamless
        bb.isInAttackRange = false;
        if (bb.agent.isOnNavMesh)
            bb.agent.isStopped = false;
    }
}

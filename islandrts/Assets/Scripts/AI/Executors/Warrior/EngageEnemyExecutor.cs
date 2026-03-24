using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Warrior executor: Target selection with hysteresis, move to enemy, attack.
/// Ports existing Warrior combat logic with wall-attack bonus and target locking.
/// Fixed: proper dead-target cleanup, smoother transitions, no isStopped thrashing.
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

    // Cached enemy agent lookups for wall-attack check
    private readonly System.Collections.Generic.Dictionary<Enemy, NavMeshAgent> enemyAgentCache
        = new System.Collections.Generic.Dictionary<Enemy, NavMeshAgent>();
    private float cacheCleanTimer = 0f;

    public override void OnEnter(AIBlackboard bb)
    {
        // Don't reset isInAttackRange if we already had a target — preserve state for smooth re-entry
        if (bb.currentTarget == null || !IsTargetAlive(bb))
        {
            bb.isInAttackRange = false;
            FindBestTarget(bb);
        }

        if (bb.currentTarget != null)
        {
            lastTargetPosition = bb.currentTarget.position;
            bb.agent.isStopped = false;
            AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
            targetAcquiredTime = Time.time;
            displayName = "Engaging " + bb.currentTargetName;
        }

        retargetTimer = Random.Range(0f, retargetInterval * 0.5f);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // --- Dead target cleanup (robust Unity null check) ---
        if (!IsTargetAlive(bb))
        {
            ClearTarget(bb);
            FindBestTarget(bb);
            if (bb.currentTarget == null) return; // No enemies, brain will switch action

            lastTargetPosition = bb.currentTarget.position;
            bb.agent.isStopped = false;
            AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
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
        if (bb.stuckResolver != null && !bb.isInAttackRange)
        {
            bb.stuckResolver.UpdateMoving();
        }

        // --- Movement ---
        MoveTowardTarget(bb);

        // --- Attack range check with hysteresis ---
        float distanceToTarget = Vector3.Distance(bb.transform.position, bb.currentTarget.position);

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
            ClearTarget(bb);
            return;
        }

        Transform nearestEnemy = null;
        float nearestDistance = bb.warriorSearchRadius;

        // Clean enemy agent cache periodically
        cacheCleanTimer += Time.deltaTime;
        if (cacheCleanTimer >= 10f)
        {
            cacheCleanTimer = 0f;
            enemyAgentCache.Clear();
        }

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;

            Health enemyHealth = enemy.CachedHealth;
            if (enemyHealth != null && !enemyHealth.IsAlive) continue;

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
            if (bb.currentTarget != null && IsTargetAlive(bb))
            {
                if (Time.time - targetAcquiredTime < minTargetLockDuration)
                    return;

                float currentDist = Vector3.Distance(bb.transform.position, bb.currentTarget.position);
                if (nearestDistance > currentDist * targetSwitchThreshold)
                    return;
            }

            bb.currentTarget = nearestEnemy;
            bb.currentTargetName = nearestEnemy.gameObject.name;
            bb.currentTargetHealth = nearestEnemy.GetComponent<Health>();
            targetAcquiredTime = Time.time;
        }
        else
        {
            ClearTarget(bb);
        }
    }

    void ClearTarget(AIBlackboard bb)
    {
        bb.currentTarget = null;
        bb.currentTargetHealth = null;
        bb.currentTargetName = "";
        bb.isInAttackRange = false;
    }

    bool IsEnemyAttackingWall(Enemy enemy)
    {
        NavMeshAgent enemyAgent;
        if (!enemyAgentCache.TryGetValue(enemy, out enemyAgent))
        {
            enemyAgent = enemy.GetComponent<NavMeshAgent>();
            enemyAgentCache[enemy] = enemyAgent;
        }
        if (enemyAgent == null || !enemyAgent.hasPath) return false;

        if (WallGrid.Instance != null)
        {
            Vector2Int destGrid = WallGrid.Instance.WorldToGrid(enemyAgent.destination);
            return WallGrid.Instance.HasWallAt(destGrid);
        }
        return false;
    }

    /// <summary>
    /// Robust alive check. Handles Unity destroyed-object null semantics.
    /// A target is dead if: the Transform is destroyed, OR Health exists and IsAlive is false.
    /// If Health is destroyed (component gone), we also treat it as dead.
    /// </summary>
    bool IsTargetAlive(AIBlackboard bb)
    {
        // Unity null check — destroyed GameObjects compare to null
        if (bb.currentTarget == null) return false;

        // If we had a health reference but it's now destroyed, target is dead
        if (bb.currentTargetHealth == null)
        {
            // Try to re-fetch in case it was never cached
            bb.currentTargetHealth = bb.currentTarget.GetComponent<Health>();
            if (bb.currentTargetHealth == null) return false; // No health component = dead/invalid
        }

        return bb.currentTargetHealth.IsAlive;
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
        if (!IsTargetAlive(bb))
        {
            ClearTarget(bb);
            return;
        }

        bb.lastAttackTime = Time.time;

        // Watchtower damage buff
        float towerMultiplier = Watchtower.GetDamageMultiplier(bb.transform.position);
        float finalDamage = bb.damage * towerMultiplier;
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

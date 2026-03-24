using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy executor: Move to priority target (warrior or building) and attack.
/// Ports existing Enemy targeting and combat logic.
/// </summary>
public class AttackTargetExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Attacking";

    public enum TargetCategory { Warrior, Building, Campfire }
    private readonly TargetCategory category;

    // Attack range hysteresis
    private float attackRangeBuffer = 0.5f;
    private float destinationUpdateThreshold = 1.5f;
    private Vector3 lastTargetPosition;

    // Periodic retargeting — pick closer targets as enemy moves
    private float retargetInterval = 2f;
    private float retargetTimer = 0f;

    public AttackTargetExecutor(TargetCategory category)
    {
        this.category = category;
    }

    public override void OnEnter(AIBlackboard bb)
    {
        bb.isInAttackRange = false;
        bb.isAttackingWall = false;
        retargetTimer = 0f;
        FindTarget(bb);

        if (bb.stuckResolver != null)
            bb.stuckResolver.ResetStuckDetection();

        if (bb.currentTarget != null)
        {
            lastTargetPosition = bb.currentTarget.position;
            bb.agent.isStopped = false;
            AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
            displayName = "Moving to " + bb.currentTargetName;
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Check target validity
        if (bb.currentTarget == null || !IsTargetAlive(bb))
        {
            FindTarget(bb);
            if (bb.currentTarget == null) return;

            lastTargetPosition = bb.currentTarget.position;
            bb.agent.isStopped = false;
            AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
            bb.isInAttackRange = false;
        }

        // Periodic retargeting while moving — pick closer targets as position changes
        if (!bb.isInAttackRange)
        {
            retargetTimer += Time.deltaTime;
            if (retargetTimer >= retargetInterval)
            {
                retargetTimer = 0f;

                // For building/campfire executors: check if a warrior just entered range
                if (category != TargetCategory.Warrior)
                {
                    if (HasWarriorNearby(bb))
                    {
                        bb.brain.ForceReeval();
                        return;
                    }
                }

                // Re-scan for a closer target of the same category
                Transform oldTarget = bb.currentTarget;
                FindTarget(bb);
                if (bb.currentTarget != oldTarget && bb.currentTarget != null)
                {
                    lastTargetPosition = bb.currentTarget.position;
                    AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
                }
                else if (bb.currentTarget == null)
                {
                    bb.currentTarget = oldTarget; // Keep old if no new target found
                }
            }
        }

        // Move toward target
        MoveTowardTarget(bb);

        // Stuck resolution (when moving, not attacking)
        if (bb.stuckResolver != null && !bb.isInAttackRange)
            bb.stuckResolver.UpdateMoving();

        // Attack range check with hysteresis
        float distanceToTarget = Vector3.Distance(bb.transform.position, bb.currentTarget.position);
        float effectiveAttackRange = bb.attackRange;

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
            retargetTimer = 0f; // Reset retarget timer while attacking
            bb.agent.isStopped = true;
            displayName = "Attacking " + bb.currentTargetName + "!";
            AttemptAttack(bb);
        }
        else
        {
            bb.agent.isStopped = false;
            displayName = "Moving to " + bb.currentTargetName;
        }
    }

    bool HasWarriorNearby(AIBlackboard bb)
    {
        for (int i = 0; i < Warrior.ActiveList.Count; i++)
        {
            Warrior warrior = Warrior.ActiveList[i];
            if (warrior == null) continue;
            Health h = warrior.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, warrior.transform.position);
            if (dist <= bb.warriorDetectionRange)
                return true;
        }
        return false;
    }

    void FindTarget(AIBlackboard bb)
    {
        switch (category)
        {
            case TargetCategory.Warrior:
                FindNearestWarrior(bb);
                break;
            case TargetCategory.Building:
                FindNearestBuilding(bb);
                break;
            case TargetCategory.Campfire:
                FindCampfire(bb);
                break;
        }
    }

    void FindNearestWarrior(AIBlackboard bb)
    {
        Transform best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < Warrior.ActiveList.Count; i++)
        {
            Warrior warrior = Warrior.ActiveList[i];
            if (warrior == null) continue;
            Health h = warrior.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, warrior.transform.position);
            if (dist <= bb.warriorDetectionRange && dist < bestDist)
            {
                bestDist = dist;
                best = warrior.transform;
            }
        }

        if (best != null)
        {
            bb.currentTarget = best;
            bb.currentTargetName = best.gameObject.name;
            bb.currentTargetHealth = best.GetComponent<Health>();
        }
        else
        {
            bb.currentTarget = null;
        }
    }

    void FindNearestBuilding(AIBlackboard bb)
    {
        Transform best = null;
        float bestDist = float.MaxValue;
        string bestName = "";

        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;
            Health h = hut.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, hut.transform.position);
            if (dist <= bb.buildingEngagementRange && dist < bestDist)
            {
                bestDist = dist;
                best = hut.transform;
                bestName = hut.gameObject.name;
            }
        }

        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            Watchtower tower = Watchtower.ActiveList[i];
            if (tower == null) continue;
            Health h = tower.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, tower.transform.position);
            if (dist <= bb.buildingEngagementRange && dist < bestDist)
            {
                bestDist = dist;
                best = tower.transform;
                bestName = tower.gameObject.name;
            }
        }

        if (best != null)
        {
            bb.currentTarget = best;
            bb.currentTargetName = bestName;
            bb.currentTargetHealth = best.GetComponent<Health>();
        }
        else
        {
            bb.currentTarget = null;
        }
    }

    void FindCampfire(AIBlackboard bb)
    {
        if (BaseBuilding.ActiveList.Count > 0)
        {
            BaseBuilding campfire = BaseBuilding.ActiveList[0];
            if (campfire != null)
            {
                Health h = campfire.CachedHealth;
                if (h != null && h.IsAlive)
                {
                    bb.currentTarget = campfire.transform;
                    bb.currentTargetName = "Campfire";
                    bb.currentTargetHealth = h;
                    return;
                }
            }
        }
        bb.currentTarget = null;
    }

    bool IsTargetAlive(AIBlackboard bb)
    {
        if (bb.currentTarget == null) return false;
        if (bb.currentTargetHealth == null)
        {
            // Re-fetch in case it was never cached
            bb.currentTargetHealth = bb.currentTarget.GetComponent<Health>();
            if (bb.currentTargetHealth == null) return false;
        }
        return bb.currentTargetHealth.IsAlive;
    }

    void MoveTowardTarget(AIBlackboard bb)
    {
        if (bb.currentTarget == null) return;
        if (bb.agent.pathPending) return;

        float distanceMoved = Vector3.Distance(bb.currentTarget.position, lastTargetPosition);
        bool needsNewPath = !bb.agent.hasPath || bb.agent.pathStatus == NavMeshPathStatus.PathInvalid;

        if (distanceMoved > destinationUpdateThreshold || needsNewPath)
        {
            AINavHelper.TrySetDestination(bb.agent, bb.currentTarget.position);
            lastTargetPosition = bb.currentTarget.position;
        }
    }

    void AttemptAttack(AIBlackboard bb)
    {
        if (Time.time - bb.lastAttackTime < bb.attackCooldown) return;
        bb.lastAttackTime = Time.time;

        // Visual effect
        if (CombatEffects.Instance != null)
        {
            CombatEffects.Instance.SpawnAttackEffect(bb.transform.position, bb.currentTarget.position, false);
        }

        // Audio
        bb.enemy.PlayAttackSoundPublic();

        // Apply damage
        if (bb.currentTargetHealth != null)
        {
            bb.currentTargetHealth.TakeDamage(bb.damage);
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        bb.isInAttackRange = false;
        bb.agent.isStopped = false;
    }
}

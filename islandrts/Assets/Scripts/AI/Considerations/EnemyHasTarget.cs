using UnityEngine;

/// <summary>
/// Enemy-specific consideration: checks if a valid target of the given category exists.
/// Returns 1.0 if a target is available and in range, 0.0 otherwise.
/// </summary>
public class EnemyHasTarget : Consideration
{
    public enum TargetCategory { Warrior, Building, Campfire }
    private readonly TargetCategory category;

    public EnemyHasTarget(TargetCategory category, ResponseCurve curve) : base(curve)
    {
        this.category = category;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        switch (category)
        {
            case TargetCategory.Warrior:
                return HasWarriorInRange(bb) ? 1f : 0f;
            case TargetCategory.Building:
                return HasBuildingInRange(bb) ? 1f : 0f;
            case TargetCategory.Campfire:
                return HasCampfire() ? 1f : 0f;
            default:
                return 0f;
        }
    }

    bool HasWarriorInRange(AIBlackboard bb)
    {
        float closestDist = float.MaxValue;
        Transform closestWarrior = null;

        for (int i = 0; i < Warrior.ActiveList.Count; i++)
        {
            Warrior warrior = Warrior.ActiveList[i];
            if (warrior == null) continue;
            Health h = warrior.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, warrior.transform.position);
            if (dist <= bb.warriorDetectionRange && dist < closestDist)
            {
                closestDist = dist;
                closestWarrior = warrior.transform;
            }
        }

        if (closestWarrior != null)
        {
            bb.nearestEnemy = closestWarrior;
            bb.nearestEnemyDistance = closestDist;
            // Boost score when a warrior is actively hitting us (within attack range + buffer)
            if (closestDist <= bb.attackRange + 2f)
                return true; // score = 1.0 via curve
            return true;
        }
        return false;
    }

    bool HasBuildingInRange(AIBlackboard bb)
    {
        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;
            Health h = hut.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, hut.transform.position);
            if (dist <= bb.buildingEngagementRange)
                return true;
        }

        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            Watchtower tower = Watchtower.ActiveList[i];
            if (tower == null) continue;
            Health h = tower.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, tower.transform.position);
            if (dist <= bb.buildingEngagementRange)
                return true;
        }

        return false;
    }

    bool HasCampfire()
    {
        if (BaseBuilding.ActiveList.Count == 0) return false;
        BaseBuilding campfire = BaseBuilding.ActiveList[0];
        if (campfire == null) return false;
        Health h = campfire.CachedHealth;
        return h != null && h.IsAlive;
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Applies a <see cref="SimConfig"/>'s balance knobs to units as they spawn.
///
/// Why this exists rather than "just edit the prefab": a public float on a unit
/// script is dead data — the prefab's serialized value wins — and unit Starts
/// copy those values into the AI blackboard, so patching an instance after the
/// fact is too late. Mutating the prefab asset at runtime would also dirty it on
/// disk when running inside the editor.
///
/// So each unit calls <see cref="Apply"/> at the top of its Start, guarded by
/// UNITY_EDITOR || DEVELOPMENT_BUILD. With no active config it is a null check.
/// Everything else the harness varies lives on scene singletons and is set
/// directly by <see cref="SimRunner"/>.
/// </summary>
public static class SimOverrides
{
    public static SimConfig Active;

    public static void Apply(Worker worker)
    {
        if (Active == null || worker == null) return;
        if (Active.workerGatherRate >= 0f) worker.gatherRatePerSecond = Active.workerGatherRate;
        // Carry capacity keeps the original's fudge (>5 avoids a float compare
        // that would strand a worker at 4.999/5).
        if (Active.workerCarryCapacity >= 0) worker.carryCapacity = Active.workerCarryCapacity + 0.01f;
    }

    public static void Apply(Warrior warrior)
    {
        if (Active == null || warrior == null) return;
        if (Active.warriorHealth >= 0f) warrior.maxHealth = Active.warriorHealth;
        if (Active.warriorDamage >= 0f) warrior.damage = Active.warriorDamage;
        if (Active.warriorMoveSpeed >= 0f) warrior.moveSpeed = Active.warriorMoveSpeed;
        if (Active.warriorAttackCooldown > 0f) warrior.attackCooldown = Active.warriorAttackCooldown;
        if (Active.warriorSearchRadius >= 0f) warrior.searchRadius = Active.warriorSearchRadius;
        if (Active.warriorPatrolRadius >= 0f) warrior.patrolRadius = Active.warriorPatrolRadius;
    }

    public static void Apply(Enemy enemy)
    {
        if (Active == null || enemy == null) return;
        if (Active.enemyHealth >= 0f) enemy.maxHealth = Active.enemyHealth;
        if (Active.enemyDamage >= 0f) enemy.damage = Active.enemyDamage;
        if (Active.enemyMoveSpeed >= 0f) enemy.moveSpeed = Active.enemyMoveSpeed;
        if (Active.enemyAttackCooldown > 0f) enemy.attackCooldown = Active.enemyAttackCooldown;
        if (Active.enemyWarriorDetectionRange >= 0f)
            enemy.warriorDetectionRange = Active.enemyWarriorDetectionRange;
    }

    public static void Apply(Hut hut)
    {
        if (Active == null || hut == null) return;
        if (Active.hutHealth > 0f) hut.maxHealth = Active.hutHealth;
    }

    public static void Apply(BaseBuilding campfire)
    {
        if (Active == null || campfire == null) return;
        if (Active.campfireHealth > 0f) campfire.maxHealth = Active.campfireHealth;
        // maxWarriors / warrior costs are applied here too rather than from
        // SimRunner, so a campfire spawned mid-run (the opening sequence places
        // it at runtime) picks them up the same way a pre-placed one would.
        if (Active.maxWarriors >= 0) campfire.maxWarriors = Active.maxWarriors;
        if (Active.warriorCostWood >= 0) campfire.warriorCost_Wood = Active.warriorCostWood;
        if (Active.warriorCostFood >= 0) campfire.warriorCost_Food = Active.warriorCostFood;
    }

    public static void Apply(Watchtower tower)
    {
        if (Active == null || tower == null) return;
        if (Active.watchtowerHealth > 0f) tower.maxHealth = Active.watchtowerHealth;
        if (Active.watchtowerDamageMultiplier > 0f)
            tower.damageMultiplier = Active.watchtowerDamageMultiplier;
        if (Active.watchtowerBuffRadius > 0f) tower.buffRadius = Active.watchtowerBuffRadius;
    }

    /// <summary>
    /// Warrior campfire heal rate, read at the point of effect rather than
    /// pushed onto a component — <see cref="HealAtCampfireExecutor"/> keeps it
    /// as a constant and there is no per-warrior field to patch.
    /// </summary>
    public static float HealRate(float shipping)
    {
        return (Active != null && Active.warriorHealRate >= 0f) ? Active.warriorHealRate : shipping;
    }
}
#endif

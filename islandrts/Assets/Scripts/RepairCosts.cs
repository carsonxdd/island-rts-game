using UnityEngine;

/// <summary>
/// What repairing a building costs and how fast it goes. A full repair from zero costs
/// <see cref="CostFraction"/> of the build price, drawn incrementally as HP ticks up, so
/// a repair that is interrupted has only been paid for as far as it got and repair
/// pauses (never cancels) while the pool is dry.
/// </summary>
/// <remarks>
/// The campfire has no <see cref="BuildingData"/> of its own — it is priced like a hut.
/// </remarks>
public static class RepairCosts
{
    /// <summary>Fraction of the build cost a full repair (0 → max HP) costs.</summary>
    public const float CostFraction = 0.25f;

    /// <summary>HP restored per second by one builder.</summary>
    public const float RepairRate = 5f;

    public struct PerHp
    {
        public float wood, food, stone;
        public bool Any => wood > 0f || food > 0f || stone > 0f;
    }

    /// <summary>Per-HP price for a building of this type. False when the database has no entry.</summary>
    public static bool TryGetPerHp(BuildingType type, float maxHealth, out PerHp cost)
    {
        cost = default;
        if (BuildingDatabase.Instance == null || maxHealth <= 0f) return false;
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        if (data == null) return false;

        float k = CostFraction / maxHealth;
        cost.wood = data.woodCost * k;
        cost.food = data.foodCost * k;
        cost.stone = data.stoneCost * k;
        return true;
    }

    /// <summary>
    /// True when the pool holds at least one unit of every resource the repair draws on,
    /// i.e. the next whole-unit charge can be paid. Used to keep colonists from walking
    /// to a repair they cannot start.
    /// </summary>
    public static bool CanAffordAny(PerHp cost)
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return false;
        if (cost.wood > 0f && rm.wood < 1) return false;
        if (cost.food > 0f && rm.food < 1) return false;
        if (cost.stone > 0f && rm.stone < 1) return false;
        return true;
    }

    public static BuildingType TypeOf(Wall wall) => wall.isStoneWall ? BuildingType.StoneWall : BuildingType.WoodenWall;
    public static BuildingType TypeOf(Gate gate) => gate.isStoneGate ? BuildingType.StoneGate : BuildingType.WoodenGate;
}

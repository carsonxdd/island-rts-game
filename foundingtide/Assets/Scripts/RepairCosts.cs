using UnityEngine;

/// <summary>
/// What repairing a building costs and how fast it goes. A full repair from zero costs
/// <see cref="CostFraction"/> of the build price, drawn incrementally as HP ticks up, so
/// a repair that is interrupted has only been paid for as far as it got and repair
/// pauses (never cancels) while the pool is dry.
/// </summary>
/// <remarks>
/// The campfire has no <see cref="BuildingData"/> of its own — it is priced like a hut.
/// Neither has a gate (2026-09-10): a gate is converted from a finished wall and the
/// database only knows placeable types, so it is priced as the wall it was — the same
/// resolution <see cref="DemolishTool"/> uses for the refund. Asking the database for
/// <see cref="BuildingType.WoodenGate"/> made every gate unrepairable for life and logged
/// an error per AI evaluation per jobless colonist once one was scratched (9,408 lines
/// in one sim run).
/// A gate is up to three cells wide (2026-09-28) and costs as that many walls, so its
/// type goes in as the GATE type and <see cref="TryGetPerHp"/> resolves it here: the
/// wall's price per GATE-CELL hit point (<see cref="Gate.CellHealth"/>). That is the same
/// per-HP rate at any width, and a full repair of a three-wide gate costs three walls'.
/// </remarks>
public static class RepairCosts
{
    /// <summary>Fraction of the build cost a full repair (0 → max HP) costs.</summary>
    public const float CostFraction = 0.25f;

    /// <summary>HP restored per second by one builder.</summary>
    public const float RepairRate = 5f;

    public struct PerHp
    {
        public float wood, food, stone, metal;   // metal since the Shipyard (2026-09-04)
        public bool Any => wood > 0f || food > 0f || stone > 0f || metal > 0f;
    }

    /// <summary>Per-HP price for a building of this type. False when the database has no entry.</summary>
    public static bool TryGetPerHp(BuildingType type, float maxHealth, out PerHp cost)
    {
        cost = default;
        if (BuildingDatabase.Instance == null || maxHealth <= 0f) return false;

        // A gate: its wall's price, per hit point of ONE gate cell (see remarks)
        float hpBasis = maxHealth;
        if (type == BuildingType.WoodenGate || type == BuildingType.StoneGate)
        {
            bool stone = type == BuildingType.StoneGate;
            type = stone ? BuildingType.StoneWall : BuildingType.WoodenWall;
            hpBasis = Gate.CellHealth(stone);
        }

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        if (data == null) return false;

        float k = CostFraction / hpBasis;
        cost.wood = data.woodCost * k;
        cost.food = data.foodCost * k;
        cost.stone = data.stoneCost * k;
        cost.metal = data.metalCost * k;
        return true;
    }

    /// <summary>
    /// True when the pool holds at least one unit of every resource the repair draws on,
    /// i.e. the next whole-unit charge can be paid. Used to keep colonists from walking
    /// to a repair they cannot start.
    /// </summary>
    public static bool CanAffordAny(PerHp cost)
    {
        ResourcePool rm = Factions.Player.Resources;
        if (cost.wood > 0f && rm.wood < 1) return false;
        if (cost.food > 0f && rm.food < 1) return false;
        if (cost.stone > 0f && rm.stone < 1) return false;
        if (cost.metal > 0f && rm.metal < 1) return false;
        return true;
    }

    public static BuildingType TypeOf(Wall wall) => wall.isStoneWall ? BuildingType.StoneWall : BuildingType.WoodenWall;
    /// <summary>
    /// A gate's repair type is the GATE type; <see cref="TryGetPerHp"/> prices it as the
    /// wall it was converted from (no gate has a BuildingData) at the gate's width.
    /// </summary>
    public static BuildingType TypeOf(Gate gate) => gate.isStoneGate ? BuildingType.StoneGate : BuildingType.WoodenGate;
    /// <summary>The wall a gate was converted from: what its demolish refund is priced as (× width).</summary>
    public static BuildingType WallTypeOf(Gate gate) => gate.isStoneGate ? BuildingType.StoneWall : BuildingType.WoodenWall;
}

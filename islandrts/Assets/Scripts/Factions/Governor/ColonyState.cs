using UnityEngine;

/// <summary>
/// Read-only snapshot of one colony handed to its <see cref="GovernorPolicy"/>
/// each tick (no allocation). Was <c>SimState</c> while only the sim's
/// simulated player read it (2026-09-16, lap step 3 slice B); a rival's
/// governor reads the same struct now, captured for ITS faction.
/// </summary>
public struct ColonyState
{
    public Faction Faction;
    public BaseBuilding Campfire;
    public int Day;
    /// <summary>The director's dawn verdict for the coming night — what the raid banner shows a player.</summary>
    public bool RaidTonight;
    /// <summary>
    /// Raiders alive but nothing has happened for <see cref="RaidDirector.LurkSeconds"/>
    /// (2026-09-10), AND they are this colony's problem: the player's always, a
    /// rival's only when a raider stands inside its own home radius. A rival that
    /// went Offensive on raiders lurking at the PLAYER's fire would march its
    /// militia across the island.
    /// </summary>
    public bool RaidLurking;
    /// <summary>
    /// Tonight's committed raid when <see cref="RaidTonight"/>, else what a roll
    /// tomorrow would land against the colony as it stands (2026-09-10). The
    /// number every strategy sizes its army against. The director rolls ONE raid
    /// off the player's prosperity; a rival hears the same drums.
    /// </summary>
    public int NextRaidSize;
    /// <summary>The clock is past dusk. A landing party sails by day (2026-09-16).</summary>
    public bool Night;
    public int Workers;
    public int Warriors;
    public int Enemies;
    public float Wood, Food, Stone;
    /// <summary>Everyone on the roster (idle, working, crafting, soldiering) — the mouths to feed.</summary>
    public int Colonists;
    /// <summary>Population.HungerState as an int: 0 fed, 1 hungry, 2 starving (2026-09-04).</summary>
    public int Hunger;

    /// <summary>This second's picture of <paramref name="faction"/>'s colony.</summary>
    public static ColonyState Capture(Faction faction, DayNightCycle clock)
    {
        BaseBuilding fire = faction.Campfire;
        ResourcePool rm = faction.Resources;
        Population pop = faction.Population;
        int day = clock != null ? clock.GetCurrentDay() : 1;
        RaidDirector rd = RaidDirector.Instance;

        return new ColonyState
        {
            Faction = faction,
            Campfire = fire,
            Day = day,
            RaidTonight = rd != null && rd.RaidTonight && rd.Target == faction,   // the roll names a shore (2026-09-16)
            RaidLurking = rd != null && rd.RaidLurking && (faction.IsPlayer || RaiderInHomeRadius(fire)),
            NextRaidSize = RaidSizeAhead(day, faction),
            Night = clock != null && clock.IsNightTime(),
            Workers = fire != null ? fire.GetTotalWorkers() : 0,
            Warriors = fire != null ? fire.GetWarriorCount() : 0,
            Enemies = Enemy.ActiveList.Count,
            Wood = rm.wood,
            Food = rm.food,
            Stone = rm.stone,
            Colonists = pop != null ? pop.GetColonistCount() : 0,
            Hunger = pop != null ? (int)pop.Hunger : 0,
        };
    }

    /// <summary>
    /// The raid the policy should be standing ready for (2026-09-10): tonight's
    /// committed size when the dawn roll said raiders land, else what a roll
    /// tomorrow would land against the colony as it stands. A player reads the
    /// same two things off the banner and the day counter.
    /// </summary>
    public static int RaidSizeAhead(int day, Faction faction)
    {
        RaidDirector rd = RaidDirector.Instance;
        if (rd == null) return 0;
        return rd.RaidTonight && rd.Target == faction ? rd.PlannedSize : rd.EstimateRaidSize(day + 1, faction);
    }

    /// <summary>A raider inside <see cref="Territory.HomeRadius"/> of <paramref name="fire"/>.</summary>
    public static bool RaiderInHomeRadius(BaseBuilding fire)
    {
        if (fire == null) return false;
        Vector3 at = fire.transform.position;
        float sqr = Territory.HomeRadius * Territory.HomeRadius;
        var list = Enemy.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Enemy e = list[i];
            if (e == null) continue;
            Vector3 d = e.transform.position - at;
            d.y = 0f;
            if (d.sqrMagnitude <= sqr) return true;
        }
        return false;
    }
}

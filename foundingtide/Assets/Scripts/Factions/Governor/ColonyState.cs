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
    /// <summary>Full-time warriors only (<see cref="BaseBuilding.GetWarriorCount"/>). A mustered levy is <see cref="Mustered"/>.</summary>
    public int Warriors;
    /// <summary>Weapons in the stockpile right now — what the next alarm can put in colonists' hands (2026-09-16, the levy).</summary>
    public int SpareWeapons;
    /// <summary>Colonists standing in a warrior body under the alarm right now.</summary>
    public int Mustered;
    public int Enemies;
    public float Wood, Food, Stone;
    /// <summary>Everyone on the roster (idle, working, crafting, soldiering) — the mouths to feed.</summary>
    public int Colonists;
    /// <summary>Population.HungerState as an int: 0 fed, 1 hungry, 2 starving (2026-09-04).</summary>
    public int Hunger;

    // ---- the forecast (2026-10-19, governor slice 2) ------------------------
    // Timing comes off the clock in Capture; the rates are a ColonyForecast the
    // Governor owns and copies in (Governor.Tick), because a snapshot remembers
    // nothing. Zero/false on a state nobody's governor has seen, which is what
    // SimPlayerDriver gets: it reads none of them.

    /// <summary>Game seconds in one calendar day (<see cref="DayNightCycle.CycleSeconds"/>), the unit of every "per day" here.</summary>
    public float CycleSeconds;
    /// <summary>Game seconds until tonight's dusk (<see cref="DayNightCycle.DuskT"/>); 0 from dusk until dawn.</summary>
    public float DuskIn;
    /// <summary>Tonight's raid lands on this shore and the dusk telegraph has gone out: <see cref="LandingPoint"/> is where. Known from dusk, as the player's banner is.</summary>
    public bool LandingKnown;
    public Vector3 LandingPoint;
    /// <summary>What the roster eats a calendar day at the active difficulty.</summary>
    public float FoodDrain;
    /// <summary>The income rates below have half a cycle behind them.</summary>
    public bool ForecastWarm;
    /// <summary>Gross deposits a calendar day, half-cycle window (<see cref="ColonyForecast"/>).</summary>
    public float WoodIncome, FoodIncome, StoneIncome, MetalIncome;
    /// <summary>Food one food worker brings home a day, measured over the last cycle; <see cref="GovernorPolicy.ForagerYieldPerDay"/> until it is.</summary>
    public float ForagerYield;
    /// <summary>Days of food left at the last cycle's income; <see cref="ColonyForecast.RunwayFed"/> when the colony feeds itself or before a cycle is in.</summary>
    public float FoodRunwayDays;

    /// <summary>
    /// What the levy can field (2026-09-16): the mustered plus every spare weapon
    /// that has a colonist to hold it. A rack of eight spears in a colony of four
    /// arms four; the rest is stock. Bodies are the roster minus the full-time
    /// warriors and the already-mustered.
    /// </summary>
    public int Levy => Mustered + Mathf.Max(0, Mathf.Min(SpareWeapons, Colonists - Warriors - Mustered));

    /// <summary>Full-time warriors plus the levy: what stands at the fire when the alarm goes up.</summary>
    public int Strength => Warriors + Levy;

    /// <summary>This second's picture of <paramref name="faction"/>'s colony.</summary>
    public static ColonyState Capture(Faction faction, DayNightCycle clock)
    {
        BaseBuilding fire = faction.Campfire;
        ResourcePool rm = faction.Resources;
        Population pop = faction.Population;
        int day = clock != null ? clock.GetCurrentDay() : 1;
        RaidDirector rd = RaidDirector.Instance;
        int colonists = pop != null ? pop.GetColonistCount() : 0;
        float duskIn = SecondsToDusk(clock);
        Vector3 landing = Vector3.zero;
        bool landingKnown = duskIn <= 0f && rd != null && rd.LandingFor(faction, out landing);

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
            SpareWeapons = faction.Militia.SpareWeapons,
            Mustered = faction.Militia.Mustered,
            Enemies = Enemy.ActiveList.Count,
            Wood = rm.wood,
            Food = rm.food,
            Stone = rm.stone,
            Colonists = colonists,
            Hunger = pop != null ? (int)pop.Hunger : 0,
            CycleSeconds = clock != null ? clock.CycleSeconds : 225f,
            DuskIn = duskIn,
            LandingKnown = landingKnown,
            LandingPoint = landing,
            // Population.DailyDrain's formula off the same roster count.
            FoodDrain = colonists * PopulationManager.Settings.FoodPerDay * Difficulty.FoodConsumptionMultiplier,
            FoodRunwayDays = ColonyForecast.RunwayFed,
            ForagerYield = GovernorPolicy.ForagerYieldPerDay,
        };
    }

    /// <summary>
    /// Game seconds until the clock reaches <see cref="DayNightCycle.DuskT"/>, 0
    /// from dusk until dawn (the evening and the night are one stretch to a
    /// governor: the line forms, the levy musters). Day and night run at their
    /// own rates, so the day's remaining parameter converts at the day's.
    /// </summary>
    public static float SecondsToDusk(DayNightCycle clock)
    {
        if (clock == null) return 0f;
        float t = clock.GetTimeOfDay();
        if (t < 0.25f || t >= DayNightCycle.DuskT) return 0f;
        return (DayNightCycle.DuskT - t) / 0.5f * clock.dayLengthInSeconds;
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

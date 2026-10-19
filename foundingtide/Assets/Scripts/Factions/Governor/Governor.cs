using UnityEngine;

/// <summary>
/// One colony's decision-maker (2026-09-16, lap step 3 slice B): a
/// <see cref="GovernorPolicy"/> bound to a faction and its own
/// <see cref="FactionBuilder"/>, ticked once a game-second. A rival's is owned
/// by <see cref="GovernorRunner"/>; the sim's simulated player is one of these
/// too, owned and ticked by <c>SimRunner</c>, which is what makes a rival and
/// the harness the same code.
/// </summary>
public sealed class Governor
{
    public readonly Faction Faction;
    public readonly GovernorPolicy Policy;
    public readonly FactionBuilder Builder;

    /// <summary>The picture the last tick decided on; the overlay and the F3 line read it.</summary>
    public ColonyState LastState { get; private set; }

    public Governor(Faction faction, GovernorPolicy policy)
    {
        Faction = faction;
        Policy = policy;
        Builder = new FactionBuilder(faction);
        policy.Bind(faction, Builder);
    }

    /// <summary>Capture this second's state and let the policy act on it.</summary>
    public void Tick(DayNightCycle clock)
    {
        Tick(ColonyState.Capture(Faction, clock));
    }

    /// <summary>The rates this colony's governor remembers between ticks (2026-10-19, slice 2).</summary>
    public readonly ColonyForecast Forecast = new ColonyForecast();

    /// <summary>Act on a state the caller already captured (the sim shares it with the player driver).</summary>
    public void Tick(ColonyState s)
    {
        ApplyForecast(ref s);
        LastState = s;
        if (Faction.Campfire == null) return;   // the colony has fallen; nothing to govern
        Policy.Tick(s);
    }

    /// <summary>Sample the forecast and copy its numbers into this tick's state.</summary>
    void ApplyForecast(ref ColonyState s)
    {
        float cycle = s.CycleSeconds;
        Forecast.Sample(Faction, s.Campfire != null ? s.Campfire.foodWorkers : 0, cycle);
        s.ForecastWarm = Forecast.IncomeWarm(cycle);
        s.WoodIncome = Forecast.IncomePerDay(ResourceNode.ResourceType.Wood, cycle);
        s.FoodIncome = Forecast.IncomePerDay(ResourceNode.ResourceType.Food, cycle);
        s.StoneIncome = Forecast.IncomePerDay(ResourceNode.ResourceType.Stone, cycle);
        s.MetalIncome = Forecast.IncomePerDay(ResourceNode.ResourceType.Metal, cycle);
        s.ForagerYield = Forecast.ForagerYield(cycle, GovernorPolicy.ForagerYieldPerDay);
        s.FoodRunwayDays = Forecast.FoodRunwayDays(s.Food, s.FoodDrain, cycle);
    }
}

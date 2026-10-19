using UnityEngine;

/// <summary>
/// What a governor remembers between ticks (2026-10-19, governor slice 2): how
/// fast each resource is coming in, and what one forager brings home a day.
/// <see cref="ColonyState"/> is a snapshot rebuilt every second and can hold no
/// rate, so each <see cref="Governor"/> owns one of these, samples it once a
/// tick and copies its numbers into the state the policy reads.
///
/// Income is GROSS (what was deposited, <see cref="ResourcePool.Gained"/>), not
/// the change in the balance, because the governor's own spending would read as
/// a collapse in income the second it placed a wall. Two windows:
/// <list type="bullet">
/// <item>Half a cycle for the four income rates (the caption, days.csv): quick
/// enough to show that income stopped within the day, slow enough that one stack
/// of deliveries is not a boom. It reads high by day and dips over the night's
/// sleep, by design.</item>
/// <item>A full cycle for the food sums the policy acts on (the forager yield
/// and the food runway). Both are per calendar day, night included, and
/// <see cref="GovernorPolicy.KeepForagers"/> only ever ADDS foragers, so a
/// half-day reading taken at dawn would ratchet the food workers up every
/// morning.</item>
/// </list>
/// Nothing here draws from any random stream, and nothing reads the fog: a
/// player sees the stockpile counters and their own workers too.
/// </summary>
public sealed class ColonyForecast
{
    /// <summary>A forager yield outside this range is noise (a salvage crate, the castaway's armful, a raid night).</summary>
    public const float MinForagerYield = 20f, MaxForagerYield = 60f;
    /// <summary>Forager-time the day window must have seen before its yield is believed.</summary>
    const float MinForagers = 0.5f;
    /// <summary>The runway when the colony feeds itself, or before the forecast knows (<see cref="Population.FoodReserveDays"/>'s own sentinel).</summary>
    public const float RunwayFed = 999f;

    const int Types = 4;

    readonly float[] rate = new float[Types];   // per game-second, half-cycle window
    readonly int[] lastGained = new int[Types];
    float foodRateDay;                          // per game-second, full-cycle window
    float foragersDay;                          // food workers, full-cycle window
    float lastTime = -1f;
    float age;                                  // game seconds sampled

    /// <summary>Fold the seconds since the last sample into the rates.</summary>
    public void Sample(Faction faction, int foodWorkers, float cycleSeconds)
    {
        ResourcePool pool = faction.Resources;
        float now = Time.time;
        if (lastTime < 0f || pool == null)
        {
            lastTime = now;
            if (pool != null)
                for (int i = 0; i < Types; i++) lastGained[i] = pool.Gained((ResourceNode.ResourceType)i);
            foragersDay = foodWorkers;
            return;
        }

        float dt = now - lastTime;
        if (dt <= 0f) return;
        lastTime = now;
        age += dt;

        float cycle = Mathf.Max(cycleSeconds, 1f);
        // Exact for an irregular step: the sim ticks on a 1 s timer, a rival on its own offset.
        float aHalf = 1f - Mathf.Exp(-dt / (cycle * 0.5f));
        float aDay = 1f - Mathf.Exp(-dt / cycle);

        for (int i = 0; i < Types; i++)
        {
            int g = pool.Gained((ResourceNode.ResourceType)i);
            float inst = (g - lastGained[i]) / dt;
            lastGained[i] = g;
            rate[i] += aHalf * (inst - rate[i]);
            if (i == (int)ResourceNode.ResourceType.Food) foodRateDay += aDay * (inst - foodRateDay);
        }
        foragersDay += aDay * (foodWorkers - foragersDay);
    }

    /// <summary>A half cycle sampled: the income rates mean something.</summary>
    public bool IncomeWarm(float cycleSeconds) => age >= cycleSeconds * 0.5f;

    /// <summary>A whole cycle sampled, night included: the food sums mean something.</summary>
    public bool DayWarm(float cycleSeconds) => age >= cycleSeconds;

    /// <summary>Deposits of <paramref name="type"/> a calendar day at the half-cycle rate.</summary>
    public float IncomePerDay(ResourceNode.ResourceType type, float cycleSeconds) => rate[(int)type] * cycleSeconds;

    /// <summary>Food deposited a calendar day over the last whole cycle.</summary>
    public float FoodPerDay(float cycleSeconds) => foodRateDay * cycleSeconds;

    /// <summary>
    /// Food one food worker brought home a calendar day over the last cycle,
    /// clamped to <see cref="MinForagerYield"/>..<see cref="MaxForagerYield"/>;
    /// <paramref name="fallback"/> until a whole cycle with foragers is in.
    /// </summary>
    public float ForagerYield(float cycleSeconds, float fallback)
    {
        if (!DayWarm(cycleSeconds) || foragersDay < MinForagers) return fallback;
        return Mathf.Clamp(FoodPerDay(cycleSeconds) / foragersDay, MinForagerYield, MaxForagerYield);
    }

    /// <summary>
    /// Days until the bank is empty at the last cycle's food income against
    /// <paramref name="drainPerDay"/>; <see cref="RunwayFed"/> when the income
    /// covers the eating, or before a whole cycle is in.
    /// </summary>
    public float FoodRunwayDays(float bank, float drainPerDay, float cycleSeconds)
    {
        if (!DayWarm(cycleSeconds)) return RunwayFed;
        float gap = drainPerDay - FoodPerDay(cycleSeconds);
        if (gap <= 0.01f) return RunwayFed;
        return Mathf.Min(RunwayFed, Mathf.Max(0f, bank) / gap);
    }
}

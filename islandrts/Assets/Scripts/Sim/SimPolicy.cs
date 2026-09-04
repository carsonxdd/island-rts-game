#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// The simulated player. Everything a human decides in a run — worker
/// assignment, what to build, when to recruit — and nothing else. Movement,
/// combat, gathering and pathing are still the game's own Utility AI, which is
/// exactly why a run of this is worth reading as balance data.
///
/// A policy is polled once a second (<see cref="Tick"/>). It should take at most
/// one action per tick, so the resource curve stays legible rather than the
/// whole bank emptying in one frame.
/// </summary>
public abstract class SimPolicy
{
    public abstract string Name { get; }

    /// <summary>Called once a game-second while the colony is alive.</summary>
    public abstract void Tick(SimState s);

    public static SimPolicy Create(string name)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "turtle": return new TurtlePolicy();
            case "rush": return new RushPolicy();
            case "eco":
            default: return new EcoPolicy();
        }
    }

    // ---- shared moves -----------------------------------------------------

    /// <summary>Assigns one worker to whichever type the ratio is shortest on.</summary>
    protected static bool HireWorker(SimState s, float woodShare, float foodShare, float stoneShare)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;

        // Colonists are a pool (2026-09-02): a job needs an idle colonist, and one is
        // held back as a builder while anything is under construction — sites no
        // longer finish on their own, so a policy that assigns everyone stalls.
        PopulationManager pm = PopulationManager.Instance;
        if (pm == null) return false;
        int idle = pm.GetIdleCount();
        if (idle <= 0) return false;
        if (ConstructionSite.ActiveList.Count > 0 && idle <= 1) return false;

        int total = fire.GetTotalWorkers();
        if (total >= fire.maxWorkers) return false;

        // Largest deficit against target share wins. Starts everyone on wood,
        // which is what a player does before the ratio means anything.
        float sum = woodShare + foodShare + stoneShare;
        if (sum <= 0f) return false;

        float woodDef = woodShare / sum * (total + 1) - fire.woodWorkers;
        float foodDef = foodShare / sum * (total + 1) - fire.foodWorkers;
        float stoneDef = stoneShare / sum * (total + 1) - fire.stoneWorkers;

        ResourceNode.ResourceType pick = ResourceNode.ResourceType.Wood;
        float best = woodDef;
        if (foodDef > best) { best = foodDef; pick = ResourceNode.ResourceType.Food; }
        if (stoneDef > best) { pick = ResourceNode.ResourceType.Stone; }

        int before = fire.GetTotalWorkers();
        fire.AssignWorker(pick);
        return fire.GetTotalWorkers() > before;
    }

    protected static bool Recruit(SimState s)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;
        if (fire.GetWarriorCount() >= fire.maxWarriors) return false;
        if (ResourceManager.Instance == null) return false;
        if (!ResourceManager.Instance.CanAfford(fire.warriorCost_Wood, fire.warriorCost_Food, 0)) return false;

        int before = fire.GetWarriorCount();
        fire.SpawnWarrior();
        return fire.GetWarriorCount() > before;
    }

    /// <summary>Build a hut when housing is the thing capping the colony.</summary>
    protected static bool BuildHutIfCapped(SimState s, int maxHuts)
    {
        if (SimBuilder.HutCount + SimBuilder.PendingSites(BuildingType.Hut) >= maxHuts) return false;
        if (PopulationManager.Instance != null
            && PopulationManager.Instance.GetAvailableHousing() > 0
            && SimBuilder.PendingSites(BuildingType.Hut) > 0) return false;
        return SimBuilder.PlaceBuilding(BuildingType.Hut, 7f, 16f);
    }
}

/// <summary>
/// Walls first. Tests whether fortification is a viable substitute for army —
/// and whether wall HP vs enemy DPS is in the right neighbourhood.
/// </summary>
public class TurtlePolicy : SimPolicy
{
    public override string Name => "Turtle";

    /// <summary>Wood never spent on walls, so the colony can still fund warriors and repairs.</summary>
    private const float WoodReserve = 120f;

    private bool ringOrdered;
    private bool gatesCut;

    public override void Tick(SimState s)
    {
        // Enough economy to pay for a wall, then wall, then a token guard.
        if (s.Workers < 4) { if (HireWorker(s, 2f, 1f, 2f)) return; }
        if (BuildHutIfCapped(s, 3)) return;

        // Build the ring in segments, keeping a wood reserve. Committing the
        // whole bank to 48 wall sites at once is what made this policy lose
        // every run at 0 wood / 400 stone: it could no longer fund warriors,
        // huts, or replacements for anything it lost.
        if (!ringOrdered && s.Wood >= WoodReserve + 90f && s.Stone >= 40)
        {
            if (SimBuilder.PlaceWallRing(BuildingType.WoodenWall, 9, 6) > 0) return;
            ringOrdered = true;   // ring is complete or fully blocked
        }

        if (ringOrdered && !gatesCut && SimBuilder.WallCount >= 20)
        {
            gatesCut = SimBuilder.ConvertGates(2) > 0;
            if (gatesCut) return;
        }

        if (s.Workers < 8) { if (HireWorker(s, 2f, 1f, 2f)) return; }

        // A small garrison from the start — walls without anyone behind them
        // just delay the wave. Gating this on a finished ring meant Turtle
        // faced night 1 and 2 with nothing but construction sites.
        if (s.Warriors < 1 + s.Day / 2 || (s.RaidTonight && s.Warriors < 2 + s.Day / 2)) { if (Recruit(s)) return; }

        // Late: a tower for reach, then top the ring back up as it gets chewed.
        if (s.Day >= 3 && SimBuilder.TowerCount == 0 && s.Stone >= 80)
        {
            if (SimBuilder.PlaceBuilding(BuildingType.Watchtower, 6f, 12f)) return;
        }
        if (ringOrdered && s.Wood >= 150) SimBuilder.PlaceWallRing(BuildingType.WoodenWall, 9, 6);
    }
}

/// <summary>
/// Army first. Tests whether the warrior cost/DPS curve keeps pace with raids
/// that grow with the day number and the colony's prosperity, with almost no
/// economy behind it.
/// </summary>
public class RushPolicy : SimPolicy
{
    public override string Name => "Rush";

    public override void Tick(SimState s)
    {
        // Minimum viable economy, then everything into warriors.
        if (s.Workers < 3) { if (HireWorker(s, 2f, 2f, 0f)) return; }
        if (BuildHutIfCapped(s, 2)) return;

        if (Recruit(s)) return;

        if (s.Workers < 5) { if (HireWorker(s, 2f, 2f, 0f)) return; }
    }
}

/// <summary>
/// Economy first, defence bought late out of the surplus. The baseline every
/// other strategy is read against.
/// </summary>
public class EcoPolicy : SimPolicy
{
    public override string Name => "Eco";

    public override void Tick(SimState s)
    {
        if (BuildHutIfCapped(s, 6)) return;
        if (s.Workers < 10) { if (HireWorker(s, 3f, 2f, 1f)) return; }

        // Scale the garrison with the threat, not to a fixed 5. The old
        // Mathf.Min(s.Day, 5) silently mirrored the shipping maxWarriors cap,
        // which would make any test of raising that cap meaningless — the
        // policy would never ask for the extra warriors.
        int wanted = 1 + s.Day;
        // The dawn roll is public knowledge — a human who sees "raiders land
        // tonight" spends the reserve on warriors, so the policy does too.
        bool spend = s.RaidTonight || (s.Wood > 60 && s.Food > 60);
        if (s.Warriors < wanted && spend)
        {
            if (Recruit(s)) return;
        }

        if (s.Day >= 3 && SimBuilder.TowerCount == 0 && s.Stone >= 100)
        {
            if (SimBuilder.PlaceBuilding(BuildingType.Watchtower, 6f, 12f)) return;
        }
        if (s.Day >= 4 && s.Wood >= 250 && s.Stone >= 150)
        {
            SimBuilder.PlaceWallRing(BuildingType.WoodenWall, 8, 12);
        }
    }
}

/// <summary>Read-only snapshot handed to a policy each tick (no allocation).</summary>
public struct SimState
{
    public BaseBuilding Campfire;
    public int Day;
    /// <summary>The director's dawn verdict for the coming night — what the raid banner shows a player.</summary>
    public bool RaidTonight;
    public int Workers;
    public int Warriors;
    public int Enemies;
    public float Wood, Food, Stone;
}
#endif

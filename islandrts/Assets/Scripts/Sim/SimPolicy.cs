#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// The simulated player. Everything a human decides in a run — what to research,
/// what to queue at the fire, worker assignment, what to build, when to recruit —
/// and nothing else. Movement, combat, gathering and pathing are still the game's
/// own Utility AI, which is exactly why a run of this is worth reading as balance
/// data. The character's legs (fetching materials, standing at the bench) are
/// <see cref="SimPlayerDriver"/>.
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

    /// <summary>
    /// Queue the first of <paramref name="ids"/> that is neither done nor queued,
    /// at a bench that lists it — the campfire for its own tier, a Workshop for
    /// the upgrades (2026-09-04). One entry at a time per bench keeps the
    /// character's material runs short. A Workshop entry is only queued once a
    /// Crafter is on the roster: the simulated character works the fire alone
    /// (<see cref="SimPlayerDriver"/>), so nobody else would stand at it.
    /// True when something was queued this tick.
    /// </summary>
    protected static bool Research(SimState s, params string[] ids)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null || fire.Station == null) return false;

        for (int i = 0; i < ids.Length; i++)
        {
            ResearchCatalog.ResearchDef d = ResearchCatalog.Find(ids[i]);
            if (d == null || Factions.Player.Knowledge.IsDone(d)) continue;
            if (!Factions.Player.Knowledge.IsAvailable(d)) continue;   // a prerequisite is still ahead in the list
            if (CraftStation.IsQueuedAnywhere(d)) return false;

            CraftStation bench = StationListing(d);
            if (bench == null) continue;                      // no Workshop yet — try the next id
            if (bench != fire.Station && fire.crafterWorkers == 0) continue;
            if (bench.HasWork) return false;                  // one thing at a time
            return bench.Enqueue(d);
        }
        return false;
    }

    /// <summary>The nearest-to-the-fire living bench that lists <paramref name="d"/>, or null.</summary>
    static CraftStation StationListing(ResearchCatalog.ResearchDef d)
    {
        var list = CraftStation.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            CraftStation st = list[i];
            if (st != null && st.IsAlive && st.Lists(d)) return st;
        }
        return null;
    }

    /// <summary>
    /// Keep weapons in stock + queued at <paramref name="wanted"/>. Needs Spearcraft.
    /// Iron Spears once Iron Work is known and the metal is there (2026-09-04);
    /// after Bowyery every third weapon is a Bow so the garrison gets archers —
    /// queued at the fire like everything else; a Crafter or the character works it.
    /// </summary>
    protected static bool KeepSpears(SimState s, int wanted)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null || fire.Station == null) return false;
        if (!Factions.Player.Knowledge.Has(Unlocks.Kind.Militia)) return false;

        CraftingCatalog.Recipe wooden = CraftingCatalog.Find("wooden_spear");
        CraftingCatalog.Recipe iron = CraftingCatalog.Find("iron_spear");
        CraftingCatalog.Recipe bow = CraftingCatalog.Find("bow");
        bool ironOk = iron != null && iron.UnlockedFor(Factions.Player.Knowledge)
                      && Factions.Player.Resources.metal >= iron.metalCost;
        CraftingCatalog.Recipe spear = ironOk ? iron : wooden;

        int have = fire.WeaponsInStock() + fire.Station.Queued(wooden) + fire.Station.Queued(iron)
                   + (bow != null ? fire.Station.Queued(bow) : 0);
        if (have >= wanted) return false;

        // One bow in three once the colony can make them (a third of the garrison shoots)
        bool bowOk = bow != null && bow.UnlockedFor(Factions.Player.Knowledge) && (s.Warriors + have) % 3 == 2;
        return fire.Station.Enqueue(bowOk ? bow : spear, 1);
    }

    /// <summary>
    /// Point the recruit picker at a bow when the stock has one and the garrison is
    /// short of archers (one in three), else at the best spear (2026-09-04).
    /// </summary>
    protected static void PickRecruitWeapon(SimState s)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return;

        int archers = 0;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].IsRanged) archers++;

        bool wantBow = fire.Stockpile.Count(ItemCatalog.Bow) > 0 && archers * 3 < s.Warriors + 1;
        ItemDef target = wantBow ? ItemCatalog.Bow : fire.FirstWeaponInStock();
        if (target == null) return;
        for (int guard = 0; guard < ItemCatalog.Weapons.Length && fire.SelectedWeapon != target; guard++)
            fire.CycleWeapon(1);
    }

    /// <summary>
    /// The Workshop and a Crafter to run it (2026-09-04): place the building once
    /// Crafting is known and there is wood to spare, then give the bench a colonist
    /// once it stands and the idle pool can afford one. One action per call.
    /// </summary>
    protected static bool RunWorkshop(SimState s)
    {
        if (!Factions.Player.Knowledge.Has(Unlocks.Kind.Crafting) || !CanBuild) return false;
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;

        if (Workshop.ActiveList.Count == 0)
        {
            if (SimBuilder.PendingSites(BuildingType.Workshop) > 0) return false;
            if (s.Wood < 80f) return false;
            return SimBuilder.PlaceBuilding(BuildingType.Workshop, 6f, 14f);
        }

        if (fire.crafterWorkers > 0) return false;
        Population pm = Factions.Player.Population;
        if (pm == null || pm.GetIdleCount() < 2) return false;   // keep a builder
        return fire.AssignSpecialist(Worker.Specialty.Crafter);
    }

    /// <summary>Assigns one worker to whichever type the ratio is shortest on, among the jobs the colony knows.</summary>
    protected static bool HireWorker(SimState s, float woodShare, float foodShare, float stoneShare)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;

        // Colonists are a pool (2026-09-02): a job needs an idle colonist, and one is
        // held back as a builder while anything is under construction — sites no
        // longer finish on their own, so a policy that assigns everyone stalls.
        Population pm = Factions.Player.Population;
        if (pm == null) return false;
        int idle = pm.GetIdleCount();
        if (idle <= 0) return false;
        if (ConstructionSite.ActiveList.Count > 0 && idle <= 1) return false;

        int total = fire.GetTotalWorkers();
        if (total >= fire.maxWorkers) return false;

        // The colony eats (2026-09-04): whatever the ratio says, keep one forager
        // per eight mouths so a strategy that ignores food starves on schedule
        // rather than by accident.
        if (Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Food)
            && fire.foodWorkers < Mathf.CeilToInt(s.Colonists / 8f))
        {
            return fire.AssignWorker(ResourceNode.ResourceType.Food);
        }

        // A job the colony has not researched yet scores nothing (2026-09-03)
        if (!Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Wood)) woodShare = 0f;
        if (!Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Food)) foodShare = 0f;
        if (!Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Stone)) stoneShare = 0f;

        // Largest deficit against target share wins. Starts everyone on wood,
        // which is what a player does before the ratio means anything.
        float sum = woodShare + foodShare + stoneShare;
        if (sum <= 0f) return false;

        float woodDef = woodShare > 0f ? woodShare / sum * (total + 1) - fire.woodWorkers : float.NegativeInfinity;
        float foodDef = foodShare > 0f ? foodShare / sum * (total + 1) - fire.foodWorkers : float.NegativeInfinity;
        float stoneDef = stoneShare > 0f ? stoneShare / sum * (total + 1) - fire.stoneWorkers : float.NegativeInfinity;

        ResourceNode.ResourceType pick = ResourceNode.ResourceType.Wood;
        float best = woodDef;
        if (foodDef > best) { best = foodDef; pick = ResourceNode.ResourceType.Food; }
        if (stoneDef > best) { pick = ResourceNode.ResourceType.Stone; }

        int before = fire.GetTotalWorkers();
        fire.AssignWorker(pick);
        return fire.GetTotalWorkers() > before;
    }

    /// <summary>Arm an idle colonist with a spear from the stockpile (the campfire checks food, cap and research).</summary>
    protected static bool Recruit(SimState s)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;
        PickRecruitWeapon(s);
        if (!fire.CanRecruitWarrior()) return false;

        int before = fire.GetWarriorCount();
        fire.SpawnWarrior();
        return fire.GetWarriorCount() > before;
    }

    /// <summary>Build a hut when housing is the thing capping the colony.</summary>
    protected static bool BuildHutIfCapped(SimState s, int maxHuts)
    {
        if (!Factions.Player.Knowledge.Has(Unlocks.Kind.Construction)) return false;
        if (SimBuilder.HutCount + SimBuilder.PendingSites(BuildingType.Hut) >= maxHuts) return false;
        if (Factions.Player.Population != null
            && Factions.Player.Population.GetAvailableHousing() > 0
            && SimBuilder.PendingSites(BuildingType.Hut) > 0) return false;
        return SimBuilder.PlaceBuilding(BuildingType.Hut, 7f, 16f);
    }

    /// <summary>Construction research gates every placement.</summary>
    protected static bool CanBuild => Factions.Player.Knowledge.Has(Unlocks.Kind.Construction);
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
        // The tech a turtle needs, in order: wood, stone, walls, a token guard, food
        // Foraging before Spearcraft since the colony eats (2026-09-04)
        if (Research(s, "woodcutting", "foraging", "quarrying", "construction", "spearcraft")) return;

        // Enough economy to pay for a wall, then wall, then a token guard.
        if (s.Workers < 4) { if (HireWorker(s, 2f, 1f, 2f)) return; }
        if (BuildHutIfCapped(s, 3)) return;

        // Build the ring in segments, keeping a wood reserve. Committing the
        // whole bank to 48 wall sites at once is what made this policy lose
        // every run at 0 wood / 400 stone: it could no longer fund warriors,
        // huts, or replacements for anything it lost.
        if (CanBuild && !ringOrdered && s.Wood >= WoodReserve + 90f && s.Stone >= 40)
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
        // just delay the wave. Spears first (the fire makes them), then people.
        int wantedWarriors = 1 + s.Day / 2 + (s.RaidTonight ? 1 : 0);
        if (KeepSpears(s, Mathf.Max(0, wantedWarriors - s.Warriors))) return;
        if (s.Warriors < wantedWarriors) { if (Recruit(s)) return; }

        // Late: a tower for reach, then top the ring back up as it gets chewed.
        if (CanBuild && s.Day >= 3 && SimBuilder.TowerCount == 0 && s.Stone >= 80)
        {
            if (SimBuilder.PlaceBuilding(BuildingType.Watchtower, 6f, 12f)) return;
        }
        if (CanBuild && ringOrdered && s.Wood >= 150) SimBuilder.PlaceWallRing(BuildingType.WoodenWall, 9, 6);
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
        // Foraging before Spearcraft since the colony eats (2026-09-04)
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction")) return;

        // Minimum viable economy, then everything into warriors.
        if (s.Workers < 3) { if (HireWorker(s, 2f, 2f, 0f)) return; }
        if (BuildHutIfCapped(s, 2)) return;

        // A spear per would-be warrior, always one ahead
        if (KeepSpears(s, 1 + (s.RaidTonight ? 1 : 0))) return;
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
        if (Research(s, "woodcutting", "foraging", "construction", "quarrying", "spearcraft",
                        "crafting", "mining", "iron_work", "bowyery")) return;

        if (BuildHutIfCapped(s, 6)) return;
        if (RunWorkshop(s)) return;
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
            if (KeepSpears(s, Mathf.Min(2, wanted - s.Warriors))) return;
            if (Recruit(s)) return;
        }

        if (CanBuild && s.Day >= 3 && SimBuilder.TowerCount == 0 && s.Stone >= 100)
        {
            if (SimBuilder.PlaceBuilding(BuildingType.Watchtower, 6f, 12f)) return;
        }
        if (CanBuild && s.Day >= 4 && s.Wood >= 250 && s.Stone >= 150)
        {
            if (SimBuilder.PlaceWallRing(BuildingType.WoodenWall, 8, 12) > 0) return;
        }

        // The escape (2026-09-04, Slice 6): from day 12 research Shipwright, build the
        // Shipyard on the beach when the bank covers it, and sail the moment it stands.
        // A run that ends "escape" is the metric: how early can Eco leave?
        if (s.Day >= 12)
        {
            if (Research(s, "shipwright")) return;
            if (Factions.Player.Knowledge.Has(Unlocks.Kind.Shipwright) && CanBuild
                && Shipyard.ActiveList.Count == 0 && SimBuilder.PendingSites(BuildingType.Shipyard) == 0)
            {
                if (SimBuilder.PlaceShoreBuilding(BuildingType.Shipyard, 60f)) return;
            }
            if (Shipyard.ActiveList.Count > 0)
            {
                Shipyard yard = TargetingUtil.FindNearestOwned(Shipyard.ActiveList, Vector3.zero, 0f, Factions.Player, out _);
                if (yard != null) { yard.SetSail(); return; }
            }
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
    /// <summary>Everyone on the roster (idle, working, crafting, soldiering) — the mouths to feed.</summary>
    public int Colonists;
    /// <summary>Population.HungerState as an int: 0 fed, 1 hungry, 2 starving (2026-09-04).</summary>
    public int Hunger;
}
#endif

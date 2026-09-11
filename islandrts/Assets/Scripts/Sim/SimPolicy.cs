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

    // ---- what the run is working on (2026-09-10) ----------------------------
    // Read by the visual overlay only. A policy sets Goal at the top of every
    // tick (what it is trying to reach) and every shared move that DOES
    // something stamps Intent (what it just did). Neither is read by anything
    // that decides.

    /// <summary>This second's target: "army 4/6 · beds 1 free · workers 5/8".</summary>
    public static string Goal = "";
    /// <summary>The last action taken and the game-second it was taken on.</summary>
    public static string Intent = "";
    private static float intentTime;

    protected static void Did(string what)
    {
        Intent = what;
        intentTime = Time.time;
    }

    /// <summary>
    /// What a player does when the HUD says "Raiders lurking" (2026-09-10): go
    /// Offensive so the militia hunts them down, and stand down at dawn. Called
    /// at the top of every Tick; takes the tick when it changes the stance.
    /// </summary>
    protected bool ManageStance(SimState s)
    {
        Faction f = Factions.Player;
        if (s.RaidLurking && f.Stance != GuardStance.Mode.Offensive)
        {
            f.SetStance(GuardStance.Mode.Offensive);
            wentOffensive = true;
            Did("stance Offensive: raiders lurking");
            return true;
        }
        if (s.Enemies == 0 && wentOffensive)
        {
            wentOffensive = false;
            if (f.Stance == GuardStance.Mode.Offensive) f.SetStance(GuardStance.Mode.Defensive);
            Did("stance Defensive");
            return true;
        }
        return false;
    }
    private bool wentOffensive;

    /// <summary>The one line every strategy's caption shares.</summary>
    protected static void SetGoal(SimState s, int wantedWarriors, int wantedWorkers, string extra = null)
    {
        Population pop = Factions.Player.Population;
        int beds = pop != null ? pop.GetAvailableHousing() : 0;
        Goal = $"army {s.Warriors}/{wantedWarriors} · workers {s.Workers}/{wantedWorkers} · beds {beds} free"
               + (string.IsNullOrEmpty(extra) ? "" : " · " + extra);
        if (Time.time - intentTime > 30f && Intent.Length > 0 && !Intent.StartsWith("(")) Intent = "(" + Intent + ")";
    }

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
            if (!bench.Enqueue(d)) return false;
            Did("research " + d.title);
            return true;
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
        CraftingCatalog.Recipe pick = bowOk ? bow : spear;
        if (!fire.Station.Enqueue(pick, 1)) return false;
        Did("queue " + pick.title);
        return true;
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
            if (!SimBuilder.PlaceBuilding(BuildingType.Workshop, 6f, 14f)) return false;
            Did("place Workshop");
            return true;
        }

        if (fire.crafterWorkers > 0) return false;
        Population pm = Factions.Player.Population;
        if (pm == null || pm.GetIdleCount() < 2) return false;   // keep a builder
        if (!fire.AssignSpecialist(Worker.Specialty.Crafter)) return false;
        Did("assign Crafter");
        return true;
    }

    /// <summary>
    /// Assigns one worker to whichever type the ratio is shortest on, among the
    /// jobs the colony knows. Metal (2026-09-10) is a fourth share: nobody mined
    /// in the first lab because no policy ever asked, so Iron Work (10 metal)
    /// and Iron Spears never came.
    /// </summary>
    protected static bool HireWorker(SimState s, float woodShare, float foodShare, float stoneShare, float metalShare = 0f)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;

        // Colonists are a pool (2026-09-02): a job needs an idle colonist, and a
        // builder is held back PERMANENTLY, not only while a site stands
        // (2026-09-10). The old "only when ConstructionSite.ActiveList is not
        // empty" guard came too late: every policy employed all three starting
        // colonists before Construction was even researched, and a colony with
        // no jobless colonist can never finish a hut, never gain housing, and so
        // never get a jobless colonist back — every run of the 2026-09-10 lab
        // sweep died on day 4-7 with 3 colonists, 0 buildings and 400+ wood.
        Population pm = Factions.Player.Population;
        if (pm == null) return false;
        if (pm.GetIdleCount() <= BuilderReserve(s)) return false;

        int total = fire.GetTotalWorkers();
        if (total >= fire.maxWorkers) return false;

        // The colony eats (2026-09-04): whatever the ratio says, keep one forager
        // per eight mouths so a strategy that ignores food starves on schedule
        // rather than by accident.
        if (Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Food)
            && fire.foodWorkers < Mathf.CeilToInt(s.Colonists / 8f))
        {
            if (!fire.AssignWorker(ResourceNode.ResourceType.Food)) return false;
            Did("hire forager");
            return true;
        }

        // A spear is 3 sticks and a CHUNK, and a chunk only ever falls off a
        // worked rock (2026-09-11). The overnight batch caught Rush hiring at a
        // stone share of 0 for the whole run: 57 sticks, 2 chunks and 2600 wood
        // on day 13, an army that could not re-arm a single loss, and a dawn
        // weapon count pinned at zero. So one quarryman is held the same way one
        // forager is, whatever the ratio says, once the colony can craft at all.
        if (Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Stone)
            && fire.stoneWorkers < 1)
        {
            if (!fire.AssignWorker(ResourceNode.ResourceType.Stone)) return false;
            Did("hire quarryman");
            return true;
        }

        // A job the colony has not researched yet scores nothing (2026-09-03)
        if (!Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Wood)) woodShare = 0f;
        if (!Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Food)) foodShare = 0f;
        if (!Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Stone)) stoneShare = 0f;
        if (!Factions.Player.Knowledge.HasJob(ResourceNode.ResourceType.Metal)) metalShare = 0f;

        // Largest deficit against target share wins. Starts everyone on wood,
        // which is what a player does before the ratio means anything.
        float sum = woodShare + foodShare + stoneShare + metalShare;
        if (sum <= 0f) return false;

        float woodDef = woodShare > 0f ? woodShare / sum * (total + 1) - fire.woodWorkers : float.NegativeInfinity;
        float foodDef = foodShare > 0f ? foodShare / sum * (total + 1) - fire.foodWorkers : float.NegativeInfinity;
        float stoneDef = stoneShare > 0f ? stoneShare / sum * (total + 1) - fire.stoneWorkers : float.NegativeInfinity;
        float metalDef = metalShare > 0f ? metalShare / sum * (total + 1) - fire.metalWorkers : float.NegativeInfinity;

        ResourceNode.ResourceType pick = ResourceNode.ResourceType.Wood;
        float best = woodDef;
        if (foodDef > best) { best = foodDef; pick = ResourceNode.ResourceType.Food; }
        if (stoneDef > best) { best = stoneDef; pick = ResourceNode.ResourceType.Stone; }
        if (metalDef > best) { pick = ResourceNode.ResourceType.Metal; }

        int before = fire.GetTotalWorkers();
        fire.AssignWorker(pick);
        if (fire.GetTotalWorkers() <= before) return false;
        Did("hire " + pick.ToString().ToLowerInvariant() + " worker");
        return true;
    }

    /// <summary>
    /// Colonists held out of every job so something can always be built
    /// (2026-09-10). One from the first day, two once the colony is past six.
    /// </summary>
    protected static int BuilderReserve(SimState s) => s.Colonists > 6 ? 2 : 1;

    /// <summary>Arm an idle colonist with a spear from the stockpile (the campfire checks food, cap and research).</summary>
    protected static bool Recruit(SimState s)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;

        // A recruit spends the same idle colonist a site needs, so the reserve
        // holds here too — until raiders land tonight, when a player would arm
        // the builder and finish the hut tomorrow.
        Population pop = Factions.Player.Population;
        if (!s.RaidTonight && pop != null && pop.GetIdleCount() <= BuilderReserve(s)) return false;

        PickRecruitWeapon(s);
        if (!fire.CanRecruitWarrior()) return false;

        int before = fire.GetWarriorCount();
        fire.SpawnWarrior();
        if (fire.GetWarriorCount() <= before) return false;
        Did("recruit warrior");
        return true;
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

    // ---- the army (2026-09-10) ---------------------------------------------
    // The 2026-09-10 lab lost all nine runs the same way: nobody sized the
    // militia against the raid that was coming. Rush pinned itself at 8 warriors
    // because two huts were all the beds it ever built, Eco met an 8-raider first
    // raid with two spears because Spearcraft was fifth on its list, Turtle put
    // 65 wall cells in front of four men. A raider is 50 HP at 6.7 DPS, a spearman
    // 75 HP at 20.8, so an army of about two thirds of the raid holds and one of
    // half breaks. Every strategy now reads the next raid's size off the director
    // (the day counter and the size of the colony, which a player sees too) and
    // buys people, beds and spears against it; what differs between them is the
    // ratio and what else the wood goes on. Nobody builds a Watchtower: it is a
    // vision building until the archer-tower path exists.

    /// <summary>
    /// Warriors the strategy wants standing before the next raid lands:
    /// <paramref name="perRaider"/> of the raid's size, never under <paramref name="floor"/>.
    /// </summary>
    protected static int WantedWarriors(SimState s, float perRaider, int floor)
        => Mathf.Max(floor, Mathf.CeilToInt(s.NextRaidSize * perRaider));

    /// <summary>
    /// Workers the strategy wants on jobs: its own floor, or half the standing
    /// army if that is more, never above the campfire's job cap. The second lab
    /// of 2026-09-10 lost Rush to food, not raiders: five workers fed 25 warriors
    /// (a bed each, a meal a day, 15 food per replacement) until the bank hit
    /// single digits on day 24 and the army decayed from 28 to 12 for the
    /// day-30 raid. The economy has to grow with the men it feeds.
    /// </summary>
    protected static int WantedWorkers(SimState s, int floor)
    {
        int cap = s.Campfire != null ? s.Campfire.maxWorkers : 10;
        return Mathf.Clamp(Mathf.Max(floor, Mathf.CeilToInt(s.Warriors * 0.5f)), floor, cap);
    }

    /// <summary>
    /// Beds ahead of need. A warrior keeps the bed they had as a colonist, so the
    /// army grows only as fast as ARRIVALS do, and arrivals need empty beds: a hut
    /// goes up whenever the roster plus the free beds is short of everyone the
    /// strategy wants (workers + warriors + the builder reserve). One site at a
    /// time so the wood curve stays legible; capped so a strategy stays itself.
    /// </summary>
    protected static bool KeepHousing(SimState s, int maxHuts, int wantedColonists)
    {
        if (!CanBuild) return false;
        Population pop = Factions.Player.Population;
        if (pop == null) return false;
        if (SimBuilder.PendingSites(BuildingType.Hut) > 0) return false;
        if (SimBuilder.HutCount >= maxHuts) return false;
        if (s.Colonists + pop.GetAvailableHousing() >= wantedColonists) return false;
        if (!SimBuilder.PlaceBuilding(BuildingType.Hut, 7f, 16f)) return false;
        Did("place hut");
        return true;
    }

    /// <summary>
    /// Spears first (never more than two ahead of the men to hold them), then a
    /// recruit. One action per call.
    /// </summary>
    protected static bool KeepArmy(SimState s, int wanted)
    {
        if (s.Warriors >= wanted) return false;
        if (KeepSpears(s, Mathf.Min(2, wanted - s.Warriors))) return true;
        return Recruit(s);
    }

    /// <summary>Construction research gates every placement.</summary>
    protected static bool CanBuild => Factions.Player.Knowledge.Has(Unlocks.Kind.Construction);
}

/// <summary>
/// Walls first. Tests whether fortification lets a SMALLER army hold — the
/// militia is sized at six tenths of the raid, the ring is meant to make up the
/// rest — and whether wall HP vs enemy DPS is in the right neighbourhood.
/// </summary>
public class TurtlePolicy : SimPolicy
{
    public override string Name => "Turtle";

    /// <summary>Wood never spent on walls, so the colony can still fund warriors and repairs.</summary>
    private const float WoodReserve = 120f;
    private const int MaxHuts = 6;
    private const int WorkerFloor = 8;
    private const int RingHalf = 12;   // 9 until 2026-09-10: huts need room inside, clear of the gate corridors

    private bool ringOrdered;

    public override void Tick(SimState s)
    {
        // Spears before stone (2026-09-10): the first raid lands on day 3-4 and
        // a wall with nobody behind it only delays it. Then the ring's stone,
        // then Bowyery so the men behind the wall can shoot over it.
        if (ManageStance(s)) return;
        SimBuilder.SetRing(RingHalf);   // huts stay off the ring line and out of its gate corridors (2026-09-10)
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction", "quarrying",
                        "crafting", "bowyery")) return;

        // A man per raider, not 0.6 of one (2026-09-11). The 2026-09-11 lab
        // watched this colony hold at 9 warriors from day 6 to day 11 with 43
        // sticks and 17 chunks banked - fourteen spears it never made - because
        // 0.6x of a 14-raider night is 9. The raiders-per-warrior threshold the
        // second lab measured is ~1.0, so a turtle at 0.6 is built to lose. The
        // wall is meant to be the edge on top of parity, not a substitute for it.
        int wantedWarriors = WantedWarriors(s, 1f, 2) + (s.RaidTonight ? 1 : 0);
        int wantedWorkers = WantedWorkers(s, WorkerFloor);
        SetGoal(s, wantedWarriors, wantedWorkers,
            ringOrdered ? $"ring up · gates {SimBuilder.GateCount}/8" : "ring pending");

        // Enough economy to pay for a wall, then wall, then the guard.
        if (s.Workers < 4) { if (HireWorker(s, 2f, 1f, 2f)) return; }
        if (KeepHousing(s, MaxHuts, wantedWorkers + wantedWarriors + BuilderReserve(s))) return;

        // Build the ring in segments, keeping a wood reserve. Committing the
        // whole bank to 48 wall sites at once is what made this policy lose
        // every run at 0 wood / 400 stone: it could no longer fund warriors,
        // huts, or replacements for anything it lost.
        if (CanBuild && !ringOrdered && s.Wood >= WoodReserve + 90f && s.Stone >= 40)
        {
            if (SimBuilder.PlaceWallRing(BuildingType.WoodenWall, RingHalf, 6) > 0) { Did("order wall segment"); return; }
            ringOrdered = true;   // ring is complete or fully blocked
        }

        // The openings get their two gates each as soon as the ring is ordered:
        // a wall site per cell, converted the tick it finishes (2026-09-10).
        if (ringOrdered && SimBuilder.GateOpenings(BuildingType.WoodenWall, RingHalf) > 0) { Did("gate an opening"); return; }

        if (KeepArmy(s, wantedWarriors)) return;
        if (s.Workers < wantedWorkers) { if (HireWorker(s, 2f, 1f, 2f, 0.5f)) return; }
        if (RunWorkshop(s)) return;   // bows for the wall

        // Top the ring back up as it gets chewed.
        if (CanBuild && ringOrdered && s.Wood >= WoodReserve + 150f
            && SimBuilder.PlaceWallRing(BuildingType.WoodenWall, RingHalf, 6) > 0) Did("repair ring");
    }
}

/// <summary>
/// Army first. Tests whether the warrior cost/DPS curve keeps pace with raids
/// that grow with the day number and the colony's prosperity, with almost no
/// economy behind it: a man per raider, and the wood goes on beds and better
/// spears rather than a hoard (which prosperity counts against the colony).
/// </summary>
public class RushPolicy : SimPolicy
{
    public override string Name => "Rush";

    private const int MaxHuts = 8;
    private const int WorkerFloor = 5;

    public override void Tick(SimState s)
    {
        // Spearcraft third, then the beds, then the Workshop tier for Iron Spears
        // and Bows — a rush colony sits on the wood for them.
        if (ManageStance(s)) return;
        // Quarrying was MISSING from this list until 2026-09-11, and Mining
        // requires it: Research skips an id whose prerequisite is unmet, so the
        // last three entries here were unreachable for the whole run and Rush
        // never learned to quarry, never saw a chunk, and never re-armed a loss.
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction",
                        "crafting", "quarrying", "mining", "iron_work", "bowyery")) return;

        int wantedWarriors = WantedWarriors(s, 1f, 2) + (s.RaidTonight ? 1 : 0);
        int wantedWorkers = WantedWorkers(s, WorkerFloor);   // grows with the army it feeds (2026-09-10)
        SetGoal(s, wantedWarriors, wantedWorkers);

        // Minimum viable economy, then everything into warriors.
        if (s.Workers < 3) { if (HireWorker(s, 2f, 2f, 1f)) return; }
        if (KeepHousing(s, MaxHuts, wantedWorkers + wantedWarriors + BuilderReserve(s))) return;
        if (KeepArmy(s, wantedWarriors)) return;
        if (RunWorkshop(s)) return;
        if (s.Workers < wantedWorkers) { if (HireWorker(s, 2f, 2f, 1f, 1f)) return; }
    }
}

/// <summary>
/// Economy first, defence bought out of the surplus — but sized to the raid,
/// seven tenths of it, not to the day number. The baseline every other
/// strategy is read against.
/// </summary>
public class EcoPolicy : SimPolicy
{
    public override string Name => "Eco";

    private const int MaxHuts = 8;
    private const int WorkerFloor = 10;
    private const int RingHalf = 12;   // 8 until 2026-09-10: huts need room inside, clear of the gate corridors

    public override void Tick(SimState s)
    {
        // Spearcraft before Construction (2026-09-10): the first raid does not
        // wait for the economy to finish.
        if (ManageStance(s)) return;
        SimBuilder.SetRing(RingHalf);   // huts stay off the ring line and out of its gate corridors (2026-09-10)
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction", "quarrying",
                        "crafting", "mining", "iron_work", "bowyery")) return;

        int wantedWarriors = WantedWarriors(s, 0.7f, 1) + (s.RaidTonight ? 1 : 0);
        int wantedWorkers = WantedWorkers(s, WorkerFloor);
        SetGoal(s, wantedWarriors, wantedWorkers, s.Day >= 12 ? "escape" : null);

        if (KeepHousing(s, MaxHuts, wantedWorkers + wantedWarriors + BuilderReserve(s))) return;
        if (RunWorkshop(s)) return;
        if (s.Workers < wantedWorkers) { if (HireWorker(s, 3f, 2f, 1f, 1f)) return; }

        // The dawn roll is public knowledge — a human who sees "raiders land
        // tonight" spends the reserve on warriors, so the policy does too.
        bool spend = s.RaidTonight || (s.Wood > 60 && s.Food > 60);
        if (spend && KeepArmy(s, wantedWarriors)) return;

        if (CanBuild && s.Day >= 4 && s.Wood >= 250 && s.Stone >= 150)
        {
            if (SimBuilder.PlaceWallRing(BuildingType.WoodenWall, RingHalf, 12) > 0) { Did("order wall segment"); return; }
            // Ring complete (or blocked): put the two gates in each opening (2026-09-10).
            if (SimBuilder.GateOpenings(BuildingType.WoodenWall, RingHalf) > 0) { Did("gate an opening"); return; }
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
                if (SimBuilder.PlaceShoreBuilding(BuildingType.Shipyard, 60f)) { Did("place Shipyard"); return; }
            }
            if (Shipyard.ActiveList.Count > 0)
            {
                Shipyard yard = TargetingUtil.FindNearestOwned(Shipyard.ActiveList, Vector3.zero, 0f, Factions.Player, out _);
                if (yard != null) { Did("set sail"); yard.SetSail(); return; }
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
    /// <summary>Raiders alive but nothing has happened for <see cref="RaidDirector.LurkSeconds"/> (2026-09-10).</summary>
    public bool RaidLurking;
    /// <summary>
    /// Tonight's committed raid when <see cref="RaidTonight"/>, else what a roll
    /// tomorrow would land against the colony as it stands (2026-09-10). The
    /// number every strategy sizes its army against.
    /// </summary>
    public int NextRaidSize;
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

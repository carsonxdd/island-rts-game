using UnityEngine;

/// <summary>
/// Everything a player decides in a run — what to research, what to queue at
/// the fire, worker assignment, what to build, when to recruit — and nothing
/// else, for ONE colony. Movement, combat, gathering and pathing are still the
/// game's own Utility AI. Was <c>SimPolicy</c>, the sim's simulated player,
/// until 2026-09-16 (lap step 3 slice B): the same three strategies now drive
/// a rival colony in real play, bound to its faction by <see cref="Bind"/>,
/// which is why nothing here may read <c>Factions.Player</c> and why the
/// goal / intent captions are instance state — two governors ticking through
/// one static would trample each other.
///
/// A policy is polled once a second (<see cref="Tick"/>). It should take at most
/// one action per tick, so the resource curve stays legible rather than the
/// whole bank emptying in one frame.
///
/// The simulated player's character (materials, bench labor) is
/// <c>SimPlayerDriver</c>, sim-only. A rival has no castaway: its jobless
/// colonists work its bench from the first day (<c>StationWorkAvailable</c>
/// gates on Crafting for the player only).
/// </summary>
public abstract class GovernorPolicy
{
    public abstract string Name { get; }

    /// <summary>Called once a game-second while the colony is alive.</summary>
    public abstract void Tick(ColonyState s);

    /// <summary>The colony this policy runs. Set once by <see cref="Bind"/>.</summary>
    protected Faction faction;
    /// <summary>The colony's own placement bookkeeping (ring, holes).</summary>
    protected FactionBuilder builder;

    public Faction Faction => faction;
    public FactionBuilder Builder => builder;

    public void Bind(Faction faction, FactionBuilder builder)
    {
        this.faction = faction;
        this.builder = builder;
    }

    // ---- what the colony is working on (2026-09-10) -------------------------
    // Read by the visual overlay and the F3 line only. A policy sets Goal at the
    // top of every tick (what it is trying to reach) and every shared move that
    // DOES something stamps Intent (what it just did). Neither is read by
    // anything that decides.

    /// <summary>This second's target: "army 4/6 · beds 1 free · workers 5/8".</summary>
    public string Goal { get; private set; } = "";
    /// <summary>The last action taken and the game-second it was taken on.</summary>
    public string Intent { get; private set; } = "";
    private float intentTime;

    protected void Did(string what)
    {
        Intent = what;
        intentTime = Time.time;
    }

    /// <summary>
    /// What a player does when the HUD says "Raiders lurking" (2026-09-10): go
    /// Offensive so the militia hunts them down, and stand down at dawn. Called
    /// at the top of every Tick; takes the tick when it changes the stance.
    /// </summary>
    protected bool ManageStance(ColonyState s)
    {
        Faction f = faction;
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

    // ---- neighbours (2026-09-16, slice B5) ---------------------------------
    // Two things a colony does with its warriors besides holding its own fire,
    // both through Expedition (the party sails, fights around the OTHER fire
    // and comes home at dawn). Relief goes to an ally the raid is at; a landing
    // goes to a Hostile neighbour when the army is comfortably past what the
    // next raid needs. Neither strips the home guard: relief takes half, a
    // landing only the surplus over the militia the strategy already wanted.

    /// <summary>Warriors a colony must have before it will send half of them to an ally.</summary>
    public const int ReliefMinWarriors = 4;

    /// <summary>
    /// The share of the wanted strength a governor leaves to the levy (2026-09-16):
    /// spare weapons in the stockpile count as soldiers, because the alarm puts
    /// each one in a colonist's hands (<see cref="Militia"/>). A colony that
    /// wants ten fields five full-time warriors and keeps five weapons on the
    /// rack; the other five bodies work by day and muster at night. Half by
    /// default — a levy walks to the fire first, so a colony of nothing but levy
    /// meets the first raiders unarmed. Rush keeps two thirds standing, the
    /// Conqueror everything (it sails with real warriors). 0.75 was tried on
    /// 2026-09-17 (the overnight levy sweep read 3/4/5 of 6 at 0.75 to 0/2/2 at
    /// 0.5) and did not replicate on the 12 baseline islands: Rush fell 61% →
    /// 36% and every late defeat was a levy larger than the standing army,
    /// mustering one body at a time into raiders already at the gate.
    /// </summary>
    protected virtual float PolicyLevyShare => 0.5f;
    /// <summary>The share in force: the sweep's <see cref="SimHooks.LevyShare"/> when it names one, else the policy's own.</summary>
    protected float LevyShare => SimHooks.Simulating && SimHooks.LevyShare >= 0f ? SimHooks.LevyShare : PolicyLevyShare;
    /// <summary>Weapons kept on the rack past the army's gap - 0 shipped, the levy sweep's <see cref="SimHooks.LevyRackExtra"/> under the sim.</summary>
    protected int RackExtra => SimHooks.Simulating && SimHooks.LevyRackExtra > 0 ? SimHooks.LevyRackExtra : 0;
    /// <summary>Surplus over the wanted militia that is worth sailing with.</summary>
    public const int LandingMinParty = 3;
    /// <summary>Days between one colony's landings.</summary>
    public const int LandingCooldownDays = 3;

    private int lastWantedWarriors;   // written by SetGoal every tick; 0 until the first
    private int lastWantedFullTime;   // the full-time part of it: a landing sails the surplus over THIS
    private int nextLandingDay;

    /// <summary>
    /// Relief for an ally under raid, or a landing on a hostile neighbour. Takes
    /// the tick when a party sails. Called right after <see cref="ManageStance"/>.
    /// </summary>
    protected bool ConsiderExpeditions(ColonyState s)
    {
        if (s.Campfire == null || lastWantedWarriors <= 0 || Expedition.Active(faction)) return false;

        var all = Factions.All;
        for (int i = 0; i < all.Count; i++)
        {
            Faction other = all[i];
            if (other == faction || other.IsRaiders || other.Campfire == null) continue;

            switch (faction.Toward(other))
            {
                case Attitude.Allied:
                {
                    // Their fire has raiders at it, mine does not, and I can spare half.
                    if (s.Warriors < ReliefMinWarriors) continue;
                    if (!ColonyState.RaiderInHomeRadius(other.Campfire) || ColonyState.RaiderInHomeRadius(s.Campfire)) continue;
                    int sent = Expedition.Send(faction, other, s.Warriors / 2, relief: true);
                    if (sent > 0) { Did("send " + sent + " to relieve the " + other.Name); return true; }
                    break;
                }
                case Attitude.Hostile:
                {
                    // By day, on a quiet day, with men to spare past what the raid needs.
                    if (s.Night || s.RaidTonight || s.Day < nextLandingDay) continue;
                    // The levy holds the fire; full-timers past the full-time want can sail (2026-09-16).
                    int surplus = s.Warriors - lastWantedFullTime;
                    if (surplus < LandingMinParty) continue;
                    int sent = Expedition.Send(faction, other, surplus, relief: false);
                    if (sent > 0)
                    {
                        nextLandingDay = s.Day + LandingCooldownDays;
                        Did("land " + sent + " on the " + other.Name);
                        return true;
                    }
                    break;
                }
            }
        }
        return false;
    }

    /// <summary>The one line every strategy's caption shares. "army 3+4/8 (5 full-time)" = warriors + levy over the wanted strength.</summary>
    protected void SetGoal(ColonyState s, int wantedWarriors, int fullTime, int wantedWorkers, string extra = null)
    {
        lastWantedWarriors = wantedWarriors;
        lastWantedFullTime = fullTime;
        Population pop = faction.Population;
        int beds = pop != null ? pop.GetAvailableHousing() : 0;
        Goal = $"army {s.Warriors}+{s.Levy}/{wantedWarriors} ({fullTime} full-time) · workers {s.Workers}/{wantedWorkers} · beds {beds} free"
               + (string.IsNullOrEmpty(extra) ? "" : " · " + extra);
        if (Time.time - intentTime > 30f && Intent.Length > 0 && !Intent.StartsWith("(")) Intent = "(" + Intent + ")";
    }

    /// <summary>The strategy names the sim and the rival picker share, in <see cref="Create"/> order.</summary>
    public static readonly string[] Names = { "Turtle", "Rush", "Eco" };

    public static GovernorPolicy Create(string name)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "turtle": return new TurtlePolicy();
            case "rush": return new RushPolicy();
            case "conqueror": return new ConquerorPolicy();   // the player's only (DeclareWar is the player's); never in Names, so a rival never draws it
            case "eco":
            default: return new EcoPolicy();
        }
    }

    /// <summary>
    /// A rival's personality when nothing named one (2026-09-16, by decision):
    /// one of the three at random. Drawn from <c>UnityEngine.Random</c> on
    /// purpose — that stream is the harness's seed, so a seeded sim run lands
    /// the same neighbour every time, and a run with no rivals never draws.
    /// </summary>
    public static GovernorPolicy CreateRandom() => Create(Names[Random.Range(0, Names.Length)]);

    // ---- shared moves -----------------------------------------------------

    /// <summary>
    /// Queue the first of <paramref name="ids"/> that is neither done nor queued,
    /// at a bench of this colony that lists it — the campfire for its own tier, a
    /// Workshop for the upgrades (2026-09-04). One entry at a time per bench keeps
    /// the material runs short. A Workshop entry is only queued once a Crafter is
    /// on the roster: the simulated character works the fire alone
    /// (<c>SimPlayerDriver</c>), so nobody else would stand at it.
    /// True when something was queued this tick.
    /// </summary>
    protected bool Research(ColonyState s, params string[] ids)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null || fire.Station == null) return false;

        for (int i = 0; i < ids.Length; i++)
        {
            ResearchCatalog.ResearchDef d = ResearchCatalog.Find(ids[i]);
            if (d == null || faction.Knowledge.IsDone(d)) continue;
            if (!faction.Knowledge.IsAvailable(d)) continue;   // a prerequisite is still ahead in the list
            if (CraftStation.IsQueuedAnywhere(d, faction)) return false;

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

    /// <summary>The first living bench of this colony that lists <paramref name="d"/>, or null.</summary>
    CraftStation StationListing(ResearchCatalog.ResearchDef d)
    {
        var list = CraftStation.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            CraftStation st = list[i];
            if (st != null && st.IsAlive && st.Faction == faction && st.Lists(d)) return st;
        }
        return null;
    }

    /// <summary>
    /// Keep weapons in stock + queued at <paramref name="wanted"/>. Needs Spearcraft.
    /// Iron Spears once Iron Work is known and the metal is there (2026-09-04);
    /// after Bowyery every third weapon is a Bow so the garrison gets archers —
    /// queued at the fire like everything else; a Crafter or the character works it.
    /// </summary>
    protected bool KeepSpears(ColonyState s, int wanted)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null || fire.Station == null) return false;
        if (!faction.Knowledge.Has(Unlocks.Kind.Militia)) return false;

        CraftingCatalog.Recipe wooden = CraftingCatalog.Find("wooden_spear");
        CraftingCatalog.Recipe iron = CraftingCatalog.Find("iron_spear");
        CraftingCatalog.Recipe bow = CraftingCatalog.Find("bow");
        bool ironOk = iron != null && iron.UnlockedFor(faction.Knowledge)
                      && faction.Resources.metal >= iron.metalCost;
        CraftingCatalog.Recipe spear = ironOk ? iron : wooden;

        // Spears and bows are counted APART (2026-09-16). One pool let bows nobody
        // would pick fill the quota: the overnight batch's Turtle sat at 59%
        // archers from day 10 (Eco 50%, Rush 17%) because bows in stock counted
        // as "weapons", no spear was queued while they sat there, and a recruit
        // with only bows to choose from took one - a 12-damage militia against
        // a 25-damage one, losing 30-42% of itself a raid night to Rush's 13%.
        // Now a third of the wanted stock is bows at most, and the spears are
        // kept up on their own count.
        // The bow share is a third of the GARRISON (standing archers + the stock
        // being kept), not of the two-weapon buffer: wanted is at most 2, and a
        // third of that rounded to zero armed nobody with a bow at all.
        bool bowKnown = bow != null && bow.UnlockedFor(faction.Knowledge);
        int bowsInStock = bow != null ? fire.Stockpile.Count(bow.output) : 0;
        int wantedBows = bowKnown ? Mathf.Clamp((s.Warriors + wanted) / 3 - ArcherCount(), 0, wanted) : 0;
        int wantedSpears = wanted - wantedBows;
        int bowsHave = bowsInStock + (bow != null ? fire.Station.Queued(bow) : 0);
        int spearsHave = fire.WeaponsInStock() - bowsInStock
                         + fire.Station.Queued(wooden) + fire.Station.Queued(iron);

        CraftingCatalog.Recipe pick;
        if (spearsHave < wantedSpears) pick = spear;
        else if (bowsHave < wantedBows) pick = bow;
        else return false;
        if (!fire.Station.Enqueue(pick, 1)) return false;
        Did("queue " + pick.title);
        return true;
    }

    /// <summary>This colony's standing archers (a warrior whose weapon says ranged).</summary>
    protected int ArcherCount()
    {
        int archers = 0;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].Faction == faction && list[i].IsRanged) archers++;
        return archers;
    }

    /// <summary>
    /// Point the recruit picker at a bow when the stock has one and the garrison is
    /// short of archers (one in three), else at the best spear (2026-09-04).
    /// </summary>
    protected void PickRecruitWeapon(ColonyState s)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return;

        int archers = ArcherCount();
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
    protected bool RunWorkshop(ColonyState s)
    {
        if (!faction.Knowledge.Has(Unlocks.Kind.Crafting) || !CanBuild) return false;
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;

        if (builder.WorkshopCount == 0)
        {
            if (builder.PendingSites(BuildingType.Workshop) > 0) return false;
            if (s.Wood < 80f) return false;
            if (!builder.PlaceBuilding(BuildingType.Workshop, 6f, 14f)) return false;
            Did("place Workshop");
            return true;
        }

        if (fire.crafterWorkers > 0) return false;
        Population pm = faction.Population;
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
    protected bool HireWorker(ColonyState s, float woodShare, float foodShare, float stoneShare, float metalShare = 0f)
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
        Population pm = faction.Population;
        if (pm == null) return false;
        if (pm.GetIdleCount() <= BuilderReserve(s)) return false;

        int total = fire.GetTotalWorkers();
        if (total >= fire.maxWorkers) return false;

        Knowledge k = faction.Knowledge;

        // The colony eats (2026-09-04): whatever the ratio says, keep one forager
        // per eight mouths so a strategy that ignores food starves on schedule
        // rather than by accident.
        if (k.HasJob(ResourceNode.ResourceType.Food)
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
        if (k.HasJob(ResourceNode.ResourceType.Stone)
            && fire.stoneWorkers < 1)
        {
            if (!fire.AssignWorker(ResourceNode.ResourceType.Stone)) return false;
            Did("hire quarryman");
            return true;
        }

        // A job the colony has not researched yet scores nothing (2026-09-03)
        if (!k.HasJob(ResourceNode.ResourceType.Wood)) woodShare = 0f;
        if (!k.HasJob(ResourceNode.ResourceType.Food)) foodShare = 0f;
        if (!k.HasJob(ResourceNode.ResourceType.Stone)) stoneShare = 0f;
        if (!k.HasJob(ResourceNode.ResourceType.Metal)) metalShare = 0f;

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
    protected static int BuilderReserve(ColonyState s) => s.Colonists > 6 ? 2 : 1;

    /// <summary>Arm an idle colonist with a spear from the stockpile (the campfire checks food, cap and research).</summary>
    protected bool Recruit(ColonyState s)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return false;

        // A recruit spends the same idle colonist a site needs, so the reserve
        // holds here too — until raiders land tonight, when a player would arm
        // the builder and finish the hut tomorrow.
        Population pop = faction.Population;
        if (!s.RaidTonight && pop != null && pop.GetIdleCount() <= BuilderReserve(s)) return false;

        PickRecruitWeapon(s);
        if (!fire.CanRecruitWarrior()) return false;

        int before = fire.GetWarriorCount();
        fire.SpawnWarrior();
        if (fire.GetWarriorCount() <= before) return false;
        Did("recruit warrior");
        if (!faction.IsPlayer) DevQuests.Signal("rival:recruited");
        return true;
    }

    // ---- the Storehouse (2026-09-16) ----------------------------------------
    // A drop-off by the far work: a colonist on a node 40 m out spent most of a
    // trip walking five wood home. One store per busy far cluster, at most two.
    protected const int MaxStorehouses = 2;
    /// <summary>A node this far from the fire and from every store is "far work".</summary>
    protected const float StorehouseMinDistance = 24f;
    private const int StorehouseMinWorkers = 2;
    private const float StorehouseWoodFloor = 45f;

    /// <summary>
    /// Place a Storehouse beside the busiest far node when Storage Pits is known
    /// and the bank can spare it. One action per call; false when nothing is
    /// far enough, everything is placed, or a site is already in flight.
    /// </summary>
    protected bool KeepStorehouse(ColonyState s)
    {
        if (!CanBuild || !faction.Knowledge.Has(Unlocks.Kind.Storage)) return false;
        if (builder.PendingSites(BuildingType.Storehouse) > 0) return false;
        if (builder.StorehouseCount >= MaxStorehouses) return false;
        if (s.Wood < StorehouseWoodFloor) return false;

        ResourceNode node = BusiestFarNode(s);
        if (node == null) return false;
        if (!builder.PlaceBuildingAround(BuildingType.Storehouse, node.transform.position, 3f, 9f)) return false;
        Did("place Storehouse");
        return true;
    }

    /// <summary>
    /// The node most of this colony's workers are on, provided it is far from the
    /// fire and from every standing store. Two passes over the owned workers, no
    /// allocation — the governor ticks at 1 Hz and a colony has tens of workers.
    /// </summary>
    ResourceNode BusiestFarNode(ColonyState s)
    {
        BaseBuilding fire = s.Campfire;
        if (fire == null) return null;
        var workers = Worker.ActiveList;
        ResourceNode best = null;
        int bestCount = 0;
        float bestDist = 0f;
        for (int i = 0; i < workers.Count; i++)
        {
            Worker w = workers[i];
            if (w == null || w.Faction != faction) continue;
            ResourceNode node = w.WorkingNode;
            if (node == null) continue;
            bool seen = false;
            for (int j = 0; j < i && !seen; j++)
                seen = workers[j] != null && workers[j].Faction == faction && workers[j].WorkingNode == node;
            if (seen) continue;

            float dist = Dropoff.NearestDistance(faction, node.transform.position);
            if (dist < StorehouseMinDistance) continue;

            int count = 0;
            for (int j = i; j < workers.Count; j++)
                if (workers[j] != null && workers[j].Faction == faction && workers[j].WorkingNode == node) count++;
            if (count < StorehouseMinWorkers) continue;
            if (count > bestCount || (count == bestCount && dist > bestDist))
            {
                best = node;
                bestCount = count;
                bestDist = dist;
            }
        }
        return best;
    }

    /// <summary>Build a hut when housing is the thing capping the colony.</summary>
    protected bool BuildHutIfCapped(ColonyState s, int maxHuts)
    {
        if (!CanBuild) return false;
        if (builder.HutCount + builder.PendingSites(BuildingType.Hut) >= maxHuts) return false;
        if (faction.Population != null
            && faction.Population.GetAvailableHousing() > 0
            && builder.PendingSites(BuildingType.Hut) > 0) return false;
        return builder.PlaceBuilding(BuildingType.Hut, 7f, 16f);
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
    protected static int WantedWarriors(ColonyState s, float perRaider, int floor)
        => Mathf.Max(floor, Mathf.CeilToInt(s.NextRaidSize * perRaider));

    /// <summary>
    /// The full-time part of a wanted strength (2026-09-16): what is left after the
    /// <see cref="LevyShare"/>, never under the strategy's floor, never over the whole.
    /// </summary>
    protected int FullTimeWanted(int wanted, int floor)
        => Mathf.Min(wanted, Mathf.Max(floor, Mathf.CeilToInt(wanted * (1f - LevyShare))));

    /// <summary>
    /// Bodies the strategy needs on the roster (2026-09-16): the full-timers, then
    /// enough colonists for the jobs OR for the levy's weapons, whichever is more
    /// (a levied colonist is a worker by day), plus the builder reserve. What
    /// <see cref="KeepHousing"/> buys beds ahead of.
    /// </summary>
    protected int WantedColonists(ColonyState s, int wantedWorkers, int wantedStrength, int fullTime)
        => fullTime + Mathf.Max(wantedWorkers, wantedStrength - fullTime) + BuilderReserve(s);

    /// <summary>
    /// Workers the strategy wants on jobs: its own floor, or half the standing
    /// army if that is more, never above the campfire's job cap. The second lab
    /// of 2026-09-10 lost Rush to food, not raiders: five workers fed 25 warriors
    /// (a bed each, a meal a day, 15 food per replacement) until the bank hit
    /// single digits on day 24 and the army decayed from 28 to 12 for the
    /// day-30 raid. The economy has to grow with the men it feeds.
    /// </summary>
    protected static int WantedWorkers(ColonyState s, int floor)
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
    protected bool KeepHousing(ColonyState s, int maxHuts, int wantedColonists)
    {
        if (!CanBuild) return false;
        Population pop = faction.Population;
        if (pop == null) return false;
        if (builder.PendingSites(BuildingType.Hut) > 0) return false;
        if (builder.HutCount >= maxHuts) return false;
        if (s.Colonists + pop.GetAvailableHousing() >= wantedColonists) return false;
        if (!builder.PlaceBuilding(BuildingType.Hut, 7f, 16f)) return false;
        Did("place hut");
        return true;
    }

    /// <summary>
    /// Keep the colony's strength at <paramref name="wanted"/> (2026-09-16): the rack
    /// first — every weapon the levy will need and the recruits will take, capped
    /// at the bodies there are to hold them, the mustered ones already out of the
    /// stockpile not re-queued — then a full-time recruit while the standing army
    /// is under <paramref name="fullTime"/>. One action per call. Until the levy
    /// this kept the rack two ahead of the men and recruited to the whole want.
    /// </summary>
    protected bool KeepArmy(ColonyState s, int wanted, int fullTime)
    {
        int extra = RackExtra;   // the levy sweep's spare rack, 0 shipped
        if (s.Strength >= wanted + extra && s.Warriors >= fullTime) return false;
        int bodies = Mathf.Max(0, s.Colonists - s.Warriors);
        int rack = Mathf.Min(wanted + extra - s.Warriors, bodies) - s.Mustered;
        if (s.Warriors < fullTime) rack = Mathf.Max(rack, 1);   // a recruit takes one off the rack
        if (rack > 0 && KeepSpears(s, rack)) return true;
        return s.Warriors < fullTime && Recruit(s);
    }

    /// <summary>Construction research gates every placement.</summary>
    protected bool CanBuild => faction.Knowledge.Has(Unlocks.Kind.Construction);
}

/// <summary>
/// Walls first. Tests whether fortification lets a SMALLER army hold — the
/// militia is sized at six tenths of the raid, the ring is meant to make up the
/// rest — and whether wall HP vs enemy DPS is in the right neighbourhood.
/// </summary>
public class TurtlePolicy : GovernorPolicy
{
    public override string Name => "Turtle";

    /// <summary>Wood never spent on walls, so the colony can still fund warriors and repairs.</summary>
    private const float WoodReserve = 120f;
    private const int MaxHuts = 6;
    private const int WorkerFloor = 8;
    private const int RingHalf = 12;   // 9 until 2026-09-10: huts need room inside, clear of the gate corridors

    private bool ringOrdered;

    public override void Tick(ColonyState s)
    {
        // Spears before stone (2026-09-10): the first raid lands on day 3-4 and
        // a wall with nobody behind it only delays it. Then the ring's stone,
        // then Bowyery so the men behind the wall can shoot over it.
        if (ManageStance(s)) return;
        if (ConsiderExpeditions(s)) return;
        builder.SetRing(RingHalf);   // huts stay off the ring line and out of its gate corridors (2026-09-10)
        // Mining and Iron Work before Bowyery (2026-09-16): the overnight batch's
        // Turtle sat on a median 1179 stone and 1320 wood with no metal job and
        // never made an Iron Spear, while Rush (which lists them) lost 13% of its
        // militia a raid night to Turtle's 30-42%. An Iron Spear is +40% damage
        // and needs no chunk, the one material every re-arm was short of.
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction", "quarrying",
                        "crafting", "storage_pits", "mining", "iron_work", "bowyery")) return;

        // A man per raider, not 0.6 of one (2026-09-11). The 2026-09-11 lab
        // watched this colony hold at 9 warriors from day 6 to day 11 with 43
        // sticks and 17 chunks banked - fourteen spears it never made - because
        // 0.6x of a 14-raider night is 9. The raiders-per-warrior threshold the
        // second lab measured is ~1.0, so a turtle at 0.6 is built to lose. The
        // wall is meant to be the edge on top of parity, not a substitute for it.
        int wantedWarriors = WantedWarriors(s, 1f, 2) + (s.RaidTonight ? 1 : 0);
        int fullTime = FullTimeWanted(wantedWarriors, 2);
        int wantedWorkers = WantedWorkers(s, WorkerFloor);
        SetGoal(s, wantedWarriors, fullTime, wantedWorkers,
            ringOrdered ? $"ring up · gates {builder.GateCount}/8" : "ring pending");

        // Enough economy to pay for a wall, then wall, then the guard.
        if (s.Workers < 4) { if (HireWorker(s, 2f, 1f, 2f)) return; }
        if (KeepHousing(s, MaxHuts, WantedColonists(s, wantedWorkers, wantedWarriors, fullTime))) return;
        if (KeepStorehouse(s)) return;

        // Build the ring in segments, keeping a wood reserve. Committing the
        // whole bank to 48 wall sites at once is what made this policy lose
        // every run at 0 wood / 400 stone: it could no longer fund warriors,
        // huts, or replacements for anything it lost.
        if (CanBuild && !ringOrdered && s.Wood >= WoodReserve + 90f && s.Stone >= 40)
        {
            if (builder.PlaceWallRing(BuildingType.WoodenWall, RingHalf, 6) > 0) { Did("order wall segment"); return; }
            ringOrdered = true;   // ring is complete or fully blocked
        }

        // The openings get their two gates each as soon as the ring is ordered:
        // a wall site per cell, converted the tick it finishes (2026-09-10).
        if (ringOrdered && builder.GateOpenings(BuildingType.WoodenWall, RingHalf) > 0) { Did("gate an opening"); return; }

        if (KeepArmy(s, wantedWarriors, fullTime)) return;
        if (s.Workers < wantedWorkers) { if (HireWorker(s, 2f, 1f, 2f, 0.5f)) return; }
        if (RunWorkshop(s)) return;   // bows for the wall

        // Top the ring back up as it gets chewed.
        if (CanBuild && ringOrdered && s.Wood >= WoodReserve + 150f
            && builder.PlaceWallRing(BuildingType.WoodenWall, RingHalf, 6) > 0) Did("repair ring");
    }
}

/// <summary>
/// Army first. Tests whether the warrior cost/DPS curve keeps pace with raids
/// that grow with the day number and the colony's prosperity, with almost no
/// economy behind it: a man per raider, and the wood goes on beds and better
/// spears rather than a hoard (which prosperity counts against the colony).
/// </summary>
public class RushPolicy : GovernorPolicy
{
    /// <summary>Army first: two thirds of the strength stands full-time, a third is the rack (2026-09-16).</summary>
    protected override float PolicyLevyShare => 1f / 3f;

    public override string Name => "Rush";

    private const int MaxHuts = 8;
    private const int WorkerFloor = 5;

    public override void Tick(ColonyState s)
    {
        // Spearcraft third, then the beds, then the Workshop tier for Iron Spears
        // and Bows — a rush colony sits on the wood for them.
        if (ManageStance(s)) return;
        if (ConsiderExpeditions(s)) return;
        // Quarrying was MISSING from this list until 2026-09-11, and Mining
        // requires it: Research skips an id whose prerequisite is unmet, so the
        // last three entries here were unreachable for the whole run and Rush
        // never learned to quarry, never saw a chunk, and never re-armed a loss.
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction",
                        "crafting", "quarrying", "mining", "iron_work", "bowyery", "storage_pits")) return;

        int wantedWarriors = WantedWarriors(s, 1f, 2) + (s.RaidTonight ? 1 : 0);
        int fullTime = FullTimeWanted(wantedWarriors, 2);
        int wantedWorkers = WantedWorkers(s, WorkerFloor);   // grows with the army it feeds (2026-09-10)
        SetGoal(s, wantedWarriors, fullTime, wantedWorkers);

        // Minimum viable economy, then everything into warriors.
        if (s.Workers < 3) { if (HireWorker(s, 2f, 2f, 1f)) return; }
        if (KeepHousing(s, MaxHuts, WantedColonists(s, wantedWorkers, wantedWarriors, fullTime))) return;
        if (KeepArmy(s, wantedWarriors, fullTime)) return;
        if (RunWorkshop(s)) return;
        if (KeepStorehouse(s)) return;
        if (s.Workers < wantedWorkers) { if (HireWorker(s, 2f, 2f, 1f, 1f)) return; }
    }
}

/// <summary>
/// Economy first, defence bought out of the surplus — but sized to the raid,
/// seven tenths of it, not to the day number. The baseline every other
/// strategy is read against.
/// </summary>
public class EcoPolicy : GovernorPolicy
{
    public override string Name => "Eco";

    private const int MaxHuts = 8;
    private const int WorkerFloor = 10;
    private const int RingHalf = 12;   // 8 until 2026-09-10: huts need room inside, clear of the gate corridors

    public override void Tick(ColonyState s)
    {
        // Spearcraft before Construction (2026-09-10): the first raid does not
        // wait for the economy to finish.
        if (ManageStance(s)) return;
        if (ConsiderExpeditions(s)) return;
        builder.SetRing(RingHalf);   // huts stay off the ring line and out of its gate corridors (2026-09-10)
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction", "quarrying",
                        "storage_pits", "crafting", "mining", "iron_work", "bowyery")) return;

        int wantedWarriors = WantedWarriors(s, 0.7f, 1) + (s.RaidTonight ? 1 : 0);
        int fullTime = FullTimeWanted(wantedWarriors, 1);
        int wantedWorkers = WantedWorkers(s, WorkerFloor);
        bool escapes = faction.IsPlayer;   // a rival's Shipyard would end the PLAYER's run (GameManager.TriggerEscape)
        SetGoal(s, wantedWarriors, fullTime, wantedWorkers, escapes && s.Day >= 12 ? "escape" : null);

        if (KeepHousing(s, MaxHuts, WantedColonists(s, wantedWorkers, wantedWarriors, fullTime))) return;
        if (KeepStorehouse(s)) return;
        if (RunWorkshop(s)) return;
        if (s.Workers < wantedWorkers) { if (HireWorker(s, 3f, 2f, 1f, 1f)) return; }

        // The dawn roll is public knowledge — a human who sees "raiders land
        // tonight" spends the reserve on warriors, so the policy does too.
        bool spend = s.RaidTonight || (s.Wood > 60 && s.Food > 60);
        if (spend && KeepArmy(s, wantedWarriors, fullTime)) return;

        if (CanBuild && s.Day >= 4 && s.Wood >= 250 && s.Stone >= 150)
        {
            if (builder.PlaceWallRing(BuildingType.WoodenWall, RingHalf, 12) > 0) { Did("order wall segment"); return; }
            // Ring complete (or blocked): put the two gates in each opening (2026-09-10).
            if (builder.GateOpenings(BuildingType.WoodenWall, RingHalf) > 0) { Did("gate an opening"); return; }
        }

        // The escape (2026-09-04, Slice 6): from day 12 research Shipwright, build the
        // Shipyard on the beach when the bank covers it, and sail the moment it stands.
        // A run that ends "escape" is the metric: how early can Eco leave?
        // The player's colony only: the escape is the run's victory beat, and a
        // rival leaving the island has no rule yet (2026-09-16) — it stays and
        // spends the surplus on the wall and the militia instead.
        if (escapes && s.Day >= 12)
        {
            if (Research(s, "shipwright")) return;
            if (faction.Knowledge.Has(Unlocks.Kind.Shipwright) && CanBuild
                && builder.ShipyardCount == 0 && builder.PendingSites(BuildingType.Shipyard) == 0)
            {
                if (builder.PlaceShoreBuilding(BuildingType.Shipyard, 60f)) { Did("place Shipyard"); return; }
            }
            if (builder.ShipyardCount > 0)
            {
                Shipyard yard = TargetingUtil.FindNearestOwned(Shipyard.ActiveList, Vector3.zero, 0f, faction, out _);
                if (yard != null) { Did("set sail"); yard.SetSail(); return; }
            }
        }
    }
}

/// <summary>
/// Take the neighbour out (2026-09-16, the conquest test). A Rush economy and
/// army, then: once a rival has landed and <see cref="WarDay"/> has come, war
/// is declared (the player's own <see cref="Diplomacy.DeclareWar"/>, so this
/// policy is the player's only), and every quiet day the men past a home
/// guard of half the next raid sail for the rival's shore, where a landing
/// party besieges buildings (<see cref="Siege"/>) and the fire; the sea brings
/// the survivors home at dawn, and they sail again the next day. Beating the
/// game never needed this — the test is whether a colony that tries it can
/// afford it, and what the loot is worth.
/// </summary>
public class ConquerorPolicy : GovernorPolicy
{
    public override string Name => "Conqueror";

    private const int MaxHuts = 8;
    private const int WorkerFloor = 5;
    /// <summary>War is declared on the first dawn at or after this day with a rival ashore.</summary>
    public const int WarDay = 12;
    /// <summary>Warriors kept home per raider of the next raid.</summary>
    public const float HomeGuardShare = 0.5f;
    /// <summary>Fewer than this and nobody sails.</summary>
    public const int MinParty = 4;
    /// <summary>Men the army is kept above the rival's, so a landing outnumbers its militia.</summary>
    public const int Overmatch = 4;
    /// <summary>A landing sails with real warriors: nothing is left to the levy (2026-09-16).</summary>
    protected override float PolicyLevyShare => 0f;

    private int nextSailDay;

    public override void Tick(ColonyState s)
    {
        if (ManageStance(s)) return;
        if (Research(s, "woodcutting", "foraging", "spearcraft", "construction",
                        "crafting", "quarrying", "mining", "iron_work", "bowyery", "storage_pits")) return;

        Faction rival = FirstRival();
        int rivalWarriors = rival != null ? TargetingUtil.CountOwned(Warrior.ActiveList, rival) : 0;
        bool atWar = rival != null && faction.Toward(rival) == Attitude.Hostile;
        bool theirFireStands = rival != null && rival.Campfire != null;

        int wantedWarriors = WantedWarriors(s, 1f, 2) + (s.RaidTonight ? 1 : 0);
        if (theirFireStands) wantedWarriors = Mathf.Max(wantedWarriors, rivalWarriors + Overmatch + Mathf.CeilToInt(s.NextRaidSize * HomeGuardShare));
        int fullTime = FullTimeWanted(wantedWarriors, 2);   // LevyShare 0: all of it
        int wantedWorkers = WantedWorkers(s, WorkerFloor);
        SetGoal(s, wantedWarriors, fullTime, wantedWorkers,
            rival == null ? "no rival yet" : !theirFireStands ? "rival fire out" : atWar ? "at war · " + rivalWarriors + " of theirs" : "war on day " + WarDay);

        if (s.Workers < 3) { if (HireWorker(s, 2f, 2f, 1f)) return; }
        if (KeepHousing(s, MaxHuts, WantedColonists(s, wantedWorkers, wantedWarriors, fullTime))) return;

        // The war, and the daily landing
        if (faction.IsPlayer && theirFireStands && !atWar && s.Day >= WarDay)
        {
            Diplomacy.DeclareWar(rival);
            Did("declare war on the " + rival.Name);
            return;
        }
        if (atWar && theirFireStands && !s.Night && !s.RaidTonight && !Expedition.Active(faction) && s.Day >= nextSailDay)
        {
            int guard = Mathf.CeilToInt(s.NextRaidSize * HomeGuardShare);
            int party = s.Warriors - guard;
            if (party >= MinParty)
            {
                int sent = Expedition.Send(faction, rival, party, relief: false);
                if (sent > 0)
                {
                    nextSailDay = s.Day + 1;
                    Did("land " + sent + " on the " + rival.Name);
                    return;
                }
            }
        }

        if (KeepArmy(s, wantedWarriors, fullTime)) return;
        if (RunWorkshop(s)) return;
        if (KeepStorehouse(s)) return;
        if (s.Workers < wantedWorkers) { if (HireWorker(s, 2f, 2f, 1f, 1f)) return; }
    }

    /// <summary>The first rival ashore this run, or null. The sim's rival columns are about the same colony.</summary>
    static Faction FirstRival()
    {
        RivalLandingDirector dir = RivalLandingDirector.Instance;
        return dir != null && dir.Landed.Count > 0 ? dir.Landed[0] : null;
    }
}

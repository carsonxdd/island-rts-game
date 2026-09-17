using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// One faction's roster: who lives here, where they sleep, how new people arrive
/// and what they eat. A plain class on <see cref="Faction.Population"/> (2026-09-09,
/// lap step 1 commit 3 — the body of the old <c>PopulationManager</c> singleton),
/// ticked once per frame by the scene's <see cref="PopulationManager"/>, which also
/// carries the tunables.
/// </summary>
/// <remarks>
/// Colonists are a pool, not a purchase (2026-09-02). Housing (the campfire and every hut)
/// registers itself here; whenever the roster is short of the capacity, a survivor washes
/// ashore at the landing cove every <c>arrivalInterval</c> seconds (daytime only by
/// default) and walks to the home that has room. The campfire panel then hands idle
/// colonists jobs — it never creates people. Recruiting a warrior converts an idle
/// colonist, so warriors occupy housing too and the roster is the one population count.
///
/// Single owner rules: buildings register/unregister their own housing, and a colonist
/// leaves the roster through exactly one path per unit type —
/// <c>Worker.OnDestroy → BaseBuilding.NotifyWorkerRemoved → RemoveColonist</c> and
/// <c>Warrior.Die → BaseBuilding.NotifyWarriorKilled → RemoveColonist</c>. Roster
/// membership is the idempotence guard, so a unit that was converted (worker → warrior)
/// is a no-op when its old body is destroyed.
///
/// Food (2026-09-04): every colonist eats from the faction's pool, continuously, one
/// unit at a time. <see cref="Hunger"/> is DERIVED from <c>starvedSeconds</c>, never stored.
/// </remarks>
public sealed class Population
{
    /// <summary>One person in the colony. The unit is a Worker or a Warrior.</summary>
    public class Colonist
    {
        public MonoBehaviour unit;
        public IHousing home;   // null = homeless
        public Persona persona; // who they are (2026-09-16); rolled once, kept across body swaps
    }

    public enum HungerState { Fed, Hungry, Starving }

    /// <summary>Fires when this colony crosses between Fed / Hungry / Starving. The HUD flashes a banner on the player's.</summary>
    public event System.Action<HungerState> OnHungerChanged;
    /// <summary>Fires when a starving colonist walks out on this colony.</summary>
    public event System.Action OnColonistLeft;

    public readonly Faction faction;

    private readonly List<IHousing> housing = new List<IHousing>();
    private readonly List<Colonist> roster = new List<Colonist>();
    private float arrivalTimer;
    private float pruneTimer;
    private bool started;

    private float foodDebt;          // fractional food owed; a whole unit is taken when it reaches 1
    private float starvedSeconds;    // how long the pool has been empty when a unit came due
    private float nextDepartureAt;   // starvedSeconds at which the next colonist walks out
    private HungerState hungerShown = HungerState.Fed;

    private const float PruneInterval = 1f;

    /// <summary>The balance sim's "nobody eats" switch (a 0 tunable would fall back to the default).</summary>
    public bool foodDisabled;

    public Population(Faction faction)
    {
        this.faction = faction;
    }

    // Tunables live on the scene component (see PopulationManager.Settings)
    static float ArrivalInterval => PopulationManager.Settings.ArrivalInterval;
    static float FoodPerDay => PopulationManager.Settings.FoodPerDay;
    static float HungryAfter => PopulationManager.Settings.HungryAfterDays;
    static float StarvingAfter => PopulationManager.Settings.StarvingAfterDays;

    // ------------------------------------------------------------------
    // Housing (owned by the buildings)
    // ------------------------------------------------------------------

    /// <summary>A building's Start registers its housing here. Homeless colonists move in at once.</summary>
    public void RegisterHousing(IHousing provider)
    {
        if (provider == null || housing.Contains(provider)) return;
        housing.Add(provider);
        RehomeHomeless();
    }

    /// <summary>Death or destruction. The building's residents become homeless and are rehomed if anywhere has room.</summary>
    public void UnregisterHousing(IHousing provider)
    {
        if (provider == null || !housing.Remove(provider)) return;
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].home == provider) roster[i].home = null;
        }
        RehomeHomeless();
    }

    /// <summary>Every registered housing provider, in the order they were built. The HUD's housing breakdown walks this.</summary>
    public IReadOnlyList<IHousing> HousingProviders => housing;

    public int GetHousingCapacity()
    {
        int cap = 0;
        for (int i = 0; i < housing.Count; i++)
        {
            IHousing h = housing[i];
            if (h != null && h.HousingAlive) cap += h.HousingCapacity;
        }
        return cap;
    }

    /// <summary>Residents homed to this building.</summary>
    public int OccupantsOf(IHousing provider)
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].home == provider && roster[i].unit != null) n++;
        }
        return n;
    }

    /// <summary>The first live building with a free slot (campfire first, then huts in build order).</summary>
    public IHousing FindHomeWithRoom()
    {
        for (int i = 0; i < housing.Count; i++)
        {
            IHousing h = housing[i];
            if (h == null || !h.HousingAlive) continue;
            if (OccupantsOf(h) < h.HousingCapacity) return h;
        }
        return null;
    }

    /// <summary>Where this unit sleeps, or null when homeless.</summary>
    public IHousing HomeOf(MonoBehaviour unit)
    {
        Colonist c = Find(unit);
        if (c == null || c.home == null || !c.home.HousingAlive) return null;
        return c.home;
    }

    void RehomeHomeless()
    {
        for (int i = 0; i < roster.Count; i++)
        {
            Colonist c = roster[i];
            if (c.unit == null) continue;
            if (c.home != null && c.home.HousingAlive) continue;
            c.home = FindHomeWithRoom();   // may stay null — genuinely homeless
        }
    }

    // ------------------------------------------------------------------
    // Roster
    // ------------------------------------------------------------------

    Colonist Find(MonoBehaviour unit)
    {
        if (unit == null) return null;
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].unit == unit) return roster[i];
        }
        return null;
    }

    /// <summary>A new person joins the colony (spawned by the campfire). Home may be null.</summary>
    public void AddColonist(MonoBehaviour unit, IHousing home)
    {
        if (unit == null || Find(unit) != null) return;
        Persona persona = Persona.Roll(this);
        roster.Add(new Colonist { unit = unit, home = home, persona = persona });
        if (faction != null && faction.IsPlayer) DevQuests.Signal("persona");
    }

    /// <summary>Everyone on the roster, in arrival order (the campfire panel's People list walks this). Entries may hold a dead unit until the next prune.</summary>
    public IReadOnlyList<Colonist> Roster => roster;

    /// <summary>The name and trait of a rostered unit, or null.</summary>
    public Persona PersonaOf(MonoBehaviour unit)
    {
        Colonist c = Find(unit);
        return c != null ? c.persona : null;
    }

    /// <summary>True while a living colonist here carries this name (Persona.Roll steps past it).</summary>
    public bool NameInUse(string name)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            Colonist c = roster[i];
            if (c.unit != null && c.persona != null && c.persona.Name == name) return true;
        }
        return false;
    }

    /// <summary>The single removal path. Safe to call for a unit that was never (or is no longer) on the roster.</summary>
    public void RemoveColonist(MonoBehaviour unit)
    {
        Colonist c = Find(unit);
        if (c != null) roster.Remove(c);
    }

    /// <summary>
    /// The same person in a new body: a colonist becoming a warrior, or a dismissed
    /// warrior becoming a colonist again. Keeps their home and their roster slot, so
    /// destroying the old body afterwards is a no-op for the count.
    /// </summary>
    public void ReplaceUnit(MonoBehaviour oldUnit, MonoBehaviour newUnit)
    {
        Colonist c = Find(oldUnit);
        if (c == null) { AddColonist(newUnit, FindHomeWithRoom()); return; }
        c.unit = newUnit;
        if (faction != null && faction.IsPlayer && c.persona != null) DevQuests.Signal("persona:kept");   // same name, new body
    }

    /// <summary>Everyone alive: workers with jobs, idle colonists and warriors.</summary>
    public int GetColonistCount()
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].unit != null) n++;
        }
        return n;
    }

    /// <summary>Utility colonists (no job, no specialty): the pool the campfire panel assigns from.</summary>
    public int GetIdleCount()
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            Worker w = roster[i].unit as Worker;
            if (w != null && w.IsIdle) n++;
        }
        return n;
    }

    /// <summary>The idle colonist nearest a point, or null when nobody is idle.</summary>
    public Worker FindIdleColonist(Vector3 near)
    {
        Worker best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < roster.Count; i++)
        {
            Worker w = roster[i].unit as Worker;
            if (w == null || !w.IsIdle) continue;
            float sqr = (w.transform.position - near).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = w; }
        }
        return best;
    }

    public bool HasAvailableHousing() => GetColonistCount() < GetHousingCapacity();

    public int GetAvailableHousing() => Mathf.Max(0, GetHousingCapacity() - GetColonistCount());

    public bool HasHomelessWorkers() => GetHomelessCount() > 0;

    public int GetHomelessCount()
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            Colonist c = roster[i];
            if (c.unit == null) continue;
            if (c.home == null || !c.home.HousingAlive) n++;
        }
        return n;
    }

    // ------------------------------------------------------------------
    // Tick (arrivals, food)
    // ------------------------------------------------------------------

    /// <summary>Seconds until the next survivor lands, or -1 when nobody is coming (no room, night, no campfire).</summary>
    public float SecondsToNextArrival => ArrivalsOpen() ? Mathf.Max(0f, arrivalTimer) : -1f;

    /// <summary>One frame. Called by <see cref="PopulationManager"/> for every faction.</summary>
    public void Tick(float dt)
    {
        if (!started) { started = true; arrivalTimer = ArrivalInterval; }

        pruneTimer -= dt;
        if (pruneTimer <= 0f)
        {
            pruneTimer = PruneInterval;
            Prune();
        }

        UpdateFood(dt);

        if (!ArrivalsOpen())
        {
            // Hold the timer while nobody can land, so a colony that just built a
            // hut does not get an instant arrival the moment night ends.
            arrivalTimer = Mathf.Min(arrivalTimer, ArrivalInterval);
            return;
        }

        arrivalTimer -= dt;
        if (arrivalTimer <= 0f)
        {
            arrivalTimer = ArrivalInterval;
            SpawnArrival(false);
        }
    }

    bool ArrivalsOpen()
    {
        if (GameStartController.IntroInProgress) return false;
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return false;
        if (PopulationManager.Settings.ArriveOnlyByDay && IsNight()) return false;
        if (Campfire() == null) return false;
        if (Hunger != HungerState.Fed) return false;   // word gets round: nobody joins a hungry colony
        return FindHomeWithRoom() != null;
    }

    // ------------------------------------------------------------------
    // Food (2026-09-04)
    // ------------------------------------------------------------------

    /// <summary>Seconds in a calendar day at the active difficulty (the "per day" unit).</summary>
    static float CycleSeconds
    {
        get
        {
            DayNightCycle dn = PopulationManager.DayNight;
            return dn != null ? Mathf.Max(1f, dn.CycleSeconds) : 150f;
        }
    }

    /// <summary>Food the whole colony eats per calendar day, after the difficulty knob.</summary>
    public float DailyDrain => GetColonistCount() * FoodPerDay * Difficulty.FoodConsumptionMultiplier;

    /// <summary>Fed, Hungry (a quarter day without food) or Starving (a full day).</summary>
    public HungerState Hunger
    {
        get
        {
            float cycle = CycleSeconds;
            if (starvedSeconds >= StarvingAfter * cycle) return HungerState.Starving;
            if (starvedSeconds >= HungryAfter * cycle) return HungerState.Hungry;
            return HungerState.Fed;
        }
    }

    /// <summary>Days of food in the stores at the current drain (large when nothing is eaten).</summary>
    public float FoodReserveDays
    {
        get
        {
            float drain = DailyDrain;
            if (drain <= 0.0001f) return 999f;
            return faction.Resources.food / drain;
        }
    }

    /// <summary>Colonists who walked out because the colony starved them. Shown on the end screen.</summary>
    public int ColonistsLeft { get; private set; }

    /// <summary>
    /// What a hungry colony's labor is worth: read at the point of effect by the
    /// gather and construction ticks (the CraftedUpgrades pattern), 1 when fed.
    /// </summary>
    public float LaborMultiplier => Hunger != HungerState.Fed ? PopulationManager.HungryLaborMultiplier : 1f;

    bool ConsumptionOpen()
    {
        if (foodDisabled) return false;
        if (GameStartController.IntroInProgress) return false;
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return false;
        return Campfire() != null;
    }

    void UpdateFood(float dt)
    {
        if (!ConsumptionOpen()) return;
        int eaters = GetColonistCount();   // the player character is not on the roster and does not eat
        if (eaters <= 0) return;

        float cycle = CycleSeconds;
        foodDebt += eaters * FoodPerDay * Difficulty.FoodConsumptionMultiplier * dt / cycle;

        if (foodDebt >= 1f)
        {
            if (faction.Resources.SpendFood(1))
            {
                foodDebt -= 1f;
                starvedSeconds = 0f;
                nextDepartureAt = StarvingAfter * cycle;
            }
            else
            {
                // Nothing to eat: the debt holds at one unit and the clock runs
                foodDebt = 1f;
                starvedSeconds += dt;
                if (nextDepartureAt <= 0f) nextDepartureAt = StarvingAfter * cycle;
                if (starvedSeconds >= nextDepartureAt)
                {
                    nextDepartureAt += cycle;   // one more per day until they are fed
                    SendOneAway();
                }
            }
        }

        HungerState now = Hunger;
        if (now != hungerShown)
        {
            hungerShown = now;
            OnHungerChanged?.Invoke(now);
            if (faction.IsPlayer)
                DevQuests.Signal(now == HungerState.Hungry ? "hungry" : now == HungerState.Starving ? "starving" : "fed");
        }
    }

    /// <summary>
    /// A starving colonist gives up on the colony: jobless first (they lose the
    /// least), then a worker with a job, then a warrior (dismissed so the weapon
    /// stays). The body walks to the cove and is destroyed there, which is the
    /// normal removal path, so the roster and housing update themselves.
    /// </summary>
    void SendOneAway()
    {
        Worker pick = PickLeaver(wantJobless: true) ?? PickLeaver(wantJobless: false);
        if (pick == null)
        {
            BaseBuilding fire = Campfire();
            Warrior soldier = null;
            for (int i = 0; i < roster.Count && soldier == null; i++) soldier = roster[i].unit as Warrior;
            if (fire != null && soldier != null) pick = fire.DismissWarrior(soldier);
        }
        if (pick == null) return;

        pick.Leave();
        ColonistsLeft++;
        OnColonistLeft?.Invoke();
        if (faction.IsPlayer) DevQuests.Signal("colonist_left");
    }

    /// <summary>F4 cheat: jump straight to Starving (the next departure is due at once).</summary>
    public void DebugStarve()
    {
        foodDebt = 1f;
        starvedSeconds = StarvingAfter * CycleSeconds;
        nextDepartureAt = starvedSeconds;
    }

    Worker PickLeaver(bool wantJobless)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            Worker w = roster[i].unit as Worker;
            if (w == null || w.leaving) continue;
            if (wantJobless != !w.hasJob) continue;
            return w;
        }
        return null;
    }

    static bool IsNight()
    {
        DayNightCycle dn = PopulationManager.DayNight;
        return dn != null && dn.IsNightTime();
    }

    /// <summary>
    /// This colony's living campfire (<see cref="Faction.Campfire"/>, which the campfire assigns itself in Start).
    /// </summary>
    BaseBuilding Campfire() => faction.Campfire;

    /// <summary>
    /// One survivor comes ashore and is homed to the first building with room.
    /// Lands at the cove (where the shipwreck is) and walks in; <paramref name="atCampfire"/>
    /// spawns beside the fire instead — used by the F4 quick-start.
    /// Returns the new colonist, or null when there is no room or no campfire.
    /// </summary>
    public Worker SpawnArrival(bool atCampfire)
    {
        BaseBuilding fire = Campfire();
        if (fire == null) return null;
        IHousing home = FindHomeWithRoom();
        if (home == null) return null;

        Vector3 pos;
        if (!atCampfire && TerrainGrid.Instance != null)
        {
            // One metre east of the cove centre, the same spot the survivor lands
            // on. A rival landed by RivalFounder has its OWN cove on its own far
            // shore (2026-09-11); everyone else uses the island's anchored one.
            Vector3 cove = faction.HasCove ? faction.Cove : TerrainGrid.Instance.CoveCenter;
            pos = cove + new Vector3(1f, 0f, 0f);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(pos, out hit, 6f, NavMesh.AllAreas)) pos = hit.position;
            else pos = fire.GetValidSpawnPosition();
        }
        else
        {
            pos = fire.GetValidSpawnPosition();
        }

        return fire.SpawnColonist(pos, home);
    }

    /// <summary>Drop roster entries whose unit died without notifying (belt and braces — never the primary path).</summary>
    void Prune()
    {
        for (int i = roster.Count - 1; i >= 0; i--)
        {
            if (roster[i].unit == null) roster.RemoveAt(i);
        }
        for (int i = housing.Count - 1; i >= 0; i--)
        {
            // Interface references never compare equal to null after Destroy; the
            // concrete Unity object does.
            if (housing[i] is Object o && o == null) housing.RemoveAt(i);
        }
    }
}

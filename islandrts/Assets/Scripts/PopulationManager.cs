using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// The colony's roster: who lives here, where they sleep, and how new people arrive.
/// </summary>
/// <remarks>
/// Colonists are a pool, not a purchase (2026-09-02). Housing (the campfire and every hut)
/// registers itself here; whenever the roster is short of the capacity, a survivor washes
/// ashore at the landing cove every <see cref="arrivalInterval"/> seconds (daytime only by
/// default) and walks to the home that has room. The campfire panel then hands idle
/// colonists jobs — it never creates people. Recruiting a warrior converts an idle
/// colonist, so warriors occupy housing too and the roster is the one population count.
///
/// Single owner rules, same as before: buildings register/unregister their own housing,
/// and a colonist leaves the roster through exactly one path per unit type —
/// <c>Worker.OnDestroy → BaseBuilding.NotifyWorkerRemoved → RemoveColonist</c> and
/// <c>Warrior.Die → BaseBuilding.NotifyWarriorKilled → RemoveColonist</c>. Roster
/// membership is the idempotence guard, so a unit that was converted (worker → warrior)
/// is a no-op when its old body is destroyed.
/// </remarks>
public class PopulationManager : MonoBehaviour
{
    public static PopulationManager Instance { get; private set; }

    /// <summary>One person in the colony. The unit is a Worker or a Warrior.</summary>
    public class Colonist
    {
        public MonoBehaviour unit;
        public IHousing home;   // null = homeless
    }

    [Header("Arrivals")]
    [Tooltip("Seconds between survivors coming ashore while there is free housing.")]
    public float arrivalInterval = 20f;
    [Tooltip("Nobody lands at night — the shallows are where the raids come from.")]
    public bool arriveOnlyByDay = true;

    // --- Food (2026-09-04, Slice 4) ---
    // Every colonist eats from the pool, continuously, one unit at a time. The
    // scene object predates these fields, so a non-positive value falls back to
    // the defaults below (a missing YAML key deserializes as 0).
    [Header("Food")]
    [Tooltip("Food each colonist eats per calendar day (day + night). Scaled by the difficulty's food knob.")]
    public float foodPerColonistPerDay = 1f;
    [Tooltip("Days without food before the colony is Hungry (slower work, no arrivals).")]
    public float hungryAfterDays = 0.25f;
    [Tooltip("Days without food before the colony is Starving (someone leaves each day).")]
    public float starvingAfterDays = 1f;

    public enum HungerState { Fed, Hungry, Starving }

    /// <summary>Fires when the colony crosses between Fed / Hungry / Starving. The HUD flashes a banner on it.</summary>
    public static event System.Action<HungerState> OnHungerChanged;
    /// <summary>Fires when a starving colonist walks out on the colony.</summary>
    public static event System.Action OnColonistLeft;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { OnHungerChanged = null; OnColonistLeft = null; }

    private readonly List<IHousing> housing = new List<IHousing>();
    private readonly List<Colonist> roster = new List<Colonist>();
    private float arrivalTimer;
    private float pruneTimer;
    private DayNightCycle dayNight;

    private float foodDebt;          // fractional food owed; a whole unit is taken when it reaches 1
    private float starvedSeconds;    // how long the pool has been empty when a unit came due
    private float nextDepartureAt;   // starvedSeconds at which the next colonist walks out
    private HungerState hungerShown = HungerState.Fed;

    // The scene object predates these fields; a missing key can deserialize as 0,
    // so a non-positive interval falls back to this instead of spawning every frame.
    public const float DefaultArrivalInterval = 20f;
    private const float PruneInterval = 1f;
    public const float DefaultFoodPerDay = 1f;
    public const float DefaultHungryAfterDays = 0.25f;
    public const float DefaultStarvingAfterDays = 1f;
    /// <summary>Gathering and construction speed while Hungry or worse.</summary>
    public const float HungryLaborMultiplier = 0.6f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// The scene normally carries one; if it does not, create it. Assignment and
    /// arrivals both route through the roster now, so a missing manager would make
    /// the campfire panel silently inert. No DontDestroyOnLoad — it holds run state.
    /// </summary>
    public static PopulationManager EnsureExists()
    {
        if (Instance == null)
        {
            Instance = FindAnyObjectByType<PopulationManager>();
            if (Instance == null)
                Instance = new GameObject("PopulationManager").AddComponent<PopulationManager>();
        }
        return Instance;
    }

    void Start()
    {
        dayNight = FindAnyObjectByType<DayNightCycle>();
        arrivalTimer = ArrivalInterval;
    }

    float ArrivalInterval => arrivalInterval > 0f ? arrivalInterval : DefaultArrivalInterval;

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

    /// <summary>
    /// Every registered housing provider, in the order they were built. The HUD's
    /// housing breakdown walks this to show who sleeps where.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<IHousing> HousingProviders => housing;

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
        roster.Add(new Colonist { unit = unit, home = home });
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
    // Arrivals
    // ------------------------------------------------------------------

    /// <summary>Seconds until the next survivor lands, or -1 when nobody is coming (no room, night, no campfire).</summary>
    public float SecondsToNextArrival => ArrivalsOpen() ? Mathf.Max(0f, arrivalTimer) : -1f;

    void Update()
    {
        pruneTimer -= Time.deltaTime;
        if (pruneTimer <= 0f)
        {
            pruneTimer = PruneInterval;
            Prune();
        }

        UpdateFood();

        if (!ArrivalsOpen())
        {
            // Hold the timer while nobody can land, so a colony that just built a
            // hut does not get an instant arrival the moment night ends.
            arrivalTimer = Mathf.Min(arrivalTimer, ArrivalInterval);
            return;
        }

        arrivalTimer -= Time.deltaTime;
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
        if (arriveOnlyByDay && IsNight()) return false;
        if (Campfire() == null) return false;
        if (Hunger != HungerState.Fed) return false;   // word gets round: nobody joins a hungry colony
        return FindHomeWithRoom() != null;
    }

    // ------------------------------------------------------------------
    // Food (2026-09-04)
    // ------------------------------------------------------------------

    float FoodPerDay => foodPerColonistPerDay > 0f ? foodPerColonistPerDay : DefaultFoodPerDay;
    float HungryAfter => hungryAfterDays > 0f ? hungryAfterDays : DefaultHungryAfterDays;
    float StarvingAfter => starvingAfterDays > 0f ? starvingAfterDays : DefaultStarvingAfterDays;

    /// <summary>Seconds in a calendar day at the active difficulty (the "per day" unit).</summary>
    float CycleSeconds
    {
        get
        {
            if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();
            return dayNight != null ? Mathf.Max(1f, dayNight.CycleSeconds) : 150f;
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
            float food = Factions.Player.Resources.food;
            return food / drain;
        }
    }

    /// <summary>Colonists who walked out because the colony starved them. Shown on the end screen.</summary>
    public int ColonistsLeft { get; private set; }

    /// <summary>
    /// What a hungry colony's labor is worth: read at the point of effect by the
    /// gather and construction ticks (the CraftedUpgrades pattern), 1 when fed.
    /// </summary>
    public static float LaborMultiplier =>
        Instance != null && Instance.Hunger != HungerState.Fed ? HungryLaborMultiplier : 1f;

    /// <summary>The balance sim's "nobody eats" switch (a 0 field would fall back to the default).</summary>
    [System.NonSerialized] public bool foodDisabled;

    bool ConsumptionOpen()
    {
        if (foodDisabled) return false;
        if (GameStartController.IntroInProgress) return false;
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return false;
        return Campfire() != null;
    }

    void UpdateFood()
    {
        if (!ConsumptionOpen()) return;
        int eaters = GetColonistCount();   // the player character is not on the roster and does not eat
        if (eaters <= 0) return;

        float cycle = CycleSeconds;
        foodDebt += eaters * FoodPerDay * Difficulty.FoodConsumptionMultiplier * Time.deltaTime / cycle;

        if (foodDebt >= 1f)
        {
            if (Factions.Player.Resources.SpendFood(1))
            {
                foodDebt -= 1f;
                starvedSeconds = 0f;
                nextDepartureAt = StarvingAfter * cycle;
            }
            else
            {
                // Nothing to eat: the debt holds at one unit and the clock runs
                foodDebt = 1f;
                starvedSeconds += Time.deltaTime;
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
        DevQuests.Signal("colonist_left");
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

    bool IsNight()
    {
        if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();
        return dayNight != null && dayNight.IsNightTime();
    }

    static BaseBuilding Campfire()
    {
        var list = BaseBuilding.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            BaseBuilding b = list[i];
            if (b == null || !b.enabled) continue;
            if (b.CachedHealth != null && !b.CachedHealth.IsAlive) continue;
            return b;
        }
        return null;
    }

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
            // One metre east of the cove centre, the same spot the survivor lands on
            pos = TerrainGrid.Instance.CoveCenter + new Vector3(1f, 0f, 0f);
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

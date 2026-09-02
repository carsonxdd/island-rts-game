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

    private readonly List<IHousing> housing = new List<IHousing>();
    private readonly List<Colonist> roster = new List<Colonist>();
    private float arrivalTimer;
    private float pruneTimer;
    private DayNightCycle dayNight;

    // The scene object predates these fields; a missing key can deserialize as 0,
    // so a non-positive interval falls back to this instead of spawning every frame.
    private const float DefaultArrivalInterval = 20f;
    private const float PruneInterval = 1f;

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

    /// <summary>Colonists with no job — the builders, and the pool the campfire panel assigns from.</summary>
    public int GetIdleCount()
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            Worker w = roster[i].unit as Worker;
            if (w != null && !w.hasJob) n++;
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
            if (w == null || w.hasJob) continue;
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
        return FindHomeWithRoom() != null;
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

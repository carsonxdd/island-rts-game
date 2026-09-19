using UnityEngine;

/// <summary>
/// The scene's population tunables and the ticker for every faction's
/// <see cref="Population"/> (2026-09-09, lap step 1 commit 3). Until then this
/// was the singleton that owned the roster; <c>PopulationManager.Instance</c> is
/// now banned (CLAUDE.md) and the roster lives on <see cref="Faction.Population"/>.
/// </summary>
/// <remarks>
/// Keeps its name so the scene object and its serialised values are untouched.
/// The scene object predates the fields, so a non-positive value falls back to the
/// defaults below (a missing YAML key deserializes as 0) — that is why
/// <see cref="Settings"/> resolves them. Assignment, arrivals and recruitment all
/// need the ticker: <see cref="EnsureExists"/> is called from the campfire's and
/// the huts' Awake and creates one when the scene has none. No DontDestroyOnLoad.
/// </remarks>
public class PopulationManager : MonoBehaviour
{
    [Header("Arrivals")]
    [Tooltip("Seconds between survivors coming ashore while there is free housing.")]
    public float arrivalInterval = 20f;
    [Tooltip("Nobody lands at night — the shallows are where the raids come from.")]
    public bool arriveOnlyByDay = true;

    [Header("Food")]
    [Tooltip("Food each colonist eats per calendar day (day + night). Scaled by the difficulty's food knob.")]
    public float foodPerColonistPerDay = 1f;
    [Tooltip("Days without food before the colony is Hungry (slower work, no arrivals).")]
    public float hungryAfterDays = 0.25f;
    [Tooltip("Days without food before the colony is Starving (someone leaves each day).")]
    public float starvingAfterDays = 1f;

    public const float DefaultArrivalInterval = 20f;
    public const float DefaultFoodPerDay = 1f;
    public const float DefaultHungryAfterDays = 0.25f;
    public const float DefaultStarvingAfterDays = 1f;
    /// <summary>Gathering and construction speed while Hungry or worse.</summary>
    public const float HungryLaborMultiplier = 0.6f;

    /// <summary>The resolved tunables every <see cref="Population"/> reads. Defaults when the scene has no component.</summary>
    public struct Tunables
    {
        public float ArrivalInterval;
        public bool ArriveOnlyByDay;
        public float FoodPerDay;
        public float HungryAfterDays;
        public float StarvingAfterDays;
    }

    private static PopulationManager ticker;
    private static DayNightCycle dayNight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ticker = null; dayNight = null; }

    public static Tunables Settings
    {
        get
        {
            PopulationManager pm = ticker;
            Tunables t;
            t.ArrivalInterval = pm != null && pm.arrivalInterval > 0f ? pm.arrivalInterval : DefaultArrivalInterval;
            t.ArriveOnlyByDay = pm == null || pm.arriveOnlyByDay;
            t.FoodPerDay = pm != null && pm.foodPerColonistPerDay > 0f ? pm.foodPerColonistPerDay : DefaultFoodPerDay;
            t.HungryAfterDays = pm != null && pm.hungryAfterDays > 0f ? pm.hungryAfterDays : DefaultHungryAfterDays;
            t.StarvingAfterDays = pm != null && pm.starvingAfterDays > 0f ? pm.starvingAfterDays : DefaultStarvingAfterDays;
            return t;
        }
    }

    /// <summary>The scene's clock, cached for the populations (a destroyed one reads null and is re-fetched).</summary>
    public static DayNightCycle DayNight
    {
        get
        {
            if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();
            return dayNight;
        }
    }

    void Awake()
    {
        if (ticker != null && ticker != this)
        {
            Destroy(gameObject);
            return;
        }
        ticker = this;
    }

    void OnDestroy()
    {
        if (ticker == this) ticker = null;
    }

    /// <summary>
    /// The scene normally carries one; if it does not, create it. The factions'
    /// rosters exist regardless, but without the ticker nobody arrives or eats.
    /// </summary>
    public static PopulationManager EnsureExists()
    {
        if (ticker == null)
        {
            ticker = FindAnyObjectByType<PopulationManager>();
            if (ticker == null)
                ticker = new GameObject("PopulationManager").AddComponent<PopulationManager>();
        }
        return ticker;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var all = Factions.All;
        for (int i = 0; i < all.Count; i++)
        {
            Faction f = all[i];
            if (f.IsRaiders) continue;   // no colony, nobody to house or feed
            f.Population.Tick(dt);
            f.Militia.Tick(dt);   // the levy's alarm rides the same ticker (2026-09-16)
        }
    }
}

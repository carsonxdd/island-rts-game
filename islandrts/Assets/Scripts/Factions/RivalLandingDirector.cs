using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides when a rival colony's ship breaks up and where its survivors come
/// ashore (2026-09-11, lap step 3 slice A). Nothing happens unless the run asked
/// for rivals; the New Game default is none.
/// </summary>
/// <remarks>
/// <para>The arrival is a FRACTION of the calendar, never a constant day: about
/// a third of <c>GameManager.daysToSurvive</c>, so day 10 of 30 and day 7 of 20.
/// A colony that has had a third of its run to itself is standing, which is what
/// makes a neighbour news rather than a second opening.</para>
/// <para>Lives on the GameManager's GameObject, added at runtime by its
/// <c>Awake</c>, so there is nothing to wire in the scene and the code defaults
/// here are the LIVE values - the same contract as <c>RaidDirector</c>. Never put
/// it on a prefab or in a scene: an inspector copy would silently win.</para>
/// <para>Once it stands, <see cref="GovernorRunner"/> adopts it and a
/// <see cref="GovernorPolicy"/> runs it (2026-09-16, slice B) - this class only
/// lands and founds.</para>
/// </remarks>
public class RivalLandingDirector : MonoBehaviour
{
    public static RivalLandingDirector Instance { get; private set; }

    /// <summary>Fraction of the calendar at which the wreck happens. 1/3 of 30 is day 10.</summary>
    public const float ArrivalFraction = 1f / 3f;

    /// <summary>Never before this day, so a very short calendar still gives the player a head start.</summary>
    public const int EarliestArrivalDay = 3;

    /// <summary>Days between one rival's landing and the next, when a run asks for two.</summary>
    public const int DaysBetweenLandings = 4;

    /// <summary>
    /// How far a rival's cove must be from the player's, before
    /// <c>TerrainGrid.SizeScale</c>. Every 150 m-map distance scales with it, or
    /// it is wrong on two of the three island sizes - and a Small island will
    /// then legitimately fail to seat a second colony.
    /// </summary>
    public const float MinCoveSeparation = 55f;

    /// <summary>The colours rivals are drawn in, in registration order.</summary>
    static readonly Color[] RivalColors =
    {
        new Color(0.35f, 0.75f, 0.85f),   // the debug rival's teal
        new Color(0.72f, 0.55f, 0.88f),   // heather
    };

    static readonly string[] RivalNames = { "Driftwood Camp", "Gullrock Camp" };

    /// <summary>A rival's ship has broken up and its colony exists. The HUD banner listens.</summary>
    public static event System.Action<Faction> OnRivalLanded;

    /// <summary>The player's people have seen a rival colonist or building for the first time.</summary>
    public static event System.Action<Faction> OnRivalMet;

    /// <summary>Rivals this run has landed, in landing order. Empty when the option is off.</summary>
    public IReadOnlyList<Faction> Landed => landed;

    /// <summary>Calendar day the first rival came ashore; 0 until one has.</summary>
    public int FirstArrivalDay { get; private set; }

    /// <summary>True once the player's fog has touched any rival's colony.</summary>
    public bool Contacted { get; private set; }

    readonly List<Faction> landed = new List<Faction>(2);
    readonly HashSet<int> met = new HashSet<int>();
    int wanted;
    int nextLandingDay;          // 0 until one has landed; then the earliest day the next may
    DayNightCycle cycle;
    bool landing;
    bool gaveUp;
    float nextContactCheck;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable() { DayNightCycle.OnDayStart += HandleDayStart; }
    void OnDisable() { DayNightCycle.OnDayStart -= HandleDayStart; }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        // GameManager.Start writes daysToSurvive, so the arrival day is computed
        // per dawn rather than cached here: Start order between the two is
        // undefined and a cached day 10 could have been read off the default 30
        // in a 20-day run.
        cycle = FindAnyObjectByType<DayNightCycle>();
        wanted = Mathf.Clamp(IslandOptions.Active.rivalCount, 0, RivalColors.Length);
        if (wanted == 0) enabled = false;   // the default: no polling, no day-hook work
    }

    /// <summary>The calendar day the first ship breaks up on, from the run's own length.</summary>
    public static int ArrivalDay()
    {
        int days = GameManager.Instance != null ? GameManager.Instance.daysToSurvive : Difficulty.DaysToSurvive;
        return Mathf.Max(EarliestArrivalDay, Mathf.RoundToInt(days * ArrivalFraction));
    }

    /// <summary>The calendar, read from the clock rather than GameManager: this handler may run before its.</summary>
    int Today => cycle != null ? cycle.GetCurrentDay() : 1;

    void HandleDayStart()
    {
        if (landing || gaveUp || landed.Count >= wanted) return;
        if (Today < Mathf.Max(ArrivalDay(), nextLandingDay)) return;

        StartCoroutine(LandOne());
    }

    IEnumerator LandOne()
    {
        landing = true;

        TerrainGrid tg = TerrainGrid.Instance;
        BaseBuilding mine = Factions.Player.Campfire;
        if (tg == null || mine == null) { landing = false; yield break; }

        // Far from the player's fire AND from every rival that has already landed,
        // so two rivals do not found on top of each other.
        Vector3 awayFrom = mine.transform.position;
        float minDistance = MinCoveSeparation * TerrainGrid.SizeScale;

        Vector3 cove, site;
        if (!tg.FindShoreSite(awayFrom, minDistance, out cove, out site) || !FarFromLanded(cove, minDistance))
        {
            // A small island with no second shore is not an error, it is the
            // island. One warning, then this run has no more neighbours.
            Debug.LogWarning("RivalLandingDirector: no shore at least " + Mathf.RoundToInt(minDistance)
                + " m from the colony; this island seats no rival.");
            gaveUp = true;
            landing = false;
            yield break;
        }

        int index = landed.Count;
        Faction rival = Factions.Register(RivalNames[index], Faction.Kind.Rival, RivalColors[index]);
        if (rival == null) { gaveUp = true; landing = false; yield break; }

        yield return RivalFounder.Found(rival, site, cove);

        if (rival.Campfire == null)
        {
            // The founding gave up (no prefab, no terrain). Take the faction back
            // out rather than leave an empty colony in the registry.
            Factions.Unregister(rival);
            gaveUp = true;
            landing = false;
            yield break;
        }

        landed.Add(rival);
        if (FirstArrivalDay == 0) FirstArrivalDay = Today;
        nextLandingDay = Today + DaysBetweenLandings;

        DevQuests.Signal("rival:landed");
        if (OnRivalLanded != null) OnRivalLanded(rival);

        landing = false;
    }

    bool FarFromLanded(Vector3 cove, float minDistance)
    {
        float minSqr = minDistance * minDistance;
        for (int i = 0; i < landed.Count; i++)
        {
            BaseBuilding fire = landed[i].Campfire;
            if (fire == null) continue;
            Vector3 d = fire.transform.position - cove;
            d.y = 0f;
            if (d.sqrMagnitude < minSqr) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------
    // First contact
    // ------------------------------------------------------------------
    // Their colony is under the fog like everything else until the player's own
    // people walk far enough to see it. Polled twice a second rather than pushed
    // from the fog, which has no per-object notion of who is looking.

    void Update()
    {
        if (landed.Count == 0 || met.Count >= landed.Count) return;
        if (Time.time < nextContactCheck) return;
        nextContactCheck = Time.time + 0.5f;

        FogOfWar fog = FogOfWar.Instance;
        if (fog == null) return;

        for (int i = 0; i < landed.Count; i++)
        {
            Faction rival = landed[i];
            if (met.Contains(rival.Id)) continue;
            if (!IsSeen(rival, fog)) continue;

            met.Add(rival.Id);
            Diplomacy.MarkKnown(rival);
            Contacted = true;
            DevQuests.Signal("rival:met");
            if (OnRivalMet != null) OnRivalMet(rival);
        }
    }

    static bool IsSeen(Faction rival, FogOfWar fog)
    {
        BaseBuilding fire = rival.Campfire;
        if (fire != null && fog.IsVisible(fire.transform.position)) return true;

        var workers = Worker.ActiveList;
        for (int i = 0; i < workers.Count; i++)
        {
            Worker w = workers[i];
            if (w != null && w.Faction == rival && fog.IsVisible(w.transform.position)) return true;
        }

        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++)
        {
            Warrior w = warriors[i];
            if (w != null && w.Faction == rival && fog.IsVisible(w.transform.position)) return true;
        }

        return false;
    }
}

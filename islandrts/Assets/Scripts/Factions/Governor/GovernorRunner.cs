using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ticks a <see cref="Governor"/> for every rival colony on the island
/// (2026-09-16, lap step 3 slice B, B3 of <c>docs/RIVAL_COLONIES_PLAN.md</c>).
/// Rides the GameManager's GameObject, runtime-added in its <c>Awake</c> the way
/// <see cref="RivalLandingDirector"/> does — public fields are the LIVE values,
/// never put it in a scene.
/// </summary>
/// <remarks>
/// <para>A rival is picked up the moment its campfire stands, whether the
/// landing director founded it on day ten or F4 spawned it: the runner scans
/// <see cref="Factions.All"/> twice a second for a <see cref="Faction.Kind.Rival"/>
/// with a campfire and no governor yet, so neither founding path has to know
/// this class exists. Its personality is <see cref="SimHooks.RivalStrategy"/>
/// when a sweep named one, else <see cref="GovernorPolicy.CreateRandom"/>.</para>
/// <para>Each governor ticks at 1 Hz on its own offset, drawn once when it is
/// created — <b>stagger every per-colony timer</b>, or two colonies place, hire
/// and re-path on the same frame. The player's colony is never governed here:
/// in real play a human runs it, and under the sim <c>SimRunner</c> ticks its
/// own governor beside the character driver.</para>
/// </remarks>
public class GovernorRunner : MonoBehaviour
{
    public static GovernorRunner Instance { get; private set; }

    /// <summary>Seconds between one colony's ticks.</summary>
    public const float TickSeconds = 1f;

    /// <summary>How often the registry is scanned for a colony that needs a governor.</summary>
    public const float ScanSeconds = 0.5f;

    sealed class Entry
    {
        public Governor governor;
        public float nextTick;
    }

    readonly List<Entry> entries = new List<Entry>(2);
    DayNightCycle clock;
    float nextScan;

    /// <summary>The governor running <paramref name="faction"/>, or null (the player's, an ungoverned colony).</summary>
    public Governor For(Faction faction)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].governor.Faction == faction) return entries[i].governor;
        return null;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable() { DayNightCycle.OnDayStart += HandleDayStart; }
    void OnDisable() { DayNightCycle.OnDayStart -= HandleDayStart; }

    /// <summary>
    /// Dawn (2026-09-16, slice B5): every party at sea comes home FIRST, so the
    /// opinion tick never reads a relief party as trespassers, then the day's
    /// drains, gains and attitude flips.
    /// </summary>
    static void HandleDayStart()
    {
        Expedition.EndAll();
        Diplomacy.DawnTick();
    }

    void Start()
    {
        clock = FindAnyObjectByType<DayNightCycle>();
    }

    void Update()
    {
        float now = Time.time;
        if (now >= nextScan)
        {
            nextScan = now + ScanSeconds;
            Adopt();
        }

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (now < e.nextTick) continue;
            e.nextTick = now + TickSeconds;
            e.governor.Tick(clock);
        }
    }

    /// <summary>Give every rival colony that stands a governor, once.</summary>
    void Adopt()
    {
        var all = Factions.All;
        for (int i = 0; i < all.Count; i++)
        {
            Faction f = all[i];
            if (f.Type != Faction.Kind.Rival || f.Campfire == null) continue;
            if (For(f) != null) continue;

            GovernorPolicy policy = SimHooks.Simulating && !string.IsNullOrEmpty(SimHooks.RivalStrategy)
                ? GovernorPolicy.Create(SimHooks.RivalStrategy)
                : GovernorPolicy.CreateRandom();

            entries.Add(new Entry
            {
                governor = new Governor(f, policy),
                nextTick = Time.time + Random.Range(0f, TickSeconds),   // staggered against every other colony
            });
            DevQuests.Signal("rival:governed");
        }
    }
}

using UnityEngine;

/// <summary>
/// Decides whether raiders land tonight and how many (2026-09-02). The run is a
/// calendar now, not a sequence of nightly waves: most nights are quiet, and
/// when a raid does come it is sized by how far into the run the colony is and
/// how much it has built up.
/// </summary>
/// <remarks>
/// The roll happens at DAWN for the coming night, so the warning covers a whole
/// day of preparation. Chance climbs with every quiet night since the last
/// raid, nothing lands before <see cref="firstRaidDay"/>, and a raid is
/// guaranteed after <see cref="maxQuietDays"/> quiet nights — a player can never
/// be lulled for a week and then hit by something they could not have seen.
///
/// Size is fixed at roll time too. Reading prosperity again at dusk would make
/// the walls a player builds during the warning day enlarge the raid they were
/// built against.
///
/// Lives on the EnemySpawner's GameObject, added at runtime by its Awake, so
/// there is nothing to wire in the scene and the code defaults below are the
/// LIVE values (not dead data under a serialized copy). The balance harness
/// writes these fields from its sceneLoaded hook; Difficulty multiplies them at
/// the point of effect. No DontDestroyOnLoad — a restart gets a fresh director.
/// </remarks>
public class RaidDirector : MonoBehaviour
{
    public static RaidDirector Instance { get; private set; }

    // The code defaults, named so the Information screen can quote them on the
    // main menu where no director exists (2026-09-07). The fields below are the
    // live values in a game; the sim writes them.
    public const int DefaultFirstRaidDay = 3;
    public const float DefaultBaseChance = 0.15f;
    public const float DefaultChancePerQuietDay = 0.2f;
    public const int DefaultMaxQuietDays = 5;
    public const int DefaultMinQuietNights = 2;
    public const float DefaultBaseSize = 2f;
    public const float DefaultSizePerDay = 0.4f;
    public const float DefaultSizePerProsperity = 0.08f;

    /// <summary>
    /// Ceiling on what stock on hand contributes to <see cref="Prosperity"/>, per
    /// pool group (2026-09-11): about 600 pooled resources, or 100 metal, is as
    /// rich as a hoard ever reads. Five extra raiders is the most a bank may buy.
    /// </summary>
    public const float MaxHoardProsperity = 10f;
    public const int DefaultMinSize = 2;

    [Header("Schedule")]
    [Tooltip("No raid lands before this day. The first days are for getting the colony standing.")]
    public int firstRaidDay = DefaultFirstRaidDay;
    [Tooltip("Chance of a raid on the first eligible night after a raid (or after firstRaidDay).")]
    public float baseChance = DefaultBaseChance;
    [Tooltip("Added to the chance for every quiet night since the last raid.")]
    public float chancePerQuietDay = DefaultChancePerQuietDay;
    [Tooltip("A raid is guaranteed once this many nights have passed without one.")]
    public int maxQuietDays = DefaultMaxQuietDays;
    [Tooltip("Quiet nights owed after every raid before another can roll. The final night ignores it.")]
    public int minQuietNights = DefaultMinQuietNights;

    [Header("Size")]
    public float baseSize = DefaultBaseSize;
    public float sizePerDay = DefaultSizePerDay;
    public float sizePerProsperity = DefaultSizePerProsperity;
    public int minSize = DefaultMinSize;

    /// <summary>True from the dawn roll until the following dawn when raiders land tonight.</summary>
    public bool RaidTonight { get; private set; }
    /// <summary>How many raiders the roll committed to. Meaningless while <see cref="RaidTonight"/> is false.</summary>
    public int PlannedSize { get; private set; }
    /// <summary>Raids that have actually landed this run.</summary>
    public int RaidsSoFar { get; private set; }
    /// <summary>Calendar day of the last raid that landed; 0 before the first.</summary>
    public int LastRaidDay { get; private set; }
    /// <summary>The day the current roll was made for.</summary>
    public int RolledForDay { get; private set; }
    /// <summary>The prosperity score the last roll read — shown by F4 so a big raid is explicable.</summary>
    public float LastProsperity { get; private set; }

    /// <summary>
    /// Whose shore tonight's raid lands on (2026-09-16, slice B5): rolled with the
    /// raid, weighted by each colony's prosperity, so the richer camp draws the
    /// raiders more often. Null on a quiet night. Before rivals existed every
    /// raid was the player's, and with no rival standing it still is.
    /// </summary>
    public Faction Target { get; private set; }

    /// <summary>The colony the last landed raid went for.</summary>
    public Faction LastRaidTarget { get; private set; }

    /// <summary>Fires after every dawn roll with the result. The HUD's calendar chip and banner listen.</summary>
    public static event System.Action<bool> OnRaidRolled;

    private EnemySpawner spawner;
    private DayNightCycle cycle;

    void Awake()
    {
        Instance = this;
        spawner = GetComponent<EnemySpawner>();
    }

    void OnEnable()
    {
        DayNightCycle.OnDayStart += HandleDayStart;
        DayNightCycle.OnNightStart += HandleNightStart;
    }

    void OnDisable()
    {
        DayNightCycle.OnDayStart -= HandleDayStart;
        DayNightCycle.OnNightStart -= HandleNightStart;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        // Day 1 never gets an OnDayStart (the scene opens mid-morning with the
        // intro holding the clock), so roll for it here. SimRunner's Start runs
        // ahead of this one (-1000), so a sweep's knobs are already in place.
        cycle = FindAnyObjectByType<DayNightCycle>();
        if (cycle != null && !cycle.IsNightTime()) RollForTonight();
    }

    // ---- lurking (2026-09-10) -------------------------------------------
    // Raiders alive, and for LurkSeconds no raider has died, the fire has not
    // been hit and the count has not changed: they are wedged somewhere, or
    // circling. The HUD tells the player to go Offensive; the balance sim does
    // it on its own. One tracker, read by both.

    public const float LurkSeconds = 30f;
    private float lastFightTime;
    private int lastAlive, lastKills;
    private float lastFireHp;
    private bool lurkSignalled;

    /// <summary>True while a raid is on the island but nothing has happened for <see cref="LurkSeconds"/>.</summary>
    public bool RaidLurking { get; private set; }

    void Update()
    {
        int alive = 0;
        bool fighting = false;
        var list = Enemy.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Enemy e = list[i];
            if (e == null || e.CachedHealth == null || !e.CachedHealth.IsAlive) continue;
            alive++;
            if (e.IsFighting) fighting = true;   // a wall being chewed is a fight the fire never feels
        }
        int kills = GameManager.Instance != null ? GameManager.Instance.totalEnemiesKilled : 0;
        BaseBuilding fire = (LastRaidTarget ?? Factions.Player).Campfire;   // the fire the raid is at
        float fireHp = fire != null ? fire.GetCurrentHealth() : 0f;

        if (alive == 0)
        {
            RaidLurking = false;
            lurkSignalled = false;
        }
        else
        {
            if (fighting || lastAlive == 0 || alive != lastAlive || kills != lastKills || fireHp < lastFireHp - 0.01f)
                lastFightTime = Time.time;
            RaidLurking = Time.time - lastFightTime > LurkSeconds;
            if (RaidLurking && !lurkSignalled) { lurkSignalled = true; DevQuests.Signal("raid:lurking"); }
        }
        lastAlive = alive;
        lastKills = kills;
        lastFireHp = fireHp;
    }

    void HandleDayStart()
    {
        if (LastRaidDay > 0 && CurrentDay() - LastRaidDay <= 1 && LastRaidTarget == Factions.Player
            && Factions.Player.Campfire != null) DevQuests.Signal("raid_survived");
        RollForTonight();
    }

    void HandleNightStart()
    {
        if (!RaidTonight) return;
        if (spawner == null) spawner = GetComponent<EnemySpawner>();
        if (spawner == null) return;

        RaidsSoFar++;
        LastRaidDay = CurrentDay();
        LastRaidTarget = Target ?? Factions.Player;
        spawner.SpawnRaid(PlannedSize, RaidsSoFar, LastRaidTarget);
    }

    int CurrentDay()
    {
        if (cycle == null) cycle = FindAnyObjectByType<DayNightCycle>();
        return cycle != null ? cycle.GetCurrentDay() : 1;
    }

    /// <summary>Nights that have passed without a raid, counted from eligibility when none has landed yet.</summary>
    int QuietNights(int day)
    {
        return LastRaidDay == 0 ? day - firstRaidDay : day - LastRaidDay - 1;
    }

    /// <summary>The calendar's last night; victory is the dawn after it.</summary>
    static int FinalDay() => GameManager.Instance != null ? GameManager.Instance.daysToSurvive : 30;

    void RollForTonight()
    {
        int day = CurrentDay();
        RolledForDay = day;

        bool raid;
        if (day < firstRaidDay)
        {
            raid = false;
        }
        else if (LastRaidDay > 0 && QuietNights(day) < minQuietNights && day < FinalDay())
        {
            // The colony is owed a breather (2026-09-10): the third lab lost two
            // runs to a raid landing the night after one that had cost half the
            // army, before a single spear could be made. The last night of the
            // calendar is exempt so the finale can still come.
            raid = false;
            DevQuests.Signal("raid:rested");
        }
        else
        {
            int quiet = Mathf.Max(0, QuietNights(day));
            float chance = (baseChance + chancePerQuietDay * quiet) * Difficulty.RaidFrequencyMultiplier;
            raid = quiet >= maxQuietDays || Random.value < Mathf.Clamp01(chance);
        }

        RaidTonight = raid;
        Target = raid ? ChooseTarget() : null;
        PlannedSize = raid ? ComputeRaidSize(day, Target) : 0;
        if (raid && Target != null && !Target.IsPlayer) DevQuests.Signal("raid:target_rival");
        if (raid && AudioManager.Instance != null) AudioManager.Instance.PlayRaidWarning();
        OnRaidRolled?.Invoke(raid);
    }

    /// <summary>
    /// Raid size for <paramref name="day"/> against the colony as it stands right now.
    /// Also used by the F4 "spawn raid" cheat, so a debug raid is the size a real one would be.
    /// </summary>
    public int ComputeRaidSize(int day) => ComputeRaidSize(day, Factions.Player);

    public int ComputeRaidSize(int day, Faction target)
    {
        LastProsperity = Prosperity(target ?? Factions.Player);
        return SizeFor(day, LastProsperity);
    }

    /// <summary>
    /// The colony tonight's raiders make for: one weighted draw over every colony
    /// with a campfire, weight = its prosperity. A lone colony is always it.
    /// Drawn from <c>UnityEngine.Random</c> like the roll itself, so a seeded run
    /// picks the same shore.
    /// </summary>
    Faction ChooseTarget()
    {
        var all = Factions.All;
        float total = 0f;
        for (int i = 0; i < all.Count; i++)
        {
            Faction f = all[i];
            if (f.IsRaiders || f.Campfire == null) continue;
            total += Mathf.Max(1f, Prosperity(f));
        }
        if (total <= 0f) return Factions.Player;

        float pick = Random.value * total;
        for (int i = 0; i < all.Count; i++)
        {
            Faction f = all[i];
            if (f.IsRaiders || f.Campfire == null) continue;
            pick -= Mathf.Max(1f, Prosperity(f));
            if (pick <= 0f) return f;
        }
        return Factions.Player;
    }

    /// <summary>
    /// What a roll on <paramref name="day"/> would land against the colony as it
    /// stands, without touching <see cref="LastProsperity"/> (2026-09-10). The
    /// balance sim's policies size their army against tomorrow's raid with it,
    /// the way a player reads the day counter and the size of their own colony.
    /// </summary>
    public int EstimateRaidSize(int day) => SizeFor(day, Prosperity());

    /// <summary>The same estimate against <paramref name="colony"/> (a rival's governor sizes its army with it).</summary>
    public int EstimateRaidSize(int day, Faction colony) => SizeFor(day, Prosperity(colony));

    int SizeFor(int day, float prosperity)
    {
        float raw = baseSize + sizePerDay * day + sizePerProsperity * prosperity;
        int count = Mathf.RoundToInt(raw * Difficulty.EnemyCountMultiplier);
        return Mathf.Max(minSize, count);
    }

    public int ComputeRaidSize() => ComputeRaidSize(CurrentDay());

    /// <summary>
    /// How much there is to raid. Every roster member counts (warriors a little
    /// more), buildings by how much they took to put up, and stock on hand —
    /// a colony sitting on a hoard is a fatter target than one that spent it.
    /// </summary>
    public static float Prosperity() => Prosperity(Factions.Player);

    /// <summary>How much there is to raid at <paramref name="me"/>'s colony (2026-09-16: per colony, for the target roll).</summary>
    public static float Prosperity(Faction me)
    {
        float p = 0f;
        if (me == null) return p;

        Population pm = me.Population;
        if (pm != null) p += pm.GetColonistCount() * 2f;

        BaseBuilding fire = me.Campfire;
        if (fire != null) p += fire.GetWarriorCount() * 1f;   // on top of the 2 they count as colonists

        // OWNED, not global (2026-09-11). These read the registries flat, so every
        // hut a RIVAL colony put up was enlarging the player's nightly raid - a
        // difficulty spike the player can neither see nor control, and it grows
        // with the rival. Population and stockpile above were already per-faction;
        // the buildings were the half that was missed.
        p += TargetingUtil.CountOwned(Hut.ActiveList, me) * 3f;
        p += TargetingUtil.CountOwned(Watchtower.ActiveList, me) * 6f;
        p += TargetingUtil.CountOwned(Workshop.ActiveList, me) * 4f;
        p += TargetingUtil.CountOwned(Shipyard.ActiveList, me) * 8f;   // a ship on the slipway is worth raiding (2026-09-04)
        // Walls count HALF of what they used to (0.3 until 2026-09-11). A wall
        // is wood and stone the colony spent, not loot sitting there for the
        // taking, and at 0.3 the 94-wall ring of the 2026-09-11 lab was handing
        // the raiders two extra men a night for the privilege of being defended.
        p += (TargetingUtil.CountOwned(Wall.ActiveList, me) + TargetingUtil.CountOwned(Gate.ActiveList, me)) * 0.15f;

        // The hoard is CAPPED (2026-09-11). It is meant to say "a colony sitting
        // on a pile is a fatter target", but the overnight batch found it saying
        // something else: a colony with more wood than it can spend, which is
        // every colony past day 10, summons raiders forever. Rush reached 3,000
        // wood on day 16 - 50 prosperity, four extra raiders a night - with no
        // building left to buy and no chunks to make a spear with. The stock term
        // now tops out, so a raid grows with what the colony HAS BUILT, which it
        // can defend, and not with what it failed to spend.
        ResourcePool rm = me.Resources;
        {
            float stock = (rm.wood + rm.food + rm.stone) / 60f;
            if (stock > MaxHoardProsperity && me.IsPlayer) DevQuests.Signal("raid:hoard_capped");
            p += Mathf.Min(stock, MaxHoardProsperity);
            p += Mathf.Min(rm.metal / 10f, MaxHoardProsperity);
        }
        return p;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>F4: force or cancel tonight's raid. Recomputes the size for a forced one.</summary>
    public void DebugSetRaidTonight(bool raid)
    {
        RaidTonight = raid;
        Target = raid ? Factions.Player : null;
        PlannedSize = raid ? ComputeRaidSize() : 0;
        OnRaidRolled?.Invoke(raid);
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        OnRaidRolled = null;
    }
}

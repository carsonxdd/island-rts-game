using UnityEngine;

/// <summary>
/// One colony's call to arms (2026-09-16): the alarm, and the levy it raises. A
/// plain class on <see cref="Faction.Militia"/>, ticked by <c>PopulationManager</c>
/// beside the roster.
/// </summary>
/// <remarks>
/// <para>There is no militia role. The <b>levy</b> is whoever can reach a spare
/// weapon: while the <see cref="Alarm"/> is up, every weapon in the stockpile that
/// no full-time warrior holds is claimed by one colonist (<see cref="Worker.levied"/>),
/// who walks HOME (their own hut, else the fire — the weapons are kept in the
/// homes since 2026-09-17, the stockpile is the count), takes it and stands in a
/// warrior body (<see cref="BaseBuilding.MusterLevy"/>). When it has been quiet for
/// <see cref="StandDownSeconds"/> they walk home again, put it back and pick up
/// the job they had (<see cref="BaseBuilding.StandDownLevy"/>). The player's only
/// knob is the stock itself: craft more weapons than you recruit.</para>
/// <para>Claims are handed out here at 2 Hz, jobless colonists first, then the
/// nearest to the fire, a hut sleeper last; never a leaver. A claim is only a
/// promise to walk — the weapon leaves the stockpile at the swap, so a recruit
/// or a second colony's needs can still take it first, and a claimant who finds
/// the rack bare simply goes back to the ladder.</para>
/// <para>The alarm has two sources: the <b>bell</b> (<see cref="Call"/> /
/// <see cref="Dismiss"/>, the player's key and buttons, a governor's call) and a
/// <b>threat at home</b> — a hostile combatant within <see cref="AlarmRadius"/> of
/// the campfire, or (the player's colony only) raiders lurking anywhere on the island.
/// Dismissing the bell clears the stand-down timer, so a colony with no threat goes
/// back to work at once; a threat that ends on its own leaves the timer running so
/// nobody strips their armour between two waves.</para>
/// </remarks>
public sealed class Militia
{
    /// <summary>How near the fire a hostile has to be to raise the levy on its own. The colony's home radius.</summary>
    public const float AlarmRadius = ForageAvailability.HomeRadius;
    /// <summary>Quiet this long after the last alarm before a mustered colonist goes back to work.</summary>
    public const float StandDownSeconds = 30f;
    const float ScanInterval = 0.5f;
    const float JobPenalty = 500f;      // jobless first: a job holder counts as this much farther
    const float SleeperPenalty = 1000f; // a hut sleeper is safe where they are: last out of the door

    /// <summary>The bell rang (true) or was silenced (false) for this colony. The HUD flashes a banner on the player's.</summary>
    public static event System.Action<Faction, bool> OnBell;

    /// <summary>A colonist took up arms (2026-09-17, sim telemetry): the colony, and whether at their own hut (true) or the fire.</summary>
    public static event System.Action<Faction, bool> OnMustered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { OnBell = null; OnMustered = null; }

    /// <summary>The MusterExecutor's last step reports here; the sim reads the walk's length off <see cref="AlarmSince"/>.</summary>
    public static void NoteMustered(Faction f, bool atHome) { OnMustered?.Invoke(f, atHome); }

    /// <summary>When the alarm last went UP (Time.time), for "how long from the alarm to the first spear". NegativeInfinity before any.</summary>
    public float AlarmSince { get; private set; } = float.NegativeInfinity;

    public static readonly string[] BellNames = { "Quiet", "Ring" };

    readonly Faction faction;
    float lastAlarmTime = float.NegativeInfinity;
    float scanTimer;

    public Militia(Faction faction) { this.faction = faction; }

    /// <summary>The bell is ringing: the levy arms and stays armed until it is silenced.</summary>
    public bool Called { get; private set; }

    /// <summary>A hostile combatant inside the home radius (or, for the player, raiders lurking). Refreshed at 2 Hz.</summary>
    public bool ThreatAtHome { get; private set; }

    /// <summary>Bell or threat: the levy musters while this is true.</summary>
    public bool Alarm => Called || ThreatAtHome;

    /// <summary>No alarm for <see cref="StandDownSeconds"/>: a mustered colonist goes back to work.</summary>
    public bool StandDownDue => !Alarm && Time.time - lastAlarmTime >= StandDownSeconds;

    /// <summary>Weapons in the stockpile right now — the levy the next alarm can raise. Refreshed at 2 Hz.</summary>
    public int SpareWeapons { get; private set; }

    /// <summary>Colonists walking to the fire for a weapon right now. Refreshed at 2 Hz.</summary>
    public int Claimed { get; private set; }

    /// <summary>Colonists standing in a warrior body right now. Refreshed at 2 Hz.</summary>
    public int Mustered { get; private set; }

    public void Call()
    {
        if (Called) return;
        if (!Alarm) AlarmSince = Time.time;
        Called = true;
        lastAlarmTime = Time.time;
        if (faction.IsPlayer) DevQuests.Signal("muster:bell");
        OnBell?.Invoke(faction, true);
    }

    public void Dismiss()
    {
        if (!Called) return;
        Called = false;
        lastAlarmTime = float.NegativeInfinity;   // an explicit stand-down waits for nothing
        if (faction.IsPlayer) DevQuests.Signal("muster:dismiss");
        OnBell?.Invoke(faction, false);
    }

    public void Toggle()
    {
        if (Called) Dismiss();
        else Call();
    }

    /// <summary>One frame, from <c>PopulationManager</c>. The scan itself runs every <see cref="ScanInterval"/>.</summary>
    public void Tick(float dt)
    {
        scanTimer -= dt;
        if (scanTimer > 0f) return;
        scanTimer = ScanInterval;
        bool was = Alarm;
        ThreatAtHome = Scan();
        if (Alarm && !was) AlarmSince = Time.time;
        if (Alarm) lastAlarmTime = Time.time;
        Levy();
    }

    bool Scan()
    {
        BaseBuilding fire = faction.Campfire;
        if (fire == null) return false;
        float unused;
        if (TargetingUtil.FindNearestHostileCombatant(fire.transform.position, AlarmRadius, faction, out unused) != null)
            return true;
        // Raiders wedged across the island still keep the player's colony armed; a
        // rival only answers what reaches its own ground (the same split as ColonyState)
        RaidDirector rd = RaidDirector.Instance;
        return faction.IsPlayer && rd != null && rd.RaidLurking;
    }

    /// <summary>
    /// Count the stock, the claims and the mustered; then, under the alarm, hand a
    /// claim per unclaimed spare weapon to the next colonist in line. With no alarm,
    /// take every claim back (a claim whose Muster never entered has no executor
    /// to clear it). No allocation: two passes over the roster.
    /// </summary>
    void Levy()
    {
        BaseBuilding fire = faction.Campfire;
        Population pm = faction.Population;
        SpareWeapons = fire != null ? fire.WeaponsInStock() : 0;
        Claimed = 0;
        Mustered = 0;
        if (pm == null) return;

        var roster = pm.Roster;
        for (int i = 0; i < roster.Count; i++)
        {
            Worker w = roster[i].unit as Worker;
            if (w != null) { if (w.levied) Claimed++; continue; }
            Warrior war = roster[i].unit as Warrior;
            if (war != null && war.levied) Mustered++;
        }

        if (!Alarm || fire == null || !faction.Knowledge.Has(Unlocks.Kind.Militia))
        {
            if (Claimed == 0) return;
            for (int i = 0; i < roster.Count; i++)
            {
                Worker w = roster[i].unit as Worker;
                if (w != null && w.levied) w.SetLevied(false);
            }
            Claimed = 0;
            return;
        }

        Vector3 firePos = fire.transform.position;
        while (Claimed < SpareWeapons)
        {
            Worker best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < roster.Count; i++)
            {
                Worker w = roster[i].unit as Worker;
                if (w == null || w.levied || w.leaving) continue;
                float score = (w.transform.position - firePos).magnitude;
                if (w.hasJob) score += JobPenalty;
                if (w.IsGarrisoned) score += SleeperPenalty;
                if (score < bestScore) { bestScore = score; best = w; }
            }
            if (best == null) break;   // everyone who can walk already has a claim
            best.SetLevied(true);
            Claimed++;
        }
    }
}

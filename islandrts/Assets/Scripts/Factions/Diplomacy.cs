using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the colonies think of each other (2026-09-16, lap step 3 slice B5):
/// a hidden opinion scalar per pair that daily events move, and the thresholds
/// that flip the <see cref="Relations"/> attitude with hysteresis. Static like
/// <see cref="Relations"/>, cleared by <see cref="Factions.ResetAll"/> so no
/// colony state leaks across a scene reload.
/// </summary>
/// <remarks>
/// <para>The numbers are the lap plan's (decided 2026-09-09): −20 when attacked,
/// −5/day for a warrior inside the other's home radius (−2 for a colonist
/// working there — the territory penalty is the opinion hook), +10 per trade,
/// +2/day while Allied, drift toward 0. Hostile below −40, Allied above +40,
/// each needing <see cref="DwellDays"/> dawns of dwell; a relation relaxes back
/// to Neutral the same way from inside <see cref="RelaxMargin"/>.</para>
/// <para>The player sees a WORD (<see cref="Word"/>), never the number; the F3
/// overlay prints the number. Every flip raises <see cref="OnAttitudeChanged"/>
/// for the banner and a <c>diplomacy:</c> signal.</para>
/// <para>Attacks are rate-limited per pair: a fight lands dozens of blows a
/// minute, and −20 each would pin the pair at −100 in seconds. One drain per
/// <see cref="AttackMemorySeconds"/> is "they attacked us", which is the fact.</para>
/// </remarks>
public static class Diplomacy
{
    public const float AttackedDrain = 20f;
    public const float AttackMemorySeconds = 30f;
    public const float WarriorTrespassDrain = 5f;
    public const float ColonistTrespassDrain = 2f;
    public const float AlliedGain = 2f;
    public const float TradeGain = 10f;
    public const float DriftPerDay = 3f;
    public const float HostileBelow = -40f;
    public const float AlliedAbove = 40f;
    /// <summary>A Hostile pair relaxes to Neutral above this, an Allied one below its negative.</summary>
    public const float RelaxMargin = 20f;
    public const int DwellDays = 2;

    // The player's two actions (decided 2026-09-16)
    public const float GiftGain = 15f;
    public const int GiftFood = 30;
    public const int GiftWood = 50;
    public const float WarDrain = 60f;
    public const float WarOthersDrain = 10f;

    const int N = Relations.MaxFactions;
    static readonly float[] opinion = new float[N * N];
    static readonly float[] yesterday = new float[N * N];
    static readonly int[] dwell = new int[N * N];           // consecutive dawns past a threshold, signed by direction
    static readonly float[] lastAttack = new float[N * N];  // Time.time of the last counted attack, −inf when none
    static readonly int[] lastGiftDay = new int[N * N];
    static readonly HashSet<int> known = new HashSet<int>();

    /// <summary>The attitude between two colonies changed: (a, b, now). Banners and signals listen.</summary>
    public static event System.Action<Faction, Faction, Attitude> OnAttitudeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Clear(); OnAttitudeChanged = null; }

    /// <summary>Forget every opinion. Called with <see cref="Relations.Clear"/>.</summary>
    public static void Clear()
    {
        for (int i = 0; i < opinion.Length; i++)
        {
            opinion[i] = 0f; yesterday[i] = 0f; dwell[i] = 0;
            lastAttack[i] = float.NegativeInfinity; lastGiftDay[i] = 0;
        }
        known.Clear();
    }

    static int Key(Faction a, Faction b) => Mathf.Min(a.Id, b.Id) * N + Mathf.Max(a.Id, b.Id);

    static bool Colonies(Faction a, Faction b)
        => a != null && b != null && !ReferenceEquals(a, b) && !a.IsRaiders && !b.IsRaiders;

    public static float Opinion(Faction a, Faction b) => Colonies(a, b) ? opinion[Key(a, b)] : 0f;

    /// <summary>Which way the opinion has moved since yesterday's dawn: −1, 0, +1.</summary>
    public static int Trend(Faction a, Faction b)
    {
        if (!Colonies(a, b)) return 0;
        float d = opinion[Key(a, b)] - yesterday[Key(a, b)];
        return d > 0.5f ? 1 : d < -0.5f ? -1 : 0;
    }

    /// <summary>The word the player sees for an opinion. Never the number.</summary>
    public static string Word(float value)
    {
        if (value <= -50f) return "Wary";
        if (value <= -15f) return "Cool";
        if (value < 15f) return "Neutral";
        if (value < 50f) return "Warm";
        return "Friendly";
    }

    // ---- known ------------------------------------------------------------
    // "Known" = ever seen in the player's fog (the landing director's first
    // contact) or spawned in front of them by F4. The Diplomacy screen lists
    // known colonies only; an unmet neighbour cannot be gifted or declared on.

    public static void MarkKnown(Faction f) { if (f != null && !f.IsPlayer && !f.IsRaiders) known.Add(f.Id); }
    public static bool IsKnown(Faction f) => f != null && known.Contains(f.Id);

    /// <summary>True once any colony other than the player's is known.</summary>
    public static bool AnyKnown => known.Count > 0;

    // ---- events that move opinion ------------------------------------------

    public static void Adjust(Faction a, Faction b, float delta)
    {
        if (!Colonies(a, b) || delta == 0f) return;
        int k = Key(a, b);
        opinion[k] = Mathf.Clamp(opinion[k] + delta, -100f, 100f);
    }

    /// <summary>
    /// <paramref name="attacker"/>'s unit struck <paramref name="victim"/>'s. Called
    /// beside every <c>TakeDamage</c> a unit deals; raiders are ignored (their
    /// relation is fixed). One drain per pair per <see cref="AttackMemorySeconds"/>.
    /// </summary>
    public static void NoteAttack(Faction attacker, Faction victim)
    {
        if (!Colonies(attacker, victim)) return;
        int k = Key(attacker, victim);
        if (Time.time - lastAttack[k] < AttackMemorySeconds) return;
        lastAttack[k] = Time.time;
        Adjust(attacker, victim, -AttackedDrain);
        DevQuests.Signal("diplomacy:attacked");
    }

    /// <summary>A completed trade between two colonies (the dock, a later step).</summary>
    public static void NoteTrade(Faction a, Faction b) => Adjust(a, b, TradeGain);

    // ---- the player's two actions -------------------------------------------

    public enum Gift { Food, Wood }

    /// <summary>Has the player already sent <paramref name="other"/> a gift today?</summary>
    public static bool GiftSentToday(Faction other, int day)
        => Colonies(Factions.Player, other) && lastGiftDay[Key(Factions.Player, other)] == day;

    /// <summary>
    /// Propose peace with a gift from the stockpile: +<see cref="GiftGain"/>, once
    /// a day per colony. A Hostile neighbour whose opinion the gift lifts past the
    /// threshold makes peace AT ONCE — the dwell is for the automatic flips, and a
    /// player who paid for peace should see it. False when unaffordable or sent
    /// already today.
    /// </summary>
    public static bool ProposePeace(Faction other, Gift gift, int day)
    {
        Faction me = Factions.Player;
        if (!Colonies(me, other) || GiftSentToday(other, day)) return false;

        ResourcePool pool = me.Resources;
        bool paid = gift == Gift.Food ? pool.SpendFood(GiftFood) : pool.SpendWood(GiftWood);
        if (!paid) return false;

        int k = Key(me, other);
        lastGiftDay[k] = day;
        Adjust(me, other, GiftGain);
        DevQuests.Signal("diplomacy:peace");

        if (me.Toward(other) == Attitude.Hostile && opinion[k] > HostileBelow)
        {
            dwell[k] = 0;
            Flip(me, other, Attitude.Neutral);
        }
        return true;
    }

    /// <summary>
    /// War, now: Hostile at once, −<see cref="WarDrain"/> with them, and every
    /// OTHER known colony thinks less of the player (−<see cref="WarOthersDrain"/>).
    /// </summary>
    public static void DeclareWar(Faction other)
    {
        Faction me = Factions.Player;
        if (!Colonies(me, other)) return;
        Adjust(me, other, -WarDrain);
        dwell[Key(me, other)] = 0;
        Flip(me, other, Attitude.Hostile);
        DevQuests.Signal("diplomacy:war");

        var all = Factions.All;
        for (int i = 0; i < all.Count; i++)
        {
            Faction f = all[i];
            if (f == other || !Colonies(me, f)) continue;
            Adjust(me, f, -WarOthersDrain);
        }
    }

    /// <summary>
    /// The F4 buttons: set the attitude AND an opinion that agrees with it, so
    /// the next dawn does not flip it straight back.
    /// </summary>
    public static void ForceAttitude(Faction a, Faction b, Attitude attitude)
    {
        if (!Colonies(a, b)) return;
        int k = Key(a, b);
        opinion[k] = attitude == Attitude.Hostile ? -60f : attitude == Attitude.Allied ? 60f : 0f;
        dwell[k] = 0;
        Flip(a, b, attitude);
    }

    static void Flip(Faction a, Faction b, Attitude attitude)
    {
        if (!Relations.Set(a, b, attitude)) return;
        DevQuests.Signal("diplomacy:" + attitude.ToString().ToLowerInvariant());
        OnAttitudeChanged?.Invoke(a, b, attitude);
    }

    // ---- the dawn tick --------------------------------------------------------

    /// <summary>
    /// Once per dawn (<see cref="GovernorRunner"/> drives it): the day's drains
    /// and gains for every colony pair, then the threshold check with dwell.
    /// </summary>
    public static void DawnTick()
    {
        var all = Factions.All;
        for (int i = 0; i < all.Count; i++)
        {
            Faction a = all[i];
            if (a.IsRaiders) continue;
            for (int j = i + 1; j < all.Count; j++)
            {
                Faction b = all[j];
                if (b.IsRaiders) continue;
                TickPair(a, b);
            }
        }
    }

    static void TickPair(Faction a, Faction b)
    {
        int k = Key(a, b);
        yesterday[k] = opinion[k];
        Attitude now = Relations.Get(a, b);

        // Trespass, both ways: my warrior in their patch reads the same as theirs in mine.
        float drain = 0f;
        if (WarriorInside(a, b) || WarriorInside(b, a)) drain += WarriorTrespassDrain;
        if (ColonistInside(a, b) || ColonistInside(b, a)) drain += ColonistTrespassDrain;
        if (drain > 0f) DevQuests.Signal("diplomacy:trespass");

        float v = opinion[k] - drain;
        // An alliance warms by itself; anything else drifts back toward rest (0)
        // on a day nothing pulled the other way. (Drift AND the allied gain
        // together would have every alliance decay a point a day.)
        if (now == Attitude.Allied) v += AlliedGain;
        else if (drain == 0f) v = Mathf.MoveTowards(v, 0f, DriftPerDay);
        opinion[k] = Mathf.Clamp(v, -100f, 100f);

        // Thresholds with dwell: the count runs while the opinion sits past a line
        // and resets the day it does not, so a single bad night never flips a friend.
        Attitude wanted = now;
        if (now != Attitude.Hostile && opinion[k] <= HostileBelow) wanted = Attitude.Hostile;
        else if (now != Attitude.Allied && opinion[k] >= AlliedAbove) wanted = Attitude.Allied;
        else if (now == Attitude.Hostile && opinion[k] > HostileBelow + RelaxMargin) wanted = Attitude.Neutral;
        else if (now == Attitude.Allied && opinion[k] < AlliedAbove - RelaxMargin) wanted = Attitude.Neutral;

        if (wanted == now) { dwell[k] = 0; return; }
        dwell[k]++;
        if (dwell[k] < DwellDays) return;
        dwell[k] = 0;
        Flip(a, b, wanted);
    }

    /// <summary>A warrior of <paramref name="who"/> inside <paramref name="whose"/>'s home radius.</summary>
    static bool WarriorInside(Faction who, Faction whose)
    {
        BaseBuilding fire = whose.Campfire;
        if (fire == null) return false;
        Vector3 at = fire.transform.position;
        float sqr = Territory.HomeRadius * Territory.HomeRadius;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Warrior w = list[i];
            if (w == null || w.Faction != who || w.OnExpedition) continue;   // a party at sea is not a trespass
            Vector3 d = w.transform.position - at;
            d.y = 0f;
            if (d.sqrMagnitude <= sqr) return true;
        }
        return false;
    }

    static bool ColonistInside(Faction who, Faction whose)
    {
        BaseBuilding fire = whose.Campfire;
        if (fire == null) return false;
        Vector3 at = fire.transform.position;
        float sqr = Territory.HomeRadius * Territory.HomeRadius;
        var list = Worker.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Worker w = list[i];
            if (w == null || w.Faction != who) continue;
            Vector3 d = w.transform.position - at;
            d.y = 0f;
            if (d.sqrMagnitude <= sqr) return true;
        }
        return false;
    }
}

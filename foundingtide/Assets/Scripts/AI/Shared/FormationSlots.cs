using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owned formation slots (2026-09-16): a warrior CLAIMS a rank in its colony's
/// formation (per kind: spearmen and archers rank separately) and keeps it —
/// through Intercept, Follow and Engage — until it dies, is dismissed, or the
/// colony changes stance or formation, when every claim of that colony is
/// cleared and the shape re-forms. <see cref="Formation.TrySlot"/> reads the
/// claimed rank, so no two warriors of a colony ever derive the same point,
/// and a rank survives the ActiveList reordering that used to reshuffle a
/// line every time a man died. Patrol never claims (its posts are its own).
/// </summary>
public static class FormationSlots
{
    sealed class Ranks
    {
        public readonly List<Warrior> melee = new List<Warrior>(16);
        public readonly List<Warrior> ranged = new List<Warrior>(16);
    }

    static readonly Dictionary<int, Ranks> byFaction = new Dictionary<int, Ranks>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { byFaction.Clear(); }

    static Ranks For(Faction f)
    {
        Ranks r;
        if (!byFaction.TryGetValue(f.Id, out r)) { r = new Ranks(); byFaction[f.Id] = r; }
        return r;
    }

    /// <summary>The warrior's rank among its kind in its colony, claiming the next free one when it has none. Also returns how many hold a rank of that kind.</summary>
    public static void RankOf(Warrior w, out int rank, out int count)
    {
        rank = 0; count = 0;
        if (w == null || w.Faction == null) return;
        List<Warrior> list = w.IsRanged ? For(w.Faction).ranged : For(w.Faction).melee;
        Purge(list);
        int i = list.IndexOf(w);
        if (i < 0) { list.Add(w); i = list.Count - 1; }
        rank = i;
        count = list.Count;
    }

    /// <summary>How many of the OTHER kind hold a rank in <paramref name="f"/>.</summary>
    public static int CountKind(Faction f, bool ranged)
    {
        if (f == null) return 0;
        List<Warrior> list = ranged ? For(f).ranged : For(f).melee;
        Purge(list);
        return list.Count;
    }

    public static void Release(Warrior w)
    {
        if (w == null || w.Faction == null) return;
        Ranks r;
        if (!byFaction.TryGetValue(w.Faction.Id, out r)) return;
        r.melee.Remove(w);
        r.ranged.Remove(w);
    }

    /// <summary>Every claim of the colony, so the next slot read re-forms the shape.</summary>
    public static void Clear(Faction f)
    {
        Ranks r;
        if (f != null && byFaction.TryGetValue(f.Id, out r)) { r.melee.Clear(); r.ranged.Clear(); }
    }

    static void Purge(List<Warrior> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            Warrior w = list[i];
            Health h = w != null ? w.CachedHealth : null;
            if (w == null || h == null || !h.IsAlive || (w.IsRanged != (list == For(w.Faction).ranged))) list.RemoveAt(i);
        }
    }
}

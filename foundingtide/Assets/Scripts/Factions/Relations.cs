using UnityEngine;

/// <summary>What one faction is to another. Symmetric: A's attitude to B is B's attitude to A.</summary>
public enum Attitude { Hostile = 0, Neutral = 1, Allied = 2 }

/// <summary>
/// The attitude between every pair of factions, a small symmetric matrix over
/// <see cref="Faction.Id"/> (2026-09-09, lap step 1). Read at every site that
/// asks "is this mine / is this a threat" — targeting, damage, flee — so it is
/// one array read and nothing else.
/// </summary>
/// <remarks>
/// <para>The Raiders are hostile to everyone and <see cref="Set"/> refuses to
/// change that; a faction is Allied with itself and that cannot change either.
/// Two colonies start Neutral. The step-3 governor moves attitudes through its
/// opinion scalar with hysteresis; the debug menu flips them directly.</para>
/// <para>Reset with the registry (<see cref="Factions.ResetAll"/>), never on its own.</para>
/// </remarks>
public static class Relations
{
    /// <summary>Hard cap on live factions per world: the matrix is <c>MaxFactions²</c> bytes.</summary>
    public const int MaxFactions = 8;

    static readonly Attitude[] matrix = new Attitude[MaxFactions * MaxFactions];

    /// <summary>Default between two colonies that have not met: Neutral. Raiders always read Hostile.</summary>
    public static Attitude Get(Faction a, Faction b)
    {
        if (a == null || b == null) return Attitude.Neutral;
        if (ReferenceEquals(a, b)) return Attitude.Allied;
        return matrix[a.Id * MaxFactions + b.Id];
    }

    /// <summary>
    /// Sets both directions. Ignored for a self pair and for any pair involving
    /// the Raiders (those are fixed at Allied and Hostile respectively). Fires the
    /// dev-quest signal for the flip. Returns true when the attitude changed.
    /// </summary>
    public static bool Set(Faction a, Faction b, Attitude attitude)
    {
        if (a == null || b == null || ReferenceEquals(a, b)) return false;
        if (a.IsRaiders || b.IsRaiders) return false;

        int ab = a.Id * MaxFactions + b.Id;
        if (matrix[ab] == attitude) return false;
        matrix[ab] = attitude;
        matrix[b.Id * MaxFactions + a.Id] = attitude;
        DevQuests.Signal("faction:" + attitude.ToString().ToLowerInvariant());
        return true;
    }

    /// <summary>Called by <see cref="Factions.Register"/> for a new faction: Neutral to every colony, Hostile to and from the Raiders.</summary>
    internal static void InitRow(Faction f)
    {
        for (int other = 0; other < MaxFactions; other++)
        {
            Attitude a = Attitude.Neutral;
            Faction o = Factions.ById(other);
            if (o != null && (o.IsRaiders || f.IsRaiders)) a = Attitude.Hostile;
            matrix[f.Id * MaxFactions + other] = a;
            matrix[other * MaxFactions + f.Id] = a;
        }
        matrix[f.Id * MaxFactions + f.Id] = Attitude.Allied;
    }

    internal static void Clear()
    {
        for (int i = 0; i < matrix.Length; i++) matrix[i] = Attitude.Neutral;
    }
}

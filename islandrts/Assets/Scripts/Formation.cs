using UnityEngine;

/// <summary>
/// How the militia arranges itself when it holds one point together
/// (2026-09-07): the Intercept rally on Defensive and Offensive, and the escort
/// around the castaway on Follow. One colony-wide static beside
/// <see cref="GuardStance"/>; <see cref="Kind.Auto"/> follows the stance
/// (Defensive = Line, Offensive = Wedge, Follow = Ring) and the Formation
/// stepper on the campfire panel overrides it.
/// </summary>
/// <remarks>
/// <para><b>Line</b>: spearmen in one rank across the facing, archers in a second
/// rank <see cref="RankDepth"/> behind. <b>Wedge</b>: the first spearman on the
/// point, the rest alternating out along the two edges of a V, archers inside the
/// V behind the point. <b>Ring</b>: spearmen evenly around the centre, archers on a
/// tighter inner ring (or the outer one when there are no spearmen). <b>Loose</b>:
/// no slots — executors fall back to their own spread (Intercept's lateral
/// offset, Follow's per-warrior bearing).</para>
/// <para>A warrior's slot is its rank among the living warriors of its kind in
/// <c>Warrior.ActiveList</c> order, so slots are stable while nobody dies and
/// reshuffle once when someone does. Patrol never uses a formation: guard posts
/// are per-warrior by design.</para>
/// </remarks>
public static class Formation
{
    public enum Kind { Auto = 0, Line = 1, Wedge = 2, Ring = 3, Loose = 4 }

    /// <summary>Caption per <see cref="Kind"/>, in enum order (the panel's stepper).</summary>
    public static readonly string[] Names = { "Auto", "Line", "Wedge", "Ring", "Loose" };

    public const float MeleeSpacing = 1.6f;   // shoulder to shoulder in a rank
    public const float RangedSpacing = 2.0f;  // archers need elbow room to loose
    public const float RankDepth = 4f;        // second rank this far behind the first
    public const float WedgeStep = 1.6f;      // out and back per edge slot
    public const float RingRadius = 3.5f;     // spearmen around the centre
    public const float RingInner = 1.8f;      // archers inside them

    public static Kind Active = Kind.Auto;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = Kind.Auto; }

    /// <summary>The one setter every control uses (panel stepper, combat HUD), so the playtest signal fires once per change.</summary>
    public static void Set(Kind kind)
    {
        if (Active == kind) return;
        Active = kind;
        DevQuests.Signal("formation:" + Names[(int)kind].ToLowerInvariant());
    }

    /// <summary>The formation in use: the override, else the stance's own.</summary>
    public static Kind Effective
    {
        get
        {
            if (Active != Kind.Auto) return Active;
            switch (GuardStance.Effective)
            {
                case GuardStance.Mode.Offensive: return Kind.Wedge;
                case GuardStance.Mode.Follow: return Kind.Ring;
                default: return Kind.Line;
            }
        }
    }

    /// <summary>
    /// This warrior's spot in the effective formation around <paramref name="center"/>,
    /// facing <paramref name="facing"/> (toward the enemy; any length, y ignored).
    /// False for Loose, in which case <paramref name="slot"/> is the centre and the
    /// caller uses its own spread. Zero-GC: two passes over Warrior.ActiveList.
    /// </summary>
    public static bool TrySlot(Warrior warrior, Vector3 center, Vector3 facing, out Vector3 slot)
    {
        Kind kind = Effective;
        if (kind == Kind.Loose || warrior == null)
        {
            slot = center;
            return false;
        }

        bool ranged = warrior.IsRanged;
        int rank, count;
        RankOf(warrior, ranged, out rank, out count);
        int others = CountKind(!ranged);

        facing.y = 0f;
        if (facing.sqrMagnitude < 0.001f) facing = Vector3.forward;
        facing.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, facing);

        float x, z;   // lateral (right +), depth (toward the enemy +)
        switch (kind)
        {
            case Kind.Wedge:
                if (!ranged)
                {
                    // Rank 0 is the point; 1, 2 sit one step back on the left and right
                    // edges, 3, 4 two steps back, and so on.
                    int row = (rank + 1) / 2;
                    float side = rank == 0 ? 0f : (rank % 2 == 1 ? -1f : 1f);
                    x = side * row * WedgeStep;
                    z = -row * WedgeStep;
                }
                else
                {
                    // Inside the V behind the point, two abreast, deeper as the V widens
                    int row = rank / 2;
                    x = count > 1 ? (rank % 2 == 0 ? -0.9f : 0.9f) : 0f;
                    z = -(2f + row * WedgeStep);
                }
                break;

            case Kind.Ring:
            {
                // Spearmen on the outer ring, archers inside; archers alone take the ring.
                float radius = !ranged || others == 0 ? RingRadius : RingInner;
                float angle = (count > 0 ? rank * (360f / count) : 0f) * Mathf.Deg2Rad;
                x = Mathf.Sin(angle) * radius;
                z = Mathf.Cos(angle) * radius;
                break;
            }

            default:   // Line
                if (!ranged)
                {
                    x = Centered(rank, count, MeleeSpacing);
                    z = 0f;
                }
                else
                {
                    x = Centered(rank, count, RangedSpacing);
                    z = others > 0 ? -RankDepth : 0f;   // archers alone hold the front rank
                }
                break;
        }

        slot = center + right * x + facing * z;
        return true;
    }

    static float Centered(int i, int n, float spacing) => (i - (n - 1) * 0.5f) * spacing;

    /// <summary>Rank of this warrior among the living warriors of its kind, and how many there are.</summary>
    static void RankOf(Warrior warrior, bool ranged, out int rank, out int count)
    {
        rank = 0;
        count = 0;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Warrior w = list[i];
            if (w == null || w.IsRanged != ranged) continue;
            Health h = w.CachedHealth;
            if (h != null && !h.IsAlive) continue;
            if (w == warrior) rank = count;
            count++;
        }
    }

    static int CountKind(bool ranged)
    {
        int count = 0;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Warrior w = list[i];
            if (w == null || w.IsRanged != ranged) continue;
            Health h = w.CachedHealth;
            if (h != null && !h.IsAlive) continue;
            count++;
        }
        return count;
    }
}

using UnityEngine;

/// <summary>
/// Who owns a thing. A plain C# class, not a MonoBehaviour: it survives without a
/// scene object, it serialises (the save file and the abstract tier of the
/// architecture lap both write it) and it can be built in a test. Step 1 of
/// <c>ARCHITECTURE_LAP_PLAN.md</c> (2026-09-09).
/// </summary>
/// <remarks>
/// <para>The player's colony, each rival colony and the Raiders are all factions;
/// "hostile" is a <see cref="Attitude"/> between two of them (see
/// <see cref="Relations"/>), never a unit type. The Raiders are hostile to
/// everyone and nobody can change it.</para>
/// <para>The colony state that is "mine" today — the resource pool, the roster,
/// unlocks, research, upgrades, priorities, stance, formation, the campfire —
/// moves onto this class one migration commit at a time (the plan's step 1
/// order). Until a piece has moved, its static is still the source of truth and
/// nothing here shadows it.</para>
/// <para><b>Read a faction in <c>Start</c> or later, never in <c>Awake</c>:</b>
/// <c>Instantiate</c> runs <c>Awake</c> before <see cref="Spawn.Owned"/> can set
/// the owner.</para>
/// </remarks>
public sealed class Faction
{
    public enum Kind { Player = 0, Rival = 1, Raiders = 2 }

    /// <summary>Stable small int, an index into <see cref="Relations"/>' matrix. Assigned by <see cref="Factions.Register"/>.</summary>
    public int Id { get; }
    public string Name { get; }
    public Color Color { get; }
    public Kind Type { get; }

    /// <summary>
    /// The colony's living campfire, or null (the intro has none; a destroyed one
    /// is cleared). Assigned by the campfire itself in a later commit of step 1;
    /// until then <c>BaseBuilding.FindAlive()</c> is still the lookup.
    /// </summary>
    public BaseBuilding Campfire { get; set; }

    public bool IsPlayer => Type == Kind.Player;
    public bool IsRaiders => Type == Kind.Raiders;

    internal Faction(int id, string name, Kind type, Color color)
    {
        Id = id;
        Name = name;
        Type = type;
        Color = color;
    }

    /// <summary>What this faction thinks of <paramref name="other"/>. Self is Allied. Zero-cost: one matrix read.</summary>
    public Attitude Toward(Faction other) => Relations.Get(this, other);

    public bool IsHostileTo(Faction other) => Relations.Get(this, other) == Attitude.Hostile;
    public bool IsAlliedWith(Faction other) => Relations.Get(this, other) == Attitude.Allied;

    public override string ToString() => Name + " (#" + Id + ")";
}

using UnityEngine;

/// <summary>
/// Who a colonist is (2026-09-16): a name and one trait. Lives on the roster entry
/// (<see cref="Population.Colonist"/>), so it survives the body swaps of a recruit
/// becoming a warrior and a dismissed warrior becoming a colonist again — the same
/// person, a new body. Rolled once in <see cref="Population.AddColonist"/>.
/// </summary>
/// <remarks>
/// The trait is light mechanics, by decision: it moves only the hours a colonist
/// sleeps (see <c>SleepExecutor</c>) and how long they linger between strolls. Nothing
/// here touches gather rate, courage or appetite. The clock values are fractions of
/// the day parameter (0 = midnight, 0.25 = dawn), so an hour is 1/24.
///
/// Draws from <c>UnityEngine.Random</c> on purpose: the trait decides behaviour, so the
/// balance harness must see the same roll headless and visual (a cosmetic-only roll
/// would use <c>CosmeticRng</c>).
/// </remarks>
public sealed class Persona
{
    public enum Trait { Steady, NightOwl, EarlyRiser, Hardy, Lazy }

    public readonly string Name;
    public readonly Trait Kind;

    /// <summary>Clock value the colonist goes to bed at (0 = midnight).</summary>
    public readonly float Bedtime;
    /// <summary>Clock value the colonist gets up at (0.25 = dawn).</summary>
    public readonly float WakeTime;
    /// <summary>Multiplier on the Idle stroll's standing time (Lazy lingers).</summary>
    public readonly float StandScale;

    const float Hour = 1f / 24f;
    const float Midnight = 0f;
    const float Dawn = 0.25f;

    Persona(string name, Trait kind)
    {
        Name = name;
        Kind = kind;
        StandScale = 1f;
        Bedtime = Midnight;
        WakeTime = Dawn;
        switch (kind)
        {
            case Trait.NightOwl: Bedtime = Midnight + Hour; break;            // turns in at 1 am
            case Trait.EarlyRiser: WakeTime = Dawn - Hour; break;             // up at 5 am
            case Trait.Hardy: Bedtime = Midnight + Hour; WakeTime = Dawn - Hour; break;   // needs four hours
            case Trait.Lazy: WakeTime = Dawn + Hour; StandScale = 1.5f; break; // sleeps in, lingers
        }
    }

    /// <summary>The trait as the player reads it.</summary>
    public string TraitName => TraitTitle(Kind);

    public static string TraitTitle(Trait t)
    {
        switch (t)
        {
            case Trait.NightOwl: return "Night Owl";
            case Trait.EarlyRiser: return "Early Riser";
            case Trait.Hardy: return "Hardy";
            case Trait.Lazy: return "Lazy";
            default: return "Steady";
        }
    }

    /// <summary>Signal slug for the dev quests: <c>trait:night_owl</c>.</summary>
    public string TraitSlug
    {
        get
        {
            switch (Kind)
            {
                case Trait.NightOwl: return "night_owl";
                case Trait.EarlyRiser: return "early_riser";
                case Trait.Hardy: return "hardy";
                case Trait.Lazy: return "lazy";
                default: return "steady";
            }
        }
    }

    /// <summary>
    /// A fresh person for <paramref name="colony"/>: a name nobody living there has
    /// (the pick steps forward through the pool rather than re-rolling, so the draw
    /// count is fixed) and a trait, Steady four times in ten.
    /// </summary>
    public static Persona Roll(Population colony)
    {
        int start = Random.Range(0, Names.Length);
        string name = Names[start];
        for (int i = 0; i < Names.Length; i++)
        {
            string candidate = Names[(start + i) % Names.Length];
            if (colony == null || !colony.NameInUse(candidate)) { name = candidate; break; }
        }

        float r = Random.value;
        Trait kind = r < 0.4f ? Trait.Steady
            : r < 0.55f ? Trait.NightOwl
            : r < 0.7f ? Trait.EarlyRiser
            : r < 0.85f ? Trait.Hardy
            : Trait.Lazy;
        return new Persona(name, kind);
    }

    /// <summary>The persona of any unit on a colony's roster, or null (the castaway, a raider, a unit not yet rostered).</summary>
    public static Persona Of(MonoBehaviour unit, Faction faction)
    {
        if (unit == null || faction == null || faction.Population == null) return null;
        return faction.Population.PersonaOf(unit);
    }

    // Short, mixed, era-neutral: read at a glance over a head in a crowd.
    static readonly string[] Names =
    {
        "Ada", "Bram", "Cass", "Dov", "Elin", "Finn", "Greta", "Hal", "Ines", "Jory",
        "Kit", "Lena", "Mads", "Nell", "Orin", "Pim", "Quill", "Rafe", "Sol", "Tove",
        "Ulf", "Vera", "Wren", "Yael", "Zed", "Abel", "Bea", "Cole", "Dana", "Ewan",
        "Fay", "Gus", "Hild", "Ivo", "Jude", "Kai", "Lior", "Mira", "Nils", "Oda",
        "Piet", "Rosa", "Sten", "Tess", "Uma", "Vik", "Wil", "Yara", "Zora", "Ash",
    };
}

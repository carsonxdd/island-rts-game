using System;
using UnityEngine;

/// <summary>
/// What the colony knows how to do (2026-09-02). Each flag is granted by
/// completing a <see cref="ResearchCatalog"/> entry at a station bench (since
/// 2026-09-03; it used to be the first craft of a tool) — knowledge, not
/// supply: after Woodcutting every colonist may take the Wood job.
///
/// The flags themselves live per faction on <see cref="Knowledge"/> (lap step 1
/// commit 4, 2026-09-09): this class is the enum and its pure helpers only.
/// </summary>
public static class Unlocks
{
    public enum Kind
    {
        WoodJob,
        FoodJob,
        StoneJob,
        MetalJob,
        Construction,
        Militia,
        /// <summary>The Workshop building and (Slice 3) the Crafter job.</summary>
        Crafting,
        /// <summary>Bows and archers (Slice 5).</summary>
        Archery,
        /// <summary>Iron Spears (Slice 3).</summary>
        IronWork,
        /// <summary>The Shipyard and the escape (Slice 6).</summary>
        Shipwright,
    }

    public static readonly int Count = Enum.GetValues(typeof(Kind)).Length;

    /// <summary>The unlock that opens a gathering job.</summary>
    public static Kind ForJob(ResourceNode.ResourceType type)
    {
        switch (type)
        {
            case ResourceNode.ResourceType.Food: return Kind.FoodJob;
            case ResourceNode.ResourceType.Stone: return Kind.StoneJob;
            case ResourceNode.ResourceType.Metal: return Kind.MetalJob;
            default: return Kind.WoodJob;
        }
    }

    /// <summary>Player-facing name of the research that opens <paramref name="kind"/> ("Woodcutting"), for lock hints.</summary>
    public static string ResearchTitleFor(Kind kind)
    {
        return ResearchCatalog.TitleGranting(kind) ?? kind.ToString();
    }
}

using UnityEngine;

/// <summary>What kind of thing an item is — decides where it goes when deposited.</summary>
public enum ItemKind
{
    /// <summary>Crafting material (sticks, stone chunks). Deposits into the campfire stockpile.</summary>
    Material,
    /// <summary>One of the four pooled resources, in hand. Deposits into ResourceManager.</summary>
    Resource,
    /// <summary>A crafted tool. Deposits into the stockpile; can be held.</summary>
    Tool,
}

/// <summary>
/// One entry of the item catalog. Items are the layer above the four pooled
/// resources (2026-09-02): what the player's character carries and what the
/// campfire stockpile holds. A <see cref="ItemKind.Resource"/> item is simply
/// wood / food / stone / metal that has not been deposited yet, so one
/// inventory can carry everything the character picks up.
/// </summary>
public sealed class ItemDef
{
    public readonly string id;
    public readonly string displayName;
    /// <summary>Two or three letters for a HUD slot ("St", "Ck").</summary>
    public readonly string glyph;
    public readonly ItemKind kind;
    public readonly int stackMax;
    /// <summary>Only meaningful for <see cref="ItemKind.Resource"/>.</summary>
    public readonly ResourceNode.ResourceType resourceType;
    public readonly Color color;

    public ItemDef(string id, string displayName, string glyph, ItemKind kind, int stackMax, Color color,
                   ResourceNode.ResourceType resourceType = ResourceNode.ResourceType.Wood)
    {
        this.id = id;
        this.displayName = displayName;
        this.glyph = glyph;
        this.kind = kind;
        this.stackMax = stackMax;
        this.color = color;
        this.resourceType = resourceType;
    }

    public override string ToString() => displayName;
}

/// <summary>
/// Every item in the game, as static definitions (the <c>CraftedUpgrades.Recipes</c>
/// pattern — no ScriptableObjects to churn while the set is still moving).
/// Prefabs reference an item by <see cref="ItemDef.id"/> string; <see cref="Find"/>
/// resolves it.
/// </summary>
public static class ItemCatalog
{
    // Materials
    public static readonly ItemDef Stick = new ItemDef("stick", "Stick", "St", ItemKind.Material, 10,
        new Color(0.66f, 0.48f, 0.30f));
    public static readonly ItemDef StoneChunk = new ItemDef("stone_chunk", "Stone chunk", "Ck", ItemKind.Material, 10,
        new Color(0.62f, 0.65f, 0.68f));

    // Resources in hand
    public static readonly ItemDef Wood = new ItemDef("wood", "Wood", "W", ItemKind.Resource, 10,
        ResourceUI.ColorFor(ResourceNode.ResourceType.Wood), ResourceNode.ResourceType.Wood);
    public static readonly ItemDef Food = new ItemDef("food", "Food", "F", ItemKind.Resource, 10,
        ResourceUI.ColorFor(ResourceNode.ResourceType.Food), ResourceNode.ResourceType.Food);
    public static readonly ItemDef Stone = new ItemDef("stone", "Stone", "S", ItemKind.Resource, 10,
        ResourceUI.ColorFor(ResourceNode.ResourceType.Stone), ResourceNode.ResourceType.Stone);
    public static readonly ItemDef Metal = new ItemDef("metal", "Metal", "M", ItemKind.Resource, 10,
        ResourceUI.ColorFor(ResourceNode.ResourceType.Metal), ResourceNode.ResourceType.Metal);

    // Tools (crafted at the campfire; stay in the character's hands — the
    // knowledge of how to make them is what the colony gains)
    static readonly Color ToolColor = new Color(0.78f, 0.70f, 0.52f);
    public static readonly ItemDef StoneAxe = new ItemDef("stone_axe", "Stone Axe", "Ax", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef FishingSpear = new ItemDef("fishing_spear", "Fishing Spear", "Fs", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef StonePick = new ItemDef("stone_pick", "Stone Pick", "Pk", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef Mallet = new ItemDef("mallet", "Mallet", "Ml", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef WoodenSpear = new ItemDef("wooden_spear", "Wooden Spear", "Sp", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef MetalPick = new ItemDef("metal_pick", "Metal Pick", "Mp", ItemKind.Tool, 1, ToolColor);

    /// <summary>Catalog order — also the display order in the stockpile and HUD.</summary>
    public static readonly ItemDef[] All =
    {
        Stick, StoneChunk,
        Wood, Food, Stone, Metal,
        StoneAxe, FishingSpear, StonePick, Mallet, WoodenSpear, MetalPick,
    };

    /// <summary>Everything that lives in the campfire stockpile (materials; tools stay in hand, resources go to the pool).</summary>
    public static readonly ItemDef[] Stockpiled = { Stick, StoneChunk };

    public static ItemDef Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < All.Length; i++)
            if (All[i].id == id) return All[i];
        return null;
    }

    /// <summary>The in-hand item for a pooled resource type.</summary>
    public static ItemDef ResourceItem(ResourceNode.ResourceType type)
    {
        switch (type)
        {
            case ResourceNode.ResourceType.Food: return Food;
            case ResourceNode.ResourceType.Stone: return Stone;
            case ResourceNode.ResourceType.Metal: return Metal;
            default: return Wood;
        }
    }
}

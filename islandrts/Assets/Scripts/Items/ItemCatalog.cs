using UnityEngine;

/// <summary>What kind of thing an item is — decides where it goes when deposited.</summary>
public enum ItemKind
{
    /// <summary>Crafting material (sticks, stone chunks). Deposits into the campfire stockpile.</summary>
    Material,
    /// <summary>One of the four pooled resources, in hand. Deposits into ResourceManager.</summary>
    Resource,
    /// <summary>A crafted tool for the player's character. Stays in the hands; purely visual.</summary>
    Tool,
    /// <summary>A weapon a unit is armed with (2026-09-03). Lives in the campfire stockpile; recruitment consumes one.</summary>
    Equipment,
}

/// <summary>
/// The combat stats a piece of <see cref="ItemKind.Equipment"/> gives the unit
/// that carries it. Read once in <c>Warrior.Start</c> into the unit's fields
/// (the prefab values become the fallback for an unarmed warrior).
/// </summary>
public sealed class EquipmentDef
{
    public readonly float damage;
    public readonly float range;
    public readonly float attackInterval;
    public readonly bool ranged;

    public EquipmentDef(float damage, float range, float attackInterval, bool ranged)
    {
        this.damage = damage;
        this.range = range;
        this.attackInterval = attackInterval;
        this.ranged = ranged;
    }
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
    /// <summary>Only set for <see cref="ItemKind.Equipment"/>.</summary>
    public readonly EquipmentDef equipment;

    public ItemDef(string id, string displayName, string glyph, ItemKind kind, int stackMax, Color color,
                   ResourceNode.ResourceType resourceType = ResourceNode.ResourceType.Wood,
                   EquipmentDef equipment = null)
    {
        this.id = id;
        this.displayName = displayName;
        this.glyph = glyph;
        this.kind = kind;
        this.stackMax = stackMax;
        this.color = color;
        this.resourceType = resourceType;
        this.equipment = equipment;
    }

    public override string ToString() => displayName;
}

/// <summary>
/// Every item in the game, as static definitions (the <c>CraftedUpgrades</c>
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

    // Tools: the player's own kit, crafted once per run after the matching
    // research. Visual only — what the colony can do is decided by research.
    static readonly Color ToolColor = new Color(0.78f, 0.70f, 0.52f);
    public static readonly ItemDef StoneAxe = new ItemDef("stone_axe", "Stone Axe", "Ax", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef FishingSpear = new ItemDef("fishing_spear", "Fishing Spear", "Fs", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef StonePick = new ItemDef("stone_pick", "Stone Pick", "Pk", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef Mallet = new ItemDef("mallet", "Mallet", "Ml", ItemKind.Tool, 1, ToolColor);
    public static readonly ItemDef MetalPick = new ItemDef("metal_pick", "Metal Pick", "Mp", ItemKind.Tool, 1, ToolColor);

    // Equipment: one per unit, consumed on recruit, returned on dismiss, lost
    // on death. The Wooden Spear's numbers ARE the live warrior stats — the
    // Warrior prefab's serialized damage 25 / range 2 / cooldown 1.2 (the
    // prefab wins over the script defaults; dead-data rule) — so arming a
    // warrior with one changes nothing about how today's warrior fights.
    static readonly Color WeaponColor = new Color(0.85f, 0.62f, 0.45f);
    public static readonly ItemDef WoodenSpear = new ItemDef("wooden_spear", "Wooden Spear", "Sp", ItemKind.Equipment, 5, WeaponColor,
        equipment: new EquipmentDef(damage: 25f, range: 2f, attackInterval: 1.2f, ranged: false));

    /// <summary>Catalog order — also the display order in the stockpile and HUD.</summary>
    public static readonly ItemDef[] All =
    {
        Stick, StoneChunk,
        Wood, Food, Stone, Metal,
        StoneAxe, FishingSpear, StonePick, Mallet, MetalPick,
        WoodenSpear,
    };

    /// <summary>Everything that lives in the campfire stockpile (materials and equipment; tools stay in hand, resources go to the pool).</summary>
    public static readonly ItemDef[] Stockpiled = { Stick, StoneChunk, WoodenSpear };

    /// <summary>Weapons a warrior can be armed with, in preference order (best first once there is more than one).</summary>
    public static readonly ItemDef[] Weapons = { WoodenSpear };

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

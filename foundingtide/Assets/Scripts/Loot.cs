using UnityEngine;

/// <summary>
/// What a colony's hoard does when the PLAYER's warriors put its campfire out
/// (2026-09-16, the conquest test, by decision): the whole pool and the
/// stockpile's materials drop as plain pickups around the dead fire for
/// anyone's haulers, nothing transfers, and a raider-burned fire drops
/// nothing (<see cref="Health.LastHitBy"/> says whose blow it was). Colony
/// state in a static is a leak across the sim's reloads, so
/// <see cref="Factions.ResetAll"/> calls <see cref="Clear"/>.
/// </summary>
public static class Loot
{
    /// <summary>Units per pickup: a barrel's worth of wood, a crate of food, a slab of stone or ore.</summary>
    public const int WoodPerPickup = 15, FoodPerPickup = 12, StonePerPickup = 9, MetalPerPickup = 9;
    /// <summary>Pickups at most from one fire; the rest of a large hoard is lost in the fire.</summary>
    public const int MaxPickups = 40;
    public const float DropRadius = 9f;

    /// <summary>The last conquest this run: whose fire, and what hit the ground (for runs.csv).</summary>
    public static Faction ConqueredFaction { get; private set; }
    public static int DroppedWood { get; private set; }
    public static int DroppedFood { get; private set; }
    public static int DroppedStone { get; private set; }
    public static int DroppedMetal { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Clear(); }

    public static void Clear()
    {
        ConqueredFaction = null;
        DroppedWood = DroppedFood = DroppedStone = DroppedMetal = 0;
    }

    /// <summary>Drop <paramref name="fire"/>'s colony's hoard around it. The pool and the material stacks are emptied whether or not every pickup found ground.</summary>
    public static void Drop(BaseBuilding fire)
    {
        if (fire == null || fire.Faction == null) return;
        Faction f = fire.Faction;
        ResourcePool pool = f.Resources;
        int wood = pool.wood, food = pool.food, stone = pool.stone, metal = pool.metal;

        // Materials in the stockpile ride along as the resource they are worth
        // (a stick is 3 wood, a chunk 3 stone); weapons and tools burn.
        Inventory stock = fire.Stockpile;
        if (stock != null)
        {
            int sticks = stock.Count(ItemCatalog.Stick), chunks = stock.Count(ItemCatalog.StoneChunk);
            wood += sticks * 3;
            stone += chunks * 3;
            if (sticks > 0) stock.Remove(ItemCatalog.Stick, sticks);
            if (chunks > 0) stock.Remove(ItemCatalog.StoneChunk, chunks);
        }
        pool.Set(0, 0, 0, 0);

        ConqueredFaction = f;
        DroppedWood = DroppedFood = DroppedStone = DroppedMetal = 0;
        PickupSpawner spawner = PickupSpawner.Instance;
        if (spawner == null) return;

        Vector3 at = fire.transform.position;
        int placed = 0;
        placed = DropAll(spawner, ResourceNode.ResourceType.Wood, wood, WoodPerPickup, at, placed, v => DroppedWood += v);
        placed = DropAll(spawner, ResourceNode.ResourceType.Food, food, FoodPerPickup, at, placed, v => DroppedFood += v);
        placed = DropAll(spawner, ResourceNode.ResourceType.Stone, stone, StonePerPickup, at, placed, v => DroppedStone += v);
        DropAll(spawner, ResourceNode.ResourceType.Metal, metal, MetalPerPickup, at, placed, v => DroppedMetal += v);

        Debug.Log("Loot: the " + f.Name + "'s fire fell to the " + Factions.Player.Name + " — dropped "
            + DroppedWood + "W " + DroppedFood + "F " + DroppedStone + "S " + DroppedMetal + "M of "
            + wood + "W " + food + "F " + stone + "S " + metal + "M.");
        DevQuests.Signal("loot");
    }

    static int DropAll(PickupSpawner spawner, ResourceNode.ResourceType type, int total, int per, Vector3 at, int placed, System.Action<int> count)
    {
        while (total > 0 && placed < MaxPickups)
        {
            int amount = Mathf.Min(per, total);
            if (!spawner.DropLoot(type, amount, at, DropRadius)) break;
            count(amount);
            total -= amount;
            placed++;
        }
        return placed;
    }
}

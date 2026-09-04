using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A small collectible lying on the ground (stick → wood, stone chunk → stone,
/// washed-up crate → food, barrel → wood). Workers assigned to the matching
/// resource walk over and scoop it up as a quick top-up
/// (CollectPickupExecutor); it grants its amount instantly on collection.
///
/// The player's own character collects the same objects by hand
/// (<see cref="CollectAsItem"/>, 2026-09-02) — but as ITEMS: a stick is a
/// crafting stick, not three wood, and a crate is six food in hand until it is
/// deposited at the fire. <see cref="itemId"/> names the item; when it is empty
/// the pickup is worth its resource type in hand (the salvage case).
///
/// Two sources, and they differ on collection. PickupSpawner owns the sticks
/// and stones and trickle-respawns them, so those report back when taken.
/// Salvage — the shipwreck cargo and the crates and barrels PropScatter leaves
/// along the shore — is finite: placed once, never counted by the spawner,
/// never replaced.
///
/// The only collider is a small sphere on the <see cref="ClickLayer"/>, added
/// at runtime so every source (prefab, scatter, wreck) gets it. That layer is
/// what the player's right-click raycast looks at; nothing else in the game
/// queries it, so pickups still never block pathing, hover, or the
/// Default-layer placement raycasts (a collider on Default would park ghosts
/// on top of sticks).
/// </summary>
public class GroundPickup : MonoBehaviour
{
    public static IReadOnlyList<GroundPickup> ActiveList => ActiveRegistry<GroundPickup>.List;

    /// <summary>Physics layer of the click collider. Named "Pickups" by the session-content setup tool.</summary>
    public const int ClickLayer = 7;
    public const int ClickMask = 1 << ClickLayer;

    [Tooltip("Which worker job collects this pickup (Wood = stick, Stone = stone chunk, Food = crate of supplies).")]
    public ResourceNode.ResourceType resourceType = ResourceNode.ResourceType.Wood;

    [Tooltip("Resource amount granted into the worker's carry on collection.")]
    public int amount = 3;

    [Tooltip("Grant the full amount even past the worker's carry capacity. Salvage is worth more than a "
           + "worker can hold, and a crate that silently evaporated most of its contents would read as a bug.")]
    public bool allowOverfill = false;

    [Header("Player collection")]
    [Tooltip("ItemCatalog id the player's character gets from this (\"stick\", \"stone_chunk\"). Empty = the resource type itself, in hand.")]
    public string itemId = "";
    [Tooltip("How many of the item the character gets when itemId is set. Resource pickups give their amount.")]
    public int itemAmount = 1;

    // Whoever is walking to this pickup (so two collectors never chase one stick).
    // Unity destroyed-object null semantics make a dead claimant read as unclaimed.
    [System.NonSerialized] public MonoBehaviour claimedBy;

    // Set by PickupSpawner on what it places. Salvage leaves it false, so taking a
    // crate can never make the spawner trickle a stick back in its place.
    [System.NonSerialized] public bool spawnerOwned;

    private ItemDef cachedItem;
    private HoverGlow glow;

    /// <summary>What the player's character gets from this pickup.</summary>
    public ItemDef Item
    {
        get
        {
            if (cachedItem == null)
            {
                cachedItem = ItemCatalog.Find(itemId) ?? ItemCatalog.ResourceItem(resourceType);
            }
            return cachedItem;
        }
    }

    /// <summary>How many of <see cref="Item"/> the character gets.</summary>
    public int ItemAmount => Item.kind == ItemKind.Resource ? amount : Mathf.Max(1, itemAmount);

    void Awake()
    {
        ActiveRegistry<GroundPickup>.Register(this);

        gameObject.layer = ClickLayer;
        if (GetComponent<Collider>() == null)
        {
            SphereCollider click = gameObject.AddComponent<SphereCollider>();
            click.radius = 0.5f;
            click.center = new Vector3(0f, 0.25f, 0f);
        }
    }

    void Start()
    {
        // A stick lying in grass at RTS zoom is invisible, so everything the player's
        // character can pick up carries a low constant shimmer and lights up properly
        // under the cursor. Collected here rather than in Awake: scatter and salvage
        // mount their art as a child in the same frame the object is created.
        glow = HoverGlow.Attach(gameObject, RendererTint.Collect(GetComponentsInChildren<Renderer>()), 0.6f, 2.8f);
    }

    void OnMouseEnter() { if (glow != null) glow.SetHovered(true); }
    void OnMouseExit() { if (glow != null) glow.SetHovered(false); }

    void OnDestroy() { ActiveRegistry<GroundPickup>.Unregister(this); }

    public bool IsClaimedByOther(MonoBehaviour who)
    {
        return claimedBy != null && claimedBy != who;
    }

    /// <summary>
    /// Worker collection: grant the amount into the worker's carry and consume
    /// the pickup. Returns the amount actually granted.
    /// </summary>
    /// <remarks>
    /// Overfill is safe for the AI: every consideration that reads the carry
    /// (ResourceCarry, ReturnUrgency) clamps the ratio to 0-1, so a worker
    /// hauling a crate simply reads as full and heads home.
    /// </remarks>
    public float Collect(AIBlackboard bb)
    {
        float space = bb.carryCapacity - bb.carryAmount;
        float granted = allowOverfill ? amount : Mathf.Min(amount, Mathf.Max(0f, space));
        bb.carryType = resourceType;   // delivered as this type (PickupAvailability never mixes types)
        bb.carryAmount += granted;
        bb.worker.carryAmount = bb.carryAmount;

        Consume();
        return granted;
    }

    /// <summary>
    /// Player collection: put the item into <paramref name="inventory"/> and
    /// consume the pickup. Returns how many were taken; 0 means nothing fit
    /// and the pickup is still on the ground.
    /// </summary>
    public int CollectAsItem(Inventory inventory)
    {
        if (inventory == null) return 0;
        int taken = inventory.Add(Item, ItemAmount);
        if (taken <= 0) return 0;

        Consume();
        return taken;
    }

    void Consume()
    {
        if (spawnerOwned && PickupSpawner.Instance != null)
            PickupSpawner.Instance.NotifyCollected(this);

        Destroy(gameObject);
    }
}

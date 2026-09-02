using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A small collectible lying on the ground (stick → wood, stone chunk → stone,
/// washed-up crate → food, barrel → wood). Workers assigned to the matching
/// resource walk over and scoop it up as a quick top-up
/// (CollectPickupExecutor); it grants its amount instantly on collection.
///
/// Two sources, and they differ on collection. PickupSpawner owns the sticks
/// and stones and trickle-respawns them, so those report back when taken.
/// Salvage — the shipwreck cargo and the crates and barrels PropScatter leaves
/// along the shore — is finite: placed once, never counted by the spawner,
/// never replaced.
///
/// No collider by design — pickups are AI targets, not click targets, and must
/// never block pathing or intercept building placement raycasts.
/// </summary>
public class GroundPickup : MonoBehaviour
{
    public static IReadOnlyList<GroundPickup> ActiveList => ActiveRegistry<GroundPickup>.List;

    [Tooltip("Which worker job collects this pickup (Wood = stick, Stone = stone chunk, Food = crate of supplies).")]
    public ResourceNode.ResourceType resourceType = ResourceNode.ResourceType.Wood;

    [Tooltip("Resource amount granted into the worker's carry on collection.")]
    public int amount = 3;

    [Tooltip("Grant the full amount even past the worker's carry capacity. Salvage is worth more than a "
           + "worker can hold, and a crate that silently evaporated most of its contents would read as a bug.")]
    public bool allowOverfill = false;

    // The worker currently walking to this pickup (so two workers never chase one stick).
    // Unity destroyed-object null semantics make a dead claimant read as unclaimed.
    [System.NonSerialized] public Worker claimedBy;

    // Set by PickupSpawner on what it places. Salvage leaves it false, so taking a
    // crate can never make the spawner trickle a stick back in its place.
    [System.NonSerialized] public bool spawnerOwned;

    void Awake() { ActiveRegistry<GroundPickup>.Register(this); }
    void OnDestroy() { ActiveRegistry<GroundPickup>.Unregister(this); }

    public bool IsClaimedByOther(Worker w)
    {
        return claimedBy != null && claimedBy != w;
    }

    /// <summary>
    /// Collect: grant the amount into the worker's carry and consume the pickup.
    /// Returns the amount actually granted.
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

        if (spawnerOwned && PickupSpawner.Instance != null)
            PickupSpawner.Instance.NotifyCollected(this);

        Destroy(gameObject);
        return granted;
    }
}

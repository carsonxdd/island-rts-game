using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A small collectible lying on the ground (stick → wood, stone chunk → stone).
/// Workers assigned to the matching resource walk over and scoop it up as a
/// quick top-up (CollectPickupExecutor); it grants its amount instantly on
/// collection. Spawned and trickle-respawned by PickupSpawner.
///
/// No collider by design — pickups are AI targets, not click targets, and must
/// never block pathing or intercept building placement raycasts.
/// </summary>
public class GroundPickup : MonoBehaviour
{
    public static IReadOnlyList<GroundPickup> ActiveList => ActiveRegistry<GroundPickup>.List;

    [Tooltip("Which worker job collects this pickup (Wood = stick, Stone = stone chunk).")]
    public ResourceNode.ResourceType resourceType = ResourceNode.ResourceType.Wood;

    [Tooltip("Resource amount granted into the worker's carry on collection.")]
    public int amount = 3;

    // The worker currently walking to this pickup (so two workers never chase one stick).
    // Unity destroyed-object null semantics make a dead claimant read as unclaimed.
    [System.NonSerialized] public Worker claimedBy;

    void Awake() { ActiveRegistry<GroundPickup>.Register(this); }
    void OnDestroy() { ActiveRegistry<GroundPickup>.Unregister(this); }

    public bool IsClaimedByOther(Worker w)
    {
        return claimedBy != null && claimedBy != w;
    }

    /// <summary>
    /// Collect: grant the amount into the worker's carry (clamped to capacity)
    /// and consume the pickup. Returns the amount actually granted.
    /// </summary>
    public float Collect(AIBlackboard bb)
    {
        float space = bb.carryCapacity - bb.carryAmount;
        float granted = Mathf.Min(amount, Mathf.Max(0f, space));
        bb.carryAmount += granted;
        bb.worker.carryAmount = bb.carryAmount;

        if (PickupSpawner.Instance != null)
            PickupSpawner.Instance.NotifyCollected(this);

        Destroy(gameObject);
        return granted;
    }
}

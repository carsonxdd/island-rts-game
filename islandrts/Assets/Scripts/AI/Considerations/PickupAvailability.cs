using UnityEngine;

/// <summary>
/// Scores the nearest unclaimed ground pickup matching the worker's assigned
/// resource, and caches it in bb.bestPickup. 1.0 right on top of it, fading to
/// 0 at AttractRange — so pickups only outbid Gather when genuinely close.
/// Food workers always score 0 (no food pickups exist).
/// </summary>
public class PickupAvailability : Consideration
{
    private const float AttractRange = 22f;

    public PickupAvailability(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        bb.bestPickup = null;

        if (bb.assignedResourceType == ResourceNode.ResourceType.Food) return 0f;

        GroundPickup best = null;
        float bestSqr = AttractRange * AttractRange;

        var list = GroundPickup.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            GroundPickup pickup = list[i];
            if (pickup == null) continue;
            if (pickup.resourceType != bb.assignedResourceType) continue;
            if (pickup.IsClaimedByOther(bb.worker)) continue;

            float sqr = (pickup.transform.position - bb.transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = pickup;
            }
        }

        bb.bestPickup = best;
        if (best == null) return 0f;

        return Mathf.Clamp01(1f - Mathf.Sqrt(bestSqr) / AttractRange);
    }
}

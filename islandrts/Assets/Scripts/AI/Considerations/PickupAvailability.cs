using UnityEngine;

/// <summary>
/// Scores the nearest unclaimed ground pickup matching the worker's assigned
/// resource, and caches it in bb.bestPickup. 1.0 right on top of it, fading to
/// 0 at AttractRange — so pickups only outbid Gather when genuinely close.
///
/// Any worker job qualifies: sticks and stones cover wood and stone, and
/// salvage crates put food on the shore. A job with no matching pickup on the
/// island (metal) simply finds nothing and scores 0.
/// </summary>
public class PickupAvailability : Consideration
{
    private const float AttractRange = 22f;

    public PickupAvailability(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        bb.bestPickup = null;

        // Idle colonists build rather than forage, and a worker already carrying a
        // different type (job changed mid-trip) delivers first — never mix types.
        if (!bb.hasJob || (bb.carryAmount > 0.01f && bb.carryType != bb.assignedResourceType))
            return 0f;

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

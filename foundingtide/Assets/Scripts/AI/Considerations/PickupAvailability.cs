using UnityEngine;

/// <summary>
/// Scores the nearest unclaimed ground pickup matching the worker's assigned
/// resource, and caches it in bb.bestPickup. 1.0 right on top of it, fading to
/// 0 at AttractRange — so pickups only outbid Gather when genuinely close.
///
/// Any worker job qualifies: sticks and stones cover wood and stone, and
/// salvage crates put food on the shore. A job with no matching pickup on the
/// island (metal) simply finds nothing and scores 0.
///
/// Territory (2026-09-11, lap step 3): a pickup outside the colony's own patch
/// counts as <see cref="Territory.OutsideHomePenalty"/> metres further away.
/// Since that is far wider than <see cref="AttractRange"/>, the practical effect
/// is that a job worker tidies its own colony's ground and leaves a neighbour's
/// litter alone - which is the rule, stated from the worker's end.
/// </summary>
public class PickupAvailability : Consideration
{
    private const float AttractRange = 22f;

    public PickupAvailability(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        bb.bestPickup = null;

        // Idle colonists build rather than forage, crafters work a bench, and a
        // worker already carrying a different type (job changed mid-trip) delivers
        // first — never mix types.
        if (!bb.hasJob || (bb.carryAmount > 0.01f && bb.carryType != bb.assignedResourceType))
            return 0f;

        GroundPickup best = null;
        float bestDistance = AttractRange;   // linear, because the territory penalty is in metres

        var list = GroundPickup.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            GroundPickup pickup = list[i];
            if (pickup == null) continue;
            if (pickup.resourceType != bb.assignedResourceType) continue;
            if (pickup.IsClaimedByOther(bb.worker)) continue;
            if (bb.IsPickupUnreachable(pickup)) continue;   // dead-ended on it recently (2026-09-09)

            Vector3 pos = pickup.transform.position;
            // Fog of war: the colony only fetches what it has found (2026-09-09).
            if (bb.faction.IsPlayer && FogOfWar.Instance != null && !FogOfWar.Instance.IsExplored(pos)) continue;   // rivals are omniscient

            float sqr = (pos - bb.transform.position).sqrMagnitude;
            if (sqr > bestDistance * bestDistance) continue;   // cheap cull before the territory read

            float distance = Mathf.Sqrt(sqr) + Territory.PenaltyAt(pos, bb.faction);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = pickup;
            }
        }

        bb.bestPickup = best;
        if (best == null) return 0f;

        return Mathf.Clamp01(1f - bestDistance / AttractRange);
    }
}

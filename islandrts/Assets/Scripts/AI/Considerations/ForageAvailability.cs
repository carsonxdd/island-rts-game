using UnityEngine;

/// <summary>
/// Scores the nearest ground pickup an IDLE colonist should fetch, and caches it
/// in bb.bestPickup for <see cref="CollectPickupExecutor"/> (2026-09-03).
///
/// This is the jobless half of <see cref="PickupAvailability"/>, which only ever
/// scores for a colonist with a job and only for that job's resource. Before this,
/// the sticks and chunks lying around the fire in the opening sat there until the
/// player unlocked a job, while the colonists who had already come ashore stood
/// idle beside them.
///
/// Two differences from the job version, both deliberate:
///
/// It takes ANY type — an idle colonist is not specialised, so a stick, a chunk
/// and a salvage crate are all worth the same walk. It still refuses to mix: once
/// their hands hold one type, only that type scores, and ReturnUrgency sends them
/// home to empty out (the same rule a job-changed worker follows).
///
/// It only looks at pickups near the CAMPFIRE, not near itself. The radius covers
/// the ground a colony actually works by day (2026-09-03: 35 -> 70), because the
/// sticks and chunks workers shed at a forest or a quarry away from the fire were
/// otherwise collected by nobody — a job worker only notices a pickup within 22u of
/// itself, so anything shed outside its own errand simply lay there. It shrinks to
/// <see cref="NightRadius"/> after dusk: a colonist with island-wide range is a long
/// way from home when raiders land, and that is the one time it matters where they
/// are standing.
/// </summary>
public class ForageAvailability : Consideration
{
    /// <summary>How far from the campfire a pickup is still the colony's business, by day.</summary>
    public const float HomeRadius = 70f;

    /// <summary>The same radius at night, when standing near the fire is worth more than the stick.</summary>
    public const float NightRadius = 30f;

    /// <summary>Distance from the colonist at which the score has faded to 0.</summary>
    private const float AttractRange = 80f;

    public ForageAvailability(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        bb.bestPickup = null;

        // Job holders forage through PickupAvailability instead.
        if (bb.hasJob) return 0f;

        BaseBuilding fire = BaseBuilding.FindAlive();
        if (fire == null) return 0f;

        Vector3 home = fire.transform.position;
        bool night = AIWorldState.Instance != null && AIWorldState.Instance.isNight;
        float homeRadius = night ? NightRadius : HomeRadius;
        float homeSqr = homeRadius * homeRadius;
        bool carrying = bb.carryAmount > 0.01f;

        GroundPickup best = null;
        float bestSqr = AttractRange * AttractRange;

        var list = GroundPickup.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            GroundPickup pickup = list[i];
            if (pickup == null) continue;
            if (carrying && pickup.resourceType != bb.carryType) continue;
            if (pickup.IsClaimedByOther(bb.worker)) continue;

            Vector3 pos = pickup.transform.position;
            if ((pos - home).sqrMagnitude > homeSqr) continue;

            float sqr = (pos - bb.transform.position).sqrMagnitude;
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

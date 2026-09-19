using UnityEngine;

/// <summary>
/// A colony's patch of the island (2026-09-11, lap step 3 slice A): the ground
/// its people work by preference, and what it costs them to work anyone else's.
/// </summary>
/// <remarks>
/// <para><b>It is a preference, not a claim.</b> A node outside my colony's home
/// radius scores as if it were <see cref="OutsideHomePenalty"/> metres further
/// away. It is never hidden and never rejected - when nothing inside the radius
/// is available, every candidate carries the same penalty and the nearest
/// distant one wins on its own. That is what makes exhaustion handling free, and
/// it is what makes an incursion mean something: a colonist across the line went
/// there because their own patch had nothing, which is exactly the grievance
/// diplomacy should be about (slice B hangs the opinion drain off it).</para>
/// <para>The radius is the one the colony already works by day,
/// <see cref="ForageAvailability.HomeRadius"/>, measured from the colony's OWN
/// campfire (<c>bb.faction.Campfire</c>) - never the player's.</para>
/// <para><b>Where the penalty may be applied.</b> The scans it feeds order their
/// checks cheapest-first and prune against the running best
/// (<c>if (distance >= bestScore) continue;</c>). The penalty is non-negative, so
/// plain distance stays a valid lower bound and the prune stays correct - add it
/// AFTER the prune, never before it, and never make it negative.</para>
/// <para>It applies to every colony including the player's, so the rule reads the
/// same from both sides of the line. A run with no rivals still pays it, which is
/// why <see cref="OutsideHomePenalty"/> is a single tunable constant: the lab
/// measures it, not feel.</para>
/// </remarks>
public static class Territory
{
    /// <summary>How far from its own fire a colony considers the ground its own. The radius Forage already uses by day.</summary>
    public const float HomeRadius = ForageAvailability.HomeRadius;

    /// <summary>
    /// Metres of extra apparent distance on anything outside the home radius.
    /// Starts at 60 - comfortably more than the width of a colony's own patch,
    /// so a home node always beats a foreign one, and small enough that an
    /// exhausted patch sends people out rather than stalling them.
    /// </summary>
    public const float OutsideHomePenalty = 60f;

    static bool crossingSignalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { crossingSignalled = false; }

    /// <summary>
    /// 0 when <paramref name="pos"/> is inside <paramref name="faction"/>'s patch,
    /// <see cref="OutsideHomePenalty"/> when it is outside. A colony with no
    /// campfire has no territory and pays nothing.
    /// </summary>
    public static float PenaltyAt(Vector3 pos, Faction faction)
    {
        if (faction == null) return 0f;
        BaseBuilding fire = faction.Campfire;
        if (fire == null) return 0f;

        Vector3 d = pos - fire.transform.position;
        d.y = 0f;
        return d.sqrMagnitude > HomeRadius * HomeRadius ? OutsideHomePenalty : 0f;
    }

    /// <summary>True when this position is outside the faction's own patch.</summary>
    public static bool IsOutside(Vector3 pos, Faction faction) => PenaltyAt(pos, faction) > 0f;

    /// <summary>
    /// Dev-quest proof that a rival crossed the line: it paid the penalty and
    /// still chose the foreign node, which only happens once its own patch is
    /// empty. Once per launch, no per-scan string hashing.
    /// </summary>
    public static void NoteCrossing(Faction faction)
    {
        if (crossingSignalled || faction == null || faction.IsPlayer) return;
        crossingSignalled = true;
        DevQuests.Signal("rival:territory");
    }
}

using UnityEngine;

/// <summary>
/// The colony-wide order for every warrior (2026-09-07): hold the colony, hunt
/// raiders across the island, or escort the castaway. Per faction
/// (<c>Faction.Stance</c>), read live at the point of decision by the
/// warrior considerations (<see cref="StanceAllows"/>, <see cref="StanceTargetAvailable"/>)
/// and by <c>EngageEnemyExecutor</c>'s target scan, so a change on the campfire
/// panel steers the whole militia at its next brain tick with nothing to push.
/// </summary>
/// <remarks>
/// <para><b>Defensive</b> is the stay-at-home order: Patrol / Intercept / DefendWall
/// as before, and Engage only fights an enemy that is a threat to the colony —
/// within <see cref="HoldRadius"/> of the campfire, heading for a wall, or within
/// <see cref="SelfDefenceRadius"/> of the warrior itself. A raider crossing the
/// island is left to arrive.</para>
/// <para><b>Offensive</b> goes and gets them: Intercept's rally becomes a point
/// advancing on the raiders (<see cref="AdvanceStandoff"/> short of their centroid,
/// in formation), and Engage takes any raider within <see cref="OffensiveEngageRadius"/>
/// of the warrior — plus, in every stance, one near the fire or heading for a
/// wall, because the colony is always defended.</para>
/// <para><b>Follow</b> shadows the player character (<c>FollowPlayerExecutor</c>)
/// and fights only what comes within <see cref="FollowEngageRadius"/> of them or
/// <see cref="SelfDefenceRadius"/> of the warrior. Patrol, Intercept and DefendWall
/// are off; Retreat, Heal and Rearm are not, so a badly hurt escort still walks
/// home. With no character on the field (knocked out, or the intro) the order
/// falls back to Defensive — see <see cref="Effective"/>.</para>
/// <para>The radii are colony-scale, not map-scale, so they deliberately do not
/// follow <c>TerrainGrid.SizeScale</c>.</para>
/// </remarks>
public static class GuardStance
{
    static bool fogSignalled;   // dev quest: once per launch, keeps string hashing out of the scans

    public enum Mode { Defensive = 0, Offensive = 1, Follow = 2 }

    /// <summary>Which warrior actions a stance switches on; see <see cref="Permits"/>.</summary>
    public enum Role { Intercept, DefendWall, Patrol, Follow }

    /// <summary>Caption per <see cref="Mode"/>, in enum order (the panel's stepper).</summary>
    public static readonly string[] Names = { "Defensive", "Offensive", "Follow" };

    /// <summary>Defensive: an enemy this close to the campfire is the colony's business.</summary>
    public const float HoldRadius = 30f;
    /// <summary>Any stance: an enemy this close to the warrior is fought regardless of orders.</summary>
    public const float SelfDefenceRadius = 10f;
    /// <summary>Follow: an enemy this close to the character is fought.</summary>
    public const float FollowEngageRadius = 12f;
    /// <summary>Offensive: the group advances in formation until a raider is this close, then charges.</summary>
    public const float OffensiveEngageRadius = 20f;
    /// <summary>Offensive: the advancing rally sits this far short of the raiders' centroid.</summary>
    public const float AdvanceStandoff = 14f;

    // Archer kiting (2026-09-07), read by EngageEnemyExecutor
    /// <summary>Offensive: an archer backs off when a raider gets inside this, to <see cref="KiteRange"/>.</summary>
    public const float KiteTrigger = 5f;
    public const float KiteRange = 8f;
    /// <summary>Defensive / Follow: an archer takes one <see cref="BackStep"/> when a raider is inside this...</summary>
    public const float HoldTrigger = 4f;
    public const float BackStep = 3f;
    /// <summary>...but never ends up further than this from where it started the fight; then it stands and shoots.</summary>
    public const float HoldLeash = 6f;

    // The stance itself is per faction since lap step 1 commit 4 (2026-09-09):
    // Faction.Stance, set through Faction.SetStance (the one setter every control
    // uses, so the playtest signal fires once per change).

    /// <summary>
    /// The stance the AI actually runs: Follow needs a standing character, and
    /// falls back to Defensive without one so the militia never stands around
    /// waiting for someone who is knocked out.
    /// </summary>
    public static Mode Effective(Faction f)
    {
        Mode m = f != null ? f.Stance : Mode.Defensive;
        if (m != Mode.Follow) return m;
        if (!f.IsPlayer) return Mode.Defensive;   // only the player's colony has a castaway to follow
        PlayerCharacter pc = PlayerCharacter.Instance;
        return pc != null && !pc.IsKnockedOut ? Mode.Follow : Mode.Defensive;
    }

    /// <summary>Whether the effective stance runs this action at all. Zero-cost.</summary>
    public static bool Permits(Role role, Faction f)
    {
        switch (Effective(f))
        {
            case Mode.Offensive:
                return role != Role.Follow;   // Intercept is the advance, see InterceptExecutor
            case Mode.Follow:
                return role == Role.Follow;
            default:
                return role != Role.Follow;
        }
    }

    /// <summary>
    /// Whether a warrior standing at <paramref name="warriorPos"/> may go for this
    /// enemy under the effective stance. The ONE filter behind Engage: the
    /// <see cref="StanceTargetAvailable"/> consideration and the executor's target
    /// scan both call it, so a warrior never fights what the consideration would
    /// not have scored.
    /// </summary>
    public static bool Allows(Enemy enemy, Vector3 warriorPos, BaseBuilding fire, Faction f)
    {
        Vector3 pos = enemy.transform.position;

        // Fog gate (2026-09-09, step 5): a raider nothing of the colony's is looking at
        // is not a target in any stance. This is the ONE filter behind Engage (the
        // consideration and both executor scans), so it is the one place to say so.
        // A raider in melee is inside its warrior's own VisionSource radius, so an
        // engaged target only drops here when it genuinely walks out of sight.
        FogOfWar fog = FogOfWar.Instance;
        if (fog != null && !fog.IsVisible(pos))
        {
            if (!fogSignalled) { fogSignalled = true; DevQuests.Signal("fog:raider_unseen"); }
            return false;
        }

        switch (Effective(f))
        {
            case Mode.Offensive:
                if ((pos - warriorPos).sqrMagnitude <= OffensiveEngageRadius * OffensiveEngageRadius) return true;
                return ThreatensColony(enemy, pos, fire);

            case Mode.Follow:
            {
                if ((pos - warriorPos).sqrMagnitude <= SelfDefenceRadius * SelfDefenceRadius) return true;
                PlayerCharacter pc = PlayerCharacter.Instance;
                return pc != null
                    && (pos - pc.transform.position).sqrMagnitude <= FollowEngageRadius * FollowEngageRadius;
            }

            default:
                if ((pos - warriorPos).sqrMagnitude <= SelfDefenceRadius * SelfDefenceRadius) return true;
                return ThreatensColony(enemy, pos, fire);
        }
    }

    /// <summary>Near the fire, or walking at a wall: the colony's business in any stance but Follow.</summary>
    static bool ThreatensColony(Enemy enemy, Vector3 pos, BaseBuilding fire)
    {
        if (fire == null) fire = BaseBuilding.FindAlive();
        if (fire == null) return true;   // no colony to hold — fight what is there
        if ((pos - fire.transform.position).sqrMagnitude <= HoldRadius * HoldRadius) return true;
        return enemy.IsHeadingForWall();
    }
}

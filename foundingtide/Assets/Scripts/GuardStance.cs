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
/// as before, and Engage only fights an enemy that reaches the LINE (2026-09-17):
/// once Intercept has given the warrior a post (<c>bb.holdPost</c> — a slot in the
/// line inside the gate the raiders are coming at, see <see cref="HoldLine"/>, or on
/// the perimeter without one) a raider is fought within <see cref="LineReach"/> of
/// that post (plus the bow's reach for an archer), or once it is nearer the fire
/// than the post is — and a spearman never takes one outside the wall the line
/// holds. A target that walks back out of reach is dropped and the warrior returns
/// to the post. Before a post exists the old rule stands: within <see cref="HoldRadius"/>
/// of the campfire or heading for a wall. <see cref="SelfDefenceRadius"/> applies in
/// every stance — inside the wall, for a spearman on a gate line.</para>
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

    /// <summary>Defensive with no post yet: an enemy this close to the campfire is the colony's business.</summary>
    public const float HoldRadius = 30f;
    /// <summary>Defensive: a raider this close to the warrior's post is fought (an archer adds its bow's reach).</summary>
    public const float LineReach = 8f;
    /// <summary>Defensive: nearer the fire than the post by this much counts as through the line.</summary>
    public const float BreachSlack = 2f;
    /// <summary>Defensive gate line: this far past the gate's radius still counts as in the gateway (a raider chewing the gate).</summary>
    public const float GateSlack = 1.5f;
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
    public static bool Allows(ITargetable target, Vector3 warriorPos, BaseBuilding fire, Faction f)
        => Allows(target, warriorPos, fire, f, null);

    /// <summary>
    /// <see cref="Allows(ITargetable, Vector3, BaseBuilding, Faction)"/> for a warrior
    /// with a blackboard: under Defensive its held post decides reach (2026-09-17).
    /// </summary>
    public static bool Allows(ITargetable target, AIBlackboard bb)
        => Allows(target, bb.transform.position, bb.baseBuilding, bb.faction, bb);

    static bool Allows(ITargetable target, Vector3 warriorPos, BaseBuilding fire, Faction f, AIBlackboard bb)
    {
        Vector3 pos = target.transform.position;

        // Fog gate (2026-09-09, step 5): a raider nothing of the colony's is looking at
        // is not a target in any stance. This is the ONE filter behind Engage (the
        // consideration and both executor scans), so it is the one place to say so.
        // A raider in melee is inside its warrior's own VisionSource radius, so an
        // engaged target only drops here when it genuinely walks out of sight.
        // The player's warriors only: a rival colony is omniscient by decision (lap step 1).
        FogOfWar fog = f.IsPlayer ? FogOfWar.Instance : null;
        if (fog != null && !fog.IsVisible(pos))
        {
            if (!fogSignalled) { fogSignalled = true; DevQuests.Signal("fog:raider_unseen"); }
            return false;
        }

        switch (Effective(f))
        {
            case Mode.Offensive:
                if ((pos - warriorPos).sqrMagnitude <= OffensiveEngageRadius * OffensiveEngageRadius) return true;
                return ThreatensColony(target, pos, fire, f);

            case Mode.Follow:
            {
                if ((pos - warriorPos).sqrMagnitude <= SelfDefenceRadius * SelfDefenceRadius) return true;
                PlayerCharacter pc = PlayerCharacter.Instance;
                return pc != null
                    && (pos - pc.transform.position).sqrMagnitude <= FollowEngageRadius * FollowEngageRadius;
            }

            default:
            {
                BaseBuilding at = fire != null ? fire : f.Campfire;
                bool hasPost = bb != null && bb.hasHoldPost;
                Vector3 post = hasPost ? bb.holdPost : Vector3.zero;
                float lineRadius = hasPost ? bb.holdLineRadius : float.MaxValue;
                // No post of its own yet but the colony holds a gate (2026-09-17, the line
                // check): the line's centre stands in for it. A levy body is NEW every
                // night and its first tick came after the raiders were inside HoldRadius,
                // so the old rule sent each one charging out through the gate alone —
                // the check tripled Turtle's levy losses (2.4 → 5.3 a raid night).
                if (!hasPost && bb != null && at != null)
                {
                    Gate held = HoldLine.CurrentGate(f);
                    if (held != null)
                    {
                        Vector3 facing;
                        HoldLine.LineAt(held, at, out post, out facing, out lineRadius);
                        hasPost = true;
                    }
                }
                // A gate line: a spearman never goes for what is outside the wall — the
                // path out is through the gate, and that is the "Defensive turns Offensive
                // when they arrive" the line exists to stop. An archer shoots over it.
                if (hasPost && !bb.isRanged && lineRadius < float.MaxValue && at != null)
                {
                    Vector3 d = pos - at.transform.position;
                    d.y = 0f;
                    float limit = lineRadius + GateSlack;
                    if (d.sqrMagnitude > limit * limit) return false;
                }
                if ((pos - warriorPos).sqrMagnitude <= SelfDefenceRadius * SelfDefenceRadius) return true;
                if (hasPost) return ReachesLine(pos, at, post, bb);
                return ThreatensColony(target, pos, fire, f);
            }
        }
    }

    /// <summary>Within reach of <paramref name="post"/>, or already nearer the fire than the post is (through the line).</summary>
    static bool ReachesLine(Vector3 pos, BaseBuilding at, Vector3 post, AIBlackboard bb)
    {
        float reach = LineReach + (bb.isRanged ? bb.attackRange : 0f);
        Vector3 toPost = pos - post;
        toPost.y = 0f;
        if (toPost.sqrMagnitude <= reach * reach) return true;

        if (at == null) return true;   // no colony to hold — fight what is there
        Vector3 firePos = at.transform.position;
        Vector3 postFromFire = post - firePos;
        Vector3 targetFromFire = pos - firePos;
        postFromFire.y = 0f;
        targetFromFire.y = 0f;
        float inside = postFromFire.magnitude + BreachSlack;
        return targetFromFire.sqrMagnitude <= inside * inside;
    }

    /// <summary>Near the fire, or walking at a wall: the colony's business in any stance but Follow.</summary>
    static bool ThreatensColony(ITargetable target, Vector3 pos, BaseBuilding fire, Faction f)
    {
        if (fire == null) fire = f.Campfire;
        if (fire == null) return true;   // no colony to hold — fight what is there
        if ((pos - fire.transform.position).sqrMagnitude <= HoldRadius * HoldRadius) return true;
        Enemy raider = target as Enemy;
        return raider != null && raider.IsHeadingForWall();
    }
}

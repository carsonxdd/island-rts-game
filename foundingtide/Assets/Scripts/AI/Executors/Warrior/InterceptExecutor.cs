using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Warrior executor: Form up at colony perimeter facing approaching enemies.
/// Warriors rally at the building edge between base and enemy cluster,
/// waiting for enemies to close in before the brain switches to EngageEnemy.
/// Produces natural grouping behavior — all warriors converge to the same intercept point.
/// 2026-09-07: each warrior takes its Formation slot around that point (Line /
/// Wedge / Ring), and on Offensive the point advances on the raiders instead.
/// 2026-09-17: on Defensive the line forms at the gate the raiders are coming at
/// (<see cref="HoldLine"/>, one choice per colony with hysteresis), and the
/// warrior's slot becomes its POST (<c>bb.holdPost</c>) — GuardStance.Allows fights
/// only what reaches it, so the line holds instead of charging the first raider
/// that crosses HoldRadius.
/// 2026-09-28: the spearmen stand OUTSIDE the held gate facing the threat (two
/// ranks while fewer than <see cref="Formation.TwoRankBelow"/>), the archers
/// inside the wall behind it. The same executor runs the dusk FORM-UP (the
/// warrior's "FormUp" action, a twin instance): before any raider is ashore the
/// threat is the published landing point, so the line is standing when they land.
/// The slot is re-derived at once when someone joins or leaves the formation
/// (<see cref="FormationSlots.Version"/>), so a line closes a dead man's gap.
/// Phase 6.25: TrySetDestination's return is honored — a rejected set retries
/// next frame instead of leaving the warrior standing until the next recalc tick.
/// </summary>
public class InterceptExecutor : ActionExecutor
{
    public override string DisplayName => formUp ? "Forming up" : "Intercepting";

    private const float RecalcInterval = 2f;       // rally re-derived this often as enemies move
    private const float ArriveRadius = 1.5f;       // standing in the slot
    private const float RepathDrift = 1.2f;        // a slot that moved this far gets a new path (a re-centred line moves ~0.8)
    private const float SlotStoppingDistance = 0.5f;
    private const float GateSnapRadius = 3f;       // a gate-line slot snaps this far at most (5 could land on the wrong side of the wall)

    private readonly bool formUp;
    private Vector3 rallyPoint;
    private Vector3 rallyFacing = Vector3.forward;
    private float recalcTimer;
    private bool rallySet;
    private bool arrived;
    private int slotVersion;
    private float originalStoppingDistance;
    private bool stoppingSwapped;

    // Spread warriors slightly so they don't all stack on the exact same spot (Loose formation)
    private readonly float spreadOffset;

    public InterceptExecutor()
    {
        // Each instance gets a random lateral offset for formation spread
        spreadOffset = Random.Range(-3f, 3f);
    }

    InterceptExecutor(bool formUp, float spreadOffset)
    {
        this.formUp = formUp;
        this.spreadOffset = spreadOffset;
    }

    /// <summary>
    /// The dusk form-up's executor (2026-09-28): the same rally with the same spread,
    /// so it draws nothing from the seeded stream and a warrior switching from
    /// forming up to intercepting keeps its spot.
    /// </summary>
    public InterceptExecutor FormUpTwin() => new InterceptExecutor(true, spreadOffset);

    public override void OnEnter(AIBlackboard bb)
    {
        if (bb.agent != null && bb.agent.isOnNavMesh && !stoppingSwapped)
        {
            // The default (attackRange - 1: 8 for a bow) parks an archer a reach short of its slot
            originalStoppingDistance = bb.agent.stoppingDistance;
            bb.agent.stoppingDistance = SlotStoppingDistance;
            stoppingSwapped = true;
        }
        arrived = false;
        recalcTimer = Random.Range(0f, RecalcInterval);   // staggered: a whole line never re-derives in one frame
        slotVersion = FormationSlots.Version(bb.faction);
        CalculateRallyPoint(bb);
        MoveToRally(bb);
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        // Periodically recalculate as enemies move, and at once when the formation changed
        recalcTimer += Time.deltaTime;
        int version = FormationSlots.Version(bb.faction);
        if (recalcTimer >= RecalcInterval || version != slotVersion)
        {
            recalcTimer = 0f;
            slotVersion = version;
            CalculateRallyPoint(bb);

            // Only re-path if the slot moved noticeably
            Vector3 drift = rallyPoint - bb.agent.destination;
            drift.y = 0f;
            if (drift.sqrMagnitude > RepathDrift * RepathDrift) MoveToRally(bb);
        }

        // Once in the slot, stop and face the enemy (or the landing, before they are ashore)
        Vector3 toRally = rallyPoint - bb.transform.position;
        toRally.y = 0f;
        if (toRally.sqrMagnitude < ArriveRadius * ArriveRadius)
        {
            bb.agent.isStopped = true;
            if (!arrived)
            {
                arrived = true;
                if (formUp && bb.faction != null && bb.faction.IsPlayer) DevQuests.Signal("raid:dusk_formed");
            }

            Vector3 lookDir = rallyFacing;
            if (bb.nearestEnemy != null) lookDir = bb.nearestEnemy.position - bb.transform.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                bb.transform.rotation = Quaternion.Slerp(
                    bb.transform.rotation,
                    Quaternion.LookRotation(lookDir.normalized),
                    Time.deltaTime * 3f);
            }
        }
        else
        {
            arrived = false;
            bb.agent.isStopped = false;

            // Retry if the throttle/NavMesh rejected the rally destination earlier
            if (!rallySet) MoveToRally(bb);

            // Stuck resolution while walking to the slot. A reset ResetPaths the agent
            // and nulls blackboard fields: bail, and re-issue next tick.
            if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            {
                rallySet = false;
                return;
            }
        }
    }

    void CalculateRallyPoint(AIBlackboard bb)
    {
        Faction f = bb.faction;
        BaseBuilding fire = bb.baseBuilding;

        // The threat: the hostile centroid (every living fighter hostile to this colony),
        // else — the dusk form-up — where tonight's raid will land on this colony's shore
        int enemyCount;
        Vector3 threat = TargetingUtil.HostileCentroid(f, out enemyCount);
        if (enemyCount > 0)
        {
            threat /= enemyCount;
        }
        else
        {
            RaidDirector rd = RaidDirector.Instance;
            if (fire == null || rd == null || !rd.LandingFor(f, out threat))
            {
                // Nothing to face: stay near base
                rallyPoint = fire != null ? fire.transform.position : bb.transform.position;
                return;
            }
        }
        if (fire == null)
        {
            rallyPoint = bb.transform.position;
            return;
        }

        // Direction from base toward the threat
        Vector3 basePos = fire.transform.position;
        Vector3 dirToEnemies = threat - basePos;
        dirToEnemies.y = 0f;
        dirToEnemies = dirToEnemies.sqrMagnitude > 0.001f ? dirToEnemies.normalized : Vector3.forward;
        rallyFacing = dirToEnemies;

        // Offensive (2026-09-07): the rally is not a line to hold but the group
        // advancing on the raiders — a point AdvanceStandoff short of their
        // centroid, on the colony side, recomputed as they move. Engage takes
        // over for each warrior once a raider is inside OffensiveEngageRadius, so
        // the wedge walks up as one body and breaks into the charge together.
        if (enemyCount > 0 && GuardStance.Effective(f) == GuardStance.Mode.Offensive)
        {
            Vector3 advance = threat - dirToEnemies * GuardStance.AdvanceStandoff;
            DevQuests.Signal("intercept:advance");
            PlaceRally(bb, advance, dirToEnemies, basePos, false, Vector3.zero, 5f);
            bb.hasHoldPost = false;
            return;
        }
        if (f.FormationKind == Formation.Kind.Auto && Formation.Effective(f) == Formation.Kind.Line)
            DevQuests.Signal("formation:auto_line");   // Defensive rally under Auto resolved to a Line

        // A gate to hold: the spearmen OUTSIDE it facing the threat, the archers
        // inside the wall behind it (2026-09-28); every warrior of the colony reads
        // the same gate. The raiders' own targeting drives them onto a gate, so
        // this is where the fight is.
        Vector3 spear, archer, facing;
        float gateRadius;
        Gate held = HoldLine.Plan(f, threat, fire, out spear, out archer, out facing, out gateRadius);
        if (held != null)
        {
            DevQuests.Signal("hold:gate");
            rallyFacing = facing;
            PlaceRally(bb, spear, facing, basePos, true, archer, GateSnapRadius);
            SetPost(bb, gateRadius);
            return;
        }

        // Colony perimeter with no walls: well clear of the fire, so a Line's second
        // rank (4 u behind the centre) still leaves the delivery edge open
        float perimeterRadius = fire.noBuildRadius + 8f;

        // Check for walls — if walls exist, position just inside the wall line
        if (Wall.ActiveList.Count > 0 || Gate.ActiveList.Count > 0)
        {
            // Find the furthest wall in the enemy direction as the defense line
            float furthestWallDist = 0f;
            for (int i = 0; i < Wall.ActiveList.Count; i++)
            {
                Wall wall = Wall.ActiveList[i];
                if (wall == null || wall.Faction != f) continue;   // this colony's line, not a rival's

                // Only consider walls roughly in the enemy direction
                Vector3 wallDir = (wall.transform.position - basePos).normalized;
                if (Vector3.Dot(wallDir, dirToEnemies) > 0.3f)
                {
                    float dist = Vector3.Distance(basePos, wall.transform.position);
                    if (dist > furthestWallDist)
                        furthestWallDist = dist;
                }
            }

            if (furthestWallDist > 0f)
            {
                // Position just inside the wall line (2 units interior)
                perimeterRadius = furthestWallDist - 2f;
            }
        }

        // Rally point: base + direction_to_enemies * perimeterRadius
        PlaceRally(bb, basePos + dirToEnemies * perimeterRadius, dirToEnemies, basePos, false, Vector3.zero, 5f);
        SetPost(bb, float.MaxValue);
    }

    /// <summary>
    /// The rally just placed is this warrior's post for the stance to read
    /// (2026-09-17). Defensive only: Offensive's advancing rally is not a line, and
    /// a post written under it would leash the charge when the orders change back.
    /// </summary>
    void SetPost(AIBlackboard bb, float lineRadius)
    {
        if (GuardStance.Effective(bb.faction) != GuardStance.Mode.Defensive) { bb.hasHoldPost = false; return; }
        bb.holdPost = rallyPoint;
        bb.holdLineRadius = lineRadius;
        if (!bb.hasHoldPost) DevQuests.Signal("hold:post");
        bb.hasHoldPost = true;
    }

    /// <summary>
    /// Turn the group's rally centre into this warrior's own spot: its
    /// <see cref="Formation"/> slot facing the enemy, or the old lateral spread
    /// when the formation is Loose. Snapped to the NavMesh; falls back halfway
    /// to the base, then to the base itself.
    /// </summary>
    void PlaceRally(AIBlackboard bb, Vector3 center, Vector3 dirToEnemies, Vector3 basePos,
                    bool hasArcherCentre, Vector3 archerCentre, float snapRadius)
    {
        Vector3 interceptPoint;
        if (!Formation.TrySlot(bb.warrior, bb.faction, center, dirToEnemies, hasArcherCentre, archerCentre, out interceptPoint))
        {
            // Loose: lateral spread so warriors form a line, not a stack (archers still behind the wall on a gate)
            Vector3 lateral = Vector3.Cross(dirToEnemies, Vector3.up).normalized;
            interceptPoint = (hasArcherCentre && bb.isRanged ? archerCentre : center) + lateral * spreadOffset;
        }

        NavMeshHit hit;
        if (NavMesh.SamplePosition(interceptPoint, out hit, snapRadius, NavMesh.AllAreas))
        {
            rallyPoint = hit.position;
            return;
        }

        // Fallback: just get close to base on the enemy side
        interceptPoint = Vector3.Lerp(basePos, center, 0.5f);
        rallyPoint = NavMesh.SamplePosition(interceptPoint, out hit, 5f, NavMesh.AllAreas) ? hit.position : basePos;
    }

    void MoveToRally(AIBlackboard bb)
    {
        if (bb.agent.isOnNavMesh && bb.agent.enabled)
        {
            bb.agent.isStopped = false;
            rallySet = AINavHelper.TrySetDestination(bb.agent, rallyPoint);

            if (bb.stuckResolver != null)
                bb.stuckResolver.ResetStuckDetection();
        }
        else
        {
            rallySet = false;
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        rallySet = false;
        arrived = false;
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.isStopped = false;
            if (stoppingSwapped) bb.agent.stoppingDistance = originalStoppingDistance;
        }
        stoppingSwapped = false;
    }
}

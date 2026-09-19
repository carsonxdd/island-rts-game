using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker executor: nothing to do — walk home, then potter about the village.
/// Low-priority fallback action.
/// </summary>
/// <remarks>
/// "Home" is the hut or campfire the colonist is homed to (2026-09-02). A survivor
/// who has just come ashore is homed to the building that had room, so this is also
/// what walks them in from the cove; an idle colonist standing beside a hut is a
/// filled slot with no job. Far from home → path to its carve-safe approach point and
/// stop a few metres short (huts and the campfire carve, and a crowd of idlers must
/// not block the delivery edge); near home, or homeless → stand where we are.
///
/// By day an idler then strolls (2026-09-08): stand <see cref="StandMin"/>–<see cref="StandMax"/>
/// seconds, pick a spot just outside a random colony building's no-build ring (campfire,
/// huts, Workshop, Watchtower, Shipyard), walk there, stand again. Spots keep
/// <see cref="FireClearance"/> off the fire's collider edge so idlers never sit on the
/// delivery edge, and are never further than <see cref="MaxStrollDistance"/> from the
/// colonist so nobody crosses the island to admire a tower. At night the stroll is off:
/// the colonist walks home and stays put (raids land at night, and a Flee from the far
/// side of the village is a worse Flee). The stroll is display only — "Wandering" over
/// the head — the roster still counts the colonist as idle and any real work outscores
/// this action as before.
///
/// Nobody idles in a gateway (2026-09-13): every stroll spot passes <see cref="Loiter.IsClear"/>,
/// and every stop - arriving home at night, a stroll that timed out, a fresh arrival
/// stopping short of its hut - checks the ground under its feet the same way and takes
/// a short <see cref="StepAsideRadius"/> step to clear ground first when it is standing
/// on a wall line or in a gate corridor. The step runs by night too; it is a few metres,
/// not a stroll.
/// </remarks>
public class IdleExecutor : ActionExecutor
{
    public override string DisplayName => displayName;
    private string displayName = "Idle";

    private const float HomeRadius = 6f;     // further than this from home's edge → walk back
    private const float ArriveRadius = 3.5f; // close enough to stop (leaves the building edge clear)

    // Stroll tuning (mostly standing, occasional walk)
    private const float StandMin = 6f;
    private const float StandMax = 15f;
    private const float StrollArrive = 1.2f;       // arrival radius at a stroll spot
    private const float MaxStrollSeconds = 20f;    // a stroll that takes longer than this just stops where it is
    private const float MaxStrollDistance = 30f;   // never pick a spot further than this from the colonist
    private const float MinStrollDistance = 4f;    // ...or so close the walk reads as a twitch
    private const float RingPadding = 1.5f;        // spot sits this far outside a building's no-build ring
    private const float RingJitter = 2f;           // ...plus up to this much
    private const float FireClearance = 4f;        // same clearance Patrol keeps off the fire's delivery edge
    private const int PickAttempts = 12;
    private const float StepAsideRadius = 6f;      // how far a "get out of the gateway" step may go
    private const float MaxStepAsideSeconds = 8f;  // then stand wherever we are

    private enum Mode { Home, Standing, Strolling, StepAside }

    private IHousing home;
    private Mode mode;
    private bool destinationQueued;
    private float standTimer;
    private float strollTimer;
    private Vector3 strollPoint;

    private static readonly System.Collections.Generic.List<(Transform t, float radius)> buildingBuffer
        = new System.Collections.Generic.List<(Transform, float)>(32);

    static bool IsNight => AIWorldState.Instance != null && AIWorldState.Instance.isNight;

    public override void OnEnter(AIBlackboard bb)
    {
        destinationQueued = false;
        home = bb.faction.Population.HomeOf(bb.worker);
        if (bb.specialty != Worker.Specialty.Any) DevQuests.Signal("specialist:idle");   // a specialist with no work of their trade

        if (home != null && AgentReady(bb)
            && TargetingUtil.EdgeDistance(bb.transform.position, home.transform, home.HousingCollider) > HomeRadius)
        {
            GoHome(bb);
        }
        else
        {
            Stand(bb);
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        switch (mode)
        {
            case Mode.Standing:
                UpdateStanding(bb);
                break;
            case Mode.Home:
                UpdateHome(bb);
                break;
            case Mode.Strolling:
                UpdateStroll(bb);
                break;
            case Mode.StepAside:
                UpdateStepAside(bb);
                break;
        }
    }

    // ---- Stepping out of a gateway ----

    void StepAside(AIBlackboard bb)
    {
        mode = Mode.StepAside;
        destinationQueued = false;
        strollTimer = 0f;
        displayName = "Idle";
        DevQuests.Signal("loiter:step_aside");
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();
        IssueStrollMove(bb);
    }

    void UpdateStepAside(AIBlackboard bb)
    {
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            destinationQueued = false;
            return;
        }

        if (!destinationQueued) IssueStrollMove(bb);

        strollTimer += Time.deltaTime;
        float dist = Vector3.Distance(bb.transform.position, strollPoint);
        bool pathDone = AgentReady(bb) && destinationQueued && !bb.agent.pathPending
            && (!bb.agent.hasPath || bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.3f);
        if (dist <= StrollArrive || pathDone || strollTimer > MaxStepAsideSeconds)
        {
            StandHere(bb);   // no second check: one step is the budget, or a queue in the gate never ends
        }
    }

    // ---- Standing ----

    void UpdateStanding(AIBlackboard bb)
    {
        if (IsNight) return;   // stays put until morning (or until the brain finds work)
        if (bb.specialty != Worker.Specialty.Any) return;   // a specialist waits at home for their trade (2026-09-08), no stroll

        standTimer -= Time.deltaTime;
        if (standTimer > 0f) return;

        if (AgentReady(bb) && TryPickStrollPoint(bb, out strollPoint))
        {
            StartStroll(bb);
        }
        else
        {
            standTimer = Random.Range(StandMin, StandMax) * bb.standScale;   // nowhere to go right now — try again later
        }
    }

    // ---- Walking home (dusk, or a fresh arrival) ----

    void GoHome(AIBlackboard bb)
    {
        mode = Mode.Home;
        destinationQueued = false;
        displayName = "Heading home";
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();
        IssueHomeMove(bb);
    }

    void UpdateHome(AIBlackboard bb)
    {
        if (home == null || !home.HousingAlive)
        {
            Stand(bb);
            return;
        }

        // A stuck reset already ForceReeval'd and ResetPath'd — bail this tick, re-issue next
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            destinationQueued = false;
            return;
        }

        if (!destinationQueued) IssueHomeMove(bb);

        float edge = TargetingUtil.EdgeDistance(bb.transform.position, home.transform, home.HousingCollider);
        bool pathDone = AgentReady(bb) && destinationQueued
            && !bb.agent.pathPending && !bb.agent.hasPath;
        if (edge <= ArriveRadius || pathDone)
        {
            Stand(bb);
        }
    }

    void IssueHomeMove(AIBlackboard bb)
    {
        if (home == null || !AgentReady(bb)) return;
        Vector3 approach = TargetingUtil.GetApproachPoint(bb.transform.position, home.transform, home.HousingCollider);
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, approach);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    // ---- Strolling ----

    void StartStroll(AIBlackboard bb)
    {
        mode = Mode.Strolling;
        destinationQueued = false;
        strollTimer = 0f;
        displayName = "Wandering";
        DevQuests.Signal("stroll");
        if (buildingBuffer.Count == 1) DevQuests.Signal("stroll:campfire_only");   // the fire is the only standing building
        Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);
        if (bb.stuckResolver != null) bb.stuckResolver.ResetStuckDetection();
        IssueStrollMove(bb);
    }

    void UpdateStroll(AIBlackboard bb)
    {
        if (IsNight)
        {
            // Dusk mid-stroll: turn for home (or stand if already there)
            DevQuests.Signal("stroll:home");
            if (home != null && home.HousingAlive && AgentReady(bb)
                && TargetingUtil.EdgeDistance(bb.transform.position, home.transform, home.HousingCollider) > HomeRadius)
                GoHome(bb);
            else
                Stand(bb);
            return;
        }

        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
        {
            destinationQueued = false;
            return;
        }

        if (!destinationQueued) IssueStrollMove(bb);

        strollTimer += Time.deltaTime;
        float dist = Vector3.Distance(bb.transform.position, strollPoint);
        bool pathDone = AgentReady(bb) && destinationQueued && !bb.agent.pathPending
            && (!bb.agent.hasPath || bb.agent.remainingDistance <= bb.agent.stoppingDistance + 0.3f);
        if (dist <= StrollArrive || pathDone || strollTimer > MaxStrollSeconds)
        {
            Stand(bb);   // as close as the crowd and the mesh allow
        }
    }

    void IssueStrollMove(AIBlackboard bb)
    {
        if (!AgentReady(bb)) return;
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, strollPoint);
        if (destinationQueued) bb.agent.isStopped = false;
    }

    /// <summary>
    /// A spot just outside a random colony building's no-build ring, on the NavMesh,
    /// off the fire's delivery edge and within walking distance. False when the colony
    /// has no standing buildings or every attempt failed (the caller waits and retries).
    /// </summary>
    bool TryPickStrollPoint(AIBlackboard bb, out Vector3 point)
    {
        point = Vector3.zero;
        CollectBuildings(bb.faction);
        if (buildingBuffer.Count == 0) return false;

        Vector3 here = bb.transform.position;
        for (int attempt = 0; attempt < PickAttempts; attempt++)
        {
            var b = buildingBuffer[Random.Range(0, buildingBuffer.Count)];
            if (b.t == null) continue;

            // Skip buildings that are out of stroll range before sampling anything
            float toBuilding = Vector3.Distance(here, b.t.position);
            if (toBuilding > MaxStrollDistance) continue;

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float ring = b.radius + RingPadding + Random.Range(0f, RingJitter);
            Vector3 candidate = b.t.position + new Vector3(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);

            float walk = Vector3.Distance(here, candidate);
            if (walk < MinStrollDistance || walk > MaxStrollDistance) continue;

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas)) continue;
            if (TooCloseToFire(hit.position)) continue;
            if (!Loiter.IsClear(hit.position)) continue;   // never a wall cell or a gate corridor

            point = hit.position;
            return true;
        }
        return false;
    }

    /// <summary>This colony's standing buildings (lap step 1): a colonist strolls its own camp, never a rival's.</summary>
    static void CollectBuildings(Faction f)
    {
        buildingBuffer.Clear();
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            var fire = BaseBuilding.ActiveList[i];
            if (fire != null && fire.Faction == f) buildingBuffer.Add((fire.transform, fire.noBuildRadius));
        }
        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            var hut = Hut.ActiveList[i];
            if (hut != null && hut.Faction == f) buildingBuffer.Add((hut.transform, hut.noBuildRadius));
        }
        for (int i = 0; i < Workshop.ActiveList.Count; i++)
        {
            var w = Workshop.ActiveList[i];
            if (w != null && w.Faction == f) buildingBuffer.Add((w.transform, w.noBuildRadius));
        }
        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            var t = Watchtower.ActiveList[i];
            if (t != null && t.Faction == f) buildingBuffer.Add((t.transform, t.noBuildRadius));
        }
        for (int i = 0; i < Shipyard.ActiveList.Count; i++)
        {
            var s = Shipyard.ActiveList[i];
            if (s != null && s.Faction == f) buildingBuffer.Add((s.transform, s.noBuildRadius));
        }
        for (int i = 0; i < Storehouse.ActiveList.Count; i++)
        {
            var s = Storehouse.ActiveList[i];
            if (s != null && s.Faction == f) buildingBuffer.Add((s.transform, s.noBuildRadius));
        }
    }

    /// <summary>A spot this close to the fire's collider edge would stand on the delivery edge.</summary>
    static bool TooCloseToFire(Vector3 point)
    {
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            BaseBuilding fire = BaseBuilding.ActiveList[i];
            if (fire == null) continue;
            if (TargetingUtil.EdgeDistance(point, fire.transform, fire.HousingCollider) < FireClearance) return true;
        }
        return false;
    }

    // ---- Shared ----

    static bool AgentReady(AIBlackboard bb)
        => bb.agent != null && bb.agent.enabled && bb.agent.isOnNavMesh;

    /// <summary>
    /// Stop here - unless "here" is a wall line or a gate corridor, in which case take
    /// one short step to clear ground first and stop there. Every stop goes through this.
    /// </summary>
    void Stand(AIBlackboard bb)
    {
        if (AgentReady(bb) && !Loiter.IsClear(bb.transform.position)
            && Loiter.TryFindClearNear(bb.transform.position, StepAsideRadius, out strollPoint))
        {
            StepAside(bb);
            return;
        }
        StandHere(bb);
    }

    void StandHere(AIBlackboard bb)
    {
        mode = Mode.Standing;
        destinationQueued = false;
        // A specialist with nothing to do says so by trade ("Builder, idle") — before
        // 2026-09-08 they never got here at all (Gather outscored Idle with no node
        // and they stood at the fire labelled "Gathering").
        displayName = bb.specialty != Worker.Specialty.Any && bb.worker != null
            ? bb.worker.RoleTitle() + ", idle"
            : "Idle";
        standTimer = Random.Range(StandMin, StandMax) * bb.standScale;   // a Lazy colonist lingers (Persona, 2026-09-16)
        if (AgentReady(bb))
        {
            bb.agent.ResetPath();
            bb.agent.isStopped = true;
            // Standing still: max-importance so deliverers route around idlers at the
            // campfire instead of shoving them (a stander has no path and can't yield)
            Worker.SetStationaryAvoidance(bb.agent);
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        if (mode == Mode.Strolling && bb.hasJob) DevQuests.Signal("stroll:interrupted");   // a job given mid-stroll        mode = Mode.Standing;
        home = null;
        if (AgentReady(bb))
        {
            bb.agent.isStopped = false;
            Worker.RollMovingAvoidance(bb.agent, bb.carryAmount);  // about to move — drop stationary-importance
        }
    }
}

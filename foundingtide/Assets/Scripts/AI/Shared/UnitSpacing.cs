using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The two-radius model (2026-09-16): a unit has a SOLID radius (its hard body,
/// used only by the soft push-apart in <see cref="UnitGrid"/>) and an AVOIDANCE
/// radius (the space ORCA prefers to keep clear, written to the NavMeshAgent).
/// Agents therefore look like they respect each other's space but can squeeze
/// through when the alternative is a deadlock: the push resolves an overlap,
/// the stuck ladder (<see cref="StuckResolver"/>) drops the avoidance radius
/// when a squeeze is the only way on. The art never drives either. Both shrank
/// with the 0.8x bodies (2026-09-28, <c>LowPolyPlumber.UnitModelScale</c>); the
/// NavMesh BAKE radius (0.5) did not, because the diagonal wall carves rely on it.
/// </summary>
/// <remarks>
/// Right of way is deterministic so two agents never mirror each other into a
/// dance (the old Random.Range(30, 70) could tie): cargo outranks empty hands,
/// then the lower instance id, and a unit standing in a gateway corridor
/// outranks anyone entering it. Lower avoidancePriority = MORE important.
/// A stationary worker stays <see cref="Worker.StationaryAvoidancePriority"/>,
/// which outranks all of these: movers route around it like furniture.
/// </remarks>
public static class UnitSpacing
{
    /// <summary>Hard body: the soft push-apart acts inside twice this.</summary>
    public const float SolidRadius = 0.18f;    // 0.22 before the 0.8x bodies
    /// <summary>Steering preference for warriors, raiders and the castaway (the spec's 0.55, x0.8 with the bodies).</summary>
    public const float AvoidanceRadius = 0.44f;
    /// <summary>
    /// Workers keep a tighter one: the gather ring seats them 0.75 m apart
    /// (<see cref="Worker.AgentRadius"/> x 2 + 0.25), and an ORCA radius that
    /// wants 1.1 m clear can never reach a ring point beside a standing mate.
    /// Raise it with the ring, not alone.
    /// </summary>
    public const float WorkerAvoidanceRadius = 0.32f;   // 0.4 before the 0.8x bodies; wants 0.64 m clear, under the ring's 0.75
    /// <summary>Positional push-apart is clamped to this.</summary>
    public const float MaxSeparationSpeed = 1.5f;

    /// <summary>The castaway: under every colonist's number, so movers steer round them and never the reverse (2026-09-17).</summary>
    public const int PlayerPriority = 5;
    public const int GatewayPriority = 20;
    public const int CargoPriority = 35;
    public const int EmptyPriority = 50;
    public const int PrioritySpread = 12;   // id-based tie-break band

    /// <summary>Set the agent's avoidance radius and quality. Called once from each unit's Start.</summary>
    public static void Apply(NavMeshAgent agent, bool worker)
    {
        if (agent == null) return;
        agent.radius = worker ? WorkerAvoidanceRadius : AvoidanceRadius;
        // One step down from what each unit used: the soft push and the ladder
        // now carry the cases high quality was paying for.
        agent.obstacleAvoidanceType = worker ? ObstacleAvoidanceType.MedQualityObstacleAvoidance
                                             : ObstacleAvoidanceType.LowQualityObstacleAvoidance;
    }

    /// <summary>
    /// The moving priority for <paramref name="agent"/>: cargo, then a stable
    /// id-based tie-break. Executors call it on every new errand (the old
    /// RollMovingAvoidance); the gateway rule is applied live by <see cref="UnitGrid"/>.
    /// </summary>
    public static int MovingPriority(NavMeshAgent agent, bool carrying)
    {
        int id = agent != null ? (int)((ulong)agent.GetEntityId().GetHashCode() % (ulong)PrioritySpread) : 0;
        return (carrying ? CargoPriority : EmptyPriority) + id;
    }

    public static void SetMoving(NavMeshAgent agent, bool carrying)
    {
        if (agent != null) agent.avoidancePriority = MovingPriority(agent, carrying);
    }

    /// <summary>Inside a gate's corridor (the chokepoint rule): within <see cref="Loiter.GateClearance"/> of a gate's span (up to three cells wide, 2026-09-28).</summary>
    public static bool InGateway(Vector3 pos)
    {
        var gates = Gate.ActiveList;
        float sqr = Loiter.GateClearance * Loiter.GateClearance;
        for (int i = 0; i < gates.Count; i++)
        {
            Gate g = gates[i];
            if (g == null) continue;
            if (g.SqrDistanceXZ(pos) <= sqr) return true;
        }
        return false;
    }
}

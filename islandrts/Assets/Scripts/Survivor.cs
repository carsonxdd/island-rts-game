using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The lone castaway the player controls during the opening sequence
/// (right-click to move, walk ashore, place the campfire). Deliberately
/// minimal: no Health, no Utility AI, no registry — nothing in the game
/// targets or counts the survivor. GameStartController owns all input and
/// destroys this unit when the colony starts.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Survivor : MonoBehaviour
{
    private NavMeshAgent agent;

    // Destination retry: AINavHelper.TrySetDestination returns Unity's real
    // SetDestination result — a false means the NavMesh rejected the point
    // (recalc in progress, throttle) and we must retry next frame, never
    // pretend success (the "ghost moving" gotcha).
    private Vector3 pendingDestination;
    private bool hasPendingDestination;

    public NavMeshAgent CachedAgent => agent;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        // Same locomotion feel as a Worker (see Worker.Start)
        agent.speed = 3.5f;
        agent.acceleration = 5f;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = 0.2f;
        agent.radius = Worker.AgentRadius;
        agent.baseOffset = 0f;  // base-pivot art: transform origin IS the feet
    }

    /// <summary>
    /// Order the survivor to walk somewhere. The point is snapped to the
    /// NavMesh (generous 4u radius — clicks in the shallows land on the
    /// nearest walkable spot instead of being ignored).
    /// </summary>
    public void MoveTo(Vector3 worldPos)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(worldPos, out hit, 4f, NavMesh.AllAreas))
        {
            pendingDestination = hit.position;
            hasPendingDestination = true;
            TryIssueMove();
        }
    }

    /// <summary>Remaining distance to the current destination (straight-line to the pending point if the agent hasn't accepted it yet).</summary>
    public bool HasArrived(float tolerance)
    {
        if (hasPendingDestination) return false;
        if (agent.pathPending) return false;
        return agent.remainingDistance <= tolerance;
    }

    void Update()
    {
        if (hasPendingDestination)
        {
            TryIssueMove();
        }
    }

    void TryIssueMove()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        if (AINavHelper.TrySetDestination(agent, pendingDestination))
        {
            agent.isStopped = false;
            hasPendingDestination = false;
        }
        // else: rejected — keep the flag, retry next frame
    }
}

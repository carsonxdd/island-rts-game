using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Static helper for throttled NavMesh operations.
/// Consolidates existing per-frame throttle patterns from Warrior and Enemy.
/// </summary>
public static class AINavHelper
{
    // SetDestination throttle — max 20 per frame across all units (Phase 6.21: bumped from 12 to handle mass retarget events when multiple enemies finish a hut simultaneously)
    private static int setDestFrame = -1;
    private static int setDestCount = 0;
    private const int SetDestPerFrameLimit = 20;

    // CalculatePath throttle — max 2 per frame across all units
    private static int calcPathFrame = -1;
    private static int calcPathCount = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        setDestFrame = -1;
        setDestCount = 0;
        calcPathFrame = -1;
        calcPathCount = 0;
    }

    /// <summary>
    /// Throttled SetDestination. Returns Unity's actual SetDestination result
    /// (false if throttled OR if Unity rejected the destination — e.g. unmappable
    /// during NavMesh recalc). Callers must respect false and retry next frame
    /// rather than assuming the destination was queued.
    /// Max 20 calls per frame across all units.
    /// </summary>
    public static bool TrySetDestination(NavMeshAgent agent, Vector3 destination)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return false;

        if (Time.frameCount != setDestFrame)
        {
            setDestFrame = Time.frameCount;
            setDestCount = 0;
        }
        if (setDestCount >= SetDestPerFrameLimit) return false;
        setDestCount++;
        // Propagate Unity's actual result: SetDestination returns false if the
        // destination can't be mapped onto the NavMesh (e.g. the NavMesh is
        // mid-recalc after a carving obstacle was just disabled). Callers need
        // to know so they can retry next frame instead of pretending success.
        return agent.SetDestination(destination);
    }

    /// <summary>
    /// Throttled CalculatePath. Returns true if the call was allowed.
    /// Max 2 calls per frame across all units.
    /// </summary>
    public static bool TryCalculatePath(Vector3 source, Vector3 destination, int areaMask, NavMeshPath path)
    {
        if (Time.frameCount != calcPathFrame)
        {
            calcPathFrame = Time.frameCount;
            calcPathCount = 0;
        }
        if (calcPathCount >= 2) return false;
        calcPathCount++;
        NavMesh.CalculatePath(source, destination, areaMask, path);
        return true;
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Static helper for throttled NavMesh operations.
/// Consolidates existing per-frame throttle patterns from Warrior and Enemy.
/// </summary>
public static class AINavHelper
{
    // SetDestination throttle — max 12 per frame across all units (increased for combat with many enemies)
    private static int setDestFrame = -1;
    private static int setDestCount = 0;

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
    /// Throttled SetDestination. Returns true if the call was allowed.
    /// Max 12 calls per frame across all units.
    /// </summary>
    public static bool TrySetDestination(NavMeshAgent agent, Vector3 destination)
    {
        if (agent == null || !agent.isOnNavMesh || !agent.enabled) return false;

        if (Time.frameCount != setDestFrame)
        {
            setDestFrame = Time.frameCount;
            setDestCount = 0;
        }
        if (setDestCount >= 12) return false;
        setDestCount++;
        agent.SetDestination(destination);
        return true;
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

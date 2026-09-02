using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static list of every live instance of T. Entities register in Awake and unregister in
/// OnDestroy, which gives every scan in the game an O(1) list to walk.
/// </summary>
/// <remarks>
/// This exists so nothing has to call FindObjectsByType, which is banned in this codebase:
/// it walks the whole scene and allocates, and several systems used to do it every frame.
/// Iterate with an index loop and null-check as you go - an entry can be a destroyed Unity
/// object if something is removed mid-iteration.
/// </remarks>
public static class ActiveRegistry<T> where T : class
{
    private static readonly List<T> list = new List<T>();
    public static IReadOnlyList<T> List => list;

    public static void Register(T item) { list.Add(item); }
    public static void Unregister(T item) { list.Remove(item); }
    public static int IndexOf(T item) { return list.IndexOf(item); }
    public static void Clear() { list.Clear(); }
}

/// <summary>
/// Empties every registry when play mode starts. Required because the lists are static:
/// with domain reload disabled they would otherwise still hold last session's destroyed
/// entities. Any new registry type must be added here too.
/// </summary>
public static class ActiveRegistryReset
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAll()
    {
        ActiveRegistry<Worker>.Clear();
        ActiveRegistry<Warrior>.Clear();
        ActiveRegistry<Enemy>.Clear();
        ActiveRegistry<BaseBuilding>.Clear();
        ActiveRegistry<Hut>.Clear();
        ActiveRegistry<Wall>.Clear();
        ActiveRegistry<Gate>.Clear();
        ActiveRegistry<Watchtower>.Clear();
        ActiveRegistry<ResourceNode>.Clear();
        ActiveRegistry<ConstructionSite>.Clear();
        ActiveRegistry<Workshop>.Clear();
        ActiveRegistry<GroundPickup>.Clear();
        ActiveRegistry<OcclusionFade>.Clear();
    }
}

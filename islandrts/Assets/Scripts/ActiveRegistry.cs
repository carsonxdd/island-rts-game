using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic static registry for tracking active instances of entity types.
/// Replaces per-class activeList boilerplate with a centralized pattern.
/// </summary>
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
/// Clears all ActiveRegistry lists on domain reload / play mode enter.
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
    }
}

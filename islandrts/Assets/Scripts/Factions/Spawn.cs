using UnityEngine;

/// <summary>
/// The one way to instantiate something a faction owns (2026-09-09, lap step 1).
/// Instantiates the prefab, then sets <see cref="IOwned.Faction"/> on the root
/// before anything else runs — <c>Awake</c> has already gone by then, which is
/// why faction is read in <c>Start</c> or later, never in <c>Awake</c>.
/// </summary>
/// <remarks>
/// A prefab with no <see cref="IOwned"/> on its root is an error, not a fallback:
/// an owner that silently fails to land is a unit that fights for nobody. Nothing
/// calls this until step 1's commit 6 rewrites the instantiate sites.
/// </remarks>
public static class Spawn
{
    public static GameObject Owned(GameObject prefab, Vector3 position, Quaternion rotation, Faction owner, Transform parent = null)
    {
        GameObject go = parent != null
            ? Object.Instantiate(prefab, position, rotation, parent)
            : Object.Instantiate(prefab, position, rotation);
        Assign(go, owner);
        return go;
    }

    public static T Owned<T>(T prefab, Vector3 position, Quaternion rotation, Faction owner, Transform parent = null) where T : Component
    {
        T c = parent != null
            ? Object.Instantiate(prefab, position, rotation, parent)
            : Object.Instantiate(prefab, position, rotation);
        Assign(c.gameObject, owner);
        return c;
    }

    /// <summary>Sets the owner on an object that already exists (a scene-placed campfire, a debug conversion).</summary>
    public static void Assign(GameObject go, Faction owner)
    {
        if (owner == null)
        {
            Debug.LogError("Spawn.Owned: '" + go.name + "' spawned with no faction.");
            return;
        }
        IOwned owned = go.GetComponent<IOwned>();
        if (owned == null)
        {
            Debug.LogError("Spawn.Owned: '" + go.name + "' has no IOwned component on its root; it belongs to nobody.");
            return;
        }
        owned.Faction = owner;
    }
}

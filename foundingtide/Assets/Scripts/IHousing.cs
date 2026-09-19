using UnityEngine;

/// <summary>
/// A building that houses colonists: the campfire and every hut. Housing has one
/// owner per building — it registers itself with <see cref="PopulationManager"/>
/// in Start and unregisters on death or destruction, and the population manager
/// derives the colony's capacity from the registered providers rather than from
/// counters that can drift.
/// </summary>
/// <remarks>
/// A colonist is homed to one provider (the one that had a free slot when they
/// came ashore). Idle colonists walk to their home and wait there, so a hut with
/// people standing beside it is a hut whose slots are filled but whose people
/// have no job yet.
/// </remarks>
public interface IHousing
{
    Transform transform { get; }

    /// <summary>Colonists this building can house.</summary>
    int HousingCapacity { get; }

    /// <summary>False once the building is dead or gone; its colonists become homeless.</summary>
    bool HousingAlive { get; }

    /// <summary>Collider used for carve-safe approach points and edge distances (may be null).</summary>
    Collider HousingCollider { get; }
}

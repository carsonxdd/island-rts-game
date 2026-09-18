using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Somewhere a colonist can hand a load in (2026-09-16): the campfire, and every
/// <see cref="Storehouse"/> the colony has built. One colony store — whatever is
/// dropped anywhere lands in the faction pool and the campfire stockpile at
/// once — so a drop-off is only a place to walk to, never an inventory. Eight
/// claimed bearings around it (<see cref="DropoffRing"/>), the campfire's pattern.
/// </summary>
public interface IDropoff : IOwned
{
    Transform transform { get; }
    /// <summary>The collider the edge distance and the approach point are measured against.</summary>
    Collider ApproachCollider { get; }
    bool IsAlive { get; }
    int ClaimDropoffSlot(Worker worker, Vector3 from);
    Vector3 DropoffPoint(int slot);
    void ReleaseDropoffSlot(Worker worker);
}

/// <summary>
/// The eight-bearing slot ring a drop-off owns. Extracted from
/// <c>BaseBuilding</c> (2026-09-08) so a Storehouse fans its returners out the
/// same way. A slot is free when its owner is gone, dead, or no longer holding
/// it (<see cref="Worker.dropoffSlot"/>) — nothing leaks. With every slot taken
/// the nearest is shared; two on one face is still a short queue.
/// </summary>
public sealed class DropoffRing
{
    public const int SlotCount = 8;
    private readonly Worker[] owners = new Worker[SlotCount];
    private readonly Transform centre;
    private readonly System.Func<Collider> collider;

    public DropoffRing(Transform centre, System.Func<Collider> collider)
    {
        this.centre = centre;
        this.collider = collider;
    }

    /// <summary>
    /// The slot this colonist should deliver at: the one it already holds, else the
    /// free bearing closest to its own line of approach, else the closest regardless.
    /// Stamps <see cref="Worker.dropoffSlot"/>. Pair with <see cref="Point"/>.
    /// </summary>
    public int Claim(Worker worker, Vector3 from)
    {
        if (worker != null && worker.dropoffSlot >= 0 && worker.dropoffSlot < SlotCount
            && owners[worker.dropoffSlot] == worker)
            return worker.dropoffSlot;

        Vector3 dir = from - centre.position;
        dir.y = 0f;
        float wanted = dir.sqrMagnitude > 0.001f ? Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg : 0f;

        int bestFree = -1, bestAny = -1;
        float bestFreeDelta = float.MaxValue, bestAnyDelta = float.MaxValue;
        for (int i = 0; i < SlotCount; i++)
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(wanted, Angle(i)));
            if (delta < bestAnyDelta) { bestAnyDelta = delta; bestAny = i; }
            if (Free(i) && delta < bestFreeDelta) { bestFreeDelta = delta; bestFree = i; }
        }

        int slot = bestFree >= 0 ? bestFree : bestAny;
        if (bestFree >= 0) owners[slot] = worker;
        if (worker != null) worker.dropoffSlot = slot;
        return slot;
    }

    /// <summary>Give the slot back (delivered, or left for another errand). Clears <see cref="Worker.dropoffSlot"/>.</summary>
    public void Release(Worker worker)
    {
        if (worker != null) worker.dropoffSlot = -1;
        for (int i = 0; i < SlotCount; i++)
            if (owners[i] == worker) owners[i] = null;
    }

    /// <summary>
    /// Walkable point at the building's collider edge along the slot's bearing. The
    /// building carves the NavMesh, so this goes through the shared ClosestPoint →
    /// SamplePosition approach-point pattern, aimed from a point out along the
    /// bearing instead of from the colonist (which is what put everyone on one face).
    /// </summary>
    public Vector3 Point(int slot)
    {
        float a = Angle(slot) * Mathf.Deg2Rad;
        Vector3 outside = centre.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 10f;
        return TargetingUtil.GetApproachPoint(outside, centre, collider());
    }

    static float Angle(int slot) => slot * (360f / SlotCount) + 22.5f;   // face-centres and corners alike, never a corner exactly

    bool Free(int i)
    {
        Worker owner = owners[i];
        if (owner == null) return true;                        // destroyed, or never claimed
        if (owner.dropoffSlot != i) return true;               // moved on without releasing
        Health h = owner.CachedHealth;
        return h != null && !h.IsAlive;
    }
}

/// <summary>
/// Which drop-off a colonist should walk to: the nearest living one of its own
/// colony, the campfire included. Registries stay global and ownership is a
/// filter on the scan (CLAUDE.md, Factions). Centre distance, not edge: every
/// candidate is a 2 m box and the choice is between places tens of metres apart.
/// </summary>
public static class Dropoff
{
    /// <summary>Nearest standing drop-off of <paramref name="faction"/> to <paramref name="from"/>, or null with no campfire.</summary>
    public static IDropoff Nearest(Faction faction, Vector3 from, out float distance)
    {
        distance = float.MaxValue;
        IDropoff best = null;

        BaseBuilding fire = faction != null ? faction.Campfire : null;
        if (fire != null && fire.IsAlive)
        {
            best = fire;
            distance = Vector3.Distance(from, fire.transform.position);
        }

        IReadOnlyList<Storehouse> stores = Storehouse.ActiveList;
        for (int i = 0; i < stores.Count; i++)
        {
            Storehouse s = stores[i];
            if (s == null || s.Faction != faction || !s.IsAlive) continue;
            float d = Vector3.Distance(from, s.transform.position);
            if (d < distance) { distance = d; best = s; }
        }
        return best;
    }

    /// <summary>Distance to the nearest drop-off, or <see cref="float.MaxValue"/> with none.</summary>
    public static float NearestDistance(Faction faction, Vector3 from)
    {
        float d;
        Nearest(faction, from, out d);
        return d;
    }
}

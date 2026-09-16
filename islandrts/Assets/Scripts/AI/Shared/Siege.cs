using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What a landing party may attack besides the militia (2026-09-16, the
/// conquest test): the buildings of the colony it landed on, in the raider's
/// order — huts and works first, then the wall (a gate at 0.3x), then the
/// campfire. Relief parties and warriors at home never see a building. The
/// engage scan and its consideration both fall through to
/// <see cref="FindNearestBuilding"/> when no fighter is in reach, so nothing
/// else in the warrior AI had to learn what a siege is.
/// </summary>
public static class Siege
{
    static readonly NavMeshPath path = new NavMeshPath();

    /// <summary>Blows landed on buildings by landing parties this run (telemetry; <see cref="Factions.ResetAll"/> clears it).</summary>
    public static int BuildingBlows { get; private set; }
    public static void NoteBuildingBlow() { BuildingBlows++; }
    public static void Clear() { BuildingBlows = 0; }

    /// <summary>The colony <paramref name="w"/>'s landing party is besieging, or null (at home, on relief, or no party).</summary>
    public static Faction TargetOf(Warrior w)
    {
        if (w == null || !w.OnExpedition) return null;
        var parties = Expedition.Parties;
        for (int p = 0; p < parties.Count; p++)
        {
            Expedition.Party party = parties[p];
            if (party.Relief) continue;
            if (party.Warriors.Contains(w)) return party.To;
        }
        return null;
    }

    public static bool IsBuilding(ITargetable t) => !(t is Enemy) && !(t is Warrior);

    /// <summary>The nearest standing building of <paramref name="of"/> in raider order, or null.</summary>
    public static ITargetable FindNearestBuilding(Vector3 from, Faction of, out float distance)
    {
        distance = float.MaxValue;
        ITargetable best = null;
        float d;

        // Works and homes, nearest first, when a complete path reaches it
        Consider(Nearest(Hut.ActiveList, from, of, out d), d, from, ref best, ref distance);
        Consider(Nearest(Watchtower.ActiveList, from, of, out d), d, from, ref best, ref distance);
        Consider(Nearest(Workshop.ActiveList, from, of, out d), d, from, ref best, ref distance);
        Consider(Nearest(Shipyard.ActiveList, from, of, out d), d, from, ref best, ref distance);
        if (best != null) return best;

        // The wall: a gate reads as three tenths of its distance, like the raiders
        Wall wall = Nearest(Wall.ActiveList, from, of, out d);
        float wallD = d;
        Gate gate = Nearest(Gate.ActiveList, from, of, out d);
        if (gate != null && (wall == null || d * 0.3f < wallD)) { distance = d; return gate; }
        if (wall != null) { distance = wallD; return wall; }

        BaseBuilding fire = Nearest(BaseBuilding.ActiveList, from, of, out d);
        if (fire != null) { distance = d; return fire; }
        return null;
    }

    static T Nearest<T>(System.Collections.Generic.IReadOnlyList<T> list, Vector3 from, Faction of, out float distance) where T : MonoBehaviour, ITargetable
    {
        distance = float.MaxValue;
        T best = null;
        for (int i = 0; i < list.Count; i++)
        {
            T t = list[i];
            if (t == null || t.Faction != of) continue;
            Health h = t.CachedHealth;
            if (h == null || !h.IsAlive) continue;
            float dist = Vector3.Distance(from, t.transform.position);
            if (dist < distance) { distance = dist; best = t; }
        }
        return best;
    }

    static void Consider(ITargetable t, float d, Vector3 from, ref ITargetable best, ref float bestD)
    {
        if (t == null || d >= bestD) return;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(t.transform.position, out hit, 3f, NavMesh.AllAreas)) return;
        if (AINavHelper.TryCalculatePath(from, hit.position, NavMesh.AllAreas, path)
            && path.status != NavMeshPathStatus.PathComplete) return;
        best = t;
        bestD = d;
    }
}

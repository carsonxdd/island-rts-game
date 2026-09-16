using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A party of one colony's warriors sent by sea to another colony's shore
/// (2026-09-16, lap step 3 slice B5). Two kinds, one mechanic: a <b>landing</b>
/// on a Hostile neighbour, which hunts its militia; and <b>relief</b> for an
/// Allied one whose fire the raiders are at, which defends it. Both end at dawn,
/// when the sea takes the survivors home.
/// </summary>
/// <remarks>
/// <para>The warriors are the colony's REAL warriors, not spawned bodies: they
/// keep their weapons, their deaths are its losses (<c>NotifyWarriorKilled</c>
/// still reaches the home fire), and while away they are missing from its
/// defence. What changes is only which fire their AI fights around:
/// <see cref="Warrior.SetExpeditionFire"/> swaps the blackboard's campfire, so
/// <c>GuardStance.ThreatensColony</c>, Intercept and Patrol all read the
/// destination's fire as "the colony" - a landing party takes any hostile
/// within the hold radius of the enemy fire, a relief party any raider within
/// it of the ally's. Nothing else in the warrior AI had to learn what an
/// expedition is.</para>
/// <para>A warrior cannot attack a building, so a landing is a fight with the
/// militia and nothing more; a walled colony with no warriors out is not
/// stormed. That is the whole of the lap plan's "sends k warriors by sea" for
/// now.</para>
/// <para>Arrival is a <c>Warp</c> to the first reachable ground inland of the
/// target's cove, the same walk the raiders use, and home is a warp to the
/// home fire's spawn point. Colony state in a static is a leak, so
/// <see cref="Factions.ResetAll"/> calls <see cref="Clear"/>.</para>
/// </remarks>
public static class Expedition
{
    public sealed class Party
    {
        public Faction From;
        public Faction To;
        public bool Relief;
        public readonly List<Warrior> Warriors = new List<Warrior>(8);
    }

    static readonly List<Party> parties = new List<Party>(2);

    /// <summary>A party set out: (from, to, head count, relief). The HUD names it.</summary>
    public static event System.Action<Faction, Faction, int, bool> OnDeparted;

    /// <summary>Landings that have hit the player's shore this run, and relief parties that came to it (the sim's columns).</summary>
    public static int LandingsOnPlayer { get; private set; }
    public static int ReliefToPlayer { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Clear(); OnDeparted = null; }

    public static void Clear()
    {
        parties.Clear();
        LandingsOnPlayer = 0;
        ReliefToPlayer = 0;
    }

    public static IReadOnlyList<Party> Parties => parties;

    /// <summary>Does <paramref name="from"/> have a party out?</summary>
    public static bool Active(Faction from)
    {
        for (int i = 0; i < parties.Count; i++) if (parties[i].From == from) return true;
        return false;
    }

    /// <summary>A party from <paramref name="from"/> standing at <paramref name="to"/>'s fire, or null.</summary>
    public static Party Find(Faction from, Faction to)
    {
        for (int i = 0; i < parties.Count; i++)
            if (parties[i].From == from && parties[i].To == to) return parties[i];
        return null;
    }

    /// <summary>
    /// Send up to <paramref name="count"/> of <paramref name="from"/>'s warriors
    /// (the ones nearest home, so the men at the wall stay) to <paramref name="to"/>'s
    /// shore. Returns how many went; 0 when either fire is missing or nobody
    /// could be landed.
    /// </summary>
    public static int Send(Faction from, Faction to, int count, bool relief)
    {
        BaseBuilding home = from != null ? from.Campfire : null;
        BaseBuilding theirs = to != null ? to.Campfire : null;
        if (home == null || theirs == null || count <= 0) return 0;

        Vector3 cove = to.HasCove ? to.Cove : (TerrainGrid.Instance != null ? TerrainGrid.Instance.CoveCenter : theirs.transform.position);

        Party party = new Party { From = from, To = to, Relief = relief };
        Vector3 homePos = home.transform.position;
        for (int n = 0; n < count; n++)
        {
            Warrior pick = NearestFree(from, homePos);
            if (pick == null) break;

            // Land a little apart so the party does not stack on one point
            Vector3 offset = new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            Vector3 landing;
            if (!EnemySpawner.FindReachableToward(cove + offset, theirs.transform.position, out landing)) break;
            landing.y += 0.1f;

            NavMeshAgent agent = pick.CachedAgent;
            if (agent == null || !agent.Warp(landing)) break;
            pick.SetExpeditionFire(theirs);
            party.Warriors.Add(pick);
        }

        if (party.Warriors.Count == 0) return 0;
        parties.Add(party);
        if (to.IsPlayer) { if (relief) ReliefToPlayer++; else LandingsOnPlayer++; }
        DevQuests.Signal(relief ? "diplomacy:relief" : "diplomacy:landing");
        OnDeparted?.Invoke(from, to, party.Warriors.Count, relief);
        return party.Warriors.Count;
    }

    /// <summary>Dawn: the sea takes every survivor home. <see cref="GovernorRunner"/> calls it.</summary>
    public static void EndAll()
    {
        for (int p = 0; p < parties.Count; p++)
        {
            Party party = parties[p];
            BaseBuilding home = party.From.Campfire;
            for (int i = 0; i < party.Warriors.Count; i++)
            {
                Warrior w = party.Warriors[i];
                if (w == null) continue;
                w.SetExpeditionFire(null);
                if (home == null) continue;   // the colony fell while they were away; they stay where they stand
                NavMeshAgent agent = w.CachedAgent;
                if (agent != null) agent.Warp(home.GetValidSpawnPosition());
            }
        }
        parties.Clear();
    }

    static Warrior NearestFree(Faction from, Vector3 near)
    {
        Warrior best = null;
        float bestSqr = float.MaxValue;
        var list = Warrior.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Warrior w = list[i];
            if (w == null || w.Faction != from || w.OnExpedition) continue;
            Health h = w.CachedHealth;
            if (h == null || !h.IsAlive) continue;
            float d = (w.transform.position - near).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = w; }
        }
        return best;
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The simulated player's hands (2026-09-03). Research and crafting need
/// someone at the bench and sticks and stone chunks that only the player's
/// character hand-collects, so a sweep has to drive the character the way a
/// human would: fetch what the front of the campfire queue is short of,
/// deposit it, stand at the bench until the queue runs dry. Nothing else —
/// the policies decide WHAT to queue; this only keeps it moving.
///
/// Polled once a game-second from SimRunner, before the policy. It drives the
/// CAMPFIRE bench only; a Crafter colonist (2026-09-04) covers the Workshop, which
/// is why the policies queue Workshop research only once a Crafter exists.
/// </summary>
public static class SimPlayerDriver
{
    public static void Tick(ColonyState s)
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        BaseBuilding fire = s.Campfire;
        if (pc == null || pc.IsKnockedOut || fire == null) return;

        CraftStation station = fire.Station;
        if (station == null) return;

        // A fetch that failed is not tried again (2026-09-17). Islands 7 and 23
        // lost every strategy on the first raid with 47-60 sticks and a queued
        // spear: the nearest chunk sat on ground the castaway could not reach,
        // "Fetching" became "Can't reach that", and the next tick picked the same
        // chunk - all run, while before Crafting nobody else works the bench.
        // An order that outlives CollectTimeout (a stutter never stalls) is
        // cancelled the same way.
        if (lastPickup != null)
        {
            if (pc.HasTask)
            {
                if (Time.time - lastIssued > CollectTimeout)
                {
                    Ban(lastPickup);
                    lastPickup = null;
                    pc.CommandDeposit(fire);   // any new order drops the fetch
                    return;
                }
            }
            else
            {
                // Still on the ground and not in hand: the fetch failed
                if (pc.Inventory.Count(lastPickup.Item) <= lastHave) Ban(lastPickup);
                lastPickup = null;
            }
        }
        else if (!pc.HasTask) lastPickup = null;

        // Mid-errand: let it finish (a stalled task drops itself)
        if (pc.HasTask) return;

        if (!station.HasWork)
        {
            // Nothing to do at the bench — bring home whatever is in hand
            if (!pc.Inventory.IsEmpty && HasDeposit(pc.Inventory)) { pc.CommandDeposit(fire); return; }

            // ...then go swing a mallet (2026-09-10). The character builds like a
            // jobless colonist now, and an idle bench is exactly when a player
            // would walk over to the half-built hut.
            if (pc.BuildingSite != null) return;   // already on one
            ConstructionSite site = NearestSite(pc);
            if (site != null) pc.CommandBuild(site);
            return;
        }

        WorkDef def = station.Active.Def;
        ItemDef missing = def.FirstMissingItem(pc.Inventory, fire.Stockpile);
        if (missing == null)
        {
            // Items are covered (resources, if short, are the colonists' job): work
            if (pc.WorkingStation != station) pc.WorkAt(station);
            return;
        }

        // Short of a material only the character can fetch
        if (pc.Inventory.SpaceFor(missing) <= 0)
        {
            pc.CommandDeposit(fire);
            return;
        }

        GroundPickup pickup = NearestPickup(missing, pc);
        if (pickup != null)
        {
            exploring = false;
            lastPickup = pickup;
            lastIssued = Time.time;
            lastHave = pc.Inventory.Count(missing);
            pc.CommandCollect(pickup);
            return;
        }

        // Nothing of it is known. Go and LOOK (2026-09-11). Waiting for the
        // trickle was a deadlock: a colony cannot make a stone chunk without the
        // Stone Pick that Quarrying grants, Quarrying costs three chunks, and the
        // 2026-09-11 lab watched island 101 sit at 0 chunks and 58 sticks for a
        // whole run with the bench stuck and the castaway standing still. A human
        // player walks off to find some, so the driver does too.
        Explore(pc);
    }

    // ---- exploring for something the colony cannot make --------------------

    private static Vector3 exploreTarget;
    private static bool exploring;
    private static float exploreDeadline;

    /// <summary>How close counts as arrived, and how long one leg may take.</summary>
    private const float ArriveDistance = 5f;
    private const float LegSeconds = 45f;

    // ---- fetches that failed (2026-09-17) -----------------------------------

    private static GroundPickup lastPickup;
    private static float lastIssued;
    private static int lastHave;
    private static readonly Dictionary<GroundPickup, float> bannedUntil = new Dictionary<GroundPickup, float>();

    /// <summary>A fetch still outstanding after this long is dropped and its pickup banned.</summary>
    private const float CollectTimeout = 45f;
    /// <summary>How long a failed pickup stays off the list (the ground may change: a flatten, a wall).</summary>
    private const float BanSeconds = 240f;

    static void Ban(GroundPickup p)
    {
        if (p == null) return;
        if (bannedUntil.Count > 64)
        {
            // Prune destroyed keys and expired bans before it grows
            var stale = new List<GroundPickup>();
            foreach (var kv in bannedUntil) if (kv.Key == null || kv.Value < Time.time) stale.Add(kv.Key);
            for (int i = 0; i < stale.Count; i++) bannedUntil.Remove(stale[i]);
        }
        bannedUntil[p] = Time.time + BanSeconds;
    }

    static bool Banned(GroundPickup p) => bannedUntil.TryGetValue(p, out float until) && until > Time.time;

    /// <summary>Per-run state; the driver is static and the sim reloads the scene.</summary>
    public static void ResetRun()
    {
        exploring = false;
        exploreDeadline = 0f;
        lastPickup = null;
        bannedUntil.Clear();
    }

    /// <summary>
    /// Walk to the nearest unexplored ground, which reveals what is on it. One leg
    /// at a time: re-issued only on arrival or when the leg times out, so the
    /// character is not re-ordered every tick.
    /// </summary>
    static void Explore(PlayerCharacter pc)
    {
        Vector3 from = pc.transform.position;
        if (exploring && Time.time < exploreDeadline
            && (exploreTarget - from).sqrMagnitude > ArriveDistance * ArriveDistance) return;

        if (!FindUnexplored(from, out Vector3 target)) { exploring = false; return; }

        exploreTarget = target;
        exploring = true;
        exploreDeadline = Time.time + LegSeconds;
        pc.CommandAt(null, target);
    }

    /// <summary>
    /// The nearest reachable point on ground the colony has not seen, searched as
    /// rings outward from the character. Null-safe: no fog means nothing to find.
    /// </summary>
    static bool FindUnexplored(Vector3 from, out Vector3 target)
    {
        target = from;
        FogOfWar fog = FogOfWar.Instance;
        TerrainGrid grid = TerrainGrid.Instance;
        if (fog == null || grid == null) return false;

        for (float radius = 12f; radius <= 150f * TerrainGrid.SizeScale; radius += 8f)
        {
            int steps = Mathf.Max(8, Mathf.RoundToInt(radius * 0.6f));
            float offset = radius * 0.7f;   // so later rings do not sample the same bearings
            for (int i = 0; i < steps; i++)
            {
                float angle = (i * (Mathf.PI * 2f / steps)) + offset;
                Vector3 pos = from + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                pos.y = grid.SampleHeight(pos);
                if (fog.IsExplored(pos)) continue;
                if (!grid.IsReachable(pos)) continue;
                target = pos;
                return true;
            }
        }
        return false;
    }

    static bool HasDeposit(Inventory inv)
    {
        for (int i = 0; i < inv.SlotCount; i++)
        {
            Inventory.Slot slot = inv[i];
            if (!slot.IsEmpty && slot.item.kind != ItemKind.Tool) return true;
        }
        return false;
    }

    /// <summary>The nearest unfinished construction site of the player's own colony, or null.</summary>
    static ConstructionSite NearestSite(PlayerCharacter pc)
    {
        ConstructionSite best = null;
        float bestSq = float.MaxValue;
        Vector3 from = pc.transform.position;
        var list = ConstructionSite.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            ConstructionSite site = list[i];
            if (site == null || site.IsComplete) continue;
            if (site.Faction != Factions.Player) continue;
            float d = (site.transform.position - from).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = site; }
        }
        return best;
    }

    static GroundPickup NearestPickup(ItemDef item, PlayerCharacter pc)
    {
        GroundPickup best = null;
        float bestSq = float.MaxValue;
        Vector3 from = pc.transform.position;
        TerrainGrid grid = TerrainGrid.Instance;
        var list = GroundPickup.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            GroundPickup p = list[i];
            if (p == null || p.Item != item || p.IsClaimedByOther(pc)) continue;
            if (Banned(p)) continue;
            if (grid != null && !grid.IsReachable(p.transform.position)) continue;   // a cliff-side chunk (2026-09-17)
            float d = (p.transform.position - from).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = p; }
        }
        return best;
    }
}
#endif

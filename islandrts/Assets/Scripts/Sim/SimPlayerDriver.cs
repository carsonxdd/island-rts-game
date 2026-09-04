#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// The simulated player's hands (2026-09-03). Research and crafting need
/// someone at the bench and sticks and stone chunks that only the player's
/// character hand-collects, so a sweep has to drive the character the way a
/// human would: fetch what the front of the campfire queue is short of,
/// deposit it, stand at the bench until the queue runs dry. Nothing else —
/// the policies decide WHAT to queue; this only keeps it moving.
///
/// Polled once a game-second from SimRunner, before the policy. Until the
/// Crafter job (Slice 3) this is the sim's only bench labor.
/// </summary>
public static class SimPlayerDriver
{
    public static void Tick(SimState s)
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        BaseBuilding fire = s.Campfire;
        if (pc == null || pc.IsKnockedOut || fire == null) return;

        CraftStation station = fire.Station;
        if (station == null) return;

        // Mid-errand: let it finish (a stalled task drops itself)
        if (pc.HasTask) return;

        if (!station.HasWork)
        {
            // Nothing to do at the bench — bring home whatever is in hand
            if (!pc.Inventory.IsEmpty && HasDeposit(pc.Inventory)) pc.CommandDeposit(fire);
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
        if (pickup != null) pc.CommandCollect(pickup);
        // else: nothing on the island right now — wait for the trickle respawn
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

    static GroundPickup NearestPickup(ItemDef item, PlayerCharacter pc)
    {
        GroundPickup best = null;
        float bestSq = float.MaxValue;
        Vector3 from = pc.transform.position;
        var list = GroundPickup.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            GroundPickup p = list[i];
            if (p == null || p.Item != item || p.IsClaimedByOther(pc)) continue;
            float d = (p.transform.position - from).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = p; }
        }
        return best;
    }
}
#endif

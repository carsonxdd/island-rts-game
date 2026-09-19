using System;

/// <summary>
/// A fixed number of slots, each holding one stack of an <see cref="ItemDef"/>.
/// Plain C# (no MonoBehaviour, no serialization) — the character and the
/// campfire each own one. Allocates nothing after construction.
///
/// Adding fills existing stacks of the same item first, then empty slots, and
/// reports how much actually fit, so a caller can leave the remainder on the
/// ground rather than silently losing it.
/// </summary>
public class Inventory
{
    public struct Slot
    {
        public ItemDef item;
        public int count;
        public bool IsEmpty => item == null || count <= 0;
    }

    private readonly Slot[] slots;

    /// <summary>Fires after any change. UI listens here instead of polling.</summary>
    public event Action OnChanged;

    public Inventory(int slotCount)
    {
        slots = new Slot[Math.Max(1, slotCount)];
    }

    public int SlotCount => slots.Length;

    /// <summary>
    /// A ceiling on the TOTAL number of items held, over and above the slots
    /// (2026-09-03; the campfire stockpile uses it, the character's hands do
    /// not). A delegate rather than a number because the campfire's room grows
    /// with research: the cap is read at the point of effect, the same rule
    /// every other upgrade in this codebase follows. Null = slots are the only
    /// limit.
    /// </summary>
    public Func<int> totalCapacity;

    /// <summary>Every item in every slot, counted.</summary>
    public int TotalCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++) n += slots[i].count;
            return n;
        }
    }

    /// <summary>The current ceiling, or 0 when there is none.</summary>
    public int Capacity => totalCapacity != null ? totalCapacity() : 0;

    /// <summary>How many more items of any kind the total cap still allows (int.MaxValue with no cap).</summary>
    public int RoomLeft
    {
        get
        {
            if (totalCapacity == null) return int.MaxValue;
            return Math.Max(0, totalCapacity() - TotalCount);
        }
    }

    public Slot this[int index] => slots[index];

    public bool IsEmpty
    {
        get
        {
            for (int i = 0; i < slots.Length; i++)
                if (!slots[i].IsEmpty) return false;
            return true;
        }
    }

    public int UsedSlots
    {
        get
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (!slots[i].IsEmpty) n++;
            return n;
        }
    }

    /// <summary>How many of <paramref name="item"/> are held across all slots.</summary>
    public int Count(ItemDef item)
    {
        if (item == null) return 0;
        int n = 0;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].item == item) n += slots[i].count;
        return n;
    }

    /// <summary>How many more of <paramref name="item"/> would fit right now.</summary>
    public int SpaceFor(ItemDef item)
    {
        if (item == null) return 0;
        int space = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsEmpty) space += item.stackMax;
            else if (slots[i].item == item) space += item.stackMax - slots[i].count;
        }
        return Math.Min(space, RoomLeft);
    }

    /// <summary>Adds up to <paramref name="amount"/>; returns how many actually fit.</summary>
    public int Add(ItemDef item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        amount = Math.Min(amount, RoomLeft);
        if (amount <= 0) return 0;
        int remaining = amount;

        // Top up existing stacks first
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (slots[i].item != item || slots[i].IsEmpty) continue;
            int take = Math.Min(remaining, item.stackMax - slots[i].count);
            if (take <= 0) continue;
            slots[i].count += take;
            remaining -= take;
        }

        // Then open new stacks
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (!slots[i].IsEmpty) continue;
            int take = Math.Min(remaining, item.stackMax);
            slots[i].item = item;
            slots[i].count = take;
            remaining -= take;
        }

        int added = amount - remaining;
        if (added > 0) OnChanged?.Invoke();
        return added;
    }

    /// <summary>Removes up to <paramref name="amount"/>; returns how many were actually removed.</summary>
    public int Remove(ItemDef item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        int remaining = amount;

        // Drain the smallest stacks first so partial stacks disappear before full ones split
        for (int pass = 0; pass < slots.Length && remaining > 0; pass++)
        {
            int best = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].item != item || slots[i].IsEmpty) continue;
                if (best < 0 || slots[i].count < slots[best].count) best = i;
            }
            if (best < 0) break;

            int take = Math.Min(remaining, slots[best].count);
            slots[best].count -= take;
            remaining -= take;
            if (slots[best].count <= 0) { slots[best].item = null; slots[best].count = 0; }
        }

        int removed = amount - remaining;
        if (removed > 0) OnChanged?.Invoke();
        return removed;
    }

    /// <summary>Empties one slot entirely; returns what it held.</summary>
    public Slot TakeSlot(int index)
    {
        Slot s = slots[index];
        if (s.IsEmpty) return s;
        slots[index].item = null;
        slots[index].count = 0;
        OnChanged?.Invoke();
        return s;
    }

    public void Clear()
    {
        bool changed = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].IsEmpty) changed = true;
            slots[i].item = null;
            slots[i].count = 0;
        }
        if (changed) OnChanged?.Invoke();
    }
}

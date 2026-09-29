using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared "is there a complete NavMesh path from about here to that?" answers for
/// the raid (2026-09-28, the raid-night lag pass). A raid is a cluster: thirty
/// raiders a few metres apart all asking whether they can reach the same fire, the
/// same hut or the same warrior, a second apart, got thirty identical
/// <c>NavMesh.CalculatePath</c> answers. One answer per (thing, coarse cell of the
/// asker) now serves them all for <see cref="Ttl"/> seconds.
/// </summary>
/// <remarks>
/// <para>Every cache is cleared the moment a wall or gate falls — that is the one
/// event that turns "no path" into "path" mid-raid. A wall going UP can turn "path"
/// into "no path", and the TTL is what bounds that (a raider that walks a stale
/// answer is re-picked by its StuckResolver, the same as a throttled optimistic
/// answer always was).</para>
/// <para>Only real answers are stored: a throttled <c>AINavHelper.TryCalculatePath</c>
/// is not an answer. No allocation after a cache warms up; a cache past
/// <see cref="MaxEntries"/> starts over rather than growing.</para>
/// </remarks>
public sealed class RaidPathCache
{
    const int MaxEntries = 1024;

    struct Entry { public bool ok; public float time; }

    readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>(128);
    readonly float cellSize;
    readonly float ttl;

    /// <summary>Seconds one answer stands.</summary>
    public float Ttl => ttl;

    static readonly List<RaidPathCache> all = new List<RaidPathCache>(4);
    static bool hooked;

    public RaidPathCache(float cellSize, float ttl)
    {
        this.cellSize = Mathf.Max(0.5f, cellSize);
        this.ttl = ttl;
        all.Add(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        hooked = false;
        for (int i = 0; i < all.Count; i++) all[i].entries.Clear();
    }

    /// <summary>Wall and gate deaths clear every cache. Idempotent, called lazily on first use.</summary>
    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        Wall.OnAnyWallDestroyed += ClearAll;
        Gate.OnAnyGateDestroyed += ClearAll;
    }

    /// <summary>Forget every answer in every cache (a wall or gate fell).</summary>
    public static void ClearAll()
    {
        for (int i = 0; i < all.Count; i++) all[i].entries.Clear();
    }

    long Key(int id, Vector3 from)
    {
        int cx = Mathf.FloorToInt(from.x / cellSize) & 0xFFFF;
        int cz = Mathf.FloorToInt(from.z / cellSize) & 0xFFFF;
        return ((long)id << 32) | ((long)cx << 16) | (long)cz;
    }

    /// <summary>A stored, fresh answer for <paramref name="id"/> asked from about <paramref name="from"/>.</summary>
    public bool TryGet(int id, Vector3 from, out bool ok)
    {
        Hook();
        Entry e;
        if (entries.TryGetValue(Key(id, from), out e) && Time.time - e.time < ttl)
        {
            ok = e.ok;
            return true;
        }
        ok = false;
        return false;
    }

    public void Put(int id, Vector3 from, bool ok)
    {
        Hook();
        if (entries.Count >= MaxEntries) entries.Clear();
        entries[Key(id, from)] = new Entry { ok = ok, time = Time.time };
    }
}

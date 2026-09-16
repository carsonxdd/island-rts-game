using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The shared spatial awareness layer (2026-09-16): a uniform grid of every
/// unit (workers, warriors, raiders, the castaway), cell 2 x the avoidance
/// radius, rebuilt at <see cref="TickHz"/> from the ActiveLists — never per
/// frame, never an O(n^2) distance loop. <see cref="Query"/> is a 3x3 cell
/// lookup. On the same tick it runs the soft separation of the two-radius
/// model: two units overlapping inside twice <see cref="UnitSpacing.SolidRadius"/>
/// get an equal-and-opposite positional push through NavMeshAgent.Move,
/// clamped to <see cref="UnitSpacing.MaxSeparationSpeed"/>; a stationary unit
/// (working, idle, sheltering — no path or stopped) is never moved, movers go
/// round it. It also applies the gateway right-of-way rule live.
/// </summary>
/// <remarks>
/// Runtime-added by <see cref="Ensure"/> (every unit's StuckResolver calls it
/// in Initialize), so its public fields are the LIVE values — never put it in
/// a scene. Zero allocation after warm-up: entries live in arrays grown by
/// doubling, the grid is a flat head[] + next[] linked list over a map-sized
/// cell field, and Query writes into a caller buffer.
/// </remarks>
public class UnitGrid : MonoBehaviour
{
    public static UnitGrid Instance { get; private set; }

    public const float TickHz = 20f;
    public const float CellSize = UnitSpacing.AvoidanceRadius * 2f;   // ~1.1 m

    public struct Entry
    {
        public Transform transform;
        public NavMeshAgent agent;
        public Vector3 position;
        public bool stationary;
        public int basePriority;   // the priority the unit had outside a gateway
    }

    Entry[] entries = new Entry[64];
    int count;
    int[] head;      // cell -> first entry index, -1 when empty
    int[] next;      // entry -> next entry in the same cell
    int cellsPerSide;
    float half;
    float nextTick;

    public static void Ensure()
    {
        if (Instance != null) return;
        var go = new GameObject("UnitGrid");
        Instance = go.AddComponent<UnitGrid>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        half = TerrainGrid.Instance != null ? TerrainGrid.Half + 8f : 120f;
        cellsPerSide = Mathf.CeilToInt(half * 2f / CellSize) + 1;
        head = new int[cellsPerSide * cellsPerSide];
        next = new int[64];
        for (int i = 0; i < head.Length; i++) head[i] = -1;
        StuckResolver.ResetCounters();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (Time.time < nextTick) return;
        float dt = Time.time - (nextTick - 1f / TickHz);
        nextTick = Time.time + 1f / TickHz;
        if (dt <= 0f || dt > 0.5f) dt = 1f / TickHz;
        Rebuild();
        Separate(dt);
    }

    // ---- build ---------------------------------------------------------------

    void Rebuild()
    {
        count = 0;
        for (int i = 0; i < head.Length; i++) head[i] = -1;
        Add(Worker.ActiveList);
        Add(Warrior.ActiveList);
        Add(Enemy.ActiveList);
        PlayerCharacter pc = PlayerCharacter.Instance;
        if (pc != null) Add(pc.transform, pc.GetComponent<NavMeshAgent>());
    }

    void Add<T>(IReadOnlyList<T> list) where T : MonoBehaviour
    {
        for (int i = 0; i < list.Count; i++)
        {
            T u = list[i];
            if (u == null) continue;
            Add(u.transform, u.GetComponent<NavMeshAgent>());
        }
    }

    void Add(Transform t, NavMeshAgent agent)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (count == entries.Length)
        {
            System.Array.Resize(ref entries, entries.Length * 2);
            System.Array.Resize(ref next, next.Length * 2);
        }
        Vector3 p = t.position;
        int cell = CellOf(p);
        if (cell < 0) return;
        bool stationary = agent.isStopped || !agent.hasPath || agent.velocity.sqrMagnitude < 0.01f;
        entries[count] = new Entry { transform = t, agent = agent, position = p, stationary = stationary,
                                     basePriority = agent.avoidancePriority };
        next[count] = head[cell];
        head[cell] = count;
        count++;
    }

    int CellOf(Vector3 p)
    {
        int x = Mathf.FloorToInt((p.x + half) / CellSize);
        int z = Mathf.FloorToInt((p.z + half) / CellSize);
        if (x < 0 || z < 0 || x >= cellsPerSide || z >= cellsPerSide) return -1;
        return z * cellsPerSide + x;
    }

    // ---- query -----------------------------------------------------------------

    /// <summary>Entries within <paramref name="radius"/> of <paramref name="pos"/>, written into <paramref name="buffer"/>; returns how many (capped at the buffer).</summary>
    public int Query(Vector3 pos, float radius, Entry[] buffer)
    {
        int n = 0;
        int cx = Mathf.FloorToInt((pos.x + half) / CellSize);
        int cz = Mathf.FloorToInt((pos.z + half) / CellSize);
        int reach = Mathf.Max(1, Mathf.CeilToInt(radius / CellSize));
        float sqr = radius * radius;
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            if (z < 0 || z >= cellsPerSide) continue;
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                if (x < 0 || x >= cellsPerSide) continue;
                for (int e = head[z * cellsPerSide + x]; e >= 0; e = next[e])
                {
                    Vector3 d = entries[e].position - pos;
                    d.y = 0f;
                    if (d.sqrMagnitude > sqr) continue;
                    if (n < buffer.Length) buffer[n] = entries[e];
                    n++;
                }
            }
        }
        return n;
    }

    // ---- soft separation + the gateway rule -----------------------------------

    /// <summary>Overlaps resolved on the last tick (the F3 overlay shows it).</summary>
    public int LastPushes { get; private set; }

    void Separate(float dt)
    {
        int pushes = 0;
        float minDist = UnitSpacing.SolidRadius * 2f;
        float minSqr = minDist * minDist;
        float maxStep = UnitSpacing.MaxSeparationSpeed * dt;

        for (int a = 0; a < count; a++)
        {
            ref Entry ea = ref entries[a];

            // The chokepoint rule: a unit inside a gateway outranks anyone entering it.
            if (!ea.stationary || ea.agent.avoidancePriority != Worker.StationaryAvoidancePriority)
            {
                bool inGate = Gate.ActiveList.Count > 0 && UnitSpacing.InGateway(ea.position);
                if (inGate && ea.agent.avoidancePriority > UnitSpacing.GatewayPriority)
                {
                    if (ea.agent.avoidancePriority != UnitSpacing.GatewayPriority) ea.basePriority = ea.agent.avoidancePriority;
                    ea.agent.avoidancePriority = UnitSpacing.GatewayPriority;
                }
                else if (!inGate && ea.agent.avoidancePriority == UnitSpacing.GatewayPriority)
                {
                    ea.agent.avoidancePriority = ea.basePriority > UnitSpacing.GatewayPriority ? ea.basePriority : UnitSpacing.EmptyPriority;
                }
            }

            // Pairs in this cell and the three cells ahead of it (each pair once)
            int cell = CellOf(ea.position);
            if (cell < 0) continue;
            int cx = cell % cellsPerSide, cz = cell / cellsPerSide;
            for (int dz = 0; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dz == 0 && dx < 0) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= cellsPerSide || z >= cellsPerSide) continue;
                    for (int b = head[z * cellsPerSide + x]; b >= 0; b = next[b])
                    {
                        if (dz == 0 && dx == 0 && b <= a) continue;
                        ref Entry eb = ref entries[b];
                        if (ea.stationary && eb.stationary) continue;
                        Vector3 d = eb.position - ea.position;
                        d.y = 0f;
                        float sqr = d.sqrMagnitude;
                        if (sqr >= minSqr) continue;
                        float dist = Mathf.Sqrt(sqr);
                        Vector3 dir = dist > 0.001f ? d / dist : new Vector3(1f, 0f, 0f);
                        float overlap = minDist - dist;
                        float step = Mathf.Min(overlap * 0.5f, maxStep);
                        // A stationary unit takes none of it: the mover takes both halves
                        if (ea.stationary) { Push(ref eb, dir * (step * 2f)); }
                        else if (eb.stationary) { Push(ref ea, -dir * (step * 2f)); }
                        else { Push(ref ea, -dir * step); Push(ref eb, dir * step); }
                        pushes++;
                    }
                }
            }
        }
        LastPushes = pushes;
    }

    static void Push(ref Entry e, Vector3 delta)
    {
        if (e.agent == null || !e.agent.isOnNavMesh) return;
        e.agent.Move(delta);
        e.position += delta;
    }
}

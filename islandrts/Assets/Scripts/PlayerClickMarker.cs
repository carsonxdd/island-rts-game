using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Right-click feedback for the castaway (2026-09-13): a small green ring on
/// the ground where you clicked that fades in about a second, and a thin trail
/// along the path the character is about to walk, shown while they walk and
/// gone on arrival. Every command click flashes a ring, queued ones included,
/// so a Shift-click run reads as a row of rings.
///
/// Runtime-added by <see cref="PlayerCharacter"/> (public fields are the live
/// values; never put it in a scene). Nothing here decides anything: it is
/// skipped headless and never draws from <c>UnityEngine.Random</c>. Rings are a
/// pool of <see cref="LineRenderer"/> loops and the trail one more, on the
/// vertex-coloured Sprites/Default shader the no-build outlines already use, so
/// no material is instanced per frame. Corners come through
/// <see cref="NavMeshPath.GetCornersNonAlloc"/> into a fixed buffer: zero GC.
/// </summary>
public class PlayerClickMarker : MonoBehaviour
{
    public static PlayerClickMarker Instance { get; private set; }

    [Tooltip("Seconds a click ring takes to fade out.")]
    public float ringSeconds = 0.9f;
    [Tooltip("Ring radius when it appears; it grows a little as it fades.")]
    public float ringRadius = 0.35f;
    public float ringGrow = 0.2f;
    public float ringWidth = 0.08f;
    public Color ringColor = new Color(0.35f, 1f, 0.45f, 0.95f);

    [Tooltip("Width of the path trail.")]
    public float trailWidth = 0.07f;
    public Color trailColor = new Color(0.35f, 1f, 0.45f, 0.5f);
    public Color trailEndColor = new Color(0.35f, 1f, 0.45f, 0.12f);
    [Tooltip("Height above the ground for both the rings and the trail.")]
    public float lift = 0.08f;
    [Tooltip("The trail hides once the character is this close to the end of the path.")]
    public float trailHideDistance = 0.4f;

    const int RingPool = 8;
    const int RingSegments = 20;
    const int MaxCorners = 32;
    // A path corner sits on the NavMesh but the straight run between two corners
    // does not follow the ground: over a rise it cut under the hill. Each segment
    // is sampled every TrailStep metres and every sample is draped.
    const float TrailStep = 0.75f;
    const int MaxTrailPoints = 192;

    private NavMeshAgent agent;
    private Material material;

    private LineRenderer[] rings;
    private float[] ringStart;      // Time.time the ring was flashed, < 0 = free
    private Vector3[] ringCenter;
    private int nextRing;

    private LineRenderer trail;
    private readonly Vector3[] corners = new Vector3[MaxCorners];
    private readonly Vector3[] draped = new Vector3[MaxTrailPoints];

    /// <summary>Create the marker for this agent's owner (once). Headless sims draw nothing.</summary>
    public static void Attach(NavMeshAgent agent)
    {
        if (SimHooks.Headless || agent == null) return;
        if (Instance == null)
        {
            var go = new GameObject("PlayerClickMarker");
            Instance = go.AddComponent<PlayerClickMarker>();
        }
        Instance.agent = agent;
    }

    /// <summary>Flash a ring on the ground at <paramref name="point"/>. Safe with no instance.</summary>
    public static void Flash(Vector3 point)
    {
        if (Instance != null) Instance.FlashRing(point);
    }

    void Awake()
    {
        material = new Material(Shader.Find("Sprites/Default"));

        rings = new LineRenderer[RingPool];
        ringStart = new float[RingPool];
        ringCenter = new Vector3[RingPool];
        for (int i = 0; i < RingPool; i++)
        {
            rings[i] = MakeLine("Ring", true, RingSegments);
            rings[i].enabled = false;
            ringStart[i] = -1f;
        }

        trail = MakeLine("Trail", false, 2);
        trail.enabled = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (material != null) Destroy(material);
    }

    LineRenderer MakeLine(string name, bool loop, int points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.positionCount = points;
        lr.sharedMaterial = material;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        return lr;
    }

    void FlashRing(Vector3 point)
    {
        int i = nextRing;
        nextRing = (nextRing + 1) % RingPool;
        ringStart[i] = Time.time;
        ringCenter[i] = point;
        ringCenter[i].y = GroundY(point);
        rings[i].enabled = true;
        LayRing(i, 0f);
    }

    void LateUpdate()
    {
        float now = Time.time;
        for (int i = 0; i < RingPool; i++)
        {
            if (ringStart[i] < 0f) continue;
            float t = (now - ringStart[i]) / Mathf.Max(0.05f, ringSeconds);
            if (t >= 1f)
            {
                ringStart[i] = -1f;
                rings[i].enabled = false;
                continue;
            }
            LayRing(i, t);
        }

        UpdateTrail();
    }

    /// <summary>Lay ring <paramref name="i"/>'s points at fade fraction <paramref name="t"/> (0 fresh, 1 gone).</summary>
    void LayRing(int i, float t)
    {
        LineRenderer lr = rings[i];
        float r = ringRadius + ringGrow * t;
        Vector3 c = ringCenter[i];
        for (int s = 0; s < RingSegments; s++)
        {
            float a = s * (Mathf.PI * 2f / RingSegments);
            Vector3 p = new Vector3(c.x + Mathf.Cos(a) * r, 0f, c.z + Mathf.Sin(a) * r);
            p.y = GroundY(p) + lift;
            lr.SetPosition(s, p);
        }
        Color col = ringColor;
        col.a *= 1f - t * t;
        lr.startColor = col;
        lr.endColor = col;
        lr.startWidth = ringWidth;
        lr.endWidth = ringWidth;
    }

    void UpdateTrail()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || !agent.hasPath || agent.pathPending
            || agent.isStopped || agent.remainingDistance <= agent.stoppingDistance + trailHideDistance)
        {
            if (trail.enabled) trail.enabled = false;
            return;
        }

        int n = agent.path.GetCornersNonAlloc(corners);
        if (n < 2)
        {
            if (trail.enabled) trail.enabled = false;
            return;
        }

        // Corner 0 is where the agent was when the path was computed; draw from the
        // feet, and drape every step of every segment so the line rides the hills.
        Vector3 prev = agent.transform.position;
        int count = 0;
        draped[count++] = new Vector3(prev.x, GroundY(prev) + lift, prev.z);
        for (int i = 1; i < n && count < MaxTrailPoints; i++)
        {
            Vector3 c = corners[i];
            float dx = c.x - prev.x, dz = c.z - prev.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / TrailStep));
            for (int s = 1; s <= steps && count < MaxTrailPoints; s++)
            {
                float t = (float)s / steps;
                Vector3 p = new Vector3(prev.x + dx * t, 0f, prev.z + dz * t);
                p.y = GroundY(p) + lift;
                draped[count++] = p;
            }
            prev = c;
        }

        if (trail.positionCount != count) trail.positionCount = count;
        trail.SetPositions(draped);   // SetPositions reads positionCount entries from the front
        trail.startWidth = trailWidth;
        trail.endWidth = trailWidth;
        trail.startColor = trailColor;
        trail.endColor = trailEndColor;
        if (!trail.enabled) trail.enabled = true;
    }

    static float GroundY(Vector3 p)
    {
        TerrainGrid grid = TerrainGrid.Instance;
        return grid != null ? grid.SampleHeight(p) : p.y;
    }
}

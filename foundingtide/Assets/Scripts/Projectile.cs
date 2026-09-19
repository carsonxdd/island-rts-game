using UnityEngine;

/// <summary>
/// An arrow in flight (2026-09-04, Slice 5): a thin runtime-built box that
/// arcs from the archer to the target's LIVE position and applies the damage
/// it carries on arrival. Pooled by <see cref="CombatEffects.FireArrow"/> —
/// one shared mesh and material for every arrow, a fixed pool of 32, nothing
/// allocated once the pool is warm.
/// </summary>
/// <remarks>
/// No line of sight and no collision: the archer's job is to shoot over the
/// wall, and a miss would only mean the same damage a frame later. The target
/// can die in flight; the arrow then lands where it was going and does nothing.
/// The damage is decided when the arrow is loosed (tower buff included), so a
/// warrior stepping out of a tower's ring mid-flight changes nothing.
/// </remarks>
public class Projectile : MonoBehaviour
{
    private const float Speed = 18f;          // world units per second along the ground
    private const float ArcHeight = 0.18f;    // fraction of the distance the arc rises at its peak
    private const float TargetHeight = 0.9f;  // aim at the body, not the feet

    private static Mesh sharedMesh;
    private static Material sharedMaterial;

    private Transform target;
    private Health targetHealth;
    private float damage;
    private Vector3 start;
    private Vector3 lastKnown;
    private float duration;
    private float elapsed;
    private bool flying;

    public bool Flying => flying;

    /// <summary>Build the arrow's own renderer once; the mesh and material are shared by the pool.</summary>
    public static Projectile Create(Transform parent)
    {
        if (sharedMesh == null) sharedMesh = BuildMesh();
        if (sharedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            sharedMaterial = new Material(shader) { color = new Color(0.55f, 0.42f, 0.28f) };
        }

        GameObject go = new GameObject("Arrow");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sharedMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Projectile p = go.AddComponent<Projectile>();
        go.SetActive(false);
        return p;
    }

    /// <summary>Loose the arrow. <paramref name="targetHealth"/> may be null (the arrow then only flies).</summary>
    public void Launch(Vector3 from, Transform to, Health targetHealth, float damage)
    {
        target = to;
        this.targetHealth = targetHealth;
        this.damage = damage;
        start = from;
        lastKnown = to != null ? to.position + Vector3.up * TargetHeight : from;
        float dist = Vector3.Distance(start, lastKnown);
        duration = Mathf.Max(0.08f, dist / Speed);
        elapsed = 0f;
        flying = true;
        transform.position = start;
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (!flying) return;

        if (target != null) lastKnown = target.position + Vector3.up * TargetHeight;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        // Straight line to the live target, lifted by a parabola
        Vector3 pos = Vector3.Lerp(start, lastKnown, t);
        float rise = Vector3.Distance(start, lastKnown) * ArcHeight;
        pos.y += 4f * rise * t * (1f - t);

        // Face along the velocity so the shaft reads as flying, not sliding
        Vector3 ahead = Vector3.Lerp(start, lastKnown, Mathf.Clamp01(t + 0.05f));
        ahead.y += 4f * rise * (t + 0.05f) * (1f - (t + 0.05f));
        Vector3 dir = ahead - pos;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        transform.position = pos;

        if (t >= 1f) Land();
    }

    void Land()
    {
        flying = false;
        gameObject.SetActive(false);

        if (targetHealth != null && targetHealth.IsAlive)
        {
            targetHealth.TakeDamage(damage);
            if (CombatEffects.Instance != null) CombatEffects.Instance.SpawnHitEffect(lastKnown, damage);
            DevQuests.Signal("arrow_hit");
        }
        target = null;
        targetHealth = null;
    }

    /// <summary>A 0.6 m shaft along +Z with a slightly wider head, 12 triangles.</summary>
    static Mesh BuildMesh()
    {
        const float len = 0.6f, r = 0.018f;
        Vector3[] v =
        {
            // shaft box: 8 corners
            new Vector3(-r, -r, -len * 0.5f), new Vector3( r, -r, -len * 0.5f),
            new Vector3( r,  r, -len * 0.5f), new Vector3(-r,  r, -len * 0.5f),
            new Vector3(-r, -r,  len * 0.5f), new Vector3( r, -r,  len * 0.5f),
            new Vector3( r,  r,  len * 0.5f), new Vector3(-r,  r,  len * 0.5f),
        };
        int[] tris =
        {
            0, 2, 1,  0, 3, 2,   // back
            4, 5, 6,  4, 6, 7,   // front
            0, 1, 5,  0, 5, 4,   // bottom
            2, 3, 7,  2, 7, 6,   // top
            1, 2, 6,  1, 6, 5,   // right
            0, 4, 7,  0, 7, 3,   // left
        };
        Mesh m = new Mesh { name = "Arrow" };
        m.vertices = v;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}

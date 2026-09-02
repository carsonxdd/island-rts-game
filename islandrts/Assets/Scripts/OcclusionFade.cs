using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fades a tree out while it stands between the camera and a unit, so workers,
/// warriors and enemies are never lost behind a canopy.
///
/// Deciding WHEN to fade belongs to <see cref="OcclusionFadeManager"/>, which tests
/// every tree against every unit once per tick; this component owns only the visual,
/// so the expensive part happens once per tick rather than once per tree per frame.
/// </summary>
/// <remarks>
/// Two things make this cheap enough to run on every tree on the island.
///
/// It reuses the material instances <see cref="ResourceNode"/> already created for its
/// hover highlight. Reading <c>renderer.materials</c> instantiates, so collecting a
/// second time would leave the highlight and the fade writing to different copies of
/// the same material and neither would look right.
///
/// URP's Lit shader is switched between opaque and transparent ONLY when the fade
/// crosses in or out of full opacity, never per frame. Between those crossings the
/// per-frame cost is one colour write per material slot, and a fully opaque tree
/// early-outs of Update entirely.
/// </remarks>
[DisallowMultipleComponent]
public class OcclusionFade : MonoBehaviour
{
    public static System.Collections.Generic.IReadOnlyList<OcclusionFade> ActiveList
        => ActiveRegistry<OcclusionFade>.List;

    [Tooltip("Alpha to fade to while the tree is hiding a unit.")]
    public float fadedAlpha = 0.3f;
    [Tooltip("Alpha units per second - the same rate both ways, so fading out and back feel symmetric.")]
    public float fadeSpeed = 4f;

    /// <summary>World-space height of the silhouette, measured from the renderer bounds.</summary>
    public float SilhouetteHeight { get; private set; } = 4f;
    /// <summary>World-space half-width of the silhouette, measured from the renderer bounds.</summary>
    public float SilhouetteRadius { get; private set; } = 1.4f;

    private Material[] materials;
    private float[] baseAlpha;
    private float current = 1f;
    private float target = 1f;
    private bool transparentMode = false;
    private bool measured = false;

    void Awake()
    {
        ActiveRegistry<OcclusionFade>.Register(this);
        OcclusionFadeManager.Ensure();
    }

    void OnDestroy()
    {
        ActiveRegistry<OcclusionFade>.Unregister(this);
    }

    /// <summary>
    /// Measure the silhouette from the renderer bounds, lazily: TreeVariance swaps the
    /// mesh and jitters the Model child's scale in its own Start, and Start order between
    /// two components on one object is undefined, so measuring in Start could read the
    /// pre-variance bounds.
    /// </summary>
    public void EnsureMeasured()
    {
        if (measured) return;
        measured = true;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        SilhouetteHeight = Mathf.Max(0.5f, b.max.y - transform.position.y);
        SilhouetteRadius = Mathf.Max(0.3f, Mathf.Max(b.extents.x, b.extents.z));
    }

    /// <summary>Called by the manager each tick with whether this tree is currently hiding a unit.</summary>
    public void SetOccluding(bool occluding)
    {
        target = occluding ? Mathf.Clamp01(fadedAlpha) : 1f;
    }

    void Update()
    {
        if (current >= 1f && target >= 1f) return;

        current = Mathf.MoveTowards(current, target, fadeSpeed * Time.deltaTime);

        if (materials == null) FetchMaterials();
        if (materials == null) return;

        bool wantTransparent = current < 0.999f;
        if (wantTransparent != transparentMode)
        {
            for (int i = 0; i < materials.Length; i++) SetSurface(materials[i], wantTransparent);
            transparentMode = wantTransparent;
        }

        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m == null) continue;
            Color c = m.color;
            c.a = baseAlpha[i] * current;
            m.color = c;
        }
    }

    void FetchMaterials()
    {
        // Share the ResourceNode's instances so the hover highlight and the fade write
        // to the same materials.
        ResourceNode node = GetComponent<ResourceNode>();
        materials = node != null
            ? node.EnsureNodeMaterials()
            : RendererTint.Collect(GetComponentsInChildren<Renderer>());

        baseAlpha = new float[materials.Length];
        for (int i = 0; i < materials.Length; i++)
            baseAlpha[i] = materials[i] != null ? materials[i].color.a : 1f;
    }

    /// <summary>
    /// Flip a URP Lit material between opaque and alpha-blended. Unity's material
    /// inspector writes exactly these properties and keywords and there is no runtime
    /// API for it, so they are set by hand. Restoring uses renderQueue -1, which means
    /// "whatever the shader declares".
    /// </summary>
    static void SetSurface(Material m, bool transparent)
    {
        if (m == null) return;

        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = -1;
        }
    }
}

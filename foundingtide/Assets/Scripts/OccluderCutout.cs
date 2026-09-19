using UnityEngine;

/// <summary>
/// Puts this object's material instances on the <c>FoundingTide/OccluderCutout</c> shader,
/// so the object stays solid except for a soft round window wherever a unit stands
/// behind it. Replaces the whole-object alpha fade (2026-09-08): a ghosted tree read as
/// a bug, a window in a canopy reads as a window.
/// </summary>
/// <remarks>
/// Deciding WHERE the windows are belongs to <see cref="UnitHoleMask"/>, which draws one
/// screen-space disc per unit into a global texture every frame. The shader samples it
/// and clips any fragment nearer the camera than the unit under it, so there is no
/// per-object "does this silhouette cover that worker" test left to be wrong — the old
/// manager's base-to-top segment guess is what made the fade miss some trees. Per-frame
/// cost on this component is nothing: it swaps the shader once and disables itself.
///
/// It reuses the material instances the object already collected for its hover
/// highlight (<see cref="IMaterialSet"/>), because reading <c>renderer.materials</c>
/// instantiates and two collectors would leave the glow and the cutout on different
/// copies. Only materials on URP Lit are swapped: a health bar sprite or a name label
/// under the same root keeps its own shader. Property names are shared with Lit, so
/// colour, smoothness and the hover glow's <c>_EmissionColor</c> carry across untouched.
/// </remarks>
[DisallowMultipleComponent]
public class OccluderCutout : MonoBehaviour
{
    private const string CutoutShaderPath = "Shaders/OccluderCutout";
    private const string LitShaderName = "Universal Render Pipeline/Lit";

    /// <summary>
    /// Shader keyword that turns the unit windows on for a material. The same shader
    /// also carries the fog of war, and the ground uses it with this OFF (see
    /// <see cref="FogMaterials"/>) so terrain never cuts a hole around anyone.
    /// </summary>
    public const string UnitCutoutKeyword = "_UNIT_CUTOUT";

    private static Shader cutoutShader;
    private static bool shaderLooked;

    private bool applied;

    /// <summary>Attach to a building or wall that should open a window for units behind it. Idempotent.</summary>
    public static void AttachTo(GameObject go)
    {
        if (go == null || go.GetComponent<OccluderCutout>() != null) return;
        go.AddComponent<OccluderCutout>();
    }

    void Awake()
    {
        UnitHoleMask.Ensure();
    }

    /// <summary>
    /// Applied on the first LateUpdate, not in Start: the object's own Start collects its
    /// materials and Start order between components on one object is undefined, so this
    /// waits until every Start of the spawn frame has run.
    /// </summary>
    void LateUpdate()
    {
        if (!applied) Apply();
        enabled = false;
    }

    void Apply()
    {
        applied = true;
        if (SimHooks.Headless) return;   // headless: nothing renders, keep the sim's materials stock

        if (!shaderLooked)
        {
            shaderLooked = true;
            cutoutShader = Resources.Load<Shader>(CutoutShaderPath);
            if (cutoutShader == null)
                Debug.LogWarning("OccluderCutout: Resources/" + CutoutShaderPath + " missing — occluders stay solid this run.");
        }
        if (cutoutShader == null) return;

        // Share whatever this object already collected; only an object with no collector
        // of its own (a hut, a wall) gets one here.
        IMaterialSet owner = GetComponent<IMaterialSet>();
        Material[] materials = owner != null
            ? owner.EnsureMaterials()
            : RendererTint.Collect(GetComponentsInChildren<Renderer>());
        if (materials == null) return;

        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m == null || m.shader == null || m.shader.name != LitShaderName) continue;
            m.shader = cutoutShader;
            m.EnableKeyword(UnitCutoutKeyword);
        }
    }
}

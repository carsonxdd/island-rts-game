using UnityEngine;

/// <summary>
/// Makes a thing glow: a breathing HDR emission on every material slot, used for
/// mouse-hover feedback on nodes and buildings and for the constant "you can pick this
/// up" shimmer on ground pickups (2026-09-03).
///
/// This replaced flat colour tinting. Writing <c>material.color</c> yellow washed the
/// object out, fought <see cref="OcclusionFade"/> (which writes the alpha of the same
/// colour), and read as a bug rather than as feedback. Emission is a separate property,
/// so the fade and the glow can both be live on one tree without either restoring the
/// other's value, and it costs nothing extra to draw: the campfire already proved that
/// an HDR emissive above the Bloom threshold of 1.0 blooms on its own.
/// </summary>
/// <remarks>
/// Three rules hold this together.
///
/// It never collects its own materials when something else already did.
/// <see cref="ResourceNode.EnsureNodeMaterials"/> is the single collector for a node,
/// because reading <c>renderer.materials</c> INSTANTIATES - a second collector would
/// leave the two writing to different copies of the same material. Callers hand their
/// array to <see cref="Bind"/>.
///
/// The glow colour is each material's own base colour pushed toward warm gold, so a palm
/// glows green-gold and a rock glows grey-gold; one flat highlight colour for everything
/// is what made the old tint read as "this object broke".
///
/// It runs on unscaled time and early-outs entirely when both the current and the target
/// intensity are zero, so an island of unhovered trees costs one float compare each per
/// frame.
/// </remarks>
[DisallowMultipleComponent]
public class HoverGlow : MonoBehaviour
{
    [Tooltip("Glow held with nothing hovering it. 0 = dark until hovered; pickups sit around 0.5.")]
    public float idleIntensity = 0f;
    [Tooltip("Glow while the mouse is over this object.")]
    public float hoverIntensity = 2.4f;
    [Tooltip("How much of the intensity breathes in and out, as a fraction of it.")]
    public float pulseAmount = 0.3f;
    [Tooltip("Radians per second of the breathing sine - roughly one slow pulse.")]
    public float pulseSpeed = 2.4f;
    [Tooltip("Intensity units per second when rising or falling toward the target.")]
    public float riseSpeed = 9f;
    [Tooltip("How far the glow colour is pushed from the material's own colour toward warm gold.")]
    [Range(0f, 1f)] public float goldBlend = 0.55f;

    static readonly Color Gold = new Color(1f, 0.86f, 0.52f);

    private Material[] materials;
    private Color[] glowColor;        // per slot: the colour that gets scaled by intensity
    private Color[] originalEmission;
    private bool[] hadEmission;
    private bool captured;

    private float current;
    private bool hovered;
    private float phase;              // so a shore full of sticks does not pulse in lockstep

    /// <summary>
    /// Add (or fetch) the glow on <paramref name="go"/> and point it at material
    /// instances someone else already collected. Returns null under the sim, which has
    /// no camera and no use for cosmetics.
    /// </summary>
    public static HoverGlow Attach(GameObject go, Material[] mats, float idle, float hover, float gold = 0.55f)
    {
        if (go == null || SimHooks.Simulating) return null;

        HoverGlow glow = go.GetComponent<HoverGlow>();
        if (glow == null) glow = go.AddComponent<HoverGlow>();
        glow.idleIntensity = idle;
        glow.hoverIntensity = hover;
        glow.goldBlend = gold;
        glow.Bind(mats);
        return glow;
    }

    void Awake()
    {
        phase = Random.value * Mathf.PI * 2f;
    }

    /// <summary>Use these material instances rather than collecting our own.</summary>
    public void Bind(Material[] mats)
    {
        materials = mats;
        captured = false;
    }

    /// <summary>Mouse entered / left. Nodes and buildings drive this from their OnMouse events.</summary>
    public void SetHovered(bool value) { hovered = value; }

    void OnDisable()
    {
        // Never leave a slot lit: a node's materials outlive the component when the
        // object is torn down mid-hover.
        current = 0f;
        hovered = false;
        Apply(0f);
    }

    void Update()
    {
        float target = hovered ? hoverIntensity : idleIntensity;
        if (current <= 0.0001f && target <= 0.0001f) return;

        current = Mathf.MoveTowards(current, target, riseSpeed * Time.unscaledDeltaTime);

        float breath = 1f + pulseAmount * Mathf.Sin(Time.unscaledTime * pulseSpeed + phase);
        Apply(current * breath);
    }

    void Apply(float intensity)
    {
        if (materials == null) return;
        if (!captured) Capture();
        if (glowColor == null) return;

        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m == null) continue;

            if (intensity <= 0.0001f)
            {
                m.SetColor("_EmissionColor", originalEmission[i]);
                if (!hadEmission[i]) m.DisableKeyword("_EMISSION");
            }
            else
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", glowColor[i] * intensity + originalEmission[i]);
            }
        }
    }

    /// <summary>
    /// Snapshot what each slot was emitting and work out what it should glow. Lazy,
    /// because a node's materials are collected on first use and TreeVariance may still
    /// be swapping them during Start.
    /// </summary>
    void Capture()
    {
        captured = true;
        glowColor = new Color[materials.Length];
        originalEmission = new Color[materials.Length];
        hadEmission = new bool[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m == null) continue;

            hadEmission[i] = m.IsKeywordEnabled("_EMISSION");
            originalEmission[i] = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            if (m.globalIlluminationFlags == MaterialGlobalIlluminationFlags.EmissiveIsBlack)
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // The object's own colour, warmed toward gold, so the glow reads as light
            // falling on it rather than as a recolour of it.
            Color own = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            glowColor[i] = Color.Lerp(own, Gold, goldBlend);
            glowColor[i].a = 1f;
        }
    }
}

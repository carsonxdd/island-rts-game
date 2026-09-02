using UnityEngine;

/// <summary>
/// Every knob the island generator reads, as a ScriptableObject so tuning is
/// an asset edit rather than a code edit and so the editor tools, the game and
/// the balance harness all generate from the same numbers.
///
/// The generator is a pipeline of layers (shape → relief → terraces → ponds →
/// seabed → detail → anchors → validation) and each layer's parameters live
/// in its own header below. Adding a layer means adding a header here and a
/// pass in <see cref="IslandGenerator"/>; nothing else has to know.
///
/// <see cref="CreateDefault"/> is the source of truth for the shipped values:
/// the setup tool writes an asset from it, and code paths that have no asset
/// (a bare scene, the sim) fall back to it. The asset carries a
/// <see cref="version"/>; when the code's <see cref="CurrentVersion"/> is
/// newer the setup tool rewrites the asset from the defaults, so a tuning
/// pass in code actually reaches the scene. Bump the version whenever the
/// defaults change on purpose.
///
/// Distances are authored for the standard 150 m map (half-extent 75). The
/// generator scales the shape, border and anchor distances by the actual
/// map's half-extent / 75 so the same asset serves every island size.
/// </summary>
[CreateAssetMenu(fileName = "IslandSettings", menuName = "Island RTS/Island Settings")]
public class IslandSettings : ScriptableObject
{
    /// <summary>Bump when the defaults below change; the setup tool refreshes older assets.</summary>
    public const int CurrentVersion = 2;

    [HideInInspector] public int version = CurrentVersion;

    [Header("Shape")]
    [Tooltip("Nominal island radius on the 150 m map (divisor for the radial falloff). Scaled with map size.")]
    public float islandRadius = 82f;
    [Tooltip("Per-seed ellipse stretch: the short axis is this fraction of the long one at minimum. 1 = always round.")]
    [Range(0.5f, 1f)] public float aspectMin = 0.80f;
    [Tooltip("The ellipse's long axis points roughly at the landing cove, swung by up to this many degrees per seed, so the cove never lands on a crushed short axis.")]
    [Range(0f, 90f)] public float axisSwing = 35f;
    [Tooltip("Land fades to seabed this far from the map centre (on the 150 m map), whatever the coastline noise says — keeps the island off the map border.")]
    public float borderFalloffStart = 66f;
    public float borderFalloffWidth = 7f;
    [Tooltip("Small coastline wobble (bays and headlands a few meters across), in meters.")]
    public float coastWarp = 6f;
    public float coastScale = 0.04f;
    [Tooltip("Large coastline wobble (whole bays and peninsulas), in meters.")]
    public float coastWarpLarge = 14f;
    public float coastScaleLarge = 0.014f;
    [Tooltip("Normalized radius where the coast falloff begins (1 = islandRadius).")]
    public float coastStart = 0.60f;
    [Tooltip("Width of the coast falloff on sandy shores (normalized radius). Wide = gentle beach + wide wading band.")]
    public float coastWidthSandy = 0.45f;
    [Tooltip("Width of the coast falloff on rocky shores. Narrow = sea cliff, little or no wading band.")]
    public float coastWidthRocky = 0.16f;
    [Tooltip("Roughly what fraction of the coastline is rocky rather than sandy.")]
    [Range(0f, 1f)] public float rockyCoastAmount = 0.30f;
    public float rockyCoastScale = 0.015f;

    [Header("Relief")]
    [Tooltip("Height of flat land at the coast, before hills.")]
    public float baseHeight = 0.5f;
    [Tooltip("Maximum hill / plateau height above baseHeight. Orthographic camera: past ~7 the terrain hides too much behind it.")]
    public float hillAmplitude = 6.5f;
    [Tooltip("Base frequency of the relief. Lower = larger, smoother landforms.")]
    public float hillScale = 0.022f;
    [Range(1, 6)] public int hillOctaves = 3;
    public float hillLacunarity = 2f;
    [Range(0f, 1f)] public float hillGain = 0.42f;
    [Tooltip("Tall relief only develops this far inside the coast (land mask value). Keeps the beach ring low and walkable.")]
    [Range(0f, 1f)] public float interiorStart = 0.70f;

    [Header("Terraces")]
    [Tooltip("Number of plateau levels the hills quantize into. Step height ≈ hillAmplitude / levels.")]
    [Range(0, 8)] public int terraceLevels = 5;
    [Tooltip("How much of the island is terraced (0 = rolling hills everywhere, 1 = stepped everywhere).")]
    [Range(0f, 1f)] public float terraceCoverage = 0.72f;
    [Tooltip("Frequency of the terraced-vs-rolling mask. Lower = larger contiguous terraced regions.")]
    public float terraceScale = 0.016f;
    [Tooltip("Fraction of terrace edge that is cliff rather than ramp (the rest is walkable slope).")]
    [Range(0f, 1f)] public float cliffFraction = 0.62f;
    [Tooltip("Edge sharpness where the ramp noise is low: small = vertical cliff face. Must be sharp enough that the whole step lands within one 1 m cell, or the step splits into two climbable half-steps and the cliff leaks.")]
    [Range(0.02f, 1f)] public float cliffSharpness = 0.04f;
    [Tooltip("Edge sharpness where the ramp noise is high: large = walkable slope between levels.")]
    [Range(0.02f, 1f)] public float rampSharpness = 0.70f;
    [Tooltip("Frequency of the cliff-vs-ramp noise. Lower = longer unbroken cliff lines and longer ramps.")]
    public float rampScale = 0.04f;

    [Header("Ponds")]
    public bool ponds = true;
    [Tooltip("Noise threshold above which the interior dips into a pond. Higher = fewer, smaller ponds.")]
    [Range(0.5f, 0.95f)] public float pondThreshold = 0.62f;
    public float pondDepth = -0.8f;
    [Tooltip("Ponds only form where the land would otherwise be below this height — never as craters in a plateau.")]
    public float pondMaxGround = 3.0f;
    [Tooltip("Width of the pond shore in noise units — wider = gentler, walkable banks.")]
    public float pondShore = 0.24f;
    public float pondScale = 0.04f;

    [Header("Seabed")]
    [Tooltip("Depth of the seabed past the coast (positive meters below sea level).")]
    public float deepDepth = 2.2f;
    [Tooltip("Sandbar relief on the seabed, in meters.")]
    public float sandbarAmplitude = 0.5f;
    public float sandbarScale = 0.05f;

    [Header("Detail")]
    [Tooltip("Per-vertex height jitter on land (meters). Breaks the grid regularity of flat areas.")]
    public float microJitter = 0.03f;
    [Tooltip("Grass tone noise frequency (meadow / dark grass patches). Lower = broad, flowing patches.")]
    public float toneScale = 0.02f;
    [Tooltip("How much height (valleys dark, plateau tops dry) steers the grass tone versus the noise.")]
    [Range(0f, 1f)] public float toneHeightWeight = 0.45f;
    [Tooltip("Material band thresholds are offset by up to this many meters so the beach line is not a perfect contour.")]
    public float bandJitter = 0.12f;

    [Header("Anchors (150 m map coordinates; scaled with map size)")]
    public Vector2 campfireSite = new Vector2(0f, 0f);
    public Vector2 coveCenter = new Vector2(-70f, 3f);
    public Vector2 coveRamp = new Vector2(-58f, 3f);
    [Tooltip("Half-width of the guaranteed-land corridor at the campfire end.")]
    public float corridorHalfWidth = 5f;
    [Tooltip("Half-width of the corridor at the cove end — wide, so it reads as a shoulder of the island rather than a pier.")]
    public float corridorHalfWidthCove = 13f;
    public float corridorBlend = 6f;
    public float campfireFlatRadius = 8f;
    public float campfireFlatBlend = 10f;
    [Tooltip("The campfire flat keeps its natural height, clamped into this range so it is always dry but not a crater.")]
    public float campfireHeightMin = 0.8f;
    public float campfireHeightMax = 2.5f;

    [Header("Validation")]
    [Tooltip("Flood-fill the walkable land from the campfire site, carve ramps to cut-off regions, reroll seeds that still fail.")]
    public bool validate = true;
    [Range(1, 20)] public int maxAttempts = 8;
    [Tooltip("Largest vertical step (meters per 1 m cell) an agent can walk. NavMesh agent: 45° slope, 0.75 climb.")]
    public float maxWalkableStep = 0.9f;
    [Tooltip("Cut-off regions smaller than this (cells) are left as unreachable outcrops instead of getting a ramp.")]
    public int minRegionCells = 40;
    [Tooltip("Rise/run of a carved ramp (0.4 ≈ 22°).")]
    public float rampSlope = 0.4f;
    public float rampHalfWidth = 2.5f;
    [Range(0, 20)] public int maxRamps = 6;
    [Tooltip("Reachable land must be at least this fraction of all land, or the seed is rerolled.")]
    [Range(0f, 1f)] public float minReachableFraction = 0.90f;
    [Tooltip("Minimum reachable buildable cells (dry, gentle) for the island to be playable, on the 150 m map. Scaled with map area.")]
    public int minBuildableCells = 1500;

    // ------------------------------------------------------------------
    // Terrain styles (player-facing presets on the New Game screen)
    // ------------------------------------------------------------------

    public enum Style { Rolling, Terraced, Rugged }
    public static readonly string[] StyleNames = { "Rolling", "Terraced", "Rugged" };
    public static readonly string[] StyleBlurbs =
    {
        "Soft hills and wide meadows. Few cliffs — easy to build anywhere.",
        "Stepped plateaus with cliff edges and ramps. The intended mix.",
        "High, broken ground: more levels, longer cliffs, rocky shores.",
    };

    /// <summary>
    /// A runtime copy of these settings with a style applied. Never mutates
    /// the asset. Terraced is the asset as authored.
    /// </summary>
    public IslandSettings WithStyle(Style style)
    {
        IslandSettings s = Instantiate(this);
        s.name = name + " (" + style + ")";
        switch (style)
        {
            case Style.Rolling:
                s.terraceCoverage = 0.22f;
                s.terraceLevels = 3;
                s.cliffFraction = 0.30f;
                s.hillAmplitude *= 0.8f;
                s.rockyCoastAmount *= 0.6f;
                break;
            case Style.Rugged:
                s.terraceCoverage = 0.92f;
                s.terraceLevels = 6;
                s.cliffFraction = 0.80f;
                s.hillAmplitude *= 1.1f;
                s.rockyCoastAmount = Mathf.Min(1f, s.rockyCoastAmount * 1.8f);
                s.maxRamps = Mathf.Max(s.maxRamps, 10);
                break;
        }
        return s;
    }

    /// <summary>The shipped defaults. The setup tool writes an asset from this.</summary>
    public static IslandSettings CreateDefault()
    {
        IslandSettings s = CreateInstance<IslandSettings>();
        s.name = "IslandSettings (defaults)";
        s.version = CurrentVersion;
        return s;
    }

    private static IslandSettings fallback;

    /// <summary>Use the given settings, or the in-memory defaults when null.</summary>
    public static IslandSettings Resolve(IslandSettings settings)
    {
        if (settings != null) return settings;
        if (fallback == null) fallback = CreateDefault();
        return fallback;
    }
}

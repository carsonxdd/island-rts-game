using UnityEngine;

/// <summary>
/// Pure, deterministic island heightfield generation (Terrain System T1).
/// Same seed → same island, and no Unity scene dependencies, so the editor
/// tools can generate the identical heightfield to snap scene props against.
///
/// Shape: radial falloff × domain-warped coastline (so the island isn't a
/// circle) × 3 octaves of Perlin hills. Amplitude budget ~3.5 m above sea
/// level (orthographic-camera readability constraint — see
/// TERRAIN_SYSTEM_PLAN.md). Sea level is y = 0; the map border sits ~2 m
/// underwater.
///
/// Two authored features are blended in after the noise:
///  - Campfire site: a gentle flat disc at the world origin (the classic
///    start position, and a natural colony heart for the placed campfire).
///  - Landing cove: a shallow shelf at the shipwreck on the west shore with
///    a walkable beach ramp rising inland — guarantees the opening sequence
///    (survivor wading ashore) works on every seed that keeps the anchors.
/// </summary>
public static class IslandGenerator
{
    // World-space anchors that generation guarantees (match the opening scene)
    public static readonly Vector2 CampfireSite = new Vector2(0f, 0f);
    public static readonly Vector2 CoveCenter = new Vector2(-46f, 2f);
    public static readonly Vector2 CoveRamp = new Vector2(-38f, 2f);

    // Shape tuning (T1: fixed gentle island; T4 revisits for random seeds)
    private const float IslandRadius = 48f;   // divisor for the radial falloff
    private const float CoastWarp = 8f;       // ± meters of coastline wobble
    private const float CoastScale = 0.045f;  // coastline noise frequency
    private const float HillAmp = 3.0f;       // hills on top of the 0.5 base
    private const float DeepDepth = 2.0f;     // seabed depth past the coast

    /// <summary>
    /// Generate the full heightfield: verts × verts samples at
    /// <paramref name="spacing"/> meters, centered on the world origin.
    /// </summary>
    public static float[,] Generate(int verts, float spacing, int seed)
    {
        float[,] heights = new float[verts, verts];
        float half = (verts - 1) * spacing * 0.5f;

        // Per-octave noise offsets from the seed (Mathf.PerlinNoise has no
        // seed parameter — offsetting the domain is the standard trick)
        System.Random rng = new System.Random(seed);
        float oc1 = Rand(rng), oc2 = Rand(rng);
        float oh1 = Rand(rng), oh2 = Rand(rng);
        float oh3 = Rand(rng), oh4 = Rand(rng);
        float oh5 = Rand(rng), oh6 = Rand(rng);

        for (int zi = 0; zi < verts; zi++)
        {
            for (int xi = 0; xi < verts; xi++)
            {
                float x = xi * spacing - half;
                float z = zi * spacing - half;

                // Domain-warped distance from center → non-circular coastline
                float d = Mathf.Sqrt(x * x + z * z);
                float warp = (Mathf.PerlinNoise(x * CoastScale + oc1, z * CoastScale + oc2) - 0.5f) * 2f * CoastWarp;
                float t = (d + warp) / IslandRadius;

                // 1 inland → 0 past the coast, over a wide window so the
                // beach gradient stays walkably gentle everywhere
                float land = 1f - SmoothStep01((t - 0.60f) / 0.45f);

                // Rolling hills: 3 octaves, normalized 0..1
                float hills =
                    (Mathf.PerlinNoise(x * 0.045f + oh1, z * 0.045f + oh2)
                     + 0.5f * Mathf.PerlinNoise(x * 0.09f + oh3, z * 0.09f + oh4)
                     + 0.25f * Mathf.PerlinNoise(x * 0.18f + oh5, z * 0.18f + oh6)) / 1.75f;

                float h = land * (0.5f + hills * HillAmp) - (1f - land) * DeepDepth;
                heights[xi, zi] = Mathf.Clamp(h, -DeepDepth, 4f);
            }
        }

        // Authored features (order matters: ramp first, cove shelf wins overlap)
        FlattenDisc(heights, verts, spacing, CampfireSite.x, CampfireSite.y, 6f, 8f, 1.0f);
        FlattenDisc(heights, verts, spacing, CoveRamp.x, CoveRamp.y, 5f, 6f, 0.6f);
        FlattenDisc(heights, verts, spacing, CoveCenter.x, CoveCenter.y, 5f, 5f, -0.25f);

        return heights;
    }

    /// <summary>
    /// Blend a disc of the heightfield toward a target height: full weight
    /// inside <paramref name="radius"/>, smoothstepping to zero over
    /// <paramref name="blend"/> meters beyond it. Also the DNA of the T2
    /// building-placement FlattenArea op.
    /// </summary>
    public static void FlattenDisc(float[,] heights, int verts, float spacing,
        float worldX, float worldZ, float radius, float blend, float target)
    {
        float half = (verts - 1) * spacing * 0.5f;
        float reach = radius + blend;

        int xMin = Mathf.Max(0, Mathf.FloorToInt((worldX - reach + half) / spacing));
        int xMax = Mathf.Min(verts - 1, Mathf.CeilToInt((worldX + reach + half) / spacing));
        int zMin = Mathf.Max(0, Mathf.FloorToInt((worldZ - reach + half) / spacing));
        int zMax = Mathf.Min(verts - 1, Mathf.CeilToInt((worldZ + reach + half) / spacing));

        for (int zi = zMin; zi <= zMax; zi++)
        {
            for (int xi = xMin; xi <= xMax; xi++)
            {
                float x = xi * spacing - half;
                float z = zi * spacing - half;
                float dist = Mathf.Sqrt((x - worldX) * (x - worldX) + (z - worldZ) * (z - worldZ));

                float w = 1f - SmoothStep01((dist - radius) / blend);
                if (w <= 0f) continue;

                heights[xi, zi] = Mathf.Lerp(heights[xi, zi], target, w);
            }
        }
    }

    static float SmoothStep01(float u)
    {
        u = Mathf.Clamp01(u);
        return u * u * (3f - 2f * u);
    }

    static float Rand(System.Random rng)
    {
        return (float)(rng.NextDouble() * 1000.0);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything the generator produces for one island: the heightfield plus
/// the per-vertex side channels the renderer and the scatter read (grass
/// tone, band jitter) and the walkability mask the validator computed.
/// Plain arrays, no Unity objects — safe to build in the editor or a test.
/// </summary>
public sealed class IslandField
{
    public readonly int verts;
    public readonly float spacing;

    /// <summary>Terrain height (y) per vertex. Sea level is 0.</summary>
    public readonly float[,] heights;
    /// <summary>Grass tone 0..1 (low = dark grass, high = dry meadow).</summary>
    public readonly float[,] tone;
    /// <summary>Material band offset −1..1 (scaled by IslandSettings.bandJitter at classification time).</summary>
    public readonly float[,] band;
    /// <summary>True where an agent starting at the campfire site can walk to.</summary>
    public readonly bool[,] reachable;

    /// <summary>The seed that produced this field (after any rerolls).</summary>
    public int seed;

    /// <summary>Anchors in THIS field's world units (the settings' 150 m coordinates scaled to the map size).</summary>
    public Vector2 campfireSite, coveCenter, coveRamp;
    /// <summary>Map half-extent / 75: 1 on the standard map.</summary>
    public float sizeScale = 1f;
    public int attempt;
    public int rampsCarved;
    public bool valid;
    public float reachableFraction;
    public int buildableCells;

    public IslandField(int verts, float spacing)
    {
        this.verts = verts;
        this.spacing = spacing;
        heights = new float[verts, verts];
        tone = new float[verts, verts];
        band = new float[verts, verts];
        reachable = new bool[verts, verts];
    }

    public string Report =>
        $"seed {seed} (attempt {attempt + 1}, {rampsCarved} ramp(s) carved, "
        + $"{reachableFraction * 100f:F0}% of land reachable, {buildableCells} buildable cells"
        + (valid ? ")" : ", VALIDATION FAILED — best attempt kept)");
}

/// <summary>
/// Pure, deterministic island generation. Same seed + same settings → same
/// island, with no scene dependencies, so the editor tools and the game
/// produce identical fields.
///
/// The field is built as a pipeline of layers, each reading its knobs from
/// <see cref="IslandSettings"/>:
///
///   1. Shape     — per-seed ellipse + rotation, two octaves of coastline
///                  warp, sandy (wide, gentle) vs rocky (narrow, cliffed)
///                  shore sectors, a guaranteed land corridor from the
///                  landing cove to the campfire site.
///   2. Relief    — fBm hills, amplitude ramping up from the coast inland so
///                  the beach ring stays low.
///   3. Terraces  — the hills quantized into plateau levels where a coverage
///                  mask says so; edge sharpness follows a second noise so
///                  the same edge is a cliff here and a walkable ramp there.
///                  Plateau tops are dead flat: the "easy to build" places.
///   4. Ponds     — interior dips below the deep-water line (NotWalkable).
///   5. Seabed    — drop to the deep depth with sandbar relief.
///   6. Detail    — per-vertex micro-jitter; tone + band side channels.
///   7. Anchors   — FlattenDisc for the campfire flat, the cove ramp and the
///                  cove shelf (the opening sequence depends on all three).
///   8. Validate  — flood-fill walkable land from the campfire; carve ramps
///                  to cut-off regions; reroll the seed if the island still
///                  fails (cove unreachable, too little reachable land, too
///                  few buildable cells). The best attempt is kept if every
///                  attempt fails, so generation always terminates.
///
/// Amplitude budget: plateau tops reach baseHeight + hillAmplitude (~6.5 m).
/// The orthographic camera hides ground behind anything taller, which is why
/// the amplitude is a setting and not a free parameter.
/// </summary>
public static class IslandGenerator
{
    /// <summary>Legacy entry point: heights only, default settings, validated.</summary>
    public static float[,] Generate(int verts, float spacing, int seed)
    {
        return Generate(verts, spacing, seed, null).heights;
    }

    /// <summary>
    /// Generate and validate an island. <paramref name="settings"/> may be
    /// null (defaults). The returned field's <c>seed</c> is the one that
    /// actually produced it — it differs from the request when a reroll
    /// happened, and the caller should keep it so a restart replays the
    /// same island.
    /// </summary>
    public static IslandField Generate(int verts, float spacing, int seed, IslandSettings settings)
    {
        IslandSettings s = IslandSettings.Resolve(settings);

        IslandField best = null;
        float bestScore = float.MinValue;
        int attempts = s.validate ? Mathf.Max(1, s.maxAttempts) : 1;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            // Deterministic reroll sequence: the same request always walks
            // the same seeds, so a logged seed reproduces the island
            int attemptSeed = attempt == 0 ? seed : unchecked(seed + attempt * 7919);
            IslandField field = GenerateOnce(verts, spacing, attemptSeed, s);
            field.attempt = attempt;

            if (!s.validate)
            {
                ComputeReachability(field, s, null, out _, out _);
                Measure(field, s);
                field.valid = true;
                return field;
            }

            RepairConnectivity(field, s);
            Measure(field, s);

            float score = Score(field, s);
            if (score > bestScore)
            {
                bestScore = score;
                best = field;
            }
            if (field.valid) return field;
        }

        return best;
    }

    // ==================================================================
    // Layers 1-7: one field from one seed
    // ==================================================================

    static IslandField GenerateOnce(int verts, float spacing, int seed, IslandSettings s)
    {
        IslandField f = new IslandField(verts, spacing);
        f.seed = seed;
        float[,] heights = f.heights;
        float half = (verts - 1) * spacing * 0.5f;

        // Settings distances are authored for the 150 m map; scale them to
        // this map so one asset serves every island size
        float scale = half / 75f;
        f.sizeScale = scale;
        f.campfireSite = s.campfireSite * scale;
        f.coveCenter = s.coveCenter * scale;
        f.coveRamp = s.coveRamp * scale;
        float islandRadius = s.islandRadius * scale;
        float borderStart = s.borderFalloffStart * scale;
        float pondRing = (s.campfireFlatRadius + s.campfireFlatBlend) * scale;

        // Noise offsets from the seed (Mathf.PerlinNoise has no seed — offsetting
        // the domain is the standard trick). One pair per noise field.
        System.Random rng = new System.Random(seed);
        Vector2 oCoast = Offset(rng), oCoastL = Offset(rng), oRocky = Offset(rng);
        Vector2[] oHill = new Vector2[s.hillOctaves];
        for (int i = 0; i < oHill.Length; i++) oHill[i] = Offset(rng);
        Vector2 oTerrace = Offset(rng), oRamp = Offset(rng), oPond = Offset(rng);
        Vector2 oSandbar = Offset(rng), oTone = Offset(rng), oToneFine = Offset(rng), oBand = Offset(rng);

        // Per-seed silhouette: ellipse aspect, long axis swung around the
        // cove direction (so the short axis never crushes the landing side)
        Vector2 coveDir = s.coveCenter - s.campfireSite;
        float coveAngle = Mathf.Atan2(coveDir.y, coveDir.x);
        float rot = coveAngle + ((float)rng.NextDouble() * 2f - 1f) * s.axisSwing * Mathf.Deg2Rad;
        float aspect = Mathf.Lerp(s.aspectMin, 1f, (float)rng.NextDouble());
        float cosR = Mathf.Cos(rot), sinR = Mathf.Sin(rot);

        // Noise thresholds derived from "fraction" knobs. Perlin values sit
        // roughly normal around 0.5 with sd ~0.13, so this maps a desired
        // fraction to a threshold well enough for tuning purposes.
        float rockyThreshold = 0.5f + (0.5f - s.rockyCoastAmount) * 0.4f;
        float terraceThreshold = 0.5f + (0.5f - s.terraceCoverage) * 0.4f;
        float rampThreshold = 0.5f + (s.cliffFraction - 0.5f) * 0.4f;

        float coastStartR = s.coastStart;

        for (int zi = 0; zi < verts; zi++)
        {
            for (int xi = 0; xi < verts; xi++)
            {
                float x = xi * spacing - half;
                float z = zi * spacing - half;

                // ---- 1. Shape -------------------------------------------
                // Rotate into the ellipse frame: rx runs along the long axis
                float rx = x * cosR + z * sinR;
                float rz = -x * sinR + z * cosR;
                float d = Mathf.Sqrt(rx * rx + (rz / aspect) * (rz / aspect));

                float warp = (Perlin(x, z, s.coastScale, oCoast) - 0.5f) * 2f * s.coastWarp
                           + (Perlin(x, z, s.coastScaleLarge, oCoastL) - 0.5f) * 2f * s.coastWarpLarge;
                float t = (d + warp) / islandRadius;

                float rocky = SmoothStep(Perlin(x, z, s.rockyCoastScale, oRocky), rockyThreshold - 0.06f, rockyThreshold + 0.06f);
                float coastWidth = Mathf.Lerp(s.coastWidthSandy, s.coastWidthRocky, rocky);
                float land = 1f - SmoothStep01((t - coastStartR) / coastWidth);

                // Keep everything off the map border whatever the noise says
                float border = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                land *= 1f - SmoothStep01((border - borderStart) / s.borderFalloffWidth);

                // Guaranteed land from the cove to the campfire (the opening
                // sequence walks this) — a wedge, narrow at the campfire and
                // wide at the shore so it reads as a shoulder of the island
                float along;
                float corridorDist = DistToSegment(x, z, f.coveRamp, f.campfireSite, out along);
                float corridorWidth = Mathf.Lerp(s.corridorHalfWidthCove, s.corridorHalfWidth, along) * scale;
                float corridor = 1f - SmoothStep01((corridorDist - corridorWidth) / (s.corridorBlend * scale));
                land = Mathf.Max(land, corridor);

                // Rocky shores hold their height and then drop: a sea cliff
                float landShaped = Mathf.Lerp(land, Mathf.Pow(land, 0.35f), rocky * (1f - corridor));

                // ---- 2. Relief ------------------------------------------
                float hills = Fbm(x, z, s.hillScale, s.hillOctaves, s.hillLacunarity, s.hillGain, oHill);
                hills = Mathf.Clamp01((hills - 0.28f) / 0.44f);   // stretch the fBm's practical range to 0..1

                // ---- 3. Terraces ----------------------------------------
                float terraceMask = SmoothStep(Perlin(x, z, s.terraceScale, oTerrace), terraceThreshold - 0.15f, terraceThreshold + 0.15f);
                terraceMask *= 1f - corridor;   // never a cliff across the landing corridor
                float rampMix = SmoothStep(Perlin(x, z, s.rampScale, oRamp), rampThreshold - 0.06f, rampThreshold + 0.06f);
                float sharpness = Mathf.Lerp(s.cliffSharpness, s.rampSharpness, rampMix);
                float terraced = Terrace(hills, s.terraceLevels, sharpness);
                float relief = Mathf.Lerp(hills, terraced, terraceMask);

                float interior = SmoothStep01((land - s.interiorStart) / (1f - s.interiorStart));
                float amp = s.hillAmplitude * Mathf.Lerp(0.25f, 1f, interior) * Mathf.Lerp(1f, 0.5f, corridor);
                float landHeight = s.baseHeight + relief * amp;

                // ---- 5. Seabed ------------------------------------------
                float seabed = -s.deepDepth + (Perlin(x, z, s.sandbarScale, oSandbar) - 0.5f) * 2f * s.sandbarAmplitude;

                float h = landShaped * landHeight + (1f - landShaped) * seabed;

                // ---- 4. Ponds -------------------------------------------
                if (s.ponds)
                {
                    float dCamp = Mathf.Sqrt((x - f.campfireSite.x) * (x - f.campfireSite.x) + (z - f.campfireSite.y) * (z - f.campfireSite.y));
                    float campfireProtect = 1f - SmoothStep01((dCamp - pondRing) / 6f);
                    // Only in low ground (a pond on a 5 m plateau is a
                    // sinkhole), with a wide enough shore that the rim stays
                    // walkable down to the waterline
                    float lowGround = 1f - SmoothStep(landHeight, s.pondMaxGround, s.pondMaxGround + 1.5f);
                    float pond = SmoothStep(Perlin(x, z, s.pondScale, oPond), s.pondThreshold, s.pondThreshold + s.pondShore)
                               * SmoothStep(land, 0.92f, 1f) * (1f - corridor) * (1f - campfireProtect) * lowGround;
                    h = Mathf.Lerp(h, s.pondDepth, pond);
                }

                // ---- 6. Detail ------------------------------------------
                h += (Hash01(xi, zi, seed) - 0.5f) * 2f * s.microJitter * landShaped;

                heights[xi, zi] = h;
                f.tone[xi, zi] = 0.7f * Perlin(x, z, s.toneScale, oTone) + 0.3f * Perlin(x, z, s.toneScale * 4.3f, oToneFine);
                f.band[xi, zi] = (Perlin(x, z, 0.15f, oBand) - 0.5f) * 2f;
            }
        }

        // ---- 7. Anchors (order matters: ramp first, cove shelf wins overlap)
        float campH = Mathf.Clamp(TerrainGrid.SampleField(heights, f.campfireSite.x, f.campfireSite.y), s.campfireHeightMin, s.campfireHeightMax);
        FlattenDisc(heights, verts, spacing, f.campfireSite.x, f.campfireSite.y, s.campfireFlatRadius * scale, s.campfireFlatBlend * scale, campH);
        FlattenDisc(heights, verts, spacing, f.coveRamp.x, f.coveRamp.y, 5f, 6f, 0.6f);
        FlattenDisc(heights, verts, spacing, f.coveCenter.x, f.coveCenter.y, 5f, 5f, -0.25f);

        return f;
    }

    // ==================================================================
    // Field ops (shared with TerrainGrid.FlattenArea)
    // ==================================================================

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

    /// <summary>
    /// Carve a walkable ramp between two world points: heights along a
    /// capsule around the segment are set to lerp(ha, hb), blending back
    /// into the terrain over <paramref name="blend"/> meters. If the segment
    /// is too short for <paramref name="slope"/>, it is extended at both
    /// ends (into the plateau on one side, into the lowland on the other).
    /// </summary>
    public static void CarveRamp(float[,] heights, int verts, float spacing,
        Vector2 a, float ha, Vector2 b, float hb, float slope, float halfWidth, float blend)
    {
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 0.001f) dir = Vector2.right; else dir /= len;

        float needed = Mathf.Abs(hb - ha) / Mathf.Max(0.05f, slope);
        if (len < needed)
        {
            float ext = (needed - len) * 0.5f;
            a -= dir * ext;
            b += dir * ext;
            len = needed;
        }

        float half = (verts - 1) * spacing * 0.5f;
        float reach = halfWidth + blend;
        float minX = Mathf.Min(a.x, b.x) - reach, maxX = Mathf.Max(a.x, b.x) + reach;
        float minZ = Mathf.Min(a.y, b.y) - reach, maxZ = Mathf.Max(a.y, b.y) + reach;

        int xMin = Mathf.Max(0, Mathf.FloorToInt((minX + half) / spacing));
        int xMax = Mathf.Min(verts - 1, Mathf.CeilToInt((maxX + half) / spacing));
        int zMin = Mathf.Max(0, Mathf.FloorToInt((minZ + half) / spacing));
        int zMax = Mathf.Min(verts - 1, Mathf.CeilToInt((maxZ + half) / spacing));

        for (int zi = zMin; zi <= zMax; zi++)
        {
            for (int xi = xMin; xi <= xMax; xi++)
            {
                float x = xi * spacing - half;
                float z = zi * spacing - half;

                float px = x - a.x, pz = z - a.y;
                float along = Mathf.Clamp01((px * dir.x + pz * dir.y) / len);
                float cx = a.x + dir.x * along * len, cz = a.y + dir.y * along * len;
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));

                float w = 1f - SmoothStep01((dist - halfWidth) / blend);
                if (w <= 0f) continue;

                float target = Mathf.Lerp(ha, hb, along);
                heights[xi, zi] = Mathf.Lerp(heights[xi, zi], target, w);
            }
        }
    }

    // ==================================================================
    // Layer 8: validation + repair
    // ==================================================================

    /// <summary>
    /// Label connected walkable regions (4-neighbour flood fill; a step is
    /// passable when the height difference is at most maxWalkableStep and
    /// both cells are above the deep-water line). Fills
    /// <c>field.reachable</c> with the campfire's region.
    /// </summary>
    static void ComputeReachability(IslandField f, IslandSettings s, int[,] regionOut,
        out List<int> regionSizes, out int mainRegion)
    {
        int n = f.verts;
        float[,] h = f.heights;
        int[,] region = regionOut ?? new int[n, n];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) region[x, z] = -1;

        regionSizes = new List<int>();
        Queue<int> queue = new Queue<int>();
        float maxStep = s.maxWalkableStep;

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                if (region[x, z] >= 0 || h[x, z] <= TerrainGrid.DeepWaterY) continue;

                int id = regionSizes.Count;
                int size = 0;
                region[x, z] = id;
                queue.Enqueue(x + z * n);

                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    int cx = c % n, cz = c / n;
                    size++;
                    float ch = h[cx, cz];

                    TryVisit(cx - 1, cz, ch, id, n, h, region, maxStep, queue);
                    TryVisit(cx + 1, cz, ch, id, n, h, region, maxStep, queue);
                    TryVisit(cx, cz - 1, ch, id, n, h, region, maxStep, queue);
                    TryVisit(cx, cz + 1, ch, id, n, h, region, maxStep, queue);
                }
                regionSizes.Add(size);
            }
        }

        int cxi, czi;
        WorldToIndex(f, f.campfireSite.x, f.campfireSite.y, out cxi, out czi);
        mainRegion = region[cxi, czi];

        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                f.reachable[x, z] = mainRegion >= 0 && region[x, z] == mainRegion;
    }

    static void TryVisit(int x, int z, float fromH, int id, int n, float[,] h, int[,] region, float maxStep, Queue<int> queue)
    {
        if (x < 0 || z < 0 || x >= n || z >= n) return;
        if (region[x, z] >= 0) return;
        float th = h[x, z];
        if (th <= TerrainGrid.DeepWaterY) return;
        if (Mathf.Abs(th - fromH) > maxStep) return;
        region[x, z] = id;
        queue.Enqueue(x + z * n);
    }

    /// <summary>
    /// Carve ramps from the main region to every sizeable cut-off land
    /// region (and to the cove's region whatever its size), largest first,
    /// up to maxRamps. Each ramp joins the closest pair of boundary cells.
    /// </summary>
    static void RepairConnectivity(IslandField f, IslandSettings s)
    {
        int n = f.verts;
        int[,] region = new int[n, n];
        List<int> boundaryA = new List<int>();
        List<int> boundaryMain = new List<int>();

        for (int ramp = 0; ramp < s.maxRamps; ramp++)
        {
            List<int> sizes;
            int main;
            ComputeReachability(f, s, region, out sizes, out main);
            if (main < 0) return;   // campfire site underwater — nothing to repair toward; the seed will fail validation

            int coveX, coveZ;
            WorldToIndex(f, f.coveCenter.x, f.coveCenter.y, out coveX, out coveZ);
            int coveRegion = region[coveX, coveZ];

            // Pick the target: the cove's region first if cut off, else the
            // largest cut-off LAND region above the size floor
            int target = -1;
            if (coveRegion >= 0 && coveRegion != main)
            {
                target = coveRegion;
            }
            else
            {
                int bestSize = s.minRegionCells - 1;
                int[] landCells = new int[sizes.Count];
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                        if (region[x, z] >= 0 && f.heights[x, z] > 0.15f) landCells[region[x, z]]++;
                for (int r = 0; r < sizes.Count; r++)
                {
                    if (r == main) continue;
                    if (landCells[r] > bestSize) { bestSize = landCells[r]; target = r; }
                }
            }
            if (target < 0) return;   // connected enough

            CollectBoundary(region, n, target, boundaryA);
            CollectBoundary(region, n, main, boundaryMain);

            // Closest boundary pair (both lists are perimeter-sized, so this is cheap)
            int bestA = -1, bestB = -1;
            int bestD2 = int.MaxValue;
            for (int i = 0; i < boundaryA.Count; i++)
            {
                int ax = boundaryA[i] % n, az = boundaryA[i] / n;
                for (int j = 0; j < boundaryMain.Count; j++)
                {
                    int bx = boundaryMain[j] % n, bz = boundaryMain[j] / n;
                    int dx = ax - bx, dz = az - bz;
                    int d2 = dx * dx + dz * dz;
                    if (d2 < bestD2) { bestD2 = d2; bestA = boundaryA[i]; bestB = boundaryMain[j]; }
                }
            }
            if (bestA < 0) return;

            Vector2 a = IndexToWorld(f, bestA % n, bestA / n);
            Vector2 b = IndexToWorld(f, bestB % n, bestB / n);
            CarveRamp(f.heights, n, f.spacing, a, f.heights[bestA % n, bestA / n], b, f.heights[bestB % n, bestB / n],
                s.rampSlope, s.rampHalfWidth, 2f);
            f.rampsCarved++;
        }

        // Final mask after the last ramp
        ComputeReachability(f, s, region, out _, out _);
    }

    static void CollectBoundary(int[,] region, int n, int id, List<int> outCells)
    {
        outCells.Clear();
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                if (region[x, z] != id) continue;
                if ((x > 0 && region[x - 1, z] != id) || (x < n - 1 && region[x + 1, z] != id)
                    || (z > 0 && region[x, z - 1] != id) || (z < n - 1 && region[x, z + 1] != id))
                {
                    outCells.Add(x + z * n);
                }
            }
        }
    }

    /// <summary>Reachable-land fraction, buildable-cell count, cove check → field.valid.</summary>
    static void Measure(IslandField f, IslandSettings s)
    {
        int n = f.verts;
        int land = 0, reachableLand = 0, buildable = 0;
        for (int z = 1; z < n - 1; z++)
        {
            for (int x = 1; x < n - 1; x++)
            {
                float h = f.heights[x, z];
                if (h <= 0.15f) continue;
                land++;
                if (!f.reachable[x, z]) continue;
                reachableLand++;

                float dhdx = (f.heights[x + 1, z] - f.heights[x - 1, z]) / (2f * f.spacing);
                float dhdz = (f.heights[x, z + 1] - f.heights[x, z - 1]) / (2f * f.spacing);
                if (Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz) < 0.55f) buildable++;
            }
        }

        f.reachableFraction = land > 0 ? (float)reachableLand / land : 0f;
        f.buildableCells = buildable;

        int coveX, coveZ;
        WorldToIndex(f, f.coveCenter.x, f.coveCenter.y, out coveX, out coveZ);
        bool coveOk = f.reachable[coveX, coveZ];

        f.valid = coveOk
               && f.reachableFraction >= s.minReachableFraction
               && f.buildableCells >= MinBuildable(f, s);
    }

    /// <summary>The buildable-cell floor scales with map AREA.</summary>
    static int MinBuildable(IslandField f, IslandSettings s)
    {
        return Mathf.RoundToInt(s.minBuildableCells * f.sizeScale * f.sizeScale);
    }

    static float Score(IslandField f, IslandSettings s)
    {
        int coveX, coveZ;
        WorldToIndex(f, f.coveCenter.x, f.coveCenter.y, out coveX, out coveZ);
        return f.reachableFraction
             + (f.reachable[coveX, coveZ] ? 1f : 0f)
             + Mathf.Clamp01(f.buildableCells / (float)Mathf.Max(1, MinBuildable(f, s)));
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    static void WorldToIndex(IslandField f, float worldX, float worldZ, out int xi, out int zi)
    {
        float half = (f.verts - 1) * f.spacing * 0.5f;
        xi = Mathf.Clamp(Mathf.RoundToInt((worldX + half) / f.spacing), 0, f.verts - 1);
        zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + half) / f.spacing), 0, f.verts - 1);
    }

    static Vector2 IndexToWorld(IslandField f, int xi, int zi)
    {
        float half = (f.verts - 1) * f.spacing * 0.5f;
        return new Vector2(xi * f.spacing - half, zi * f.spacing - half);
    }

    static float Perlin(float x, float z, float scale, Vector2 offset)
    {
        return Mathf.PerlinNoise(x * scale + offset.x, z * scale + offset.y);
    }

    static float Fbm(float x, float z, float scale, int octaves, float lacunarity, float gain, Vector2[] offsets)
    {
        float sum = 0f, ampSum = 0f, amp = 1f, freq = scale;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Perlin(x, z, freq, offsets[i]);
            ampSum += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return sum / ampSum;
    }

    /// <summary>
    /// Quantize 0..1 into <paramref name="levels"/> steps. Within each step
    /// the transition occupies a <paramref name="width"/> fraction centred on
    /// the step midpoint: small width = cliff, width 1 = no terrace at all.
    /// </summary>
    static float Terrace(float v, int levels, float width)
    {
        if (levels <= 0) return v;
        float f = v * levels;
        int i = Mathf.FloorToInt(f);
        float frac = f - i;
        float e = SmoothStep01((frac - 0.5f) / width + 0.5f);
        return (i + e) / levels;
    }

    /// <summary>Distance from (x,z) to segment a→b; <paramref name="t"/> is the 0..1 position of the closest point along it.</summary>
    static float DistToSegment(float x, float z, Vector2 a, Vector2 b, out float t)
    {
        float abx = b.x - a.x, abz = b.y - a.y;
        float len2 = abx * abx + abz * abz;
        t = len2 > 0f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.y) * abz) / len2) : 0f;
        float cx = a.x + abx * t, cz = a.y + abz * t;
        return Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
    }

    static float SmoothStep01(float u)
    {
        u = Mathf.Clamp01(u);
        return u * u * (3f - 2f * u);
    }

    static float SmoothStep(float v, float edge0, float edge1)
    {
        return SmoothStep01((v - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
    }

    /// <summary>Stable per-vertex hash in 0..1 (independent of System.Random consumption order).</summary>
    static float Hash01(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static Vector2 Offset(System.Random rng)
    {
        return new Vector2((float)(rng.NextDouble() * 1000.0), (float)(rng.NextDouble() * 1000.0));
    }
}

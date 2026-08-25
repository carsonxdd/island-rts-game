using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Environment filler: palms, rocks, bushes, grass, driftwood, barrels, crates.
    /// None of these carry gameplay logic, so they are the safest set to drop into a
    /// scene first.
    /// </summary>
    public static partial class LowPolyShapes
    {
        static partial void AddEnvironmentImpl(List<AssetDef> list)
        {
            list.Add(new AssetDef("Palm_Tall", AssetCategory.Environment,
                () => Palm(seed: 1101, height: 5.0f, lean: 0.55f, frondCount: 8, coconuts: 3),
                "~1.6 wide crown, 5.0 tall"));

            list.Add(new AssetDef("Palm_Bent", AssetCategory.Environment,
                () => Palm(seed: 1102, height: 4.0f, lean: 1.35f, frondCount: 7, coconuts: 2),
                "~1.5 wide crown, 4.0 tall, strong lean"));

            list.Add(new AssetDef("Palm_Young", AssetCategory.Environment,
                () => Palm(seed: 1103, height: 2.2f, lean: 0.25f, frondCount: 6, coconuts: 0),
                "~1.1 wide crown, 2.2 tall"));

            list.Add(new AssetDef("Rock_Small", AssetCategory.Environment,
                () => Boulder(1201, new Vector3(0.55f, 0.38f, 0.5f), 1),
                "0.55 x 0.38"));

            list.Add(new AssetDef("Rock_Medium", AssetCategory.Environment,
                () => Boulder(1202, new Vector3(1.1f, 0.8f, 1.0f), 2),
                "1.1 x 0.8"));

            list.Add(new AssetDef("Rock_Large", AssetCategory.Environment,
                () => Boulder(1203, new Vector3(1.9f, 1.5f, 1.7f), 3),
                "1.9 x 1.5"));

            list.Add(new AssetDef("Bush_Round", AssetCategory.Environment,
                () => Bush(1301, 0.9f, 0.75f, 4, false),
                "0.9 x 0.75"));

            list.Add(new AssetDef("Bush_Wide", AssetCategory.Environment,
                () => Bush(1302, 1.4f, 0.5f, 5, false),
                "1.4 x 0.5"));

            list.Add(new AssetDef("GrassTuft", AssetCategory.Environment,
                () => GrassTuft(1401, 0.42f, 0.4f, 14),
                "0.5 x 0.4"));

            list.Add(new AssetDef("Fern", AssetCategory.Environment,
                () => Fern(1402, 0.95f, 0.6f),
                "0.95 x 0.6"));

            list.Add(new AssetDef("DriftwoodLog", AssetCategory.Environment,
                () => Driftwood(1501, 2.2f, 0.19f),
                "2.2 long x 0.38 thick"));

            list.Add(new AssetDef("Barrel", AssetCategory.Environment,
                () => Barrel(0.34f, 0.9f),
                "0.68 dia x 0.9 tall"));

            list.Add(new AssetDef("Crate", AssetCategory.Environment,
                () => Crate(0.8f),
                "0.8 cube"));
        }

        // ==================================================================
        // Palm
        // ==================================================================

        private static MeshBuilder Palm(int seed, float height, float lean, int frondCount, int coconuts)
        {
            MeshBuilder b = new MeshBuilder(seed);

            // ---- Trunk: chained tapered segments following a lean curve --------
            b.Use("PalmBark");
            const int trunkSegments = 7;
            float rootRadius = height * 0.038f + 0.03f;
            float tipRadius = rootRadius * 0.55f;
            float leanDir = b.Rand(0f, 360f);
            Vector3 leanAxis = new Vector3(Mathf.Cos(leanDir * Mathf.Deg2Rad), 0f, Mathf.Sin(leanDir * Mathf.Deg2Rad));

            Vector3 TrunkPoint(float t)
            {
                // t^1.8 keeps the base near-vertical and pushes the bend into the upper trunk,
                // which is what makes a palm read as a palm rather than a bent pole.
                return new Vector3(0f, height * t, 0f) + leanAxis * (lean * Mathf.Pow(t, 1.8f));
            }

            Vector3 top = TrunkPoint(1f);
            for (int i = 0; i < trunkSegments; i++)
            {
                float t0 = (float)i / trunkSegments;
                float t1 = (float)(i + 1) / trunkSegments;
                float r0 = Mathf.Lerp(rootRadius, tipRadius, t0);
                float r1 = Mathf.Lerp(rootRadius, tipRadius, t1);
                // Slight per-segment flare gives the stacked-ring look of real palm bark.
                float flare = (i % 2 == 0) ? 1.12f : 1.0f;
                b.TaperedSegment(TrunkPoint(t0), TrunkPoint(t1), r0 * flare, r1, 5, 12f * i);
            }

            // Root flare so the trunk does not look pushed into the sand.
            b.Prism(Vector3.zero, rootRadius * 1.7f, rootRadius * 1.05f, height * 0.06f, 5);

            // ---- Crown -------------------------------------------------------
            b.Use("FrondDark");
            b.Prism(top - new Vector3(0f, tipRadius * 0.5f, 0f), tipRadius * 1.35f, tipRadius * 0.6f, tipRadius * 2.2f, 5);

            // ---- Coconuts ----------------------------------------------------
            if (coconuts > 0)
            {
                b.Use("TrunkBark");
                for (int i = 0; i < coconuts; i++)
                {
                    float a = (360f / coconuts) * i + b.Rand(-18f, 18f);
                    Vector3 offset = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * tipRadius * 1.5f;
                    b.Rock(top + offset - new Vector3(0f, tipRadius * 1.9f, 0f),
                           Vector3.one * (tipRadius * 1.25f), 0.10f, 2, 5);
                }
            }

            // ---- Fronds ------------------------------------------------------
            b.DoubleSided = true;
            float frondLength = height * 0.42f;
            for (int i = 0; i < frondCount; i++)
            {
                // Alternate the three greens so the crown has internal contrast instead of
                // reading as one flat green blob from camera height.
                b.Use(i % 3 == 0 ? "FrondLight" : (i % 3 == 1 ? "FrondMid" : "FrondDark"));

                float yaw = (360f / frondCount) * i + b.Rand(-10f, 10f);
                // Start angled slightly upward: combined with the droop below this gives the
                // arch a palm actually has. Starting flat or downward reads as a dead tree.
                float pitch = b.Rand(-22f, 4f);
                float len = frondLength * b.Rand(0.82f, 1.12f);

                b.Push();
                b.Translate(top);
                b.RotateY(yaw);
                b.Rotate(pitch, 0f, b.Rand(-12f, 12f));
                // Droop is deliberately NOT a flat fraction of length - on a 5m palm a
                // proportional droop drops the tips below the crown and the whole thing
                // reads as stringy, so longer fronds droop proportionally less.
                Frond(b, len, len * 0.22f, Mathf.Min(len * 0.55f, 0.85f), 5);
                b.Pop();
            }
            b.DoubleSided = false;

            return b;
        }

        /// <summary>
        /// A single frond as a flat, tapered, drooping strip growing along +Z from the
        /// origin. Emitted double-sided by the caller so it reads from underneath.
        /// </summary>
        private static void Frond(MeshBuilder b, float length, float maxHalfWidth, float droop, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments;
                float t1 = (float)(i + 1) / segments;

                float w0 = FrondWidth(t0, maxHalfWidth, i);
                float w1 = FrondWidth(t1, maxHalfWidth, i + 1);

                float y0 = -droop * t0 * t0;
                float y1 = -droop * t1 * t1;
                float z0 = length * t0;
                float z1 = length * t1;

                b.Quad(new Vector3(-w0, y0, z0),
                       new Vector3(-w1, y1, z1),
                       new Vector3(w1, y1, z1),
                       new Vector3(w0, y0, z0));
            }
        }

        private static float FrondWidth(float t, float maxHalfWidth, int station)
        {
            // Widest around a third of the way out, pointed at the tip, narrow at the stem.
            float shape = Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01(t), 0.55f));
            float serration = (station % 2 == 0) ? 1f : 0.82f; // saw-tooth edge, cheap and reads well
            return maxHalfWidth * shape * serration + 0.005f;
        }

        // ==================================================================
        // Rocks
        // ==================================================================

        private static MeshBuilder Boulder(int seed, Vector3 size, int lumps)
        {
            MeshBuilder b = new MeshBuilder(seed);

            b.Use("RockMid");
            b.Rock(Vector3.zero, size, 0.22f, 3, 7);

            // Extra lumps break the silhouette so it does not read as a squashed sphere.
            for (int i = 0; i < lumps; i++)
            {
                b.Use(i % 2 == 0 ? "RockLight" : "RockDark");
                float a = b.Rand(0f, 360f) * Mathf.Deg2Rad;
                float dist = b.Rand(0.2f, 0.42f);
                Vector3 offset = new Vector3(Mathf.Cos(a) * size.x * dist, 0f, Mathf.Sin(a) * size.z * dist);
                Vector3 lumpSize = size * b.Rand(0.38f, 0.62f);
                b.Rock(offset, lumpSize, 0.25f, 2, 6);
            }

            return b;
        }

        // ==================================================================
        // Foliage
        // ==================================================================

        private static MeshBuilder Bush(int seed, float width, float height, int clusters, bool berries)
        {
            MeshBuilder b = new MeshBuilder(seed);

            // One solid rounded mass - template-simple, no separate blobs.
            b.Use("BushGreen");
            Vector3 size = new Vector3(width, height, width);
            b.Rock(Vector3.zero, size, 0.08f, 3, 7);

            if (berries)
            {
                // Berries are chunky and half-embedded: centers sit ON the nominal surface
                // of the ellipsoid, and at 0.16 * width diameter they always straddle the
                // low-jitter (0.08) surface - attached, never floating.
                b.Use("BerryRed");
                Vector3 half = size * 0.5f;
                Vector3 center = new Vector3(0f, half.y, 0f);
                const int count = 8;
                for (int i = 0; i < count; i++)
                {
                    float a = ((360f / count) * i + b.Rand(-14f, 14f)) * Mathf.Deg2Rad;
                    float yUnit = b.Rand(0.15f, 0.62f); // upper hemisphere, visible from the RTS camera
                    float ring = Mathf.Sqrt(1f - yUnit * yUnit);
                    Vector3 unit = new Vector3(Mathf.Cos(a) * ring, yUnit, Mathf.Sin(a) * ring);
                    Vector3 p = center + Vector3.Scale(unit, half);
                    float berry = width * 0.16f;
                    b.Rock(p - new Vector3(0f, berry * 0.5f, 0f), Vector3.one * berry, 0.10f, 2, 5);
                }
            }

            return b;
        }

        private static MeshBuilder GrassTuft(int seed, float spread, float height, int blades)
        {
            MeshBuilder b = new MeshBuilder(seed);
            b.Use("GrassGreen");
            b.DoubleSided = true;

            for (int i = 0; i < blades; i++)
            {
                float a = b.Rand(0f, 360f);
                float dist = b.Rand(0f, spread * 0.5f);
                float h = height * b.Rand(0.55f, 1.15f);
                float lean = b.Rand(18f, 46f);

                b.Push();
                b.Translate(Mathf.Cos(a * Mathf.Deg2Rad) * dist, 0f, Mathf.Sin(a * Mathf.Deg2Rad) * dist);
                b.RotateY(b.Rand(0f, 360f));

                // Two-segment blade: straight from the root, then folding over.
                float w = h * 0.10f;
                Vector3 mid = new Vector3(0f, h * 0.62f, h * 0.10f);
                Vector3 tip = mid + new Vector3(0f, h * 0.30f, h * 0.38f) * Mathf.Sin(lean * Mathf.Deg2Rad) * 1.6f;

                b.Quad(new Vector3(-w, 0f, 0f), new Vector3(-w * 0.6f, mid.y, mid.z),
                       new Vector3(w * 0.6f, mid.y, mid.z), new Vector3(w, 0f, 0f));
                b.Tri(new Vector3(-w * 0.6f, mid.y, mid.z), tip, new Vector3(w * 0.6f, mid.y, mid.z));

                b.Pop();
            }

            b.DoubleSided = false;
            return b;
        }

        private static MeshBuilder Fern(int seed, float spread, float height)
        {
            MeshBuilder b = new MeshBuilder(seed);

            b.Use("WoodDark");
            b.Prism(Vector3.zero, 0.035f, 0.02f, height * 0.25f, 5);

            b.Use("FernGreen");
            b.DoubleSided = true;
            const int fronds = 7;
            for (int i = 0; i < fronds; i++)
            {
                float yaw = (360f / fronds) * i + b.Rand(-14f, 14f);
                float len = spread * 0.5f * b.Rand(0.8f, 1.1f);

                b.Push();
                b.Translate(0f, height * 0.25f, 0f);
                b.RotateY(yaw);
                b.Rotate(b.Rand(14f, 34f), 0f, 0f);
                Frond(b, len, len * 0.22f, len * 0.55f, 4);
                b.Pop();
            }
            b.DoubleSided = false;

            return b;
        }

        // ==================================================================
        // Props
        // ==================================================================

        private static MeshBuilder Driftwood(int seed, float length, float radius)
        {
            MeshBuilder b = new MeshBuilder(seed);
            b.Use("WoodPale");

            // Main trunk, slightly kinked in the middle so it does not read as a pipe.
            Vector3 a = new Vector3(-length * 0.5f, radius * 0.9f, 0f);
            Vector3 mid = new Vector3(0f, radius, b.Rand(-0.1f, 0.1f));
            Vector3 c = new Vector3(length * 0.5f, radius * 0.75f, b.Rand(-0.12f, 0.12f));

            b.TaperedSegment(a, mid, radius * 0.85f, radius, 6);
            b.TaperedSegment(mid, c, radius, radius * 0.7f, 6);

            // Broken stubs.
            b.Use("WoodDark");
            b.TaperedSegment(mid, mid + new Vector3(length * 0.14f, radius * 1.7f, radius * 1.4f), radius * 0.42f, 0.01f, 5);
            b.TaperedSegment(new Vector3(-length * 0.18f, radius, 0f),
                             new Vector3(-length * 0.26f, radius * 2.1f, -radius * 1.2f), radius * 0.34f, 0.01f, 5);

            return b;
        }

        private static MeshBuilder Barrel(float radius, float height)
        {
            MeshBuilder b = new MeshBuilder(1601);

            // Belly built from three stacked prisms so the barrel bulges at the waist.
            b.Use("BarrelWood");
            float r0 = radius * 0.86f;
            b.Prism(Vector3.zero, r0, radius, height * 0.35f, 8, 0f, true, false);
            b.Prism(new Vector3(0f, height * 0.35f, 0f), radius, radius, height * 0.30f, 8, 0f, false, false);
            b.Prism(new Vector3(0f, height * 0.65f, 0f), radius, r0, height * 0.35f, 8, 0f, false, true);

            // Hoops.
            b.Use("BarrelBand");
            float bandH = height * 0.055f;
            b.Prism(new Vector3(0f, height * 0.20f, 0f), radius * 0.99f, radius * 1.02f, bandH, 8, 0f, false, false);
            b.Prism(new Vector3(0f, height * 0.72f, 0f), radius * 1.02f, radius * 0.99f, bandH, 8, 0f, false, false);

            return b;
        }

        private static MeshBuilder Crate(float size)
        {
            MeshBuilder b = new MeshBuilder(1602);

            // Pale body against a dark frame. The reverse (dark frame colour on a
            // mid-brown body) collapses into one brown mass once the crate is in shadow.
            b.Use("WoodPale");
            b.BoxOnGround(Vector3.zero, new Vector3(size, size, size) * 0.88f);

            // Corner posts and rails, proud of the body so they catch the light.
            b.Use("WoodDark");
            float t = size * 0.12f;
            float h = size * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    b.BoxOnGround(new Vector3(sx * h * 0.95f, 0f, sz * h * 0.95f), new Vector3(t, size, t));
                }
            }
            // Rims at the top and bottom edges. size * 0.5 is the crate's MID height, not
            // its top, and putting them there gives it a belt instead of a frame.
            foreach (float y in new[] { t * 0.6f, size - t * 0.6f })
            {
                b.Box(new Vector3(0f, y, 0f), new Vector3(size * 1.0f, t, size * 0.99f));
                b.Box(new Vector3(0f, y, 0f), new Vector3(size * 0.99f, t, size * 1.0f));
            }

            return b;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// The three gatherable node types. These read at a glance because workers path to
    /// them constantly: tree = tall green canopy, berry bush = low green with red dots,
    /// rock node = solid grey boulder with ore crystals.
    /// </summary>
    public static partial class LowPolyShapes
    {
        static partial void AddResourcesImpl(List<AssetDef> list)
        {
            // Tree plus two variants: same species, different seed and height. TreeVariance
            // on the gameplay prefab picks one per instance. All variants come from the same
            // builder so they share one material key order - required for the runtime swap.
            list.Add(new AssetDef("Tree", AssetCategory.Resources,
                () => BroadleafTree(4101, 3.6f), "~2.2 wide canopy, 3.6 tall"));

            list.Add(new AssetDef("Tree_B", AssetCategory.Resources,
                () => BroadleafTree(4113, 3.4f), "variant: ~2.1 wide canopy, 3.4 tall"));

            list.Add(new AssetDef("Tree_C", AssetCategory.Resources,
                () => BroadleafTree(4127, 3.8f), "variant: ~2.4 wide canopy, 3.8 tall"));

            list.Add(new AssetDef("Tree_Small", AssetCategory.Resources,
                () => BroadleafTree(4102, 2.6f), "~1.6 wide canopy, 2.6 tall"));

            list.Add(new AssetDef("BerryBush", AssetCategory.Resources,
                () => Bush(4201, 1.0f, 0.8f, 5, true), "1.0 x 0.8"));

            list.Add(new AssetDef("RockNode", AssetCategory.Resources,
                () => OreRock(4301, 1.3f), "1.3 x 1.0"));
        }

        // ==================================================================
        // Tree
        // ==================================================================

        private static MeshBuilder BroadleafTree(int seed, float height)
        {
            MeshBuilder b = new MeshBuilder(seed);

            float trunkH = height * 0.42f;
            float rootR = height * 0.05f;
            float topR = rootR * 0.55f;

            // ---- Trunk with a slight natural sway --------------------------------
            b.Use("TrunkBark");
            const int segs = 4;
            float sway = height * 0.04f;
            float swayDir = b.Rand(0f, 360f);
            Vector3 swayAxis = new Vector3(Mathf.Cos(swayDir * Mathf.Deg2Rad), 0f, Mathf.Sin(swayDir * Mathf.Deg2Rad));

            Vector3 TrunkAt(float t) { return new Vector3(0f, trunkH * t, 0f) + swayAxis * (sway * t * t); }

            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs;
                float t1 = (float)(i + 1) / segs;
                b.TaperedSegment(TrunkAt(t0), TrunkAt(t1),
                                 Mathf.Lerp(rootR, topR, t0), Mathf.Lerp(rootR, topR, t1), 6);
            }

            // Root flare.
            b.Prism(Vector3.zero, rootR * 1.7f, rootR * 1.02f, height * 0.05f, 6);

            Vector3 crotch = TrunkAt(1f);

            // ---- Two branches lifting into the canopy ------------------------------
            for (int i = 0; i < 2; i++)
            {
                float a = b.Rand(0f, 360f);
                Vector3 dir = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 1.5f, Mathf.Sin(a * Mathf.Deg2Rad)).normalized;
                b.TaperedSegment(crotch, crotch + dir * height * 0.20f, topR * 0.75f, topR * 0.35f, 5);
            }

            // ---- Canopy: three overlapping faceted blobs, not one sphere -------------
            float canopyW = height * 0.62f;
            // Side-blob placement is seeded so every seed gets its own canopy shape -
            // this is what makes the Tree_B/Tree_C variants read as different trees.
            float blobA = b.Rand(0f, 360f) * Mathf.Deg2Rad;
            float blobB = blobA + Mathf.PI * b.Rand(0.7f, 1.3f);
            Vector3[] canopyOffsets =
            {
                new Vector3(0f, height * 0.10f, 0f),
                new Vector3(Mathf.Cos(blobA) * canopyW * b.Rand(0.24f, 0.38f), height * 0.02f,
                            Mathf.Sin(blobA) * canopyW * b.Rand(0.24f, 0.38f)),
                new Vector3(Mathf.Cos(blobB) * canopyW * b.Rand(0.22f, 0.34f), height * 0.01f,
                            Mathf.Sin(blobB) * canopyW * b.Rand(0.22f, 0.34f)),
            };
            string[] canopyKeys = { "FrondMid", "FrondDark", "FrondLight" };

            for (int i = 0; i < canopyOffsets.Length; i++)
            {
                b.Use(canopyKeys[i]);
                Vector3 size = new Vector3(canopyW, canopyW * 0.78f, canopyW) * (i == 0 ? 1f : 0.72f);
                b.Rock(crotch + canopyOffsets[i] - new Vector3(0f, size.y * 0.5f, 0f), size, 0.20f, 3, 7);
            }

            return b;
        }

        // ==================================================================
        // Rock node
        // ==================================================================

        private static MeshBuilder OreRock(int seed, float size)
        {
            MeshBuilder b = new MeshBuilder(seed);

            // One solid boulder - no satellite rocks, so the node reads as a single object.
            b.Use("RockMid");
            Vector3 bodySize = new Vector3(size * 1.15f, size * 0.8f, size * 1.05f);
            b.Rock(Vector3.zero, bodySize, 0.14f, 3, 7);

            // ---- Ore crystals: tapered spikes rooted deep INSIDE the boulder that poke
            //      out through the surface, so they always read as attached.
            b.Use("OreVein");
            Vector3 half = bodySize * 0.5f;
            Vector3 center = new Vector3(0f, half.y, 0f);
            const int crystals = 4;
            for (int i = 0; i < crystals; i++)
            {
                float a = ((360f / crystals) * i + 25f + b.Rand(-18f, 18f)) * Mathf.Deg2Rad;
                float yUnit = b.Rand(0.30f, 0.75f);
                float ring = Mathf.Sqrt(1f - yUnit * yUnit);
                Vector3 unit = new Vector3(Mathf.Cos(a) * ring, yUnit, Mathf.Sin(a) * ring);

                Vector3 inner = center + Vector3.Scale(unit * 0.35f, half);                // buried root
                Vector3 outer = center + Vector3.Scale(unit * b.Rand(1.25f, 1.45f), half); // tip past the surface
                b.TaperedSegment(inner, outer, size * b.Rand(0.10f, 0.14f), 0f, 4, 45f);
            }

            return b;
        }
    }
}

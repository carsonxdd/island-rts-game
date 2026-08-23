using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// The three gatherable node types. These read at a glance because workers path to
    /// them constantly: tree = tall green canopy, berry bush = low green with red dots,
    /// rock node = grey cluster with ore flecks.
    /// </summary>
    public static partial class LowPolyShapes
    {
        static partial void AddResourcesImpl(List<AssetDef> list)
        {
            list.Add(new AssetDef("Tree", AssetCategory.Resources,
                () => BroadleafTree(4101, 3.6f), "~2.2 wide canopy, 3.6 tall"));

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
            Vector3[] canopyOffsets =
            {
                new Vector3(0f, height * 0.10f, 0f),
                new Vector3(canopyW * 0.30f, height * 0.02f, -canopyW * 0.20f),
                new Vector3(-canopyW * 0.26f, height * 0.01f, canopyW * 0.24f),
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

            // Main mass plus two satellites, so it reads as a quarryable outcrop rather
            // than a single boulder.
            b.Use("RockMid");
            b.Rock(Vector3.zero, new Vector3(size, size * 0.78f, size * 0.9f), 0.22f, 3, 7);

            b.Use("RockDark");
            b.Rock(new Vector3(size * 0.34f, 0f, size * 0.26f),
                   new Vector3(size * 0.52f, size * 0.46f, size * 0.5f), 0.24f, 2, 6);

            b.Use("RockLight");
            b.Rock(new Vector3(-size * 0.36f, 0f, -size * 0.20f),
                   new Vector3(size * 0.44f, size * 0.36f, size * 0.42f), 0.24f, 2, 6);

            // ---- Ore veins: small angular chips pushed just proud of the surface -------
            b.Use("OreVein");
            const int chips = 6;
            for (int i = 0; i < chips; i++)
            {
                float a = (360f / chips) * i + b.Rand(-24f, 24f);
                float r = size * b.Rand(0.24f, 0.40f);
                float y = size * b.Rand(0.18f, 0.58f);
                Vector3 p = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, y, Mathf.Sin(a * Mathf.Deg2Rad) * r);

                b.Push();
                b.Translate(p);
                b.Rotate(b.Rand(-40f, 40f), a, b.Rand(-40f, 40f));
                b.Box(Vector3.zero, new Vector3(size * 0.13f, size * 0.07f, size * 0.10f));
                b.Pop();
            }

            return b;
        }
    }
}

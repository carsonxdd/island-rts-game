using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Buildings, authored to the footprints the gameplay prefabs already use so a later
    /// swap does not move collider bounds and force a NavMesh re-bake:
    ///   Hut         2 x 2, body 1.5 tall   (Hut.prefab scale)
    ///   Wooden wall 1 x 0.3, 1.2 tall      (WallConnector.WOODEN_HEIGHT / WALL_THICKNESS)
    ///   Stone wall  1 x 0.3, 2.0 tall      (WallConnector.STONE_HEIGHT)
    ///   Watchtower  2 x 2, 4.0 tall        (WatchTower.prefab scale)
    ///   Campfire    1.5 dia                (Campfire.prefab scale)
    /// </summary>
    public static partial class LowPolyShapes
    {
        private const float WallThickness = 0.3f;
        private const float WoodenWallHeight = 1.2f;
        private const float StoneWallHeight = 2.0f;

        static partial void AddBuildingsImpl(List<AssetDef> list)
        {
            list.Add(new AssetDef("Hut", AssetCategory.Buildings,
                () => Hut(2f, 1.5f), "2.0 x 2.0 footprint, 2.6 to roof peak"));

            list.Add(new AssetDef("WoodenWall_Segment", AssetCategory.Buildings,
                () => PalisadeWall(1f, WoodenWallHeight, WallThickness), "1.0 x 0.3, 1.2 tall"));

            list.Add(new AssetDef("StoneWall_Segment", AssetCategory.Buildings,
                () => StoneWall(1f, StoneWallHeight, WallThickness), "1.0 x 0.3, 2.0 tall"));

            list.Add(new AssetDef("Gate_Wooden", AssetCategory.Buildings,
                () => Gate(1f, WoodenWallHeight, WallThickness), "1.0 x 0.3, 1.2 tall, 0.62 opening"));

            list.Add(new AssetDef("Watchtower", AssetCategory.Buildings,
                () => Watchtower(2f, 4f), "2.0 x 2.0 footprint, 4.0 tall"));

            list.Add(new AssetDef("Campfire", AssetCategory.Buildings,
                () => Campfire(0.75f), "1.5 dia, ~1.0 tall including flame"));
        }

        // ==================================================================
        // Hut
        // ==================================================================

        private static MeshBuilder Hut(float footprint, float bodyHeight)
        {
            MeshBuilder b = new MeshBuilder(2101);
            float half = footprint * 0.5f;

            // ---- Walls: one plain, slightly tapered block ----------------------
            b.Use("WoodPlank");
            b.Frustum(Vector3.zero,
                      new Vector2(footprint, footprint),
                      new Vector2(footprint * 0.94f, footprint * 0.94f),
                      bodyHeight);

            // ---- Door: a single dark panel on the front face -------------------
            b.Use("Charcoal");
            float doorW = footprint * 0.30f;
            float doorH = bodyHeight * 0.60f;
            b.Box(new Vector3(0f, doorH * 0.5f, -half * 0.97f), new Vector3(doorW, doorH, 0.10f));

            // ---- One window panel on each side face ----------------------------
            float winS = footprint * 0.18f;
            float winY = bodyHeight * 0.55f;
            b.Box(new Vector3(-half * 0.97f, winY, 0f), new Vector3(0.10f, winS, winS));
            b.Box(new Vector3(half * 0.97f, winY, 0f), new Vector3(0.10f, winS, winS));

            // ---- Roof: one overhanging pyramid, one tone (peak stays at 2.6) -----
            b.Use("ThatchDark");
            b.Pyramid(new Vector3(0f, bodyHeight, 0f), footprint * 1.3f, 1.1f);

            return b;
        }

        // ==================================================================
        // Walls
        // ==================================================================

        private static MeshBuilder PalisadeWall(float width, float height, float thickness)
        {
            MeshBuilder b = new MeshBuilder(2201);

            // Vertical logs with pointed tips - the whole palisade read, nothing else.
            b.Use("WoodLog");
            const int logs = 4;
            float logR = width / (logs * 2f) * 1.06f;
            for (int i = 0; i < logs; i++)
            {
                float x = -width * 0.5f + logR + (width - logR * 2f) * i / (logs - 1f);
                float h = height * b.Rand(0.90f, 1.0f);

                b.Prism(new Vector3(x, 0f, 0f), logR, logR * 0.94f, h - logR * 1.2f, 4, 0f, true, false);
                // Sharpened tip.
                b.Prism(new Vector3(x, h - logR * 1.2f, 0f), logR * 0.94f, 0f, logR * 1.2f, 4);
            }

            return b;
        }

        private static MeshBuilder StoneWall(float width, float height, float thickness)
        {
            MeshBuilder b = new MeshBuilder(2202);

            // Three chunky courses of plain blocks, alternating tone and joint offset -
            // big shapes only.
            const int courses = 3;
            float courseH = (height - 0.12f) / courses;

            for (int c = 0; c < courses; c++)
            {
                int blocks = (c % 2 == 0) ? 2 : 1;
                for (int i = 0; i < blocks; i++)
                {
                    b.Use((c + i) % 2 == 0 ? "StoneBlock" : "StoneShadow");
                    float blockW = width / blocks;
                    float x = -width * 0.5f + blockW * (i + 0.5f);
                    b.BoxOnGround(new Vector3(x, courseH * c, 0f),
                                  new Vector3(blockW * 0.97f, courseH * 0.97f, thickness));
                }
            }

            // Capstone.
            b.Use("StoneBlock");
            b.Frustum(new Vector3(0f, height - 0.12f, 0f),
                      new Vector2(width * 1.04f, thickness * 1.1f),
                      new Vector2(width * 0.96f, thickness * 0.9f),
                      0.12f);

            return b;
        }

        private static MeshBuilder Gate(float width, float height, float thickness)
        {
            MeshBuilder b = new MeshBuilder(2203);

            float postW = width * 0.17f;
            float openingW = width - postW * 2f;

            // ---- Frame -------------------------------------------------------
            b.Use("WoodLog");
            b.BoxOnGround(new Vector3(-(width - postW) * 0.5f, 0f, 0f), new Vector3(postW, height, thickness));
            b.BoxOnGround(new Vector3((width - postW) * 0.5f, 0f, 0f), new Vector3(postW, height, thickness));

            // Lintel with a small overhang past the posts.
            b.Use("WoodDark");
            b.Box(new Vector3(0f, height - 0.09f, 0f), new Vector3(width * 1.06f, 0.18f, thickness * 1.08f));

            // ---- Two door leaves, hinged open by a few degrees so the gate reads as
            //      a gate rather than a filled panel.
            b.Use("WoodPlank");
            float leafW = openingW * 0.5f;
            float leafH = height - 0.2f;

            for (int side = -1; side <= 1; side += 2)
            {
                b.Push();
                b.Translate(side * openingW * 0.5f, 0f, 0f);
                b.RotateY(side * -12f);
                b.BoxOnGround(new Vector3(-side * leafW * 0.5f, 0f, 0f), new Vector3(leafW, leafH, thickness * 0.42f));
                b.Pop();
            }

            return b;
        }

        // ==================================================================
        // Watchtower
        // ==================================================================

        private static MeshBuilder Watchtower(float footprint, float totalHeight)
        {
            MeshBuilder b = new MeshBuilder(2301);

            float platformY = totalHeight * 0.62f;
            float baseSpread = footprint * 0.44f;
            float topSpread = footprint * 0.30f;
            float legR = 0.09f;

            // ---- Four splayed legs: the tower silhouette -----------------------
            b.Use("WoodLog");
            for (int i = 0; i < 4; i++)
            {
                float ax = (i == 0 || i == 3) ? -1f : 1f;
                float az = (i < 2) ? -1f : 1f;
                b.TaperedSegment(new Vector3(ax * baseSpread, 0f, az * baseSpread),
                                 new Vector3(ax * topSpread, platformY, az * topSpread),
                                 legR, legR * 0.82f, 4);
            }

            // ---- Platform ------------------------------------------------------
            b.Use("WoodPlank");
            float platW = footprint * 0.92f;
            b.Box(new Vector3(0f, platformY + 0.06f, 0f), new Vector3(platW, 0.12f, platW));

            // ---- Railing: four low walls ----------------------------------------
            float railH = 0.44f;
            float railY = platformY + 0.12f;
            float railInset = platW * 0.5f - 0.06f;
            for (int side = 0; side < 4; side++)
            {
                b.Push();
                b.RotateY(90f * side);
                b.BoxOnGround(new Vector3(0f, railY, -railInset), new Vector3(platW * 0.78f, railH, 0.08f));
                b.Pop();
            }

            // ---- Roof: four corner posts and a single one-tone pyramid -----------
            b.Use("WoodDark");
            float roofPostY = railY + railH;
            float roofPostH = totalHeight - roofPostY - 0.55f;
            for (int i = 0; i < 4; i++)
            {
                float ax = (i == 0 || i == 3) ? -1f : 1f;
                float az = (i < 2) ? -1f : 1f;
                b.Prism(new Vector3(ax * platW * 0.42f, roofPostY, az * platW * 0.42f), 0.05f, 0.045f, roofPostH, 4);
            }

            b.Use("ThatchDark");
            b.Pyramid(new Vector3(0f, roofPostY + roofPostH, 0f), footprint * 1.14f, 0.55f);

            return b;
        }

        // ==================================================================
        // Campfire
        // ==================================================================

        private static MeshBuilder Campfire(float radius)
        {
            MeshBuilder b = new MeshBuilder(2401);

            // ---- Ash bed ------------------------------------------------------
            b.Use("Charcoal");
            b.Prism(Vector3.zero, radius * 0.72f, radius * 0.66f, 0.05f, 7);

            // ---- Stone ring ---------------------------------------------------
            const int stones = 6;
            for (int i = 0; i < stones; i++)
            {
                b.Use(i % 2 == 0 ? "RockLight" : "RockMid");
                float a = (360f / stones) * i + b.Rand(-6f, 6f);
                Vector3 p = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * radius * 0.86f;
                b.Rock(p, new Vector3(radius * 0.44f, radius * 0.36f, radius * 0.4f) * b.Rand(0.9f, 1.1f), 0.18f, 2, 5);
            }

            // ---- Log teepee ---------------------------------------------------
            b.Use("WoodLog");
            const int logs = 4;
            for (int i = 0; i < logs; i++)
            {
                float a = (360f / logs) * i + 45f;
                Vector3 outer = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * radius * 0.55f;
                Vector3 apex = new Vector3(outer.x * 0.14f, radius * 0.78f, outer.z * 0.14f);
                b.TaperedSegment(outer + new Vector3(0f, 0.03f, 0f), apex, radius * 0.09f, radius * 0.06f, 4);
            }

            // ---- Flame: two stacked cones on the HDR-emissive palette entries
            //      (intensity 2-3) so they clear the Global Volume Bloom threshold
            //      of 1.0 without the threshold being lowered globally.
            b.Use("Ember");
            b.Prism(new Vector3(0f, 0.04f, 0f), radius * 0.42f, radius * 0.24f, radius * 0.52f, 6, 22f);

            b.Use("FireCore");
            b.Prism(new Vector3(0f, radius * 0.52f, 0f), radius * 0.24f, 0f, radius * 0.55f, 5, -30f);

            return b;
        }
    }
}

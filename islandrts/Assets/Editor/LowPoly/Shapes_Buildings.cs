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

            // ---- Foundation --------------------------------------------------
            b.Use("StoneShadow");
            b.BoxOnGround(Vector3.zero, new Vector3(footprint * 1.05f, 0.12f, footprint * 1.05f));

            // ---- Walls: slight inward taper reads as hand-built, not a shipping box.
            b.Use("WoodPlank");
            b.Frustum(new Vector3(0f, 0.12f, 0f),
                      new Vector2(footprint, footprint),
                      new Vector2(footprint * 0.93f, footprint * 0.93f),
                      bodyHeight);

            // ---- Corner posts ------------------------------------------------
            b.Use("WoodDark");
            float postR = 0.08f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    b.Prism(new Vector3(sx * half * 0.97f, 0.1f, sz * half * 0.97f),
                            postR, postR * 0.85f, bodyHeight + 0.1f, 5);
                }
            }

            // ---- Plank seams on the two most-visible walls --------------------
            b.Use("WoodDark");
            for (int i = 1; i <= 3; i++)
            {
                float y = 0.12f + bodyHeight * (i / 4f);
                b.Box(new Vector3(0f, y, -half * 0.99f), new Vector3(footprint * 0.92f, 0.045f, 0.04f));
                b.Box(new Vector3(-half * 0.99f, y, 0f), new Vector3(0.04f, 0.045f, footprint * 0.92f));
            }

            // ---- Doorway: a recessed dark panel plus a frame. No booleans needed,
            //      and at RTS camera distance an inset panel reads as an opening.
            b.Use("Charcoal");
            float doorW = footprint * 0.34f;
            float doorH = bodyHeight * 0.62f;
            b.Box(new Vector3(0f, 0.12f + doorH * 0.5f, -half * 0.965f), new Vector3(doorW, doorH, 0.06f));

            b.Use("WoodDark");
            b.Box(new Vector3(0f, 0.12f + doorH, -half * 0.94f), new Vector3(doorW + 0.14f, 0.08f, 0.08f));
            b.Box(new Vector3(-(doorW + 0.07f) * 0.5f, 0.12f + doorH * 0.5f, -half * 0.94f), new Vector3(0.07f, doorH, 0.08f));
            b.Box(new Vector3((doorW + 0.07f) * 0.5f, 0.12f + doorH * 0.5f, -half * 0.94f), new Vector3(0.07f, doorH, 0.08f));

            // ---- Thatch roof: overhanging pyramid, layered in two tones --------
            float roofBase = 0.12f + bodyHeight;
            float roofOverhang = footprint * 1.34f;

            b.Use("ThatchDark");
            b.Pyramid(new Vector3(0f, roofBase, 0f), roofOverhang, 1.1f);

            b.Use("ThatchLight");
            // Second, smaller pyramid slightly above gives a layered-thatch silhouette.
            b.Pyramid(new Vector3(0f, roofBase + 0.34f, 0f), roofOverhang * 0.68f, 0.82f);

            // Ridge cap.
            b.Use("WoodDark");
            b.Prism(new Vector3(0f, roofBase + 1.06f, 0f), 0.09f, 0.05f, 0.22f, 5);

            return b;
        }

        // ==================================================================
        // Walls
        // ==================================================================

        private static MeshBuilder PalisadeWall(float width, float height, float thickness)
        {
            MeshBuilder b = new MeshBuilder(2201);

            // Sharpened vertical logs of varying height - the uneven top edge is what
            // separates a palisade read from an extruded cube.
            b.Use("WoodLog");
            const int logs = 5;
            float logR = width / (logs * 2f) * 1.06f;
            for (int i = 0; i < logs; i++)
            {
                float x = -width * 0.5f + logR + (width - logR * 2f) * i / (logs - 1f);
                float h = height * b.Rand(0.88f, 1.0f);
                float z = b.Rand(-thickness * 0.06f, thickness * 0.06f);

                b.Prism(new Vector3(x, 0f, z), logR, logR * 0.94f, h - logR * 1.3f, 5, 0f, true, false);
                // Sharpened tip.
                b.Prism(new Vector3(x, h - logR * 1.3f, z), logR * 0.94f, 0f, logR * 1.3f, 5);
            }

            // Horizontal binding rails, front and back.
            b.Use("WoodDark");
            for (int i = 0; i < 2; i++)
            {
                float y = height * (i == 0 ? 0.28f : 0.68f);
                b.Box(new Vector3(0f, y, -thickness * 0.34f), new Vector3(width, 0.075f, 0.07f));
                b.Box(new Vector3(0f, y, thickness * 0.34f), new Vector3(width, 0.075f, 0.07f));
            }

            return b;
        }

        private static MeshBuilder StoneWall(float width, float height, float thickness)
        {
            MeshBuilder b = new MeshBuilder(2202);

            // Four courses of jittered blocks, alternating the joint offset so the
            // courses interlock like real coursed masonry.
            const int courses = 4;
            float courseH = (height - 0.14f) / courses;

            for (int c = 0; c < courses; c++)
            {
                int blocks = (c % 2 == 0) ? 2 : 3;
                float y = courseH * c;

                for (int i = 0; i < blocks; i++)
                {
                    b.Use((c + i) % 2 == 0 ? "StoneBlock" : "StoneShadow");

                    float blockW = width / blocks;
                    float x = -width * 0.5f + blockW * (i + 0.5f);
                    Vector3 size = new Vector3(
                        blockW * b.Rand(0.90f, 0.99f),
                        courseH * b.Rand(0.88f, 0.99f),
                        thickness * b.Rand(0.88f, 1.0f));

                    b.Push();
                    b.Translate(x, y, b.Rand(-0.012f, 0.012f));
                    b.RotateY(b.Rand(-2.5f, 2.5f));
                    b.BoxOnGround(Vector3.zero, size);
                    b.Pop();
                }
            }

            // Capstone.
            b.Use("StoneBlock");
            b.Frustum(new Vector3(0f, height - 0.14f, 0f),
                      new Vector2(width * 1.04f, thickness * 1.14f),
                      new Vector2(width * 0.96f, thickness * 0.94f),
                      0.14f);

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

                // Iron bracing.
                b.Use("MetalDark");
                b.Box(new Vector3(-side * leafW * 0.5f, leafH * 0.24f, 0f), new Vector3(leafW * 0.94f, 0.07f, thickness * 0.52f));
                b.Box(new Vector3(-side * leafW * 0.5f, leafH * 0.76f, 0f), new Vector3(leafW * 0.94f, 0.07f, thickness * 0.52f));
                b.Use("WoodPlank");
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
            float legR = 0.085f;

            // ---- Splayed legs -------------------------------------------------
            b.Use("WoodLog");
            Vector3[] legBottom = new Vector3[4];
            Vector3[] legTop = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float ax = (i == 0 || i == 3) ? -1f : 1f;
                float az = (i < 2) ? -1f : 1f;
                legBottom[i] = new Vector3(ax * baseSpread, 0f, az * baseSpread);
                legTop[i] = new Vector3(ax * topSpread, platformY, az * topSpread);
                b.TaperedSegment(legBottom[i], legTop[i], legR, legR * 0.82f, 5);
            }

            // ---- Cross bracing on each of the four faces -----------------------
            b.Use("WoodDark");
            int[,] faces = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 } };
            for (int f = 0; f < 4; f++)
            {
                int a = faces[f, 0], c = faces[f, 1];
                for (int level = 0; level < 2; level++)
                {
                    float t0 = 0.14f + level * 0.38f;
                    float t1 = t0 + 0.34f;
                    Vector3 aLow = Vector3.Lerp(legBottom[a], legTop[a], t0);
                    Vector3 aHigh = Vector3.Lerp(legBottom[a], legTop[a], t1);
                    Vector3 cLow = Vector3.Lerp(legBottom[c], legTop[c], t0);
                    Vector3 cHigh = Vector3.Lerp(legBottom[c], legTop[c], t1);
                    b.Beam(aLow, cHigh, 0.055f, 0.055f);
                    b.Beam(cLow, aHigh, 0.055f, 0.055f);
                }
            }

            // ---- Platform ------------------------------------------------------
            b.Use("WoodPlank");
            float platW = footprint * 0.92f;
            b.Box(new Vector3(0f, platformY + 0.06f, 0f), new Vector3(platW, 0.12f, platW));

            // Deck planks.
            b.Use("WoodDark");
            for (int i = -2; i <= 2; i++)
            {
                b.Box(new Vector3(i * platW * 0.2f, platformY + 0.125f, 0f), new Vector3(0.035f, 0.02f, platW));
            }

            // ---- Railing: four low walls with corner gaps ----------------------
            b.Use("WoodPlank");
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

            // ---- Roof on four short posts ---------------------------------------
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
            b.Use("ThatchLight");
            b.Pyramid(new Vector3(0f, roofPostY + roofPostH + 0.17f, 0f), footprint * 0.7f, 0.42f);

            // ---- Ladder up one face ---------------------------------------------
            b.Use("WoodPale");
            Vector3 ladderBottom = new Vector3(0f, 0f, baseSpread * 1.06f);
            Vector3 ladderTop = new Vector3(0f, platformY, topSpread * 1.02f);
            b.Beam(ladderBottom + Vector3.left * 0.16f, ladderTop + Vector3.left * 0.16f, 0.05f, 0.05f);
            b.Beam(ladderBottom + Vector3.right * 0.16f, ladderTop + Vector3.right * 0.16f, 0.05f, 0.05f);
            int rungs = 7;
            for (int i = 1; i < rungs; i++)
            {
                float t = (float)i / rungs;
                Vector3 p = Vector3.Lerp(ladderBottom, ladderTop, t);
                b.Box(p, new Vector3(0.36f, 0.035f, 0.035f));
            }

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
            b.Prism(Vector3.zero, radius * 0.72f, radius * 0.66f, 0.05f, 9);

            // ---- Stone ring ---------------------------------------------------
            const int stones = 9;
            for (int i = 0; i < stones; i++)
            {
                b.Use(i % 2 == 0 ? "RockLight" : "RockMid");
                float a = (360f / stones) * i + b.Rand(-7f, 7f);
                Vector3 p = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * radius * 0.86f;
                b.Rock(p, new Vector3(radius * 0.42f, radius * 0.34f, radius * 0.38f) * b.Rand(0.82f, 1.15f), 0.22f, 2, 6);
            }

            // ---- Log teepee ---------------------------------------------------
            b.Use("WoodLog");
            const int logs = 5;
            for (int i = 0; i < logs; i++)
            {
                float a = (360f / logs) * i + 18f;
                Vector3 outer = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * radius * 0.55f;
                Vector3 apex = new Vector3(outer.x * 0.14f, radius * 0.78f, outer.z * 0.14f);
                b.TaperedSegment(outer + new Vector3(0f, 0.03f, 0f), apex, radius * 0.085f, radius * 0.06f, 5);
            }

            // Charred lower halves sell the "burning" read.
            b.Use("Charcoal");
            for (int i = 0; i < logs; i++)
            {
                float a = (360f / logs) * i + 18f;
                Vector3 outer = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * radius * 0.55f;
                Vector3 apex = new Vector3(outer.x * 0.14f, radius * 0.78f, outer.z * 0.14f);
                b.TaperedSegment(Vector3.Lerp(outer, apex, 0.35f), Vector3.Lerp(outer, apex, 0.72f),
                                 radius * 0.078f, radius * 0.066f, 5);
            }

            // ---- Flame: stacked twisted cones. These use the HDR-emissive palette
            //      entries (intensity 2-3) so they clear the Global Volume Bloom
            //      threshold of 1.0 without the threshold being lowered globally.
            b.Use("Ember");
            b.Prism(new Vector3(0f, 0.04f, 0f), radius * 0.42f, radius * 0.26f, radius * 0.5f, 6, 22f);

            b.Use("FireCore");
            b.Prism(new Vector3(0f, radius * 0.5f, 0f), radius * 0.26f, radius * 0.13f, radius * 0.42f, 6, -30f);
            b.Prism(new Vector3(0f, radius * 0.9f, 0f), radius * 0.13f, 0f, radius * 0.34f, 5, 40f);

            return b;
        }
    }
}

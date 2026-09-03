using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// The three unit types, authored to the capsule dimensions the gameplay prefabs use
    /// (Worker 0.4 x 1.2, Warrior 0.5 x 1.4, Enemy 0.45 x 1.4).
    ///
    /// Deliberately template-simple: one tapered body block, a head, and a single
    /// identifying accessory per unit. No limbs, straps or props - silhouette and
    /// colour do all the work from RTS camera height:
    ///   Worker  - cream body, conical straw hat
    ///   Warrior - blue body, steel helmet with red crest, round shield
    ///   Enemy   - dark purple body, red band, swept horns
    /// </summary>
    public static partial class LowPolyShapes
    {
        static partial void AddUnitsImpl(List<AssetDef> list)
        {
            list.Add(new AssetDef("Worker", AssetCategory.Units, Worker, "0.40 wide, 1.2 tall"));
            list.Add(new AssetDef("Warrior", AssetCategory.Units, Warrior, "0.50 wide, 1.4 tall"));
            list.Add(new AssetDef("Enemy", AssetCategory.Units, Enemy, "0.45 wide, 1.4 tall"));
            list.Add(new AssetDef("Castaway", AssetCategory.Units, Castaway, "0.40 wide, 1.2 tall"));
        }

        // ==================================================================
        // Worker
        // ==================================================================

        private static MeshBuilder Worker()
        {
            MeshBuilder b = new MeshBuilder(3101);

            // Body: one tapered block, meeple-style.
            b.Use("ClothCream");
            b.Frustum(Vector3.zero, new Vector2(0.30f, 0.24f), new Vector2(0.22f, 0.18f), 0.78f);

            // Head.
            b.Use("SkinTan");
            b.Frustum(new Vector3(0f, 0.78f, 0f), new Vector2(0.17f, 0.17f), new Vector2(0.15f, 0.15f), 0.20f);

            // Conical straw hat: the worker silhouette from directly above.
            b.Use("ThatchLight");
            b.Prism(new Vector3(0f, 0.96f, 0f), 0.20f, 0f, 0.20f, 6);

            return b;
        }

        // ==================================================================
        // Castaway — the player's own character (2026-09-02)
        // ==================================================================

        /// <summary>
        /// Worker body and head, but a red bandana instead of the straw hat and a
        /// blue sash at the waist: from directly above the player reads as a red
        /// dot among cream cones, never as one more colonist.
        /// </summary>
        private static MeshBuilder Castaway()
        {
            MeshBuilder b = new MeshBuilder(3104);

            b.Use("ClothCream");
            b.Frustum(Vector3.zero, new Vector2(0.30f, 0.24f), new Vector2(0.22f, 0.18f), 0.78f);

            // Sash — a second colour read at the body's mid-height
            b.Use("ClothBlue");
            b.Box(new Vector3(0f, 0.48f, 0f), new Vector3(0.28f, 0.06f, 0.23f));

            b.Use("SkinTan");
            b.Frustum(new Vector3(0f, 0.78f, 0f), new Vector2(0.17f, 0.17f), new Vector2(0.15f, 0.15f), 0.20f);

            // Bandana: wraps the top of the head, knotted at the back
            b.Use("ClothRed");
            b.Frustum(new Vector3(0f, 0.90f, 0f), new Vector2(0.19f, 0.19f), new Vector2(0.15f, 0.15f), 0.09f);
            b.Box(new Vector3(0f, 0.92f, -0.11f), new Vector3(0.07f, 0.05f, 0.08f));

            return b;
        }

        // ==================================================================
        // Warrior
        // ==================================================================

        private static MeshBuilder Warrior()
        {
            MeshBuilder b = new MeshBuilder(3201);

            // Body: broadens into the shoulders.
            b.Use("ClothBlue");
            b.Frustum(Vector3.zero, new Vector2(0.28f, 0.22f), new Vector2(0.36f, 0.24f), 0.92f);

            // Head.
            b.Use("SkinTan");
            b.Frustum(new Vector3(0f, 0.92f, 0f), new Vector2(0.18f, 0.18f), new Vector2(0.16f, 0.16f), 0.20f);

            // Helmet with a red crest line: the warrior read from above.
            b.Use("MetalSteel");
            b.Frustum(new Vector3(0f, 1.10f, 0f), new Vector2(0.21f, 0.21f), new Vector2(0.14f, 0.14f), 0.16f);
            b.Use("ClothRed");
            b.Box(new Vector3(0f, 1.30f, 0f), new Vector3(0.05f, 0.10f, 0.24f));

            // Round shield on the left side.
            b.Use("WoodPlank");
            b.Push();
            b.Translate(-0.23f, 0.52f, 0f);
            b.Rotate(0f, 0f, 90f);
            b.Prism(Vector3.zero, 0.17f, 0.17f, 0.06f, 8);
            b.Pop();

            return b;
        }

        // ==================================================================
        // Enemy
        // ==================================================================

        private static MeshBuilder Enemy()
        {
            MeshBuilder b = new MeshBuilder(3301);

            // Body.
            b.Use("EnemyCloth");
            b.Frustum(Vector3.zero, new Vector2(0.28f, 0.22f), new Vector2(0.34f, 0.24f), 0.88f);

            // Red band: the one saturated accent, so enemies pop against terrain.
            b.Use("EnemyAccent");
            b.Box(new Vector3(0f, 0.62f, 0f), new Vector3(0.37f, 0.10f, 0.27f));

            // Head.
            b.Use("EnemySkin");
            b.Frustum(new Vector3(0f, 0.88f, 0f), new Vector2(0.18f, 0.18f), new Vector2(0.16f, 0.16f), 0.22f);

            // Horns, swept back and out: the enemy read from above.
            b.Use("MetalDark");
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 root = new Vector3(s * 0.08f, 1.08f, 0.01f);
                Vector3 tip = root + new Vector3(s * 0.13f, 0.20f, -0.11f);
                b.TaperedSegment(root, tip, 0.045f, 0f, 4, 45f);
            }

            return b;
        }
    }
}

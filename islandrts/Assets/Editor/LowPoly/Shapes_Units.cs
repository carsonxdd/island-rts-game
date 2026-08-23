using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// The three unit types, authored to the capsule dimensions the current prefabs use
    /// (Worker 0.4 x 1.2, Warrior 0.5 x 1.4, Enemy 0.45 x 1.4).
    ///
    /// The design priority is silhouette separation from RTS camera height, where faces
    /// and detail are invisible. Each unit gets one distinctive top-down shape:
    ///   Worker  - wide conical straw hat, hunched, carries a tool
    ///   Warrior - square steel pauldrons and a round shield, upright, crested helmet
    ///   Enemy   - jagged shoulder spikes and swept horns, forward hunch, long arms
    /// Colour reinforces it: workers cream, warriors blue/steel, enemies desaturated
    /// purple with a red accent.
    /// </summary>
    public static partial class LowPolyShapes
    {
        static partial void AddUnitsImpl(List<AssetDef> list)
        {
            list.Add(new AssetDef("Worker", AssetCategory.Units, Worker, "0.40 wide, 1.2 tall"));
            list.Add(new AssetDef("Warrior", AssetCategory.Units, Warrior, "0.50 wide, 1.4 tall (spear to ~1.55)"));
            list.Add(new AssetDef("Enemy", AssetCategory.Units, Enemy, "0.45 wide, 1.4 tall"));
        }

        /// <summary>Tapered square-section limb, used for arms and legs on every unit.</summary>
        private static void Limb(MeshBuilder b, Vector3 from, Vector3 to, float rFrom, float rTo)
        {
            b.TaperedSegment(from, to, rFrom, rTo, 4, 45f);
        }

        // ==================================================================
        // Worker
        // ==================================================================

        private static MeshBuilder Worker()
        {
            MeshBuilder b = new MeshBuilder(3101);
            const float H = 1.2f;

            float hipY = H * 0.34f;
            float shoulderY = H * 0.70f;

            // ---- Legs and feet -------------------------------------------------
            b.Use("LeatherBrown");
            for (int s = -1; s <= 1; s += 2)
            {
                Limb(b, new Vector3(s * 0.075f, 0.05f, 0f), new Vector3(s * 0.065f, hipY, 0f), 0.055f, 0.062f);
            }
            b.Use("WoodDark");
            for (int s = -1; s <= 1; s += 2)
            {
                b.BoxOnGround(new Vector3(s * 0.075f, 0f, 0.015f), new Vector3(0.10f, 0.055f, 0.15f));
            }

            // ---- Torso: leans forward, which is the whole worker read ------------
            b.Use("ClothCream");
            b.Push();
            b.Translate(0f, hipY, 0f);
            b.Rotate(9f, 0f, 0f);
            b.Frustum(Vector3.zero, new Vector2(0.21f, 0.15f), new Vector2(0.25f, 0.17f), shoulderY - hipY);
            b.Pop();

            b.Use("LeatherBrown");
            b.Box(new Vector3(0f, hipY + 0.03f, 0f), new Vector3(0.23f, 0.05f, 0.17f));

            // ---- Backpack ---------------------------------------------------------
            b.Use("WoodPale");
            b.Box(new Vector3(0f, shoulderY - 0.13f, -0.145f), new Vector3(0.20f, 0.22f, 0.11f));
            b.Use("LeatherBrown");
            b.Box(new Vector3(0f, shoulderY - 0.02f, -0.145f), new Vector3(0.21f, 0.05f, 0.12f));

            // ---- Arms: forward and down, as if carrying ---------------------------
            b.Use("SkinTan");
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 shoulder = new Vector3(s * 0.125f, shoulderY - 0.03f, 0.01f);
                Vector3 hand = new Vector3(s * 0.115f, hipY + 0.06f, 0.115f);
                Limb(b, shoulder, hand, 0.042f, 0.034f);
            }

            // ---- Head ------------------------------------------------------------
            b.Use("SkinTan");
            b.Frustum(new Vector3(0f, shoulderY, 0.012f), new Vector2(0.115f, 0.115f), new Vector2(0.125f, 0.125f), 0.15f);

            // ---- Conical straw hat: the worker silhouette from directly above -------
            b.Use("ThatchLight");
            b.Prism(new Vector3(0f, shoulderY + 0.135f, 0.012f), 0.20f, 0.055f, 0.13f, 8);
            b.Use("ThatchDark");
            b.Prism(new Vector3(0f, shoulderY + 0.125f, 0.012f), 0.205f, 0.20f, 0.018f, 8);

            // ---- Pickaxe in the right hand ----------------------------------------
            b.Use("WoodDark");
            Vector3 gripLow = new Vector3(0.155f, hipY - 0.04f, 0.10f);
            Vector3 gripHigh = new Vector3(0.20f, shoulderY + 0.10f, 0.05f);
            b.TaperedSegment(gripLow, gripHigh, 0.019f, 0.017f, 5);

            b.Use("MetalDark");
            b.Push();
            b.Translate(gripHigh + new Vector3(0f, -0.02f, 0f));
            b.RotateY(12f);
            b.Box(Vector3.zero, new Vector3(0.05f, 0.05f, 0.20f));
            b.Pop();

            return b;
        }

        // ==================================================================
        // Warrior
        // ==================================================================

        private static MeshBuilder Warrior()
        {
            MeshBuilder b = new MeshBuilder(3201);
            const float H = 1.4f;

            float hipY = H * 0.33f;
            float shoulderY = H * 0.68f;

            // ---- Legs and boots ---------------------------------------------------
            b.Use("LeatherBrown");
            for (int s = -1; s <= 1; s += 2)
            {
                Limb(b, new Vector3(s * 0.09f, 0.07f, 0f), new Vector3(s * 0.08f, hipY, 0f), 0.065f, 0.072f);
            }
            b.Use("WoodDark");
            for (int s = -1; s <= 1; s += 2)
            {
                b.BoxOnGround(new Vector3(s * 0.09f, 0f, 0.02f), new Vector3(0.12f, 0.08f, 0.18f));
            }

            // ---- Torso: upright, broadening hard into the shoulders -----------------
            b.Use("ClothBlue");
            b.Frustum(new Vector3(0f, hipY, 0f), new Vector2(0.24f, 0.17f), new Vector2(0.33f, 0.20f), shoulderY - hipY);

            b.Use("LeatherBrown");
            b.Box(new Vector3(0f, hipY + 0.035f, 0f), new Vector3(0.26f, 0.06f, 0.19f));
            // Chest strap.
            b.Push();
            b.Translate(0f, (hipY + shoulderY) * 0.5f, -0.10f);
            b.Rotate(0f, 0f, 26f);
            b.Box(Vector3.zero, new Vector3(0.07f, 0.34f, 0.13f));
            b.Pop();

            // ---- Square steel pauldrons: the warrior top-down read -------------------
            b.Use("MetalSteel");
            for (int s = -1; s <= 1; s += 2)
            {
                b.Push();
                b.Translate(s * 0.20f, shoulderY - 0.035f, 0f);
                b.Rotate(0f, 0f, s * -16f);
                b.Frustum(new Vector3(0f, -0.06f, 0f), new Vector2(0.15f, 0.20f), new Vector2(0.11f, 0.16f), 0.11f);
                b.Pop();
            }

            // ---- Arms --------------------------------------------------------------
            b.Use("SkinTan");
            Vector3 rShoulder = new Vector3(0.185f, shoulderY - 0.05f, 0f);
            Vector3 rHand = new Vector3(0.20f, hipY + 0.13f, 0.09f);
            Limb(b, rShoulder, rHand, 0.05f, 0.04f);

            Vector3 lShoulder = new Vector3(-0.185f, shoulderY - 0.05f, 0f);
            Vector3 lHand = new Vector3(-0.20f, hipY + 0.15f, 0.10f);
            Limb(b, lShoulder, lHand, 0.05f, 0.04f);

            // ---- Head and crested helmet --------------------------------------------
            b.Use("SkinTan");
            b.Frustum(new Vector3(0f, shoulderY, 0f), new Vector2(0.13f, 0.13f), new Vector2(0.14f, 0.14f), 0.16f);

            b.Use("MetalSteel");
            b.Frustum(new Vector3(0f, shoulderY + 0.07f, 0f), new Vector2(0.165f, 0.165f), new Vector2(0.12f, 0.12f), 0.13f);
            // Brow guard.
            b.Box(new Vector3(0f, shoulderY + 0.075f, 0.085f), new Vector3(0.17f, 0.045f, 0.03f));

            b.Use("ClothRed");
            // Crest fin runs front-to-back so it reads as a line from directly above.
            b.Push();
            b.Translate(0f, shoulderY + 0.20f, 0f);
            b.Tri(new Vector3(0f, 0f, -0.09f), new Vector3(0f, 0.10f, 0f), new Vector3(0f, 0f, 0.09f));
            b.Tri(new Vector3(0f, 0f, 0.09f), new Vector3(0f, 0.10f, 0f), new Vector3(0f, 0f, -0.09f));
            b.Box(new Vector3(0f, 0.015f, 0f), new Vector3(0.035f, 0.03f, 0.18f));
            b.Pop();

            // ---- Round shield on the left arm ----------------------------------------
            b.Use("WoodPlank");
            b.Push();
            b.Translate(lHand + new Vector3(-0.045f, 0.04f, 0.02f));
            b.Rotate(0f, 0f, 90f);
            b.Prism(Vector3.zero, 0.20f, 0.20f, 0.045f, 8);
            b.Use("MetalSteel");
            b.Prism(new Vector3(0f, 0.045f, 0f), 0.06f, 0.035f, 0.035f, 6);
            b.Pop();

            // ---- Spear in the right hand ----------------------------------------------
            b.Use("WoodDark");
            Vector3 spearLow = new Vector3(0.235f, 0.02f, 0.02f);
            Vector3 spearHigh = new Vector3(0.175f, 1.42f, 0.14f);
            b.TaperedSegment(spearLow, spearHigh, 0.021f, 0.019f, 5);

            b.Use("MetalSteel");
            Vector3 tipBase = spearHigh;
            Vector3 tipEnd = spearHigh + (spearHigh - spearLow).normalized * 0.16f;
            b.TaperedSegment(tipBase, Vector3.Lerp(tipBase, tipEnd, 0.35f), 0.021f, 0.045f, 4, 45f);
            b.TaperedSegment(Vector3.Lerp(tipBase, tipEnd, 0.35f), tipEnd, 0.045f, 0f, 4, 45f);

            return b;
        }

        // ==================================================================
        // Enemy
        // ==================================================================

        private static MeshBuilder Enemy()
        {
            MeshBuilder b = new MeshBuilder(3301);
            const float H = 1.4f;

            float hipY = H * 0.30f;
            float shoulderY = H * 0.62f;

            // ---- Legs: wider stance than the other two -----------------------------
            b.Use("EnemyCloth");
            for (int s = -1; s <= 1; s += 2)
            {
                Limb(b, new Vector3(s * 0.105f, 0.06f, 0f), new Vector3(s * 0.085f, hipY, -0.01f), 0.06f, 0.068f);
            }
            b.Use("Charcoal");
            for (int s = -1; s <= 1; s += 2)
            {
                b.BoxOnGround(new Vector3(s * 0.105f, 0f, 0.02f), new Vector3(0.115f, 0.07f, 0.17f));
            }

            // ---- Torso: pronounced forward hunch -------------------------------------
            b.Use("EnemyCloth");
            b.Push();
            b.Translate(0f, hipY, 0f);
            b.Rotate(17f, 0f, 0f);
            b.Frustum(Vector3.zero, new Vector2(0.23f, 0.17f), new Vector2(0.31f, 0.21f), shoulderY - hipY);
            b.Pop();

            // Red sash: the one saturated accent, so enemies pop against terrain.
            b.Use("EnemyAccent");
            b.Push();
            b.Translate(0f, (hipY + shoulderY) * 0.5f + 0.02f, 0.02f);
            b.Rotate(14f, 0f, -32f);
            b.Box(Vector3.zero, new Vector3(0.085f, 0.36f, 0.20f));
            b.Pop();

            // ---- Shoulder spikes: three per side, swept back ---------------------------
            b.Use("MetalDark");
            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 root = new Vector3(s * (0.13f + i * 0.045f), shoulderY - 0.02f - i * 0.035f, -0.02f - i * 0.03f);
                    Vector3 tip = root + new Vector3(s * 0.05f, 0.10f - i * 0.012f, -0.075f);
                    b.TaperedSegment(root, tip, 0.032f - i * 0.005f, 0f, 4, 45f);
                }
            }

            // ---- Arms: long and low, knuckles near the knees ---------------------------
            b.Use("EnemySkin");
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 shoulder = new Vector3(s * 0.155f, shoulderY - 0.05f, 0f);
                Vector3 hand = new Vector3(s * 0.195f, hipY - 0.10f, 0.10f);
                Limb(b, shoulder, hand, 0.05f, 0.038f);
            }

            // ---- Head: pushed forward off the hunched shoulders ------------------------
            b.Use("EnemySkin");
            b.Push();
            b.Translate(0f, shoulderY - 0.01f, 0.055f);
            b.Rotate(12f, 0f, 0f);
            b.Frustum(Vector3.zero, new Vector2(0.145f, 0.15f), new Vector2(0.125f, 0.13f), 0.17f);
            b.Pop();

            // Jaw shadow, so the head does not read as a plain block.
            b.Use("Charcoal");
            b.Box(new Vector3(0f, shoulderY + 0.045f, 0.135f), new Vector3(0.125f, 0.05f, 0.045f));

            // ---- Horns: swept back and out ---------------------------------------------
            b.Use("MetalDark");
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 root = new Vector3(s * 0.065f, shoulderY + 0.13f, 0.045f);
                Vector3 mid = root + new Vector3(s * 0.075f, 0.055f, -0.06f);
                Vector3 tip = mid + new Vector3(s * 0.035f, 0.02f, -0.09f);
                b.TaperedSegment(root, mid, 0.032f, 0.022f, 4, 45f);
                b.TaperedSegment(mid, tip, 0.022f, 0f, 4, 45f);
            }

            // ---- Crude cleaver in the right hand ------------------------------------------
            b.Use("WoodDark");
            Vector3 gripLow = new Vector3(0.205f, hipY - 0.14f, 0.09f);
            Vector3 gripHigh = new Vector3(0.215f, hipY + 0.10f, 0.13f);
            b.TaperedSegment(gripLow, gripHigh, 0.022f, 0.020f, 5);

            b.Use("MetalDark");
            b.Push();
            b.Translate(gripHigh + new Vector3(0f, 0.11f, 0.015f));
            b.Rotate(0f, 0f, -7f);
            // Wedge blade: wide at the tip, which reads as "crude" rather than "forged".
            b.Frustum(new Vector3(0f, -0.10f, 0f), new Vector2(0.03f, 0.075f), new Vector2(0.035f, 0.16f), 0.24f);
            b.Pop();

            return b;
        }
    }
}

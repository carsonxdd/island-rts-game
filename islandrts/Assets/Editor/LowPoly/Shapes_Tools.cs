using System.Collections.Generic;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Hand tools the player's character crafts at the campfire and holds
    /// (2026-09-02). Authored grip-down: pivot at the bottom of the handle, the
    /// tool pointing +Y, so a HandSocket that tilts them a little reads as
    /// "carried". Deliberately chunky — they are seen from RTS camera height
    /// beside a 1.2 m meeple, so a head of 0.16 m is the minimum that registers.
    /// </summary>
    public static partial class LowPolyShapes
    {
        static partial void AddToolsImpl(List<AssetDef> list)
        {
            list.Add(new AssetDef("StoneAxe", AssetCategory.Tools, StoneAxe, "0.55 tall"));
            list.Add(new AssetDef("StonePick", AssetCategory.Tools, StonePick, "0.60 tall"));
            list.Add(new AssetDef("FishingSpear", AssetCategory.Tools, FishingSpear, "1.05 tall"));
            list.Add(new AssetDef("Mallet", AssetCategory.Tools, Mallet, "0.50 tall"));
            list.Add(new AssetDef("WoodenSpear", AssetCategory.Tools, WoodenSpear, "1.15 tall"));
            list.Add(new AssetDef("MetalPick", AssetCategory.Tools, MetalPick, "0.60 tall"));
        }

        private static void Handle(MeshBuilder b, float height, float radius)
        {
            b.Use("WoodLog");
            b.Prism(Vector3.zero, radius, radius * 0.85f, height, 6);
        }

        private static void Binding(MeshBuilder b, float y, float size)
        {
            b.Use("ClothRed");
            b.Box(new Vector3(0f, y, 0f), new Vector3(size, 0.05f, size));
        }

        private static MeshBuilder StoneAxe()
        {
            MeshBuilder b = new MeshBuilder(4101);
            Handle(b, 0.55f, 0.028f);
            b.Use("RockDark");
            b.Box(new Vector3(0.07f, 0.47f, 0f), new Vector3(0.17f, 0.11f, 0.05f));
            Binding(b, 0.44f, 0.08f);
            return b;
        }

        private static MeshBuilder StonePick()
        {
            MeshBuilder b = new MeshBuilder(4102);
            Handle(b, 0.60f, 0.028f);
            b.Use("RockDark");
            b.Box(new Vector3(0f, 0.54f, 0f), new Vector3(0.32f, 0.06f, 0.05f));
            Binding(b, 0.49f, 0.08f);
            return b;
        }

        private static MeshBuilder FishingSpear()
        {
            MeshBuilder b = new MeshBuilder(4103);
            Handle(b, 0.90f, 0.022f);
            b.Use("RockDark");
            b.TaperedSegment(new Vector3(0f, 0.90f, 0f), new Vector3(0f, 1.05f, 0f), 0.035f, 0.004f, 6);
            // Two barbs so it reads as a fishing spear, not a stick
            b.Box(new Vector3(0.035f, 0.92f, 0f), new Vector3(0.05f, 0.02f, 0.02f));
            b.Box(new Vector3(-0.035f, 0.92f, 0f), new Vector3(0.05f, 0.02f, 0.02f));
            Binding(b, 0.86f, 0.06f);
            return b;
        }

        private static MeshBuilder Mallet()
        {
            MeshBuilder b = new MeshBuilder(4104);
            Handle(b, 0.45f, 0.026f);
            b.Use("WoodDark");
            b.Box(new Vector3(0f, 0.44f, 0f), new Vector3(0.11f, 0.11f, 0.24f));
            return b;
        }

        private static MeshBuilder WoodenSpear()
        {
            MeshBuilder b = new MeshBuilder(4105);
            Handle(b, 1.00f, 0.022f);
            b.Use("WoodPale");
            b.TaperedSegment(new Vector3(0f, 1.00f, 0f), new Vector3(0f, 1.15f, 0f), 0.028f, 0.003f, 6);
            Binding(b, 0.96f, 0.06f);
            return b;
        }

        private static MeshBuilder MetalPick()
        {
            MeshBuilder b = new MeshBuilder(4106);
            Handle(b, 0.60f, 0.028f);
            b.Use("MetalSteel");
            b.Box(new Vector3(0f, 0.54f, 0f), new Vector3(0.34f, 0.055f, 0.045f));
            b.Use("MetalDark");
            Binding(b, 0.49f, 0.08f);
            return b;
        }
    }
}

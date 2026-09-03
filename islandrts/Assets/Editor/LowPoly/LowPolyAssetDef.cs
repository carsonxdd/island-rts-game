using System;
using System.Collections.Generic;

namespace IslandRTS.ArtGen
{
    public enum AssetCategory
    {
        Environment,
        Buildings,
        Units,
        Resources,
        Tools
    }

    /// <summary>
    /// One generated asset: a name, the category it belongs to, and the function that
    /// builds its geometry. The generator turns each of these into a .asset mesh, a set
    /// of material assignments, and a .prefab.
    /// </summary>
    public class AssetDef
    {
        public readonly string Name;
        public readonly AssetCategory Category;
        public readonly Func<MeshBuilder> Build;

        /// <summary>Footprint/height note shown in the generator summary, for scale sanity-checking.</summary>
        public readonly string SizeNote;

        public AssetDef(string name, AssetCategory category, Func<MeshBuilder> build, string sizeNote)
        {
            Name = name;
            Category = category;
            Build = build;
            SizeNote = sizeNote;
        }
    }

    /// <summary>
    /// Registry of every template asset. Split across Shapes_*.cs partials by category.
    ///
    /// Authoring convention for all shapes: real world units, pivot at the base of the
    /// asset (y = 0), facing +Z. That means the generated prefabs sit at scale 1 rather
    /// than inheriting the current "unit primitive squashed by an odd scale" setup on
    /// the existing gameplay prefabs.
    /// </summary>
    public static partial class LowPolyShapes
    {
        public static List<AssetDef> All()
        {
            List<AssetDef> list = new List<AssetDef>();
            AddEnvironment(list);
            AddBuildings(list);
            AddUnits(list);
            AddResources(list);
            AddTools(list);
            return list;
        }

        static partial void AddEnvironmentImpl(List<AssetDef> list);
        static partial void AddBuildingsImpl(List<AssetDef> list);
        static partial void AddUnitsImpl(List<AssetDef> list);
        static partial void AddResourcesImpl(List<AssetDef> list);
        static partial void AddToolsImpl(List<AssetDef> list);

        private static void AddEnvironment(List<AssetDef> list) { AddEnvironmentImpl(list); }
        private static void AddBuildings(List<AssetDef> list) { AddBuildingsImpl(list); }
        private static void AddUnits(List<AssetDef> list) { AddUnitsImpl(list); }
        private static void AddResources(List<AssetDef> list) { AddResourcesImpl(list); }
        private static void AddTools(List<AssetDef> list) { AddToolsImpl(list); }
    }
}

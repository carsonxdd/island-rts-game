using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Builds the <see cref="ScatterSettings"/> asset that <see cref="PropScatter"/>
    /// reads at runtime, from a code table of terrain rules.
    ///
    /// Environment props used to be scattered into the scene at edit time from
    /// a fixed seed. With a random island per run that is impossible — the
    /// props have to be placed after the island exists — so the editor's job
    /// shrank to resolving prefab references into an asset the runtime can
    /// load. Counts and bands are then tunable on the asset without touching
    /// code; re-running this menu item rewrites the asset from the table
    /// below (it is the source of truth, so put lasting tuning here).
    ///
    /// Rules are terrain-based (height band, slope band, grass tone): palms
    /// and flotsam hug the shore by HEIGHT, ferns prefer dark grass by TONE,
    /// rocks lean toward slopes and cliff feet by SLOPE. Nothing is radial —
    /// the island is a different shape every run.
    /// </summary>
    public static class LowPolyScatter
    {
        private const string MenuRoot = "Tools/Island RTS/Low-Poly Templates/";
        private const string LegacyScatterRootName = "_LowPolyScatter";
        private const string ArtPrefabRoot = "Assets/Art/Prefabs/Environment/";
        public const string SettingsAssetPath = "Assets/Settings/ScatterSettings.asset";

        private struct Def
        {
            public string Prefab;
            public int Count;
            public float MinH, MaxH;
            public float MinSlope, MaxSlope;
            public float MinTone, MaxTone;
            public float Spacing;
            public float MinScale, MaxScale;
            /// <summary>Non-zero makes this a harvestable wood node rather than decor, holding this much wood.</summary>
            public int Wood;

            public Def(string prefab, int count, float minH, float maxH, float minSlope, float maxSlope,
                       float minTone, float maxTone, float spacing, float minScale, float maxScale, int wood = 0)
            {
                Prefab = prefab; Count = count; MinH = minH; MaxH = maxH; MinSlope = minSlope; MaxSlope = maxSlope;
                MinTone = minTone; MaxTone = maxTone; Spacing = spacing; MinScale = minScale; MaxScale = maxScale;
                Wood = wood;
            }
        }

        // Height reference: wading band −0.4..0, wet sand to ~0.2, beach to ~0.65,
        // grass above, plateau tops up to ~6.5. Counts are dressing, not a forest —
        // ResourceSpawner drops ~440 gameplay nodes on top of this.
        private static readonly Def[] Table =
        {
            //          prefab                 count  minH   maxH   minSl  maxSl  minT  maxT  spacing minS  maxS
            // Deliberately sparse (halved 2026-09-01): decor should frame the
            // gameplay nodes, not compete with them. Counts are for the 150 m
            // map; PropScatter scales them with map area.
            // Palms are GATHERABLE (2026-09-02): every tree on the island is a tree a
            // worker can chop, so the shore is a wood source and not just scenery. The
            // trailing number is how much wood each holds (Tree.prefab carries 50).
            new Def("Palm_Tall.prefab",         26,   0.20f, 1.60f, 0f,    0.50f, 0f,   1f,   5.0f,  0.85f, 1.20f, 40),
            new Def("Palm_Bent.prefab",         16,   0.15f, 1.20f, 0f,    0.50f, 0f,   1f,   5.0f,  0.85f, 1.20f, 35),
            new Def("Palm_Young.prefab",        18,   0.30f, 2.50f, 0f,    0.55f, 0f,   1f,   4.0f,  0.90f, 1.15f, 20),

            // Rocks: one rule for cliff feet and slopes, one for open ground
            new Def("Rock_Large.prefab",         9,   0.60f, 7.50f, 0.30f, 1.50f, 0f,   1f,   6.0f,  0.90f, 1.30f),
            new Def("Rock_Large.prefab",         4,   0.60f, 7.50f, 0f,    0.30f, 0f,   1f,   8.0f,  0.90f, 1.30f),
            new Def("Rock_Medium.prefab",       16,   0.50f, 7.50f, 0.15f, 1.50f, 0f,   1f,   4.5f,  0.85f, 1.25f),
            new Def("Rock_Small.prefab",        26,   0.50f, 7.50f, 0f,    1.50f, 0f,   1f,   3.5f,  0.80f, 1.30f),

            new Def("Bush_Round.prefab",        24,   0.70f, 7.00f, 0f,    0.60f, 0f,   1f,   3.5f,  0.85f, 1.25f),
            new Def("Bush_Wide.prefab",         16,   0.70f, 7.00f, 0f,    0.60f, 0f,   1f,   3.5f,  0.85f, 1.25f),
            new Def("Fern.prefab",              30,   0.70f, 7.00f, 0f,    0.60f, 0f,   0.40f, 3.0f, 0.80f, 1.20f),
            new Def("GrassTuft.prefab",         90,   0.60f, 7.20f, 0f,    0.70f, 0f,   1f,   2.0f,  0.80f, 1.35f),

            // Flotsam: wading band + wet sand
            new Def("DriftwoodLog.prefab",      12,  -0.30f, 0.50f, 0f,    0.40f, 0f,   1f,   5.0f,  0.90f, 1.20f),
            new Def("Barrel.prefab",             5,  -0.20f, 0.50f, 0f,    0.40f, 0f,   1f,   4.0f,  0.95f, 1.10f),
            new Def("Crate.prefab",              6,  -0.20f, 0.50f, 0f,    0.40f, 0f,   1f,   4.0f,  0.95f, 1.10f),
        };

        // ==================================================================
        // Menu entries
        // ==================================================================

        [MenuItem(MenuRoot + "Build Scatter Settings", false, 80)]
        public static void BuildSettingsMenu()
        {
            ScatterSettings asset = EnsureSettingsAsset();
            if (asset != null) Selection.activeObject = asset;
        }

        /// <summary>
        /// Write (or rewrite) the ScatterSettings asset from the table.
        /// Called by the terrain setup so a fresh project gets one without a
        /// separate step. Returns null if the art library is missing.
        /// </summary>
        public static ScatterSettings EnsureSettingsAsset()
        {
            ScatterSettings asset = AssetDatabase.LoadAssetAtPath<ScatterSettings>(SettingsAssetPath);
            bool isNew = asset == null;
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<ScatterSettings>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsAssetPath));
                AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("[LowPoly] ScatterSettings " + (isNew ? "created" : "rewritten") + " at " + SettingsAssetPath);

            var rules = new System.Collections.Generic.List<ScatterSettings.Rule>();
            int missing = 0;
            for (int i = 0; i < Table.Length; i++)
            {
                Def d = Table[i];
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPrefabRoot + d.Prefab);
                if (prefab == null)
                {
                    missing++;
                    summary.AppendLine("    MISSING " + ArtPrefabRoot + d.Prefab + " — run 'Generate All Assets' first");
                    continue;
                }

                rules.Add(new ScatterSettings.Rule
                {
                    prefab = prefab,
                    count = d.Count,
                    minHeight = d.MinH, maxHeight = d.MaxH,
                    minSlope = d.MinSlope, maxSlope = d.MaxSlope,
                    minTone = d.MinTone, maxTone = d.MaxTone,
                    spacing = d.Spacing,
                    minScale = d.MinScale, maxScale = d.MaxScale,
                    gatherable = d.Wood > 0,
                    resourceType = ResourceNode.ResourceType.Wood,
                    resourceAmount = d.Wood > 0 ? d.Wood : 40,
                });
            }

            asset.rules = rules.ToArray();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            summary.AppendLine("    " + rules.Count + " rules, " + TotalCount(rules) + " props wanted per island");
            if (missing > 0)
            {
                Debug.LogError(summary.ToString());
                return rules.Count > 0 ? asset : null;
            }
            Debug.Log(summary.ToString());
            return asset;
        }

        /// <summary>
        /// Remove the pre-runtime-scatter scene decor (the old _LowPolyScatter
        /// root). Also called by the terrain setup.
        /// </summary>
        [MenuItem(MenuRoot + "Clear Legacy Scattered Props", false, 81)]
        public static void Clear()
        {
            GameObject scatterRoot = FindLegacyScatterRoot();
            if (scatterRoot == null)
            {
                Debug.Log("[LowPoly] Nothing to clear - no '" + LegacyScatterRootName + "' in the open scene.");
                return;
            }

            Undo.DestroyObjectImmediate(scatterRoot);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[LowPoly] Removed '" + LegacyScatterRootName + "' — props are scattered at runtime now (PropScatter).");
        }

        public static bool HasLegacyScatter() => FindLegacyScatterRoot() != null;

        // ==================================================================

        private static int TotalCount(System.Collections.Generic.List<ScatterSettings.Rule> rules)
        {
            int n = 0;
            for (int i = 0; i < rules.Count; i++) n += rules[i].count;
            return n;
        }

        private static GameObject FindLegacyScatterRoot()
        {
            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == LegacyScatterRootName) return roots[i];
            }
            return null;
        }
    }
}

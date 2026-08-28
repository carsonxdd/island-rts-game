using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Scatters the generated environment props into the open scene as set dressing.
    ///
    /// The 13 Environment assets (palms, rocks, bushes, ferns, grass, driftwood, barrel,
    /// crate) have no gameplay prefab to plumb into - they are pure decor, so "plumbing"
    /// them means placing them in MainIsland.unity. Doing that by hand across a 100x100
    /// island is tedious and unrepeatable, so this does it from a seed.
    ///
    /// Everything lands under one parent object, so a re-scatter is destroy-and-rebuild and
    /// the scene diff stays regenerable rather than hand-authored. Nothing here is a
    /// prefab-breaking change: the art prefabs carry no colliders, so scattered props never
    /// block pathing, never intercept clicks, and never affect the NavMesh bake.
    ///
    /// Placement rules:
    ///   - a downward raycast must hit ground, so props never land on water
    ///   - a clearing is kept around the campfire so the build area stays usable
    ///   - each prop type gets a radial band (palms and flotsam outward, ferns inward)
    ///   - a spacing grid stops props from interpenetrating
    /// </summary>
    public static class LowPolyScatter
    {
        private const string MenuRoot = "Tools/Island RTS/Low-Poly Templates/";
        private const string ScatterRootName = "_LowPolyScatter";
        private const string ArtPrefabRoot = "Assets/Art/Prefabs/Environment/";

        // ---- Tuning ------------------------------------------------------
        // Deliberately constants rather than a window: re-scattering is cheap, so tuning is
        // "edit a number, run it again" instead of a UI nobody maintains.

        /// <summary>Change this for a completely different layout with the same density.</summary>
        private const int Seed = 20260823;

        /// <summary>Half-extent of the playfield (150x150 world, island radius ~72).</summary>
        private const float IslandRadius = 70f;

        /// <summary>Nothing is placed inside this radius, so the campfire build area stays clear.</summary>
        private const float CampfireClearing = 13f;

        /// <summary>Give up on a prop after this many rejected candidates.</summary>
        private const int MaxTriesPerProp = 40;

        private class PropDef
        {
            public string Prefab;
            public int Count;
            /// <summary>Radial band from the island centre, in world units.</summary>
            public float MinRadius, MaxRadius;
            /// <summary>Minimum distance to any other scattered prop.</summary>
            public float Spacing;
            /// <summary>Uniform scale jitter range.</summary>
            public float MinScale, MaxScale;
        }

        // Palms and flotsam sit outward toward the shoreline; ferns, grass and bushes fill
        // the interior. Counts are deliberately moderate - ResourceSpawner already drops 200
        // trees, 100 bushes and 100 rocks at runtime, so this is dressing, not a forest.
        private static readonly PropDef[] Props =
        {
            new PropDef { Prefab = "Palm_Tall.prefab",     Count = 30, MinRadius = 30f, MaxRadius = 69f, Spacing = 4f,   MinScale = 0.85f, MaxScale = 1.2f },
            new PropDef { Prefab = "Palm_Bent.prefab",     Count = 20, MinRadius = 36f, MaxRadius = 69f, Spacing = 4f,   MinScale = 0.85f, MaxScale = 1.2f },
            new PropDef { Prefab = "Palm_Young.prefab",    Count = 24, MinRadius = 27f, MaxRadius = 69f, Spacing = 3f,   MinScale = 0.9f,  MaxScale = 1.15f },

            new PropDef { Prefab = "Rock_Large.prefab",    Count = 14, MinRadius = 21f, MaxRadius = 69f, Spacing = 5f,   MinScale = 0.9f,  MaxScale = 1.3f },
            new PropDef { Prefab = "Rock_Medium.prefab",   Count = 24, MinRadius = 18f, MaxRadius = 69f, Spacing = 3.5f, MinScale = 0.85f, MaxScale = 1.25f },
            new PropDef { Prefab = "Rock_Small.prefab",    Count = 40, MinRadius = 18f, MaxRadius = 69f, Spacing = 2.5f, MinScale = 0.8f,  MaxScale = 1.3f },

            new PropDef { Prefab = "Bush_Round.prefab",    Count = 44, MinRadius = 19f, MaxRadius = 67f, Spacing = 2.5f, MinScale = 0.85f, MaxScale = 1.25f },
            new PropDef { Prefab = "Bush_Wide.prefab",     Count = 30, MinRadius = 19f, MaxRadius = 67f, Spacing = 2.5f, MinScale = 0.85f, MaxScale = 1.25f },
            new PropDef { Prefab = "Fern.prefab",          Count = 50, MinRadius = 18f, MaxRadius = 66f, Spacing = 2f,   MinScale = 0.8f,  MaxScale = 1.2f },
            new PropDef { Prefab = "GrassTuft.prefab",     Count = 120, MinRadius = 18f, MaxRadius = 69f, Spacing = 1.5f, MinScale = 0.8f,  MaxScale = 1.35f },

            new PropDef { Prefab = "DriftwoodLog.prefab",  Count = 16, MinRadius = 50f, MaxRadius = 69f, Spacing = 4f,   MinScale = 0.9f,  MaxScale = 1.2f },
            new PropDef { Prefab = "Barrel.prefab",        Count = 8,  MinRadius = 48f, MaxRadius = 69f, Spacing = 3f,   MinScale = 0.95f, MaxScale = 1.1f },
            new PropDef { Prefab = "Crate.prefab",         Count = 10, MinRadius = 48f, MaxRadius = 69f, Spacing = 3f,   MinScale = 0.95f, MaxScale = 1.1f },
        };

        // ==================================================================
        // Menu entries
        // ==================================================================

        [MenuItem(MenuRoot + "Scatter Environment Props", false, 80)]
        public static void Scatter()
        {
            // This decorates whatever scene is active, and every other setup
            // tool guards on MainIsland — without this, running it with the
            // menu scene open drops 400 props into the title screen.
            if (!EnsureGameSceneOpen()) return;

            GameObject scatterRoot = FindScatterRoot();
            if (scatterRoot != null) Object.DestroyImmediate(scatterRoot);

            scatterRoot = new GameObject(ScatterRootName);
            Undo.RegisterCreatedObjectUndo(scatterRoot, "Scatter Environment Props");

            Vector3 centre = FindIslandCentre();

            // Terrain-aware grounding: generate the same heightfield the game builds at
            // runtime. (Terrain chunk colliders only exist in Play mode, so the old
            // physics raycast found nothing once the Ground plane was deleted.) Seed
            // comes from the scene's TerrainGrid when present.
            TerrainGrid sceneGrid = Object.FindAnyObjectByType<TerrainGrid>();
            float[,] heights = IslandGenerator.Generate(
                TerrainGrid.VertsPerSide, TerrainGrid.Spacing,
                sceneGrid != null ? sceneGrid.seed : 20260825);

            // Deterministic layout: same seed in, same island out. Save and restore Unity's
            // global random state so running this does not perturb anything else.
            Random.State previousState = Random.state;
            Random.InitState(Seed);

            List<Vector3> placed = new List<Vector3>();
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("[LowPoly] Environment scatter complete (seed " + Seed + ").");
            summary.AppendLine("  PROP                 PLACED / WANTED");

            int total = 0;
            int skipped = 0;

            try
            {
                for (int i = 0; i < Props.Length; i++)
                {
                    PropDef def = Props[i];

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPrefabRoot + def.Prefab);
                    if (prefab == null)
                    {
                        Debug.LogError("[LowPoly] Prop prefab not found: " + ArtPrefabRoot + def.Prefab
                            + ". Run 'Generate All Assets' first.");
                        continue;
                    }

                    Transform group = new GameObject(System.IO.Path.GetFileNameWithoutExtension(def.Prefab)).transform;
                    group.SetParent(scatterRoot.transform, false);

                    int made = PlaceGroup(def, prefab, group, centre, placed, heights);
                    total += made;
                    skipped += def.Count - made;

                    summary.AppendLine(string.Format("  {0,-20} {1,3} / {2,3}",
                        System.IO.Path.GetFileNameWithoutExtension(def.Prefab), made, def.Count));
                }
            }
            finally
            {
                Random.state = previousState;
            }

            summary.AppendLine();
            summary.AppendLine("  " + total + " props placed under '" + ScatterRootName + "'.");
            if (skipped > 0)
            {
                // Never let a density cap look like full coverage.
                summary.AppendLine("  " + skipped + " could not find a free spot in " + MaxTriesPerProp
                    + " tries - lower Spacing or Count in LowPolyScatter.cs to fit more.");
            }
            summary.AppendLine("  Props carry no colliders, so pathing, clicks and the NavMesh are unaffected.");
            summary.AppendLine("  Scene is dirty - save it to keep the layout.");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(summary.ToString());
        }

        [MenuItem(MenuRoot + "Clear Scattered Props", false, 81)]
        public static void Clear()
        {
            GameObject scatterRoot = FindScatterRoot();
            if (scatterRoot == null)
            {
                Debug.Log("[LowPoly] Nothing to clear - no '" + ScatterRootName + "' in the open scene.");
                return;
            }

            Undo.DestroyObjectImmediate(scatterRoot);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[LowPoly] Removed '" + ScatterRootName + "'.");
        }

        // ==================================================================
        // Placement
        // ==================================================================

        private static int PlaceGroup(PropDef def, GameObject prefab, Transform group,
                                      Vector3 centre, List<Vector3> placed, float[,] heights)
        {
            int made = 0;

            for (int i = 0; i < def.Count; i++)
            {
                for (int attempt = 0; attempt < MaxTriesPerProp; attempt++)
                {
                    // Sqrt on the radius keeps the distribution area-uniform instead of
                    // bunching everything against the inner edge of the band.
                    float t = Mathf.Sqrt(Random.value);
                    float radius = Mathf.Lerp(def.MinRadius, def.MaxRadius, t);
                    float angle = Random.value * Mathf.PI * 2f;

                    Vector3 candidate = centre + new Vector3(
                        Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                    if (candidate.magnitude > IslandRadius) continue;
                    if (Vector3.Distance(candidate, centre) < CampfireClearing) continue;
                    if (TooClose(candidate, placed, def.Spacing)) continue;

                    Vector3 grounded;
                    if (!SampleGround(candidate, heights, out grounded)) continue;

                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                    instance.transform.position = grounded;
                    instance.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);

                    float scale = Random.Range(def.MinScale, def.MaxScale);
                    instance.transform.localScale = new Vector3(scale, scale, scale);

                    // Decor never moves, so let it batch now and be lightmap-ready for Stage 4.
                    GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.BatchingStatic
                        | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);

                    placed.Add(grounded);
                    made++;
                    break;
                }
            }

            return made;
        }

        private static bool TooClose(Vector3 candidate, List<Vector3> placed, float spacing)
        {
            float sqrSpacing = spacing * spacing;
            for (int i = 0; i < placed.Count; i++)
            {
                Vector3 delta = placed[i] - candidate;
                delta.y = 0f;
                if (delta.sqrMagnitude < sqrSpacing) return true;
            }
            return false;
        }

        /// <summary>
        /// Drop the candidate onto the generated island surface. Below-waterline
        /// (h &lt; 0.05) candidates are rejected - that is how props stay off the water.
        /// </summary>
        private static bool SampleGround(Vector3 candidate, float[,] heights, out Vector3 grounded)
        {
            grounded = candidate;

            float h = TerrainGrid.SampleField(heights, candidate.x, candidate.z);
            if (h < 0.05f) return false;

            grounded = new Vector3(candidate.x, h, candidate.z);
            return true;
        }

        /// <summary>
        /// The campfire is the island centre for layout purposes. Falls back to the origin,
        /// which is where MainIsland actually puts it.
        /// </summary>
        private static Vector3 FindIslandCentre()
        {
            BaseBuilding campfire = Object.FindAnyObjectByType<BaseBuilding>();
            if (campfire != null) return new Vector3(campfire.transform.position.x, 0f, campfire.transform.position.z);
            return Vector3.zero;
        }

        /// <summary>Same guard the other setup tools use — scatter belongs in the game scene only.</summary>
        private static bool EnsureGameSceneOpen()
        {
            const string ScenePath = "Assets/MainIsland.unity";
            if (EditorSceneManager.GetActiveScene().path == ScenePath) return true;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(ScenePath);
            return true;
        }

        private static GameObject FindScatterRoot()
        {
            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == ScatterRootName) return roots[i];
            }
            return null;
        }
    }
}

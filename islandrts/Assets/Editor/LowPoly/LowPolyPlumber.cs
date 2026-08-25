using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Wires the generated Assets/Art library into the real gameplay prefabs.
    ///
    /// LowPolyAssetGenerator deliberately writes a self-contained art library and touches
    /// nothing else. This is the other half: it mounts those mesh-only art prefabs onto the
    /// gameplay prefabs that carry Health, AIBrain, NavMeshAgent, ResourceNode and the
    /// registry components.
    ///
    /// Shape of the swap:
    ///   - the root keeps every script / collider / agent, and loses its primitive
    ///     MeshFilter+MeshRenderer (or has its old visual children removed)
    ///   - the root localScale is reset to 1 - the art is authored in real world units
    ///   - a "Model" child is added as a NESTED PREFAB INSTANCE of the art prefab, so
    ///     re-running the generator propagates mesh/material edits automatically
    ///   - colliders and NavMeshObstacle carves are re-expressed in world units (they used
    ///     to ride the root's non-uniform scale) and recentred for the art's base pivot
    ///   - NavMeshAgent.baseOffset goes to 0 so feet sit on the NavMesh, not 1 above it
    ///   - HealthBar / state-text offsets are retuned, because resetting the root scale
    ///     un-squashes every child (bars were scaled, text was skewed by LookAt)
    ///   - BuildingData.placementHeight goes 0.75 -> 0 for base-pivot buildings
    ///
    /// NavMeshAgent radius/height/speed are deliberately NOT touched - those are pathing and
    /// NavMesh-bake concerns, not art.
    ///
    /// Re-running is safe: the Model child is rebuilt from scratch and every value is
    /// assigned absolutely, never accumulated.
    /// </summary>
    public static class LowPolyPlumber
    {
        private const string MenuRoot = "Tools/Island RTS/Low-Poly Templates/";
        private const string ModelChildName = "Model";
        private const string ArtPrefabRoot = "Assets/Art/Prefabs/";
        private const string ArtMeshRoot = "Assets/Art/Meshes/";
        private const string GhostMaterialPath = "Assets/Materials/Mat_Ghostbuilding.mat";

        // ==================================================================
        // Plumb table
        // ==================================================================

        /// <summary>
        /// One gameplay prefab to mount art onto. Every retune field is nullable: null means
        /// "leave whatever the prefab already has alone".
        /// </summary>
        private class Plumb
        {
            public string GameplayPrefab;
            public string ArtPrefab;

            /// <summary>Reset the root to scale 1. Set when the root carried a primitive squash.</summary>
            public bool ResetRootScale;
            /// <summary>Destroy the root's MeshFilter/MeshRenderer (the old primitive visual).</summary>
            public bool StripRootMesh;
            /// <summary>Child GameObjects to delete outright - old visual-only parts.</summary>
            public string[] RemoveChildren;
            /// <summary>
            /// Disable every Renderer outside the Model child instead of deleting it. Used where
            /// the old visual belongs to a nested prefab (the Tree FBX) or is being kept on
            /// purpose (the Campfire's Flame), so it stays one checkbox away from coming back.
            /// </summary>
            public bool HideOtherRenderers;

            // Collider / obstacle retune, in world units at scale 1.
            public Vector3? BoxSize, BoxCenter;
            public Vector3? ObstacleExtents, ObstacleCenter;
            public float? CapsuleRadius, CapsuleHeight;
            public float? BaseOffset;

            // UI retune.
            public float? BarOffset, BarWidth, BarHeight, TextOffset;

            /// <summary>BuildingData asset whose placementHeight follows this prefab's pivot.</summary>
            public string BuildingData;
            public float? PlacementHeight;

            /// <summary>Art mesh variants wired onto a TreeVariance component on the root.</summary>
            public string[] VariantMeshes;
        }

        // ---- Units -------------------------------------------------------
        // Sizes come from the SizeNote strings in Shapes_Units.cs, and they already match the
        // old primitives' WORLD dimensions (capsule r0.5 h2 times the root scale). So the
        // silhouette SIZE does not change - only pivot, scale and shape.
        //   Worker  0.40 wide, 1.2 tall   (was r0.5 h2 * 0.40/0.60/0.40)
        //   Warrior 0.50 wide, 1.4 tall   (was r0.5 h2 * 0.50/0.70/0.50)
        //   Enemy   0.45 wide, 1.4 tall   (was r0.5 h2 * 0.45/0.70/0.45)
        private static readonly Plumb[] Units =
        {
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/Worker.prefab",
                ArtPrefab      = ArtPrefabRoot + "Units/Worker.prefab",
                ResetRootScale = true, StripRootMesh = true,
                CapsuleRadius = 0.2f, CapsuleHeight = 1.2f, BaseOffset = 0f,
                TextOffset = 2f
            },
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/Warrior.prefab",
                ArtPrefab      = ArtPrefabRoot + "Units/Warrior.prefab",
                ResetRootScale = true, StripRootMesh = true,
                CapsuleRadius = 0.25f, CapsuleHeight = 1.4f, BaseOffset = 0f,
                BarOffset = 1.8f, BarWidth = 0.6f, BarHeight = 0.1f, TextOffset = 2.2f
            },
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/Enemy.prefab",
                ArtPrefab      = ArtPrefabRoot + "Units/Enemy.prefab",
                ResetRootScale = true, StripRootMesh = true,
                CapsuleRadius = 0.225f, CapsuleHeight = 1.4f, BaseOffset = 0f,
                BarOffset = 1.8f, BarWidth = 0.6f, BarHeight = 0.1f, TextOffset = 2.2f
            },
        };

        // ---- Buildings ---------------------------------------------------
        //   Hut         2.0 x 2.0 footprint, 2.6 to roof peak   (was cube * 2/1.5/2)
        //   Watchtower  2.0 x 2.0 footprint, 4.0 tall           (was cube * 2/4/2)
        //   Campfire    1.5 dia, ~1.0 tall including flame      (root was already scale 1)
        //
        // Hut/Watchtower obstacle extents keep the SAME world carve volume as before
        // (2.2 x 1.65 x 2.2 for the hut) - just lifted onto the base pivot so it sits on the
        // ground instead of straddling it. Horizontal extent is what pathing cares about.
        //
        // Health bars are NOT preserved verbatim: heightOffset was multiplied by the root's Y
        // scale, so the hut's bar was floating 5.25 world units up and the tower's 12.75.
        // These values put them just above the new rooflines instead.
        private static readonly Plumb[] Buildings =
        {
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/Hut.prefab",
                ArtPrefab      = ArtPrefabRoot + "Buildings/Hut.prefab",
                ResetRootScale = true, StripRootMesh = true,
                BoxSize = new Vector3(2f, 2.6f, 2f), BoxCenter = new Vector3(0f, 1.3f, 0f),
                ObstacleExtents = new Vector3(1.1f, 0.825f, 1.1f), ObstacleCenter = new Vector3(0f, 0.825f, 0f),
                BarOffset = 3.2f, BarWidth = 2f, BarHeight = 0.2f,
                BuildingData = "Assets/HutData.asset", PlacementHeight = 0f
            },
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/WatchTower.prefab",
                ArtPrefab      = ArtPrefabRoot + "Buildings/Watchtower.prefab",
                ResetRootScale = true, StripRootMesh = true,
                BoxSize = new Vector3(2f, 4f, 2f), BoxCenter = new Vector3(0f, 2f, 0f),
                ObstacleExtents = new Vector3(1.1f, 2f, 1.1f), ObstacleCenter = new Vector3(0f, 2f, 0f),
                BarOffset = 4.6f, BarWidth = 2f, BarHeight = 0.2f,
                BuildingData = "Assets/WatchTowerData.asset", PlacementHeight = 0f
            },
            new Plumb
            {
                // The art Campfire mesh contains its OWN flame (HDR-emissive Ember/FireCore
                // prisms that clear the Bloom threshold of 1.0), so the old Flame object is
                // kept but hidden rather than deleted - re-enable its MeshRenderer to get the
                // original capsule flame back. FirePit and Wood are pure visual and go.
                // NavMeshObstacle is left alone: MainIsland overrides its extents per-instance,
                // so a prefab-level change would not reach the scene's campfire anyway.
                GameplayPrefab = "Assets/Prefabs/Campfire.prefab",
                ArtPrefab      = ArtPrefabRoot + "Buildings/Campfire.prefab",
                RemoveChildren = new[] { "FirePit", "Wood" },
                HideOtherRenderers = true,
                BoxSize = new Vector3(2f, 1.2f, 2f), BoxCenter = new Vector3(0f, 0.6f, 0f)
            },
        };

        // ---- Resource nodes ----------------------------------------------
        //   Tree       ~2.2 wide canopy, 3.6 tall
        //   RockNode   1.3 x 1.0
        //   BerryBush  1.0 x 0.8
        //
        // Tree.prefab wraps the "Tree for Carson V3" FBX as a nested prefab instance at root
        // scale 0.5, so its renderers are hidden rather than deleted. Taking the root to scale
        // 1 also doubles the runtime NavMeshObstacle that ResourceNode.SetupNavMeshObstacle
        // assigns (radius 0.8 / height 2) from an effective 0.4/1.0 to the intended 0.8/2.0 -
        // which is the right size for a 3.6-tall tree, but it IS a pathing change worth watching.
        //
        // Serialized obstacle extents on these three are not listed: ResourceNode overwrites
        // shape/radius/height at runtime, so editing them here would do nothing.
        private static readonly Plumb[] Resources =
        {
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/Tree.prefab",
                ArtPrefab      = ArtPrefabRoot + "Resources/Tree.prefab",
                ResetRootScale = true, HideOtherRenderers = true,
                BoxSize = new Vector3(2.2f, 3.6f, 2.2f), BoxCenter = new Vector3(0f, 1.8f, 0f),
                VariantMeshes = new[]
                {
                    ArtMeshRoot + "Tree.asset",
                    ArtMeshRoot + "Tree_B.asset",
                    ArtMeshRoot + "Tree_C.asset",
                }
            },
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/RockNode.prefab",
                ArtPrefab      = ArtPrefabRoot + "Resources/RockNode.prefab",
                RemoveChildren = new[] { "Cube" },
                BoxSize = new Vector3(1.4f, 1f, 1.4f), BoxCenter = new Vector3(0f, 0.5f, 0f)
            },
            new Plumb
            {
                GameplayPrefab = "Assets/Prefabs/BerryBush.prefab",
                ArtPrefab      = ArtPrefabRoot + "Resources/BerryBush.prefab",
                RemoveChildren = new[] { "Sphere" },
                BoxSize = new Vector3(1.1f, 0.8f, 1.1f), BoxCenter = new Vector3(0f, 0.4f, 0f)
            },
        };

        // ---- Placement ghosts --------------------------------------------
        private class GhostPlumb
        {
            public string GhostPrefab;
            public string ArtMesh;
            public Vector3 BoxSize, BoxCenter;
        }

        // Ghosts take the art MESH directly on their existing root renderer rather than a Model
        // child. That keeps BuildPlacement's `currentGhost.GetComponent<Renderer>()` working and
        // lets every submesh slot be filled with the translucent ghost material - a nested art
        // prefab would drag its opaque LP materials along instead.
        //
        // Wall ghosts are not listed: WallLinePlacer builds those procedurally from
        // WallConnector.GetOrCreateMesh, so their prefabs are never instantiated.
        private static readonly GhostPlumb[] Ghosts =
        {
            new GhostPlumb
            {
                GhostPrefab = "Assets/Prefabs/HutGhost.prefab",
                ArtMesh     = ArtMeshRoot + "Hut.asset",
                BoxSize = new Vector3(2f, 2.6f, 2f), BoxCenter = new Vector3(0f, 1.3f, 0f)
            },
            new GhostPlumb
            {
                GhostPrefab = "Assets/Prefabs/WatchTowerGhost.prefab",
                ArtMesh     = ArtMeshRoot + "Watchtower.asset",
                BoxSize = new Vector3(2f, 4f, 2f), BoxCenter = new Vector3(0f, 2f, 0f)
            },
        };

        // ---- Wall materials ----------------------------------------------
        private class WallMaterialPlumb
        {
            public string GameplayPrefab;
            public string Material;
        }

        // Walls stay procedural - WallConnector generates 6 shapes + 6 gate variants at runtime
        // and writes them onto the root MeshFilter, so a mesh swap here would just be overwritten.
        // All we do is put them on the low-poly palette so they stop clashing with everything else.
        private static readonly WallMaterialPlumb[] WallMaterials =
        {
            new WallMaterialPlumb { GameplayPrefab = "Assets/Prefabs/WoodenWall.prefab", Material = "Assets/Art/Materials/LP_WoodPlank.mat" },
            new WallMaterialPlumb { GameplayPrefab = "Assets/Prefabs/StoneWall.prefab",  Material = "Assets/Art/Materials/LP_StoneBlock.mat" },
        };

        // ==================================================================
        // Menu entries
        // ==================================================================

        /// <summary>
        /// The one-click path: regenerate the art library from the current shape
        /// code, THEN mount it onto the gameplay prefabs. Running Plumb alone
        /// re-mounts whatever is already on disk — after any Shapes_*.cs change
        /// the generate step is REQUIRED first, and skipping it silently plumbs
        /// stale art (exactly what happened on 2026-08-24).
        /// </summary>
        [MenuItem(MenuRoot + "Generate + Plumb Everything", false, 58)]
        public static void GenerateAndPlumbEverything()
        {
            LowPolyAssetGenerator.GenerateAll();
            PlumbEverything();
        }

        [MenuItem(MenuRoot + "Plumb Everything", false, 59)]
        public static void PlumbEverything()
        {
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("[LowPoly] Full plumbing pass.");

            int done = 0;
            done += RunPlumbs(Units, "Units", summary);
            done += RunPlumbs(Buildings, "Buildings", summary);
            done += RunPlumbs(Resources, "Resources", summary);
            done += RunGhosts(summary);
            done += RunWallMaterials(summary);

            Finish(done, summary, true);
        }

        [MenuItem(MenuRoot + "Plumb Units", false, 60)]
        public static void PlumbUnits()
        {
            StringBuilder summary = new StringBuilder();
            Finish(RunPlumbs(Units, "Units", summary), summary, true);
        }

        [MenuItem(MenuRoot + "Plumb Buildings (+ Ghosts)", false, 61)]
        public static void PlumbBuildings()
        {
            StringBuilder summary = new StringBuilder();
            int done = RunPlumbs(Buildings, "Buildings", summary) + RunGhosts(summary);
            Finish(done, summary, true);
        }

        [MenuItem(MenuRoot + "Plumb Resource Nodes", false, 62)]
        public static void PlumbResources()
        {
            StringBuilder summary = new StringBuilder();
            Finish(RunPlumbs(Resources, "Resources", summary), summary, true);
        }

        [MenuItem(MenuRoot + "Re-Material Walls (Keep Procedural Meshes)", false, 63)]
        public static void RematerialWalls()
        {
            StringBuilder summary = new StringBuilder();
            Finish(RunWallMaterials(summary), summary, false);
        }

        // ==================================================================
        // Batch runners
        // ==================================================================

        private static int RunPlumbs(Plumb[] entries, string label, StringBuilder summary)
        {
            summary.AppendLine();
            summary.AppendLine("  " + label.ToUpperInvariant());

            int done = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (PlumbPrefab(entries[i], summary)) done++;
            }
            return done;
        }

        private static int RunGhosts(StringBuilder summary)
        {
            summary.AppendLine();
            summary.AppendLine("  GHOSTS");

            Material ghostMat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (ghostMat == null)
            {
                Debug.LogError("[LowPoly] Ghost material not found: " + GhostMaterialPath);
                return 0;
            }

            int done = 0;
            for (int i = 0; i < Ghosts.Length; i++)
            {
                if (PlumbGhost(Ghosts[i], ghostMat, summary)) done++;
            }
            return done;
        }

        private static int RunWallMaterials(StringBuilder summary)
        {
            summary.AppendLine();
            summary.AppendLine("  WALL MATERIALS (meshes left procedural)");

            int done = 0;
            for (int i = 0; i < WallMaterials.Length; i++)
            {
                if (RematerialWall(WallMaterials[i], summary)) done++;
            }
            return done;
        }

        private static void Finish(int done, StringBuilder summary, bool navMeshAffected)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (done == 0) return;

            if (navMeshAffected)
            {
                summary.AppendLine();
                summary.AppendLine("  Collider and carve footprints changed - RE-BAKE THE NAVMESH.");
            }

            Debug.Log(summary.ToString());
        }

        // ==================================================================
        // Prefab plumbing
        // ==================================================================

        private static bool PlumbPrefab(Plumb p, StringBuilder summary)
        {
            GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(p.ArtPrefab);
            if (art == null)
            {
                Debug.LogError("[LowPoly] Art prefab not found: " + p.ArtPrefab + ". Run 'Generate All Assets' first.");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(p.GameplayPrefab);
            if (root == null)
            {
                Debug.LogError("[LowPoly] Gameplay prefab not found: " + p.GameplayPrefab);
                return false;
            }

            try
            {
                // 1. Drop the old primitive visual off the root.
                if (p.StripRootMesh)
                {
                    MeshFilter rootFilter = root.GetComponent<MeshFilter>();
                    if (rootFilter != null) Object.DestroyImmediate(rootFilter, true);
                    MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
                    if (rootRenderer != null) Object.DestroyImmediate(rootRenderer, true);
                }

                // 2. Delete old visual-only children (FirePit/Wood, Cube, Sphere, ...).
                if (p.RemoveChildren != null)
                {
                    for (int i = 0; i < p.RemoveChildren.Length; i++)
                    {
                        Transform child = root.transform.Find(p.RemoveChildren[i]);
                        if (child != null) Object.DestroyImmediate(child.gameObject);
                    }
                }

                // 3. The art is authored at real world scale, so the root goes back to 1.
                //    Everything that used to inherit the squash is fixed up in steps 5-7.
                if (p.ResetRootScale) root.transform.localScale = Vector3.one;

                // 4. Rebuild the Model child as a nested prefab instance (idempotent).
                Transform existing = root.transform.Find(ModelChildName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(art, root.transform);
                model.name = ModelChildName;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                // 5. Hide leftover visuals that could not be deleted (nested prefab meshes) or
                //    are being kept deliberately. Renderers only - the GameObjects stay.
                if (p.HideOtherRenderers) HideRenderersOutside(root, model.transform);

                // 6. Colliders / carves were in local space riding the root's scale; restate
                //    them in world units and recentre for the art's base pivot.
                ApplyColliders(root, p);

                // 7. Base pivot means the transform origin IS the agent's base.
                //    (radius/height/speed left alone - pathing concerns, not art.)
                if (p.BaseOffset.HasValue)
                {
                    NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
                    if (agent != null) agent.baseOffset = p.BaseOffset.Value;
                }

                // 8. Un-squashed children need retuned offsets.
                ApplyUI(root, p);

                // 9. Mesh variants: wire the runtime TreeVariance component.
                ApplyVariants(root, p);

                PrefabUtility.SaveAsPrefabAsset(root, p.GameplayPrefab);
                summary.AppendLine("    " + Path.GetFileNameWithoutExtension(p.GameplayPrefab).PadRight(14)
                    + "<- " + p.ArtPrefab.Substring(ArtPrefabRoot.Length));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // BuildingData is a separate asset, so it is edited outside the prefab round-trip.
            ApplyBuildingData(p, summary);
            return true;
        }

        private static void HideRenderersOutside(GameObject root, Transform model)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (renderers[i].transform.IsChildOf(model)) continue;
                renderers[i].enabled = false;
            }
        }

        private static void ApplyVariants(GameObject root, Plumb p)
        {
            if (p.VariantMeshes == null) return;

            TreeVariance variance = root.GetComponent<TreeVariance>();
            if (variance == null) variance = root.AddComponent<TreeVariance>();

            Mesh[] meshes = new Mesh[p.VariantMeshes.Length];
            for (int i = 0; i < p.VariantMeshes.Length; i++)
            {
                meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(p.VariantMeshes[i]);
                if (meshes[i] == null)
                    Debug.LogError("[LowPoly] Variant mesh not found: " + p.VariantMeshes[i]
                        + ". Run 'Generate All Assets' first.");
            }
            variance.variantMeshes = meshes;
        }

        private static void ApplyColliders(GameObject root, Plumb p)
        {
            if (p.CapsuleRadius.HasValue && p.CapsuleHeight.HasValue)
            {
                float radius = p.CapsuleRadius.Value;
                float height = p.CapsuleHeight.Value;

                CapsuleCollider[] capsules = root.GetComponents<CapsuleCollider>();
                for (int i = 0; i < capsules.Length; i++)
                {
                    capsules[i].direction = 1; // Y axis
                    capsules[i].radius = radius;
                    capsules[i].height = height;
                    capsules[i].center = new Vector3(0f, height * 0.5f, 0f);
                }
            }

            if (p.BoxSize.HasValue)
            {
                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.size = p.BoxSize.Value;
                    if (p.BoxCenter.HasValue) box.center = p.BoxCenter.Value;
                }
            }

            if (p.ObstacleExtents.HasValue)
            {
                NavMeshObstacle obstacle = root.GetComponent<NavMeshObstacle>();
                if (obstacle != null)
                {
                    obstacle.size = p.ObstacleExtents.Value * 2f; // size, not half-extents
                    if (p.ObstacleCenter.HasValue) obstacle.center = p.ObstacleCenter.Value;
                }
            }
        }

        private static void ApplyUI(GameObject root, Plumb p)
        {
            HealthBar bar = root.GetComponent<HealthBar>();
            if (bar != null)
            {
                if (p.BarOffset.HasValue) bar.heightOffset = p.BarOffset.Value;
                if (p.BarWidth.HasValue) bar.barWidth = p.BarWidth.Value;
                if (p.BarHeight.HasValue) bar.barHeight = p.BarHeight.Value;
            }

            if (!p.TextOffset.HasValue) return;

            // textHeightOffset lives on UnitBase<T>, which is generic - reach it by name
            // instead of casting, so one code path covers Worker/Warrior/Enemy.
            MonoBehaviour[] behaviours = root.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null) continue;

                SerializedObject so = new SerializedObject(behaviours[i]);
                SerializedProperty prop = so.FindProperty("textHeightOffset");
                if (prop != null)
                {
                    prop.floatValue = p.TextOffset.Value;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static void ApplyBuildingData(Plumb p, StringBuilder summary)
        {
            if (p.BuildingData == null || !p.PlacementHeight.HasValue) return;

            Object data = AssetDatabase.LoadAssetAtPath<Object>(p.BuildingData);
            if (data == null)
            {
                Debug.LogError("[LowPoly] BuildingData not found: " + p.BuildingData);
                return;
            }

            SerializedObject so = new SerializedObject(data);
            SerializedProperty prop = so.FindProperty("placementHeight");
            if (prop == null)
            {
                Debug.LogError("[LowPoly] No placementHeight on " + p.BuildingData);
                return;
            }

            prop.floatValue = p.PlacementHeight.Value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            summary.AppendLine("      " + Path.GetFileNameWithoutExtension(p.BuildingData)
                + ".placementHeight -> " + p.PlacementHeight.Value.ToString("0.##"));
        }

        // ==================================================================
        // Ghost plumbing
        // ==================================================================

        private static bool PlumbGhost(GhostPlumb g, Material ghostMat, StringBuilder summary)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(g.ArtMesh);
            if (mesh == null)
            {
                Debug.LogError("[LowPoly] Art mesh not found: " + g.ArtMesh + ". Run 'Generate All Assets' first.");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(g.GhostPrefab);
            if (root == null)
            {
                Debug.LogError("[LowPoly] Ghost prefab not found: " + g.GhostPrefab);
                return false;
            }

            try
            {
                MeshFilter filter = root.GetComponent<MeshFilter>();
                MeshRenderer renderer = root.GetComponent<MeshRenderer>();
                if (filter == null || renderer == null)
                {
                    Debug.LogError("[LowPoly] " + g.GhostPrefab + " needs a root MeshFilter+MeshRenderer - "
                        + "BuildPlacement looks the ghost renderer up on the root.");
                    return false;
                }

                filter.sharedMesh = mesh;

                // One ghost material per submesh, or the extra submeshes render with Unity's
                // magenta error material.
                Material[] slots = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < slots.Length; i++) slots[i] = ghostMat;
                renderer.sharedMaterials = slots;

                root.transform.localScale = Vector3.one;

                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.size = g.BoxSize;
                    box.center = g.BoxCenter;
                }

                PrefabUtility.SaveAsPrefabAsset(root, g.GhostPrefab);
                summary.AppendLine("    " + Path.GetFileNameWithoutExtension(g.GhostPrefab).PadRight(18)
                    + "<- " + Path.GetFileNameWithoutExtension(g.ArtMesh)
                    + "  (" + slots.Length + " ghost slots)");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ==================================================================
        // Wall materials
        // ==================================================================

        private static bool RematerialWall(WallMaterialPlumb w, StringBuilder summary)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(w.Material);
            if (mat == null)
            {
                Debug.LogError("[LowPoly] Material not found: " + w.Material + ". Run 'Generate All Assets' first.");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(w.GameplayPrefab);
            if (root == null)
            {
                Debug.LogError("[LowPoly] Gameplay prefab not found: " + w.GameplayPrefab);
                return false;
            }

            try
            {
                MeshRenderer renderer = root.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    Debug.LogError("[LowPoly] No root MeshRenderer on " + w.GameplayPrefab + " - WallConnector expects one.");
                    return false;
                }

                // Procedural wall meshes are single-submesh, so exactly one material slot.
                renderer.sharedMaterials = new Material[] { mat };
                PrefabUtility.SaveAsPrefabAsset(root, w.GameplayPrefab);

                summary.AppendLine("    " + Path.GetFileNameWithoutExtension(w.GameplayPrefab).PadRight(14)
                    + "<- " + mat.name);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}

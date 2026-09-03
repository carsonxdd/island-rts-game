using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Generates the low-poly template art set into Assets/Art/.
    ///
    /// Nothing here touches the existing gameplay prefabs, scenes, or NavMesh - the
    /// output is a self-contained folder you can preview, tweak, and swap in yourself
    /// when you are happy with it.
    ///
    /// Re-running is safe and idempotent: existing meshes and materials are updated
    /// in place via CopySerialized so their GUIDs survive, which means anything you
    /// have already dragged into a scene keeps working after a regenerate.
    /// </summary>
    public static class LowPolyAssetGenerator
    {
        private const string RootFolder = "Assets/Art";
        private const string MeshFolder = RootFolder + "/Meshes";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string PrefabFolder = RootFolder + "/Prefabs";
        private const string ShowcaseScenePath = RootFolder + "/LowPolyShowcase.unity";

        private const string MenuRoot = "Tools/Island RTS/Low-Poly Templates/";

        // Matches the gameplay camera in MainIsland.unity (euler 45/45/0, orthographic).
        private static readonly Vector3 GameplayCameraEuler = new Vector3(45f, 45f, 0f);
        // Matches the scene directional light (euler 20/0/0) described in CLAUDE.md Phase 10 Stage 1.
        private static readonly Vector3 SunEuler = new Vector3(20f, -35f, 0f);

        // ==================================================================
        // Menu entries
        // ==================================================================

        [MenuItem(MenuRoot + "Generate All Assets", false, 0)]
        public static void GenerateAll()
        {
            Generate(null);
        }

        [MenuItem(MenuRoot + "Generate Environment Only", false, 20)]
        public static void GenerateEnvironment() { Generate(AssetCategory.Environment); }

        [MenuItem(MenuRoot + "Generate Buildings Only", false, 21)]
        public static void GenerateBuildings() { Generate(AssetCategory.Buildings); }

        [MenuItem(MenuRoot + "Generate Units Only", false, 22)]
        public static void GenerateUnits() { Generate(AssetCategory.Units); }

        [MenuItem(MenuRoot + "Generate Resource Nodes Only", false, 23)]
        public static void GenerateResources() { Generate(AssetCategory.Resources); }

        [MenuItem(MenuRoot + "Generate Tools Only", false, 24)]
        public static void GenerateTools() { Generate(AssetCategory.Tools); }

        [MenuItem(MenuRoot + "Regenerate Materials Only", false, 40)]
        public static void RegenerateMaterials()
        {
            EnsureFolders();
            int n = BuildMaterials().Count;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LowPoly] Rebuilt " + n + " materials in " + MaterialFolder + ".");
        }

        // ==================================================================
        // Generation
        // ==================================================================

        private static void Generate(AssetCategory? only)
        {
            EnsureFolders();

            Dictionary<string, Material> materials = BuildMaterials();
            List<AssetDef> defs = LowPolyShapes.All();

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("[LowPoly] Template asset generation complete.");
            summary.AppendLine("Output: " + RootFolder + "  (existing gameplay prefabs untouched)");
            summary.AppendLine();
            summary.AppendLine("  ASSET                  TRIS   SUBMESHES  SIZE");

            int generated = 0;
            int totalTris = 0;

            // Deliberately NOT wrapped in StartAssetEditing/StopAssetEditing:
            // PrefabUtility.SaveAsPrefabAsset and AssetDatabase.CreateFolder both need a
            // live asset database, and folder creation inside a batch block silently fails.
            // At ~26 assets the import cost is not worth the correctness risk.
            try
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    AssetDef def = defs[i];
                    if (only.HasValue && def.Category != only.Value) continue;

                    EditorUtility.DisplayProgressBar("Generating low-poly templates", def.Name, (float)i / defs.Count);

                    MeshBuilder builder = def.Build();
                    List<string> matKeys = builder.MaterialKeys;
                    Mesh mesh = builder.ToMesh(def.Name);

                    SaveMesh(mesh, MeshFolder + "/" + def.Name + ".asset", out mesh);
                    BuildPrefab(def, mesh, matKeys, materials);

                    generated++;
                    totalTris += builder.TriangleCount;
                    summary.AppendLine(string.Format("  {0,-22} {1,5}   {2,5}      {3}",
                        def.Name, builder.TriangleCount, matKeys.Count, def.SizeNote));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            summary.AppendLine();
            summary.AppendLine("  " + generated + " assets, " + totalTris + " triangles total.");
            summary.AppendLine("  Next: " + MenuRoot + "Build Showcase Scene to view them at the gameplay camera angle.");
            Debug.Log(summary.ToString());
        }

        private static void SaveMesh(Mesh mesh, string path, out Mesh saved)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Copy into the existing asset so its GUID survives - anything already
                // placed in a scene keeps pointing at the updated mesh.
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                saved = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
                saved = mesh;
            }
        }

        private static void BuildPrefab(AssetDef def, Mesh mesh, List<string> matKeys, Dictionary<string, Material> materials)
        {
            string categoryFolder = PrefabFolder + "/" + def.Category;

            GameObject go = new GameObject(def.Name);
            try
            {
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                Material[] mats = new Material[matKeys.Count];
                for (int i = 0; i < matKeys.Count; i++)
                {
                    Material m;
                    mats[i] = materials.TryGetValue(matKeys[i], out m) ? m : null;
                }
                mr.sharedMaterials = mats;

                PrefabUtility.SaveAsPrefabAsset(go, categoryFolder + "/" + def.Name + ".prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ==================================================================
        // Materials
        // ==================================================================

        private static Dictionary<string, Material> BuildMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[LowPoly] Could not find the URP Lit shader. Materials will fall back to the built-in Standard shader.");
                shader = Shader.Find("Standard");
            }

            Dictionary<string, Material> result = new Dictionary<string, Material>();
            LowPolyPalette.Entry[] entries = LowPolyPalette.All;

            for (int i = 0; i < entries.Length; i++)
            {
                LowPolyPalette.Entry e = entries[i];
                string path = MaterialFolder + "/LP_" + e.Key + ".mat";

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew) mat = new Material(shader);
                else mat.shader = shader;

                mat.SetColor("_BaseColor", e.Color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", e.Color); // built-in fallback
                mat.SetFloat("_Smoothness", e.Smoothness);
                mat.SetFloat("_Metallic", 0f);

                if (e.HasEmission)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    // HDR value above 1 so it clears the Global Volume Bloom threshold of 1.0
                    // without the threshold having to be lowered scene-wide.
                    mat.SetColor("_EmissionColor", e.Emission * e.EmissionIntensity);
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
                }

                if (isNew) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);

                result[e.Key] = mat;
            }

            return result;
        }

        // ==================================================================
        // Showcase scene
        // ==================================================================

        [MenuItem(MenuRoot + "Build Showcase Scene", false, 60)]
        public static void BuildShowcaseScene()
        {
            // Opening a new scene would discard unsaved work, so always give the user
            // the save prompt first and bail if they cancel.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            List<AssetDef> defs = LowPolyShapes.All();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            const float spacing = 3.2f;
            const float rowGap = 4.6f;

            // Group by category, one row each.
            AssetCategory[] categories = { AssetCategory.Environment, AssetCategory.Buildings, AssetCategory.Units, AssetCategory.Resources, AssetCategory.Tools };
            int maxCols = 1;
            for (int c = 0; c < categories.Length; c++)
            {
                int count = 0;
                for (int i = 0; i < defs.Count; i++) if (defs[i].Category == categories[c]) count++;
                if (count > maxCols) maxCols = count;
            }

            float width = maxCols * spacing;
            float depth = categories.Length * rowGap;

            // ---- Ground -------------------------------------------------------
            Material sand = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/LP_Sand.mat");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, 0f, 0f);
            ground.transform.localScale = new Vector3(width * 0.16f + 2f, 1f, depth * 0.16f + 2f);
            if (sand != null) ground.GetComponent<MeshRenderer>().sharedMaterial = sand;

            // ---- Assets -------------------------------------------------------
            int placed = 0;
            for (int c = 0; c < categories.Length; c++)
            {
                GameObject row = new GameObject(categories[c].ToString());
                float z = depth * 0.5f - rowGap * c - rowGap * 0.5f;
                row.transform.position = new Vector3(0f, 0f, z);

                int col = 0;
                for (int i = 0; i < defs.Count; i++)
                {
                    if (defs[i].Category != categories[c]) continue;

                    string path = PrefabFolder + "/" + categories[c] + "/" + defs[i].Name + ".prefab";
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        Debug.LogWarning("[LowPoly] Showcase: prefab missing, run Generate All Assets first - " + path);
                        col++;
                        continue;
                    }

                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.transform.SetParent(row.transform, false);
                    instance.transform.localPosition = new Vector3(-width * 0.5f + spacing * (col + 0.5f), 0f, 0f);
                    col++;
                    placed++;
                }
            }

            // ---- Sun ----------------------------------------------------------
            GameObject sunGO = new GameObject("Directional Light");
            Light sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.79f);   // warm gold, matching the day preset
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sunGO.transform.rotation = Quaternion.Euler(SunEuler);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.60f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.44f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.25f, 0.31f);

            // ---- Camera at the real gameplay angle -------------------------------
            GameObject camGO = new GameObject("Preview Camera");
            Camera cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(width, depth) * 0.42f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.62f, 0.72f);
            camGO.transform.rotation = Quaternion.Euler(GameplayCameraEuler);
            camGO.transform.position = -camGO.transform.forward * 40f;
            camGO.tag = "MainCamera";

            EditorSceneManager.SaveScene(scene, ShowcaseScenePath);

            Debug.Log("[LowPoly] Showcase scene built at " + ShowcaseScenePath + " with " + placed +
                      " assets, lit and framed at the gameplay camera angle (euler " +
                      GameplayCameraEuler.x + "/" + GameplayCameraEuler.y + "/0, orthographic).");
        }

        // ==================================================================
        // Folders
        // ==================================================================

        private static void EnsureFolders()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            foreach (AssetCategory c in System.Enum.GetValues(typeof(AssetCategory)))
                EnsureFolder(PrefabFolder + "/" + c);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

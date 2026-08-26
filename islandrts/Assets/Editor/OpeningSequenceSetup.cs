using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;

/// <summary>
/// One-shot (idempotent) setup for the opening sequence — the game now starts
/// with a lone survivor wading ashore from a shipwreck and placing the campfire.
///
/// What it does to MainIsland + assets:
///  1. Converts the scene Campfire into a runtime-spawnable prefab: applies the
///     instance's overrides (added BaseBuilding component, NavMeshObstacle carve
///     extents 0.6/0.5/0.6, trigger flags) INTO Campfire.prefab, then deletes
///     the scene instance. The scene-only workerUI reference is recorded first
///     and handed to GameStartController instead (a prefab can't hold it).
///  2. Builds CampfireGhost.prefab (art mesh + one Mat_Ghostbuilding per
///     submesh on the ROOT renderer — same pattern as the other ghosts).
///  3. Builds Survivor.prefab (NavMeshAgent + Survivor script + nested art
///     Worker prefab as a "Model" child, base pivot, scale 1).
///  4. Creates Mat_Water (URP Lit, transparent blue) and an "_Ocean" frame of
///     four water quads overlapping the ground rim — the outer ~6 units of the
///     island read as a shallow wading band. NOT static (water stays real-time).
///  5. Builds a "_Shipwreck" set piece on the west shore from primitives with
///     LP materials + Crate/Barrel/DriftwoodLog art prefabs. No colliders, so
///     it never blocks pathing, clicks, or the NavMesh bake (like scatter props).
///  6. Creates the "GameStart" object wired with everything, clears the (now
///     dangling) GameManager.campfire reference, saves the scene.
///
/// Re-running is safe: _Ocean/_Shipwreck/GameStart are rebuilt from scratch and
/// the campfire conversion step is skipped once the scene instance is gone.
/// </summary>
public static class OpeningSequenceSetup
{
    private const string MenuRoot = "Tools/Island RTS/Opening Sequence/";

    private const string ScenePath = "Assets/MainIsland.unity";
    private const string CampfirePrefabPath = "Assets/Prefabs/Campfire.prefab";
    private const string CampfireGhostPath = "Assets/Prefabs/CampfireGhost.prefab";
    private const string SurvivorPrefabPath = "Assets/Prefabs/Survivor.prefab";

    private const string CampfireArtMeshPath = "Assets/Art/Meshes/Campfire.asset";
    private const string WorkerArtPrefabPath = "Assets/Art/Prefabs/Units/Worker.prefab";
    private const string GhostMaterialPath = "Assets/Materials/Mat_Ghostbuilding.mat";
    private const string WaterMaterialPath = "Assets/Materials/Mat_Water.mat";

    private const string CratePrefabPath = "Assets/Art/Prefabs/Environment/Crate.prefab";
    private const string BarrelPrefabPath = "Assets/Art/Prefabs/Environment/Barrel.prefab";
    private const string DriftwoodPrefabPath = "Assets/Art/Prefabs/Environment/DriftwoodLog.prefab";

    private const string MatWoodDark = "Assets/Art/Materials/LP_WoodDark.mat";
    private const string MatWoodPlank = "Assets/Art/Materials/LP_WoodPlank.mat";
    private const string MatTrunkBark = "Assets/Art/Materials/LP_TrunkBark.mat";
    private const string MatClothCream = "Assets/Art/Materials/LP_ClothCream.mat";

    // Ground is a 100x100 plane centered on origin (edges at ±50). The water
    // frame's inner edge sits at ±44, so the outer 6 units of ground are a
    // shallow wading band; the frame extends to ±72 as open ocean.
    private const float WaterInnerEdge = 44f;
    private const float WaterOuterEdge = 72f;
    private const float WaterY = 0.12f;

    private static readonly Vector3 WreckPosition = new Vector3(-47f, 0f, 3f);
    private static readonly Vector3 SurvivorSpawnPos = new Vector3(-45f, 0f, 2f);

    // Mirror LowPolyScatter's prop flags: batched + GI + occludee, never occluder
    private const StaticEditorFlags PropStaticFlags =
        StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic;

    [MenuItem(MenuRoot + "Setup Opening Scene", false, 10)]
    public static void SetupOpeningScene()
    {
        if (!EnsureSceneOpen()) return;

        StringBuilder summary = new StringBuilder();
        summary.AppendLine("[Opening] Setup pass.");

        WorkerAssignmentUI workerUI = ConvertCampfireToRuntimePrefab(summary);
        BuildCampfireGhostPrefab(summary);
        BuildSurvivorPrefab(summary);
        Material water = EnsureWaterMaterial(summary);
        BuildOcean(water, summary);
        BuildShipwreck(summary);
        BuildController(workerUI, summary);
        ClearGameManagerCampfireRef(summary);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        summary.AppendLine("[Opening] Done. Press Play: right-click moves the survivor ashore, B places the campfire.");
        summary.AppendLine("[Opening] To playtest the classic start instead, tick 'skipIntro' on the GameStart object.");
        Debug.Log(summary.ToString());
    }

    // ------------------------------------------------------------------

    private static bool EnsureSceneOpen()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath) return true;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    /// <summary>
    /// Apply the scene campfire's added components/overrides into Campfire.prefab
    /// so a runtime Instantiate produces a complete campfire, then delete the
    /// scene instance. Returns the workerUI reference the prefab can't carry.
    /// </summary>
    private static WorkerAssignmentUI ConvertCampfireToRuntimePrefab(StringBuilder summary)
    {
        WorkerAssignmentUI ui = null;

        BaseBuilding sceneCampfire = Object.FindAnyObjectByType<BaseBuilding>();
        if (sceneCampfire != null)
        {
            ui = sceneCampfire.workerUI;

            GameObject go = sceneCampfire.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);

                // A scene-object reference can't live in a prefab — null it before
                // applying (GameStartController re-wires it at spawn instead).
                sceneCampfire.workerUI = null;

                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                summary.AppendLine("    Campfire: instance overrides (BaseBuilding, carve extents) applied into " + path);

                Object.DestroyImmediate(root);
                summary.AppendLine("    Campfire: scene instance removed — it is spawned at runtime now");
            }
            else
            {
                Debug.LogError("[Opening] Scene campfire is not a prefab instance — cannot auto-apply its components into Campfire.prefab. Aborting the conversion step.");
            }
        }
        else
        {
            summary.AppendLine("    Campfire: already converted (no BaseBuilding in scene)");
        }

        if (ui == null)
        {
            ui = Object.FindAnyObjectByType<WorkerAssignmentUI>(FindObjectsInactive.Include);
        }
        if (ui == null)
        {
            Debug.LogWarning("[Opening] No WorkerAssignmentUI found — the campfire's assignment panel won't open. Wire GameStart.workerUI by hand.");
        }
        return ui;
    }

    private static void BuildCampfireGhostPrefab(StringBuilder summary)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(CampfireArtMeshPath);
        Material ghostMat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        if (mesh == null || ghostMat == null)
        {
            Debug.LogError("[Opening] Missing " + (mesh == null ? CampfireArtMeshPath : GhostMaterialPath)
                + " — run 'Generate All Assets' first. Ghost prefab not built.");
            return;
        }

        GameObject root = new GameObject("CampfireGhost");
        try
        {
            MeshFilter filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            // One ghost material per submesh, or extras render magenta
            Material[] slots = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < slots.Length; i++) slots[i] = ghostMat;
            renderer.sharedMaterials = slots;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            PrefabUtility.SaveAsPrefabAsset(root, CampfireGhostPath);
            summary.AppendLine("    CampfireGhost.prefab rebuilt (" + slots.Length + " ghost slots)");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void BuildSurvivorPrefab(StringBuilder summary)
    {
        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerArtPrefabPath);
        if (art == null)
        {
            Debug.LogError("[Opening] Art prefab not found: " + WorkerArtPrefabPath + " — run 'Generate All Assets' first. Survivor prefab not built.");
            return;
        }

        GameObject root = new GameObject("Survivor");
        try
        {
            // Agent values mirror Worker.prefab / Worker.Start (Survivor.Start
            // re-asserts the runtime-tunable ones)
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.speed = 3.5f;
            agent.acceleration = 5f;
            agent.angularSpeed = 360f;

            root.AddComponent<Survivor>();

            // Art mounts on a "Model" child as a nested prefab instance (house
            // style: regenerating art propagates automatically)
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(art, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            PrefabUtility.SaveAsPrefabAsset(root, SurvivorPrefabPath);
            summary.AppendLine("    Survivor.prefab rebuilt (agent + Model child <- art Worker)");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    internal static Material EnsureWaterMaterial(StringBuilder summary)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[Opening] URP Lit shader not found — water material not created.");
            return null;
        }

        mat = new Material(shader);
        // URP Lit transparent surface recipe
        mat.SetFloat("_Surface", 1f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        mat.SetColor("_BaseColor", new Color(0.16f, 0.42f, 0.62f, 0.75f));
        mat.SetFloat("_Smoothness", 0.85f);

        AssetDatabase.CreateAsset(mat, WaterMaterialPath);
        summary.AppendLine("    Mat_Water created (URP Lit transparent — placeholder until the Stage 3 water shader)");
        return mat;
    }

    private static void BuildOcean(Material water, StringBuilder summary)
    {
        DestroyExisting("_Ocean");

        // The terrain system replaces the flat-world ocean frame with a real
        // runtime water plane — don't rebuild the frame once terrain exists
        if (Object.FindAnyObjectByType<TerrainGrid>() != null)
        {
            summary.AppendLine("    _Ocean skipped (terrain system present — water is a runtime plane now)");
            return;
        }

        if (water == null) return;

        GameObject root = new GameObject("_Ocean");

        float band = WaterOuterEdge - WaterInnerEdge;          // 28
        float center = WaterInnerEdge + band * 0.5f;           // 58
        float fullSpan = WaterOuterEdge * 2f;                  // 144
        float innerSpan = WaterInnerEdge * 2f;                 // 88

        AddWaterQuad(root, "Water_N", new Vector3(0f, WaterY, center), new Vector2(fullSpan, band), water);
        AddWaterQuad(root, "Water_S", new Vector3(0f, WaterY, -center), new Vector2(fullSpan, band), water);
        AddWaterQuad(root, "Water_E", new Vector3(center, WaterY, 0f), new Vector2(band, innerSpan), water);
        AddWaterQuad(root, "Water_W", new Vector3(-center, WaterY, 0f), new Vector2(band, innerSpan), water);

        summary.AppendLine("    _Ocean rebuilt: 4 quads, inner edge ±" + WaterInnerEdge + " (wading band over the ground rim), y=" + WaterY);
    }

    private static void AddWaterQuad(GameObject parent, string name, Vector3 pos, Vector2 size, Material water)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        // No collider: ground raycasts (build placement, survivor movement) must pass through
        Object.DestroyImmediate(quad.GetComponent<Collider>());

        quad.transform.SetParent(parent.transform, false);
        quad.transform.localPosition = pos;
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = water;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        // Deliberately NOT static: water must stay real-time (Stage 3 mounts the
        // displaced shader here later).
    }

    private static void BuildShipwreck(StringBuilder summary)
    {
        DestroyExisting("_Shipwreck");

        Material woodDark = AssetDatabase.LoadAssetAtPath<Material>(MatWoodDark);
        Material plank = AssetDatabase.LoadAssetAtPath<Material>(MatWoodPlank);
        Material bark = AssetDatabase.LoadAssetAtPath<Material>(MatTrunkBark);
        Material cloth = AssetDatabase.LoadAssetAtPath<Material>(MatClothCream);
        if (woodDark == null || plank == null || bark == null || cloth == null)
        {
            Debug.LogError("[Opening] LP materials missing — run 'Generate All Assets' first. Shipwreck not built.");
            return;
        }

        GameObject wreck = new GameObject("_Shipwreck");
        wreck.transform.position = WreckPosition;
        wreck.transform.rotation = Quaternion.Euler(0f, 20f, 0f);

        // Broken hull half, beached at an angle, keel buried
        AddBlock(wreck, "HullKeel", new Vector3(0f, 0.35f, 0f), new Vector3(4f, 0f, 12f), new Vector3(4.5f, 0.8f, 1.9f), woodDark);
        AddBlock(wreck, "HullSideL", new Vector3(0f, 0.8f, 0.85f), new Vector3(18f, 0f, 10f), new Vector3(4.2f, 0.7f, 0.18f), plank);
        AddBlock(wreck, "HullSideR", new Vector3(0f, 0.8f, -0.85f), new Vector3(-18f, 0f, 10f), new Vector3(4.2f, 0.7f, 0.18f), plank);
        AddBlock(wreck, "BowBroken", new Vector3(3.4f, 0.25f, 0.4f), new Vector3(10f, 35f, 25f), new Vector3(1.6f, 0.6f, 1.4f), woodDark);

        // Fallen mast + draped sail scrap
        AddCylinder(wreck, "Mast", new Vector3(-1.2f, 0.5f, -1.6f), new Vector3(75f, 25f, 0f), new Vector3(0.22f, 1.9f, 0.22f), bark);
        AddBlock(wreck, "SailScrap", new Vector3(-1.6f, 0.28f, -2.4f), new Vector3(6f, 40f, 3f), new Vector3(1.8f, 0.06f, 1.3f), cloth);

        // Washed-up cargo (nested art prefab instances, collider-free by design)
        AddProp(wreck, CratePrefabPath, new Vector3(2.0f, 0f, 2.2f), 30f);
        AddProp(wreck, CratePrefabPath, new Vector3(4.6f, 0f, -0.8f), 70f);
        AddProp(wreck, BarrelPrefabPath, new Vector3(1.1f, 0f, -2.6f), 0f);
        AddProp(wreck, DriftwoodPrefabPath, new Vector3(5.5f, 0f, 1.8f), 100f);
        AddProp(wreck, DriftwoodPrefabPath, new Vector3(-2.8f, 0f, 3.4f), 200f);

        summary.AppendLine("    _Shipwreck rebuilt at " + WreckPosition + " (west shore, half in the wading band)");
    }

    private static void AddBlock(GameObject parent, string name, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material mat)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        FinishPiece(block, parent, name, localPos, localEuler, scale, mat);
    }

    private static void AddCylinder(GameObject parent, string name, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material mat)
    {
        GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        FinishPiece(cyl, parent, name, localPos, localEuler, scale, mat);
    }

    private static void FinishPiece(GameObject piece, GameObject parent, string name, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material mat)
    {
        piece.name = name;
        // No colliders: the wreck must never block pathing or intercept clicks
        Object.DestroyImmediate(piece.GetComponent<Collider>());

        piece.transform.SetParent(parent.transform, false);
        piece.transform.localPosition = localPos;
        piece.transform.localRotation = Quaternion.Euler(localEuler);
        piece.transform.localScale = scale;
        piece.GetComponent<MeshRenderer>().sharedMaterial = mat;

        GameObjectUtility.SetStaticEditorFlags(piece, PropStaticFlags);
    }

    private static void AddProp(GameObject parent, string prefabPath, Vector3 localPos, float yaw)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[Opening] Prop prefab missing, skipped: " + prefabPath);
            return;
        }

        GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
        prop.transform.localPosition = localPos;
        prop.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        GameObjectUtility.SetStaticEditorFlags(prop, PropStaticFlags);
    }

    private static void BuildController(WorkerAssignmentUI workerUI, StringBuilder summary)
    {
        DestroyExisting("GameStart");

        GameObject go = new GameObject("GameStart");
        GameStartController controller = go.AddComponent<GameStartController>();

        controller.campfirePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CampfirePrefabPath);
        controller.campfireGhostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CampfireGhostPath);
        controller.survivorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SurvivorPrefabPath);
        controller.workerUI = workerUI;

        GameObject spawn = new GameObject("SurvivorSpawn");
        spawn.transform.SetParent(go.transform, false);
        spawn.transform.position = SurvivorSpawnPos;
        controller.survivorSpawnPoint = spawn.transform;

        if (controller.campfirePrefab == null) Debug.LogError("[Opening] Campfire prefab missing at " + CampfirePrefabPath);
        if (controller.campfireGhostPrefab == null) Debug.LogError("[Opening] CampfireGhost prefab missing — see errors above");
        if (controller.survivorPrefab == null) Debug.LogError("[Opening] Survivor prefab missing — see errors above");

        summary.AppendLine("    GameStart controller wired (campfire, ghost, survivor, workerUI, spawn point)");
    }

    private static void ClearGameManagerCampfireRef(StringBuilder summary)
    {
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null && gm.campfire == null)
        {
            // The inspector slot may hold a dangling reference to the deleted
            // scene component; write an explicit null so the saved scene is clean.
            gm.campfire = null;
            EditorUtility.SetDirty(gm);
            summary.AppendLine("    GameManager.campfire cleared (assigned at runtime by GameStartController)");
        }
    }

    private static void DestroyExisting(string name)
    {
        GameObject existing;
        while ((existing = GameObject.Find(name)) != null)
        {
            Object.DestroyImmediate(existing);
        }
    }
}

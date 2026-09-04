using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.AI;
using System.Text;

/// <summary>
/// One-shot (idempotent) setup for the 2026-08-26 session content:
///
///  1. Ground pickups — builds Stick.prefab (GroundPickup: wood) and
///     StonePickup.prefab (GroundPickup: stone) from the environment art
///     (DriftwoodLog / Rock_Small as scaled Model children), and creates a
///     wired "_PickupSpawner" object in MainIsland.
///  2. Workshop — builds Workshop.prefab (Workshop + Health-ready collider +
///     carving NavMeshObstacle + art Model child), WorkshopGhost.prefab (art
///     mesh on the ROOT renderer with one ghost material per submesh — the
///     established ghost pattern), creates/updates WorkshopData.asset, and
///     registers it in the scene BuildingDatabase.
///
/// Run AFTER "Low-Poly Templates > Generate All Assets" (it consumes the art
/// library, including the new Workshop building shape). Re-running is safe:
/// prefabs are rebuilt in place (GUIDs survive SaveAsPrefabAsset), the data
/// asset is updated, and scene objects are rebuilt from scratch.
/// </summary>
public static class NewContentSetup
{
    private const string ScenePath = "Assets/MainIsland.unity";

    private const string DriftwoodArtPath = "Assets/Art/Prefabs/Environment/DriftwoodLog.prefab";
    private const string RockSmallArtPath = "Assets/Art/Prefabs/Environment/Rock_Small.prefab";
    private const string WorkshopArtPath = "Assets/Art/Prefabs/Buildings/Workshop.prefab";
    private const string WorkshopMeshPath = "Assets/Art/Meshes/Workshop.asset";
    private const string GhostMaterialPath = "Assets/Materials/Mat_Ghostbuilding.mat";

    private const string StickPrefabPath = "Assets/Prefabs/Stick.prefab";
    private const string StonePickupPrefabPath = "Assets/Prefabs/StonePickup.prefab";
    private const string WorkshopPrefabPath = "Assets/Prefabs/Workshop.prefab";
    private const string WorkshopGhostPrefabPath = "Assets/Prefabs/WorkshopGhost.prefab";

    [MenuItem("Tools/Island RTS/Session Content/Setup Pickups + Workshop", false, 10)]
    public static void Setup()
    {
        if (!EnsureSceneOpen()) return;

        StringBuilder summary = new StringBuilder();
        summary.AppendLine("[Session Content] Pickups + Workshop setup.");

        NamePickupLayer(summary);
        GameObject stickPrefab = BuildPickupPrefab(StickPrefabPath, "Stick",
            ResourceNode.ResourceType.Wood, 3, "stick", DriftwoodArtPath, 0.45f, summary);
        GameObject stonePrefab = BuildPickupPrefab(StonePickupPrefabPath, "StonePickup",
            ResourceNode.ResourceType.Stone, 3, "stone_chunk", RockSmallArtPath, 0.9f, summary);

        GameObject workshopPrefab = BuildWorkshopPrefab(summary);
        GameObject workshopGhost = BuildWorkshopGhost(summary);
        BuildingData workshopData = BuildWorkshopData(workshopPrefab, workshopGhost, summary);
        RegisterInDatabase(workshopData, summary);

        BuildPickupSpawner(stickPrefab, stonePrefab, summary);
        WireResourceSpawner(summary);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        summary.AppendLine("[Session Content] Done. Build mode key 5 = Workshop; pickups spawn at Play.");
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
    /// Point the scene ResourceSpawner at the ore node prefab (created by the
    /// plumber as a copy of RockNode) and write the 2026-09-01 sparse,
    /// terrain-purposed counts. The scene's serialized values are what the
    /// game reads, so a code-default change never reaches the scene without
    /// this — hence it lives in a setup step and re-runs idempotently.
    /// </summary>
    private static void WireResourceSpawner(StringBuilder summary)
    {
        ResourceSpawner spawner = Object.FindAnyObjectByType<ResourceSpawner>();
        if (spawner == null)
        {
            summary.AppendLine("    ResourceSpawner: not found in the scene — ore node NOT wired");
            return;
        }

        GameObject ore = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/OreNode.prefab");
        if (ore == null)
        {
            Debug.LogError("[Session Content] OreNode.prefab missing — run 'Low-Poly Templates > Plumb Everything' first (it creates the ore node from RockNode).");
        }

        SerializedObject so = new SerializedObject(spawner);
        if (ore != null) so.FindProperty("oreNodePrefab").objectReferenceValue = ore;
        so.FindProperty("treeCount").intValue = 150;
        so.FindProperty("berryBushCount").intValue = 60;
        so.FindProperty("rockNodeCount").intValue = 70;
        so.FindProperty("oreNodeCount").intValue = 9;
        so.FindProperty("treeClusters").intValue = 5;
        so.FindProperty("clusterRadius").floatValue = 12f;
        so.FindProperty("minTreeSpacing").floatValue = 2.2f;
        so.FindProperty("minClusterDistFromCampfire").floatValue = 14f;
        so.FindProperty("scatteredTreeCount").intValue = 20;
        so.FindProperty("minScatteredTreeSpacing").floatValue = 6f;
        so.FindProperty("minDistanceBetweenNodes").floatValue = 3.5f;
        so.FindProperty("minDistanceFromCampfire").floatValue = 6f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spawner);

        summary.AppendLine("    ResourceSpawner: " + (ore != null ? "OreNode wired, " : "")
            + "counts set to 150 trees / 60 bushes / 55 rocks / 24 ore (5 forests of r12)");
    }

    /// <summary>
    /// Give GroundPickup.ClickLayer its name in the TagManager so the layer reads
    /// as "Pickups" in the Inspector rather than "User Layer 7". The code uses the
    /// index, so the game works either way; this is for the humans.
    /// </summary>
    private static void NamePickupLayer(StringBuilder summary)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || !layers.isArray) return;

        bool changed = false;
        changed |= NameLayer(layers, GroundPickup.ClickLayer, "Pickups", "GroundPickup.ClickLayer", summary);
        changed |= NameLayer(layers, ResourceNode.ClickLayer, "Nodes", "ResourceNode.ClickLayer", summary);
        if (changed) tagManager.ApplyModifiedProperties();
    }

    /// <summary>Names one TagManager layer slot if it is free; warns and leaves it if another name is there.</summary>
    private static bool NameLayer(SerializedProperty layers, int index, string name, string owner, StringBuilder summary)
    {
        if (layers.arraySize <= index) return false;
        SerializedProperty slot = layers.GetArrayElementAtIndex(index);
        if (slot.stringValue == name) return false;
        if (!string.IsNullOrEmpty(slot.stringValue))
        {
            Debug.LogWarning("[Session Content] Layer " + index + " is already named '" + slot.stringValue
                + "' — leaving it. " + owner + " expects it to be the '" + name + "' click layer.");
            return false;
        }
        slot.stringValue = name;
        summary.AppendLine("    Layer " + index + " named '" + name + "'");
        return true;
    }

    private static GameObject BuildPickupPrefab(string path, string name,
        ResourceNode.ResourceType type, int amount, string itemId, string artPath, float modelScale, StringBuilder summary)
    {
        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(artPath);
        if (art == null)
        {
            Debug.LogError("[Session Content] Art prefab missing: " + artPath
                + " — run 'Low-Poly Templates > Generate All Assets' first.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        GameObject root = new GameObject(name);
        try
        {
            GroundPickup pickup = root.AddComponent<GroundPickup>();
            pickup.resourceType = type;
            pickup.amount = amount;
            pickup.itemId = itemId;      // what the player's character gets (one stick, one chunk)
            pickup.itemAmount = 1;
            // The click collider is added by GroundPickup.Awake at runtime (so scatter
            // and wreck salvage get it too); the layer is set here as well so it reads
            // correctly in the Inspector
            root.layer = GroundPickup.ClickLayer;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(art);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one * modelScale;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            summary.AppendLine("    " + name + ".prefab rebuilt (" + type + " +" + amount + ")");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject BuildWorkshopPrefab(StringBuilder summary)
    {
        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopArtPath);
        if (art == null)
        {
            Debug.LogError("[Session Content] Workshop art missing: " + WorkshopArtPath
                + " — run 'Low-Poly Templates > Generate All Assets' first (it now includes the Workshop shape).");
            return AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopPrefabPath);
        }

        GameObject root = new GameObject("Workshop");
        try
        {
            root.AddComponent<Workshop>();

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 1.5f, 2f);
            box.center = new Vector3(0f, 0.75f, 0f);

            NavMeshObstacle obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(2.2f, 1.65f, 2.2f);
            obstacle.center = new Vector3(0f, 0.825f, 0f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(art);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, WorkshopPrefabPath);
            summary.AppendLine("    Workshop.prefab rebuilt (Workshop + collider 2x1.5x2 + carving obstacle)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject BuildWorkshopGhost(StringBuilder summary)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WorkshopMeshPath);
        Material ghostMat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        if (mesh == null || ghostMat == null)
        {
            Debug.LogError("[Session Content] Workshop mesh or ghost material missing ("
                + WorkshopMeshPath + " / " + GhostMaterialPath + ") — run the art generation first.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopGhostPrefabPath);
        }

        GameObject root = new GameObject("WorkshopGhost");
        try
        {
            // Ghost pattern: art MESH on the ROOT renderer, one translucent ghost
            // material per submesh (see the ghost gotcha in CLAUDE.md)
            MeshFilter mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = root.AddComponent<MeshRenderer>();
            Material[] mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
            mr.sharedMaterials = mats;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, WorkshopGhostPrefabPath);
            summary.AppendLine("    WorkshopGhost.prefab rebuilt (" + mesh.subMeshCount + " ghost material slots)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static BuildingData BuildWorkshopData(GameObject workshopPrefab, GameObject ghostPrefab, StringBuilder summary)
    {
        // Locate the existing BuildingData assets — WorkshopData lives beside
        // them and borrows Hut's construction-site prefab.
        BuildingData hutData = null;
        string dataFolder = "Assets";
        foreach (string guid in AssetDatabase.FindAssets("t:BuildingData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(path);
            if (data != null && data.buildingType == BuildingType.Hut)
            {
                hutData = data;
                dataFolder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                break;
            }
        }

        if (hutData == null)
        {
            Debug.LogError("[Session Content] HutData (BuildingData for Hut) not found — cannot borrow the construction site prefab.");
            return null;
        }

        string workshopDataPath = dataFolder + "/WorkshopData.asset";
        BuildingData workshopData = AssetDatabase.LoadAssetAtPath<BuildingData>(workshopDataPath);
        bool isNew = workshopData == null;
        if (isNew) workshopData = ScriptableObject.CreateInstance<BuildingData>();

        workshopData.buildingType = BuildingType.Workshop;
        workshopData.buildingName = "Workshop";
        workshopData.woodCost = 30;
        workshopData.foodCost = 0;
        workshopData.stoneCost = 20;
        workshopData.ghostPrefab = ghostPrefab;
        workshopData.constructionSitePrefab = hutData.constructionSitePrefab;
        workshopData.finishedBuildingPrefab = workshopPrefab;
        workshopData.buildingSize = new Vector3(2f, 1.5f, 2f);
        workshopData.noBuildRadius = 3.5f;
        workshopData.visualNoBuildRadius = 3.5f;
        workshopData.placementHeight = 0f;
        workshopData.maxHealth = 150f;
        workshopData.blocksNavMesh = false;
        workshopData.isWall = false;

        if (isNew) AssetDatabase.CreateAsset(workshopData, workshopDataPath);
        else EditorUtility.SetDirty(workshopData);

        summary.AppendLine("    WorkshopData.asset " + (isNew ? "created" : "updated")
            + " (30W 20S, HP 150) at " + workshopDataPath);
        return workshopData;
    }

    private static void RegisterInDatabase(BuildingData workshopData, StringBuilder summary)
    {
        if (workshopData == null) return;

        BuildingDatabase db = Object.FindAnyObjectByType<BuildingDatabase>();
        if (db == null)
        {
            Debug.LogError("[Session Content] No BuildingDatabase in the scene — Workshop not registered.");
            return;
        }

        var list = new System.Collections.Generic.List<BuildingData>(db.buildings ?? new BuildingData[0]);
        bool present = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].buildingType == BuildingType.Workshop)
            {
                list[i] = workshopData;
                present = true;
            }
        }
        if (!present) list.Add(workshopData);

        db.buildings = list.ToArray();
        EditorUtility.SetDirty(db);
        summary.AppendLine("    BuildingDatabase: Workshop " + (present ? "refreshed" : "registered")
            + " (" + db.buildings.Length + " entries)");
    }

    private static void BuildPickupSpawner(GameObject stickPrefab, GameObject stonePrefab, StringBuilder summary)
    {
        GameObject existing;
        while ((existing = GameObject.Find("_PickupSpawner")) != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject go = new GameObject("_PickupSpawner");
        PickupSpawner spawner = go.AddComponent<PickupSpawner>();
        spawner.stickPrefab = stickPrefab;
        spawner.stonePrefab = stonePrefab;

        summary.AppendLine("    _PickupSpawner rebuilt (sticks "
            + spawner.stickCount + ", stones " + spawner.stoneCount + ")");
    }
}

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
///     (DriftwoodLog / Stone_Pile as scaled Model children), and creates a
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
    // The stone chunk is a Stone_Pile, not a Rock_Small (2026-09-08): the decor small
    // rocks use Rock_Small, and a pickup that shares the scenery's silhouette gets
    // clicked on by everyone and collected by no one.
    private const string StonePileArtPath = "Assets/Art/Prefabs/Environment/Stone_Pile.prefab";
    private const string WorkshopArtPath = "Assets/Art/Prefabs/Buildings/Workshop.prefab";
    private const string WorkshopMeshPath = "Assets/Art/Meshes/Workshop.asset";
    private const string GhostMaterialPath = "Assets/Materials/Mat_Ghostbuilding.mat";

    private const string StickPrefabPath = "Assets/Prefabs/Stick.prefab";
    private const string StonePickupPrefabPath = "Assets/Prefabs/StonePickup.prefab";
    private const string WorkshopPrefabPath = "Assets/Prefabs/Workshop.prefab";
    private const string WorkshopGhostPrefabPath = "Assets/Prefabs/WorkshopGhost.prefab";

    // The Shipyard and the escape ship (2026-09-04, Slice 6)
    private const string ShipyardArtPath = "Assets/Art/Prefabs/Buildings/Shipyard.prefab";
    private const string ShipyardMeshPath = "Assets/Art/Meshes/Shipyard.asset";
    private const string EscapeShipArtPath = "Assets/Art/Prefabs/Environment/EscapeShip.prefab";
    private const string ShipyardPrefabPath = "Assets/Prefabs/Shipyard.prefab";
    private const string ShipyardGhostPrefabPath = "Assets/Prefabs/ShipyardGhost.prefab";

    // The Storehouse (2026-09-16): a drop-off point with a little stockpile room
    private const string StorehouseArtPath = "Assets/Art/Prefabs/Buildings/Storehouse.prefab";
    private const string StorehouseMeshPath = "Assets/Art/Meshes/Storehouse.asset";
    private const string StorehousePrefabPath = "Assets/Prefabs/Storehouse.prefab";
    private const string StorehouseGhostPrefabPath = "Assets/Prefabs/StorehouseGhost.prefab";

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
            ResourceNode.ResourceType.Stone, 3, "stone_chunk", StonePileArtPath, 1f, summary);

        GameObject workshopPrefab = BuildWorkshopPrefab(summary);
        GameObject workshopGhost = BuildWorkshopGhost(summary);
        BuildingData workshopData = BuildWorkshopData(workshopPrefab, workshopGhost, summary);
        RegisterInDatabase(workshopData, summary);

        // The Shipyard (2026-09-04): same three steps, its own art and a beach rule
        GameObject shipyardPrefab = BuildShipyardPrefab(summary);
        GameObject shipyardGhost = BuildShipyardGhost(summary);
        BuildingData shipyardData = BuildShipyardData(shipyardPrefab, shipyardGhost, summary);
        RegisterInDatabase(shipyardData, summary);

        // The Storehouse (2026-09-16): the Workshop steps with the rack art, key 7
        GameObject storehousePrefab = BuildStorehousePrefab(summary);
        GameObject storehouseGhost = BuildStorehouseGhost(summary);
        BuildingData storehouseData = BuildStorehouseData(storehousePrefab, storehouseGhost, summary);
        RegisterInDatabase(storehouseData, summary);

        BuildPickupSpawner(stickPrefab, stonePrefab, summary);
        WireResourceSpawner(summary);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        summary.AppendLine("[Session Content] Done. Build mode key 5 = Workshop, key 6 = Shipyard (beach only), key 7 = Storehouse; pickups spawn at Play.");
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

    private static void RegisterInDatabase(BuildingData data, StringBuilder summary)
    {
        if (data == null) return;

        BuildingDatabase db = Object.FindAnyObjectByType<BuildingDatabase>();
        if (db == null)
        {
            Debug.LogError("[Session Content] No BuildingDatabase in the scene — " + data.buildingType + " not registered.");
            return;
        }

        var list = new System.Collections.Generic.List<BuildingData>(db.buildings ?? new BuildingData[0]);
        bool present = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].buildingType == data.buildingType)
            {
                list[i] = data;
                present = true;
            }
        }
        if (!present) list.Add(data);

        db.buildings = list.ToArray();
        EditorUtility.SetDirty(db);
        summary.AppendLine("    BuildingDatabase: " + data.buildingType + " " + (present ? "refreshed" : "registered")
            + " (" + db.buildings.Length + " entries)");
    }

    // ------------------------------------------------------------------
    // Shipyard (2026-09-04, Slice 6): the Workshop steps with the slipway art,
    // a wider collider, and the escape ship wired onto the component.
    // ------------------------------------------------------------------

    private static GameObject BuildShipyardPrefab(StringBuilder summary)
    {
        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(ShipyardArtPath);
        GameObject ship = AssetDatabase.LoadAssetAtPath<GameObject>(EscapeShipArtPath);
        if (art == null || ship == null)
        {
            Debug.LogError("[Session Content] Shipyard art missing: " + ShipyardArtPath + " / " + EscapeShipArtPath
                + " — run 'Low-Poly Templates > Generate All Assets' first (it includes the Shipyard and EscapeShip shapes).");
            return AssetDatabase.LoadAssetAtPath<GameObject>(ShipyardPrefabPath);
        }

        GameObject root = new GameObject("Shipyard");
        try
        {
            Shipyard yard = root.AddComponent<Shipyard>();
            yard.shipArtPrefab = ship;

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 1.5f, 2.5f);
            box.center = new Vector3(0f, 0.75f, 0f);

            NavMeshObstacle obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(4.2f, 1.65f, 2.7f);
            obstacle.center = new Vector3(0f, 0.825f, 0f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(art);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;

            int layer = LayerMask.NameToLayer("Buildings");
            if (layer >= 0) root.layer = layer;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ShipyardPrefabPath);
            summary.AppendLine("    Shipyard.prefab rebuilt (Shipyard + collider 4x1.5x2.5 + carving obstacle + EscapeShip art)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject BuildShipyardGhost(StringBuilder summary)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ShipyardMeshPath);
        Material ghostMat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        if (mesh == null || ghostMat == null)
        {
            Debug.LogError("[Session Content] Shipyard mesh or ghost material missing ("
                + ShipyardMeshPath + " / " + GhostMaterialPath + ") — run the art generation first.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(ShipyardGhostPrefabPath);
        }

        GameObject root = new GameObject("ShipyardGhost");
        try
        {
            MeshFilter mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = root.AddComponent<MeshRenderer>();
            Material[] mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
            mr.sharedMaterials = mats;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ShipyardGhostPrefabPath);
            summary.AppendLine("    ShipyardGhost.prefab rebuilt (" + mesh.subMeshCount + " ghost material slots)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------
    // Storehouse (2026-09-16): the Workshop steps with the log-rack art. A
    // 2x2 box collider like the Workshop's (the drop-off ring measures edge
    // distance against it) and a carving obstacle so the approach points
    // land outside it.
    // ------------------------------------------------------------------

    private static GameObject BuildStorehousePrefab(StringBuilder summary)
    {
        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(StorehouseArtPath);
        if (art == null)
        {
            Debug.LogError("[Session Content] Storehouse art missing: " + StorehouseArtPath
                + " — run 'Low-Poly Templates > Generate All Assets' first (it includes the Storehouse shape).");
            return AssetDatabase.LoadAssetAtPath<GameObject>(StorehousePrefabPath);
        }

        GameObject root = new GameObject("Storehouse");
        try
        {
            root.AddComponent<Storehouse>();

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 1.4f, 2f);
            box.center = new Vector3(0f, 0.7f, 0f);

            NavMeshObstacle obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(2.2f, 1.5f, 2.2f);
            obstacle.center = new Vector3(0f, 0.75f, 0f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(art);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StorehousePrefabPath);
            summary.AppendLine("    Storehouse.prefab rebuilt (Storehouse + collider 2x1.4x2 + carving obstacle)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject BuildStorehouseGhost(StringBuilder summary)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(StorehouseMeshPath);
        Material ghostMat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        if (mesh == null || ghostMat == null)
        {
            Debug.LogError("[Session Content] Storehouse mesh or ghost material missing ("
                + StorehouseMeshPath + " / " + GhostMaterialPath + ") — run the art generation first.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(StorehouseGhostPrefabPath);
        }

        GameObject root = new GameObject("StorehouseGhost");
        try
        {
            MeshFilter mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = root.AddComponent<MeshRenderer>();
            Material[] mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
            mr.sharedMaterials = mats;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StorehouseGhostPrefabPath);
            summary.AppendLine("    StorehouseGhost.prefab rebuilt (" + mesh.subMeshCount + " ghost material slots)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static BuildingData BuildStorehouseData(GameObject storehousePrefab, GameObject ghostPrefab, StringBuilder summary)
    {
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
            Debug.LogError("[Session Content] HutData not found — cannot borrow the construction site prefab for the Storehouse.");
            return null;
        }

        string dataPath = dataFolder + "/StorehouseData.asset";
        BuildingData storeData = AssetDatabase.LoadAssetAtPath<BuildingData>(dataPath);
        bool isNew = storeData == null;
        if (isNew) storeData = ScriptableObject.CreateInstance<BuildingData>();

        storeData.buildingType = BuildingType.Storehouse;
        storeData.buildingName = "Storehouse";
        storeData.woodCost = 15;
        storeData.foodCost = 0;
        storeData.stoneCost = 10;
        storeData.metalCost = 0;
        storeData.ghostPrefab = ghostPrefab;
        storeData.constructionSitePrefab = hutData.constructionSitePrefab;
        storeData.finishedBuildingPrefab = storehousePrefab;
        storeData.buildingSize = new Vector3(2f, 1.4f, 2f);
        storeData.noBuildRadius = 3f;
        storeData.visualNoBuildRadius = 3f;
        storeData.placementHeight = 0f;
        storeData.maxHealth = 120f;
        storeData.blocksNavMesh = false;
        storeData.isWall = false;
        storeData.requiresShore = false;
        storeData.buildTimeOverride = 0f;   // the hut site's own time: a quick build

        if (isNew) AssetDatabase.CreateAsset(storeData, dataPath);
        else EditorUtility.SetDirty(storeData);

        summary.AppendLine("    StorehouseData.asset " + (isNew ? "created" : "updated")
            + " (15W 10S, HP 120) at " + dataPath);
        return storeData;
    }

    private static BuildingData BuildShipyardData(GameObject shipyardPrefab, GameObject ghostPrefab, StringBuilder summary)
    {
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
            Debug.LogError("[Session Content] HutData not found — cannot borrow the construction site prefab for the Shipyard.");
            return null;
        }

        string dataPath = dataFolder + "/ShipyardData.asset";
        BuildingData yardData = AssetDatabase.LoadAssetAtPath<BuildingData>(dataPath);
        bool isNew = yardData == null;
        if (isNew) yardData = ScriptableObject.CreateInstance<BuildingData>();

        yardData.buildingType = BuildingType.Shipyard;
        yardData.buildingName = "Shipyard";
        yardData.woodCost = 200;
        yardData.foodCost = 0;
        yardData.stoneCost = 120;
        yardData.metalCost = 30;
        yardData.ghostPrefab = ghostPrefab;
        yardData.constructionSitePrefab = hutData.constructionSitePrefab;
        yardData.finishedBuildingPrefab = shipyardPrefab;
        yardData.buildingSize = new Vector3(4f, 1.5f, 2.5f);
        yardData.noBuildRadius = 5f;
        yardData.visualNoBuildRadius = 5f;
        yardData.placementHeight = 0f;
        yardData.maxHealth = 300f;
        yardData.blocksNavMesh = false;
        yardData.isWall = false;
        yardData.requiresShore = true;
        yardData.buildTimeOverride = 45f;   // × LaborFactor 2 = 90 s alone, ~30 s with three builders

        if (isNew) AssetDatabase.CreateAsset(yardData, dataPath);
        else EditorUtility.SetDirty(yardData);

        summary.AppendLine("    ShipyardData.asset " + (isNew ? "created" : "updated")
            + " (200W 120S 30M, HP 300, shore only, 45 s) at " + dataPath);
        return yardData;
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

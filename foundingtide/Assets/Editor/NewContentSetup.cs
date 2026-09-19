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

    // The Tent (2026-09-18): housing tier 1, which the Hut is now the upgrade of
    private const string TentArtPath = "Assets/Art/Prefabs/Buildings/Tent.prefab";
    private const string TentMeshPath = "Assets/Art/Meshes/Tent.asset";
    private const string TentPrefabPath = "Assets/Prefabs/Tent.prefab";
    private const string TentGhostPrefabPath = "Assets/Prefabs/TentGhost.prefab";

    [MenuItem("Tools/Founding Tide/Session Content/Setup Pickups + Workshop", false, 10)]
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

        // The Tent (2026-09-18): housing tier 1, key 1. Built before the tier pass so
        // TentData exists for the Hut to be pointed at.
        GameObject tentPrefab = BuildTentPrefab(summary);
        GameObject tentGhost = BuildTentGhost(summary);
        BuildingData tentData = BuildTentData(tentPrefab, tentGhost, summary);
        RegisterInDatabase(tentData, summary);

        // Every BuildingData's tier, category and research gate. Runs LAST and over
        // ALL of them: these fields were added on 2026-09-18, and an asset saved before
        // that has no YAML key for them, so they deserialize as 0/false - which would
        // make every existing building unplaceable and tier 0 rather than take the C#
        // initializer. Stamping them here is what upgrades the assets on disk.
        ApplyTierData(summary);

        BuildPickupSpawner(stickPrefab, stonePrefab, summary);
        WireResourceSpawner(summary);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        summary.AppendLine("[Session Content] Done. Build mode key 1 = Tent (upgrades to a Hut), key 5 = Workshop, key 6 = Shipyard (beach only), key 7 = Storehouse; pickups spawn at Play.");
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

    // ------------------------------------------------------------------
    // The Tent (2026-09-18): housing tier 1. It carries the SAME Hut component
    // as the tier it upgrades into - every consumer (Population, the raider
    // target order, Prosperity, Siege, the minimap) wants "any housing", which
    // is what Hut.ActiveList already means - and differs only in its serialized
    // capacity, health and buildingType. The footprint matches the Hut's 2x2 on
    // purpose, so upgrading keeps the pad, the collider bounds and the carve.
    // ------------------------------------------------------------------

    private static GameObject BuildTentPrefab(StringBuilder summary)
    {
        // The plumber (step 2) is the single owner of a building's art, collider, carve
        // volume and health bar, and it BIRTHS Tent.prefab as a copy of the Hut. So the
        // normal path here is a retune of what it produced, not a rebuild - a rebuild from
        // scratch would drop the HealthBar the copy inherited and re-mount the art behind
        // the plumber's back, which is exactly the kind of two-owner drift that bites later.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TentPrefabPath) != null)
            return RetuneTentPrefab(summary);

        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(TentArtPath);
        if (art == null)
        {
            Debug.LogError("[Session Content] Tent art missing: " + TentArtPath
                + " — run 'Low-Poly Templates > Generate All Assets' first (it includes the Tent shape).");
            return null;
        }

        // Fallback only: the plumber never ran, or its entry was skipped. Build the whole
        // thing here, HealthBar included, so the Tent is never left without one.
        GameObject root = new GameObject("Tent");
        try
        {
            Hut housing = root.AddComponent<Hut>();
            housing.buildingType = BuildingType.Tent;
            housing.workerCapacity = 1;      // half a hut's beds, for half the wood
            housing.maxHealth = 50f;
            housing.noBuildRadius = 3.5f;

            HealthBar bar = root.AddComponent<HealthBar>();
            bar.heightOffset = 2.1f;         // the plumber's Tent values, kept in step
            bar.barWidth = 2f;
            bar.barHeight = 0.2f;

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 1.6f, 2f);
            box.center = new Vector3(0f, 0.8f, 0f);

            NavMeshObstacle obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(2.2f, 1.6f, 2.2f);
            obstacle.center = new Vector3(0f, 0.8f, 0f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(art);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TentPrefabPath);
            summary.AppendLine("    Tent.prefab BUILT from scratch — the plumber had not made one "
                + "(Hut component, HealthBar, 1 bed, HP 50, collider 2x1.6x2 + carving obstacle)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Sets the Tent-only gameplay numbers on the prefab the plumber copied off the Hut.
    /// Everything visual (art, collider, carve, bar) is the plumber's and is left alone.
    /// </summary>
    private static GameObject RetuneTentPrefab(StringBuilder summary)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(TentPrefabPath);
        try
        {
            Hut housing = root.GetComponent<Hut>();
            if (housing == null)
            {
                Debug.LogError("[Session Content] " + TentPrefabPath + " has no Hut component — "
                    + "delete it and re-run Setup Everything so the plumber rebuilds it from the Hut.");
                return AssetDatabase.LoadAssetAtPath<GameObject>(TentPrefabPath);
            }

            housing.buildingType = BuildingType.Tent;
            housing.workerCapacity = 1;      // half a hut's beds, for half the wood
            housing.maxHealth = 50f;
            housing.noBuildRadius = 3.5f;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TentPrefabPath);
            summary.AppendLine("    Tent.prefab retuned (1 bed, HP 50, no-build 3.5; art and bar left to the plumber)");
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject BuildTentGhost(StringBuilder summary)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TentMeshPath);
        Material ghostMat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        if (mesh == null || ghostMat == null)
        {
            Debug.LogError("[Session Content] Tent mesh or ghost material missing ("
                + TentMeshPath + " / " + GhostMaterialPath + ") — run the art generation first.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(TentGhostPrefabPath);
        }

        GameObject root = new GameObject("TentGhost");
        try
        {
            MeshFilter mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = root.AddComponent<MeshRenderer>();
            Material[] mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
            mr.sharedMaterials = mats;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TentGhostPrefabPath);
            summary.AppendLine("    TentGhost.prefab rebuilt (" + mesh.subMeshCount + " ghost material slots)");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static BuildingData BuildTentData(GameObject tentPrefab, GameObject ghostPrefab, StringBuilder summary)
    {
        string dataFolder;
        BuildingData hutData = FindBuildingData(BuildingType.Hut, out dataFolder);
        if (hutData == null)
        {
            Debug.LogError("[Session Content] HutData not found — cannot borrow the construction site prefab for the Tent.");
            return null;
        }

        string dataPath = dataFolder + "/TentData.asset";
        BuildingData tentData = AssetDatabase.LoadAssetAtPath<BuildingData>(dataPath);
        bool isNew = tentData == null;
        if (isNew) tentData = ScriptableObject.CreateInstance<BuildingData>();

        tentData.buildingType = BuildingType.Tent;
        tentData.buildingName = "Tent";
        tentData.description = "A sailcloth shelter for one. Cheap and quick; upgrade it to a hut when there is wood to spare.";
        tentData.category = BuildCategory.Housing;
        tentData.woodCost = 10;
        tentData.foodCost = 0;
        tentData.stoneCost = 0;
        tentData.metalCost = 0;
        tentData.ghostPrefab = ghostPrefab;
        tentData.constructionSitePrefab = hutData.constructionSitePrefab;
        tentData.finishedBuildingPrefab = tentPrefab;
        tentData.buildingSize = new Vector3(2f, 1.6f, 2f);
        tentData.noBuildRadius = 3.5f;
        tentData.visualNoBuildRadius = 3.5f;
        tentData.placementHeight = 0f;
        tentData.maxHealth = 50f;
        tentData.blocksNavMesh = false;
        tentData.isWall = false;
        tentData.requiresShore = false;
        tentData.buildTimeOverride = 4f;    // quicker than a hut: it is canvas over two poles
        tentData.tier = 1;
        tentData.placeable = true;
        tentData.upgradesTo = hutData;
        tentData.requiresUnlock = false;

        if (isNew) AssetDatabase.CreateAsset(tentData, dataPath);
        else EditorUtility.SetDirty(tentData);

        summary.AppendLine("    TentData.asset " + (isNew ? "created" : "updated")
            + " (10W, 1 bed, HP 50, upgrades to Hut) at " + dataPath);
        return tentData;
    }

    /// <summary>Find a BuildingData asset by type, and the folder they all live in.</summary>
    private static BuildingData FindBuildingData(BuildingType type, out string dataFolder)
    {
        dataFolder = "Assets";
        foreach (string guid in AssetDatabase.FindAssets("t:BuildingData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(path);
            if (data != null && data.buildingType == type)
            {
                dataFolder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                return data;
            }
        }
        return null;
    }

    /// <summary>
    /// Stamp the 2026-09-18 tier / category / research-gate fields onto EVERY
    /// BuildingData asset.
    ///
    /// This is not tidying. Those fields did not exist when these assets were last
    /// written, so their YAML has no key for them — and a missing key deserializes as
    /// 0/false, never as the C# initializer. Without this pass every existing building
    /// comes back placeable = false at tier 0, and the build palette is empty.
    ///
    /// The research gates moved here from three hardcoded ifs in
    /// BuildPlacement.SelectBuilding, so this is now the one place they are set.
    /// </summary>
    private static void ApplyTierData(StringBuilder summary)
    {
        string unused;
        BuildingData hut = FindBuildingData(BuildingType.Hut, out unused);
        int touched = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:BuildingData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BuildingData d = AssetDatabase.LoadAssetAtPath<BuildingData>(path);
            if (d == null) continue;

            // Defaults for anything not named below: a placeable, ungated top tier.
            d.tier = 1;
            d.placeable = true;
            d.upgradesTo = null;
            d.requiresUnlock = false;
            d.category = BuildCategory.Production;

            switch (d.buildingType)
            {
                case BuildingType.Tent:
                    d.category = BuildCategory.Housing;
                    d.upgradesTo = hut;
                    if (string.IsNullOrEmpty(d.description))
                        d.description = "A sailcloth shelter for one. Cheap and quick; upgrade it to a hut when there is wood to spare.";
                    break;

                case BuildingType.Hut:
                    // Tier 2, and reached ONLY by upgrading a Tent (2026-09-18).
                    d.category = BuildCategory.Housing;
                    d.tier = 2;
                    d.placeable = false;
                    d.description = "Two beds behind timber walls. Upgraded from a tent, and far harder for a raider to break.";
                    break;

                case BuildingType.WoodenWall:
                    d.category = BuildCategory.Defence;
                    d.description = "A palisade line. Raiders must chew through it or find the gate.";
                    break;

                case BuildingType.StoneWall:
                    d.category = BuildCategory.Defence;
                    d.description = "Twice a palisade's health, for stone instead of speed.";
                    break;

                case BuildingType.Watchtower:
                    d.category = BuildCategory.Defence;
                    d.description = "Sees far. Vision only, until the archer post is built.";
                    break;

                case BuildingType.Workshop:
                    d.category = BuildCategory.Production;
                    d.requiresUnlock = true;
                    d.requiredUnlock = Unlocks.Kind.Crafting;
                    d.description = "A second bench. Makes tools and weapons at double speed.";
                    break;

                case BuildingType.Storehouse:
                    d.category = BuildCategory.Production;
                    d.requiresUnlock = true;
                    d.requiredUnlock = Unlocks.Kind.Storage;
                    d.description = "A drop-off out among the work, and twenty more room in the stockpile.";
                    break;

                case BuildingType.Shipyard:
                    d.category = BuildCategory.Special;
                    d.requiresUnlock = true;
                    d.requiredUnlock = Unlocks.Kind.Shipwright;
                    d.description = "The way off the island. Beach only, and it takes a long time.";
                    break;
            }

            EditorUtility.SetDirty(d);
            touched++;
        }

        summary.AppendLine("    Tier data stamped on " + touched
            + " BuildingData assets (Hut is now tier 2 and not placeable; research gates read from the asset)");
    }
}

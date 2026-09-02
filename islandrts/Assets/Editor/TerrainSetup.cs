using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using System.Text;
using IslandRTS.ArtGen;

/// <summary>
/// One-shot (idempotent) scene setup for the terrain system — replaces the
/// flat Ground plane with the runtime-generated island.
///
/// What it does to MainIsland:
///  1. Deletes the "Ground" plane (its NavMeshSurface + baked data go with
///     it) and the baked NavMesh-Ground.asset — leaving either would union a
///     ghost of the old flat world into the runtime NavMesh.
///  2. Deletes the "_Ocean" quad frame from the opening-sequence setup — the
///     terrain spawns a real water plane at sea level instead.
///  3. Deletes the legacy edit-time "_LowPolyScatter" decor — props are
///     placed at runtime now (PropScatter), because the island is random.
///  4. Ensures the IslandSettings and ScatterSettings assets exist under
///     Assets/Settings (created from code defaults, never overwritten once
///     they exist — they are the tuning surface).
///  5. Creates a "Terrain" object with TerrainGrid + PropScatter + a
///     NavMeshSurface (children-only, physics colliders), wired with one LP
///     material per TerrainGrid.Surface band and Mat_Water.
///  6. Snaps the _Shipwreck root onto the landing-cove shelf. The cove is an
///     authored anchor at a fixed height on EVERY seed, so this is
///     seed-independent.
///
/// Run AFTER the Opening Sequence setup. Re-running is safe.
/// </summary>
public static class TerrainSetup
{
    private const string ScenePath = "Assets/MainIsland.unity";
    private const string BakedNavMeshPath = "Assets/MainIsland/NavMesh-Ground.asset";
    private const string WaterMaterialPath = "Assets/Materials/Mat_Water.mat";
    private const string MaterialFolder = "Assets/Art/Materials/";
    public const string IslandSettingsPath = "Assets/Settings/IslandSettings.asset";

    /// <summary>LP material key per TerrainGrid.Surface, in enum order.</summary>
    private static readonly string[] SurfaceMaterialKeys =
    {
        "SandWet",     // Surface.SandWet
        "Sand",        // Surface.Sand
        "GrassGreen",  // Surface.GrassGreen
        "GrassDark",   // Surface.GrassDark
        "GrassDry",    // Surface.GrassDry
        "RockMid",     // Surface.RockMid
        "RockDark",    // Surface.RockDark
    };

    [MenuItem("Tools/Island RTS/Terrain/Setup Terrain Scene", false, 10)]
    public static void SetupTerrainScene()
    {
        if (!EnsureSceneOpen()) return;

        StringBuilder summary = new StringBuilder();
        summary.AppendLine("[Terrain] setup pass.");

        RemoveFlatWorld(summary);
        IslandSettings island = EnsureIslandSettings(summary);
        ScatterSettings scatter = LowPolyScatter.EnsureSettingsAsset();
        TerrainGrid grid = CreateTerrainObject(island, scatter, summary);
        if (grid != null)
        {
            SnapShipwreck(island, summary);
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        summary.AppendLine("[Terrain] Done. Press Play: a random island generates at load (restart replays the same one) and the NavMesh builds at runtime.");
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

    private static void RemoveFlatWorld(StringBuilder summary)
    {
        // The Ground plane carries the old NavMeshSurface component — both go
        GameObject ground = GameObject.Find("Ground");
        if (ground != null)
        {
            Object.DestroyImmediate(ground);
            summary.AppendLine("    Ground plane removed (terrain chunks replace it)");
        }
        else
        {
            summary.AppendLine("    Ground: already removed");
        }

        if (AssetDatabase.LoadAssetAtPath<Object>(BakedNavMeshPath) != null)
        {
            AssetDatabase.DeleteAsset(BakedNavMeshPath);
            summary.AppendLine("    Baked NavMesh-Ground.asset deleted (runtime NavMeshSurface replaces it — leaving it would double-navmesh)");
        }

        GameObject ocean = GameObject.Find("_Ocean");
        if (ocean != null)
        {
            Object.DestroyImmediate(ocean);
            summary.AppendLine("    _Ocean quad frame removed (real water plane spawns at runtime)");
        }

        if (LowPolyScatter.HasLegacyScatter())
        {
            LowPolyScatter.Clear();
            summary.AppendLine("    _LowPolyScatter removed (props are scattered at runtime by PropScatter now)");
        }
    }

    private static IslandSettings EnsureIslandSettings(StringBuilder summary)
    {
        IslandSettings asset = AssetDatabase.LoadAssetAtPath<IslandSettings>(IslandSettingsPath);
        if (asset != null)
        {
            if (asset.version >= IslandSettings.CurrentVersion)
            {
                summary.AppendLine("    IslandSettings: existing asset kept (v" + asset.version + ", " + IslandSettingsPath + ")");
                return asset;
            }

            // The code defaults moved on (a tuning pass): refresh the asset in
            // place so the GUID and every scene reference survive
            int old = asset.version;
            IslandSettings fresh = IslandSettings.CreateDefault();
            EditorUtility.CopySerialized(fresh, asset);
            asset.name = "IslandSettings";
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            summary.AppendLine("    IslandSettings: asset refreshed from code defaults (v" + old + " → v"
                + IslandSettings.CurrentVersion + "); any hand tuning on it was replaced");
            return asset;
        }

        asset = IslandSettings.CreateDefault();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(IslandSettingsPath));
        AssetDatabase.CreateAsset(asset, IslandSettingsPath);
        AssetDatabase.SaveAssets();
        summary.AppendLine("    IslandSettings created from code defaults at " + IslandSettingsPath);
        return asset;
    }

    private static TerrainGrid CreateTerrainObject(IslandSettings island, ScatterSettings scatter, StringBuilder summary)
    {
        // Carry the previous seed across a re-run so a tuned fixed-seed
        // scene keeps its number
        int previousSeed = 0;
        bool previousRandomize = true;
        GameObject existing;
        while ((existing = GameObject.Find("Terrain")) != null)
        {
            TerrainGrid old = existing.GetComponent<TerrainGrid>();
            if (old != null) { previousSeed = old.seed; previousRandomize = old.randomizeSeed; }
            Object.DestroyImmediate(existing);
        }

        Material[] mats = new Material[SurfaceMaterialKeys.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            mats[i] = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "LP_" + SurfaceMaterialKeys[i] + ".mat");
            if (mats[i] == null)
            {
                Debug.LogError("[Terrain] LP band material missing: LP_" + SurfaceMaterialKeys[i]
                    + ".mat — run 'Low-Poly Templates > Generate All Assets' first. Terrain object not created.");
                return null;
            }
        }
        if (mats.Length != TerrainGrid.SurfaceCount)
        {
            Debug.LogError("[Terrain] SurfaceMaterialKeys has " + mats.Length + " entries but TerrainGrid.Surface has "
                + TerrainGrid.SurfaceCount + " — add the new band's material key to TerrainSetup.");
            return null;
        }

        Material water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (water == null)
        {
            water = OpeningSequenceSetup.EnsureWaterMaterial(summary);
        }
        else if (OpeningSequenceSetup.ApplyWaterLook(water))
        {
            summary.AppendLine("    Mat_Water switched to " + water.shader.name);
        }

        GameObject go = new GameObject("Terrain");
        TerrainGrid grid = go.AddComponent<TerrainGrid>();
        grid.settings = island;
        grid.surfaceMaterials = mats;
        grid.waterMaterial = water;
        grid.randomizeSeed = previousRandomize;
        if (previousSeed != 0) grid.seed = previousSeed;

        PropScatter props = go.AddComponent<PropScatter>();
        props.settings = scatter;

        // TerrainGrid re-asserts these at runtime; setting them here keeps
        // the Inspector honest
        NavMeshSurface surface = go.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        summary.AppendLine("    Terrain object created: TerrainGrid (" + mats.Length + " band materials, "
            + (grid.randomizeSeed ? "random seed per run" : "fixed seed " + grid.seed) + ") + PropScatter ("
            + (scatter != null ? scatter.rules.Length + " rules" : "NO SETTINGS") + ") + NavMeshSurface");
        return grid;
    }

    /// <summary>
    /// Move the _Shipwreck ROOT onto the cove shelf. The cove disc is
    /// flattened to the same height on every seed, so the fixed seed is as
    /// good as any — authored child offsets (hull tilt, mast, cargo) ride
    /// along. Idempotent: writes an absolute y.
    /// </summary>
    private static void SnapShipwreck(IslandSettings island, StringBuilder summary)
    {
        GameObject wreck = GameObject.Find("_Shipwreck");
        if (wreck == null)
        {
            summary.AppendLine("    _Shipwreck not found (run the Opening Sequence setup first)");
            return;
        }

        IslandField field = IslandGenerator.Generate(TerrainGrid.VertsPerSide, TerrainGrid.Spacing, 20260825, island);
        Vector3 p = wreck.transform.position;
        p.y = TerrainGrid.SampleField(field.heights, p.x, p.z);
        wreck.transform.position = p;
        summary.AppendLine("    _Shipwreck root snapped to the landing cove (y=" + p.y.ToString("F2") + ")");
    }
}

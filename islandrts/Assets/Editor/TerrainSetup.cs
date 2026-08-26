using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using System.Text;

/// <summary>
/// One-shot (idempotent) scene setup for Terrain System T1 — replaces the
/// flat Ground plane with the runtime-generated island.
///
/// What it does to MainIsland:
///  1. Deletes the "Ground" plane (its NavMeshSurface + baked data go with
///     it) and the baked NavMesh-Ground.asset — leaving either would union a
///     ghost of the old flat world into the runtime NavMesh.
///  2. Deletes the "_Ocean" quad frame from the opening-sequence setup — the
///     terrain spawns a real 320×320 water plane at sea level instead.
///  3. Creates a "Terrain" object with TerrainGrid + a NavMeshSurface
///     (children-only, physics colliders), wired with the LP band materials
///     and Mat_Water.
///  4. Snaps scene props to the generated ground: the _Shipwreck root drops
///     onto the landing-cove shelf, and every _LowPolyScatter prop lands on
///     the island surface (props that end up underwater are deleted). This
///     works in-editor because IslandGenerator is pure/deterministic — the
///     tool generates the same heightfield the game will.
///
/// Run AFTER the Opening Sequence setup. Re-running is safe: Terrain is
/// rebuilt, prop snapping is absolute (y = sampled height), and deletion
/// steps skip when already done.
/// </summary>
public static class TerrainSetup
{
    private const string ScenePath = "Assets/MainIsland.unity";
    private const string BakedNavMeshPath = "Assets/MainIsland/NavMesh-Ground.asset";

    private const string SandMaterialPath = "Assets/Art/Materials/LP_Sand.mat";
    private const string GrassMaterialPath = "Assets/Art/Materials/LP_GrassGreen.mat";
    private const string RockMaterialPath = "Assets/Art/Materials/LP_RockMid.mat";
    private const string WaterMaterialPath = "Assets/Materials/Mat_Water.mat";

    [MenuItem("Tools/Island RTS/Terrain/Setup Terrain Scene (T1)", false, 10)]
    public static void SetupTerrainScene()
    {
        if (!EnsureSceneOpen()) return;

        StringBuilder summary = new StringBuilder();
        summary.AppendLine("[Terrain] T1 setup pass.");

        RemoveFlatWorld(summary);
        TerrainGrid grid = CreateTerrainObject(summary);
        if (grid != null)
        {
            SnapScenePropsToTerrain(grid.seed, summary);
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        summary.AppendLine("[Terrain] Done. Press Play: the island generates at load (fixed seed) and the NavMesh builds at runtime.");
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
    }

    private static TerrainGrid CreateTerrainObject(StringBuilder summary)
    {
        GameObject existing;
        while ((existing = GameObject.Find("Terrain")) != null)
        {
            Object.DestroyImmediate(existing);
        }

        Material sand = AssetDatabase.LoadAssetAtPath<Material>(SandMaterialPath);
        Material grass = AssetDatabase.LoadAssetAtPath<Material>(GrassMaterialPath);
        Material rock = AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath);
        if (sand == null || grass == null || rock == null)
        {
            Debug.LogError("[Terrain] LP band materials missing (LP_Sand / LP_GrassGreen / LP_RockMid) — run 'Low-Poly Templates > Generate All Assets' first. Terrain object not created.");
            return null;
        }

        Material water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (water == null)
        {
            water = OpeningSequenceSetup.EnsureWaterMaterial(summary);
        }

        GameObject go = new GameObject("Terrain");
        TerrainGrid grid = go.AddComponent<TerrainGrid>();
        grid.sandMaterial = sand;
        grid.grassMaterial = grass;
        grid.rockMaterial = rock;
        grid.waterMaterial = water;

        // TerrainGrid re-asserts these at runtime; setting them here keeps
        // the Inspector honest
        NavMeshSurface surface = go.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        summary.AppendLine("    Terrain object created: TerrainGrid (seed " + grid.seed + ") + NavMeshSurface (children / physics colliders)");
        return grid;
    }

    /// <summary>
    /// Generate the same heightfield the game will and snap scene props onto
    /// it. Idempotent: every snap writes an absolute y from the field.
    /// </summary>
    private static void SnapScenePropsToTerrain(int seed, StringBuilder summary)
    {
        float[,] heights = IslandGenerator.Generate(TerrainGrid.VertsPerSide, TerrainGrid.Spacing, seed);

        // Shipwreck: move the ROOT onto the cove shelf; authored child
        // offsets (hull tilt, mast, cargo) ride along
        GameObject wreck = GameObject.Find("_Shipwreck");
        if (wreck != null)
        {
            Vector3 p = wreck.transform.position;
            p.y = TerrainGrid.SampleField(heights, p.x, p.z);
            wreck.transform.position = p;
            summary.AppendLine("    _Shipwreck root snapped to the landing cove (y=" + p.y.ToString("F2") + ")");
        }

        // Scatter props: each prop sits exactly on the ground (base-pivot
        // art); anything that lands underwater is deleted
        GameObject scatter = GameObject.Find("_LowPolyScatter");
        if (scatter != null)
        {
            int snapped = 0, drowned = 0;
            for (int i = scatter.transform.childCount - 1; i >= 0; i--)
            {
                Transform prop = scatter.transform.GetChild(i);
                Vector3 p = prop.position;
                float h = TerrainGrid.SampleField(heights, p.x, p.z);
                if (h < 0.05f)
                {
                    Object.DestroyImmediate(prop.gameObject);
                    drowned++;
                    continue;
                }
                p.y = h;
                prop.position = p;
                snapped++;
            }
            summary.AppendLine("    _LowPolyScatter: " + snapped + " props snapped to terrain, " + drowned + " underwater props removed");
        }
        else
        {
            summary.AppendLine("    _LowPolyScatter not found (run 'Scatter Environment Props' first if you want props re-snapped — safe to re-run this tool after)");
        }
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Editor front end for the balance simulation: write a starter sweep, run one
/// inside the editor for debugging, or build the headless player that actually
/// chews through a sweep at speed.
///
/// The build here deliberately does NOT read EditorBuildSettings — that scene
/// list still points at the leftover SampleScene, and fixing it is a project
/// decision, not this tool's business. It passes MainIsland explicitly instead.
/// </summary>
public static class SimTools
{
    private const string Scene = "Assets/MainIsland.unity";
    private const string BuildDir = "Build/SimPlayer";
    private const string PlayerName = "islandrts-sim.exe";

    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    // ---- sweep authoring --------------------------------------------------

    [MenuItem("Tools/Island RTS/Simulation/Write Example Sweep", priority = 200)]
    public static void WriteExampleSweep()
    {
        string path = Path.Combine(ProjectRoot, "SimSweeps", "example.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        SimSweep sweep = new SimSweep
        {
            outputDir = "SimLogs",
            repeats = 3,
            runs = new List<SimConfig>
            {
                new SimConfig { id = "turtle_baseline", strategy = "Turtle", seed = 1 },
                new SimConfig { id = "rush_baseline",   strategy = "Rush",   seed = 1 },
                new SimConfig { id = "eco_baseline",    strategy = "Eco",    seed = 1 },

                // A first knob sweep: is the raid curve the thing that decides runs?
                new SimConfig { id = "eco_raids_often", strategy = "Eco", seed = 1, raidChancePerQuietDay = 0.35f },
                new SimConfig { id = "eco_raids_big",   strategy = "Eco", seed = 1, raidSizePerDay = 0.6f },
                new SimConfig { id = "eco_rich_target", strategy = "Eco", seed = 1, raidSizePerProsperity = 0.14f },
            }
        };

        File.WriteAllText(path, JsonUtility.ToJson(sweep, true));
        Debug.Log($"[Sim] Example sweep written to {path}");
        EditorUtility.RevealInFinder(path);
    }

    // ---- in-editor debugging ---------------------------------------------

    [MenuItem("Tools/Island RTS/Simulation/Run Sweep In Editor…", priority = 201)]
    public static void RunSweepInEditor()
    {
        string start = Path.Combine(ProjectRoot, "SimSweeps");
        if (!Directory.Exists(start)) start = ProjectRoot;

        string path = EditorUtility.OpenFilePanel("Select sweep JSON", start, "json");
        if (string.IsNullOrEmpty(path)) return;

        if (EditorSceneOpen() == false) return;

        SimRunner.QueuedSweepPath = path;
        EditorApplication.isPlaying = true;
        Debug.Log($"[Sim] Queued sweep {Path.GetFileName(path)} — entering Play mode. " +
                  "The editor exits Play mode automatically when the sweep finishes.");
    }

    private static bool EditorSceneOpen()
    {
        var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (active.path == Scene) return true;

        if (!EditorUtility.DisplayDialog(
                "Wrong scene open",
                $"The simulation runs on {Scene}, but '{active.name}' is open.\n\nOpen MainIsland now?",
                "Open MainIsland", "Cancel"))
        {
            return false;
        }

        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);
        return true;
    }

    // ---- headless player --------------------------------------------------

    [MenuItem("Tools/Island RTS/Simulation/Build Headless Sim Player", priority = 220)]
    public static void BuildSimPlayerMenu()
    {
        BuildReport report = BuildSimPlayerInternal();
        if (report == null) return;

        if (report.summary.result == BuildResult.Succeeded)
        {
            string exe = Path.Combine(ProjectRoot, BuildDir, PlayerName);
            Debug.Log($"[Sim] Sim player built: {exe} ({report.summary.totalSize / (1024 * 1024)} MB)");
            EditorUtility.RevealInFinder(exe);
        }
        else
        {
            Debug.LogError($"[Sim] Sim player build {report.summary.result}: {report.summary.totalErrors} errors");
        }
    }

    /// <summary>Entry point for <c>-executeMethod SimTools.BuildSimPlayerBatch</c>.</summary>
    public static void BuildSimPlayerBatch()
    {
        BuildReport report = BuildSimPlayerInternal();
        bool ok = report != null && report.summary.result == BuildResult.Succeeded;
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static BuildReport BuildSimPlayerInternal()
    {
        string outDir = Path.Combine(ProjectRoot, BuildDir);
        Directory.CreateDirectory(outDir);

        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            // Explicit scene list — EditorBuildSettings still points at SampleScene.
            scenes = new[] { Scene },
            locationPathName = Path.Combine(outDir, PlayerName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            // Development is load-bearing: it defines DEVELOPMENT_BUILD, which is
            // what compiles SimRunner and the sim hooks into the player at all.
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        return BuildPipeline.BuildPlayer(opts);
    }
}

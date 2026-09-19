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
    private const string PlayerName = "foundingtide-sim.exe";

    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    // ---- sweep authoring --------------------------------------------------

    [MenuItem("Tools/Founding Tide/Simulation/Write Example Sweep", priority = 200)]
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

    [MenuItem("Tools/Founding Tide/Simulation/Run Sweep In Editor…", priority = 201)]
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

    [MenuItem("Tools/Founding Tide/Simulation/Build Headless Sim Player", priority = 220)]
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

    /// <summary>
    /// Builds with Unity audio switched off at the project level, then puts the
    /// setting back (2026-09-10).
    ///
    /// <c>AudioListener.volume = 0</c> in SimRunner silences a run but the engine
    /// has already opened an output device by then, and nine lab windows each
    /// grabbing one was enough to take the machine's audio driver down. The only
    /// switch that stops the device being opened at all is "Disable Unity Audio"
    /// in Project Settings, which is baked into the player at build time — so it
    /// is flipped for the duration of this build and restored in a finally, which
    /// is why a cancelled or failed build still leaves the project as it was.
    /// The sim player has no use for sound in either mode.
    /// </summary>
    private static BuildReport BuildSimPlayerInternal()
    {
        bool restore = false;
        try
        {
            restore = SetProjectAudioDisabled(true);
            return BuildSimPlayerPlayer();
        }
        finally
        {
            if (restore) SetProjectAudioDisabled(false);
        }
    }

    /// <summary>
    /// Writes ProjectSettings/AudioManager.asset's <c>m_DisableAudio</c>. Returns
    /// true when the value actually changed, so the caller knows to put it back.
    /// </summary>
    private static bool SetProjectAudioDisabled(bool disabled)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
        if (assets == null || assets.Length == 0 || assets[0] == null)
        {
            Debug.LogWarning("[Sim] Could not open AudioManager.asset — the sim player will open an audio device.");
            return false;
        }

        SerializedObject so = new SerializedObject(assets[0]);
        SerializedProperty prop = so.FindProperty("m_DisableAudio");
        if (prop == null)
        {
            Debug.LogWarning("[Sim] AudioManager.asset has no m_DisableAudio — the sim player will open an audio device.");
            return false;
        }
        if (prop.boolValue == disabled) return false;

        prop.boolValue = disabled;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        return true;
    }

    private static BuildReport BuildSimPlayerPlayer()
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

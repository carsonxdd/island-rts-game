using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates the MainMenu scene and puts both scenes into the build settings.
///
/// The build list currently contains only the leftover stock SampleScene, which
/// means a build today ships an empty scene. This fixes that as part of adding
/// the menu, since the menu is what a build should open on.
///
/// Idempotent — re-running rebuilds the scene from scratch.
/// </summary>
public static class MenuSceneSetup
{
    private const string MenuScenePath = "Assets/MainMenu.unity";
    private const string GameScenePath = "Assets/MainIsland.unity";

    [MenuItem("Tools/Island RTS/Menus/Setup Main Menu Scene", priority = 300)]
    public static void SetupMenuScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // A camera is required or the scene renders nothing but the UI canvas
        // on an undefined background.
        GameObject camGo = new GameObject("MenuCamera", typeof(Camera), typeof(AudioListener));
        Camera cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        // Deep dusk blue — a stand-in until the artist supplies a title backdrop.
        cam.backgroundColor = new Color(0.05f, 0.07f, 0.11f, 1f);
        cam.orthographic = true;
        camGo.tag = "MainCamera";

        // The menu itself is built at runtime by PauseController's bootstrap,
        // so there is nothing else to author here. This object exists purely to
        // make the scene's purpose obvious in the Hierarchy.
        new GameObject("~MenuSceneMarker");

        EditorSceneManager.SaveScene(scene, MenuScenePath);
        AddScenesToBuildSettings();

        Debug.Log($"[Menus] Created {MenuScenePath} and updated build settings. " +
                  "Menu UI is created at runtime — press Play to see it.");
    }

    /// <summary>MainMenu first (index 0 = what a build opens on), then MainIsland.</summary>
    private static void AddScenesToBuildSettings()
    {
        if (!File.Exists(GameScenePath))
        {
            Debug.LogError($"[Menus] {GameScenePath} not found — build settings left alone.");
            return;
        }

        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MenuScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true)
        };

        // Keep any other enabled scene except the stock SampleScene, which is
        // the thing that made builds ship an empty world.
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            string p = s.path;
            if (p == MenuScenePath || p == GameScenePath) continue;
            if (p.EndsWith("SampleScene.unity")) continue;
            scenes.Add(s);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("[Menus] Build settings: 0=MainMenu, 1=MainIsland (SampleScene removed).");
    }
}

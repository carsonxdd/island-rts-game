using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes the scene-authored Victory / Defeat panels that `MenuScreens` replaced.
///
/// Those panels predated the menu system: hand-built uGUI under the gameplay
/// Canvas, driven by a `VictoryDefeatUI` component whose Quit button called
/// `Application.Quit` (which, in the editor, just stopped Play — there was no
/// way back to the main menu from either screen). Both screens are now built at
/// runtime on the same widgets as every other menu, so the scene objects are
/// dead weight that would still render if something re-enabled them.
///
/// The `VictoryDefeatUI` script is already deleted, so Unity shows its component
/// as "Missing (Mono Script)" until this runs — that broken component is the
/// visible symptom this tool clears.
///
/// Idempotent: finds nothing and says so on a scene that is already clean.
/// </summary>
public static class LegacyEndScreenCleanup
{
    private const string GameScenePath = "Assets/MainIsland.unity";

    private static readonly string[] LegacyObjectNames = { "VictoryScreen", "DefeatScreen" };

    [MenuItem("Tools/Island RTS/Menus/Remove Legacy Victory-Defeat Panels", false, 40)]
    public static void Cleanup()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != GameScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(GameScenePath);
        }

        int removed = 0;

        // Walk the scene rather than using GameObject.Find: the panels are
        // inactive (GameManager hid them at Start), and Find skips inactive
        // objects entirely — which is exactly why this looked like a no-op the
        // obvious way round.
        List<GameObject> all = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            CollectRecursive(root, all);
        }

        foreach (GameObject go in all)
        {
            if (go == null) continue;

            bool isLegacyPanel = false;
            for (int i = 0; i < LegacyObjectNames.Length; i++)
            {
                if (go.name == LegacyObjectNames[i]) { isLegacyPanel = true; break; }
            }

            if (isLegacyPanel)
            {
                Undo.DestroyObjectImmediate(go);
                removed++;
            }
        }

        // The VictoryDefeatUI component itself: its script asset is gone, so the
        // component deserializes as a null MonoBehaviour on the Canvas. Unity
        // has a built-in sweep for exactly this case.
        int missing = 0;
        foreach (GameObject go in all)
        {
            if (go == null) continue;
            missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        if (removed == 0 && missing == 0)
        {
            Debug.Log("[Menus] No legacy victory/defeat panels found — scene is already clean.");
            return;
        }

        Debug.Log($"[Menus] Removed {removed} legacy end-screen panel(s) and " +
                  $"{missing} missing-script component(s). Victory and defeat are " +
                  "built at runtime by MenuScreens now.");
    }

    private static void CollectRecursive(GameObject go, List<GameObject> into)
    {
        into.Add(go);
        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            CollectRecursive(t.GetChild(i).gameObject, into);
        }
    }
}

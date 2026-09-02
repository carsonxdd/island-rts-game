using System.Text;
using IslandRTS.ArtGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Runs every one-time editor setup step, in the order they depend on each other.
///
/// The steps have always had to run in a specific sequence — the opening-sequence
/// tool consumes the generated art library, the terrain tool deletes the ocean
/// frame the opening tool built and re-snaps the props the scatter placed, and
/// the menu tool replaces the open scene entirely so it can only go last. Getting
/// that order wrong produces a scene that looks set up and is not, which is worse
/// than one that obviously failed. This encodes the order once.
///
/// Idempotent: every underlying step is, so re-running is the normal way to pick
/// up new art or a changed island seed.
/// </summary>
public static class FullSetup
{
    private const string GameScenePath = "Assets/MainIsland.unity";
    private const string MenuScenePath = "Assets/MainMenu.unity";

    [MenuItem("Tools/Island RTS/Setup Everything (In Order)", false, -100)]
    public static void SetupEverything()
    {
        if (!EditorUtility.DisplayDialog(
                "Set up everything?",
                "Runs all eight setup steps in dependency order:\n\n" +
                "1. Generate the low-poly art library\n" +
                "2. Plumb it onto the gameplay prefabs\n" +
                "3. Set up the opening sequence (survivor + wreck)\n" +
                "4. Build the environment scatter settings\n" +
                "5. Build the island terrain + runtime NavMesh\n" +
                "6. Add pickups and the workshop\n" +
                "7. Remove the legacy victory/defeat panels\n" +
                "8. Create the MainMenu scene and fix the build scene list\n\n" +
                "This rewrites prefabs and MainIsland, and leaves MainMenu open " +
                "so you can press Play on the real entry point.\n\n" +
                "Unsaved changes to the open scene will be saved first.",
                "Run all steps", "Cancel"))
        {
            return;
        }

        // Save whatever is open before the first step touches anything — several
        // steps swap the active scene, and a discarded scene mid-run is the one
        // failure mode that loses work.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[Setup] Cancelled — nothing was changed.");
            return;
        }

        StringBuilder log = new StringBuilder("[Setup] Full setup complete:\n");

        try
        {
            // --- art library: prefabs only, no scene involvement -------------
            Step(0, 8, "Generating art library");
            LowPolyAssetGenerator.GenerateAll();
            log.AppendLine("  1. art library generated");

            Step(1, 8, "Plumbing art onto gameplay prefabs");
            LowPolyPlumber.PlumbEverything();
            log.AppendLine("  2. prefabs plumbed");

            // --- scene steps: each needs MainIsland to be the active scene ---
            if (!OpenGameScene()) return;

            Step(2, 8, "Setting up the opening sequence");
            OpeningSequenceSetup.SetupOpeningScene();
            log.AppendLine("  3. opening sequence set up");

            // Props are scattered at RUNTIME now (the island is random per
            // run); this step just resolves the prop table into an asset.
            Step(3, 8, "Building the environment scatter settings");
            LowPolyScatter.EnsureSettingsAsset();
            log.AppendLine("  4. scatter settings asset built");

            // Terrain must follow the opening sequence: it deletes the flat
            // Ground plane, the ocean quad frame and any legacy edit-time
            // scatter, and snaps the wreck onto the landing cove.
            if (!OpenGameScene()) return;

            Step(4, 8, "Generating terrain and NavMesh");
            TerrainSetup.SetupTerrainScene();
            log.AppendLine("  5. terrain set up (random island per run, runtime scatter)");

            Step(5, 8, "Adding pickups and the workshop");
            NewContentSetup.Setup();
            log.AppendLine("  6. pickups + workshop added");

            // Must precede the menu step for the usual reason: it is a
            // MainIsland edit, and the menu step swaps the active scene.
            Step(6, 8, "Removing the legacy victory/defeat panels");
            LegacyEndScreenCleanup.Cleanup();
            log.AppendLine("  7. legacy end-screen panels removed");

            // Save MainIsland before the menu step: it opens a brand new scene
            // and would otherwise prompt (or discard) everything above.
            EditorSceneManager.SaveOpenScenes();

            Step(7, 8, "Creating the menu scene");
            MenuSceneSetup.SetupMenuScene();
            log.AppendLine("  8. MainMenu created, build scene list fixed");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        log.AppendLine();
        log.AppendLine("MainMenu is open — press Play to test the real entry point ")
           .AppendLine("(title screen, NEW GAME, then Esc for the pause menu in game).");
        Debug.Log(log.ToString());
    }

    private static bool OpenGameScene()
    {
        if (EditorSceneManager.GetActiveScene().path == GameScenePath) return true;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogError("[Setup] Aborted before finishing — the scene steps did not all run. " +
                           "Re-run the menu item to continue; every step is idempotent.");
            return false;
        }

        EditorSceneManager.OpenScene(GameScenePath);
        return true;
    }

    private static void Step(int index, int total, string label)
    {
        EditorUtility.DisplayProgressBar("Island RTS setup", $"{label}…", (float)index / total);
    }

    /// <summary>
    /// Opens MainIsland for direct gameplay testing, skipping the title screen.
    /// Separate menu item because the full setup deliberately leaves MainMenu
    /// open — that is what a build starts on.
    /// </summary>
    [MenuItem("Tools/Island RTS/Open Game Scene (MainIsland)", false, -99)]
    public static void OpenGameSceneMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(GameScenePath);
    }

    [MenuItem("Tools/Island RTS/Open Menu Scene (MainMenu)", false, -98)]
    public static void OpenMenuSceneMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (System.IO.File.Exists(MenuScenePath))
        {
            EditorSceneManager.OpenScene(MenuScenePath);
            return;
        }
        Debug.LogError($"[Setup] {MenuScenePath} does not exist yet — run " +
                       "Tools > Island RTS > Menus > Setup Main Menu Scene (or Setup Everything).");
    }
}

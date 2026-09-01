using UnityEngine;

/// <summary>
/// Shows/hides the build grid.
///
/// The grid appears automatically whenever build mode is active (that's when it's
/// actually useful) and the Toggle Grid binding forces it on or off on top of that.
///
/// NOTE: this used to be bound to G, which collided with BuildPlacement's
/// wall-to-gate conversion — both fired on the same frame. Keep it off G.
/// </summary>
public class GridToggleHotkey : MonoBehaviour
{
    public GridOverlay grid;
    // The toggle key moved into KeyBindings (Action.ToggleGrid) so it appears on
    // the Controls screen and can be rebound. It defaults to F2 there, and the
    // reserved-key list keeps a player from putting it on F3/F4 (AI overlay,
    // debug menu). Never G — that is gate conversion.
    [Tooltip("Show the grid automatically while build mode is active.")]
    public bool autoShowInBuildMode = true;

    BuildPlacement buildPlacement;

    // User intent layered over the build-mode auto-show:
    //   manual     — user forced it visible outside build mode
    //   suppressed — user forced it hidden while build mode was showing it
    //                (cleared when build mode ends, so the next B starts fresh)
    bool manual;
    bool suppressed;

    void Start()
    {
        if (grid == null)
        {
            grid = FindAnyObjectByType<GridOverlay>();
            if (grid == null)
            {
                Debug.LogError("GridToggleHotkey: No GridOverlay found in scene!");
            }
        }

        buildPlacement = FindAnyObjectByType<BuildPlacement>();

        // "Show build grid by default" in Options is exactly the manual-on
        // state, so it seeds the same flag the toggle key sets.
        manual = GameSettings.GridByDefault;
    }

    void Update()
    {
        // Menus own input while paused/open (PauseController.BlockGameplayInput).
        if (PauseController.BlockGameplayInput) return;
        if (grid == null) return;

        bool auto = autoShowInBuildMode && buildPlacement != null && buildPlacement.isPlacing;

        if (KeyBindings.Down(KeyBindings.Action.ToggleGrid))
        {
            if (grid.show)
            {
                manual = false;
                suppressed = auto;      // only meaningful while build mode wants it on
            }
            else
            {
                manual = true;
                suppressed = false;
            }
        }

        if (!auto) suppressed = false;

        bool desired = (manual || auto) && !suppressed;
        if (desired == grid.show) return;

        grid.show = desired;
        grid.Rebuild();   // re-drapes against current terrain (placement flattens it)
    }
}

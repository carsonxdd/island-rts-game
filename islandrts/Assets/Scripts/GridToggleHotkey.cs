using UnityEngine;

public class GridToggleHotkey : MonoBehaviour
{
    public GridOverlay grid;

    void Start()
    {
        // Auto-find if not assigned
        if (grid == null)
        {
            grid = FindFirstObjectByType<GridOverlay>();
            if (grid == null)
            {
                Debug.LogError("GridToggleHotkey: No GridOverlay found in scene!");
            }
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (grid == null)
            {
                Debug.LogError("GridToggleHotkey: Grid reference is missing!");
                return;
            }

            // Toggle the show flag
            grid.show = !grid.show;

            // Rebuild the grid to apply the change
            grid.Rebuild();
        }
    }
}

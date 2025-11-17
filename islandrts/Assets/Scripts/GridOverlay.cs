using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class GridOverlay : MonoBehaviour
{
    [Header("Size & Cells")]
    public int halfExtent = 25;       // draws from -N..+N in X/Z
    public float cellSize = 1f;

    [Header("Style")]
    public Color lineColor = new Color(1f,1f,1f,0.2f);
    public float lineWidth = 0.03f;
    public bool show = true;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    Material _mat;
    bool _needsRebuild = false;

    void OnEnable(){ Build(); }
    void OnValidate()
    {
        // Can't create objects during OnValidate, so defer it
        _needsRebuild = true;
    }
    void OnDisable(){ Clear(); }

    void Update()
    {
        // Rebuild if flagged from OnValidate
        if (_needsRebuild)
        {
            _needsRebuild = false;
            Build();
        }
    }

    // Public method to trigger rebuild (for hotkey toggle)
    public void Rebuild()
    {
        Build();
    }

    void Clear()
    {
        // Destroy all tracked lines
        foreach (var lr in lines)
        {
            if (lr != null && lr.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(lr.gameObject);
                else
                    DestroyImmediate(lr.gameObject);
            }
        }
        lines.Clear();

        // Also find any orphaned grid_line objects that might not be tracked
        // Need to collect them first to avoid modifying collection while iterating
        var childrenToDestroy = new List<GameObject>();
        foreach (Transform child in transform)
        {
            if (child.name == "grid_line")
            {
                childrenToDestroy.Add(child.gameObject);
            }
        }

        foreach (var child in childrenToDestroy)
        {
            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }

        // Clean up material
        if (_mat)
        {
            if (Application.isPlaying)
                Destroy(_mat);
            else
                DestroyImmediate(_mat);
        }
        _mat = null;
    }

    void Build()
    {
        if (!isActiveAndEnabled) return;

        // Always clear first
        Clear();

        // If show is false, don't create new lines
        if (!show)
        {
            Debug.Log("GridOverlay: Grid hidden (cleared all lines)");
            return;
        }

        // Create material for lines
        _mat = new Material(Shader.Find("Sprites/Default")); // simple, transparent

        int count = (halfExtent * 2) + 1;
        // Vertical lines (Z axis)
        for (int i = -halfExtent; i <= halfExtent; i++)
            CreateLine(new Vector3(i*cellSize, 0f, -halfExtent*cellSize),
                       new Vector3(i*cellSize, 0f,  halfExtent*cellSize));

        // Horizontal lines (X axis)
        for (int j = -halfExtent; j <= halfExtent; j++)
            CreateLine(new Vector3(-halfExtent*cellSize, 0f, j*cellSize),
                       new Vector3( halfExtent*cellSize, 0f, j*cellSize));

        Debug.Log($"GridOverlay: Grid shown (created {lines.Count} lines)");

        void CreateLine(Vector3 a, Vector3 b)
        {
            var go = new GameObject("grid_line");
            go.transform.SetParent(transform, false);
            go.isStatic = true;  // Mark as static - no physics

            var lr = go.AddComponent<LineRenderer>();
            lr.material = _mat;
            lr.widthMultiplier = lineWidth;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.startColor = lr.endColor = lineColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = 1000;     // draw on top
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lines.Add(lr);
        }
    }
}

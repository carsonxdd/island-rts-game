using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides which trees are currently hiding a unit, and tells those trees to fade
/// (see <see cref="OcclusionFade"/>).
/// </summary>
/// <remarks>
/// The test is done in SCREEN space, not with physics raycasts. Trees carry a click
/// hitbox on the Default layer, so a camera-to-unit raycast would also hit terrain and
/// buildings and would cost one raycast per unit per frame; projecting instead lets one
/// pass over the tree list answer the question for every unit at once, and it is exactly
/// the question being asked - "does this canopy cover that worker on screen".
///
/// A tree is treated as the screen-space segment from its base to the top of its
/// renderer bounds, widened by its half-width. A unit is a point at chest height. The
/// tree fades when a unit sits inside that widened segment AND is further from the
/// camera, which under the orthographic projection is just a depth comparison.
///
/// Runs at 10 Hz. The fade itself is a per-frame lerp in OcclusionFade, so the low tick
/// rate is invisible; what it buys is that the trees-by-units loop costs a fraction of a
/// millisecond even with a few hundred trees on screen.
///
/// Self-bootstrapping from OcclusionFade.Awake, so nothing needs wiring in the scene,
/// and NOT DontDestroyOnLoad - it holds no state worth carrying across a scene load.
/// </remarks>
public class OcclusionFadeManager : MonoBehaviour
{
    private const float TickInterval = 0.1f;
    /// <summary>Half-width of a unit on screen, in world units - roughly a meeple's shoulders.</summary>
    private const float UnitHalfWidth = 0.5f;
    /// <summary>Chest height: where a unit's silhouette actually is, rather than its feet.</summary>
    private const float UnitChestHeight = 0.9f;

    private static OcclusionFadeManager instance;

    private readonly List<Vector3> unitPoints = new List<Vector3>();  // screen x, screen y, view depth
    private Camera cam;
    private float tickTimer;
    private Survivor survivor;

    /// <summary>Create the manager if this scene does not have one yet.</summary>
    public static void Ensure()
    {
        if (instance != null) return;
        if (SimHooks.Simulating) return;  // headless: no camera, and this is pure cosmetics

        GameObject go = new GameObject("_OcclusionFade");
        instance = go.AddComponent<OcclusionFadeManager>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void LateUpdate()
    {
        tickTimer -= Time.unscaledDeltaTime;
        if (tickTimer > 0f) return;
        tickTimer = TickInterval;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        var trees = OcclusionFade.ActiveList;
        if (trees.Count == 0) return;

        CollectUnitPoints();
        if (unitPoints.Count == 0)
        {
            for (int i = 0; i < trees.Count; i++)
            {
                if (trees[i] != null) trees[i].SetOccluding(false);
            }
            return;
        }

        // World units to screen pixels. Orthographic size is half the vertical view.
        float pixelsPerUnit = cam.orthographic
            ? Screen.height / (2f * Mathf.Max(0.01f, cam.orthographicSize))
            : Screen.height / 20f;
        float unitPixels = UnitHalfWidth * pixelsPerUnit;
        float margin = 200f;

        for (int i = 0; i < trees.Count; i++)
        {
            OcclusionFade tree = trees[i];
            if (tree == null) continue;

            tree.EnsureMeasured();

            Vector3 basePos = tree.transform.position;
            Vector3 baseScreen = cam.WorldToScreenPoint(basePos);
            Vector3 topScreen = cam.WorldToScreenPoint(basePos + Vector3.up * tree.SilhouetteHeight);

            // Cheap reject: nothing off-screen can be hiding anything the player is looking at.
            float minX = Mathf.Min(baseScreen.x, topScreen.x) - margin;
            float maxX = Mathf.Max(baseScreen.x, topScreen.x) + margin;
            float minY = Mathf.Min(baseScreen.y, topScreen.y) - margin;
            float maxY = Mathf.Max(baseScreen.y, topScreen.y) + margin;
            if (maxX < 0f || minX > Screen.width || maxY < 0f || minY > Screen.height)
            {
                tree.SetOccluding(false);
                continue;
            }

            float reach = tree.SilhouetteRadius * pixelsPerUnit + unitPixels;
            float reachSq = reach * reach;
            // Nearest point of the tree to the camera, so a unit behind ANY part of it counts.
            float treeDepth = Mathf.Min(baseScreen.z, topScreen.z);

            bool occluding = false;
            for (int u = 0; u < unitPoints.Count; u++)
            {
                Vector3 p = unitPoints[u];
                if (p.z <= treeDepth) continue;  // unit is in front of the tree
                if (SqrDistanceToSegment(p.x, p.y, baseScreen.x, baseScreen.y, topScreen.x, topScreen.y) < reachSq)
                {
                    occluding = true;
                    break;
                }
            }

            tree.SetOccluding(occluding);
        }
    }

    void CollectUnitPoints()
    {
        unitPoints.Clear();
        AddUnits(Worker.ActiveList);
        AddUnits(Warrior.ActiveList);
        AddUnits(Enemy.ActiveList);

        // The survivor exists only during the opening and has no registry of its own;
        // the lookup is skipped entirely once the colony starts.
        if (GameStartController.IntroInProgress)
        {
            if (survivor == null) survivor = Object.FindAnyObjectByType<Survivor>();
            if (survivor != null) AddPoint(survivor.transform.position);
        }
    }

    void AddUnits<T>(IReadOnlyList<T> units) where T : MonoBehaviour
    {
        for (int i = 0; i < units.Count; i++)
        {
            T unit = units[i];
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;  // garrisoned workers are hidden
            AddPoint(unit.transform.position);
        }
    }

    void AddPoint(Vector3 worldPos)
    {
        Vector3 sp = cam.WorldToScreenPoint(worldPos + Vector3.up * UnitChestHeight);
        unitPoints.Add(sp);
    }

    static float SqrDistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float abx = bx - ax, aby = by - ay;
        float apx = px - ax, apy = py - ay;
        float lenSq = abx * abx + aby * aby;
        float t = lenSq > 0.0001f ? Mathf.Clamp01((apx * abx + apy * aby) / lenSq) : 0f;
        float dx = apx - abx * t, dy = apy - aby * t;
        return dx * dx + dy * dy;
    }
}

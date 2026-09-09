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
    /// <summary>
    /// 20 Hz (2026-09-08, was 10). At 10 Hz the decision could lag a tenth of a second
    /// behind a walking unit, and with the old symmetric quarter-second fade on top of
    /// it the object was still solid for a third of a second after the unit went behind
    /// it — which is exactly the "fades too late" complaint.
    /// </summary>
    private const float TickInterval = 0.05f;
    /// <summary>Half-width of a unit on screen, in world units - roughly a meeple's shoulders.</summary>
    private const float UnitHalfWidth = 0.22f;

    /// <summary>
    /// A unit is tested as its whole standing silhouette, not one point (2026-09-08). A
    /// single chest point missed a unit whose head was behind a canopy while its chest
    /// was clear of it, and missed the reverse under a hut roof — both read as the fade
    /// simply not working. Two points down the body cost one extra distance test each
    /// and catch both.
    /// </summary>
    private static readonly float[] UnitSampleHeights = { 0.35f, 1.35f };

    /// <summary>
    /// How far behind the object a unit must be, in view depth, before it fades.
    /// Without it a unit standing level with a trunk flickered the tree on and off as
    /// the depth compare crossed zero.
    /// </summary>
    private const float DepthMargin = 0.6f;

    /// <summary>
    /// Once faded, an object stays faded until the unit is this much further out than it
    /// took to start (2026-09-08). Pure hysteresis: without it a unit walking the edge of
    /// the silhouette sat on the threshold and the object chattered in and out.
    /// </summary>
    private const float ReleaseSlack = 1.2f;

    private static OcclusionFadeManager instance;

    private readonly List<Vector3> unitPoints = new List<Vector3>();  // screen x, screen y, view depth
    private Camera cam;
    private float tickTimer;

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

            // Measuring is lazy and can retire an object too short to hide anyone, which
            // unregisters it mid-loop. The bound is re-read each iteration so that is
            // safe; it costs one skipped entry for one tick, once, ever.
            tree.EnsureMeasured();
            if (!tree.enabled) continue;

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

            // Tightness is per object: a canopy is mostly gaps, a hut is a solid box.
            float reach = tree.SilhouetteRadius * tree.silhouetteTightness * pixelsPerUnit + unitPixels;
            if (tree.IsOccluding) reach *= ReleaseSlack;   // hysteresis: harder to let go than to grab
            float reachSq = reach * reach;

            bool occluding = false;
            for (int u = 0; u < unitPoints.Count; u++)
            {
                Vector3 p = unitPoints[u];
                float t;
                if (SqrDistanceToSegment(p.x, p.y, baseScreen.x, baseScreen.y, topScreen.x, topScreen.y, out t) >= reachSq)
                    continue;

                // Depth is read WHERE THE COVER IS, not at the nearest end of the object
                // (2026-09-08). The camera looks down, so a palm's crown sits metres nearer
                // in view depth than its trunk; testing every unit against the crown made a
                // colonist standing well IN FRONT of the trunk read as hidden and faded the
                // palm for nothing. The piece of silhouette that overlaps the unit on screen
                // is at t along base->top, so that is the depth the unit has to be behind.
                float coverDepth = Mathf.Lerp(baseScreen.z, topScreen.z, t) + DepthMargin;
                if (p.z <= coverDepth) continue;  // unit is in front of, or level with, the cover

                occluding = true;
                break;
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

        // The player's own character is there for the whole run (2026-09-02); a
        // knocked-out body is hidden and must not keep its tree faded.
        PlayerCharacter player = PlayerCharacter.Instance;
        if (player != null && !player.IsKnockedOut) AddPoint(player.transform.position);
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
        for (int h = 0; h < UnitSampleHeights.Length; h++)
            unitPoints.Add(cam.WorldToScreenPoint(worldPos + Vector3.up * UnitSampleHeights[h]));
    }

    /// <summary>
    /// Squared screen distance from a point to the segment, and where along it the nearest
    /// point sits (0 at the base, 1 at the top) so the caller can read the silhouette's
    /// view depth at exactly that spot.
    /// </summary>
    static float SqrDistanceToSegment(float px, float py, float ax, float ay, float bx, float by, out float t)
    {
        float abx = bx - ax, aby = by - ay;
        float apx = px - ax, apy = py - ay;
        float lenSq = abx * abx + aby * aby;
        t = lenSq > 0.0001f ? Mathf.Clamp01((apx * abx + apy * aby) / lenSq) : 0f;
        float dx = apx - abx * t, dy = apy - aby * t;
        return dx * dx + dy * dy;
    }
}

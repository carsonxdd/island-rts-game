using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// RTS camera: smoothed WASD pan (screen-relative), smoothed Q/E rotation,
/// smoothed scroll zoom, and middle-mouse free-look (vertical drag tilts,
/// horizontal drag rotates), orbiting the ground point at screen center.
/// Pan speed scales with zoom so the world moves at a consistent screen speed.
/// Uses unscaled time so camera feel is identical at 1x/2x/4x game speed.
/// CameraShake (pure offset, LateUpdate) layers on top without conflict.
/// </summary>
public class CameraController : MonoBehaviour
{
    /// <summary>The scene's camera controller (one per scene; no DontDestroyOnLoad).</summary>
    public static CameraController Instance { get; private set; }

    [Header("WASD Movement (Screen-Relative)")]
    public float panSpeed = 25f;              // world units/sec at startOrthoSize zoom
    [Tooltip("Seconds to reach/leave full pan speed. Lower = snappier.")]
    public float panSmoothTime = 0.12f;

    [Header("Middle-Mouse Free-Look (Tilt + Rotate)")]
    public bool enableFreeLook = true;
    [Tooltip("Degrees of tilt per unit of vertical mouse movement.")]
    public float tiltSensitivity = 1.2f;
    [Tooltip("Degrees of rotation per unit of horizontal mouse movement.")]
    public float lookYawSensitivity = 1.0f;
    [Tooltip("Lowest camera pitch (more cinematic, sees further under the horizon).")]
    public float minTilt = 30f;
    [Tooltip("Highest camera pitch (closer to top-down).")]
    public float maxTilt = 60f;
    [Tooltip("Seconds for free-look to ease toward the mouse.")]
    public float freeLookSmoothTime = 0.10f;

    [Header("Camera Rotation")]
    public float rotationSpeed = 90f;         // degrees per second
    [Tooltip("Seconds for rotation to ease in/out.")]
    public float rotationSmoothTime = 0.08f;
    // The rotate keys are no longer inspector fields. Every gameplay key lives
    // in KeyBindings now, so the Controls screen can list and rebind it —
    // a binding hidden on a component is one a player cannot find.

    [Header("Zoom (Orthographic)")]
    public float zoomSpeed = 2f;              // ortho size change per scroll unit
    [Tooltip("Seconds for zoom to ease toward its target.")]
    public float zoomSmoothTime = 0.12f;
    public float startOrthoSize = 15f;
    public float minOrthoSize = 5f;
    public float maxOrthoSize = 30f;
    [Tooltip("Ortho near clip. NEGATIVE on purpose: when tilted low / zoomed out, ground at the bottom of the view sits behind the camera plane and would be sliced off. A negative near extends the render box backwards (standard for ortho RTS cameras).")]
    public float nearClip = -100f;

    [Header("Shadows")]
    [Tooltip("Drive URP's shadow distance from the current zoom/tilt. The asset's fixed value only covers the view when zoomed in — zoomed out, everything past it renders unshadowed.")]
    public bool scaleShadowDistanceWithZoom = true;
    [Tooltip("Safety factor over the computed view depth, so shadow casters just off-screen still cast in.")]
    public float shadowDistanceMargin = 1.25f;
    public float minShadowDistance = 50f;
    [Tooltip("Upper bound. Higher covers more, but spreads the shadowmap thinner (softer/blockier close-up shadows).")]
    public float maxShadowDistance = 160f;

    [Header("Edge Pan")]
    [Tooltip("Screen-edge thickness in pixels that pans the camera. Only active when the player turns Edge Pan on in Options.")]
    public float edgePanBorder = 14f;

    [Header("Bounds")]
    public bool useBounds = false;
    public Vector2 minBounds = new Vector2(-120, -120);
    public Vector2 maxBounds = new Vector2(120, 120);

    Camera cam;

    // Smoothing state
    Vector3 panVelocity;                      // current smoothed pan velocity
    Vector3 panVelocityRef;                   // SmoothDamp internal
    float rotVelocity;                        // current smoothed angular velocity (deg/s)
    float rotVelocityRef;                     // SmoothDamp internal
    float targetOrthoSize;
    float zoomVelocityRef;                    // SmoothDamp internal

    // Free-look state
    bool isFreeLooking;                       // middle mouse held
    bool freeLookSettling;                    // easing out the last bit after release
    Vector3 freeLookPivot;                    // ground point orbited during the drag
    float targetPitch;                        // absolute degrees, clamped minTilt..maxTilt
    float targetYaw;                          // absolute degrees, unclamped
    float pitchVelRef;                        // SmoothDampAngle internal
    float yawVelRef;                          // SmoothDampAngle internal
    static readonly Plane groundPlane = new Plane(Vector3.up, 0f);

    // Shadow-distance state. The URP asset is a ScriptableObject on disk, so the
    // original value is cached and restored on disable — otherwise a Play session
    // would permanently rewrite the asset in the editor.
    UniversalRenderPipelineAsset shadowAsset;
    float originalShadowDistance;
    float appliedShadowDistance = -1f;

    void Awake()
    {
        Instance = this;
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("CameraController: No Main Camera found!");
            return;
        }
        cam.orthographic = true;
        cam.orthographicSize = startOrthoSize;
        cam.nearClipPlane = nearClip;         // negative: stops bottom-of-screen clipping when tilted/zoomed out
        targetOrthoSize = startOrthoSize;
    }

    void Update()
    {
        // Menus own input while paused/open (PauseController.BlockGameplayInput).
        if (PauseController.BlockGameplayInput) return;
        if (cam == null) return;
        float dt = Time.unscaledDeltaTime;

        UpdateZoom(dt);
        UpdateFreeLook(dt);
        UpdateKeyboardPan(dt);
        UpdateRotation(dt);
        UpdateShadowDistance();

        // Snap the view onto the player's character (Space by default). No
        // auto-follow — it is an RTS — just a way to find yourself again.
        if (KeyBindings.Down(KeyBindings.Action.CenterOnCharacter) && PlayerCharacter.Instance != null)
        {
            CenterOn(PlayerCharacter.Instance.transform.position);
        }

        if (useBounds)
        {
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.z = Mathf.Clamp(p.z, minBounds.y, maxBounds.y);
            transform.position = p;
        }
    }

    /// <summary>
    /// Shift the camera (XZ only, rotation and zoom untouched) so the current
    /// view centre lands on <paramref name="worldPos"/>. Rotation-agnostic, and
    /// compatible with CameraShake's pure-offset approach. The intersection is
    /// taken at the target's own height so a character on a hill lands in the
    /// middle of the screen, not a few metres downhill of it.
    /// </summary>
    public void CenterOn(Vector3 worldPos)
    {
        if (cam == null) return;

        Ray centerRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane ground = new Plane(Vector3.up, new Vector3(0f, worldPos.y, 0f));
        float dist;
        if (!ground.Raycast(centerRay, out dist)) return;

        Vector3 viewCenter = centerRay.GetPoint(dist);
        Vector3 delta = worldPos - viewCenter;
        delta.y = 0f;
        transform.position += delta;

        // Kill any pan momentum so the smoothing doesn't drift the view off again
        panVelocity = Vector3.zero;
        panVelocityRef = Vector3.zero;
    }

    void UpdateKeyboardPan(float dt)
    {
        // Read through KeyBindings rather than the "Horizontal"/"Vertical" input
        // axes. Those axes are fixed in the project's input settings, so pan
        // would be the one action a player could see on the Controls screen and
        // not actually change.
        float horizontal = KeyBindings.Axis(KeyBindings.Action.PanLeft, KeyBindings.Action.PanRight);
        float vertical = KeyBindings.Axis(KeyBindings.Action.PanDown, KeyBindings.Action.PanUp);

        if (GameSettings.EdgePan) ApplyEdgePan(ref horizontal, ref vertical);

        Vector3 targetVel = Vector3.zero;
        if (!isFreeLooking && (Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f))
        {
            // Screen-relative: W moves toward 12 o'clock regardless of rotation
            Vector3 forward = transform.forward; forward.y = 0; forward.Normalize();
            Vector3 right = transform.right;     right.y = 0;   right.Normalize();
            Vector3 dir = (forward * vertical + right * horizontal).normalized;

            // Scale by zoom so screen-space speed stays constant
            float zoomScale = cam.orthographicSize / Mathf.Max(startOrthoSize, 0.01f);
            // GameSettings.CameraSpeed is the player's Options multiplier, read
            // at the point of effect so changing the slider applies instantly.
            targetVel = dir * panSpeed * GameSettings.CameraSpeed * zoomScale;
        }

        panVelocity = Vector3.SmoothDamp(panVelocity, targetVel, ref panVelocityRef,
                                         panSmoothTime, Mathf.Infinity, dt);
        if (panVelocity.sqrMagnitude > 0.0001f)
            transform.position += panVelocity * dt;
    }

    /// <summary>
    /// Adds screen-edge input to the keyboard pan axes.
    ///
    /// Only counts while the pointer is actually inside the window — an
    /// unfocused or alt-tabbed game reports a stale mouse position that would
    /// otherwise pan the camera forever on its own.
    /// </summary>
    void ApplyEdgePan(ref float horizontal, ref float vertical)
    {
        Vector3 m = Input.mousePosition;
        if (m.x < 0f || m.y < 0f || m.x >= UnityEngine.Screen.width || m.y >= UnityEngine.Screen.height) return;

        if (m.x < edgePanBorder) horizontal -= 1f;
        else if (m.x > UnityEngine.Screen.width - edgePanBorder) horizontal += 1f;

        if (m.y < edgePanBorder) vertical -= 1f;
        else if (m.y > UnityEngine.Screen.height - edgePanBorder) vertical += 1f;

        // Keyboard + edge in the same direction must not double the speed.
        horizontal = Mathf.Clamp(horizontal, -1f, 1f);
        vertical = Mathf.Clamp(vertical, -1f, 1f);
    }

    /// <summary>
    /// Middle-mouse free-look: vertical drag tilts (pitch, clamped minTilt..maxTilt),
    /// horizontal drag rotates (yaw). Both orbit the ground point that was at screen
    /// center when the drag started, so the framing stays anchored on what the player
    /// is looking at. Eases toward the mouse and keeps easing briefly after release.
    /// </summary>
    void UpdateFreeLook(float dt)
    {
        if (!enableFreeLook) return;

        if (Input.GetMouseButtonDown(2))
        {
            isFreeLooking = true;
            freeLookSettling = false;
            freeLookPivot = GetViewCenterGroundPoint();
            Vector3 e = transform.eulerAngles;
            targetPitch = Mathf.Clamp(NormalizePitch(e.x), minTilt, maxTilt);
            targetYaw = e.y;
            pitchVelRef = 0f;
            yawVelRef = 0f;
            panVelocity = Vector3.zero;       // orbit is direct, kill any pan glide
            panVelocityRef = Vector3.zero;
        }

        if (isFreeLooking && !Input.GetMouseButton(2))
        {
            isFreeLooking = false;
            freeLookSettling = true;          // finish easing to the final target
        }

        if (isFreeLooking)
        {
            // Default is "drag down to look up", the RTS convention; the Options
            // toggle flips it for players who read the drag as moving the world.
            float tiltDir = GameSettings.InvertTilt ? 1f : -1f;
            targetPitch = Mathf.Clamp(
                targetPitch + Input.GetAxisRaw("Mouse Y") * tiltSensitivity * tiltDir,
                minTilt, maxTilt);
            targetYaw += Input.GetAxisRaw("Mouse X") * lookYawSensitivity;
        }

        if (!isFreeLooking && !freeLookSettling) return;

        Vector3 euler = transform.eulerAngles;
        float pitch = NormalizePitch(euler.x);
        float newPitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchVelRef,
                                               freeLookSmoothTime, Mathf.Infinity, dt);
        float newYaw = Mathf.SmoothDampAngle(euler.y, targetYaw, ref yawVelRef,
                                             freeLookSmoothTime, Mathf.Infinity, dt);

        float yawDelta = Mathf.DeltaAngle(euler.y, newYaw);
        if (Mathf.Abs(yawDelta) > 0.0001f)
            transform.RotateAround(freeLookPivot, Vector3.up, yawDelta);

        float pitchDelta = newPitch - pitch;
        if (Mathf.Abs(pitchDelta) > 0.0001f)
            transform.RotateAround(freeLookPivot, transform.right, pitchDelta);

        if (freeLookSettling &&
            Mathf.Abs(Mathf.DeltaAngle(newYaw, targetYaw)) < 0.01f &&
            Mathf.Abs(newPitch - targetPitch) < 0.01f)
        {
            freeLookSettling = false;
        }
    }

    void UpdateRotation(float dt)
    {
        if (isFreeLooking) return;            // free-look owns rotation while held

        float target = 0f;
        // GameSettings.RotationSpeed is the player's Options multiplier, read at
        // the point of effect so the slider applies instantly.
        float speed = rotationSpeed * GameSettings.RotationSpeed;
        if (KeyBindings.Held(KeyBindings.Action.RotateCameraLeft)) target -= speed;
        if (KeyBindings.Held(KeyBindings.Action.RotateCameraRight)) target += speed;

        if (target != 0f && freeLookSettling)
            freeLookSettling = false;         // keyboard takes over, drop the ease-out

        rotVelocity = Mathf.SmoothDamp(rotVelocity, target, ref rotVelocityRef,
                                       rotationSmoothTime, Mathf.Infinity, dt);
        if (Mathf.Abs(rotVelocity) > 0.01f)
            transform.Rotate(Vector3.up, rotVelocity * dt, Space.World);
    }

    void UpdateZoom(float dt)
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            targetOrthoSize = Mathf.Clamp(
                targetOrthoSize - scroll * zoomSpeed * GameSettings.ZoomSpeed,
                minOrthoSize, maxOrthoSize);
        }

        if (Mathf.Abs(cam.orthographicSize - targetOrthoSize) > 0.001f)
        {
            cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, targetOrthoSize,
                                                    ref zoomVelocityRef, zoomSmoothTime,
                                                    Mathf.Infinity, dt);
        }
    }

    /// <summary>
    /// Keeps URP's shadow distance covering what's actually on screen.
    ///
    /// Shadow distance is measured along the view axis from the camera, and the
    /// furthest thing visible is the ground at the TOP edge of the view — at depth
    /// (cameraHeight + orthoSize * cos(pitch)) / sin(pitch). The asset ships a fixed
    /// 50, which covers a zoomed-in view but is roughly half of what a fully zoomed-out
    /// low-tilt view spans, so distant trees rendered with no shadow at all.
    ///
    /// Scaling it means close-up shadows keep the full shadowmap resolution instead of
    /// paying for coverage they never use.
    /// </summary>
    void UpdateShadowDistance()
    {
        if (!scaleShadowDistanceWithZoom) return;

        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) return;

        if (asset != shadowAsset)
        {
            RestoreShadowDistance();          // quality level switched — put the old asset back
            shadowAsset = asset;
            originalShadowDistance = asset.shadowDistance;
            appliedShadowDistance = -1f;
        }

        float pitch = Mathf.Max(NormalizePitch(transform.eulerAngles.x), 1f) * Mathf.Deg2Rad;
        float sin = Mathf.Max(Mathf.Sin(pitch), 0.0001f);
        float viewDepth = (transform.position.y + cam.orthographicSize * Mathf.Cos(pitch)) / sin;

        float want = Mathf.Clamp(viewDepth * shadowDistanceMargin,
                                 minShadowDistance, maxShadowDistance);
        if (Mathf.Abs(want - appliedShadowDistance) < 0.5f) return;

        appliedShadowDistance = want;
        shadowAsset.shadowDistance = want;
    }

    void OnDisable()
    {
        RestoreShadowDistance();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void RestoreShadowDistance()
    {
        if (shadowAsset == null) return;
        shadowAsset.shadowDistance = originalShadowDistance;
        shadowAsset = null;
        appliedShadowDistance = -1f;
    }

    /// <summary>
    /// The ground point at screen center — the free-look orbit pivot. Intersects the
    /// y=0 plane, then (when terrain exists) re-intersects at the terrain height there
    /// so tilting on a hill orbits the hilltop, not sea level below it.
    /// </summary>
    Vector3 GetViewCenterGroundPoint()
    {
        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        if (TryGetGroundPoint(screenCenter, out Vector3 point))
        {
            if (TerrainGrid.Instance != null)
            {
                float h = TerrainGrid.Instance.SampleHeight(point);
                if (h > 0.01f)
                {
                    Plane lifted = new Plane(Vector3.up, new Vector3(0f, h, 0f));
                    Ray ray = cam.ScreenPointToRay(screenCenter);
                    if (lifted.Raycast(ray, out float enter))
                        point = ray.GetPoint(enter);
                }
            }
            return point;
        }
        // Degenerate fallback (should not happen with a downward-tilted camera)
        return transform.position + transform.forward * 20f;
    }

    /// <summary>Euler X as signed pitch (350 becomes -10) so clamping works across wrap.</summary>
    static float NormalizePitch(float eulerX)
    {
        return eulerX > 180f ? eulerX - 360f : eulerX;
    }

    /// <summary>Where a screen ray meets the y=0 ground plane (ortho-safe).</summary>
    bool TryGetGroundPoint(Vector3 screenPos, out Vector3 world)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (groundPlane.Raycast(ray, out float enter))
        {
            world = ray.GetPoint(enter);
            return true;
        }
        world = Vector3.zero;
        return false;
    }
}

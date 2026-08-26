using UnityEngine;

/// <summary>
/// RTS camera: smoothed WASD pan (screen-relative), smoothed Q/E rotation,
/// smoothed scroll zoom, and middle-mouse grab-drag panning.
/// Pan speed scales with zoom so the world moves at a consistent screen speed.
/// Uses unscaled time so camera feel is identical at 1x/2x/4x game speed.
/// CameraShake (pure offset, LateUpdate) layers on top without conflict.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("WASD Movement (Screen-Relative)")]
    public float panSpeed = 25f;              // world units/sec at startOrthoSize zoom
    [Tooltip("Seconds to reach/leave full pan speed. Lower = snappier.")]
    public float panSmoothTime = 0.12f;

    [Header("Middle-Mouse Drag Pan")]
    public bool enableDragPan = true;

    [Header("Camera Rotation")]
    public float rotationSpeed = 90f;         // degrees per second
    [Tooltip("Seconds for rotation to ease in/out.")]
    public float rotationSmoothTime = 0.08f;
    public KeyCode rotateLeftKey = KeyCode.Q;
    public KeyCode rotateRightKey = KeyCode.E;

    [Header("Zoom (Orthographic)")]
    public float zoomSpeed = 2f;              // ortho size change per scroll unit
    [Tooltip("Seconds for zoom to ease toward its target.")]
    public float zoomSmoothTime = 0.12f;
    public float startOrthoSize = 15f;
    public float minOrthoSize = 5f;
    public float maxOrthoSize = 30f;

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

    // Drag-pan state
    bool isDragging;
    Vector3 dragAnchor;                       // world point grabbed at drag start
    static readonly Plane groundPlane = new Plane(Vector3.up, 0f);

    void Awake()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("CameraController: No Main Camera found!");
            return;
        }
        cam.orthographic = true;
        cam.orthographicSize = startOrthoSize;
        targetOrthoSize = startOrthoSize;
    }

    void Update()
    {
        if (cam == null) return;
        float dt = Time.unscaledDeltaTime;

        UpdateZoom(dt);
        UpdateDragPan();
        UpdateKeyboardPan(dt);
        UpdateRotation(dt);

        if (useBounds)
        {
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.z = Mathf.Clamp(p.z, minBounds.y, maxBounds.y);
            transform.position = p;
        }
    }

    void UpdateKeyboardPan(float dt)
    {
        float horizontal = Input.GetAxisRaw("Horizontal");  // A/D
        float vertical = Input.GetAxisRaw("Vertical");      // W/S

        Vector3 targetVel = Vector3.zero;
        if (!isDragging && (Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f))
        {
            // Screen-relative: W moves toward 12 o'clock regardless of rotation
            Vector3 forward = transform.forward; forward.y = 0; forward.Normalize();
            Vector3 right = transform.right;     right.y = 0;   right.Normalize();
            Vector3 dir = (forward * vertical + right * horizontal).normalized;

            // Scale by zoom so screen-space speed stays constant
            float zoomScale = cam.orthographicSize / Mathf.Max(startOrthoSize, 0.01f);
            targetVel = dir * panSpeed * zoomScale;
        }

        panVelocity = Vector3.SmoothDamp(panVelocity, targetVel, ref panVelocityRef,
                                         panSmoothTime, Mathf.Infinity, dt);
        if (panVelocity.sqrMagnitude > 0.0001f)
            transform.position += panVelocity * dt;
    }

    void UpdateDragPan()
    {
        if (!enableDragPan) return;

        if (Input.GetMouseButtonDown(2) && TryGetGroundPoint(Input.mousePosition, out Vector3 hit))
        {
            isDragging = true;
            dragAnchor = hit;
            panVelocity = Vector3.zero;       // drag is 1:1, kill any glide
            panVelocityRef = Vector3.zero;
        }

        if (isDragging)
        {
            if (!Input.GetMouseButton(2))
            {
                isDragging = false;
                return;
            }
            if (TryGetGroundPoint(Input.mousePosition, out Vector3 current))
            {
                // Move so the grabbed world point stays under the cursor
                Vector3 offset = dragAnchor - current;
                offset.y = 0;
                transform.position += offset;
            }
        }
    }

    void UpdateRotation(float dt)
    {
        float target = 0f;
        if (Input.GetKey(rotateLeftKey)) target -= rotationSpeed;
        if (Input.GetKey(rotateRightKey)) target += rotationSpeed;

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
            targetOrthoSize = Mathf.Clamp(targetOrthoSize - scroll * zoomSpeed,
                                          minOrthoSize, maxOrthoSize);
        }

        if (Mathf.Abs(cam.orthographicSize - targetOrthoSize) > 0.001f)
        {
            cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, targetOrthoSize,
                                                    ref zoomVelocityRef, zoomSmoothTime,
                                                    Mathf.Infinity, dt);
        }
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

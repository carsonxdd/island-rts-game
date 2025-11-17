using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("WASD Movement (Screen-Relative)")]
    public float panSpeed = 25f;

    [Header("Camera Rotation")]
    public float rotationSpeed = 90f;  // degrees per second
    public KeyCode rotateLeftKey = KeyCode.Q;
    public KeyCode rotateRightKey = KeyCode.E;

    [Header("Zoom (Orthographic)")]
    public float zoomSpeed = 2f;
    public float startOrthoSize = 15f;  // Starting zoom level
    public float minOrthoSize = 5f;     // Minimum (closest zoom)
    public float maxOrthoSize = 30f;    // Maximum (farthest zoom)

    [Header("Bounds")]
    public bool useBounds = false;  // Disable bounds for now
    public Vector2 minBounds = new Vector2(-120, -120);
    public Vector2 maxBounds = new Vector2(120, 120);

    Camera cam;

    void Awake()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("CameraController: No Main Camera found!");
            return;
        }
        cam.orthographic = true;   // Force orthographic
        cam.orthographicSize = startOrthoSize;  // Force starting zoom
    }

    void Update()
    {
        // === WASD Movement (Screen-Relative) ===
        // W moves "up" on screen (12 o'clock), regardless of camera rotation
        float horizontal = Input.GetAxisRaw("Horizontal");  // A/D
        float vertical = Input.GetAxisRaw("Vertical");      // W/S

        if (Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f)
        {
            // Get camera's forward and right directions, but flatten to XZ plane
            Vector3 forward = transform.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 right = transform.right;
            right.y = 0;
            right.Normalize();

            // Calculate movement direction based on camera orientation
            Vector3 move = (forward * vertical + right * horizontal).normalized;

            // Apply movement
            transform.position += move * panSpeed * Time.deltaTime;
        }

        // === Camera Rotation (Q/E) ===
        if (Input.GetKey(rotateLeftKey))
        {
            // Rotate camera left (counter-clockwise around Y axis)
            transform.Rotate(Vector3.up, -rotationSpeed * Time.deltaTime, Space.World);
        }
        if (Input.GetKey(rotateRightKey))
        {
            // Rotate camera right (clockwise around Y axis)
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        }

        // === Zoom (Mouse Wheel) ===
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            // Scroll is already small (~0.1), multiply by zoom speed
            cam.orthographicSize = Mathf.Clamp(
                cam.orthographicSize - scroll * zoomSpeed,
                minOrthoSize, maxOrthoSize
            );
        }

        // === Clamp Position Within Bounds ===
        if (useBounds)
        {
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.z = Mathf.Clamp(p.z, minBounds.y, maxBounds.y);
            transform.position = p;
        }
    }
}

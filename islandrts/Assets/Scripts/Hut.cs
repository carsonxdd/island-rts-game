using UnityEngine;

public class Hut : MonoBehaviour
{
    [Header("Building Placement")]
    public float noBuildRadius = 2.5f;  // Creates 5x5 square no-build zone (1 grid cell buffer)

    void Start()
    {
        Debug.Log($"Hut: Initialized at {transform.position} with no-build radius {noBuildRadius}");
    }

    // Visual helper in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}

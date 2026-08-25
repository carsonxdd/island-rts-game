using UnityEngine;

/// <summary>
/// Per-instance visual variance for trees: picks one of the authored mesh variants and
/// applies a random yaw + slight scale jitter to the Model child, so a forest spawned
/// from one prefab doesn't read as copy-paste.
///
/// Visual-only by design: it touches the Model child, never the root, so ResourceNode's
/// depletion scaling (root scale) and the runtime NavMeshObstacle are unaffected. All
/// variant meshes must share the same material key order (they come from the same
/// BroadleafTree builder), which is what makes the sharedMesh swap safe.
/// </summary>
public class TreeVariance : MonoBehaviour
{
    [Tooltip("Mesh variants sharing the same material list/order. Wired by LowPolyPlumber.")]
    public Mesh[] variantMeshes;

    [Tooltip("Uniform scale jitter applied to the Model child.")]
    public float minScale = 0.9f;
    public float maxScale = 1.12f;

    void Start()
    {
        Transform model = transform.Find("Model");
        if (model == null) return;

        MeshFilter filter = model.GetComponent<MeshFilter>();
        if (filter == null) filter = model.GetComponentInChildren<MeshFilter>();

        if (filter != null && variantMeshes != null && variantMeshes.Length > 0)
        {
            Mesh pick = variantMeshes[Random.Range(0, variantMeshes.Length)];
            if (pick != null) filter.sharedMesh = pick;
        }

        model.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        model.localScale = Vector3.one * Random.Range(minScale, maxScale);
    }
}

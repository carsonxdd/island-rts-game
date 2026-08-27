using UnityEngine;

/// <summary>
/// Per-instance visual variance for trees: picks one of the authored art variants and
/// applies a random yaw + slight scale jitter to the Model child, so a forest spawned
/// from one prefab doesn't read as copy-paste.
///
/// 2026-08-26: variants are now ART PREFAB references, not bare meshes — the picker
/// copies BOTH sharedMesh and sharedMaterials from the chosen prefab, so variants may
/// use different canopy palette trios (olive / deep green shades). The legacy
/// variantMeshes path is kept as a fallback for prefabs plumbed before this change
/// (mesh-only swap; requires identical material key order).
///
/// Visual-only by design: it touches the Model child, never the root, so ResourceNode's
/// depletion scaling (Model child too, lazily baselined) and the runtime NavMeshObstacle
/// are unaffected.
/// </summary>
public class TreeVariance : MonoBehaviour
{
    [Tooltip("Art prefab variants (mesh + materials copied from each). Wired by LowPolyPlumber.")]
    public GameObject[] variantPrefabs;

    [Tooltip("Legacy mesh-only variants sharing one material list/order. Used only when variantPrefabs is empty.")]
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

        if (filter != null)
        {
            if (variantPrefabs != null && variantPrefabs.Length > 0)
            {
                GameObject pick = variantPrefabs[Random.Range(0, variantPrefabs.Length)];
                if (pick != null)
                {
                    MeshFilter sourceFilter = pick.GetComponent<MeshFilter>();
                    MeshRenderer sourceRenderer = pick.GetComponent<MeshRenderer>();
                    if (sourceFilter != null && sourceFilter.sharedMesh != null)
                        filter.sharedMesh = sourceFilter.sharedMesh;

                    MeshRenderer targetRenderer = filter.GetComponent<MeshRenderer>();
                    if (sourceRenderer != null && targetRenderer != null)
                        targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
                }
            }
            else if (variantMeshes != null && variantMeshes.Length > 0)
            {
                Mesh pick = variantMeshes[Random.Range(0, variantMeshes.Length)];
                if (pick != null) filter.sharedMesh = pick;
            }
        }

        model.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        model.localScale = Vector3.one * Random.Range(minScale, maxScale);
    }
}

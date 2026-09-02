using UnityEngine;

/// <summary>
/// The environment-prop table for <see cref="PropScatter"/>: which art
/// prefabs go where on the generated island, described by terrain rules
/// (height band, slope band, grass tone) rather than by radial distance —
/// the island is a different shape every run, so "30 m from the centre"
/// means nothing, but "on the beach" or "on dark grass" always does.
///
/// Built from a code table by Tools > Island RTS > Low-Poly Templates >
/// Build Scatter Settings so prefab references resolve in the editor;
/// tune counts and bands on the asset afterwards.
/// </summary>
[CreateAssetMenu(fileName = "ScatterSettings", menuName = "Island RTS/Scatter Settings")]
public class ScatterSettings : ScriptableObject
{
    [System.Serializable]
    public class Rule
    {
        public GameObject prefab;
        [Tooltip("How many to try to place. Placement gives up per prop after maxTriesPerProp rejected candidates.")]
        public int count = 20;

        [Header("Where")]
        [Tooltip("Ground height band the prop may stand on (sea level 0; wading band is −0.4..0).")]
        public float minHeight = 0.6f;
        public float maxHeight = 7f;
        [Tooltip("Slope band (rise/run). Rocks like slopes; ferns don't.")]
        public float minSlope = 0f;
        public float maxSlope = 0.6f;
        [Tooltip("Grass tone band 0..1 (0 = dark grass, 1 = dry meadow). Ignored below the grass line.")]
        public float minTone = 0f;
        public float maxTone = 1f;

        [Header("Spacing / size")]
        [Tooltip("Minimum distance to any other scattered prop.")]
        public float spacing = 2.5f;
        public float minScale = 0.85f;
        public float maxScale = 1.2f;
    }

    [Tooltip("Nothing is placed inside this radius of the campfire site, so the build area stays clear.")]
    public float campfireClearing = 13f;
    [Tooltip("Nothing is placed inside this radius of the landing cove (the shipwreck lives there).")]
    public float coveClearing = 5f;
    [Tooltip("Lowest ground any prop may stand on — keeps decor out of deep water. Flotsam rules reach into the wading band above this.")]
    public float minGroundHeight = -0.35f;
    public int maxTriesPerProp = 40;
    [Tooltip("Combine the placed props into static batches after placement (fewer draw calls; props never move).")]
    public bool staticBatch = true;

    public Rule[] rules;
}

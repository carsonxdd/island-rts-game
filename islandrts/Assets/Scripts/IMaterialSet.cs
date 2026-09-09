/// <summary>
/// Implemented by anything that already owns the instanced material copies for its
/// renderers, so a second system can tint or fade the object without collecting a
/// second set (2026-09-08).
/// </summary>
/// <remarks>
/// Reading <c>Renderer.materials</c> INSTANTIATES. Two collectors on one object end up
/// writing to different copies of the same material, and whichever the renderer is
/// actually drawing with wins — which is how a hover highlight and a fade can both
/// look "sometimes broken". The rule is one collector per object; this interface is how
/// the second system finds it.
///
/// <see cref="OcclusionFade"/> asks for this before falling back to collecting its own,
/// which is what lets it be attached to a building that already has a hover glow.
/// </remarks>
public interface IMaterialSet
{
    /// <summary>The instanced material copies for every renderer slot, created on first call.</summary>
    UnityEngine.Material[] EnsureMaterials();
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helpers for tinting every material slot of a set of renderers.
///
/// Written for the Phase 10 art swap. The old primitive prefabs had exactly one material
/// per renderer, so hover/highlight/darken code could get away with `renderer.material`,
/// which only ever touches slot 0. The low-poly art meshes are multi-submesh (8 slots on a
/// unit, 4-6 on a building), so slot-0-only tinting leaves most of the object untinted.
///
/// Collect() is called once in Start and returns instanced copies of EVERY slot, so the
/// per-frame tint path afterwards is a plain array walk with zero allocation - reading
/// `renderer.materials` inside Update would allocate a fresh array every call.
/// </summary>
public static class RendererTint
{
    // Reused across Collect calls so building the flat list allocates nothing permanent.
    private static readonly List<Material> collectBuffer = new List<Material>();

    /// <summary>
    /// Instantiate per-slot material copies for every renderer and return them as one flat
    /// array. Reading Renderer.materials instantiates the copies and assigns them back to
    /// the renderer, so the returned array is what the renderers are actually drawing with.
    /// </summary>
    public static Material[] Collect(Renderer[] renderers)
    {
        collectBuffer.Clear();

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                Material[] slots = renderers[i].materials;
                for (int s = 0; s < slots.Length; s++)
                {
                    if (slots[s] != null) collectBuffer.Add(slots[s]);
                }
            }
        }

        return collectBuffer.ToArray();
    }

    /// <summary>Single-renderer overload.</summary>
    public static Material[] Collect(Renderer renderer)
    {
        if (renderer == null) return new Material[0];
        return renderer.materials;
    }

    /// <summary>Snapshot the current colors so they can be restored later.</summary>
    public static Color[] CaptureColors(Material[] materials)
    {
        if (materials == null) return new Color[0];

        Color[] colors = new Color[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null) colors[i] = materials[i].color;
        }
        return colors;
    }

    /// <summary>Tint every slot to one color.</summary>
    public static void SetColor(Material[] materials, Color color)
    {
        if (materials == null) return;

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null) materials[i].color = color;
        }
    }

    /// <summary>Restore each slot to its captured color.</summary>
    public static void RestoreColors(Material[] materials, Color[] originals)
    {
        if (materials == null || originals == null) return;

        int count = materials.Length < originals.Length ? materials.Length : originals.Length;
        for (int i = 0; i < count; i++)
        {
            if (materials[i] != null) materials[i].color = originals[i];
        }
    }
}

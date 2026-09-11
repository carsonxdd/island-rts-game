using UnityEngine;

/// <summary>
/// A random stream for things that are only LOOKED at: cloud puffs, shimmer
/// phases, cosmetic timer jitter.
///
/// UnityEngine.Random is one global sequence, seeded per run by the balance
/// harness (<c>Random.InitState(cfg.seed)</c>) and drawn from by gameplay — AI
/// stagger, spawn jitter, ORCA avoidance priorities. Anything cosmetic that
/// draws from it therefore MOVES gameplay's numbers, and systems that exist only
/// when something is being rendered move them by a different amount depending on
/// whether a window is open. That is how a "purely visual" toggle quietly turns
/// into a balance change.
///
/// So cosmetics draw from here instead. Nothing gameplay reads may ever call it,
/// and nothing that calls it may ever decide anything.
/// </summary>
public static class CosmeticRng
{
    // Seeded off the frame count rather than a constant so two windows opened at
    // the same moment do not produce identical clouds. It is decorative noise;
    // it never has to be reproducible.
    private static readonly System.Random rng = new System.Random(System.Environment.TickCount);

    /// <summary>0..1, like UnityEngine.Random.value.</summary>
    public static float Value => (float)rng.NextDouble();

    /// <summary>Uniform in [min, max), like UnityEngine.Random.Range(float, float).</summary>
    public static float Range(float min, float max) => min + (max - min) * (float)rng.NextDouble();

    /// <summary>A point inside the unit circle, like UnityEngine.Random.insideUnitCircle.</summary>
    public static Vector2 InsideUnitCircle
    {
        get
        {
            // Rejection sampling: a polar draw clusters points at the centre.
            for (int i = 0; i < 16; i++)
            {
                Vector2 p = new Vector2(Range(-1f, 1f), Range(-1f, 1f));
                if (p.sqrMagnitude <= 1f) return p;
            }
            return Vector2.zero;
        }
    }
}

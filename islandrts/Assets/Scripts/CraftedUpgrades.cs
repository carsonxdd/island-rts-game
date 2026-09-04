using UnityEngine;

/// <summary>
/// Global multipliers granted by Workshop-tier research (2026-08-26; fed by
/// <see cref="ResearchCatalog"/> since 2026-09-03 — the old one-time Workshop
/// recipes became research entries). Static and read at the point of effect —
/// gathering (GatherExecutor), construction (ConstructionSite) — so an upgrade
/// applies to every existing and future unit the moment its research completes.
///
/// The warrior damage global is gone: weapons are equipment now, and a better
/// spear (Slice 3) is how warriors hit harder.
///
/// Statics reset on play via [RuntimeInitializeOnLoadMethod].
/// </summary>
public static class CraftedUpgrades
{
    public static float GatherRateMult { get; private set; } = 1f;
    public static float BuildSpeedMult { get; private set; } = 1f;

    public static void SetGatherRate(float mult) { GatherRateMult = mult; }
    public static void SetBuildSpeed(float mult) { BuildSpeedMult = mult; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        GatherRateMult = 1f;
        BuildSpeedMult = 1f;
    }
}

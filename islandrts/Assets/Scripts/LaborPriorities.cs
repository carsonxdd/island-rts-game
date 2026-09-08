using UnityEngine;

/// <summary>
/// Colony-wide weights for what a utility colonist reaches for first
/// (2026-09-07): Build, Craft, Repair, Forage, each 0..1. Read at the point of
/// effect by <see cref="LaborPriority"/>, the consideration every jobless
/// action carries, so a slider on the campfire panel changes every idle
/// colonist's next decision with nothing pushed. Zero switches that work off
/// (the consideration early-outs the action).
///
/// The defaults are the old fixed base priorities. Distance still matters:
/// every scan scores by how far the work is, and these only weight that. A
/// specialist ignores the weights of the trades they do not do.
///
/// Run-scoped statics: they survive a Restart like the other run choices and
/// reset when the player next launches. The balance sim runs on the defaults.
/// </summary>
public static class LaborPriorities
{
    public const float DefaultBuild = 1f;
    public const float DefaultCraft = 0.95f;
    public const float DefaultRepair = 0.9f;
    public const float DefaultForage = 0.85f;

    public static float Build = DefaultBuild;
    public static float Craft = DefaultCraft;
    public static float Repair = DefaultRepair;
    public static float Forage = DefaultForage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Build = DefaultBuild;
        Craft = DefaultCraft;
        Repair = DefaultRepair;
        Forage = DefaultForage;
    }

    /// <summary>The weight for a trade; <see cref="Worker.Specialty.Any"/> stands for Forage (the utility-only work).</summary>
    public static float For(Worker.Specialty trade)
    {
        switch (trade)
        {
            case Worker.Specialty.Builder: return Build;
            case Worker.Specialty.Crafter: return Craft;
            case Worker.Specialty.Repairer: return Repair;
            default: return Forage;
        }
    }
}

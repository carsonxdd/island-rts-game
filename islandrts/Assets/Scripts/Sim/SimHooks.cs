/// <summary>
/// Global switch the balance-simulation harness flips before a headless run.
///
/// It exists so a handful of purely cosmetic systems (VFX, damage numbers,
/// floating state text, health bars) can opt out during simulation — they cost
/// real CPU and produce nothing a CSV can read. Every consumer is a single
/// early-return; nothing about gameplay, AI, or pathing is touched, so a
/// simulated run takes the same decisions a played run would.
///
/// Always compiled (a static bool is free), but only ever set by
/// <see cref="SimRunner"/>, which is editor/dev-build only.
/// </summary>
public static class SimHooks
{
    /// <summary>True while a headless balance run is driving the game.</summary>
    public static bool Simulating;
}

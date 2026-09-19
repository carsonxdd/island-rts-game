/// <summary>
/// 1 between this colonist's bedtime and wake time, else 0 (2026-09-16). The hours
/// come from their <see cref="Persona"/> via <c>bb.bedtime</c> / <c>bb.wakeTime</c>:
/// midnight to dawn for most, an hour either side for the traits. A sharp gate, not
/// a ramp, by decision: a colonist works into the night as before and turns in at
/// their hour; the executor delivers what they carry first.
///
/// Raiders wake the colony — a hostile in the density grid scores 0 so Flee wins and
/// everyone runs for the huts — EXCEPT a sleeper already garrisoned inside a hut
/// (<c>bb.asleepInHut</c>, set only by SleepExecutor): they are exactly where Flee
/// would put them, so they sleep through it instead of popping out and back in.
/// Zero-cost apart from one grid read, so it sits alone on the action and early-outs
/// the whole thing by day.
/// </summary>
public class SleepUrge : Consideration
{
    public SleepUrge(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        AIWorldState ws = AIWorldState.Instance;
        if (ws == null) return 0f;

        float t = ws.timeOfDay;
        if (t < bb.bedtime || t >= bb.wakeTime) return 0f;
        if (bb.asleepInHut) return 1f;

        return ws.GetNearbyHostileCount(bb.transform.position, bb.faction) > 0 ? 0f : 1f;
    }
}

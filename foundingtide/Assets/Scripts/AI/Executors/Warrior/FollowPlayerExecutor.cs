using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Warrior executor for the Follow stance (2026-09-07): shadow the player
/// character. Each warrior keeps its own spot a few metres off the character at
/// a fixed bearing (rolled once per executor, like Intercept's lateral spread),
/// so an escort forms a loose ring instead of a stack on the character's heels.
/// Walks whenever it falls outside <see cref="Leash"/>, stops inside
/// <see cref="Arrive"/>, re-paths only when the character has moved a step.
/// Fighting is Engage's job: <see cref="GuardStance.Allows"/> lets Engage take
/// anything that comes within reach of the character, and this action is the
/// thing to come back to afterwards.
/// </summary>
public class FollowPlayerExecutor : ActionExecutor
{
    public override string DisplayName => moving ? "Escorting" : "Guarding you";

    private const float Leash = 5.5f;          // farther than this from the character → walk
    private const float Arrive = 4f;           // inside this → stop (hysteresis with Leash)
    private const float Offset = 3f;           // the ring radius around the character
    private const float RepathMove = 1.5f;     // character moved this far since the last path → new one
    private const float StoppingDistance = 0.5f;

    private readonly Vector3 bearing;          // this warrior's spot on the ring
    private bool moving;
    private bool destinationQueued;
    private Vector3 lastDestination;
    private float originalStoppingDistance;

    public FollowPlayerExecutor()
    {
        float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        bearing = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    public override void OnEnter(AIBlackboard bb)
    {
        moving = false;
        destinationQueued = false;
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            // The warrior's stopping distance is tuned for attack reach (attackRange - 1,
            // 8 u for an archer); walking to a ring spot needs to actually get there.
            originalStoppingDistance = bb.agent.stoppingDistance;
            bb.agent.stoppingDistance = StoppingDistance;
        }
    }

    public override void OnUpdate(AIBlackboard bb)
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        if (pc == null || bb.agent == null || !bb.agent.enabled || !bb.agent.isOnNavMesh) return;   // StanceAllows re-gates

        Vector3 playerPos = pc.transform.position;
        float dist = Vector3.Distance(bb.transform.position, playerPos);

        if (!moving)
        {
            if (dist > Leash)
            {
                moving = true;
                destinationQueued = false;
                bb.agent.isStopped = false;
            }
            else
            {
                return;   // standing at our spot
            }
        }

        // A stuck reset nulls blackboard fields and ForceReevals — bail for this tick.
        if (bb.stuckResolver != null && bb.stuckResolver.UpdateMoving())
            return;

        // The escort's Formation slot (Ring by default), facing the nearest raider
        // when one is about; the per-warrior bearing when the formation is Loose.
        Vector3 facing = bb.nearestEnemy != null ? bb.nearestEnemy.position - playerPos : pc.transform.forward;
        Vector3 want;
        if (!Formation.TrySlot(bb.warrior, bb.faction, playerPos, facing, out want))
            want = playerPos + bearing * Offset;

        if (!destinationQueued || (want - lastDestination).sqrMagnitude > RepathMove * RepathMove)
            IssueMove(bb, want);

        if (dist <= Arrive)
        {
            moving = false;
            bb.agent.ResetPath();      // genuinely stopping — stand here until the character walks off
            bb.agent.isStopped = true;
        }
    }

    void IssueMove(AIBlackboard bb, Vector3 want)
    {
        NavMeshHit hit;
        Vector3 dest = NavMesh.SamplePosition(want, out hit, 2.5f, NavMesh.AllAreas) ? hit.position : want;
        destinationQueued = AINavHelper.TrySetDestination(bb.agent, dest);
        if (destinationQueued)
        {
            lastDestination = want;
            bb.agent.isStopped = false;
        }
    }

    public override void OnExit(AIBlackboard bb)
    {
        moving = false;
        destinationQueued = false;
        if (bb.agent != null && bb.agent.isOnNavMesh)
        {
            bb.agent.stoppingDistance = originalStoppingDistance;
            bb.agent.isStopped = false;
        }
    }
}

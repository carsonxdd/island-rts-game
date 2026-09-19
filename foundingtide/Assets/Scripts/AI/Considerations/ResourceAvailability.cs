using UnityEngine;

/// <summary>
/// Score from best available resource node quality.
/// Finds the best resource node by distance + claim penalty (ports existing Worker logic).
/// Returns 1.0 when a great node is nearby, 0.0 when none available.
/// Also caches the best resource node in the blackboard.
/// </summary>
/// <remarks>
/// The floor lives HERE, not in the curve (2026-09-08): a node at the search-radius edge
/// still scores <see cref="FloorWithNode"/> so a worker will walk to it, but "no node" is
/// a genuine 0 and the action early-outs. With the floor in the curve's yShift every
/// jobless colonist scored ~0.08 on Gather against Idle's 0.01, entered the executor with
/// nothing to walk to and stood at the fire labelled "Gathering" — the specialist bug.
/// Crowding counts workers already at the node as well as those walking to it.
/// Fog gate (2026-09-09, step 5): a node on ground the colony has never seen is not
/// scanned, so exploring is what puts trees on a worker's list — the same rule the
/// two pickup scans already apply. Scan time only: a worker at a node is standing on
/// explored ground by definition (its own <c>VisionSource</c>), so nothing re-checks.
/// Territory (2026-09-11, lap step 3): a node outside this colony's own patch scores
/// as if it were <see cref="Territory.OutsideHomePenalty"/> metres further away. A
/// preference, never a reject - when the home patch is empty every candidate pays the
/// same penalty and the nearest foreign node wins on its own.
/// </remarks>
public class ResourceAvailability : Consideration
{
    public ResourceAvailability(ResponseCurve curve) : base(curve) { }

    private const float FloorWithNode = 0.1f;    // a far node is still worth walking to
    private const float CrowdPenaltyMetres = 5f; // every worker committed to a node counts as 5 m further

    static bool fogSignalled;   // dev quest: once per launch, no per-scan string hashing

    public override float ScoreRaw(AIBlackboard bb)
    {
        // Idle colonists build and crafters work a bench; neither gathers. And a
        // worker whose hands hold a different type (job changed mid-trip) must
        // deliver before gathering the new one — ReturnUrgency's "no node
        // available" branch sends them home.
        if (!bb.hasJob || (bb.carryAmount > 0.01f && bb.carryType != bb.assignedResourceType))
        {
            bb.bestResource = null;
            return 0f;
        }

        ResourceNode bestNode = null;
        float bestScore = float.MaxValue;

        // Checks are ordered cheapest-first. The distance cull used to run LAST, so
        // HasWorkerRoom() — which compacts a claim list and can fire 8 NavMesh.SamplePosition
        // calls on a cache miss — ran for every same-type node on the island, including
        // ones 100m away. On the 150x150 map that is ~440 nodes per scan, per worker,
        // ~3x a second.
        Vector3 myPos = bb.transform.position;
        float searchSqr = bb.searchRadius * bb.searchRadius;
        FogOfWar fog = bb.faction.IsPlayer ? FogOfWar.Instance : null;   // rivals are omniscient
        float nearestHiddenSqr = float.MaxValue;   // closest same-type node the fog kept off the list

        var list = ResourceNode.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            ResourceNode node = list[i];
            if (node == null) continue;
            if (node.resourceType != bb.assignedResourceType) continue;

            // Cheap squared-distance cull before anything that touches node state.
            float sqr = (node.transform.position - myPos).sqrMagnitude;
            if (sqr > searchSqr) continue;

            float distance = Mathf.Sqrt(sqr);

            // Prune: score is distance + (workers * 5) and the crowd penalty is never
            // negative, so a node already further than the current best cannot win.
            // Skipping it here avoids the expensive availability checks below.
            if (distance >= bestScore) continue;

            // Fog gate: unexplored ground is not on the colony's map (one array read).
            if (fog != null && !fog.IsExplored(node.transform.position))
            {
                if (sqr < nearestHiddenSqr) nearestHiddenSqr = sqr;
                continue;
            }

            if (!node.HasResources()) continue;
            if (bb.IsNodeUnreachable(node)) continue;  // walled off / off-mesh - skip until its entry expires
            if (!node.HasWorkerRoom(bb.worker)) continue;  // at worker capacity - spill to another node

            // Scoring: distance + crowd penalty (walking there AND already working
            // there) + the territory penalty. Both penalties are non-negative and
            // both are added AFTER the prune above, which is what keeps plain
            // distance a valid lower bound on the score.
            float score = distance + (node.GetWorkerCount() * CrowdPenaltyMetres)
                        + Territory.PenaltyAt(node.transform.position, bb.faction);

            if (score < bestScore)
            {
                bestScore = score;
                bestNode = node;
            }
        }

        // Cache in blackboard
        bb.bestResource = bestNode;

        // Dev quest proof: the fog changed this worker's answer (a hidden node was
        // nearer than the one chosen, or there was nothing else to choose).
        if (!fogSignalled && nearestHiddenSqr < float.MaxValue
            && (bestNode == null || nearestHiddenSqr < (bestNode.transform.position - myPos).sqrMagnitude))
        {
            fogSignalled = true;
            DevQuests.Signal("fog:node_skipped");
        }

        if (bestNode == null) return 0f;

        // Crossed the line: paid the penalty and still chose it, so the colony's
        // own patch had nothing left. The dev quest wants to see that happen once.
        if (Territory.IsOutside(bestNode.transform.position, bb.faction)) Territory.NoteCrossing(bb.faction);

        // Normalize: floor = far / crowded, 1 = great (very close, unclaimed). Never 0
        // with a node in hand — 0 means "nothing to gather" and early-outs the action.
        return Mathf.Max(FloorWithNode, Mathf.Clamp01(1f - bestScore / bb.searchRadius));
    }
}

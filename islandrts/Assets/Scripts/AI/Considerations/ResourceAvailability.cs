using UnityEngine;

/// <summary>
/// Score from best available resource node quality.
/// Finds the best resource node by distance + claim penalty (ports existing Worker logic).
/// Returns 1.0 when a great node is nearby, 0.0 when none available.
/// Also caches the best resource node in the blackboard.
/// </summary>
public class ResourceAvailability : Consideration
{
    public ResourceAvailability(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        ResourceNode bestNode = null;
        float bestScore = float.MaxValue;

        // Checks are ordered cheapest-first. The distance cull used to run LAST, so
        // HasWorkerRoom() — which compacts a claim list and can fire 8 NavMesh.SamplePosition
        // calls on a cache miss — ran for every same-type node on the island, including
        // ones 100m away. On the 150x150 map that is ~440 nodes per scan, per worker,
        // ~3x a second.
        Vector3 myPos = bb.transform.position;
        float searchSqr = bb.searchRadius * bb.searchRadius;

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

            // Prune: score is distance + (claims * 5) and the claim penalty is never
            // negative, so a node already further than the current best cannot win.
            // Skipping it here avoids the expensive availability checks below.
            if (distance >= bestScore) continue;

            if (!node.HasResources()) continue;
            if (bb.IsNodeUnreachable(node)) continue;  // walled off / off-mesh - skip until its entry expires
            if (!node.HasWorkerRoom(bb.worker)) continue;  // at worker capacity - spill to another node

            // Existing scoring: distance + claim penalty
            float score = distance + (node.GetClaimCount() * 5f);

            if (score < bestScore)
            {
                bestScore = score;
                bestNode = node;
            }
        }

        // Cache in blackboard
        bb.bestResource = bestNode;

        if (bestNode == null) return 0f;

        // Normalize: 0 = terrible (at search radius), 1 = great (very close, unclaimed)
        return Mathf.Clamp01(1f - bestScore / bb.searchRadius);
    }
}

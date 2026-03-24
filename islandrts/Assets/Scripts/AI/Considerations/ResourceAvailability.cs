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

        for (int i = 0; i < ResourceNode.ActiveList.Count; i++)
        {
            ResourceNode node = ResourceNode.ActiveList[i];
            if (node == null) continue;
            if (node.resourceType != bb.assignedResourceType) continue;
            if (!node.HasResources()) continue;

            float distance = Vector3.Distance(bb.transform.position, node.transform.position);
            if (distance > bb.searchRadius) continue;

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
        bb.bestResourceScore = bestScore;

        if (bestNode == null) return 0f;

        // Normalize: 0 = terrible (at search radius), 1 = great (very close, unclaimed)
        return Mathf.Clamp01(1f - bestScore / bb.searchRadius);
    }
}

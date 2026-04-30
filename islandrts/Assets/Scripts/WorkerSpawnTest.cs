using UnityEngine;

// Simple test script to spawn workers properly
public class WorkerSpawnTest : MonoBehaviour
{
    public BaseBuilding campfire;

    void Start()
    {
        // Auto-find campfire if not assigned
        if (campfire == null)
        {
            campfire = GetComponent<BaseBuilding>();
            if (campfire == null)
            {
                Debug.LogError("WorkerSpawnTest: No BaseBuilding found! Make sure this is on the Campfire.");
            }
        }
    }

    void Update()
    {
        if (campfire == null) return;

        // Press 1 to spawn wood worker
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            campfire.AssignWorker(ResourceNode.ResourceType.Wood);
        }

        // Press 2 to spawn food worker
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            campfire.AssignWorker(ResourceNode.ResourceType.Food);
        }

        // Press 3 to spawn stone worker
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            campfire.AssignWorker(ResourceNode.ResourceType.Stone);
        }
    }
}

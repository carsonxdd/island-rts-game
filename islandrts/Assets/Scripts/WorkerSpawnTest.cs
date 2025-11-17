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
            Debug.Log("Spawned Wood Worker! Press 1 for more.");
        }

        // Press 2 to spawn food worker
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            campfire.AssignWorker(ResourceNode.ResourceType.Food);
            Debug.Log("Spawned Food Worker! Press 2 for more.");
        }

        // Press 3 to spawn stone worker
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            campfire.AssignWorker(ResourceNode.ResourceType.Stone);
            Debug.Log("Spawned Stone Worker! Press 3 for more.");
        }
    }
}

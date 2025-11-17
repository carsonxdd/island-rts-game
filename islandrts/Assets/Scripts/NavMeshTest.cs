using UnityEngine;
using UnityEngine.AI;

// Simple test script to verify NavMesh is working
public class NavMeshTest : MonoBehaviour
{
    private NavMeshAgent agent;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (agent != null)
        {
            // Walk to a random position on click
            Debug.Log("NavMeshTest: Click anywhere on the ground to make worker walk there!");
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                agent.SetDestination(hit.point);
                Debug.Log($"NavMeshTest: Walking to {hit.point}");
            }
        }
    }
}

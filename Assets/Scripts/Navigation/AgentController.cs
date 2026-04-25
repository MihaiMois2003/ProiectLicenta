using UnityEngine;
using UnityEngine.AI;

public class AgentController : MonoBehaviour
{
    [Header("Agent Settings")]
    public float moveSpeed = 3.5f;
    public string agentID;

    private NavMeshAgent navAgent;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        navAgent.speed = moveSpeed;

        // ID unic pentru fiecare agent
        if (string.IsNullOrEmpty(agentID))
            agentID = System.Guid.NewGuid().ToString().Substring(0, 8);
    }

    // Trimite agentul la o destinatie
    public void MoveTo(Vector3 destination)
    {
        if (navAgent.isOnNavMesh)
            navAgent.SetDestination(destination);
    }

    // Opreste agentul
    public void Stop()
    {
        if (navAgent.isOnNavMesh)
            navAgent.ResetPath();
    }

    // Verifica daca agentul a ajuns la destinatie
    public bool HasReachedDestination()
    {
        if (!navAgent.pathPending && navAgent.remainingDistance <= navAgent.stoppingDistance)
            return true;
        return false;
    }

    public void SetSpeed(float speed)
    {
        navAgent.speed = speed;
    }
}
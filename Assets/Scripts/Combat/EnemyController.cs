using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    [Header("Patrol Settings")]
    public Transform[] patrolPoints;
    public float moveSpeed = 2f;

    private NavMeshAgent navAgent;
    private int currentPatrolIndex = 0;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent != null)
            navAgent.speed = moveSpeed;
    }

    void Update()
    {
        Patrol();
    }

    void Patrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        if (navAgent == null) return;

        if (!navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
            navAgent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }
}
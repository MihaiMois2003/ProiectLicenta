using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    [Header("Patrol Settings")]
    public float moveSpeed = 2f;
    public float patrolRadius = 20f;
    public float waitAtPointTime = 0f;

    private NavMeshAgent navAgent;
    private Vector3 patrolTarget;
    private float waitTimer = 0f;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent != null)
            navAgent.speed = moveSpeed;
    }

    void Start()
    {
        SetNewPatrolTarget();
    }

    void Update()
    {
        Patrol();
    }

    void Patrol()
    {
        if (navAgent == null) return;

        if (!navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= waitAtPointTime)
            {
                waitTimer = 0f;
                SetNewPatrolTarget();
            }
        }
    }

    void SetNewPatrolTarget()
    {
        // Cauta un punct random valid pe NavMesh
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomPoint = transform.position +
                new Vector3(
                    Random.Range(-patrolRadius, patrolRadius),
                    0,
                    Random.Range(-patrolRadius, patrolRadius));

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(
                randomPoint, out hit, 5f,
                UnityEngine.AI.NavMesh.AllAreas))
            {
                patrolTarget = hit.position;
                navAgent.SetDestination(patrolTarget);
                return;
            }
        }
    }
}
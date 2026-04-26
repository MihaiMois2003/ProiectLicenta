using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    [Header("Settings")]
    public float normalSpeed = 2f;
    public float fleeSpeed = 5f;
    public float patrolRadius = 20f;

    [Header("Secondary Enemies")]
    public GameObject secondaryEnemyPrefab1;
    public GameObject secondaryEnemyPrefab2;
    public Vector3 spawnPosition1 = new Vector3(0, 1, 20);
    public Vector3 spawnPosition2 = new Vector3(2, 1, 20);

    private NavMeshAgent navAgent;
    private TacticalBlackboard blackboard;
    private bool enemiesLiberated = false;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        navAgent.speed = normalSpeed;
    }

    void Start()
    {
        blackboard = TacticalBlackboard.Instance;

        if (blackboard != null)
            blackboard.mainEnemy = transform;

        SetNewPatrolTarget();
    }

    void Update()
    {
        if (blackboard != null &&
            (blackboard.combatState == CombatState.Engaging ||
             blackboard.combatState == CombatState.Combat))
        {
            CheckLiberation();
            Flee();
        }
        else
        {
            Patrol();
        }
    }

    void CheckLiberation()
    {
        if (enemiesLiberated) return;

        HealthSystem hs = GetComponent<HealthSystem>();
        if (hs != null && hs.GetHPPercentage() < 0.75f)
            LiberateEnemies();
    }

    void LiberateEnemies()
    {
        enemiesLiberated = true;

        if (secondaryEnemyPrefab1 != null)
        {
            GameObject e1 = Instantiate(secondaryEnemyPrefab1,
                spawnPosition1, Quaternion.identity);
            e1.GetComponent<SecondaryEnemyController>()?.Liberate();
        }

        if (secondaryEnemyPrefab2 != null)
        {
            GameObject e2 = Instantiate(secondaryEnemyPrefab2,
                spawnPosition2, Quaternion.identity);
            e2.GetComponent<SecondaryEnemyController>()?.Liberate();
        }

        Debug.Log("[Enemy] Inamici secundari spawned!");
    }

    void Flee()
    {
        navAgent.speed = fleeSpeed;

        if (!navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance)
            SetNewFleeTarget();
    }

    void SetNewFleeTarget()
    {
        AgentBehaviorTree leader = blackboard?.GetLeader();
        if (leader == null) return;

        for (int i = 0; i < 15; i++)
        {
            Vector3 randomPoint = new Vector3(
                Random.Range(-20f, 20f),
                0,
                Random.Range(-20f, 20f));

            float distFromLeader = Vector3.Distance(
                randomPoint, leader.transform.position);
            if (distFromLeader < 10f) continue;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 3f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
        }

        SetNewPatrolTarget();
    }

    void Patrol()
    {
        navAgent.speed = normalSpeed;

        if (!navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance)
            SetNewPatrolTarget();
    }

    void SetNewPatrolTarget()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomPoint = new Vector3(
                Random.Range(-patrolRadius, patrolRadius),
                0,
                Random.Range(-patrolRadius, patrolRadius));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 5f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
        }
    }

    public float GetHPPercentage()
    {
        HealthSystem hs = GetComponent<HealthSystem>();
        return hs != null ? hs.GetHPPercentage() : 1f;
    }
}
using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    [Header("Settings")]
    public float normalSpeed = 2f;
    public float fleeSpeed = 5f;
    public float patrolRadius = 20f;
    public float liberationDistance = 2f;

    private NavMeshAgent navAgent;
    private TacticalBlackboard blackboard;
    private SecondaryEnemyController[] secondaryEnemies;
    private bool enemiesLiberated = false;
    private Vector3 liberationPoint;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        navAgent.speed = normalSpeed;
    }

    void Start()
    {
        blackboard = TacticalBlackboard.Instance;
        secondaryEnemies = FindObjectsOfType<SecondaryEnemyController>();

        if (secondaryEnemies.Length >= 2)
        {
            liberationPoint = (secondaryEnemies[0].transform.position +
                secondaryEnemies[1].transform.position) / 2f;
        }

        if (blackboard != null)
            blackboard.mainEnemy = transform;

        SetNewPatrolTarget();
    }

    void Update()
    {
        if (blackboard != null && blackboard.combatState == CombatState.Combat)
            Flee();
        else
            Patrol();
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

    public void TriggerLiberation()
    {
        if (enemiesLiberated) return;
        enemiesLiberated = true;
        foreach (var enemy in secondaryEnemies)
            enemy.Liberate();
    }

    public float GetHPPercentage()
    {
        HealthSystem hs = GetComponent<HealthSystem>();
        return hs != null ? hs.GetHPPercentage() : 1f;
    }
}
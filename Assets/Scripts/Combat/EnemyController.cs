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

        // Punctul de eliberare = mijlocul dintre cei 2 inamici secundari
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
        {
            if (!enemiesLiberated)
                MoveToLiberate();
            else
                Flee();
        }
        else
        {
            Patrol();
        }
    }

    void MoveToLiberate()
    {
        navAgent.speed = fleeSpeed;
        navAgent.SetDestination(liberationPoint);

        // Verifica daca a ajuns la punctul de eliberare
        float dist = Vector3.Distance(transform.position, liberationPoint);
        if (dist <= liberationDistance)
        {
            LiberateEnemies();
        }
    }

    void LiberateEnemies()
    {
        enemiesLiberated = true;
        foreach (var enemy in secondaryEnemies)
            enemy.Liberate();

        Debug.Log("[Enemy] Inamici secundari eliberati!");
    }

    void Flee()
    {
        navAgent.speed = fleeSpeed;

        AgentBehaviorTree leader = blackboard?.GetLeader();
        if (leader == null) return;

        // Fuge in directia opusa leaderului
        Vector3 fleeDirection = (transform.position -
            leader.transform.position).normalized;

        // Incearca distante din ce in ce mai mici pana gaseste punct valid
        for (int i = 10; i >= 2; i--)
        {
            Vector3 fleeTarget = transform.position + fleeDirection * i;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(fleeTarget, out hit, 3f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
        }

        // Fallback - punct random valid pe harta
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
}
using UnityEngine;
using UnityEngine.AI;

public class SecondaryEnemyController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Viteza cand fuge de agenti in Faza 2 normala. " +
             "Tine-o sub catchUpSpeed-ul agentilor ca sa fie prinsi.")]
    public float fleeSpeed = 3.5f;
    public float chaseSpeed = 4f;

    private NavMeshAgent navAgent;
    private TacticalBlackboard blackboard;
    private bool isLiberated = false;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        navAgent.enabled = false;
    }

    void Start()
    {
        blackboard = TacticalBlackboard.Instance;
    }

    void Update()
    {
        if (!isLiberated) return;

        // Fallback daca Start() nu a apucat sa ruleze
        if (blackboard == null)
        {
            blackboard = TacticalBlackboard.Instance;
            if (blackboard == null) return;
        }

        if (blackboard.rolesReversed)
        {
            ChaseAssignedGroup();
        }
        else
        {
            // Faza 2 normala: fuge de grupul de agenti care il urmareste
            FleeFromAssignedGroup();
        }
    }

    public void Liberate()
    {
        isLiberated = true;
        navAgent.enabled = true;
        navAgent.speed = fleeSpeed;

        // Fallback: daca Start() inca nu a rulat, ia referinta direct
        if (blackboard == null)
            blackboard = TacticalBlackboard.Instance;

        // Nu setam o destinatie aici - Update() va apela FleeFromAssignedGroup()
        // care va asigna o destinatie corecta dupa ce ActivatePhase2() creeaza grupurile.
    }

    void StopMoving()
    {
        if (navAgent.isOnNavMesh && navAgent.hasPath)
            navAgent.ResetPath();
    }

    // Faza 2 normala: fuge de centrul grupului care il urmareste
    void FleeFromAssignedGroup()
    {
        navAgent.speed = fleeSpeed;

        if (!navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance)
            SetNewFleeTarget();
    }

    void SetNewFleeTarget()
    {
        // Gaseste grupul care ma urmareste pe mine
        EnemyGroup myGroup = blackboard.GetGroupAssignedToEnemy(transform);
        Vector3 threatCenter;

        if (myGroup != null && myGroup.agents.Count > 0)
        {
            // Calculeaza centrul agentilor vii din grup
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (AgentBehaviorTree a in myGroup.agents)
            {
                HealthSystem hs = a.GetComponent<HealthSystem>();
                if (hs == null || hs.isDead) continue;
                sum += a.transform.position;
                count++;
            }
            threatCenter = count > 0 ? sum / count : transform.position;
        }
        else
        {
            // Nimeni asignat -> fuge la random
            SetRandomFleeTarget();
            return;
        }

        // Alege un punct in directia opusa fata de grup
        Vector3 awayDir = (transform.position - threatCenter);
        awayDir.y = 0;
        if (awayDir.sqrMagnitude < 0.1f)
        {
            SetRandomFleeTarget();
            return;
        }
        awayDir.Normalize();

        // Cauta un punct valid pe NavMesh la 10-15 unitati in directia opusa
        for (int i = 0; i < 10; i++)
        {
            float fleeDist = Random.Range(10f, 15f);
            Vector3 candidate = transform.position +
                awayDir * fleeDist +
                new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
        }

        // Fallback
        SetRandomFleeTarget();
    }

    void SetRandomFleeTarget()
    {
        for (int i = 0; i < 15; i++)
        {
            Vector3 randomPoint = new Vector3(
                Random.Range(-20f, 20f),
                0,
                Random.Range(-20f, 20f));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 3f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
        }
    }

    void ChaseAssignedGroup()
    {
        navAgent.speed = chaseSpeed;

        EnemyGroup myGroup = blackboard.GetGroupAssignedToEnemy(transform);
        if (myGroup == null || myGroup.agents.Count == 0)
        {
            ChaseNearestSniper();
            return;
        }

        Vector3 groupCenter = Vector3.zero;
        int count = 0;
        foreach (AgentBehaviorTree a in myGroup.agents)
        {
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            groupCenter += a.transform.position;
            count++;
        }

        if (count == 0)
        {
            ChaseNearestSniper();
            return;
        }

        groupCenter /= count;

        if (navAgent.isOnNavMesh)
            navAgent.SetDestination(groupCenter);
    }

    void ChaseNearestSniper()
    {
        Transform nearestSniper = null;
        float minDist = Mathf.Infinity;
        foreach (AgentBehaviorTree a in blackboard.allAgents)
        {
            if (a == null || a.role != AgentRole.Sniper) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            float dist = Vector3.Distance(transform.position, a.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearestSniper = a.transform;
            }
        }

        if (nearestSniper != null && navAgent.isOnNavMesh)
            navAgent.SetDestination(nearestSniper.position);
        else
            StopMoving();
    }
}
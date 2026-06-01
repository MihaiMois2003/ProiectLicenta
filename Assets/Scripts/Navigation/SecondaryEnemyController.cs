using UnityEngine;
using UnityEngine.AI;

public class SecondaryEnemyController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Viteza cand fuge de agenti in Faza 2 normala. " +
             "Tine-o sub catchUpSpeed-ul agentilor ca sa fie prinsi.")]
    public float fleeSpeed = 3.5f;
    public float chaseSpeed = 4f;

    [Header("Fuga haotica")]
    public float fleeRetargetInterval = 0.6f;
    public float fleeAngleJitter = 75f;
    public float fleeStepDistance = 11f;
    private float fleeTimer = 0f;

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
        navAgent.isStopped = false;

        fleeTimer += Time.deltaTime;

        bool reached = !navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance + 0.1f;
        bool noPath = !navAgent.hasPath && !navAgent.pathPending;

        if (fleeTimer >= fleeRetargetInterval || reached || noPath)
        {
            fleeTimer = 0f;
            SetNewFleeTarget();
        }
    }

    void SetNewFleeTarget()
    {
        // Gaseste grupul care ma urmareste pe mine
        EnemyGroup myGroup = blackboard.GetGroupAssignedToEnemy(transform);
        Vector3 awayDir;

        if (myGroup != null && myGroup.agents.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (AgentBehaviorTree a in myGroup.agents)
            {
                if (a == null) continue;
                HealthSystem hs = a.GetComponent<HealthSystem>();
                if (hs == null || hs.isDead) continue;
                sum += a.transform.position;
                count++;
            }
            Vector3 threatCenter = count > 0 ? sum / count : transform.position;
            awayDir = transform.position - threatCenter;
        }
        else
        {
            awayDir = Random.insideUnitSphere;
        }

        awayDir.y = 0;
        if (awayDir.sqrMagnitude < 0.01f)
            awayDir = Random.insideUnitSphere;
        awayDir.y = 0;
        awayDir.Normalize();

        // Jitter unghiular -> fuga haotica
        float jitter = Random.Range(-fleeAngleJitter, fleeAngleJitter);
        Vector3 dir = Quaternion.Euler(0, jitter, 0) * awayDir;

        for (int i = 0; i < 8; i++)
        {
            float fleeDist = fleeStepDistance * Random.Range(0.6f, 1.2f);
            Vector3 candidate = transform.position + dir * fleeDist;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 4f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
            dir = Quaternion.Euler(0, Random.Range(-90f, 90f), 0) * dir;
        }

        // ANTI-BLOCAJ garantat
        NavMeshHit fallbackHit;
        if (NavMesh.SamplePosition(
                transform.position + Random.insideUnitSphere * 5f,
                out fallbackHit, 6f, NavMesh.AllAreas))
            navAgent.SetDestination(fallbackHit.position);
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
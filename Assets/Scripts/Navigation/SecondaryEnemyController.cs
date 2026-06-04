using UnityEngine;
using UnityEngine.AI;

public class SecondaryEnemyController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Viteza de plimbare haotica in Faza 2 normala. " +
             "Tine-o sub catchUpSpeed-ul agentilor ca sa fie prinsi.")]
    public float wanderSpeed = 3f;
    public float chaseSpeed = 5.5f;

    [Header("Plimbare haotica")]
    public float wanderRetargetInterval = 1.5f;
    public float fleeRetargetInterval = 0.8f;
    public float mapHalfExtent = 20f;
    private float wanderTimer = 0f;

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
            // Faza 2 normala: se plimba haotic prin toata harta (trage din mers)
            Wander();
        }
    }

    public void Liberate()
    {
        isLiberated = true;
        navAgent.enabled = true;
        navAgent.speed = wanderSpeed;

        // Fallback: daca Start() inca nu a rulat, ia referinta direct
        if (blackboard == null)
            blackboard = TacticalBlackboard.Instance;

        // Nu setam o destinatie aici - Update() va apela Wander()
        // dupa ce ActivatePhase2() creeaza grupurile.
    }

    void StopMoving()
    {
        if (navAgent.isOnNavMesh && navAgent.hasPath)
            navAgent.ResetPath();
    }

    // Faza 2 normala: fuga HAOTICA de grupul de agenti asignat. Nu se blocheaza.
    void Wander()
    {
        if (!navAgent.isOnNavMesh) return;
        navAgent.isStopped = false;
        navAgent.speed = wanderSpeed;

        wanderTimer += Time.deltaTime;

        bool reached = !navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance + 0.2f;
        bool noPath = !navAgent.hasPath && !navAgent.pathPending;

        if (wanderTimer >= fleeRetargetInterval || reached || noPath)
        {
            wanderTimer = 0f;
            SetFleeWanderTarget();
        }
    }

    // Centrul grupului care il urmareste (sau al tuturor agentilor ca fallback).
    Vector3 GetThreatCenter()
    {
        EnemyGroup myGroup = blackboard.GetGroupAssignedToEnemy(transform);
        Vector3 sum = Vector3.zero;
        int count = 0;

        if (myGroup != null && myGroup.agents.Count > 0)
        {
            foreach (AgentBehaviorTree a in myGroup.agents)
            {
                if (a == null) continue;
                HealthSystem hs = a.GetComponent<HealthSystem>();
                if (hs == null || hs.isDead) continue;
                sum += a.transform.position;
                count++;
            }
        }
        if (count == 0)
        {
            // fallback: toti agentii vii
            foreach (AgentBehaviorTree a in blackboard.allAgents)
            {
                if (a == null) continue;
                HealthSystem hs = a.GetComponent<HealthSystem>();
                if (hs == null || hs.isDead) continue;
                sum += a.transform.position;
                count++;
            }
        }
        return count > 0 ? sum / count : transform.position;
    }

    // Fuga haotica: departe de grup, dar cu zigzag mare. Ramane pe harta.
    void SetFleeWanderTarget()
    {
        Vector3 center = GetThreatCenter();

        int dirCount = 16;
        float angleOffset = Random.Range(0f, 360f);

        Vector3 bestPoint = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < dirCount; i++)
        {
            float ang = (angleOffset + i * (360f / dirCount)) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang));
            float step = Random.Range(8f, 13f);

            Vector3 candidate = transform.position + dir * step;
            candidate.x = Mathf.Clamp(candidate.x, -mapHalfExtent + 2f, mapHalfExtent - 2f);
            candidate.z = Mathf.Clamp(candidate.z, -mapHalfExtent + 2f, mapHalfExtent - 2f);

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
                continue;

            float distFromAgents = Vector3.Distance(hit.position, center);
            float score = distFromAgents + Random.Range(0f, 4f);

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = hit.position;
                found = true;
            }
        }

        if (found)
        {
            navAgent.SetDestination(bestPoint);
            return;
        }

        SetWanderTarget();
    }

    void SetWanderTarget()
    {
        for (int i = 0; i < 20; i++)
        {
            Vector3 p = new Vector3(
                Random.Range(-mapHalfExtent, mapHalfExtent),
                0,
                Random.Range(-mapHalfExtent, mapHalfExtent));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(p, out hit, 5f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
        }
        NavMeshHit fb;
        if (NavMesh.SamplePosition(transform.position + Random.insideUnitSphere * 6f,
            out fb, 8f, NavMesh.AllAreas))
            navAgent.SetDestination(fb.position);
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
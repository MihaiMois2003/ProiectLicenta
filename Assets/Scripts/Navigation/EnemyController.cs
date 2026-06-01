using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    [Header("Settings")]
    public float normalSpeed = 2f;
    [Tooltip("Viteza cand fuge de Leader in Faza 1 (Engaging). " +
             "Tine-o usor sub viteza Leader-ului ca sa fie prins, dar nu prea mica.")]
    public float fleeSpeed = 3.2f;
    public float chaseSpeed = 4f;
    public float patrolRadius = 20f;

    [Header("Fuga haotica")]
    [Tooltip("Cat de des isi schimba directia de fuga (secunde). Mic = mai haotic.")]
    public float fleeRetargetInterval = 0.6f;
    [Tooltip("Imprastiere unghiulara fata de directia 'departe de leader' (grade).")]
    public float fleeAngleJitter = 75f;
    [Tooltip("Distanta pana la urmatorul punct de fuga.")]
    public float fleeStepDistance = 9f;

    private float fleeTimer = 0f;

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
        if (blackboard == null) return;

        // Verifica eliberarea inamicilor secundari (HP < 75%)
        if (blackboard.combatState == CombatState.Engaging ||
            blackboard.combatState == CombatState.Combat ||
            blackboard.phase2Active)
        {
            CheckLiberation();
        }

        // Comportament:
        // - Faza 2 cu reversal: URMARESC grupul asignat
        // - Faza 2 normala: sta pe loc (lasa CombatModule sa traga)
        // - Combat (Faza 1): sta pe loc (Leader-ul e in range, lupta)
        // - Engaging (Faza 1): FUGE de Leader (asa pare ca incearca sa scape)
        // - Idle: patruleaza

        if (blackboard.phase2Active && blackboard.rolesReversed)
        {
            ChaseAssignedGroup();
        }
        else if (blackboard.phase2Active)
        {
            // Faza 2 normala: stau pe loc
            StopMoving();
        }
        else if (blackboard.combatState == CombatState.Combat)
        {
            // Combat Faza 1: stau pe loc, Leader-ul e aproape, lupta
            StopMoving();
        }
        else if (blackboard.combatState == CombatState.Engaging)
        {
            // Engaging Faza 1: FUG de Leader pana cand ajunge in range
            Flee();
        }
        else
        {
            Patrol();
        }
    }

    void StopMoving()
    {
        if (navAgent.isOnNavMesh && navAgent.hasPath)
            navAgent.ResetPath();
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

        // Citeste maxHP de la inamicul principal pentru a-l aplica secundarilor
        HealthSystem mainHS = GetComponent<HealthSystem>();
        float mainMaxHP = mainHS != null ? mainHS.maxHP : 100f;

        if (secondaryEnemyPrefab1 != null)
        {
            GameObject e1 = Instantiate(secondaryEnemyPrefab1,
                spawnPosition1, Quaternion.identity);
            ApplyHPFromMain(e1, mainMaxHP);
            e1.GetComponent<SecondaryEnemyController>()?.Liberate();
        }

        if (secondaryEnemyPrefab2 != null)
        {
            GameObject e2 = Instantiate(secondaryEnemyPrefab2,
                spawnPosition2, Quaternion.identity);
            ApplyHPFromMain(e2, mainMaxHP);
            e2.GetComponent<SecondaryEnemyController>()?.Liberate();
        }

        blackboard?.ActivatePhase2();

        Debug.Log($"[Enemy] Inamici secundari spawned cu {mainMaxHP} HP!");
    }

    // Suprascrie maxHP si currentHP ale secundarului cu HP-ul inamicului principal
    void ApplyHPFromMain(GameObject secondary, float mainMaxHP)
    {
        HealthSystem hs = secondary.GetComponent<HealthSystem>();
        if (hs == null) return;

        hs.maxHP = mainMaxHP;
        hs.currentHP = mainMaxHP;
    }

    // Fuge de Leader (Faza 1, Engaging)
    void Flee()
    {
        navAgent.speed = fleeSpeed;
        navAgent.isStopped = false;

        fleeTimer += Time.deltaTime;

        bool reached = !navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance + 0.1f;
        bool noPath = !navAgent.hasPath && !navAgent.pathPending;

        // Reschimba directia periodic (haotic), cand ajunge, SAU daca a ramas fara drum.
        if (fleeTimer >= fleeRetargetInterval || reached || noPath)
        {
            fleeTimer = 0f;
            SetNewFleeTarget();
        }
    }

    void SetNewFleeTarget()
    {
        AgentBehaviorTree leader = blackboard?.GetLeader();

        // Directia de baza: departe de leader (daca exista), altfel directie random.
        Vector3 awayDir;
        if (leader != null)
        {
            awayDir = transform.position - leader.transform.position;
            awayDir.y = 0;
            if (awayDir.sqrMagnitude < 0.01f)
                awayDir = Random.insideUnitSphere;
        }
        else
        {
            awayDir = Random.insideUnitSphere;
        }
        awayDir.y = 0;
        awayDir.Normalize();

        // Aplica jitter unghiular mare ca sa para haotic (zig-zag).
        float jitter = Random.Range(-fleeAngleJitter, fleeAngleJitter);
        Vector3 dir = Quaternion.Euler(0, jitter, 0) * awayDir;

        // Incearca puncte la distante descrescatoare; primul valid pe NavMesh castiga.
        for (int i = 0; i < 8; i++)
        {
            float dist = fleeStepDistance * Random.Range(0.6f, 1.2f);
            Vector3 candidate = transform.position + dir * dist;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 4f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                return;
            }
            // Daca nu merge, roteste directia si reincearca (cauta o iesire).
            dir = Quaternion.Euler(0, Random.Range(-90f, 90f), 0) * dir;
        }

        // ANTI-BLOCAJ garantat: daca nimic nu a mers, sample direct langa pozitia curenta.
        NavMeshHit fallbackHit;
        if (NavMesh.SamplePosition(
                transform.position + Random.insideUnitSphere * 5f,
                out fallbackHit, 6f, NavMesh.AllAreas))
            navAgent.SetDestination(fallbackHit.position);
    }

    // In reversal: urmareste grupul de agenti asignat
    void ChaseAssignedGroup()
    {
        navAgent.speed = chaseSpeed;

        EnemyGroup myGroup = blackboard.GetGroupAssignedToEnemy(transform);
        if (myGroup == null || myGroup.agents.Count == 0)
        {
            StopMoving();
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
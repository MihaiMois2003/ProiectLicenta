using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Viteza de plimbare haotica (Idle + dupa ce e depistat).")]
    public float wanderSpeed = 3f;
    public float chaseSpeed = 5.5f;
    public float patrolRadius = 20f;

    [Header("Plimbare haotica")]
    [Tooltip("Cat de des isi schimba destinatia cand NU e depistat (secunde).")]
    public float wanderRetargetInterval = 1.5f;
    [Tooltip("Cat de des isi schimba directia cand fuge (mai mic = zigzag mai viu).")]
    public float fleeRetargetInterval = 0.8f;
    [Tooltip("Limitele hartii pe X/Z in care se plimba (jumatate de latime).")]
    public float mapHalfExtent = 20f;
    private float wanderTimer = 0f;

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
        navAgent.speed = wanderSpeed;
    }

    void Start()
    {
        blackboard = TacticalBlackboard.Instance;

        if (blackboard != null)
            blackboard.mainEnemy = transform;

        SetWanderTarget();
    }

    void Update()
    {
        if (!TacticalBlackboard.IsRunning()) return;
        if (blackboard == null) return;

        // Verifica eliberarea inamicilor secundari (HP < 75%)
        if (blackboard.combatState == CombatState.Engaging ||
            blackboard.combatState == CombatState.Rallying ||
            blackboard.combatState == CombatState.Combat ||
            blackboard.phase2Active)
        {
            CheckLiberation();
        }

        // Comportament:
        // - Faza 2 cu reversal: URMARESC grupul asignat
        // - Faza 2 normala: se plimba haotic (lasa CombatModule sa traga)
        // - Combat (Faza 1): sta pe loc (echipa l-a incercuit, lupta)
        // - Idle/Engaging/Rallying: se plimba HAOTIC prin toata harta

        if (blackboard.phase2Active && blackboard.rolesReversed)
        {
            ChaseAssignedGroup();
        }
        else if (blackboard.phase2Active)
        {
            // Faza 2 normala: fuga haotica (departe de grupuri) in timp ce trage
            Wander(true);
        }
        else if (blackboard.combatState == CombatState.Combat)
        {
            // Combat Faza 1: echipa adunata, inamicul se opreste si lupta
            StopMoving();
        }
        else if (blackboard.combatState == CombatState.Engaging ||
                 blackboard.combatState == CombatState.Rallying)
        {
            // Depistat: fuga HAOTICA (tinde departe de agenti, dar cu zigzag)
            Wander(true);
        }
        else
        {
            // Idle (nedepistat): plimbare haotica pura prin toata harta
            Wander(false);
        }
    }

    void StopMoving()
    {
        if (navAgent.isOnNavMesh && navAgent.hasPath)
            navAgent.ResetPath();
    }

    // Miscare haotica. alert=false => wander pur. alert=true => fuga haotica:
    // alege puncte care tind sa fie departe de centrul agentilor, dar cu zigzag.
    void Wander(bool alert)
    {
        if (!navAgent.isOnNavMesh) return;
        navAgent.isStopped = false;
        navAgent.speed = wanderSpeed;

        wanderTimer += Time.deltaTime;

        bool reached = !navAgent.pathPending &&
            navAgent.remainingDistance <= navAgent.stoppingDistance + 0.2f;
        bool noPath = !navAgent.hasPath && !navAgent.pathPending;

        float interval = alert ? fleeRetargetInterval : wanderRetargetInterval;
        if (wanderTimer >= interval || reached || noPath)
        {
            wanderTimer = 0f;
            if (alert) SetFleeWanderTarget();
            else SetWanderTarget();
        }
    }

    // Centrul agentilor vii (pentru a sti de cine sa fuga).
    Vector3 GetAgentsCenter()
    {
        if (blackboard == null) return transform.position;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (AgentBehaviorTree a in blackboard.allAgents)
        {
            if (a == null) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            sum += a.transform.position;
            count++;
        }
        return count > 0 ? sum / count : transform.position;
    }

    // Fuga haotica: directia de baza = departe de centrul agentilor,
    // dar cu jitter unghiular mare (zigzag) si pas variabil. Ramane pe harta.
    void SetFleeWanderTarget()
    {
        Vector3 center = GetAgentsCenter();

        // Scaneaza directii pe un cerc COMPLET. Pentru fiecare punct valid pe harta,
        // calculeaza un scor (cat de departe ajunge de agenti). Alege cel mai bun.
        // Astfel, daca e incoltit, accepta sa treaca pe langa agenti ca sa scape,
        // dar prefera mereu directia care-l duce cel mai departe de ei.
        int dirCount = 16;
        float angleOffset = Random.Range(0f, 360f); // rotim startul = haotic intre apeluri

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

            // Scor = distanta punctului fata de agenti (mai mare = mai bine)
            // + un bonus mic random ca sa nu fie mereu identic (haotic).
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

        // Daca NICIO directie nu a mers (foarte rar) -> wander pur garantat.
        SetWanderTarget();
    }

    void SetWanderTarget()
    {
        // Incearca pana gaseste un punct valid pe NavMesh, oriunde pe harta.
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
        // ANTI-BLOCAJ: daca nimic, sample langa pozitia curenta.
        NavMeshHit fb;
        if (NavMesh.SamplePosition(transform.position + Random.insideUnitSphere * 6f,
            out fb, 8f, NavMesh.AllAreas))
            navAgent.SetDestination(fb.position);
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

        blackboard?.ActivatePhase2();

        Debug.Log("[Enemy] Inamici secundari spawned (HP din propriul prefab).");
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

    public float GetHPPercentage()
    {
        HealthSystem hs = GetComponent<HealthSystem>();
        return hs != null ? hs.GetHPPercentage() : 1f;
    }
}
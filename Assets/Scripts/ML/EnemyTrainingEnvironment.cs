using UnityEngine;
using UnityEngine.AI;

// Gestioneaza scena de antrenament pentru EnemyMLAgent.
// Inamicul invata sa fuga de cativa "agenti dummy" simplificati care il urmaresc.
//
// Setup in scena de antrenament:
//  - 1 GameObject "Enemy" cu NavMeshAgent + HealthSystem + EnemyMLAgent
//  - 3-5 GameObject-uri "DummyAgent" cu NavMeshAgent + HealthSystem
//  - 1 GameObject gol "Environment" cu acest script
//  - Optional: obstacole pe layer Obstacle
public class EnemyTrainingEnvironment : MonoBehaviour
{
    [Header("Enemy")]
    public Transform enemy;
    public Vector3 enemySpawnAreaCenter = Vector3.zero;
    public float enemySpawnRadius = 5f;

    [Header("Dummy Agents (chasers)")]
    public Transform[] dummyAgents;
    public float dummySpawnRadius = 12f;
    public float dummySpeed = 3f;
    public float dummyDamagePerTick = 5f;
    public float dummyAttackRange = 2f;
    public float dummyAttackCooldown = 1f;

    [Header("Episode Settings")]
    public int maxStepsPerEpisode = 2000;
    private int currentStep = 0;

    private EnemyMLAgent enemyAgent;
    private HealthSystem enemyHS;
    private NavMeshAgent enemyNav;

    private float[] dummyAttackTimers;

    void Start()
    {
        if (enemy != null)
        {
            enemyAgent = enemy.GetComponent<EnemyMLAgent>();
            enemyHS = enemy.GetComponent<HealthSystem>();
            enemyNav = enemy.GetComponent<NavMeshAgent>();
        }

        if (dummyAgents != null)
            dummyAttackTimers = new float[dummyAgents.Length];

        ResetEnvironment();
    }

    void FixedUpdate()
    {
        currentStep++;

        // Misca inamicul folosind directia ceruta de retea
        if (enemyAgent != null && enemyAgent.decisionReady && enemyHS != null && !enemyHS.isDead)
        {
            Vector2 dir = enemyAgent.desiredMoveDirection;
            Vector3 moveDir = new Vector3(dir.x, 0, dir.y);

            if (enemyNav != null && enemyNav.isOnNavMesh)
            {
                Vector3 destination = enemy.position + moveDir * 5f;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(destination, out hit, 5f, NavMesh.AllAreas))
                    enemyNav.SetDestination(hit.position);
            }
            enemyAgent.ConsumeDecision();
        }

        // Dummy agents urmaresc inamicul si ataca daca sunt aproape
        if (dummyAgents != null && enemy != null)
        {
            for (int i = 0; i < dummyAgents.Length; i++)
            {
                Transform dummy = dummyAgents[i];
                if (dummy == null) continue;

                HealthSystem dummyHS = dummy.GetComponent<HealthSystem>();
                if (dummyHS != null && dummyHS.isDead) continue;

                NavMeshAgent dummyNav = dummy.GetComponent<NavMeshAgent>();
                if (dummyNav != null && dummyNav.isOnNavMesh)
                    dummyNav.SetDestination(enemy.position);

                // Atac
                float dist = Vector3.Distance(dummy.position, enemy.position);
                dummyAttackTimers[i] += Time.fixedDeltaTime;
                if (dist <= dummyAttackRange && dummyAttackTimers[i] >= dummyAttackCooldown)
                {
                    if (enemyHS != null && !enemyHS.isDead)
                    {
                        enemyHS.TakeDamage(dummyDamagePerTick);
                        dummyAttackTimers[i] = 0f;
                    }
                }
            }
        }

        // Verifica end conditions
        bool enemyDead = enemyHS != null && enemyHS.isDead;
        bool timeout = currentStep >= maxStepsPerEpisode;

        if (enemyDead || timeout)
        {
            if (enemyAgent != null)
            {
                if (enemyDead) enemyAgent.OnSelfDied();
                else enemyAgent.EndEpisode();
            }
            ResetEnvironment();
        }
    }

    public void ResetEnvironment()
    {
        currentStep = 0;

        // Reset inamic
        if (enemy != null)
        {
            Vector2 randCircle = Random.insideUnitCircle * enemySpawnRadius;
            Vector3 newPos = enemySpawnAreaCenter + new Vector3(randCircle.x, 1, randCircle.y);

            // Pune-l pe NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(newPos, out hit, 5f, NavMesh.AllAreas))
                newPos = hit.position;

            if (enemyNav != null && enemyNav.isOnNavMesh)
                enemyNav.Warp(newPos);
            else
                enemy.position = newPos;

            enemy.rotation = Quaternion.identity;

            if (enemyHS != null)
            {
                enemyHS.currentHP = enemyHS.maxHP;
                enemyHS.isDead = false;
                if (enemyNav != null) enemyNav.enabled = true;
            }
        }

        // Reset dummy agents la pozitii random pe perimetru
        if (dummyAgents != null)
        {
            for (int i = 0; i < dummyAgents.Length; i++)
            {
                Transform d = dummyAgents[i];
                if (d == null) continue;

                float angle = (360f / dummyAgents.Length) * i +
                    Random.Range(-30f, 30f);
                float rad = angle * Mathf.Deg2Rad;
                Vector3 spawnPos = enemySpawnAreaCenter +
                    new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * dummySpawnRadius;
                spawnPos.y = 1;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(spawnPos, out hit, 5f, NavMesh.AllAreas))
                    spawnPos = hit.position;

                NavMeshAgent dNav = d.GetComponent<NavMeshAgent>();
                if (dNav != null)
                {
                    dNav.speed = dummySpeed;
                    if (dNav.isOnNavMesh) dNav.Warp(spawnPos);
                    else d.position = spawnPos;
                }
                else
                {
                    d.position = spawnPos;
                }

                d.rotation = Quaternion.identity;

                HealthSystem hs = d.GetComponent<HealthSystem>();
                if (hs != null)
                {
                    hs.currentHP = hs.maxHP;
                    hs.isDead = false;
                }

                dummyAttackTimers[i] = 0f;
            }
        }
    }
}
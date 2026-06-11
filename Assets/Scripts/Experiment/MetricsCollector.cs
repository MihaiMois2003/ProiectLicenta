using UnityEngine;
using System.Collections.Generic;

// Colecteaza metrici pentru experimente. Toate sunt afisate live de ExperimentUI.
// Cronometrul porneste la primul contact (combatState devine Engaging) si se
// opreste cand toti inamicii sunt morti.
public class MetricsCollector : MonoBehaviour
{
    public static MetricsCollector Instance;

    [Header("Timing (readonly)")]
    public bool timerRunning = false;
    public float elapsedTime = 0f;          // timp de la primul contact
    public float timeFirstContact = -1f;    // momentul (Time.time) primului Engaging
    public float timeSceneStart = -1f;      // momentul (Time.time) pornirii masuratorii
    public float detectionTime = -1f;       // timeFirstContact - timeSceneStart (cat dureaza sa fie gasit)
    public float timeFirstCombat = -1f;     // momentul primului Combat (primul agent in lupta)
    public float reactionTime = -1f;        // timeFirstCombat - timeFirstContact
    public float timeAllEnemiesDead = -1f;  // elapsedTime cand a murit ultimul inamic

    [Header("Knowledge spread (readonly)")]
    [Tooltip("Cati agenti 'stiu' de inamic acum (relevant la LocalBroadcast).")]
    public int agentsAware = 0;
    public int totalAgents = 0;
    [Tooltip("Timpul (de la primul contact) cand TOTI agentii au aflat. -1 daca nu s-a intamplat.")]
    public float timeFullAwareness = -1f;

    [Header("Outcome (readonly)")]
    public int agentsAlive = 0;
    public int enemiesAlive = 0;
    public float totalAgentHP = 0f;
    public float totalEnemyHP = 0f;
    public bool finished = false;

    [Header("Eficienta (readonly)")]
    [Tooltip("Distanta totala parcursa de toti agentii (planificare proasta = drum irosit).")]
    public float totalDistanceTraveled = 0f;
    [Tooltip("Damage 'irosit' = damage dat peste HP-ul tintei (overkill cumulat).")]
    public float overkillDamage = 0f;

    private TacticalBlackboard bb;
    private bool firstContactSeen = false;
    private bool firstCombatSeen = false;
    private Dictionary<Transform, Vector3> lastPositions = new Dictionary<Transform, Vector3>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }
    }

    void Start()
    {
        bb = TacticalBlackboard.Instance;
        timeSceneStart = -1f;
    }

    public void ResetRun()
    {
        timerRunning = false;
        elapsedTime = 0f;
        timeFirstContact = -1f;
        timeSceneStart = -1f;
        detectionTime = -1f;
        timeFirstCombat = -1f;
        reactionTime = -1f;
        timeAllEnemiesDead = -1f;
        agentsAware = 0;
        timeFullAwareness = -1f;
        finished = false;
        firstContactSeen = false;
        firstCombatSeen = false;
        totalDistanceTraveled = 0f;
        overkillDamage = 0f;
        lastPositions.Clear();
    }

    void Update()
    {
        if (bb == null) { bb = TacticalBlackboard.Instance; if (bb == null) return; }
        if (!bb.simulationStarted) return;

        // Marcheaza momentul real de start (prima data cand simularea ruleaza).
        if (timeSceneStart < 0f) timeSceneStart = Time.time;

        // Start cronometru la primul contact
        if (!firstContactSeen &&
            (bb.combatState == CombatState.Engaging ||
             bb.combatState == CombatState.Rallying ||
             bb.combatState == CombatState.Combat ||
             bb.phase2Active))
        {
            firstContactSeen = true;
            timerRunning = true;
            timeFirstContact = Time.time;
            detectionTime = timeFirstContact - timeSceneStart;
        }

        if (timerRunning && !finished)
            elapsedTime += Time.deltaTime;

        // Primul moment de Combat = reactie completa
        if (firstContactSeen && !firstCombatSeen &&
            (bb.combatState == CombatState.Combat || bb.phase2Active))
        {
            firstCombatSeen = true;
            timeFirstCombat = Time.time;
            reactionTime = timeFirstCombat - timeFirstContact;
        }

        RecomputeCounts();

        // Knowledge spread
        if (firstContactSeen && timeFullAwareness < 0f &&
            agentsAware >= totalAgents && totalAgents > 0)
            timeFullAwareness = elapsedTime;

        // Conditie de final: cronometrul a pornit si nu mai e niciun inamic viu
        if (firstContactSeen && !finished && enemiesAlive == 0)
        {
            finished = true;
            timerRunning = false;
            timeAllEnemiesDead = elapsedTime;
        }
    }

    void RecomputeCounts()
    {
        agentsAlive = 0;
        agentsAware = 0;
        totalAgents = 0;
        totalAgentHP = 0f;

        foreach (AgentBehaviorTree a in bb.allAgents)
        {
            if (a == null) continue;
            totalAgents++;
            if (a.KnowsEnemy()) agentsAware++;

            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs != null && !hs.isDead)
            {
                agentsAlive++;
                totalAgentHP += hs.currentHP;

                // Acumuleaza distanta parcursa (doar cat ruleaza cronometrul).
                if (timerRunning)
                {
                    Vector3 cur = a.transform.position;
                    if (lastPositions.TryGetValue(a.transform, out Vector3 prev))
                        totalDistanceTraveled += Vector3.Distance(prev, cur);
                    lastPositions[a.transform] = cur;
                }
            }
        }

        // Inamici
        enemiesAlive = 0;
        totalEnemyHP = 0f;
        foreach (Transform e in CollectEnemies())
        {
            HealthSystem hs = e.GetComponent<HealthSystem>();
            if (hs != null && !hs.isDead)
            {
                enemiesAlive++;
                totalEnemyHP += hs.currentHP;
            }
        }
    }

    List<Transform> CollectEnemies()
    {
        List<Transform> result = new List<Transform>();
        if (bb.mainEnemy != null) result.Add(bb.mainEnemy);

        SecondaryEnemyController[] secs =
            Object.FindObjectsByType<SecondaryEnemyController>(FindObjectsSortMode.None);
        foreach (SecondaryEnemyController s in secs)
            result.Add(s.transform);

        return result;
    }
}
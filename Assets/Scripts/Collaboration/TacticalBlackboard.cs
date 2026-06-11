using UnityEngine;
using System.Collections.Generic;

public enum CombatState
{
    Idle,
    Engaging,
    Rallying,
    Combat
}

[System.Serializable]
public class EnemyGroup
{
    public int groupID;
    public List<AgentBehaviorTree> agents = new List<AgentBehaviorTree>();

    // Inamicul fix asignat acestui grup la activarea Fazei 2.
    public Transform assignedEnemy;
}

public class TacticalBlackboard : MonoBehaviour
{
    public static TacticalBlackboard Instance;

    [Header("Combat State")]
    public CombatState combatState = CombatState.Idle;

    [Header("Control simulare")]
    [Tooltip("Cat e false, simularea e inghetata (asteapta butonul START din GUI).")]
    public bool simulationStarted = false;

    // Helper static rapid pentru toate scripturile.
    public static bool IsRunning()
    {
        return Instance != null && Instance.simulationStarted;
    }

    [Header("Rally (adunare in formatie inainte de atac)")]
    [Tooltip("Distanta la care Leaderul se opreste si asteapta echipa.")]
    public float rallyDistance = 11f;
    [Tooltip("Cat de aproape de pozitia lui de formatie trebuie sa fie un agent ca sa conteze 'in formatie'.")]
    public float formationTolerance = 2.5f;
    [Range(0f, 1f)]
    [Tooltip("Ce fractie din echipa non-sniper trebuie sa fie in formatie ca sa porneasca atacul.")]
    public float rallyReadyFraction = 0.6f;
    [Tooltip("Timp maxim de asteptare in Rally inainte de a forta atacul (anti-blocaj).")]
    public float rallyTimeout = 8f;
    [HideInInspector] public float rallyStartTime = -1f;

    [Header("Main Enemy")]
    public Transform mainEnemy;

    [Header("Enemy Info")]
    public Vector3 lastKnownEnemyPosition;
    public bool enemySpotted = false;
    public float timeEnemyWasSpotted = -1f;

    [Header("Agenti")]
    public List<AgentBehaviorTree> allAgents = new List<AgentBehaviorTree>();

    [Header("Sniper Positions")]
    public Vector3 sniperPosition1 = new Vector3(-20, 1, -20);
    public Vector3 sniperPosition2 = new Vector3(-20, 1, 20);

    [Header("Phase 2")]
    public bool phase2Active = false;
    public List<EnemyGroup> enemyGroups = new List<EnemyGroup>();

    [Header("Reversal")]
    [Tooltip("Cand suma HP a inamicilor > suma HP a agentilor non-sniper, rolurile se inverseaza.")]
    public bool rolesReversed = false;
    public float reversalCheckInterval = 1f;
    private float reversalTimer = 0f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private float relayTimer = 0f;
    [Header("Relay")]
    [Tooltip("La fiecare cat timp (sec) se propaga un val de informatie in modul Relay.")]
    public float relayPropagationInterval = 0.4f;

    void Update()
    {
        if (!simulationStarted) return;

        if (combatState == CombatState.Combat)
        {
            AgentBehaviorTree leader = GetLeader();
            if (leader == null || leader.GetComponent<HealthSystem>().isDead)
                PromoteNewLeader();
        }

        if (enemySpotted && Time.time - timeEnemyWasSpotted > 10f)
            ClearEnemyInfo();

        // Propagare Relay: cine stie retransmite vecinilor la intervale (valuri).
        ExperimentConfig cfg = ExperimentConfig.Instance;
        if (cfg != null && cfg.communicationMode == CommunicationMode.Relay && enemySpotted)
        {
            relayTimer += Time.deltaTime;
            if (relayTimer >= relayPropagationInterval)
            {
                relayTimer = 0f;
                PropagateRelay(cfg.commRange);
            }
        }

        // In Faza 2, daca tinta unui grup a murit, reasigneaza alta tinta vie
        if (phase2Active)
        {
            ReassignDeadEnemyTargets();

            // Verifica reversalul la fiecare reversalCheckInterval secunde
            reversalTimer += Time.deltaTime;
            if (reversalTimer >= reversalCheckInterval)
            {
                reversalTimer = 0f;
                CheckRolesReversal();
            }
        }
    }

    // Un val de propagare: fiecare agent care STIE anunta vecinii din commRange
    // care INCA nu stiu. Apelat periodic => informatia se raspandeste din aproape
    // in aproape, nu instant.
    void PropagateRelay(float range)
    {
        // Colecteaza cine stie acum (snapshot, ca sa nu propagam in cascada intr-un singur tick).
        List<AgentBehaviorTree> knowers = new List<AgentBehaviorTree>();
        foreach (AgentBehaviorTree a in allAgents)
            if (a != null && a.knowsEnemy) knowers.Add(a);

        foreach (AgentBehaviorTree src in knowers)
        {
            Vector3 origin = src.transform.position;
            foreach (AgentBehaviorTree dst in allAgents)
            {
                if (dst == null || dst.knowsEnemy) continue;
                if (Vector3.Distance(origin, dst.transform.position) <= range)
                    dst.ReceiveEnemyReport(src.knownEnemyPosition);
            }
        }
    }

    void PromoteNewLeader()
    {
        foreach (AgentBehaviorTree agent in allAgents)
        {
            HealthSystem hs = agent.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            if (agent.role == AgentRole.Leader) continue;
            if (agent.role == AgentRole.Sniper) continue;

            agent.role = AgentRole.Leader;
            agent.RebuildTree();
            return;
        }
    }

    public void RegisterAgent(AgentBehaviorTree agent)
    {
        if (!allAgents.Contains(agent))
            allAgents.Add(agent);
    }

    public void ReportEnemy(Vector3 position, string agentID)
    {
        lastKnownEnemyPosition = position;
        enemySpotted = true;
        timeEnemyWasSpotted = Time.time;
        if (combatState == CombatState.Idle)
            combatState = CombatState.Engaging;

        // Propaga raportul in functie de modul de comunicare.
        ExperimentConfig cfg = ExperimentConfig.Instance;
        CommunicationMode mode = cfg != null ? cfg.communicationMode
                                             : CommunicationMode.Blackboard;

        if (mode == CommunicationMode.Blackboard)
        {
            // Cunoastere globala: toti agentii afla instant.
            foreach (AgentBehaviorTree a in allAgents)
                if (a != null) a.ReceiveEnemyReport(position);
        }
        else
        {
            // LocalBroadcast SI Relay: raportorul anunta vecinii din commRange.
            // Diferenta: la Relay, vecinii care au aflat vor propaga MAI DEPARTE
            //            in PropagateRelay() (apelat din Update), in valuri.
            AgentBehaviorTree reporter = GetAgentByID(agentID);
            if (reporter != null)
            {
                Vector3 origin = reporter.transform.position;
                float r = cfg != null ? cfg.commRange : 12f;
                foreach (AgentBehaviorTree a in allAgents)
                {
                    if (a == null) continue;
                    if (Vector3.Distance(origin, a.transform.position) <= r)
                        a.ReceiveEnemyReport(position);
                }
            }
        }
    }

    public void ClearEnemyInfo()
    {
        enemySpotted = false;
        foreach (AgentBehaviorTree a in allAgents)
            if (a != null) a.ClearEnemyKnowledge();
        // Nu resetam combatState daca suntem deja in Combat sau Faza 2
        if ((combatState == CombatState.Engaging || combatState == CombatState.Rallying)
            && !phase2Active)
            combatState = CombatState.Idle;
    }

    public AgentBehaviorTree GetLeader()
    {
        foreach (var agent in allAgents)
            if (agent.role == AgentRole.Leader) return agent;
        return null;
    }

    // Fractia de agenti non-sniper, non-leader, vii, care sunt aproape de pozitia lor de formatie.
    public float FormationReadyFraction()
    {
        int total = 0, ready = 0;
        foreach (AgentBehaviorTree a in allAgents)
        {
            if (a == null) continue;
            if (a.role == AgentRole.Sniper || a.role == AgentRole.Leader) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;

            total++;
            if (a.IsNearFormationSlot(formationTolerance)) ready++;
        }
        if (total == 0) return 1f; // nimeni de asteptat
        return (float)ready / total;
    }

    // Trebuie sa pornim atacul? (destui in formatie SAU timeout)
    public bool RallyComplete()
    {
        if (FormationReadyFraction() >= rallyReadyFraction) return true;
        if (rallyStartTime >= 0 && Time.time - rallyStartTime >= rallyTimeout) return true;
        return false;
    }

    public AgentBehaviorTree GetAgentByID(string id)
    {
        foreach (var agent in allAgents)
            if (agent.agentID == id) return agent;
        return null;
    }

    public List<string> helpRequests = new List<string>();

    public void RequestHelp(string agentID)
    {
        if (!helpRequests.Contains(agentID))
            helpRequests.Add(agentID);
    }

    public void ResolveHelp(string agentID)
    {
        helpRequests.Remove(agentID);
    }

    public void ActivatePhase2()
    {
        if (phase2Active) return;
        phase2Active = true;

        // Cand intram in Faza 2, fortam combatState = Combat ca sa traga toata lumea
        combatState = CombatState.Combat;

        // 1. Colecteaza agentii non-sniper vii
        List<AgentBehaviorTree> availableAgents = new List<AgentBehaviorTree>();
        foreach (AgentBehaviorTree agent in allAgents)
        {
            if (agent.role == AgentRole.Sniper) continue;
            HealthSystem hs = agent.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            availableAgents.Add(agent);
        }

        // 2. Amesteca aleatoriu (Fisher-Yates)
        for (int i = 0; i < availableAgents.Count; i++)
        {
            int rand = Random.Range(i, availableAgents.Count);
            AgentBehaviorTree temp = availableAgents[i];
            availableAgents[i] = availableAgents[rand];
            availableAgents[rand] = temp;
        }

        // 3. Colecteaza inamicii vii
        List<Transform> liveEnemies = CollectLiveEnemies();

        // 4. Imparte agentii in grupuri de cate 3
        enemyGroups.Clear();
        int groupIndex = 0;
        for (int i = 0; i < availableAgents.Count; i += 3)
        {
            EnemyGroup group = new EnemyGroup();
            group.groupID = groupIndex;

            int groupSize = Mathf.Min(3, availableAgents.Count - i);
            int backRowCount = groupSize - 1;

            for (int j = i; j < i + groupSize; j++)
            {
                int indexInGroup = j - i;
                availableAgents[j].groupID = group.groupID;

                if (indexInGroup == 0)
                {
                    availableAgents[j].formationRow = 0;
                    availableAgents[j].formationIndexInRow = 0;
                    availableAgents[j].formationTotalInRow = 1;
                }
                else
                {
                    availableAgents[j].formationRow = 1;
                    availableAgents[j].formationIndexInRow = indexInGroup - 1;
                    availableAgents[j].formationTotalInRow = backRowCount;
                }

                availableAgents[j].RebuildTree();
                group.agents.Add(availableAgents[j]);
            }

            // 5. Asigneaza un inamic in functie de modul de colaborare
            AssignTargetToGroup(group, liveEnemies);

            enemyGroups.Add(group);
            groupIndex++;
        }

        Debug.Log($"[Blackboard] Faza 2 activa! {enemyGroups.Count} grupuri, " +
                  $"{liveEnemies.Count} inamici disponibili.");

        foreach (EnemyGroup g in enemyGroups)
        {
            string enemyName = g.assignedEnemy != null ? g.assignedEnemy.name : "NIMIC";
            Debug.Log($"[Blackboard] Grup {g.groupID} ({g.agents.Count} agenti) -> {enemyName}");
        }
    }

    List<Transform> CollectLiveEnemies()
    {
        List<Transform> result = new List<Transform>();

        if (mainEnemy != null)
        {
            HealthSystem hs = mainEnemy.GetComponent<HealthSystem>();
            if (hs != null && !hs.isDead)
                result.Add(mainEnemy);
        }

        SecondaryEnemyController[] secondaries =
            Object.FindObjectsByType<SecondaryEnemyController>(FindObjectsSortMode.None);

        foreach (SecondaryEnemyController sec in secondaries)
        {
            HealthSystem hs = sec.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            result.Add(sec.transform);
        }

        return result;
    }

    // Asigneaza un inamic unui grup, in functie de CollaborationMode.
    // RandomRoundRobin = ordine ciclica (comportamentul original).
    // NearestEnemy     = inamicul cel mai apropiat de centrul grupului.
    // FocusFire        = acelasi inamic pentru toate grupurile (primul viu).
    void AssignTargetToGroup(EnemyGroup group, List<Transform> liveEnemies)
    {
        if (liveEnemies == null || liveEnemies.Count == 0)
        {
            group.assignedEnemy = null;
            return;
        }

        ExperimentConfig cfg = ExperimentConfig.Instance;
        CollaborationMode mode = cfg != null ? cfg.collaborationMode
                                             : CollaborationMode.RandomRoundRobin;

        switch (mode)
        {
            case CollaborationMode.FocusFire:
                // Toate grupurile concentreaza focul pe primul inamic viu.
                group.assignedEnemy = liveEnemies[0];
                break;

            case CollaborationMode.NearestEnemy:
                {
                    Vector3 center = GetGroupCenter(group);
                    Transform best = null;
                    float minDist = Mathf.Infinity;
                    foreach (Transform e in liveEnemies)
                    {
                        float d = Vector3.Distance(center, e.position);
                        if (d < minDist) { minDist = d; best = e; }
                    }
                    group.assignedEnemy = best;
                    break;
                }

            case CollaborationMode.Auction:
                // Auction necesita vedere globala -> asigneaza toate grupurile odata.
                AssignByAuction(liveEnemies);
                break;

            case CollaborationMode.RandomRoundRobin:
            default:
                // groupID e setat inainte de apel; ciclam peste inamicii vii.
                group.assignedEnemy = liveEnemies[group.groupID % liveEnemies.Count];
                break;
        }
    }

    // ── AUCTION (licitatie) ─────────────────────────
    // Fiecare grup liciteaza pe fiecare inamic (bid = 1/distanta). Asignam iterativ
    // perechea cu cel mai mare bid, apoi penalizam inamicul deja luat ca sa
    // distribuim grupurile (evitam sa se inghesuie toate pe acelasi inamic).
    void AssignByAuction(List<Transform> liveEnemies)
    {
        if (enemyGroups.Count == 0 || liveEnemies.Count == 0) return;

        // Cate grupuri are voie un inamic, ca sa fie echilibrat.
        int cap = Mathf.CeilToInt((float)enemyGroups.Count / liveEnemies.Count);
        Dictionary<Transform, int> load = new Dictionary<Transform, int>();
        foreach (Transform e in liveEnemies) load[e] = 0;

        // Construieste lista de licitatii (grup, inamic, bid).
        foreach (EnemyGroup g in enemyGroups)
        {
            Vector3 center = GetGroupCenter(g);
            Transform best = null;
            float bestBid = -1f;

            foreach (Transform e in liveEnemies)
            {
                float dist = Vector3.Distance(center, e.position);
                float bid = 1f / Mathf.Max(0.1f, dist);

                // Penalizeaza inamicii deja incarcati la capacitate.
                if (load[e] >= cap) bid *= 0.25f;

                if (bid > bestBid) { bestBid = bid; best = e; }
            }

            g.assignedEnemy = best;
            if (best != null) load[best]++;
        }
    }


    void ReassignDeadEnemyTargets()
    {
        List<Transform> liveEnemies = null;

        foreach (EnemyGroup group in enemyGroups)
        {
            bool needsNewTarget = false;

            if (group.assignedEnemy == null)
                needsNewTarget = true;
            else
            {
                HealthSystem hs = group.assignedEnemy.GetComponent<HealthSystem>();
                if (hs == null || hs.isDead)
                    needsNewTarget = true;
            }

            if (!needsNewTarget) continue;

            if (liveEnemies == null)
                liveEnemies = CollectLiveEnemies();

            if (liveEnemies.Count == 0)
            {
                group.assignedEnemy = null;
                continue;
            }

            Transform previous = group.assignedEnemy;
            AssignTargetToGroup(group, liveEnemies);

            if (group.assignedEnemy != null && group.assignedEnemy != previous)
                Debug.Log($"[Blackboard] Grup {group.groupID} reasignat -> {group.assignedEnemy.name}");
        }
    }

    // ── REVERSAL LOGIC ─────────────────────────────

    void CheckRolesReversal()
    {
        float totalAgentHP = 0f;
        foreach (AgentBehaviorTree a in allAgents)
        {
            if (a == null) continue;
            // Sniperii NU intra in calcul (sunt mereu protejati la pozitia lor)
            if (a.role == AgentRole.Sniper) continue;

            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            totalAgentHP += hs.currentHP;
        }

        float totalEnemyHP = 0f;
        List<Transform> liveEnemies = CollectLiveEnemies();
        foreach (Transform e in liveEnemies)
        {
            HealthSystem hs = e.GetComponent<HealthSystem>();
            if (hs == null) continue;
            totalEnemyHP += hs.currentHP;
        }

        bool shouldBeReversed = totalEnemyHP > totalAgentHP;

        if (shouldBeReversed != rolesReversed)
        {
            rolesReversed = shouldBeReversed;
            if (rolesReversed)
                Debug.Log($"[Blackboard] !!! ROLURI INVERSATE !!! " +
                          $"HP Inamici: {totalEnemyHP:F0} > HP Agenti: {totalAgentHP:F0}. " +
                          $"Acum agentii fug, inamicii urmaresc.");
            else
                Debug.Log($"[Blackboard] Roluri revenite la normal. " +
                          $"HP Agenti: {totalAgentHP:F0} >= HP Inamici: {totalEnemyHP:F0}.");
        }
    }

    // Helper: ce grup de agenti are asignat un anumit inamic
    public EnemyGroup GetGroupAssignedToEnemy(Transform enemy)
    {
        foreach (EnemyGroup g in enemyGroups)
            if (g.assignedEnemy == enemy) return g;
        return null;
    }

    Vector3 GetGroupCenter(EnemyGroup group)
    {
        if (group.agents.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (AgentBehaviorTree a in group.agents)
        {
            if (a == null) continue;
            sum += a.transform.position;
            count++;
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    public Transform GetAssignedEnemyForGroup(int groupID)
    {
        foreach (EnemyGroup g in enemyGroups)
            if (g.groupID == groupID) return g.assignedEnemy;
        return null;
    }

    public Transform GetNearestEnemyFor(Vector3 position)
    {
        int enemyLayer = LayerMask.GetMask("Enemy", "SecondaryEnemy");
        Collider[] colliders = Physics.OverlapSphere(position, 100f, enemyLayer);

        Transform nearest = null;
        float minDist = Mathf.Infinity;

        foreach (Collider col in colliders)
        {
            HealthSystem hs = col.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;

            float dist = Vector3.Distance(position, col.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = col.transform;
            }
        }
        return nearest;
    }
}
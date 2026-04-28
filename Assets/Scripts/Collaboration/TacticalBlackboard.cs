using UnityEngine;
using System.Collections.Generic;

public enum CombatState
{
    Idle,
    Engaging,
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

    void Update()
    {
        if (combatState == CombatState.Combat)
        {
            AgentBehaviorTree leader = GetLeader();
            if (leader == null || leader.GetComponent<HealthSystem>().isDead)
                PromoteNewLeader();
        }

        if (enemySpotted && Time.time - timeEnemyWasSpotted > 10f)
            ClearEnemyInfo();

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
    }

    public void ClearEnemyInfo()
    {
        enemySpotted = false;
        // Nu resetam combatState daca suntem deja in Combat sau Faza 2
        if (combatState == CombatState.Engaging && !phase2Active)
            combatState = CombatState.Idle;
    }

    public AgentBehaviorTree GetLeader()
    {
        foreach (var agent in allAgents)
            if (agent.role == AgentRole.Leader) return agent;
        return null;
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

            // 5. Asigneaza un inamic
            if (liveEnemies.Count > 0)
                group.assignedEnemy = liveEnemies[groupIndex % liveEnemies.Count];

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

            Vector3 center = GetGroupCenter(group);
            Transform best = null;
            float minDist = Mathf.Infinity;
            foreach (Transform e in liveEnemies)
            {
                float d = Vector3.Distance(center, e.position);
                if (d < minDist)
                {
                    minDist = d;
                    best = e;
                }
            }

            if (best != null && best != group.assignedEnemy)
            {
                group.assignedEnemy = best;
                Debug.Log($"[Blackboard] Grup {group.groupID} reasignat -> {best.name}");
            }
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
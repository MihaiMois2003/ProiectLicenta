using UnityEngine;
using System.Collections.Generic;

public enum CombatState
{
    Idle,        // nimeni nu a vazut inamicul
    Engaging,  // formatia se pune in pozitie, nimeni nu trage
    Combat       // inamic detectat, formatie activa
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

    

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        // Daca liderul a murit, promoveaza un Scout ca lider nou
        if (combatState == CombatState.Combat)
        {
            AgentBehaviorTree leader = GetLeader();
            if (leader == null || leader.GetComponent<HealthSystem>().isDead)
                PromoteNewLeader();
        }

        if (enemySpotted && Time.time - timeEnemyWasSpotted > 10f)
            ClearEnemyInfo();
    }

    void PromoteNewLeader()
    {
        foreach (AgentBehaviorTree agent in allAgents)
        {
            HealthSystem hs = agent.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            if (agent.role == AgentRole.Leader) continue;

            // Primul agent viu devine Leader
            agent.role = AgentRole.Leader;
            agent.RebuildTree();
            Debug.Log($"[Blackboard] {agent.agentID} promovat ca Leader nou!");
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
        combatState = CombatState.Engaging;
    }

    public void ClearEnemyInfo()
    {
        enemySpotted = false;
        combatState = CombatState.Idle;
        Debug.Log("[Blackboard] Inamic pierdut, revenire la Idle");
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
}
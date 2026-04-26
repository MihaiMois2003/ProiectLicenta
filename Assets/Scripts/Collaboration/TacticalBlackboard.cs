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
    }

    void PromoteNewLeader()
    {
        foreach (AgentBehaviorTree agent in allAgents)
        {
            HealthSystem hs = agent.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            if (agent.role == AgentRole.Leader) continue;
            if (agent.role == AgentRole.Sniper) continue; // sniperii nu devin lideri

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
        combatState = CombatState.Engaging;
    }

    public void ClearEnemyInfo()
    {
        enemySpotted = false;
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

        // Colecteaza agentii non-sniper vii
        List<AgentBehaviorTree> availableAgents = new List<AgentBehaviorTree>();
        foreach (AgentBehaviorTree agent in allAgents)
        {
            if (agent.role == AgentRole.Sniper) continue;
            HealthSystem hs = agent.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            availableAgents.Add(agent);
        }

        // Amesteca aleatoriu
        for (int i = 0; i < availableAgents.Count; i++)
        {
            int rand = Random.Range(i, availableAgents.Count);
            AgentBehaviorTree temp = availableAgents[i];
            availableAgents[i] = availableAgents[rand];
            availableAgents[rand] = temp;
        }

        // Imparte in grupuri de 3
        enemyGroups.Clear();
        for (int i = 0; i < availableAgents.Count; i += 3)
        {
            EnemyGroup group = new EnemyGroup();
            group.groupID = i / 3;

            for (int j = i; j < Mathf.Min(i + 3, availableAgents.Count); j++)
            {
                int indexInGroup = j - i;
                availableAgents[j].groupID = group.groupID;

                // Primul din grup e varful triunghiului
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
                    availableAgents[j].formationTotalInRow = 2;
                }

                availableAgents[j].RebuildTree();
                group.agents.Add(availableAgents[j]);
            }

            enemyGroups.Add(group);
        }

        Debug.Log($"[Blackboard] Faza 2 activa! {enemyGroups.Count} grupuri formate.");
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
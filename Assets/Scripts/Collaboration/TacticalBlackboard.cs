using UnityEngine;
using System.Collections.Generic;

public class TacticalBlackboard : MonoBehaviour
{
    // Singleton - orice agent il poate accesa
    public static TacticalBlackboard Instance;

    [Header("Informatii tactice partajate")]
    public Vector3 lastKnownEnemyPosition;
    public bool enemySpotted = false;
    public string spottedByAgentID = "";
    public float timeEnemyWasSpotted = -1f;

    [Header("Cereri de ajutor")]
    public List<string> helpRequests = new List<string>();

    [Header("Agenti inregistrati")]
    public List<AgentBehaviorTree> allAgents = new List<AgentBehaviorTree>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    void Update()
    {
        // Daca a trecut mai mult de 10 secunde de cand s-a vazut inamicul, resetam
        if (enemySpotted && Time.time - timeEnemyWasSpotted > 10f)
        {
            ClearEnemyInfo();
        }
    }

    // Agent raporteaza inamic
    public void ReportEnemy(Vector3 position, string agentID)
    {
        lastKnownEnemyPosition = position;
        enemySpotted = true;
        spottedByAgentID = agentID;
        timeEnemyWasSpotted = Time.time;

        Debug.Log($"[Blackboard] Inamic raportat de {agentID} la {position}");
    }

    // Agent cere ajutor
    public void RequestHelp(string agentID)
    {
        if (!helpRequests.Contains(agentID))
        {
            helpRequests.Add(agentID);
            Debug.Log($"[Blackboard] {agentID} cere ajutor!");
        }
    }

    // Agent rezolva cererea de ajutor
    public void ResolveHelp(string agentID)
    {
        helpRequests.Remove(agentID);
    }

    // Reseteaza informatiile despre inamic dupa un timp
    public void ClearEnemyInfo()
    {
        enemySpotted = false;
        spottedByAgentID = "";
        Debug.Log("[Blackboard] Informatii despre inamic resetate");
    }

    // Inregistreaza un agent nou
    public void RegisterAgent(AgentBehaviorTree agent)
    {
        if (!allAgents.Contains(agent))
            allAgents.Add(agent);
    }

    // Returneaza cel mai apropiat agent de o pozitie
    public AgentBehaviorTree GetNearestAgentTo(Vector3 position, string excludeID)
    {
        AgentBehaviorTree nearest = null;
        float minDist = Mathf.Infinity;

        foreach (AgentBehaviorTree agent in allAgents)
        {
            if (agent.GetAgentID() == excludeID) continue;

            float dist = Vector3.Distance(agent.transform.position, position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = agent;
            }
        }
        return nearest;
    }

    public AgentBehaviorTree GetLeader()
    {
        foreach (AgentBehaviorTree agent in allAgents)
        {
            if (agent.role == AgentRole.Leader) return agent;
        }
        return null;
    }

    // Cere tuturor agentilor sa se indrepte spre inamic
    public void RequestAllAgentsToEngage(Vector3 enemyPosition)
    {
        lastKnownEnemyPosition = enemyPosition;
        enemySpotted = true;
        Debug.Log($"[Blackboard] Leader coordoneaza atac la {enemyPosition}");
    }

    // Returneaza un agent dupa ID
    public AgentBehaviorTree GetAgentByID(string id)
    {
        foreach (AgentBehaviorTree agent in allAgents)
        {
            if (agent.GetAgentID() == id) return agent;
        }
        return null;
    }
}
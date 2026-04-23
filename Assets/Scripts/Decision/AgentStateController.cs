using UnityEngine;

public enum AgentState
{
    Idle,
    Alert,
    Engage,
    Regroup
}

public class AgentStateController : MonoBehaviour
{
    [Header("State")]
    public AgentState currentState = AgentState.Idle;

    [Header("Settings")]
    public float engageDistance = 15f;

    private AgentController agentController;
    private PerceptionModule perception;
    private Vector3 lastKnownEnemyPosition;
    private Vector3 startPosition;

    void Awake()
    {
        agentController = GetComponent<AgentController>();
        perception = GetComponent<PerceptionModule>();
        startPosition = transform.position;
    }

    void Update()
    {
        UpdateState();
        ExecuteState();
    }

    void UpdateState()
    {
        if (perception.CanSeeEnemies())
        {
            // Vede inamic direct
            lastKnownEnemyPosition = perception.GetNearestEnemy().position;
            currentState = AgentState.Engage;
        }
        else if (currentState == AgentState.Engage)
        {
            // Nu mai vede inamicul dar stie ultima pozitie
            currentState = AgentState.Alert;
        }
        else if (currentState == AgentState.Alert)
        {
            // A ajuns la ultima pozitie cunoscuta, se intoarce
            if (agentController.HasReachedDestination())
                currentState = AgentState.Regroup;
        }
        else if (currentState == AgentState.Regroup)
        {
            // A ajuns inapoi la pozitia initiala
            if (agentController.HasReachedDestination())
                currentState = AgentState.Idle;
        }
    }

    void ExecuteState()
    {
        switch (currentState)
        {
            case AgentState.Idle:
                agentController.Stop();
                break;

            case AgentState.Alert:
                // Se duce la ultima pozitie cunoscuta a inamicului
                agentController.MoveTo(lastKnownEnemyPosition);
                break;

            case AgentState.Engage:
                // Urmareste inamicul in timp real
                Transform enemy = perception.GetNearestEnemy();
                if (enemy != null)
                    agentController.MoveTo(enemy.position);
                break;

            case AgentState.Regroup:
                // Se intoarce la pozitia de start
                agentController.MoveTo(startPosition);
                break;
        }
    }
}
using UnityEngine;

public class TestMove : MonoBehaviour
{
    private AgentController agent;

    void Start()
    {
        agent = GetComponent<AgentController>();
        // Trimite agentul la coordonatele (5, 0, 5)
        agent.MoveTo(new Vector3(15, 0, 25));
    }
}
using UnityEngine;
using UnityEngine.AI;

public class SecondaryEnemyController : MonoBehaviour
{
    [Header("Settings")]
    public float fleeSpeed = 5f;
    public bool isLiberated = false;
    public bool isLeftSide = true;

    private NavMeshAgent navAgent;
    private TacticalBlackboard blackboard;
    private EnemyController mainEnemy;
    private float updateTimer = 0f;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        navAgent.enabled = false;
    }

    void Start()
    {
        blackboard = TacticalBlackboard.Instance;
        mainEnemy = FindObjectOfType<EnemyController>();
    }

    void Update()
    {
        if (!isLiberated) return;

        updateTimer += Time.deltaTime;
        if (updateTimer >= 0.3f)
        {
            updateTimer = 0f;
            UpdateFleeTarget();
        }
    }

    public void Liberate()
    {
        isLiberated = true;
        navAgent.enabled = true;
        navAgent.speed = fleeSpeed;
        Debug.Log($"[Enemy] {gameObject.name} eliberat!");
    }

    void UpdateFleeTarget()
    {
        if (mainEnemy == null) return;

        // Urmeaza inamicul principal cu offset lateral
        Vector3 mainPos = mainEnemy.transform.position;

        Vector3 sideOffset = isLeftSide
            ? new Vector3(3f, 0, 0)
            : new Vector3(-3f, 0, 0);

        Vector3 targetPos = mainPos + sideOffset;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(targetPos, out hit, 5f, NavMesh.AllAreas))
            navAgent.SetDestination(hit.position);
        else
            navAgent.SetDestination(mainPos); // fallback - merge direct la principal
    }
}
using UnityEngine;

public class AgentBehaviorTree : MonoBehaviour
{
    [Header("Role & Identity")]
    public AgentRole role = AgentRole.Scout;
    public string agentID;

    [Header("Settings")]
    public float attackRange = 3f;
    public float sniperRange = 15f;
    public float patrolRadius = 8f;

    [Header("Formation")]
    public int formationIndex = 0;

    private BTNode behaviorTree;
    private AgentController agentController;
    private PerceptionModule perception;
    private TacticalBlackboard blackboard;
    private Vector3 startPosition;
    private Vector3 patrolTarget;

    void Awake()
    {
        agentController = GetComponent<AgentController>();
        perception = GetComponent<PerceptionModule>();
        startPosition = transform.position;

        if (string.IsNullOrEmpty(agentID))
            agentID = System.Guid.NewGuid().ToString().Substring(0, 8);
    }

    void Start()
    {
        blackboard = TacticalBlackboard.Instance;
        blackboard?.RegisterAgent(this);
        BuildBehaviorTree();
        SetNewPatrolTarget();
    }

    void Update()
    {
        behaviorTree?.Evaluate();
    }

    void BuildBehaviorTree()
    {
        switch (role)
        {
            case AgentRole.Leader: BuildLeaderTree(); break;
            case AgentRole.Scout: BuildScoutTree(); break;
            case AgentRole.Support: BuildSupportTree(); break;
            case AgentRole.Sniper: BuildSniperTree(); break;
        }
    }

    // ── LEADER ─────────────────────────────────────────
    // Patruleaza, raporteaza inamic, se opreste si coordoneaza
    void BuildLeaderTree()
    {
        behaviorTree = new BTSelector(

            // 1. Vede inamic → se opreste si coordoneaza
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);
                    blackboard?.RequestAllAgentsToEngage(enemy.position);
                    agentController.Stop(); // Leader-ul se opreste
                    return NodeState.Running;
                })
            ),

            // 2. Patruleaza
            new BTAction(Patrol)
        );
    }

    // ── SCOUT ──────────────────────────────────────────
    // Merge in fata, raporteaza si urmareste inamicul
    void BuildScoutTree()
    {
        behaviorTree = new BTSelector(

            // 1. Vede inamic → raporteaza si urmareste
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);
                    agentController.MoveTo(enemy.position);
                    return NodeState.Running;
                })
            ),

            // 2. Mentine pozitia in fata Leader-ului
            new BTSequence(
                new BTCondition(() => blackboard?.GetLeader() != null),
                new BTAction(MaintainFormation)
            ),

            // 3. Patruleaza
            new BTAction(Patrol)
        );
    }

    // ── SUPPORT ────────────────────────────────────────
    // Urmeaza Leader-ul, ajuta colegii
    void BuildSupportTree()
    {
        behaviorTree = new BTSelector(

            // 1. Vede inamic direct → se angajeaza
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);
                    agentController.MoveTo(enemy.position);
                    return NodeState.Running;
                })
            ),

            // 2. Coleg cere ajutor → se duce la el
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.helpRequests.Count > 0),
                new BTAction(SupportAlly)
            ),

            // 3. Urmeaza Leader-ul in formatie
            new BTSequence(
                new BTCondition(() => blackboard?.GetLeader() != null),
                new BTAction(MaintainFormation)
            ),

            // 4. Patruleaza
            new BTAction(Patrol)
        );
    }

    // ── SNIPER ─────────────────────────────────────────
    // Urmeaza formatia, se opreste la distanta cand vede inamic
    void BuildSniperTree()
    {
        behaviorTree = new BTSelector(

            // 1. Vede inamic → se opreste la distanta mare
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);

                    float dist = Vector3.Distance(
                        transform.position, enemy.position);

                    if (dist > sniperRange)
                    {
                        // Se apropie pana la sniperRange
                        Vector3 dir = (enemy.position -
                            transform.position).normalized;
                        Vector3 targetPos = enemy.position -
                            dir * sniperRange;
                        agentController.MoveTo(targetPos);
                    }
                    else
                    {
                        // Este la distanta optima, sta pe loc
                        agentController.Stop();
                    }
                    return NodeState.Running;
                })
            ),

            // 2. Urmeaza formatia
            new BTSequence(
                new BTCondition(() => blackboard?.GetLeader() != null),
                new BTAction(MaintainFormation)
            ),

            // 3. Patruleaza
            new BTAction(Patrol)
        );
    }

    // ── ACTIUNI COMUNE ─────────────────────────────────

    NodeState MaintainFormation()
    {
        if (FormationManager.Instance == null) return NodeState.Failure;

        AgentBehaviorTree leader = blackboard?.GetLeader();
        if (leader == null) return NodeState.Failure;

        int totalAgents = blackboard.allAgents.Count - 1;
        Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
            leader.transform.position,
            leader.transform.rotation,
            formationIndex,
            totalAgents);

        agentController.MoveTo(formationPos);
        return NodeState.Running;
    }

    NodeState SupportAlly()
    {
        if (blackboard == null || blackboard.helpRequests.Count == 0)
            return NodeState.Failure;

        AgentBehaviorTree ally = blackboard.GetAgentByID(
            blackboard.helpRequests[0]);
        if (ally == null) return NodeState.Failure;

        agentController.MoveTo(ally.transform.position);

        if (agentController.HasReachedDestination())
        {
            blackboard.ResolveHelp(blackboard.helpRequests[0]);
            return NodeState.Success;
        }
        return NodeState.Running;
    }

    NodeState Patrol()
    {
        if (agentController.HasReachedDestination())
            SetNewPatrolTarget();

        agentController.MoveTo(patrolTarget);
        return NodeState.Running;
    }

    void SetNewPatrolTarget()
    {
        Vector3 randomOffset = new Vector3(
            Random.Range(-patrolRadius, patrolRadius),
            0,
            Random.Range(-patrolRadius, patrolRadius));
        patrolTarget = startPosition + randomOffset;
    }

    public string GetAgentID() => agentID;
}
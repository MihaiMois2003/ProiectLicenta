using UnityEngine;

public class AgentBehaviorTree : MonoBehaviour
{
    [Header("Role & Identity")]
    public AgentRole role = AgentRole.Scout;
    public string agentID;

    [Header("Formation")]
    public int formationRow = 0;
    public int formationIndexInRow = 0;
    public int formationTotalInRow = 1;

    [Header("Speed Settings")]
    public float normalSpeed = 3.5f;
    public float catchUpSpeed = 6f;
    public float catchUpDistance = 3f;

    [Header("Patrol Settings")]
    public float patrolRadius = 8f;

    private BTNode behaviorTree;
    private AgentController agentController;
    private PerceptionModule perception;
    private TacticalBlackboard blackboard;
    private Vector3 startPosition;
    private Vector3 patrolTarget;

    public string agentIDPublic => agentID;

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
        UpdateSpeed();
    }

    void UpdateSpeed()
    {
        if (blackboard == null) return;

        AgentBehaviorTree leader = blackboard.GetLeader();
        if (leader == null || role == AgentRole.Leader) return;

        // Daca e departe de pozitia din formatie → viteza marita
        Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
            leader.transform.position,
            leader.transform.rotation,
            formationRow,
            formationIndexInRow,
            formationTotalInRow);

        float distToFormation = Vector3.Distance(
            transform.position, formationPos);

        if (distToFormation > catchUpDistance)
            agentController.SetSpeed(catchUpSpeed);
        else
            agentController.SetSpeed(normalSpeed);
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

    // ── LEADER ─────────────────────────────────────
    void BuildLeaderTree()
    {
        behaviorTree = new BTSelector(

            // Combat → urmareste inamicul
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Combat),
                new BTAction(() => {
                    agentController.MoveTo(blackboard.lastKnownEnemyPosition);
                    return NodeState.Running;
                })
            ),

            // Idle → sta pe loc
            new BTAction(() => {
                agentController.Stop();
                return NodeState.Running;
            })
        );
    }

    // ── SCOUT ───────────────────────────────────────
    void BuildScoutTree()
    {
        behaviorTree = new BTSelector(

            // Vede inamic → raporteaza
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    if (blackboard != null && blackboard.mainEnemy != null)
                        blackboard.ReportEnemy(blackboard.mainEnemy.position, agentID);
                    return NodeState.Success;
                })
            ),

            // Combat → mentine formatia
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Combat),
                new BTAction(MaintainFormation)
            ),

            // Idle → patruleaza toata harta
            new BTAction(Patrol)
        );
    }

    // ── SUPPORT ─────────────────────────────────────
    void BuildSupportTree()
    {
        behaviorTree = new BTSelector(

            // Vede inamic → raporteaza
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    if (blackboard != null && blackboard.mainEnemy != null)
                        blackboard.ReportEnemy(blackboard.mainEnemy.position, agentID);
                    return NodeState.Success;
                })
            ),

            // Combat → mentine formatia
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Combat),
                new BTAction(MaintainFormation)
            ),

            // Idle → patruleaza perimetru definit
            new BTAction(PatrolPerimeter)
        );
    }

    // ── SNIPER ──────────────────────────────────────
    void BuildSniperTree()
    {
        behaviorTree = new BTSelector(

            // Combat → merge la pozitia de sniper si priveste spre inamic
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Combat),
                new BTAction(SniperCombat)
            ),

            // Idle → patruleaza perimetru definit
            new BTAction(PatrolPerimeter)
        );
    }

    // ── ACTIUNI ─────────────────────────────────────

    NodeState MaintainFormation()
    {
        if (FormationManager.Instance == null) return NodeState.Failure;

        AgentBehaviorTree leader = blackboard?.GetLeader();
        if (leader == null) return NodeState.Failure;

        Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
            leader.transform.position,
            leader.transform.rotation,
            formationRow,
            formationIndexInRow,
            formationTotalInRow);

        agentController.MoveTo(formationPos);
        return NodeState.Running;
    }

    NodeState SniperCombat()
    {
        // Determina pozitia sniper-ului in functie de index
        Vector3 sniperPos = formationIndexInRow == 0
            ? blackboard.sniperPosition1
            : blackboard.sniperPosition2;

        agentController.MoveTo(sniperPos);

        // Priveste spre inamic
        if (blackboard.enemySpotted)
        {
            Vector3 dirToEnemy = (blackboard.lastKnownEnemyPosition
                - transform.position).normalized;
            if (dirToEnemy != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(dirToEnemy);
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

    NodeState PatrolPerimeter()
    {
        if (agentController.HasReachedDestination())
            SetNewPerimeterTarget();

        agentController.MoveTo(patrolTarget);
        return NodeState.Running;
    }

    void SetNewPatrolTarget()
    {
        // Patrulare pe toata harta
        patrolTarget = new Vector3(
            Random.Range(-20f, 20f),
            0,
            Random.Range(-20f, 20f));
    }

    void SetNewPerimeterTarget()
    {
        // Patrulare in jurul pozitiei de start
        patrolTarget = startPosition + new Vector3(
            Random.Range(-patrolRadius, patrolRadius),
            0,
            Random.Range(-patrolRadius, patrolRadius));
    }

    public string GetAgentID() => agentID;
}
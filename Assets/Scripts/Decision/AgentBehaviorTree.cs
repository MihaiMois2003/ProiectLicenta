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

    [Header("Group")]
    public int groupID = -1;

    private BTNode behaviorTree;
    private AgentController agentController;
    private PerceptionModule perception;
    private TacticalBlackboard blackboard;
    private Vector3 startPosition;
    private Vector3 patrolTarget;
    private Transform phase2Target;
    private float phase2TargetUpdateTimer = 0f;

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
        if (role == AgentRole.Leader || role == AgentRole.Sniper) return;
        if (FormationManager.Instance == null) return;

        // In Faza 2 viteza se calculeaza fata de liderul grupului
        AgentBehaviorTree referenceLeader = GetGroupLeader();
        if (referenceLeader == null) return;

        Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
            referenceLeader.transform.position,
            referenceLeader.transform.rotation,
            formationRow,
            formationIndexInRow,
            formationTotalInRow);

        float distToFormation = Vector3.Distance(transform.position, formationPos);

        if (distToFormation > catchUpDistance)
            agentController.SetSpeed(catchUpSpeed);
        else
            agentController.SetSpeed(normalSpeed);
    }

    // Returneaza liderul grupului in Faza 2, sau liderul global in Faza 1
    AgentBehaviorTree GetGroupLeader()
    {
        if (!blackboard.phase2Active)
            return blackboard.GetLeader();

        // In Faza 2 liderul grupului e primul agent din grup (formationRow == 0)
        foreach (EnemyGroup group in blackboard.enemyGroups)
        {
            if (group.groupID != groupID) continue;
            foreach (AgentBehaviorTree agent in group.agents)
            {
                if (agent.formationRow == 0) return agent;
            }
        }
        return null;
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

    public void RebuildTree()
    {
        BuildBehaviorTree();
    }

    // ── LEADER ─────────────────────────────────────
    void BuildLeaderTree()
    {
        behaviorTree = new BTSelector(

            // Faza 2 → urmareste cel mai apropiat inamic
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2FollowEnemy)
            ),

            // Combat → urmareste inamicul principal
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Combat),
                new BTAction(() => {
                    if (blackboard.mainEnemy != null)
                        agentController.MoveTo(blackboard.mainEnemy.position);
                    return NodeState.Running;
                })
            ),

            // Engaging → merge spre inamic, la 8 unitati porneste Combat
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Engaging),
                new BTAction(() => {
                    if (blackboard.mainEnemy == null) return NodeState.Failure;

                    float dist = Vector3.Distance(
                        transform.position, blackboard.mainEnemy.position);

                    if (dist <= 8f)
                    {
                        blackboard.combatState = CombatState.Combat;
                        return NodeState.Running;
                    }

                    agentController.MoveTo(blackboard.mainEnemy.position);
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

            // Faza 2 → mentine formatia grupului
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2MaintainFormation)
            ),

            // Vede inamic → raporteaza
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);
                    return NodeState.Success;
                })
            ),

            // Engaging sau Combat → mentine formatia globala
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Combat)),
                new BTAction(MaintainFormation)
            ),

            // Idle → patruleaza
            new BTAction(Patrol)
        );
    }

    // ── SUPPORT ─────────────────────────────────────
    void BuildSupportTree()
    {
        behaviorTree = new BTSelector(

            // Faza 2 → mentine formatia grupului
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2MaintainFormation)
            ),

            // Vede inamic → raporteaza
            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);
                    return NodeState.Success;
                })
            ),

            // Engaging sau Combat → mentine formatia globala
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Combat)),
                new BTAction(MaintainFormation)
            ),

            // Idle → patruleaza perimetru
            new BTAction(PatrolPerimeter)
        );
    }

    // ── SNIPER ──────────────────────────────────────
    void BuildSniperTree()
    {
        behaviorTree = new BTSelector(

            // Engaging sau Combat → merge la pozitie si sta acolo
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Combat)),
                new BTAction(SniperCombat)
            ),

            // Idle → patruleaza perimetru
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

    NodeState Phase2MaintainFormation()
    {
        if (FormationManager.Instance == null) return NodeState.Failure;

        // Gaseste liderul grupului meu
        AgentBehaviorTree groupLeader = GetGroupLeader();
        if (groupLeader == null) return NodeState.Failure;

        // Daca eu sunt liderul grupului → urmaresc inamicul
        if (groupLeader == this)
            return Phase2FollowEnemy();

        // Altfel → mentine formatia in jurul liderului grupului
        Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
            groupLeader.transform.position,
            groupLeader.transform.rotation,
            formationRow,
            formationIndexInRow,
            formationTotalInRow);

        agentController.MoveTo(formationPos);
        return NodeState.Running;
    }

    NodeState Phase2FollowEnemy()
    {
        // Actualizeaza tinta la fiecare 2 secunde
        phase2TargetUpdateTimer += Time.deltaTime;
        if (phase2TargetUpdateTimer >= 2f || phase2Target == null)
        {
            phase2TargetUpdateTimer = 0f;
            Vector3 groupCenter = GetGroupCenter();
            phase2Target = blackboard.GetNearestEnemyFor(groupCenter);
        }

        if (phase2Target == null) return NodeState.Failure;

        // Verifica daca tinta a murit
        HealthSystem hs = phase2Target.GetComponent<HealthSystem>();
        if (hs != null && hs.isDead)
        {
            phase2Target = null;
            return NodeState.Failure;
        }

        agentController.MoveTo(phase2Target.position);
        return NodeState.Running;
    }

    Vector3 GetGroupCenter()
    {
        Vector3 center = Vector3.zero;
        int count = 0;

        foreach (EnemyGroup group in blackboard.enemyGroups)
        {
            if (group.groupID != groupID) continue;
            foreach (AgentBehaviorTree agent in group.agents)
            {
                center += agent.transform.position;
                count++;
            }
        }

        return count > 0 ? center / count : transform.position;
    }

    NodeState SniperCombat()
    {
        Vector3 sniperPos = formationIndexInRow == 0
            ? blackboard.sniperPosition1
            : blackboard.sniperPosition2;

        // Daca nu a ajuns inca la pozitie → merge acolo
        float distToPos = Vector3.Distance(transform.position, sniperPos);
        if (distToPos > 1f)
        {
            agentController.MoveTo(sniperPos);
            return NodeState.Running;
        }

        // A ajuns → se opreste si priveste spre cel mai apropiat inamic
        agentController.Stop();

        Transform nearestEnemy = blackboard.GetNearestEnemyFor(transform.position);
        if (nearestEnemy != null)
        {
            Vector3 dirToEnemy = (nearestEnemy.position - transform.position).normalized;
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
        patrolTarget = new Vector3(
            Random.Range(-20f, 20f),
            0,
            Random.Range(-20f, 20f));
    }

    void SetNewPerimeterTarget()
    {
        patrolTarget = startPosition + new Vector3(
            Random.Range(-patrolRadius, patrolRadius),
            0,
            Random.Range(-patrolRadius, patrolRadius));
    }

    public string GetAgentID() => agentID;
}
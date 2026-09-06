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
    public float catchUpSpeed = 5.5f;
    public float catchUpDistance = 3f;
    [Tooltip("Viteza agentilor cand FUG la reversal (mica, ca inamicii sa-i prinda).")]
    public float reversalFleeSpeed = 3f;

    [Header("Patrol Settings")]
    public float patrolRadius = 8f;

    [Header("Group")]
    public int groupID = -1;

    [Header("Flee Settings (reversal)")]
    [Tooltip("Distanta minima pe care grupul incearca sa o pastreze fata de inamic in reversal.")]
    public float fleeDistance = 15f;

    [HideInInspector]
    public Transform currentCombatTarget;

    [HideInInspector] public bool knowsEnemy = false;
    [HideInInspector] public Vector3 knownEnemyPosition;
    [HideInInspector] public float timeLearnedEnemy = -1f;

    public bool KnowsEnemy()
    {
        if (blackboard == null) return false;

        if (blackboard.phase2Active) return true;
        var cfg = ExperimentConfig.Instance;
        if (cfg == null || cfg.communicationMode == CommunicationMode.Blackboard)
            return blackboard.enemySpotted;
        return knowsEnemy;
    }

    public void ReceiveEnemyReport(Vector3 position)
    {
        if (!knowsEnemy) timeLearnedEnemy = Time.time;
        knowsEnemy = true;
        knownEnemyPosition = position;
    }

    public void ClearEnemyKnowledge()
    {
        knowsEnemy = false;
        timeLearnedEnemy = -1f;
    }

    private BTNode behaviorTree;
    private AgentController agentController;
    private PerceptionModule perception;
    private TacticalBlackboard blackboard;
    private Vector3 startPosition;
    private Vector3 patrolTarget;
    private bool sniperPositionReached = false;

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

    }

    private bool agentInitialized = false;

    void Update()
    {
        if (!TacticalBlackboard.IsRunning())
        {

            var na = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (na != null && na.isOnNavMesh)
            {
                na.isStopped = true;
                na.velocity = Vector3.zero;
            }
            return;
        }

        if (!agentInitialized)
        {
            agentInitialized = true;
            var na = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (na != null && na.isOnNavMesh) na.isStopped = false;
            SetNewPatrolTarget();
        }

        UpdateCombatTarget();
        behaviorTree?.Evaluate();
        UpdateSpeed();
        UpdateSupportRegen();
        UpdateHelpRequest();
    }

    [HideInInspector] public bool isRespondingToHelp = false;
    [HideInInspector] public AgentBehaviorTree helpTarget = null;

    void UpdateHelpRequest()
    {
        var cfg = ExperimentConfig.Instance;
        if (cfg == null || !cfg.helpRequestEnabled) return;
        if (blackboard == null) return;

        HealthSystem ownHS = GetComponent<HealthSystem>();
        if (ownHS == null || ownHS.isDead) return;

        if (role == AgentRole.Sniper) return;

        if (ownHS.GetHPPercentage() < cfg.helpRequestThreshold)
        {
            blackboard.RequestHelp(agentID);
        }
        else
        {
            blackboard.ResolveHelp(agentID);
        }

        if (ownHS.GetHPPercentage() >= cfg.helpRequestThreshold)
        {
            AgentBehaviorTree needy = FindNearestHelpRequester();
            if (needy != null && needy != this)
            {

                if (AmINearestHelperTo(needy))
                {
                    isRespondingToHelp = true;
                    helpTarget = needy;
                    agentController.MoveTo(needy.transform.position);
                    return;
                }
            }
        }
        isRespondingToHelp = false;
        helpTarget = null;
    }

    AgentBehaviorTree FindNearestHelpRequester()
    {
        AgentBehaviorTree nearest = null;
        float minDist = Mathf.Infinity;
        foreach (string id in blackboard.helpRequests)
        {
            AgentBehaviorTree a = blackboard.GetAgentByID(id);
            if (a == null) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            float d = Vector3.Distance(transform.position, a.transform.position);
            if (d < minDist) { minDist = d; nearest = a; }
        }
        return nearest;
    }

    bool AmINearestHelperTo(AgentBehaviorTree needy)
    {
        var cfg = ExperimentConfig.Instance;
        float myDist = Vector3.Distance(transform.position, needy.transform.position);
        foreach (AgentBehaviorTree a in blackboard.allAgents)
        {
            if (a == null || a == this || a == needy) continue;
            if (a.role == AgentRole.Sniper) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            if (hs.GetHPPercentage() < (cfg != null ? cfg.helpRequestThreshold : 0.3f)) continue;
            float d = Vector3.Distance(a.transform.position, needy.transform.position);
            if (d < myDist) return false;
        }
        return true;
    }

    void UpdateSupportRegen()
    {
        if (role != AgentRole.Support) return;
        var cfg = ExperimentConfig.Instance;
        if (cfg == null || !cfg.supportRegenEnabled) return;

        HealthSystem ownHS = GetComponent<HealthSystem>();
        if (ownHS == null || ownHS.isDead) return;

        float regenRadius = 6f;
        int allyLayer = LayerMask.GetMask("Ally");
        Collider[] near = Physics.OverlapSphere(transform.position, regenRadius, allyLayer);
        float amount = cfg.supportRegenPerSecond * Time.deltaTime;

        foreach (Collider c in near)
        {
            HealthSystem hs = c.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            if (hs.currentHP >= hs.maxHP) continue;
            hs.Heal(amount);
        }
    }

    void UpdateCombatTarget()
    {
        if (blackboard == null) { currentCombatTarget = null; return; }

        if (role == AgentRole.Sniper)
        {
            currentCombatTarget = null;
            return;
        }

        if (blackboard.phase2Active)
        {
            currentCombatTarget = blackboard.GetAssignedEnemyForGroup(groupID);
            return;
        }

        if (blackboard.combatState == CombatState.Combat)
        {
            currentCombatTarget = blackboard.mainEnemy;
            return;
        }

        currentCombatTarget = null;
    }

    void UpdateSpeed()
    {
        if (blackboard == null) return;
        if (FormationManager.Instance == null) return;

        if (role == AgentRole.Sniper) return;

        if (blackboard.phase2Active)
        {
            AgentBehaviorTree groupLeader = GetGroupLeader();
            bool iAmGroupLeader = (groupLeader == this);

            if (blackboard.rolesReversed)
            {

                agentController.SetSpeed(reversalFleeSpeed);
            }
            else if (iAmGroupLeader)
            {

                agentController.SetSpeed(catchUpSpeed);
            }
            else
            {

                SetSpeedByFormationDistance(groupLeader);
            }
            return;
        }

        if (role == AgentRole.Leader)
        {

            bool pursuing = blackboard.combatState == CombatState.Engaging ||
                            blackboard.combatState == CombatState.Rallying ||
                            blackboard.combatState == CombatState.Combat;
            agentController.SetSpeed(pursuing ? catchUpSpeed : normalSpeed);
            return;
        }

        SetSpeedByFormationDistance(blackboard.GetLeader());
    }

    void SetSpeedByFormationDistance(AgentBehaviorTree referenceLeader)
    {
        if (referenceLeader == null) { agentController.SetSpeed(normalSpeed); return; }

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

    AgentBehaviorTree GetGroupLeader()
    {
        if (!blackboard.phase2Active)
            return blackboard.GetLeader();

        foreach (EnemyGroup group in blackboard.enemyGroups)
        {
            if (group.groupID != groupID) continue;

            foreach (AgentBehaviorTree agent in group.agents)
            {
                if (agent == null) continue;
                if (agent.formationRow != 0) continue;
                HealthSystem hs = agent.GetComponent<HealthSystem>();
                if (hs != null && !hs.isDead) return agent;
            }

            AgentBehaviorTree newLeader = null;
            foreach (AgentBehaviorTree agent in group.agents)
            {
                if (agent == null) continue;
                HealthSystem hs = agent.GetComponent<HealthSystem>();
                if (hs == null || hs.isDead) continue;
                newLeader = agent;
                break;
            }

            if (newLeader != null)
            {

                newLeader.formationRow = 0;
                newLeader.formationIndexInRow = 0;
                newLeader.formationTotalInRow = 1;
            }
            return newLeader;
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

    void BuildLeaderTree()
    {
        behaviorTree = new BTSelector(
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2MaintainFormation)
            ),

            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Combat),
                new BTAction(() => {
                    if (blackboard.mainEnemy != null)
                        agentController.MoveTo(blackboard.mainEnemy.position);
                    return NodeState.Running;
                })
            ),

            new BTSequence(
                new BTCondition(() => KnowsEnemy() && blackboard != null &&
                    blackboard.combatState == CombatState.Engaging),
                new BTAction(() => {
                    if (blackboard.mainEnemy == null) return NodeState.Failure;

                    float dist = Vector3.Distance(
                        transform.position, blackboard.mainEnemy.position);

                    if (dist <= blackboard.rallyDistance)
                    {
                        blackboard.combatState = CombatState.Rallying;
                        blackboard.rallyStartTime = Time.time;
                        return NodeState.Running;
                    }

                    agentController.MoveTo(blackboard.mainEnemy.position);
                    return NodeState.Running;
                })
            ),

            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Rallying),
                new BTAction(() => {
                    if (blackboard.mainEnemy == null) return NodeState.Failure;

                    if (blackboard.RallyComplete())
                    {
                        blackboard.combatState = CombatState.Combat;
                        return NodeState.Running;
                    }

                    float dist = Vector3.Distance(
                        transform.position, blackboard.mainEnemy.position);
                    if (dist < blackboard.rallyDistance - 1f)
                    {

                        Vector3 away = (transform.position - blackboard.mainEnemy.position).normalized;
                        agentController.MoveTo(transform.position + away * 2f);
                    }
                    else
                    {
                        agentController.Stop();
                    }
                    return NodeState.Running;
                })
            ),

            new BTAction(() => {
                agentController.Stop();
                return NodeState.Running;
            })
        );
    }

    void BuildScoutTree()
    {
        behaviorTree = new BTSelector(
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2MaintainFormation)
            ),

            new BTSequence(
                new BTCondition(() => perception.HasRememberedEnemy()),
                new BTAction(() => {
                    Vector3 pos = perception.GetRememberedEnemyPosition();
                    blackboard?.ReportEnemy(pos, agentID);
                    return NodeState.Success;
                })
            ),

            new BTSequence(
                new BTCondition(() => KnowsEnemy() && blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Rallying ||
                     blackboard.combatState == CombatState.Combat)),
                new BTAction(MaintainFormation)
            ),

            new BTAction(Patrol)
        );
    }

    void BuildSupportTree()
    {
        behaviorTree = new BTSelector(
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2MaintainFormation)
            ),

            new BTSequence(
                new BTCondition(() => perception.HasRememberedEnemy()),
                new BTAction(() => {
                    Vector3 pos = perception.GetRememberedEnemyPosition();
                    blackboard?.ReportEnemy(pos, agentID);
                    return NodeState.Success;
                })
            ),

            new BTSequence(
                new BTCondition(() => KnowsEnemy() && blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Rallying ||
                     blackboard.combatState == CombatState.Combat)),
                new BTAction(MaintainFormation)
            ),

            new BTAction(PatrolPerimeter)
        );
    }

    void BuildSniperTree()
    {
        behaviorTree = new BTSelector(
            new BTSequence(
                new BTCondition(() => sniperPositionReached),
                new BTAction(SniperHoldPosition)
            ),

            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Combat ||
                     blackboard.phase2Active)),
                new BTAction(SniperGoToPosition)
            ),

            new BTAction(SniperGoToPosition)
        );
    }

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

    public bool IsNearFormationSlot(float tolerance)
    {
        if (FormationManager.Instance == null) return true;
        AgentBehaviorTree leader = blackboard?.GetLeader();
        if (leader == null) return true;

        Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
            leader.transform.position,
            leader.transform.rotation,
            formationRow,
            formationIndexInRow,
            formationTotalInRow);

        Vector3 a = transform.position; a.y = 0;
        Vector3 b = formationPos; b.y = 0;
        return Vector3.Distance(a, b) <= tolerance;
    }

    NodeState Phase2MaintainFormation()
    {
        if (FormationManager.Instance == null) return NodeState.Failure;

        AgentBehaviorTree groupLeader = GetGroupLeader();
        if (groupLeader == null) return NodeState.Failure;

        if (blackboard.rolesReversed)
        {
            if (groupLeader == this)
                return Phase2FleeAsLeader();

            Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
                groupLeader.transform.position,
                groupLeader.transform.rotation,
                formationRow,
                formationIndexInRow,
                formationTotalInRow);

            agentController.MoveTo(formationPos);
            return NodeState.Running;
        }

        if (groupLeader == this)
            return Phase2FollowEnemy();

        Vector3 normalFormationPos = FormationManager.Instance.GetFormationPosition(
            groupLeader.transform.position,
            groupLeader.transform.rotation,
            formationRow,
            formationIndexInRow,
            formationTotalInRow);

        agentController.MoveTo(normalFormationPos);
        return NodeState.Running;
    }

    NodeState Phase2FollowEnemy()
    {
        Transform target = blackboard.GetAssignedEnemyForGroup(groupID);

        if (target == null)
        {

            target = FindNearestLivingEnemy();
            if (target == null) return NodeState.Failure;
        }

        Vector3 dest = ComputeApproachDestination(target.position);
        agentController.MoveTo(dest);

        Vector3 dir = target.position - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, Time.deltaTime * 5f);
        }

        return NodeState.Running;
    }

    Transform FindNearestLivingEnemy()
    {
        Transform nearest = null;
        float minDist = Mathf.Infinity;

        if (blackboard.mainEnemy != null)
        {
            HealthSystem hs = blackboard.mainEnemy.GetComponent<HealthSystem>();
            if (hs != null && !hs.isDead)
            {
                float d = Vector3.Distance(transform.position, blackboard.mainEnemy.position);
                if (d < minDist) { minDist = d; nearest = blackboard.mainEnemy; }
            }
        }

        SecondaryEnemyController[] secs =
            Object.FindObjectsByType<SecondaryEnemyController>(FindObjectsSortMode.None);
        foreach (SecondaryEnemyController s in secs)
        {
            HealthSystem hs = s.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            float d = Vector3.Distance(transform.position, s.transform.position);
            if (d < minDist) { minDist = d; nearest = s.transform; }
        }

        return nearest;
    }

    Vector3 ComputeApproachDestination(Vector3 targetPos)
    {
        var cfg = ExperimentConfig.Instance;
        PlanningMode mode = cfg != null ? cfg.planningMode : PlanningMode.Reactive;

        switch (mode)
        {
            case PlanningMode.Flanking:
                return ComputeFlankDestination(targetPos, cfg != null ? cfg.flankRadius : 6f);

            case PlanningMode.CoverPoints:
                return ComputeCoverDestination(targetPos);

            case PlanningMode.Reactive:
            default:
                return targetPos;
        }
    }

    Vector3 ComputeFlankDestination(Vector3 targetPos, float radius)
    {
        int totalGroups = (blackboard != null && blackboard.enemyGroups != null &&
                           blackboard.enemyGroups.Count > 0)
                           ? blackboard.enemyGroups.Count : 1;

        float baseAngle = (360f / Mathf.Max(1, totalGroups)) * Mathf.Max(0, groupID);

        Vector3 toTarget = transform.position - targetPos;
        toTarget.y = 0;
        float startAngle = toTarget.sqrMagnitude > 0.01f
            ? Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg
            : 0f;

        float angle = (startAngle + baseAngle) * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
        Vector3 candidate = targetPos + offset;

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out hit, 4f,
            UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;

        return targetPos;
    }

    Vector3 ComputeCoverDestination(Vector3 targetPos)
    {
        TacticalCoverPoint cover = TacticalCoverPoint.GetBestCover(
            transform.position, targetPos);

        if (cover == null) return targetPos;

        float distToCover = Vector3.Distance(transform.position, cover.transform.position);
        if (distToCover > 2.5f)
            return cover.transform.position;

        return targetPos;
    }

    private Vector3 fleeWaypoint;
    private bool hasFleeWaypoint = false;
    private float fleeWaypointTimer = 0f;
    [Header("Flee Internal")]
    [Tooltip("Schimba directia de fuga la fiecare X secunde, chiar daca nu a ajuns inca.")]
    public float fleeRefreshInterval = 2.5f;
    [Tooltip("Distanta minima fata de inamic pe care o cauta cand alege un punct nou de fuga.")]
    public float fleeMinDistanceFromEnemy = 12f;
    [Tooltip("Raza in jurul agentului in care cauta puncte de fuga (limiteaza pana la marginea hartii).")]
    public float fleeSearchRadius = 18f;

    NodeState Phase2FleeAsLeader()
    {
        Transform threat = blackboard.GetAssignedEnemyForGroup(groupID);
        if (threat == null) return NodeState.Failure;

        fleeWaypointTimer += Time.deltaTime;

        bool needsNewWaypoint = false;

        if (!hasFleeWaypoint)
        {
            needsNewWaypoint = true;
        }
        else if (fleeWaypointTimer >= fleeRefreshInterval)
        {

            needsNewWaypoint = true;
        }
        else if (Vector3.Distance(transform.position, fleeWaypoint) < 1.5f)
        {

            needsNewWaypoint = true;
        }
        else
        {

            float waypointDistFromThreat = Vector3.Distance(fleeWaypoint, threat.position);
            if (waypointDistFromThreat < fleeMinDistanceFromEnemy * 0.6f)
                needsNewWaypoint = true;
        }

        if (needsNewWaypoint)
        {
            if (PickRandomFleeWaypoint(threat.position, out fleeWaypoint))
            {
                hasFleeWaypoint = true;
                fleeWaypointTimer = 0f;
            }
            else
            {

                Vector3 awayDir = (transform.position - threat.position);
                awayDir.y = 0;
                if (awayDir.sqrMagnitude < 0.1f)
                    awayDir = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                awayDir.Normalize();
                fleeWaypoint = transform.position + awayDir * fleeDistance;
                hasFleeWaypoint = true;
                fleeWaypointTimer = 0f;
            }
        }

        agentController.MoveTo(fleeWaypoint);

        Vector3 moveDir = fleeWaypoint - transform.position;
        moveDir.y = 0;
        if (moveDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir.normalized);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, Time.deltaTime * 5f);
        }

        return NodeState.Running;
    }

    bool PickRandomFleeWaypoint(Vector3 threatPosition, out Vector3 waypoint)
    {
        for (int i = 0; i < 20; i++)
        {

            Vector2 randomCircle = Random.insideUnitCircle * fleeSearchRadius;
            Vector3 candidate = transform.position +
                new Vector3(randomCircle.x, 0, randomCircle.y);

            float distFromThreat = Vector3.Distance(candidate, threatPosition);
            if (distFromThreat < fleeMinDistanceFromEnemy) continue;

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out hit, 3f,
                UnityEngine.AI.NavMesh.AllAreas))
            {
                waypoint = hit.position;
                return true;
            }
        }

        waypoint = Vector3.zero;
        return false;
    }

    Vector3 GetMySniperPosition()
    {
        return formationIndexInRow == 0
            ? blackboard.sniperPosition1
            : blackboard.sniperPosition2;
    }

    NodeState SniperGoToPosition()
    {
        Vector3 sniperPos = GetMySniperPosition();
        float distToPos = Vector3.Distance(transform.position, sniperPos);

        if (distToPos <= 1f)
        {
            sniperPositionReached = true;
            agentController.Stop();
            return NodeState.Running;
        }

        agentController.MoveTo(sniperPos);
        return NodeState.Running;
    }

    NodeState SniperHoldPosition()
    {
        Vector3 sniperPos = GetMySniperPosition();
        float distToPos = Vector3.Distance(transform.position, sniperPos);

        if (distToPos > 1.5f)
        {
            agentController.MoveTo(sniperPos);
            return NodeState.Running;
        }

        agentController.Stop();

        Transform lookTarget = null;
        CombatModule cm = GetComponent<CombatModule>();
        if (cm != null && cm.GetSniperTarget() != null)
            lookTarget = cm.GetSniperTarget();
        else
            lookTarget = blackboard.GetNearestEnemyFor(transform.position);

        if (lookTarget != null)
        {
            Vector3 dirToEnemy = (lookTarget.position - transform.position);
            dirToEnemy.y = 0;
            if (dirToEnemy.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dirToEnemy.normalized);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot, Time.deltaTime * 5f);
            }
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

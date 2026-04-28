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

    [Header("Flee Settings (reversal)")]
    [Tooltip("Distanta minima pe care grupul incearca sa o pastreze fata de inamic in reversal.")]
    public float fleeDistance = 15f;

    // Tinta de combat curenta - citita de CombatModule pentru a sti pe cine atacam
    [HideInInspector]
    public Transform currentCombatTarget;

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
        SetNewPatrolTarget();
    }

    void Update()
    {
        UpdateCombatTarget();
        behaviorTree?.Evaluate();
        UpdateSpeed();
    }

    // Stabileste tinta de combat curenta in functie de faza si rol.
    // CombatModule citeste acest camp ca sa stie pe cine sa atace.
    void UpdateCombatTarget()
    {
        if (blackboard == null) { currentCombatTarget = null; return; }

        // Sniperii isi gestioneaza singuri tinta in CombatModule (sniperPrivateTarget)
        if (role == AgentRole.Sniper)
        {
            currentCombatTarget = null;
            return;
        }

        // Faza 2: tinta = inamicul asignat grupului
        if (blackboard.phase2Active)
        {
            currentCombatTarget = blackboard.GetAssignedEnemyForGroup(groupID);
            return;
        }

        // Faza 1: tinta = mainEnemy daca suntem in Combat
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
        if (role == AgentRole.Leader || role == AgentRole.Sniper) return;
        if (FormationManager.Instance == null) return;

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

    AgentBehaviorTree GetGroupLeader()
    {
        if (!blackboard.phase2Active)
            return blackboard.GetLeader();

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
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2MaintainFormation)
            ),

            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);
                    return NodeState.Success;
                })
            ),

            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Combat)),
                new BTAction(MaintainFormation)
            ),

            new BTAction(Patrol)
        );
    }

    // ── SUPPORT ─────────────────────────────────────
    void BuildSupportTree()
    {
        behaviorTree = new BTSelector(
            new BTSequence(
                new BTCondition(() => blackboard != null && blackboard.phase2Active),
                new BTAction(Phase2MaintainFormation)
            ),

            new BTSequence(
                new BTCondition(() => perception.CanSeeEnemies()),
                new BTAction(() => {
                    Transform enemy = perception.GetNearestEnemy();
                    if (enemy == null) return NodeState.Failure;
                    blackboard?.ReportEnemy(enemy.position, agentID);
                    return NodeState.Success;
                })
            ),

            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    (blackboard.combatState == CombatState.Engaging ||
                     blackboard.combatState == CombatState.Combat)),
                new BTAction(MaintainFormation)
            ),

            new BTAction(PatrolPerimeter)
        );
    }

    // ── SNIPER ──────────────────────────────────────
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

        AgentBehaviorTree groupLeader = GetGroupLeader();
        if (groupLeader == null) return NodeState.Failure;

        // Daca rolurile sunt inversate, intregul grup fuge in formatie
        if (blackboard.rolesReversed)
        {
            if (groupLeader == this)
                return Phase2FleeAsLeader();

            // Restul: mentine formatia in jurul liderului grupului care fuge
            Vector3 formationPos = FormationManager.Instance.GetFormationPosition(
                groupLeader.transform.position,
                groupLeader.transform.rotation,
                formationRow,
                formationIndexInRow,
                formationTotalInRow);

            agentController.MoveTo(formationPos);
            return NodeState.Running;
        }

        // Comportament normal: liderul de grup urmareste inamicul, restul mentin formatia
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
        if (target == null) return NodeState.Failure;

        agentController.MoveTo(target.position);

        // Roteaza liderul de grup catre tinta (ca sa intoarca formatia in directia buna)
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

    // Liderul grupului fuge haotic: alege puncte random pe harta care sunt
    // destul de departe de inamic. Schimba destinatia cand a ajuns la ea
    // sau dupa un timeout, pentru a parea imprevizibil.
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

        // Stabileste daca avem nevoie de un punct nou de fuga
        bool needsNewWaypoint = false;

        if (!hasFleeWaypoint)
        {
            needsNewWaypoint = true;
        }
        else if (fleeWaypointTimer >= fleeRefreshInterval)
        {
            // A trecut suficient timp - schimba directia ca sa para haotic
            needsNewWaypoint = true;
        }
        else if (Vector3.Distance(transform.position, fleeWaypoint) < 1.5f)
        {
            // A ajuns la punctul curent - alege altul
            needsNewWaypoint = true;
        }
        else
        {
            // Daca punctul actual a ajuns prea aproape de inamic intre timp
            // (inamicul s-a apropiat), il abandonam
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
                // Fallback daca nu a gasit nimic valid - directia opusa simpla
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

        // Liderul de grup priveste in directia in care merge (formatia il urmeaza)
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

    // Cauta un punct random pe NavMesh in jurul agentului care e suficient de departe
    // de inamicul-amenintare. Incearca de mai multe ori cu directii random.
    bool PickRandomFleeWaypoint(Vector3 threatPosition, out Vector3 waypoint)
    {
        for (int i = 0; i < 20; i++)
        {
            // Punct random in jurul pozitiei curente
            Vector2 randomCircle = Random.insideUnitCircle * fleeSearchRadius;
            Vector3 candidate = transform.position +
                new Vector3(randomCircle.x, 0, randomCircle.y);

            // Verifica distanta fata de inamic
            float distFromThreat = Vector3.Distance(candidate, threatPosition);
            if (distFromThreat < fleeMinDistanceFromEnemy) continue;

            // Verifica ca punctul e pe NavMesh (deci accesibil, nu in afara hartii)
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

    // ── SNIPER ACTIONS ──────────────────────────────

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

        // Sniperul priveste catre tinta lui privata (din CombatModule)
        // Daca nu are inca tinta, priveste catre cel mai apropiat inamic
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

    // ── PATROL ──────────────────────────────────────

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
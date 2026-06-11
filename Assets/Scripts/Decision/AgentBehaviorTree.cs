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

    // Tinta de combat curenta - citita de CombatModule pentru a sti pe cine atacam
    [HideInInspector]
    public Transform currentCombatTarget;

    // ── Cunoastere locala despre inamic (folosit la CommunicationMode.LocalBroadcast) ──
    // In modul Blackboard aceste campuri sunt ignorate (toata lumea "stie" prin starea globala).
    [HideInInspector] public bool knowsEnemy = false;
    [HideInInspector] public Vector3 knownEnemyPosition;
    [HideInInspector] public float timeLearnedEnemy = -1f;

    // Helper: agentul "stie" de inamic? In Blackboard = starea globala; in Local = flagul propriu.
    public bool KnowsEnemy()
    {
        if (blackboard == null) return false;
        // In Faza 2 toti agentii au tinte asignate -> stiu prin definitie.
        if (blackboard.phase2Active) return true;
        var cfg = ExperimentConfig.Instance;
        if (cfg == null || cfg.communicationMode == CommunicationMode.Blackboard)
            return blackboard.enemySpotted;
        return knowsEnemy;
    }

    // Apelat de Blackboard cand un raport ajunge la acest agent.
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
        // Nu setam destinatie aici - patrularea porneste dupa START.
    }

    private bool agentInitialized = false;

    void Update()
    {
        if (!TacticalBlackboard.IsRunning())
        {
            // Inghetat: tine agentul pe loc.
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

    // Sistem de cerere de ajutor: agentul sub prag cere ajutor; cel mai apropiat
    // aliat sanatos vine spre el. Activat din config (helpRequestEnabled).
    void UpdateHelpRequest()
    {
        var cfg = ExperimentConfig.Instance;
        if (cfg == null || !cfg.helpRequestEnabled) return;
        if (blackboard == null) return;

        HealthSystem ownHS = GetComponent<HealthSystem>();
        if (ownHS == null || ownHS.isDead) return;

        // Sniperii nu cer si nu raspund (raman la pozitie).
        if (role == AgentRole.Sniper) return;

        // 1. Daca sunt sub prag, cer ajutor.
        if (ownHS.GetHPPercentage() < cfg.helpRequestThreshold)
        {
            blackboard.RequestHelp(agentID);
        }
        else
        {
            blackboard.ResolveHelp(agentID); // m-am refacut, nu mai cer
        }

        // 2. Daca eu sunt sanatos, verific daca trebuie sa raspund la o cerere.
        if (ownHS.GetHPPercentage() >= cfg.helpRequestThreshold)
        {
            AgentBehaviorTree needy = FindNearestHelpRequester();
            if (needy != null && needy != this)
            {
                // Sunt cel mai apropiat aliat sanatos de cel ranit? Atunci ma duc.
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
            if (d < myDist) return false; // altcineva e mai aproape
        }
        return true;
    }

    // Support regenereaza HP-ul aliatilor vii din jur (daca e activat in config).
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
        if (FormationManager.Instance == null) return;

        if (role == AgentRole.Sniper) return;

        // ── FAZA 2 ──
        if (blackboard.phase2Active)
        {
            AgentBehaviorTree groupLeader = GetGroupLeader();
            bool iAmGroupLeader = (groupLeader == this);

            if (blackboard.rolesReversed)
            {
                // REVERSAL: agentii FUG de inamici -> viteza MICA, ca inamicii sa-i prinda.
                agentController.SetSpeed(reversalFleeSpeed);
            }
            else if (iAmGroupLeader)
            {
                // Normal: liderul de grup urmareste inamicul care fuge -> viteza mare.
                agentController.SetSpeed(catchUpSpeed);
            }
            else
            {
                // Restul grupului: catch-up daca a ramas in urma fata de slot.
                SetSpeedByFormationDistance(groupLeader);
            }
            return;
        }

        // ── FAZA 1 ──
        if (role == AgentRole.Leader)
        {
            // Leaderul urmareste inamicul care se plimba haotic -> viteza mare cat urmareste.
            bool pursuing = blackboard.combatState == CombatState.Engaging ||
                            blackboard.combatState == CombatState.Rallying ||
                            blackboard.combatState == CombatState.Combat;
            agentController.SetSpeed(pursuing ? catchUpSpeed : normalSpeed);
            return;
        }

        // Restul (Scout/Support) in Faza 1: catch-up fata de formatie.
        SetSpeedByFormationDistance(blackboard.GetLeader());
    }

    // Seteaza viteza in functie de cat de departe e agentul de slotul lui de formatie.
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

            // Cauta liderul desemnat (formationRow == 0) DACA e viu.
            foreach (AgentBehaviorTree agent in group.agents)
            {
                if (agent == null) continue;
                if (agent.formationRow != 0) continue;
                HealthSystem hs = agent.GetComponent<HealthSystem>();
                if (hs != null && !hs.isDead) return agent;
            }

            // Liderul desemnat e mort (sau lipseste) -> promoveaza primul agent viu.
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
                // Devine noul lider de grup: preia rolul de fruntas al formatiei.
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
                new BTCondition(() => KnowsEnemy() && blackboard != null &&
                    blackboard.combatState == CombatState.Engaging),
                new BTAction(() => {
                    if (blackboard.mainEnemy == null) return NodeState.Failure;

                    float dist = Vector3.Distance(
                        transform.position, blackboard.mainEnemy.position);

                    // Ajuns la distanta de rally -> trecem in Rallying si asteptam echipa.
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

            // RALLYING: Leaderul tine pozitia la rallyDistance si asteapta formatia.
            new BTSequence(
                new BTCondition(() => blackboard != null &&
                    blackboard.combatState == CombatState.Rallying),
                new BTAction(() => {
                    if (blackboard.mainEnemy == null) return NodeState.Failure;

                    // Destui in formatie (sau timeout) -> ATAC.
                    if (blackboard.RallyComplete())
                    {
                        blackboard.combatState = CombatState.Combat;
                        return NodeState.Running;
                    }

                    // Mentine distanta de rally: nu se napusteste, asteapta.
                    float dist = Vector3.Distance(
                        transform.position, blackboard.mainEnemy.position);
                    if (dist < blackboard.rallyDistance - 1f)
                    {
                        // prea aproape, da inapoi un pas
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

    // ── SCOUT ───────────────────────────────────────
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

    // ── SUPPORT ─────────────────────────────────────
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

    // Cat de aproape e agentul de slotul lui de formatie (Faza 1). Folosit pentru rally.
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

        // Destinatia depinde de tehnica de planificare.
        Vector3 dest = ComputeApproachDestination(target.position);
        agentController.MoveTo(dest);

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

    // ── PLANIFICARE: cum se apropie liderul de grup de tinta ──
    // Reactive    = direct la tinta.
    // Flanking    = pe un arc lateral fata de tinta (incercuire).
    // CoverPoints = via cel mai apropiat punct de acoperire (langa obstacol).
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

    // Fiecare grup ataca dintr-un unghi diferit, distribuit pe cerc dupa groupID.
    Vector3 ComputeFlankDestination(Vector3 targetPos, float radius)
    {
        int totalGroups = (blackboard != null && blackboard.enemyGroups != null &&
                           blackboard.enemyGroups.Count > 0)
                           ? blackboard.enemyGroups.Count : 1;

        // Unghi de baza per grup, distribuit uniform pe 360 grade.
        float baseAngle = (360f / Mathf.Max(1, totalGroups)) * Mathf.Max(0, groupID);

        // Mic offset bazat pe directia curenta agent->tinta, ca arcul sa fie fata de pozitia reala.
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

        return targetPos; // fallback
    }

    // Se apropie via cel mai apropiat punct de acoperire (TacticalCoverPoint) fata de drum.
    Vector3 ComputeCoverDestination(Vector3 targetPos)
    {
        TacticalCoverPoint cover = TacticalCoverPoint.GetBestCover(
            transform.position, targetPos);

        if (cover == null) return targetPos; // nu exista cover-uri -> reactiv

        // Daca inca nu am ajuns la cover, mergem la cover; altfel, spre tinta.
        float distToCover = Vector3.Distance(transform.position, cover.transform.position);
        if (distToCover > 2.5f)
            return cover.transform.position;

        return targetPos;
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
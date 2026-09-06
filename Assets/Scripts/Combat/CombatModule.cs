using UnityEngine;

public class CombatModule : MonoBehaviour
{
    [Header("Type")]
    public bool isEnemy = false;
    public bool isSniper = false;

    [Header("Sniper Settings")]
    [Range(0f, 1f)]
    public float sniperHitChance = 0.3f;

    private HealthSystem healthSystem;
    private PerceptionModule perception;
    private AgentBehaviorTree behaviorTree;

    private Transform sniperPrivateTarget = null;

    void Awake()
    {
        healthSystem = GetComponent<HealthSystem>();
        perception = GetComponent<PerceptionModule>();
        behaviorTree = GetComponent<AgentBehaviorTree>();
    }

    void Update()
    {
        if (!TacticalBlackboard.IsRunning()) return;
        if (healthSystem.isDead) return;

        TacticalBlackboard bb = TacticalBlackboard.Instance;
        if (bb == null) return;

        bool combatActive = bb.combatState == CombatState.Combat || bb.phase2Active;
        if (!combatActive) return;

        FindAndAttackTarget();
    }

    void FindAndAttackTarget()
    {
        if (!healthSystem.CanAttack()) return;

        Transform target = GetBestTarget();
        if (target == null) return;

        if (!isSniper)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist > healthSystem.attackRange) return;
        }

        Attack(target);
    }

    Transform GetBestTarget()
    {
        if (isEnemy)
        {

            return GetNearestAliveAlly();
        }

        if (isSniper)
        {

            if (!IsTargetAlive(sniperPrivateTarget))
                sniperPrivateTarget = PickSniperTarget();

            return sniperPrivateTarget;
        }

        if (behaviorTree == null) return null;

        Transform combatTarget = behaviorTree.currentCombatTarget;
        if (!IsTargetAlive(combatTarget)) return null;

        return combatTarget;
    }

    Transform PickSniperTarget()
    {

        int enemyLayer = LayerMask.GetMask("Enemy", "SecondaryEnemy");
        Collider[] colliders = Physics.OverlapSphere(transform.position, 200f, enemyLayer);

        TacticalBlackboard bb = TacticalBlackboard.Instance;
        System.Collections.Generic.HashSet<Transform> takenByOtherSnipers =
            new System.Collections.Generic.HashSet<Transform>();

        if (bb != null)
        {
            foreach (AgentBehaviorTree a in bb.allAgents)
            {
                if (a == null || a == this.behaviorTree) continue;
                if (a.role != AgentRole.Sniper) continue;
                CombatModule otherCM = a.GetComponent<CombatModule>();
                if (otherCM == null) continue;
                if (otherCM.sniperPrivateTarget != null)
                    takenByOtherSnipers.Add(otherCM.sniperPrivateTarget);
            }
        }

        Transform fallback = null;
        Transform preferred = null;
        float minDistPreferred = Mathf.Infinity;
        float minDistFallback = Mathf.Infinity;

        foreach (Collider col in colliders)
        {
            HealthSystem hs = col.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            if (!HasLineOfSight(col.transform)) continue;

            float dist = Vector3.Distance(transform.position, col.transform.position);

            if (takenByOtherSnipers.Contains(col.transform))
            {

                if (dist < minDistFallback)
                {
                    minDistFallback = dist;
                    fallback = col.transform;
                }
            }
            else
            {

                if (dist < minDistPreferred)
                {
                    minDistPreferred = dist;
                    preferred = col.transform;
                }
            }
        }

        return preferred != null ? preferred : fallback;
    }

    public Transform GetSniperTarget()
    {
        return sniperPrivateTarget;
    }

    bool IsTargetAlive(Transform t)
    {
        if (t == null) return false;
        HealthSystem hs = t.GetComponent<HealthSystem>();
        return hs != null && !hs.isDead;
    }

    Transform GetNearestAliveAlly()
    {
        int layer = LayerMask.GetMask("Ally");
        Collider[] colliders = Physics.OverlapSphere(transform.position, 100f, layer);

        Transform nearest = null;
        float minDist = Mathf.Infinity;

        foreach (Collider col in colliders)
        {
            HealthSystem hs = col.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;

            float dist = Vector3.Distance(transform.position, col.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = col.transform;
            }
        }
        return nearest;
    }

    bool HasLineOfSight(Transform target)
    {
        Vector3 direction = (target.position - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, target.position);

        int obstacleLayer = LayerMask.GetMask("Obstacle");
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f,
            direction, distance, obstacleLayer))
            return false;

        return true;
    }

    void Attack(Transform target)
    {
        HealthSystem targetHS = target.GetComponent<HealthSystem>();
        if (targetHS == null) return;

        if (isSniper)
        {
            if (!SniperDecidesToFire(target, targetHS))
            {
                healthSystem.ResetAttackTimer();
                return;
            }
        }

        targetHS.TakeDamage(healthSystem.attackDamage);
        healthSystem.ResetAttackTimer();

        Debug.Log($"[Combat] {gameObject.name} a atacat {target.name} " +
            $"pentru {healthSystem.attackDamage} damage. " +
            $"HP ramas: {targetHS.currentHP}/{targetHS.maxHP}");
    }

    bool SniperDecidesToFire(Transform target, HealthSystem targetHS)
    {
        var cfg = ExperimentConfig.Instance;
        DecisionMode mode = cfg != null ? cfg.decisionMode : DecisionMode.FixedChance;

        switch (mode)
        {
            case DecisionMode.Heuristic:
                return HeuristicFireDecision(target, targetHS);

            case DecisionMode.ML_PPO:
                return MLFireDecision(target, targetHS);

            case DecisionMode.FixedChance:
            default:

                return Random.Range(0f, 1f) <= sniperHitChance;
        }
    }

    [Header("Heuristic Decision")]
    [Tooltip("Trage doar daca tinta e sub acest procent de HP (Heuristic).")]
    [Range(0f, 1f)] public float heuristicTargetHPThreshold = 0.85f;
    [Tooltip("Raza in jurul tintei in care un aliat e considerat 'in pericol' (Heuristic).")]
    public float heuristicAllyDangerRadius = 3f;

    bool HeuristicFireDecision(Transform target, HealthSystem targetHS)
    {

        if (!HasLineOfSight(target)) return false;

        float hpPct = targetHS.GetHPPercentage();
        if (hpPct > heuristicTargetHPThreshold) return false;

        if (AllyNearTarget(target)) return false;

        return true;
    }

    bool AllyNearTarget(Transform target)
    {
        int allyLayer = LayerMask.GetMask("Ally");
        Collider[] near = Physics.OverlapSphere(
            target.position, heuristicAllyDangerRadius, allyLayer);
        foreach (Collider c in near)
        {
            HealthSystem hs = c.GetComponent<HealthSystem>();
            if (hs != null && !hs.isDead) return true;
        }
        return false;
    }

    bool MLFireDecision(Transform target, HealthSystem targetHS)
    {
        SniperMLAgent ml = GetComponent<SniperMLAgent>();
        if (ml != null)
            return ml.DecideFire(target, targetHS, this);

        return HeuristicFireDecision(target, targetHS);
    }
}

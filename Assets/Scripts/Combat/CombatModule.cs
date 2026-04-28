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
    private AgentBehaviorTree behaviorTree; // pentru a sti currentCombatTarget

    // Folosit DOAR pentru sniperi: tinta proprie, persistenta pana moare
    private Transform sniperPrivateTarget = null;

    void Awake()
    {
        healthSystem = GetComponent<HealthSystem>();
        perception = GetComponent<PerceptionModule>();
        behaviorTree = GetComponent<AgentBehaviorTree>();
    }

    void Update()
    {
        if (healthSystem.isDead) return;

        // Tragerea porneste doar in Combat (sau in Faza 2, care implicit e combat)
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

        // Verifica raza de tragere
        // Sniperul nu are limita de raza; ceilalti folosesc attackRange
        if (!isSniper)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist > healthSystem.attackRange) return;
        }

        Attack(target);
    }

    // Decide pe cine atacam
    Transform GetBestTarget()
    {
        if (isEnemy)
        {
            // Inamicii ataca cel mai apropiat agent viu
            return GetNearestAliveAlly();
        }

        if (isSniper)
        {
            // Sniper: tine o tinta privata pana moare. Daca moare, alege alta.
            if (!IsTargetAlive(sniperPrivateTarget))
                sniperPrivateTarget = PickSniperTarget();

            return sniperPrivateTarget;
        }

        // Agent normal: ataca tinta setata de behavior tree (currentCombatTarget)
        if (behaviorTree == null) return null;

        Transform combatTarget = behaviorTree.currentCombatTarget;
        if (!IsTargetAlive(combatTarget)) return null;

        return combatTarget;
    }

    // Sniper alege o tinta noua. Prefera o tinta pe care alt sniper nu o are deja.
    Transform PickSniperTarget()
    {
        // Colecteaza inamicii vii cu line-of-sight
        int enemyLayer = LayerMask.GetMask("Enemy", "SecondaryEnemy");
        Collider[] colliders = Physics.OverlapSphere(transform.position, 200f, enemyLayer);

        // Vezi ce tinte au ceilalti sniperi
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
                // E luata de alt sniper -> doar fallback
                if (dist < minDistFallback)
                {
                    minDistFallback = dist;
                    fallback = col.transform;
                }
            }
            else
            {
                // Tinta libera -> preferata
                if (dist < minDistPreferred)
                {
                    minDistPreferred = dist;
                    preferred = col.transform;
                }
            }
        }

        return preferred != null ? preferred : fallback;
    }

    // Helper public folosit de AgentBehaviorTree pentru sniperi (rotatia capului)
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

        // Sniper: rata de succes configurabila
        if (isSniper)
        {
            float chance = Random.Range(0f, 1f);
            if (chance > sniperHitChance)
            {
                healthSystem.ResetAttackTimer();
                Debug.Log($"[Combat] {gameObject.name} (sniper) a tras in {target.name} dar a RATAT.");
                return;
            }
        }

        targetHS.TakeDamage(healthSystem.attackDamage);
        healthSystem.ResetAttackTimer();

        Debug.Log($"[Combat] {gameObject.name} a atacat {target.name} " +
            $"pentru {healthSystem.attackDamage} damage. " +
            $"HP ramas: {targetHS.currentHP}/{targetHS.maxHP}");
    }
}
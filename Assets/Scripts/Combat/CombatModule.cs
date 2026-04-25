using UnityEngine;
using System.Collections.Generic;

public class CombatModule : MonoBehaviour
{
    [Header("Type")]
    public bool isEnemy = false;
    public bool isSniper = false;

    private HealthSystem healthSystem;
    private PerceptionModule perception;
    private Transform currentTarget = null;

    void Awake()
    {
        healthSystem = GetComponent<HealthSystem>();
        perception = GetComponent<PerceptionModule>();
    }

    void Update()
    {
        if (healthSystem.isDead) return;

        // Nimeni nu trage pana combatState nu e Combat
        if (TacticalBlackboard.Instance == null ||
            TacticalBlackboard.Instance.combatState != CombatState.Combat) return;

        FindAndAttackTarget();
    }

    void FindAndAttackTarget()
    {
        if (!healthSystem.CanAttack()) return;

        Transform target = GetBestTarget();
        if (target == null) return;

        // Agentii normali ataca doar daca tinta e in viewRadius
        if (!isSniper && !isEnemy)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist > perception.viewRadius) return;
        }

        Attack(target);
    }

    Transform GetBestTarget()
    {
        if (isEnemy)
        {
            return GetNearestAlive("Ally");
        }
        else
        {
            if (isSniper)
                return GetNearestWithLOS();
            else
            {
                if (perception == null) return null;
                Transform enemy = perception.GetNearestEnemy();
                if (enemy == null) return null;

                // Verifica daca tinta e moarta
                HealthSystem hs = enemy.GetComponent<HealthSystem>();
                if (hs != null && hs.isDead) return null;

                return enemy;
            }
        }
    }

    Transform GetNearestAlive(string layerName)
    {
        int layer = LayerMask.GetMask(layerName);
        Collider[] colliders = Physics.OverlapSphere(
            transform.position, 100f, layer);

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

    Transform GetNearestWithLOS()
    {
        int layer = LayerMask.GetMask("Enemy");
        Collider[] colliders = Physics.OverlapSphere(
            transform.position, 100f, layer);

        Transform nearest = null;
        float minDist = Mathf.Infinity;

        foreach (Collider col in colliders)
        {
            HealthSystem hs = col.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;

            if (!HasLineOfSight(col.transform)) continue;

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

        // Sniper are 30% sansa de reusita
        if (isSniper)
        {
            float chance = Random.Range(0f, 1f);
            if (chance > 0.3f)
            {
                healthSystem.ResetAttackTimer();
                return; // ratat
            }
        }

        targetHS.TakeDamage(healthSystem.attackDamage);
        healthSystem.ResetAttackTimer();

        Debug.Log($"[Combat] {gameObject.name} a atacat {target.name} " +
            $"pentru {healthSystem.attackDamage} damage. " +
            $"HP ramas: {targetHS.currentHP}");
    }
}
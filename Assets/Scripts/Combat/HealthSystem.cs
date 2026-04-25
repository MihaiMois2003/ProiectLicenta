using UnityEngine;
using System;

public class HealthSystem : MonoBehaviour
{
    [Header("Stats")]
    public float maxHP = 100f;
    public float currentHP;
    public float attackDamage = 10f;
    public float attackRange = 10f;
    public float attackCooldown = 1f;

    [Header("State")]
    public bool isDead = false;

    private float attackTimer = 0f;

    public event Action<float> OnHPChanged;
    public event Action OnDeath;

    void Awake()
    {
        currentHP = maxHP;
    }

    void Update()
    {
        if (isDead) return;
        attackTimer += Time.deltaTime;
    }

    public bool CanAttack()
    {
        return !isDead && attackTimer >= attackCooldown;
    }

    public void ResetAttackTimer()
    {
        attackTimer = 0f;
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;

        currentHP -= damage;
        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
        OnHPChanged?.Invoke(currentHP);

        if (currentHP <= 0)
            Die();
    }

    void Die()
    {
        isDead = true;
        OnDeath?.Invoke();

        // Dezactiveaza componentele
        UnityEngine.AI.NavMeshAgent nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        CombatModule combat = GetComponent<CombatModule>();
        if (combat != null) combat.enabled = false;

        AgentBehaviorTree bt = GetComponent<AgentBehaviorTree>();
        if (bt != null) bt.enabled = false;

        EnemyController ec = GetComponent<EnemyController>();
        if (ec != null) ec.enabled = false;

        SecondaryEnemyController sec = GetComponent<SecondaryEnemyController>();
        if (sec != null) sec.enabled = false;

        // Pune pe jos
        transform.rotation = Quaternion.Euler(90f, transform.rotation.eulerAngles.y, 0f);

        Debug.Log($"[Combat] {gameObject.name} a murit!");
    }

    public float GetHPPercentage() => currentHP / maxHP;
}
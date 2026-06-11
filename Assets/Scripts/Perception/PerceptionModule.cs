using UnityEngine;
using System.Collections.Generic;

public class PerceptionModule : MonoBehaviour
{
    [Header("Field of View Settings")]
    public float viewRadius = 10f;
    [Range(0, 360)]
    public float viewAngle = 120f;

    [Header("Layer Masks")]
    public LayerMask enemyLayer;
    public LayerMask allyLayer;
    public LayerMask obstacleLayer;

    [Header("Perceived Objects (readonly)")]
    public List<Transform> visibleEnemies = new List<Transform>();
    public List<Transform> visibleAllies = new List<Transform>();

    // ── Memorie (folosit la PerceptionMode.FOV_Memory) ──
    private Transform rememberedEnemy = null;
    private Vector3 rememberedPosition;
    private float rememberedUntil = -1f;

    void Update()
    {
        if (!TacticalBlackboard.IsRunning()) return;
        FindVisibleTargets();
        UpdateMemory();
    }

    PerceptionMode Mode()
    {
        var cfg = ExperimentConfig.Instance;
        return cfg != null ? cfg.perceptionMode : PerceptionMode.FOV_LOS;
    }

    void FindVisibleTargets()
    {
        visibleEnemies.Clear();
        visibleAllies.Clear();

        PerceptionMode mode = Mode();

        if (mode == PerceptionMode.Omniscient)
        {
            // Vede toti inamicii vii din scena, fara raza / unghi / LOS.
            CollectAllLiveEnemies(visibleEnemies);
            CollectAllInLayer(allyLayer, visibleAllies, ignoreSelf: true);
            return;
        }

        // Celelalte moduri pornesc de la inamicii in raza.
        Collider[] enemiesInRadius = Physics.OverlapSphere(
            transform.position, viewRadius, enemyLayer);

        foreach (Collider enemy in enemiesInRadius)
        {
            Transform target = enemy.transform;
            if (PassesPerceptionFilters(target, mode))
                visibleEnemies.Add(target);
        }

        Collider[] alliesInRadius = Physics.OverlapSphere(
            transform.position, viewRadius, allyLayer);

        foreach (Collider ally in alliesInRadius)
        {
            Transform target = ally.transform;
            if (target == this.transform) continue;
            if (PassesPerceptionFilters(target, mode))
                visibleAllies.Add(target);
        }
    }

    // Aplica filtrele de unghi / LOS in functie de modul de perceptie.
    bool PassesPerceptionFilters(Transform target, PerceptionMode mode)
    {
        switch (mode)
        {
            case PerceptionMode.RadiusOnly:
                // 360 grade: doar LOS conteaza, fara unghi.
                return HasLineOfSight(target);

            case PerceptionMode.FOV_LOS:
            case PerceptionMode.FOV_Memory:
            default:
                return IsInFieldOfView(target) && HasLineOfSight(target);
        }
    }

    // Tine minte ultima pozitie a inamicului cel mai apropiat (doar FOV_Memory).
    void UpdateMemory()
    {
        if (Mode() != PerceptionMode.FOV_Memory) { rememberedUntil = -1f; return; }

        var cfg = ExperimentConfig.Instance;
        float dur = cfg != null ? cfg.memoryDuration : 4f;

        Transform seen = GetNearestEnemyRaw();
        if (seen != null)
        {
            rememberedEnemy = seen;
            rememberedPosition = seen.position;
            rememberedUntil = Time.time + dur;
        }
    }

    // Inamicul "stiut": vizibil acum, SAU memorat recent (FOV_Memory).
    public bool HasRememberedEnemy()
    {
        if (visibleEnemies.Count > 0) return true;
        return Mode() == PerceptionMode.FOV_Memory &&
               Time.time <= rememberedUntil && rememberedEnemy != null;
    }

    public Vector3 GetRememberedEnemyPosition()
    {
        Transform live = GetNearestEnemyRaw();
        if (live != null) return live.position;
        return rememberedPosition;
    }

    void CollectAllLiveEnemies(List<Transform> into)
    {
        // Mainul
        var bb = TacticalBlackboard.Instance;
        if (bb != null && bb.mainEnemy != null)
        {
            HealthSystem hs = bb.mainEnemy.GetComponent<HealthSystem>();
            if (hs == null || !hs.isDead) into.Add(bb.mainEnemy);
        }
        // Secundarii
        SecondaryEnemyController[] secs =
            Object.FindObjectsByType<SecondaryEnemyController>(FindObjectsSortMode.None);
        foreach (var s in secs)
        {
            HealthSystem hs = s.GetComponent<HealthSystem>();
            if (hs == null || !hs.isDead) into.Add(s.transform);
        }
    }

    void CollectAllInLayer(LayerMask layer, List<Transform> into, bool ignoreSelf)
    {
        Collider[] all = Physics.OverlapSphere(transform.position, 9999f, layer);
        foreach (Collider c in all)
        {
            if (ignoreSelf && c.transform == this.transform) continue;
            into.Add(c.transform);
        }
    }

    bool IsInFieldOfView(Transform target)
    {
        Vector3 directionToTarget = (target.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, directionToTarget);
        return angle < viewAngle / 2f;
    }

    bool HasLineOfSight(Transform target)
    {
        Vector3 directionToTarget = (target.position - transform.position).normalized;
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        if (Physics.Raycast(transform.position + Vector3.up * 0.5f,
            directionToTarget, distanceToTarget, obstacleLayer))
            return false;

        return true;
    }

    public Vector3 DirFromAngle(float angleInDegrees, bool angleIsGlobal)
    {
        if (!angleIsGlobal)
            angleInDegrees += transform.eulerAngles.y;

        return new Vector3(
            Mathf.Sin(angleInDegrees * Mathf.Deg2Rad),
            0,
            Mathf.Cos(angleInDegrees * Mathf.Deg2Rad));
    }

    public bool CanSeeEnemies() => visibleEnemies.Count > 0;
    public bool CanSeeAllies() => visibleAllies.Count > 0;

    // Cel mai apropiat inamic vizibil acum (fara memorie).
    Transform GetNearestEnemyRaw()
    {
        if (visibleEnemies.Count == 0) return null;
        Transform nearest = null;
        float minDist = Mathf.Infinity;
        foreach (Transform enemy in visibleEnemies)
        {
            float dist = Vector3.Distance(transform.position, enemy.position);
            if (dist < minDist) { minDist = dist; nearest = enemy; }
        }
        return nearest;
    }

    public Transform GetNearestEnemy() => GetNearestEnemyRaw();
}
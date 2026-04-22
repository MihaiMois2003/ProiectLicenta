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

    void Update()
    {
        FindVisibleTargets();
    }

    void FindVisibleTargets()
    {
        visibleEnemies.Clear();
        visibleAllies.Clear();

        // Gaseste toti inamicii in raza
        Collider[] enemiesInRadius = Physics.OverlapSphere(
            transform.position, viewRadius, enemyLayer);

        foreach (Collider enemy in enemiesInRadius)
        {
            Transform target = enemy.transform;
            if (IsInFieldOfView(target) && HasLineOfSight(target))
                visibleEnemies.Add(target);
        }

        // Gaseste toti aliatii in raza
        Collider[] alliesInRadius = Physics.OverlapSphere(
            transform.position, viewRadius, allyLayer);

        foreach (Collider ally in alliesInRadius)
        {
            Transform target = ally.transform;
            // Nu se "vede" pe sine
            if (target == this.transform) continue;
            if (IsInFieldOfView(target) && HasLineOfSight(target))
                visibleAllies.Add(target);
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

        // Daca exista un obstacol intre agent si tinta, nu are line of sight
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f,
            directionToTarget, distanceToTarget, obstacleLayer))
            return false;

        return true;
    }

    // Returneaza directia unui unghi dat (folosit pentru debug vizual)
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
    public Transform GetNearestEnemy()
    {
        if (visibleEnemies.Count == 0) return null;

        Transform nearest = null;
        float minDist = Mathf.Infinity;

        foreach (Transform enemy in visibleEnemies)
        {
            float dist = Vector3.Distance(transform.position, enemy.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = enemy;
            }
        }
        return nearest;
    }
}
using UnityEngine;

public class PerceptionDebugDrawer : MonoBehaviour
{
    private PerceptionModule perception;

    void Awake()
    {
        perception = GetComponent<PerceptionModule>();
    }

    // OnDrawGizmos se executa doar in Editor, nu afecteaza performanta
    void OnDrawGizmos()
    {
        if (perception == null)
            perception = GetComponent<PerceptionModule>();
        if (perception == null) return;

        // Cercul razei de perceptie
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, perception.viewRadius);

        // Liniile care delimiteaza unghiul de vedere
        Vector3 leftBoundary = perception.DirFromAngle(
            -perception.viewAngle / 2, false);
        Vector3 rightBoundary = perception.DirFromAngle(
            perception.viewAngle / 2, false);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position,
            transform.position + leftBoundary * perception.viewRadius);
        Gizmos.DrawLine(transform.position,
            transform.position + rightBoundary * perception.viewRadius);

        // Linii rosii spre inamicii vizibili
        Gizmos.color = Color.red;
        foreach (Transform enemy in perception.visibleEnemies)
            Gizmos.DrawLine(transform.position, enemy.position);

        // Linii verzi spre aliatii vizibili
        Gizmos.color = Color.green;
        foreach (Transform ally in perception.visibleAllies)
            Gizmos.DrawLine(transform.position, ally.position);
    }
}
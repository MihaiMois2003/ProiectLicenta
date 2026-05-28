using UnityEngine;
using UnityEngine.AI;

// Obstacol mobil care oscileaza intre doua puncte (ping-pong).
// Necesita un NavMeshObstacle cu Carving activat ca sa modifice
// pathfinding-ul in timp real. Layerul GameObject-ului ar trebui
// setat pe "Obstacle" ca sa blocheze line-of-sight in PerceptionModule/CombatModule.
[RequireComponent(typeof(NavMeshObstacle))]
public class MovingObstacle : MonoBehaviour
{
    [Header("Capete de traseu (in spatiul lumii)")]
    public Vector3 pointA;
    public Vector3 pointB;

    [Header("Miscare")]
    public float speed = 3f;
    [Tooltip("Daca e bifat, pointA/pointB se initializeaza relativ la pozitia de start.")]
    public bool autoInitFromStart = false;
    public Vector3 offsetA = new Vector3(-5, 0, 0);
    public Vector3 offsetB = new Vector3(5, 0, 0);

    private NavMeshObstacle navObstacle;
    private float t = 0f;
    private int dir = 1;

    void Awake()
    {
        navObstacle = GetComponent<NavMeshObstacle>();
        navObstacle.carving = true;

        if (autoInitFromStart)
        {
            pointA = transform.position + offsetA;
            pointB = transform.position + offsetB;
        }
    }

    void OnEnable()
    {
        // Plaseaza la capatul A cand reapare, ca sa fie predictibil intre rulari.
        transform.position = pointA;
        t = 0f;
        dir = 1;
    }

    void Update()
    {
        float dist = Vector3.Distance(pointA, pointB);
        if (dist < 0.01f) return;

        t += dir * (speed / dist) * Time.deltaTime;
        if (t >= 1f) { t = 1f; dir = -1; }
        else if (t <= 0f) { t = 0f; dir = 1; }

        transform.position = Vector3.Lerp(pointA, pointB, t);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(pointA, Vector3.one);
        Gizmos.DrawWireCube(pointB, Vector3.one);
        Gizmos.DrawLine(pointA, pointB);
    }
}
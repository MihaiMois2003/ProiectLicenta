using UnityEngine;
using UnityEngine.AI;

// Obstacol mobil care se misca prin scena. Are 2 moduri:
//  - Waypoints: parcurge in cerc o lista de puncte definite manual in inspector.
//  - RandomWander: alege puncte random pe NavMesh in jurul pozitiei de start.
//
// Cerinte pe GameObject:
//  - Layer = "Obstacle" (pentru blocarea line-of-sight)
//  - Componenta NavMeshObstacle cu Carve = true
//    (carving-ul actualizeaza NavMesh-ul in runtime ca agentii sa il ocoleasca)
//
// Important: NU adauga NavMeshAgent. Folosim doar NavMeshObstacle + miscare manuala
// prin transform.position. NavMeshAgent + NavMeshObstacle pe acelasi obiect
// se contrazic.
public class MovingObstacle : MonoBehaviour
{
    public enum MoveMode
    {
        Waypoints,
        RandomWander
    }

    [Header("Mode")]
    public MoveMode mode = MoveMode.Waypoints;

    [Header("Common Settings")]
    [Tooltip("Viteza de deplasare a obstacolului (unitati/sec).")]
    public float speed = 2f;

    [Tooltip("Distanta sub care consideram ca am ajuns la destinatie.")]
    public float arriveThreshold = 0.5f;

    [Tooltip("Cat asteapta la fiecare punct inainte sa plece spre urmatorul.")]
    public float waitTimeAtPoint = 1f;

    [Header("Waypoints Mode")]
    [Tooltip("Punctele prin care trece obstacolul (ciclu inchis: ultimul -> primul).")]
    public Transform[] waypoints;

    [Header("Random Wander Mode")]
    [Tooltip("Raza in jurul pozitiei initiale in care alege puncte random.")]
    public float wanderRadius = 10f;

    [Header("Debug")]
    public bool drawGizmos = true;

    private int currentWaypointIndex = 0;
    private Vector3 currentTarget;
    private bool hasTarget = false;
    private float waitTimer = 0f;
    private bool isWaiting = false;
    private Vector3 startPosition;

    // Forta NavMeshObstacle sa fie configurat corect (in caz ca cineva uita)
    private NavMeshObstacle navObstacle;

    void Awake()
    {
        startPosition = transform.position;
        navObstacle = GetComponent<NavMeshObstacle>();

        if (navObstacle == null)
        {
            Debug.LogWarning($"[MovingObstacle] {gameObject.name} nu are NavMeshObstacle! " +
                $"Agentii nu il vor ocoli corect. Adauga componenta NavMeshObstacle " +
                $"si bifeaza Carve.");
        }
        else if (!navObstacle.carving)
        {
            Debug.LogWarning($"[MovingObstacle] {gameObject.name} are NavMeshObstacle " +
                $"dar Carve = false. Bifeaza Carve in inspector.");
        }
    }

    void Start()
    {
        PickNewTarget();
    }

    void Update()
    {
        // Daca asteptam la un punct, doar numaram
        if (isWaiting)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= waitTimeAtPoint)
            {
                isWaiting = false;
                waitTimer = 0f;
                PickNewTarget();
            }
            return;
        }

        if (!hasTarget) return;

        // Misca-te catre target
        Vector3 toTarget = currentTarget - transform.position;
        toTarget.y = 0; // misca-te doar pe planul orizontal

        float distance = toTarget.magnitude;

        if (distance <= arriveThreshold)
        {
            // Am ajuns - asteapta apoi alege urmatorul punct
            isWaiting = true;
            return;
        }

        // Pas de miscare
        Vector3 direction = toTarget.normalized;
        Vector3 newPos = transform.position + direction * speed * Time.deltaTime;

        // Pastreaza Y-ul actual (nu cobori in pamant)
        newPos.y = transform.position.y;
        transform.position = newPos;
    }

    void PickNewTarget()
    {
        if (mode == MoveMode.Waypoints)
        {
            PickNextWaypoint();
        }
        else
        {
            PickRandomPoint();
        }
    }

    void PickNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            // Fallback: ramane pe loc
            hasTarget = false;
            return;
        }

        // Avanseaza in lista (in cerc)
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;

        Transform wp = waypoints[currentWaypointIndex];
        if (wp == null)
        {
            hasTarget = false;
            return;
        }

        currentTarget = wp.position;
        hasTarget = true;
    }

    void PickRandomPoint()
    {
        for (int i = 0; i < 20; i++)
        {
            // Punct random in jurul pozitiei de start
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = startPosition +
                new Vector3(randomCircle.x, 0, randomCircle.y);

            // Verifica ca punctul e pe NavMesh (deci accesibil)
            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
            {
                currentTarget = new Vector3(hit.position.x,
                    transform.position.y, hit.position.z);
                hasTarget = true;
                return;
            }
        }

        // Daca nu a gasit nimic valid, ramane pe loc o tura
        hasTarget = false;
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        if (mode == MoveMode.Waypoints && waypoints != null)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawWireSphere(waypoints[i].position, 0.5f);

                int next = (i + 1) % waypoints.Length;
                if (waypoints[next] != null)
                    Gizmos.DrawLine(waypoints[i].position, waypoints[next].position);
            }
        }
        else if (mode == MoveMode.RandomWander)
        {
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.3f);
            Vector3 center = Application.isPlaying ? startPosition : transform.position;
            Gizmos.DrawWireSphere(center, wanderRadius);
        }

        if (Application.isPlaying && hasTarget)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, currentTarget);
            Gizmos.DrawWireCube(currentTarget, Vector3.one * 0.5f);
        }
    }
}
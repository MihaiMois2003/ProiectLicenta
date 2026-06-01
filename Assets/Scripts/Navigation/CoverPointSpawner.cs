using UnityEngine;
using UnityEngine.AI;

// Genereaza automat puncte de acoperire in jurul obstacolelor la Start.
// Pune-l pe un GameObject gol in scena. Pentru fiecare obstacol din
// ObstacleManager, creeaza cateva cover points pe NavMesh in jurul lui.
public class CoverPointSpawner : MonoBehaviour
{
    [Tooltip("Cate cover points pe obstacol (distribuite pe cerc in jurul lui).")]
    public int pointsPerObstacle = 4;
    [Tooltip("Distanta cover-ului fata de centrul obstacolului.")]
    public float ringRadius = 3f;
    [Tooltip("Genereaza si in jurul obstacolelor mobile (pozitia lor initiala).")]
    public bool includeMobile = true;

    void Start()
    {
        ObstacleManager om = ObstacleManager.Instance;
        if (om == null)
        {
            Debug.LogWarning("[CoverPointSpawner] Nu exista ObstacleManager. Niciun cover generat.");
            return;
        }

        int created = 0;
        foreach (GameObject o in om.fixedObstacles)
            created += SpawnAround(o);

        if (includeMobile)
            foreach (GameObject o in om.mobileObstacles)
                created += SpawnAround(o);

        Debug.Log($"[CoverPointSpawner] {created} cover points generate.");
    }

    int SpawnAround(GameObject obstacle)
    {
        if (obstacle == null) return 0;
        Vector3 center = obstacle.transform.position;
        int made = 0;

        for (int i = 0; i < pointsPerObstacle; i++)
        {
            float angle = (360f / pointsPerObstacle) * i * Mathf.Deg2Rad;
            Vector3 candidate = center + new Vector3(
                Mathf.Sin(angle), 0, Mathf.Cos(angle)) * ringRadius;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
            {
                GameObject cp = new GameObject("CoverPoint_" + obstacle.name + "_" + i);
                cp.transform.position = hit.position;
                cp.AddComponent<TacticalCoverPoint>();
                made++;
            }
        }
        return made;
    }
}
using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// Agent ML pentru inamicul principal. Decide UNDE sa se miste pentru a supravietui.
// Daca acest component e atasat pe acelasi GameObject ca EnemyController,
// EnemyController.Flee() va consulta acest agent in loc sa aleaga puncte aleatorii.
//
// Observatii (8):
//  - own HP normalized
//  - distance to nearest agent (normalized)
//  - direction to nearest agent (Vector2 normalized: x, z)
//  - count of alive agents (normalized)
//  - distance to nearest obstacle (normalized)
//  - own normalized position (Vector2: x/mapHalfSize, z/mapHalfSize)
//
// Action (continuous, 2):
//  - moveX [-1, 1] - directie pe X
//  - moveZ [-1, 1] - directie pe Z
//  Inamicul se misca cu speed * action in fiecare frame
//
// Rewards:
//  - +0.01 per step in viata (supravietuire)
//  - +0.5 cand creste distanta minima fata de agenti
//  - -0.5 cand e atacat
//  - -1.0 la moarte
//  - -0.05 cand iese din harta (punisment pt boundary)
public class EnemyMLAgent : Agent
{
    [Header("ML Settings")]
    [Tooltip("Daca e true, EnemyController consulta acest agent pentru fugit.")]
    public bool isActive = true;

    [Tooltip("Jumatate din lungimea hartii (pentru normalizare). " +
             "Daca harta e 40x40, asta e 20.")]
    public float mapHalfSize = 20f;

    [Tooltip("Distanta maxima pentru normalizarea distantelor.")]
    public float maxDistanceForNormalization = 50f;

    // Output: directia ceruta de retea (citita de EnemyController)
    public Vector2 desiredMoveDirection { get; private set; }
    public bool decisionReady { get; private set; }

    private HealthSystem healthSystem;
    private float lastMinDistanceToAgent = -1f;
    private float lastHP = -1f;

    public override void Initialize()
    {
        healthSystem = GetComponent<HealthSystem>();
    }

    public override void OnEpisodeBegin()
    {
        decisionReady = false;
        lastMinDistanceToAgent = -1f;
        lastHP = healthSystem != null ? healthSystem.currentHP : 100f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Obs 1: HP propriu normalizat
        float hp = healthSystem != null ? healthSystem.GetHPPercentage() : 1f;
        sensor.AddObservation(hp);

        // Obs 2-4: cel mai apropiat agent (distanta + directie x,z)
        Transform nearest = FindNearestAgent();
        float distToNearest = 1f;
        Vector2 dirToNearest = Vector2.zero;
        if (nearest != null)
        {
            Vector3 diff = nearest.position - transform.position;
            float dist = diff.magnitude;
            distToNearest = Mathf.Clamp01(dist / maxDistanceForNormalization);
            if (dist > 0.001f)
            {
                Vector3 dirN = diff.normalized;
                dirToNearest = new Vector2(dirN.x, dirN.z);
            }
        }
        sensor.AddObservation(distToNearest);
        sensor.AddObservation(dirToNearest.x);
        sensor.AddObservation(dirToNearest.y);

        // Obs 5: numar agenti vii / total agenti
        sensor.AddObservation(ComputeAliveAgentsRatio());

        // Obs 6: distanta la cel mai apropiat obstacol
        sensor.AddObservation(ComputeDistanceToNearestObstacle());

        // Obs 7-8: pozitia proprie normalizata
        sensor.AddObservation(transform.position.x / mapHalfSize);
        sensor.AddObservation(transform.position.z / mapHalfSize);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        desiredMoveDirection = new Vector2(moveX, moveZ);
        decisionReady = true;

        // Reward shaping
        AddReward(0.01f); // supravietuire per step

        // Reward: distanta minima a crescut?
        Transform nearest = FindNearestAgent();
        if (nearest != null)
        {
            float curDist = Vector3.Distance(transform.position, nearest.position);
            if (lastMinDistanceToAgent > 0f)
            {
                float delta = curDist - lastMinDistanceToAgent;
                // delta > 0 = a crescut distanta, delta < 0 = s-a apropiat
                AddReward(delta * 0.05f);
            }
            lastMinDistanceToAgent = curDist;
        }

        // Reward: a fost atacat? (HP scazut)
        if (healthSystem != null)
        {
            float curHP = healthSystem.currentHP;
            if (lastHP > 0f && curHP < lastHP)
            {
                float damage = lastHP - curHP;
                AddReward(-damage * 0.01f); // -0.5 pentru 50 damage
            }
            lastHP = curHP;
        }

        // Penalizare daca e in afara hartii
        if (Mathf.Abs(transform.position.x) > mapHalfSize ||
            Mathf.Abs(transform.position.z) > mapHalfSize)
        {
            AddReward(-0.05f);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Pentru testare manuala - directie zero (nu se misca)
        // Daca ai nevoie de control manual, folosesti Input System nou
        var ca = actionsOut.ContinuousActions;
        ca[0] = 0f;
        ca[1] = 0f;
    }

    public void OnSelfDied()
    {
        AddReward(-1f);
        EndEpisode();
    }

    public void ConsumeDecision()
    {
        decisionReady = false;
    }

    // ── Helpers ────────────────────────────────

    Transform FindNearestAgent()
    {
        TacticalBlackboard bb = TacticalBlackboard.Instance;
        if (bb == null) return null;

        Transform nearest = null;
        float minDist = Mathf.Infinity;
        foreach (AgentBehaviorTree a in bb.allAgents)
        {
            if (a == null) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            float d = Vector3.Distance(transform.position, a.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = a.transform;
            }
        }
        return nearest;
    }

    float ComputeAliveAgentsRatio()
    {
        TacticalBlackboard bb = TacticalBlackboard.Instance;
        if (bb == null || bb.allAgents.Count == 0) return 0f;

        int total = bb.allAgents.Count;
        int alive = 0;
        foreach (AgentBehaviorTree a in bb.allAgents)
        {
            if (a == null) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            alive++;
        }
        return (float)alive / total;
    }

    float ComputeDistanceToNearestObstacle()
    {
        int obstacleLayer = LayerMask.GetMask("Obstacle");
        Collider[] obstacles = Physics.OverlapSphere(
            transform.position, maxDistanceForNormalization, obstacleLayer);

        if (obstacles.Length == 0) return 1f;

        float minDist = Mathf.Infinity;
        foreach (Collider c in obstacles)
        {
            float d = Vector3.Distance(transform.position, c.transform.position);
            if (d < minDist) minDist = d;
        }

        return Mathf.Clamp01(minDist / maxDistanceForNormalization);
    }
}
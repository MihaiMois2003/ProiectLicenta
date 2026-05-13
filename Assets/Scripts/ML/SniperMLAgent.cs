using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// Agent ML pentru un sniper. Decide CAND sa traga (nu unde se afla - sniperul e fix).
// Daca acest component e atasat pe acelasi GameObject ca CombatModule (cu isSniper=true),
// CombatModule va consulta acest agent in loc sa foloseasca sniperHitChance.
//
// Observatii (5):
//  - distance to target (normalized 0..1)
//  - has line of sight (0/1)
//  - target HP (normalized 0..1)
//  - own HP (normalized 0..1)
//  - allies in danger ratio (cati alii sunt sub 30% HP / total alii)
//
// Action (1 discrete branch, 2 actions):
//  - 0 = nu trage
//  - 1 = trage
//
// Rewards:
//  - +1.0 cand nimereste
//  - +2.0 cand tinta moare din lovitura sa
//  - -0.2 cand trage si rateaza
//  - -0.001 per step (presiune sa fie eficient)
//  - +0.3 cand un aliat in pericol e salvat
public class SniperMLAgent : Agent
{
    [Header("ML Settings")]
    [Tooltip("Distanta maxima pentru normalizare. Tinte mai departe = 1.0.")]
    public float maxDistanceForNormalization = 100f;

    [Tooltip("Daca e true, CombatModule consulta acest agent. Daca e false, fallback la sniperHitChance fix.")]
    public bool isActive = true;

    // Citite de CombatModule cand vrea sa decida daca traga
    public bool wantsToShoot { get; private set; }
    public bool decisionReady { get; private set; }

    // Setate de CombatModule inainte de RequestDecision
    private Transform currentTarget;
    private float lastTargetHP;
    private bool lastShotHit;
    private bool lastTargetDied;

    private HealthSystem healthSystem;
    private CombatModule combatModule;

    public override void Initialize()
    {
        healthSystem = GetComponent<HealthSystem>();
        combatModule = GetComponent<CombatModule>();
    }

    public override void OnEpisodeBegin()
    {
        // Resetarea scenei e gestionata de SniperTrainingEnvironment.
        // Aici doar resetam state-ul intern.
        wantsToShoot = false;
        decisionReady = false;
        lastShotHit = false;
        lastTargetDied = false;
    }

    // Apelat de CombatModule cu tinta curenta inainte sa cerem decizia
    public void SetTarget(Transform target)
    {
        currentTarget = target;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Observatie 1: distanta normalizata la tinta
        float distNorm = 1f;
        if (currentTarget != null)
        {
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            distNorm = Mathf.Clamp01(dist / maxDistanceForNormalization);
        }
        sensor.AddObservation(distNorm);

        // Observatie 2: line of sight (0/1)
        float los = 0f;
        if (currentTarget != null)
            los = HasLineOfSight(currentTarget) ? 1f : 0f;
        sensor.AddObservation(los);

        // Observatie 3: HP tinta normalizat
        float targetHP = 0f;
        if (currentTarget != null)
        {
            HealthSystem hs = currentTarget.GetComponent<HealthSystem>();
            if (hs != null) targetHP = hs.GetHPPercentage();
        }
        sensor.AddObservation(targetHP);

        // Observatie 4: HP propriu normalizat
        float ownHP = healthSystem != null ? healthSystem.GetHPPercentage() : 1f;
        sensor.AddObservation(ownHP);

        // Observatie 5: aliati in pericol (sub 30% HP)
        float alliesInDanger = ComputeAlliesInDangerRatio();
        sensor.AddObservation(alliesInDanger);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];
        wantsToShoot = (action == 1);
        decisionReady = true;

        // Penalizare mica per step ca sa nu astepte la infinit
        AddReward(-0.001f);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var actions = actionsOut.DiscreteActions;
        actions[0] = (Random.value < 0.3f) ? 1 : 0;
    }

    // Apelate de CombatModule dupa ce s-a tras
    public void OnShotHit(float damageDelivered, bool targetDied)
    {
        AddReward(1.0f);
        if (targetDied)
            AddReward(2.0f);
        lastShotHit = true;
        lastTargetDied = targetDied;
    }

    public void OnShotMissed()
    {
        AddReward(-0.2f);
        lastShotHit = false;
    }

    public void OnAllySaved()
    {
        AddReward(0.3f);
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

    bool HasLineOfSight(Transform target)
    {
        Vector3 dir = (target.position - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, target.position);
        int obstacleLayer = LayerMask.GetMask("Obstacle");
        return !Physics.Raycast(transform.position + Vector3.up * 0.5f,
            dir, dist, obstacleLayer);
    }

    float ComputeAlliesInDangerRatio()
    {
        TacticalBlackboard bb = TacticalBlackboard.Instance;
        if (bb == null || bb.allAgents.Count == 0) return 0f;

        int total = 0;
        int inDanger = 0;
        foreach (AgentBehaviorTree a in bb.allAgents)
        {
            if (a == null) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            total++;
            if (hs.GetHPPercentage() < 0.3f) inDanger++;
        }
        return total > 0 ? (float)inDanger / total : 0f;
    }
}
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

public class SniperMLAgent : Agent
{
    [Header("Observatii curente (readonly, debug)")]
    public float obsDistanceToTarget;
    public float obsLineOfSightClear;
    public float obsTargetHP;
    public float obsOwnHP;
    public float obsAlliesBelow30;

    public float maxObservedDistance = 50f;

    private Transform currentTarget;
    private HealthSystem currentTargetHS;

    private bool fireDecision = false;
    private bool decisionPending = false;

    [Header("Debug (pentru verificare)")]
    [Tooltip("Cand e bifat, afiseaza in Console fiecare decizie + observatiile care au dus la ea. " +
             "Dezactiveaza inainte de masuratori reale, ca sa nu umple Console-ul.")]
    public bool debugLogDecisions = false;

    private float targetHPBeforeShot = -1f;

    void Start()
    {

        var bp = GetComponent<BehaviorParameters>();
        if (bp == null)
        {
            Debug.LogWarning($"[ML-Sniper:{gameObject.name}] Lipseste Behavior Parameters! " +
                "Adauga-l pe acest GameObject pentru ca ML_PPO sa functioneze.");
            return;
        }

        bool hasModel = bp.Model != null;
        string status = bp.BehaviorType == BehaviorType.InferenceOnly && hasModel
            ? "MODEL ONNX ACTIV (reteaua antrenata decide)"
            : bp.BehaviorType == BehaviorType.HeuristicOnly || !hasModel
                ? "FALLBACK Heuristic() - NU foloseste reteaua! Verifica Behavior Type si Model."
                : $"mod {bp.BehaviorType}, model {(hasModel ? "prezent" : "LIPSA")}";

        Debug.Log($"[ML-Sniper:{gameObject.name}] Status la pornire: {status}");
    }

    public bool DecideFire(Transform target, HealthSystem targetHS, CombatModule cm)
    {
        currentTarget = target;
        currentTargetHS = targetHS;

        RequestDecision();

        return fireDecision;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        ComputeObservations();
        sensor.AddObservation(obsDistanceToTarget);
        sensor.AddObservation(obsLineOfSightClear);
        sensor.AddObservation(obsTargetHP);
        sensor.AddObservation(obsOwnHP);
        sensor.AddObservation(obsAlliesBelow30);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int act = actions.DiscreteActions[0];
        fireDecision = (act == 1);

        if (currentTarget == null)
        {
            fireDecision = false;
            return;
        }

        if (debugLogDecisions)
        {
            Debug.Log($"[ML-Sniper:{gameObject.name}] decizie={(fireDecision ? "TRAGE" : "asteapta")} " +
                $"| dist={obsDistanceToTarget:F2} LOS={obsLineOfSightClear:F0} " +
                $"targetHP={obsTargetHP:F2} ownHP={obsOwnHP:F2} aliatiJos={obsAlliesBelow30:F2}");
        }

        if (fireDecision)
        {
            if (obsLineOfSightClear < 0.5f)
            {

                AddReward(-0.2f);
            }
            else
            {

                AddReward(+0.5f);
                if (obsTargetHP < 0.4f) AddReward(+0.5f);
                if (obsAlliesBelow30 > 0.2f) AddReward(+0.3f);
            }
        }
        else
        {

            if (obsLineOfSightClear < 0.5f) AddReward(+0.05f);
            else AddReward(-0.05f);
        }

        AddReward(-0.001f);

        currentTarget = null;
        currentTargetHS = null;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        ComputeObservations();

        bool fire = obsLineOfSightClear > 0.5f &&
                    (obsTargetHP < 0.6f || obsAlliesBelow30 > 0.2f || obsDistanceToTarget < 0.5f);
        d[0] = fire ? 1 : 0;
    }

    void ComputeObservations()
    {
        if (currentTarget == null)
        {
            obsDistanceToTarget = 1f;
            obsLineOfSightClear = 0f;
            obsTargetHP = 1f;
            obsOwnHP = 1f;
            obsAlliesBelow30 = 0f;
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        obsDistanceToTarget = Mathf.Clamp01(dist / maxObservedDistance);
        obsLineOfSightClear = HasLineOfSight(currentTarget) ? 1f : 0f;
        obsTargetHP = currentTargetHS != null ? currentTargetHS.GetHPPercentage() : 1f;

        HealthSystem ownHS = GetComponent<HealthSystem>();
        obsOwnHP = ownHS != null ? ownHS.GetHPPercentage() : 1f;

        obsAlliesBelow30 = FractionAlliesBelow30();
    }

    float FractionAlliesBelow30()
    {
        var bb = TacticalBlackboard.Instance;
        if (bb == null) return 0f;
        int total = 0, low = 0;
        foreach (AgentBehaviorTree a in bb.allAgents)
        {
            if (a == null) continue;
            HealthSystem hs = a.GetComponent<HealthSystem>();
            if (hs == null || hs.isDead) continue;
            total++;
            if (hs.GetHPPercentage() < 0.3f) low++;
        }
        return total > 0 ? (float)low / total : 0f;
    }

    bool HasLineOfSight(Transform target)
    {
        Vector3 dir = (target.position - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, target.position);
        int obstacleLayer = LayerMask.GetMask("Obstacle");
        return !Physics.Raycast(transform.position + Vector3.up * 0.5f,
            dir, dist, obstacleLayer);
    }
}

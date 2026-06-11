using UnityEngine;

// SLOT pentru decizia de tragere prin ML (PPO / ONNX).
//
// Acum e un STUB: nu depinde de pachetul ML-Agents, ca sa compileze fara el.
// Cele 5 observatii din poster sunt deja calculate aici, ca sa fie gata cand
// conectezi modelul antrenat.
//
// CAND INTEGREZI ML-AGENTS REAL:
//  1. Instaleaza pachetul com.unity.ml-agents.
//  2. Schimba clasa sa mosteneasca Unity.MLAgents.Agent.
//  3. In CollectObservations adaugi cele 5 valori din ComputeObservations().
//  4. In OnActionReceived citesti actiunea discreta (0 = asteapta, 1 = trage)
//     si o salvezi in lastDecision.
//  5. Atasezi modelul ONNX antrenat in campul Behavior Parameters (Inference Only).
//
// Pana atunci, DecideFire foloseste o politica simpla bazata pe aceleasi observatii,
// ca sistemul sa fie functional si demonstrabil.
public class SniperMLAgent : MonoBehaviour
{
    [Header("Observatii curente (readonly, pentru debug)")]
    public float obsDistanceToTarget;   // normalizat 0..1
    public float obsLineOfSightClear;   // 0 sau 1
    public float obsTargetHP;           // normalizat 0..1
    public float obsOwnHP;              // normalizat 0..1
    public float obsAlliesBelow30;      // fractie aliati sub 30% HP

    [Header("Politica de rezerva (pana exista modelul ONNX)")]
    [Tooltip("Daca e bifat, foloseste o politica simpla bazata pe observatii. " +
             "Cand ai modelul ONNX, debifezi si conectezi reteaua.")]
    public bool useFallbackPolicy = true;

    public float maxObservedDistance = 50f;

    [HideInInspector] public bool lastDecision = false;

    // Apelat de CombatModule cand DecisionMode = ML_PPO.
    public bool DecideFire(Transform target, HealthSystem targetHS, CombatModule cm)
    {
        ComputeObservations(target, targetHS);

        // TODO: cand ai ML-Agents, aici citesti decizia retelei (lastDecision).
        if (useFallbackPolicy)
            lastDecision = FallbackPolicy();

        return lastDecision;
    }

    // Calculeaza cele 5 observatii din poster.
    void ComputeObservations(Transform target, HealthSystem targetHS)
    {
        // 1. Distanta normalizata
        float dist = Vector3.Distance(transform.position, target.position);
        obsDistanceToTarget = Mathf.Clamp01(dist / maxObservedDistance);

        // 2. Line of sight (1 daca clar)
        obsLineOfSightClear = HasLineOfSight(target) ? 1f : 0f;

        // 3. HP tinta normalizat
        obsTargetHP = targetHS != null ? targetHS.GetHPPercentage() : 1f;

        // 4. HP propriu normalizat
        HealthSystem ownHS = GetComponent<HealthSystem>();
        obsOwnHP = ownHS != null ? ownHS.GetHPPercentage() : 1f;

        // 5. Fractia de aliati sub 30% HP (cat de mult e echipa in pericol)
        obsAlliesBelow30 = FractionAlliesBelow30();
    }

    // Politica simpla de rezerva: trage cand are LOS si fie tinta e slabita,
    // fie echipa e in pericol (prioritizeaza aliatii amenintati).
    bool FallbackPolicy()
    {
        if (obsLineOfSightClear < 0.5f) return false;

        bool targetWeak = obsTargetHP < 0.6f;
        bool teamInDanger = obsAlliesBelow30 > 0.2f;
        bool targetClose = obsDistanceToTarget < 0.5f;

        // Trage daca tinta e slabita, SAU echipa e in pericol si are linie clara,
        // SAU tinta e suficient de aproape ca sa fie sigur.
        return targetWeak || teamInDanger || targetClose;
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
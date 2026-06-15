using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

// Agent ML real pentru decizia de tragere a sniperului (PPO via ML-Agents).
//
// Foloseste o singura actiune discreta cu 2 optiuni:
//    0 = nu trage (asteapta)
//    1 = trage
//
// Cele 5 observatii din poster:
//    1. distanta normalizata la tinta
//    2. line-of-sight clar (0/1)
//    3. HP tinta normalizat
//    4. HP propriu normalizat
//    5. fractia de aliati sub 30% HP
//
// FLUX:
//  - In ANTRENARE: ML-Agents cere o decizie, noi o aplicam si dam reward.
//  - In INFERENTA (joc normal cu .onnx): la fel, dar fara reward (e ignorat).
//  - CombatModule apeleaza DecideFire() cand sniperul vrea sa traga; noi
//    cerem o decizie de la creier (RequestDecision) si returnam ce a ales.
public class SniperMLAgent : Agent
{
    [Header("Observatii curente (readonly, debug)")]
    public float obsDistanceToTarget;
    public float obsLineOfSightClear;
    public float obsTargetHP;
    public float obsOwnHP;
    public float obsAlliesBelow30;

    public float maxObservedDistance = 50f;

    // Tinta curenta pentru care se ia decizia (setata de CombatModule).
    private Transform currentTarget;
    private HealthSystem currentTargetHS;

    // Rezultatul ultimei decizii a retelei (true = trage).
    private bool fireDecision = false;
    private bool decisionPending = false;

    // Pentru reward: retinem HP-ul tintei inainte de a trage.
    private float targetHPBeforeShot = -1f;

    // ── Apelat de CombatModule cand DecisionMode = ML_PPO ──
    // Returneaza decizia curenta a retelei pentru aceasta tinta.
    public bool DecideFire(Transform target, HealthSystem targetHS, CombatModule cm)
    {
        currentTarget = target;
        currentTargetHS = targetHS;

        // Cere o noua decizie de la creier (reteaua / heuristica de training).
        // RequestDecision -> CollectObservations -> OnActionReceived (sincron).
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
        int act = actions.DiscreteActions[0]; // 0 = asteapta, 1 = trage
        fireDecision = (act == 1);

        // ── REWARD SHAPING (folosit doar la antrenare) ──
        // Recompensam deciziile bune ca sa invete cand sa traga.
        if (fireDecision)
        {
            if (obsLineOfSightClear < 0.5f)
            {
                // A tras fara linie clara -> penalizare (irosire/risc).
                AddReward(-0.2f);
            }
            else
            {
                // A tras cu linie clara. Bonus mai mare daca tinta e slabita
                // (eliminari eficiente) si daca echipa e in pericol (prioritate).
                AddReward(+0.5f);
                if (obsTargetHP < 0.4f) AddReward(+0.5f);      // tinta aproape moarta
                if (obsAlliesBelow30 > 0.2f) AddReward(+0.3f); // salveaza aliati
            }
        }
        else
        {
            // A ales sa NU traga. Mic bonus daca a fost o alegere buna
            // (fara linie clara), mica penalizare daca a ratat o ocazie clara.
            if (obsLineOfSightClear < 0.5f) AddReward(+0.05f);
            else AddReward(-0.05f);
        }

        // Mica penalizare pe pas, ca sa nu invete sa stea degeaba la nesfarsit.
        AddReward(-0.001f);
    }

    // Heuristic = control manual / fallback cand NU exista model antrenat.
    // ML-Agents foloseste asta daca Behavior Type = Heuristic Only,
    // sau daca nu e atasat niciun model.
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
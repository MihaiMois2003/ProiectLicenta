using UnityEngine;

// Gestioneaza scena de antrenament pentru SniperMLAgent.
// Resetuieste sniperul si tinta la pozitii random la fiecare episod.
//
// Setup in scena de antrenament:
//  - 1 GameObject "Sniper" cu HealthSystem + CombatModule(isSniper=true) + SniperMLAgent
//  - 1-3 GameObject-uri "Target" cu HealthSystem (fara CombatModule, sau cu retaliere)
//  - 1 GameObject gol "Environment" cu acest script
//  - Optional: obstacole pe layer Obstacle pentru LOS
//
// Rezolva doua probleme:
//  - Reseteaza pozitiile la fiecare episod
//  - Daca tinta moare, spawneaza una noua si continua episodul (sau termina)
public class SniperTrainingEnvironment : MonoBehaviour
{
    [Header("Sniper")]
    public Transform sniper;
    public Vector3 sniperFixedPosition = new Vector3(-15, 1, -15);

    [Header("Targets")]
    public Transform[] targets;
    public float targetSpawnRadius = 15f;
    public Vector3 targetAreaCenter = new Vector3(5, 1, 5);

    [Header("Episode Settings")]
    public int maxStepsPerEpisode = 1000;
    private int currentStep = 0;

    private SniperMLAgent sniperAgent;
    private HealthSystem sniperHS;

    void Start()
    {
        if (sniper != null)
        {
            sniperAgent = sniper.GetComponent<SniperMLAgent>();
            sniperHS = sniper.GetComponent<HealthSystem>();
        }
        ResetEnvironment();
    }

    void FixedUpdate()
    {
        currentStep++;

        // Verifica end conditions
        bool allTargetsDead = AllTargetsDead();
        bool sniperDead = sniperHS != null && sniperHS.isDead;
        bool timeout = currentStep >= maxStepsPerEpisode;

        if (allTargetsDead || sniperDead || timeout)
        {
            if (sniperAgent != null)
            {
                if (sniperDead) sniperAgent.OnSelfDied();
                else sniperAgent.EndEpisode();
            }
            ResetEnvironment();
        }
    }

    public void ResetEnvironment()
    {
        currentStep = 0;

        // Reset sniper
        if (sniper != null)
        {
            sniper.position = sniperFixedPosition;
            sniper.rotation = Quaternion.identity;
            if (sniperHS != null)
            {
                sniperHS.currentHP = sniperHS.maxHP;
                sniperHS.isDead = false;
            }
        }

        // Reset targets la pozitii random
        if (targets != null)
        {
            foreach (Transform t in targets)
            {
                if (t == null) continue;
                Vector2 randomCircle = Random.insideUnitCircle * targetSpawnRadius;
                Vector3 newPos = targetAreaCenter +
                    new Vector3(randomCircle.x, 0, randomCircle.y);
                t.position = newPos;

                HealthSystem hs = t.GetComponent<HealthSystem>();
                if (hs != null)
                {
                    hs.currentHP = hs.maxHP;
                    hs.isDead = false;
                }

                // Reset rotation/euler in caz ca a "murit" si s-a culcat
                t.rotation = Quaternion.identity;
            }
        }
    }

    bool AllTargetsDead()
    {
        if (targets == null || targets.Length == 0) return false;
        foreach (Transform t in targets)
        {
            if (t == null) continue;
            HealthSystem hs = t.GetComponent<HealthSystem>();
            if (hs == null || !hs.isDead) return false;
        }
        return true;
    }
}
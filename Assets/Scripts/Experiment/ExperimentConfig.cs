using UnityEngine;

public enum PerceptionMode
{
    FOV_LOS,
    Omniscient,
    RadiusOnly,
    FOV_Memory
}

public enum CommunicationMode
{
    Blackboard,
    LocalBroadcast,
    Relay
}

public enum CollaborationMode
{
    RandomRoundRobin,
    NearestEnemy,
    FocusFire,
    Auction
}

public enum PlanningMode
{
    Reactive,
    Flanking,
    CoverPoints
}

public enum DecisionMode
{
    FixedChance,
    Heuristic,
    ML_PPO
}

public class ExperimentConfig : MonoBehaviour
{
    public static ExperimentConfig Instance;

    [Header("=== TEHNICI COMUTABILE ===")]
    public PerceptionMode perceptionMode = PerceptionMode.FOV_LOS;
    public CommunicationMode communicationMode = CommunicationMode.Blackboard;
    public CollaborationMode collaborationMode = CollaborationMode.RandomRoundRobin;
    public PlanningMode planningMode = PlanningMode.Reactive;
    public DecisionMode decisionMode = DecisionMode.FixedChance;

    [Header("=== Perceptie ===")]
    [Tooltip("Cat timp (sec) tine minte ultima pozitie in modul FOV_Memory.")]
    public float memoryDuration = 4f;

    [Header("=== Comunicare ===")]
    [Tooltip("Raza in care un agent isi anunta vecinii (LocalBroadcast / Relay).")]
    public float commRange = 12f;

    [Header("=== Planificare ===")]
    [Tooltip("Raza arcului de incercuire fata de inamic (Flanking).")]
    public float flankRadius = 6f;

    [Header("=== Optiuni extra ===")]
    [Tooltip("Agentii sub pragul de HP cer ajutor; cel mai apropiat aliat vine.")]
    public bool helpRequestEnabled = false;
    [Range(0f, 1f)] public float helpRequestThreshold = 0.3f;

    [Tooltip("Support-ii regenereaza HP-ul aliatilor din jur in timp.")]
    public bool supportRegenEnabled = false;
    public float supportRegenPerSecond = 2f;

    [Header("=== Reproductibilitate ===")]
    [Tooltip("Seed pentru random. Aceeasi valoare => aceeasi rulare. 0 = aleator.")]
    public int randomSeed = 12345;
    [Tooltip("Dupa fiecare rulare logata in CSV, seed-ul creste automat cu 1 " +
             "(pregatit pentru urmatoarea repetare). Ignorat daca seed = 0.")]
    public bool autoIncrementSeed = true;

    static bool hasSaved = false;
    static PerceptionMode sPerception;
    static CommunicationMode sComm;
    static CollaborationMode sCollab;
    static PlanningMode sPlanning;
    static DecisionMode sDecision;
    static float sMemory, sCommRange, sFlank, sHelpThr, sRegen;
    static bool sHelp, sSupportRegen, sAutoInc;
    static int sSeed;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }

        if (hasSaved) RestoreSaved();

        ApplySeed();
    }

    public void ApplySeed()
    {
        if (randomSeed != 0)
            Random.InitState(randomSeed);
    }

    public void SaveForReload()
    {
        hasSaved = true;
        sPerception = perceptionMode;
        sComm = communicationMode;
        sCollab = collaborationMode;
        sPlanning = planningMode;
        sDecision = decisionMode;
        sMemory = memoryDuration;
        sCommRange = commRange;
        sFlank = flankRadius;
        sHelp = helpRequestEnabled;
        sHelpThr = helpRequestThreshold;
        sSupportRegen = supportRegenEnabled;
        sRegen = supportRegenPerSecond;
        sSeed = randomSeed;
        sAutoInc = autoIncrementSeed;
    }

    void RestoreSaved()
    {
        perceptionMode = sPerception;
        communicationMode = sComm;
        collaborationMode = sCollab;
        planningMode = sPlanning;
        decisionMode = sDecision;
        memoryDuration = sMemory;
        commRange = sCommRange;
        flankRadius = sFlank;
        helpRequestEnabled = sHelp;
        helpRequestThreshold = sHelpThr;
        supportRegenEnabled = sSupportRegen;
        supportRegenPerSecond = sRegen;
        randomSeed = sSeed;
        autoIncrementSeed = sAutoInc;
    }
}

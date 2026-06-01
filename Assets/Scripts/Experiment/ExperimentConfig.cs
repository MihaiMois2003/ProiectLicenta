using UnityEngine;

// ─────────────────────────────────────────────────────────
//  ENUM-URI PENTRU CELE 5 AXE DE TEHNICI
// ─────────────────────────────────────────────────────────

// PERCEPTIE — cum detecteaza agentii inamicii.
public enum PerceptionMode
{
    FOV_LOS,     // con de vedere (unghi) + line-of-sight (comportamentul original)
    Omniscient,  // vede tot, fara unghi sau LOS (baseline ideal)
    RadiusOnly,  // 360 grade dar limitat de raza, fara restrictie de unghi
    FOV_Memory   // FOV+LOS, dar tine minte ultima pozitie cateva secunde dupa contact
}

// COMUNICARE — cum circula informatia despre inamic intre agenti.
public enum CommunicationMode
{
    Blackboard,      // cunoastere globala instant
    LocalBroadcast,  // doar agentii in commRange afla
    Relay            // cine aude retransmite mai departe (propagare in valuri)
}

// COLABORARE — cum se asigneaza tintele grupurilor in Faza 2.
public enum CollaborationMode
{
    RandomRoundRobin, // shuffle + ciclic (original)
    NearestEnemy,     // fiecare grup ia inamicul cel mai apropiat
    FocusFire,        // toate grupurile pe acelasi inamic pana moare
    Auction           // licitatie: grupul cel mai bine pozitionat ia tinta
}

// PLANIFICARE — cum decid grupurile traiectoria spre inamic.
public enum PlanningMode
{
    Reactive,    // merg direct spre tinta (original)
    Flanking,    // se pozitioneaza pe arc in jurul tintei (incercuire)
    CoverPoints  // folosesc puncte tactice / de acoperire de pe harta
}

// DECIZIE — logica de tragere a sniperului.
public enum DecisionMode
{
    FixedChance, // sniperHitChance fix (original)
    Heuristic,   // reguli: LOS clar + tinta slabita + aliat nu e in pericol
    ML_PPO       // decizie luata de reteaua ML-Agents (slot pregatit pentru viitor)
}

// ─────────────────────────────────────────────────────────
//  CONFIGURARE CENTRALA
// ─────────────────────────────────────────────────────────
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

    // ── Persistenta peste reload (resetul rularii) ──
    static bool hasSaved = false;
    static PerceptionMode sPerception;
    static CommunicationMode sComm;
    static CollaborationMode sCollab;
    static PlanningMode sPlanning;
    static DecisionMode sDecision;
    static float sMemory, sCommRange, sFlank, sHelpThr, sRegen;
    static bool sHelp, sSupportRegen;
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

    // Salveaza alegerile curente in campuri statice, ca sa supravietuiasca LoadScene.
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
    }
}
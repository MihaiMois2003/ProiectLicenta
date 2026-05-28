using UnityEngine;

// Modurile de comunicare intre agenti.
// Blackboard      = cunoastere globala instant (toti afla simultan).
// LocalBroadcast  = doar agentii aflati in commRange fata de raportor afla.
public enum CommunicationMode
{
    Blackboard,
    LocalBroadcast
}

// Modurile de colaborare la asignarea tintelor in Faza 2.
// RandomRoundRobin = shuffle + groupIndex % liveEnemies (comportamentul original).
// NearestEnemy     = fiecare grup ia inamicul cel mai apropiat de centrul lui.
// FocusFire        = toate grupurile pe acelasi inamic pana moare, apoi urmatorul.
public enum CollaborationMode
{
    RandomRoundRobin,
    NearestEnemy,
    FocusFire
}

// Configurare centrala a experimentului. Singurul loc de adevar pentru
// tehnicile comutabile + parametrii lor. Atasata pe acelasi GameObject
// ca TacticalBlackboard (sau acceseaza prin ExperimentConfig.Instance).
public class ExperimentConfig : MonoBehaviour
{
    public static ExperimentConfig Instance;

    [Header("Tehnici comutabile")]
    [Tooltip("Cum se propaga informatia despre inamic intre agenti.")]
    public CommunicationMode communicationMode = CommunicationMode.Blackboard;

    [Tooltip("Cum se asigneaza tintele grupurilor in Faza 2.")]
    public CollaborationMode collaborationMode = CollaborationMode.RandomRoundRobin;

    [Header("Comunicare locala")]
    [Tooltip("Raza in care un agent isi anunta vecinii (doar pentru LocalBroadcast).")]
    public float commRange = 12f;

    // Persista alegerile peste un reload de scena (resetul rularii).
    static bool hasSaved = false;
    static CommunicationMode savedComm;
    static CollaborationMode savedCollab;
    static float savedRange;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }

        if (hasSaved)
        {
            communicationMode = savedComm;
            collaborationMode = savedCollab;
            commRange = savedRange;
        }
    }

    // Apelat de UI inainte de reload, ca alegerile sa supravietuiasca.
    public void SaveForReload()
    {
        hasSaved = true;
        savedComm = communicationMode;
        savedCollab = collaborationMode;
        savedRange = commRange;
    }
}
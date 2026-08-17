using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ruleaza automat un studiu de tip "ablation": o configuratie de baza (baseline),
// apoi variaza CATE O AXA pe rand, x2 (cu/fara obstacole), x5 repetari (seed 100-104).
// Total: 130 rulari, complet nesupravegheat - porneste, reincarca scena, aplica
// urmatoarea combinatie, apasa Start singur, asteapta finalul, salveaza in CSV
// (deja automat prin MetricsCollector -> ExperimentLogger), repeta.
public class AutoStudyRunner : MonoBehaviour
{
    public static AutoStudyRunner Instance;

    [Header("Studiu")]
    [Tooltip("Pauza (secunde) dupa finalul unei rulari, inainte de a trece la urmatoarea " +
             "(doar cosmetic, ca sa apuci sa vezi rezultatul daca te uiti).")]
    public float pauseBetweenRuns = 1f;

    // ── Persistenta REALA, peste opriri/reporniri complete de Unity (nu doar reload de scena) ──
    // Folosim PlayerPrefs in loc de campuri statice, ca planIndex sa supravietuiasca
    // si atunci cand opresti Play-ul si il pornesti mai tarziu, ca sa poti relua
    // studiul exact de unde ai ramas, fara sa dubleze randuri in CSV.
    const string PrefActive = "AutoStudy_Active";
    const string PrefIndex = "AutoStudy_PlanIndex";

    static bool studyActive
    {
        get => PlayerPrefs.GetInt(PrefActive, 0) == 1;
        set => PlayerPrefs.SetInt(PrefActive, value ? 1 : 0);
    }
    static int planIndex
    {
        get => PlayerPrefs.GetInt(PrefIndex, 0);
        set => PlayerPrefs.SetInt(PrefIndex, value);
    }

    bool waitingForNext = false;
    List<PlanItem> plan;

    public struct PlanItem
    {
        public PerceptionMode perception;
        public CommunicationMode communication;
        public CollaborationMode collaboration;
        public PlanningMode planning;
        public DecisionMode decision;
        public bool obstaclesFixedOn;
        public bool obstaclesMobileOn;
        public int seed;
        public string label;
    }

    // Apelat din UI la click pe "START STUDIU". Porneste MEREU curat: sterge
    // CSV-ul vechi si reseteaza indexul, ca sa nu amesteci niciodata date
    // colectate sub versiuni diferite de cod (ex: inainte/dupa un bugfix).
    public static void BeginStudy()
    {
        ExperimentLogger.ClearFile();
        studyActive = true;
        planIndex = 0;
        PlayerPrefs.Save();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public static void StopStudy()
    {
        studyActive = false;
        PlayerPrefs.Save();
    }

    public static bool IsActive => studyActive;
    public static int CurrentIndex => planIndex;

    void Awake()
    {
        Instance = this;
        plan = BuildPlan();
    }

    void Start()
    {
        if (studyActive)
            StartCoroutine(ApplyCurrentAndLaunch());
    }

    void Update()
    {
        if (!studyActive || waitingForNext) return;

        var m = MetricsCollector.Instance;
        if (m != null && m.finished)
        {
            waitingForNext = true;
            StartCoroutine(AdvanceToNext());
        }
    }

    // Mica pauza dupa Awake, ca sa fim siguri ca ExperimentConfig/ObstacleManager/
    // TacticalBlackboard si-au terminat Awake/Start inainte sa suprascriem valorile.
    IEnumerator ApplyCurrentAndLaunch()
    {
        yield return null; // o frame e suficient, toate Awake-urile deja au rulat

        if (planIndex >= plan.Count)
        {
            studyActive = false;
            Debug.Log("[AutoStudy] Index invalid la pornire, opresc studiul.");
            yield break;
        }

        ApplyPlanItem(plan[planIndex]);

        var bb = TacticalBlackboard.Instance;
        if (bb != null) bb.simulationStarted = true; // apasa "Start" automat

        Debug.Log($"[AutoStudy] Rulare {planIndex + 1}/{plan.Count}: {plan[planIndex].label}");
    }

    IEnumerator AdvanceToNext()
    {
        yield return new WaitForSeconds(pauseBetweenRuns);

        planIndex++;
        PlayerPrefs.Save(); // scrie pe disc INAINTE de reload, ca sa nu se piarda daca inchizi Unity

        if (planIndex >= plan.Count)
        {
            studyActive = false;
            PlayerPrefs.Save();
            Debug.Log("[AutoStudy] STUDIU COMPLET - toate cele " + plan.Count + " rulari s-au terminat. " +
                "Vezi rezultatele in CSV: " + ExperimentLogger.GetFilePath());
            yield break;
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void ApplyPlanItem(PlanItem item)
    {
        var cfg = ExperimentConfig.Instance;
        var obs = ObstacleManager.Instance;

        if (cfg != null)
        {
            cfg.perceptionMode = item.perception;
            cfg.communicationMode = item.communication;
            cfg.collaborationMode = item.collaboration;
            cfg.planningMode = item.planning;
            cfg.decisionMode = item.decision;
            cfg.randomSeed = item.seed;
            cfg.autoIncrementSeed = false; // studiul controleaza explicit seed-ul, nu vrem dublu-increment
            cfg.ApplySeed();
        }

        if (obs != null)
        {
            obs.SetFixedActive(item.obstaclesFixedOn);
            obs.SetMobileActive(item.obstaclesMobileOn);
        }
    }

    // ── Generarea planului: baseline + o axa variata pe rand, x4 stari de obstacole, x5 seeduri ──
    static List<PlanItem> BuildPlan()
    {
        var list = new List<PlanItem>();

        // 4 stari: fara obstacole, doar fixe, doar mobile, toate.
        var obstacleStates = new (bool fixedOn, bool mobileOn, string tag)[]
        {
            (false, false, "obs=NONE"),
            (true,  false, "obs=FIXED"),
            (false, true,  "obs=MOBILE"),
            (true,  true,  "obs=BOTH"),
        };

        foreach (var st in obstacleStates)
        {
            // Baseline
            AddCombo(list, Baseline(), st.fixedOn, st.mobileOn, "baseline, " + st.tag);

            // Perceptie (sarim indexul 0 = baseline, deja acoperit)
            foreach (PerceptionMode v in System.Enum.GetValues(typeof(PerceptionMode)))
                if (v != PerceptionMode.FOV_LOS)
                {
                    var c = Baseline(); c.perception = v;
                    AddCombo(list, c, st.fixedOn, st.mobileOn, "perception=" + v + ", " + st.tag);
                }

            // Comunicare
            foreach (CommunicationMode v in System.Enum.GetValues(typeof(CommunicationMode)))
                if (v != CommunicationMode.Blackboard)
                {
                    var c = Baseline(); c.communication = v;
                    AddCombo(list, c, st.fixedOn, st.mobileOn, "communication=" + v + ", " + st.tag);
                }

            // Colaborare
            foreach (CollaborationMode v in System.Enum.GetValues(typeof(CollaborationMode)))
                if (v != CollaborationMode.RandomRoundRobin)
                {
                    var c = Baseline(); c.collaboration = v;
                    AddCombo(list, c, st.fixedOn, st.mobileOn, "collaboration=" + v + ", " + st.tag);
                }

            // Planificare
            foreach (PlanningMode v in System.Enum.GetValues(typeof(PlanningMode)))
                if (v != PlanningMode.Reactive)
                {
                    var c = Baseline(); c.planning = v;
                    AddCombo(list, c, st.fixedOn, st.mobileOn, "planning=" + v + ", " + st.tag);
                }

            // Decizie
            foreach (DecisionMode v in System.Enum.GetValues(typeof(DecisionMode)))
                if (v != DecisionMode.FixedChance)
                {
                    var c = Baseline(); c.decision = v;
                    AddCombo(list, c, st.fixedOn, st.mobileOn, "decision=" + v + ", " + st.tag);
                }
        }

        return list;
    }

    static PlanItem Baseline()
    {
        return new PlanItem
        {
            perception = PerceptionMode.FOV_LOS,
            communication = CommunicationMode.Blackboard,
            collaboration = CollaborationMode.RandomRoundRobin,
            planning = PlanningMode.Reactive,
            decision = DecisionMode.FixedChance,
        };
    }

    // Adauga 5 repetari (seed 100-104) ale aceleiasi combinatii in plan.
    static void AddCombo(List<PlanItem> list, PlanItem template, bool fixedOn, bool mobileOn, string label)
    {
        for (int rep = 0; rep < 5; rep++)
        {
            var item = template;
            item.obstaclesFixedOn = fixedOn;
            item.obstaclesMobileOn = mobileOn;
            item.seed = 100 + rep;
            item.label = label + $" (seed {item.seed})";
            list.Add(item);
        }
    }

    // Pentru UI: informatii despre pasul curent.
    public string CurrentLabel()
    {
        if (plan == null) plan = BuildPlan();
        if (planIndex < 0 || planIndex >= plan.Count) return "-";
        return plan[planIndex].label;
    }

    public int TotalCount()
    {
        if (plan == null) plan = BuildPlan();
        return plan.Count;
    }
}
using UnityEngine;
using UnityEngine.SceneManagement;

public class ExperimentUI : MonoBehaviour
{
    [Header("Layout")]
    public int panelWidth = 300;

    public static int ReservedPixelWidth = 320;
    public int fontSize = 13;

    GUIStyle panelBg, sectionBg, label, valLabel, btn, dropItem, header;
    Texture2D texPanel, texSection, texBtn, texBtnSel;
    bool stylesReady = false;

    Vector2 scroll = Vector2.zero;
    string openDropdown = null;
    TacticalBlackboard bb_ref;

    Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    void BuildStyles()
    {
        texPanel = Tex(new Color(0.11f, 0.11f, 0.12f, 0.97f));
        texSection = Tex(new Color(0.16f, 0.14f, 0.08f, 1f));
        texBtn = Tex(new Color(0.20f, 0.20f, 0.22f, 1f));
        texBtnSel = Tex(new Color(0.33f, 0.33f, 0.36f, 1f));

        panelBg = new GUIStyle();
        panelBg.normal.background = texPanel;
        panelBg.padding = new RectOffset(10, 10, 10, 10);

        sectionBg = new GUIStyle();
        sectionBg.normal.background = texSection;
        sectionBg.padding = new RectOffset(8, 8, 6, 8);
        sectionBg.margin = new RectOffset(0, 0, 0, 4);

        header = new GUIStyle();
        header.fontSize = fontSize;
        header.fontStyle = FontStyle.Bold;
        header.normal.textColor = new Color(0.6f, 0.6f, 0.65f);

        label = new GUIStyle();
        label.fontSize = fontSize - 1;
        label.normal.textColor = new Color(0.65f, 0.65f, 0.68f);
        label.wordWrap = true;

        valLabel = new GUIStyle();
        valLabel.fontSize = fontSize;
        valLabel.normal.textColor = Color.white;
        valLabel.wordWrap = true;

        btn = new GUIStyle(GUI.skin.button);
        btn.fontSize = fontSize;
        btn.normal.textColor = Color.white;
        btn.normal.background = texBtn;
        btn.hover.background = texBtnSel;
        btn.alignment = TextAnchor.MiddleLeft;
        btn.padding = new RectOffset(8, 8, 5, 5);

        dropItem = new GUIStyle(btn);

        stylesReady = true;
    }

    void Awake()
    {

        ReservedPixelWidth = panelWidth + 20 + 16;
    }

    void OnGUI()
    {
        if (!stylesReady) BuildStyles();
        if (bb_ref == null) bb_ref = TacticalBlackboard.Instance;

        float w = panelWidth + 20;
        GUILayout.BeginArea(new Rect(8, 8, w, Screen.height - 16), panelBg);
        scroll = GUILayout.BeginScrollView(scroll);

        var cfg = ExperimentConfig.Instance;
        var m = MetricsCollector.Instance;
        var obs = ObstacleManager.Instance;

        if (bb_ref != null && !bb_ref.simulationStarted)
        {
            if (GUILayout.Button("START", btn, GUILayout.Height(32)))
                bb_ref.simulationStarted = true;
            GUILayout.Label("Choose the techniques, then press START.", label);
        }
        else
        {
            if (GUILayout.Button("RESET RUN", btn, GUILayout.Height(28)))
            {
                if (cfg != null) cfg.SaveForReload();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        GUILayout.Space(8);

        if (AutoStudyRunner.IsActive)
        {
            if (GUILayout.Button("STOP STUDY", btn, GUILayout.Height(30)))
                AutoStudyRunner.StopStudy();
        }
        else
        {
            if (GUILayout.Button("START STUDY", btn, GUILayout.Height(30)))
                AutoStudyRunner.BeginStudy();
        }

        GUILayout.Space(8);
        GUILayout.Label("TECHNIQUES", header);

        if (cfg != null)
        {
            cfg.perceptionMode = (PerceptionMode)Dropdown("Perception", "perc",
                (int)cfg.perceptionMode, System.Enum.GetNames(typeof(PerceptionMode)));

            cfg.communicationMode = (CommunicationMode)Dropdown("Communication", "comm",
                (int)cfg.communicationMode, System.Enum.GetNames(typeof(CommunicationMode)));
            if (cfg.communicationMode == CommunicationMode.LocalBroadcast ||
                cfg.communicationMode == CommunicationMode.Relay)
                GUILayout.Label("  commRange: " + cfg.commRange.ToString("F1"), label);

            cfg.collaborationMode = (CollaborationMode)Dropdown("Collaboration", "collab",
                (int)cfg.collaborationMode, System.Enum.GetNames(typeof(CollaborationMode)));

            cfg.planningMode = (PlanningMode)Dropdown("Planning", "plan",
                (int)cfg.planningMode, System.Enum.GetNames(typeof(PlanningMode)));

            cfg.decisionMode = (DecisionMode)Dropdown("Sniper decision", "dec",
                (int)cfg.decisionMode, System.Enum.GetNames(typeof(DecisionMode)));

            GUILayout.Space(6);
            cfg.helpRequestEnabled = ToggleRow("Help-request", cfg.helpRequestEnabled);
            cfg.supportRegenEnabled = ToggleRow("Support regen", cfg.supportRegenEnabled);

            GUILayout.Space(8);
            GUILayout.Label("Seed: " + cfg.randomSeed + (cfg.randomSeed == 0 ? " (random)" : ""), valLabel);
            cfg.autoIncrementSeed = ToggleRow("Auto-increment seed", cfg.autoIncrementSeed);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-1", btn, GUILayout.Width(40))) cfg.randomSeed -= 1;
            if (GUILayout.Button("+1", btn, GUILayout.Width(40))) cfg.randomSeed += 1;
            if (GUILayout.Button("Reset to 100", btn)) cfg.randomSeed = 100;
            GUILayout.EndHorizontal();
        }
        else GUILayout.Label("(ExperimentConfig missing)", label);

        GUILayout.Space(8);
        GUILayout.Label("VIEW", header);
        var camLayout = SceneCameraLayout.Instance;
        if (camLayout != null)
        {
            GUILayout.Label("Scene zoom: " + camLayout.sceneZoom.ToString("F2") + "x", label);
            float newZoom = GUILayout.HorizontalSlider(camLayout.sceneZoom, 0.4f, 3f);
            if (!Mathf.Approximately(newZoom, camLayout.sceneZoom))
                camLayout.SetZoom(newZoom);
        }
        else GUILayout.Label("(SceneCameraLayout missing from Main Camera)", label);

        GUILayout.Space(8);
        GUILayout.Label("OBSTACLES", header);
        if (obs != null)
        {
            if (ToggleRow("Fixed", obs.FixedActive) != obs.FixedActive) obs.ToggleFixed();
            if (ToggleRow("Mobile", obs.MobileActive) != obs.MobileActive) obs.ToggleMobile();
        }

        GUILayout.Space(10);
        GUILayout.Label("METRICS", header);

        if (m != null)
        {
            if (bb_ref != null)
                MetricRow("State", bb_ref.combatState + (bb_ref.phase2Active ? " (Phase 2)" : ""));
            MetricRow("Elapsed time", m.elapsedTime.ToString("F2") + " s" +
                (m.timerRunning ? "  *" : ""));
            MetricRow("Detection time", Fmt(m.detectionTime));
            MetricRow("Reaction", Fmt(m.reactionTime));
            MetricRow("Awareness", m.agentsAware + "/" + m.totalAgents);
            MetricRow("Full awareness at", Fmt(m.timeFullAwareness));
            MetricRow("Agents alive", m.agentsAlive + " (HP " + m.totalAgentHP.ToString("F0") + ")");
            MetricRow("Enemies alive", m.enemiesAlive + " (HP " + m.totalEnemyHP.ToString("F0") + ")");
            MetricRow("Distance", m.totalDistanceTraveled.ToString("F0") + " u");
            MetricRow("Dmg -> enemies", m.damageToEnemies.ToString("F0"));
            MetricRow("Dmg -> agents", m.damageToAgents.ToString("F0"));
            MetricRow("Overkill (wasted)", m.overkillDamage.ToString("F0"));

            GUILayout.Space(4);
            if (m.finished)
            {
                GUIStyle big = new GUIStyle(valLabel); big.fontStyle = FontStyle.Bold;
                big.fontSize = fontSize + 2;

                if (m.outcome == MetricsCollector.RunOutcome.AgentsWon)
                    GUILayout.Label("AGENTS WON — time: " +
                        m.timeAllEnemiesDead.ToString("F2") + " s", big);
                else if (m.outcome == MetricsCollector.RunOutcome.EnemiesWon)
                    GUILayout.Label("ENEMIES WON — time: " +
                        m.timeAllEnemiesDead.ToString("F2") + " s", big);
                else
                    GUILayout.Label("TOTAL TIME: " + m.timeAllEnemiesDead.ToString("F2") + " s", big);
            }
            else GUILayout.Label("Battle in progress...", label);
        }
        else GUILayout.Label("(MetricsCollector missing)", label);

        GUILayout.Space(10);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    int Dropdown(string title, string id, int current, string[] options)
    {
        GUILayout.Space(3);
        GUILayout.Label(title, label);

        string shown = options[Mathf.Clamp(current, 0, options.Length - 1)];
        bool isOpen = openDropdown == id;

        if (GUILayout.Button((isOpen ? "[-] " : "[+] ") + shown, btn))
            openDropdown = isOpen ? null : id;

        if (isOpen)
        {
            for (int i = 0; i < options.Length; i++)
            {
                dropItem.normal.background = (i == current) ? texBtnSel : texBtn;
                if (GUILayout.Button("    " + options[i], dropItem))
                {
                    current = i;
                    openDropdown = null;
                }
            }
        }
        return current;
    }

    bool ToggleRow(string title, bool value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(title, valLabel, GUILayout.Width(panelWidth - 75));
        if (GUILayout.Button(value ? "ON" : "OFF", btn, GUILayout.Width(55)))
            value = !value;
        GUILayout.EndHorizontal();
        return value;
    }

    void MetricRow(string name, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(name, label, GUILayout.Width(115));
        GUILayout.Label(value, valLabel);
        GUILayout.EndHorizontal();
    }

    string Fmt(float v) { return v >= 0 ? v.ToString("F2") + " s" : "-"; }
}

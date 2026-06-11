using UnityEngine;
using UnityEngine.SceneManagement;

// Panou de control simplu si ordonat (OnGUI).
//  - panou lateral stanga cu SCROLL (incape pe orice monitor)
//  - dropdown-uri pentru fiecare axa de tehnica
//  - sectiune metrici aliniata
//  - sobru: gri inchis + text alb, fara culori inutile
public class ExperimentUI : MonoBehaviour
{
    [Header("Layout")]
    public int panelWidth = 290;
    public int fontSize = 13;

    GUIStyle panelBg, label, valLabel, btn, dropItem, header;
    Texture2D texPanel, texBtn, texBtnSel;
    bool stylesReady = false;

    Vector2 scroll = Vector2.zero;
    string openDropdown = null;
    TacticalBlackboard bb_ref;

    Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    void BuildStyles()
    {
        texPanel = Tex(new Color(0.11f, 0.11f, 0.12f, 0.97f));
        texBtn = Tex(new Color(0.20f, 0.20f, 0.22f, 1f));
        texBtnSel = Tex(new Color(0.33f, 0.33f, 0.36f, 1f));

        panelBg = new GUIStyle();
        panelBg.normal.background = texPanel;
        panelBg.padding = new RectOffset(10, 10, 10, 10);

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

    void OnGUI()
    {
        if (!stylesReady) BuildStyles();
        if (bb_ref == null) bb_ref = TacticalBlackboard.Instance;

        // Scalare pe inaltime ca sa incapa pe orice monitor.
        float designH = 740f;
        float scale = Mathf.Clamp(Screen.height / designH, 0.65f, 1.1f);
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float screenH = Screen.height / scale;

        float w = panelWidth + 20;
        GUILayout.BeginArea(new Rect(8, 8, w, screenH - 16), panelBg);
        scroll = GUILayout.BeginScrollView(scroll);

        var cfg = ExperimentConfig.Instance;
        var m = MetricsCollector.Instance;
        var obs = ObstacleManager.Instance;

        // ── START / RESET ──
        if (bb_ref != null && !bb_ref.simulationStarted)
        {
            if (GUILayout.Button("START", btn, GUILayout.Height(32)))
                bb_ref.simulationStarted = true;
            GUILayout.Label("Alege modurile, apoi START.", label);
        }
        else
        {
            if (GUILayout.Button("RESET RULARE", btn, GUILayout.Height(28)))
            {
                if (cfg != null) cfg.SaveForReload();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        // ── TEHNICI ──
        GUILayout.Space(8);
        GUILayout.Label("TEHNICI", header);

        if (cfg != null)
        {
            cfg.perceptionMode = (PerceptionMode)Dropdown("Perceptie", "perc",
                (int)cfg.perceptionMode, System.Enum.GetNames(typeof(PerceptionMode)));

            cfg.communicationMode = (CommunicationMode)Dropdown("Comunicare", "comm",
                (int)cfg.communicationMode, System.Enum.GetNames(typeof(CommunicationMode)));
            if (cfg.communicationMode == CommunicationMode.LocalBroadcast ||
                cfg.communicationMode == CommunicationMode.Relay)
                GUILayout.Label("  commRange: " + cfg.commRange.ToString("F1"), label);

            cfg.collaborationMode = (CollaborationMode)Dropdown("Colaborare", "collab",
                (int)cfg.collaborationMode, System.Enum.GetNames(typeof(CollaborationMode)));

            cfg.planningMode = (PlanningMode)Dropdown("Planificare", "plan",
                (int)cfg.planningMode, System.Enum.GetNames(typeof(PlanningMode)));

            cfg.decisionMode = (DecisionMode)Dropdown("Decizie sniper", "dec",
                (int)cfg.decisionMode, System.Enum.GetNames(typeof(DecisionMode)));

            GUILayout.Space(6);
            cfg.helpRequestEnabled = ToggleRow("Help-request", cfg.helpRequestEnabled);
            cfg.supportRegenEnabled = ToggleRow("Support regen", cfg.supportRegenEnabled);
        }
        else GUILayout.Label("(ExperimentConfig lipseste)", label);

        // ── OBSTACOLE ──
        GUILayout.Space(8);
        GUILayout.Label("OBSTACOLE", header);
        if (obs != null)
        {
            if (ToggleRow("Fixe", obs.FixedActive) != obs.FixedActive) obs.ToggleFixed();
            if (ToggleRow("Mobile", obs.MobileActive) != obs.MobileActive) obs.ToggleMobile();
        }

        // ── METRICI ──
        GUILayout.Space(10);
        GUILayout.Label("METRICI", header);

        if (m != null)
        {
            if (bb_ref != null)
                MetricRow("Stare", bb_ref.combatState + (bb_ref.phase2Active ? " (F2)" : ""));
            MetricRow("Timp scurs", m.elapsedTime.ToString("F2") + " s" +
                (m.timerRunning ? "  *" : ""));
            MetricRow("Timp detectie", Fmt(m.detectionTime));
            MetricRow("Reactie", Fmt(m.reactionTime));
            MetricRow("Constientizare", m.agentsAware + "/" + m.totalAgents);
            MetricRow("Timp toti stiu", Fmt(m.timeFullAwareness));
            MetricRow("Agenti vii", m.agentsAlive + " (HP " + m.totalAgentHP.ToString("F0") + ")");
            MetricRow("Inamici vii", m.enemiesAlive + " (HP " + m.totalEnemyHP.ToString("F0") + ")");
            MetricRow("Distanta", m.totalDistanceTraveled.ToString("F0") + " u");

            GUILayout.Space(4);
            if (m.finished)
            {
                GUIStyle big = new GUIStyle(valLabel); big.fontStyle = FontStyle.Bold;
                big.fontSize = fontSize + 2;
                GUILayout.Label("TIMP TOTAL: " + m.timeAllEnemiesDead.ToString("F2") + " s", big);
            }
            else GUILayout.Label("Lupta in desfasurare...", label);
        }
        else GUILayout.Label("(MetricsCollector lipseste)", label);

        GUILayout.Space(10);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.matrix = old;
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
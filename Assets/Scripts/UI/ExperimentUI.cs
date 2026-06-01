using UnityEngine;
using UnityEngine.SceneManagement;

// Panou de control in scena (OnGUI). Permite:
//  - activare/dezactivare obstacole (fixe / mobile) live
//  - comutarea modului de comunicare si colaborare
//  - reset rulare (reincarca scena, pastrand modurile alese)
//  - afisarea live a metricilor (le copiezi manual in documentatie)
public class ExperimentUI : MonoBehaviour
{
    [Header("Layout")]
    public int panelWidth = 320;
    public int fontSize = 13;

    GUIStyle box, label, btn, header;
    bool stylesReady = false;
    Texture2D bgTex;
    TacticalBlackboard bb_ref;

    void BuildStyles()
    {
        // Fundal opac propriu, ca sa fie sigur vizibil pe orice pipeline / scale.
        bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
        bgTex.Apply();

        box = new GUIStyle();
        box.normal.background = bgTex;
        box.padding = new RectOffset(10, 10, 10, 10);

        label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };
        label.normal.textColor = Color.white;

        btn = new GUIStyle(GUI.skin.button) { fontSize = fontSize };

        header = new GUIStyle(GUI.skin.label)
        { fontSize = fontSize + 2, fontStyle = FontStyle.Bold };
        header.normal.textColor = Color.white;

        stylesReady = true;
    }

    void Update() { } // pastreaza scriptul "viu"; logica e in OnGUI

    void OnGUI()
    {
        if (!stylesReady) BuildStyles();

        GUI.depth = 0; // deseneaza deasupra altui IMGUI

        ExperimentConfig cfg = ExperimentConfig.Instance;
        MetricsCollector m = MetricsCollector.Instance;
        ObstacleManager obs = ObstacleManager.Instance;

        // Inaltime fixa, NU Screen.height (care da probleme la scale-ul ferestrei Game).
        GUILayout.BeginArea(new Rect(10, 10, panelWidth, 760), box);
        GUILayout.BeginVertical();

        // ── CONTROALE ─────────────────────────────
        GUILayout.Label("CONTROALE EXPERIMENT", header);

        if (cfg != null)
        {
            GUILayout.Space(4);
            GUILayout.Label("Perceptie: " + cfg.perceptionMode, label);
            if (GUILayout.Button("Schimba perceptia", btn))
            {
                int n = ((int)cfg.perceptionMode + 1) % 4;
                cfg.perceptionMode = (PerceptionMode)n;
            }

            GUILayout.Space(4);
            GUILayout.Label("Comunicare: " + cfg.communicationMode, label);
            if (GUILayout.Button("Schimba comunicarea", btn))
            {
                int n = ((int)cfg.communicationMode + 1) % 3;
                cfg.communicationMode = (CommunicationMode)n;
            }

            if (cfg.communicationMode == CommunicationMode.LocalBroadcast ||
                cfg.communicationMode == CommunicationMode.Relay)
                GUILayout.Label("   commRange: " + cfg.commRange.ToString("F1"), label);

            GUILayout.Space(4);
            GUILayout.Label("Colaborare: " + cfg.collaborationMode, label);
            if (GUILayout.Button("Schimba colaborarea", btn))
            {
                int next = ((int)cfg.collaborationMode + 1) % 4;
                cfg.collaborationMode = (CollaborationMode)next;
            }

            GUILayout.Space(4);
            GUILayout.Label("Planificare: " + cfg.planningMode, label);
            if (GUILayout.Button("Schimba planificarea", btn))
            {
                int next = ((int)cfg.planningMode + 1) % 3;
                cfg.planningMode = (PlanningMode)next;
            }
        }
        else
        {
            GUILayout.Label("(ExperimentConfig lipseste!)", label);
        }

        GUILayout.Space(6);
        if (obs != null)
        {
            GUILayout.Label("OBSTACOLE", header);
            string fx = obs.FixedActive ? "ON" : "OFF";
            string mb = obs.MobileActive ? "ON" : "OFF";
            if (GUILayout.Button("Fixe: " + fx, btn))
                obs.ToggleFixed();
            if (GUILayout.Button("Mobile: " + mb, btn))
                obs.ToggleMobile();
        }

        GUILayout.Space(6);
        if (GUILayout.Button("RESET RULARE (reload)", btn))
        {
            if (cfg != null) cfg.SaveForReload();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ── METRICI ───────────────────────────────
        GUILayout.Space(10);
        GUILayout.Label("METRICI (live)", header);

        if (m != null)
        {
            if (bb_ref == null) bb_ref = TacticalBlackboard.Instance;
            if (bb_ref != null)
                GUILayout.Label("Stare: " + bb_ref.combatState +
                    (bb_ref.phase2Active ? " (Faza2)" : ""), label);

            GUILayout.Label("Timp scurs: " + m.elapsedTime.ToString("F2") + " s" +
                (m.timerRunning ? "  [ruleaza]" : ""), label);

            GUILayout.Label("Timp detectie: " +
                (m.detectionTime >= 0 ? m.detectionTime.ToString("F2") + " s" : "-"), label);
            GUILayout.Label("Reactie (contact->combat): " +
                (m.reactionTime >= 0 ? m.reactionTime.ToString("F2") + " s" : "-"), label);

            GUILayout.Label("Constientizare: " +
                m.agentsAware + "/" + m.totalAgents + " agenti", label);
            GUILayout.Label("Timp pana toti stiu: " +
                (m.timeFullAwareness >= 0 ? m.timeFullAwareness.ToString("F2") + " s" : "-"), label);

            GUILayout.Space(4);
            GUILayout.Label("Agenti vii: " + m.agentsAlive +
                "  (HP " + m.totalAgentHP.ToString("F0") + ")", label);
            GUILayout.Label("Inamici vii: " + m.enemiesAlive +
                "  (HP " + m.totalEnemyHP.ToString("F0") + ")", label);
            GUILayout.Label("Distanta parcursa: " +
                m.totalDistanceTraveled.ToString("F0") + " u", label);

            GUILayout.Space(4);
            if (m.finished)
                GUILayout.Label(">>> TIMP TOTAL: " +
                    m.timeAllEnemiesDead.ToString("F2") + " s <<<", header);
            else
                GUILayout.Label("Inamici inca in viata...", label);
        }
        else
        {
            GUILayout.Label("(MetricsCollector lipseste!)", label);
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
using System;
using System.IO;
using System.Globalization;
using UnityEngine;

public static class ExperimentLogger
{
    const string FileName = "experiment_results.csv";
    static bool announcedPath = false;
    static int runCounter = 0;

    static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    static readonly string[] Header = {
        "run", "timestamp", "seed",
        "perception", "communication", "collaboration", "planning", "decision",
        "helpRequest", "supportRegen", "obstaclesFixed", "obstaclesMobile",
        "outcome", "timedOut",
        "totalTime_s", "detectionTime_s", "reactionTime_s", "timeFullAwareness_s",
        "agentsAlive", "enemiesAlive", "totalAgentHP", "totalEnemyHP",
        "distanceTraveled", "damageToEnemies", "damageToAgents", "overkillDamage"
    };

    public static void LogCompletedRun(MetricsCollector m)
    {
        var cfg = ExperimentConfig.Instance;
        var obs = ObstacleManager.Instance;

        runCounter++;
        EnsureHeader();

        string[] fields = {
            runCounter.ToString(CultureInfo.InvariantCulture),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            (cfg != null ? cfg.randomSeed : 0).ToString(CultureInfo.InvariantCulture),
            cfg != null ? cfg.perceptionMode.ToString() : "",
            cfg != null ? cfg.communicationMode.ToString() : "",
            cfg != null ? cfg.collaborationMode.ToString() : "",
            cfg != null ? cfg.planningMode.ToString() : "",
            cfg != null ? cfg.decisionMode.ToString() : "",
            cfg != null ? cfg.helpRequestEnabled.ToString() : "",
            cfg != null ? cfg.supportRegenEnabled.ToString() : "",
            obs != null ? obs.FixedActive.ToString() : "",
            obs != null ? obs.MobileActive.ToString() : "",
            m.outcome.ToString(),
            m.timedOut.ToString(),
            Inv(m.timeAllEnemiesDead),
            Inv(m.detectionTime),
            Inv(m.reactionTime),
            Inv(m.timeFullAwareness),
            m.agentsAlive.ToString(CultureInfo.InvariantCulture),
            m.enemiesAlive.ToString(CultureInfo.InvariantCulture),
            Inv(m.totalAgentHP),
            Inv(m.totalEnemyHP),
            Inv(m.totalDistanceTraveled),
            Inv(m.damageToEnemies),
            Inv(m.damageToAgents),
            Inv(m.overkillDamage),
        };

        string row = string.Join(",", fields);

        try
        {
            File.AppendAllText(FilePath, row + "\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentLogger] Nu am putut scrie in CSV: " + e.Message);
        }

        if (!announcedPath)
        {
            announcedPath = true;
            Debug.Log("[ExperimentLogger] Rezultatele se salveaza in: " + FilePath);
        }

        if (cfg != null && cfg.autoIncrementSeed && cfg.randomSeed != 0)
            cfg.randomSeed += 1;
    }

    static string Inv(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

    static void EnsureHeader()
    {
        try
        {
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, string.Join(",", Header) + "\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentLogger] Nu am putut crea fisierul CSV: " + e.Message);
        }
    }

    public static string GetFilePath() => FilePath;

    public static void ClearFile()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentLogger] Nu am putut sterge CSV-ul vechi: " + e.Message);
        }
        runCounter = 0;
        announcedPath = false;
    }
}

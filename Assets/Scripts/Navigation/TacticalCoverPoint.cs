using UnityEngine;
using System.Collections.Generic;

// Punct tactic de acoperire pe harta. Folosit de PlanningMode.CoverPoints:
// liderul de grup se apropie de inamic trecand prin cel mai convenabil cover.
//
// DOUA moduri de a le avea in scena:
//  1. MANUAL: pui GameObject-uri cu acest script, la pozitiile dorite.
//  2. AUTO: pune un singur GameObject cu CoverPointSpawner (mai jos) care
//     genereaza cover-uri in jurul obstacolelor la Start.
public class TacticalCoverPoint : MonoBehaviour
{
    public static readonly List<TacticalCoverPoint> All = new List<TacticalCoverPoint>();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // Alege cel mai bun cover: aproape de agent SI care reduce distanta spre tinta.
    // Scor mic = mai bun. Preferam cover-uri intre agent si tinta.
    public static TacticalCoverPoint GetBestCover(Vector3 from, Vector3 target)
    {
        TacticalCoverPoint best = null;
        float bestScore = Mathf.Infinity;

        foreach (TacticalCoverPoint c in All)
        {
            if (c == null || !c.isActiveAndEnabled) continue;

            Vector3 cp = c.transform.position;
            float distFromAgent = Vector3.Distance(from, cp);
            float distCoverToTarget = Vector3.Distance(cp, target);
            float distAgentToTarget = Vector3.Distance(from, target);

            // Ignora cover-uri care ne duc mai departe de tinta decat suntem deja.
            if (distCoverToTarget > distAgentToTarget + 2f) continue;

            // Scor: cat ne costa drumul prin cover (agent->cover->tinta), penalizat usor.
            float score = distFromAgent + distCoverToTarget * 0.5f;
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 1f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
    }
}
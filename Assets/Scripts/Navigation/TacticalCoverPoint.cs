using UnityEngine;
using System.Collections.Generic;

public class TacticalCoverPoint : MonoBehaviour
{
    public static readonly List<TacticalCoverPoint> All = new List<TacticalCoverPoint>();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

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

            if (distCoverToTarget > distAgentToTarget + 2f) continue;

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

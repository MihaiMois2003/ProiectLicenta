using UnityEngine;
using System.Collections.Generic;

public enum FormationType
{
    Wedge,    // V - cel mai comun tactic
    Line,     // linie orizontala
    Column,   // coloana
    Circle    // cerc de aparare
}

public class FormationManager : MonoBehaviour
{
    public static FormationManager Instance;

    [Header("Settings")]
    public FormationType currentFormation = FormationType.Wedge;
    public float spacing = 3f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Returneaza pozitia din formatie pentru agentul cu indexul dat
    public Vector3 GetFormationPosition(Vector3 leaderPosition,
        Quaternion leaderRotation, int agentIndex, int totalAgents)
    {
        Vector3 offset = Vector3.zero;

        switch (currentFormation)
        {
            case FormationType.Wedge:
                offset = GetWedgeOffset(agentIndex);
                break;
            case FormationType.Line:
                offset = GetLineOffset(agentIndex, totalAgents);
                break;
            case FormationType.Column:
                offset = GetColumnOffset(agentIndex);
                break;
            case FormationType.Circle:
                offset = GetCircleOffset(agentIndex, totalAgents);
                break;
        }

        // Rotim offset-ul in directia leader-ului
        Vector3 rotatedOffset = leaderRotation * offset * spacing;
        return leaderPosition + rotatedOffset;
    }

    Vector3 GetWedgeOffset(int index)
    {
        switch (index)
        {
            case 0: return new Vector3(-1, 0, -1);  // stanga spate
            case 1: return new Vector3(1, 0, -1);   // dreapta spate
            case 2: return new Vector3(-2, 0, -2);  // stanga spate departe
            case 3: return new Vector3(2, 0, -2);   // dreapta spate departe
            case 4: return new Vector3(0, 0, -3);   // centru spate
            default: return new Vector3(0, 0, -index);
        }
    }

    Vector3 GetLineOffset(int index, int total)
    {
        float startX = -(total - 1) * 0.5f;
        return new Vector3(startX + index, 0, -1);
    }

    Vector3 GetColumnOffset(int index)
    {
        return new Vector3(0, 0, -(index + 1));
    }

    Vector3 GetCircleOffset(int index, int total)
    {
        float angle = (360f / total) * index * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 2f;
    }
}
using UnityEngine;

public class FormationManager : MonoBehaviour
{
    public static FormationManager Instance;

    [Header("Formation Settings")]
    public float spacing = 3f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public Vector3 GetFormationPosition(Vector3 leaderPosition,
        Quaternion leaderRotation, int row, int indexInRow, int totalInRow)
    {

        float totalWidth = (totalInRow - 1) * spacing;
        float startX = -totalWidth / 2f;
        float xOffset = startX + indexInRow * spacing;
        float zOffset = -row * spacing;

        Vector3 localOffset = new Vector3(xOffset, 0, zOffset);
        Vector3 rotatedOffset = leaderRotation * localOffset;
        return leaderPosition + rotatedOffset;
    }
}

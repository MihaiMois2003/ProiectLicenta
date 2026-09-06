using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SceneCameraLayout : MonoBehaviour
{
    public static SceneCameraLayout Instance;

    [Header("Fundal (zona de sub panou)")]
    [Tooltip("Culoarea cu care se curata zona din stanga, sub panoul de control. " +
             "Recomandat: aceeasi nuanta ca fundalul panoului, ca sa nu se vada un contrast dur.")]
    public Color backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);

    [Header("Zoom scena (afecteaza DOAR camera, niciodata panoul)")]
    [Tooltip("1 = marimea originala. >1 = mai aproape (zoom in). <1 = mai departe (zoom out).")]
    [Range(0.4f, 3f)] public float sceneZoom = 1f;

    private Camera mainCam;
    private Camera bgCam;
    private int lastScreenW = -1, lastScreenH = -1, lastReserved = -1;
    private float lastAppliedZoom = -1f;

    private bool isOrtho;
    private float baseFOV;
    private float baseOrthoSize;

    void Awake()
    {
        Instance = this;
        mainCam = GetComponent<Camera>();

        isOrtho = mainCam.orthographic;
        baseFOV = mainCam.fieldOfView;
        baseOrthoSize = mainCam.orthographicSize;

        EnsureBackgroundCamera();
        ApplyLayout();
        ApplyZoom();
    }

    void Update()
    {
        int reserved = ExperimentUI.ReservedPixelWidth;
        if (Screen.width != lastScreenW || Screen.height != lastScreenH || reserved != lastReserved)
            ApplyLayout();

        if (!Mathf.Approximately(sceneZoom, lastAppliedZoom))
            ApplyZoom();
    }

    public void SetZoom(float zoom)
    {
        sceneZoom = Mathf.Clamp(zoom, 0.4f, 3f);
        ApplyZoom();
    }

    void ApplyZoom()
    {
        lastAppliedZoom = sceneZoom;
        if (isOrtho)
        {

            mainCam.orthographicSize = baseOrthoSize / sceneZoom;
        }
        else
        {

            mainCam.fieldOfView = Mathf.Clamp(baseFOV / sceneZoom, 1f, 170f);
        }
    }

    void EnsureBackgroundCamera()
    {
        if (bgCam != null) return;

        GameObject go = new GameObject("PanelBackgroundCamera");
        go.transform.SetParent(transform.parent, false);
        bgCam = go.AddComponent<Camera>();

        bgCam.clearFlags = CameraClearFlags.SolidColor;
        bgCam.backgroundColor = backgroundColor;
        bgCam.cullingMask = 0;
        bgCam.depth = mainCam.depth - 10;
        bgCam.orthographic = true;
        bgCam.orthographicSize = 1f;
        bgCam.nearClipPlane = 0.01f;
        bgCam.farClipPlane = 1f;
        bgCam.useOcclusionCulling = false;
        bgCam.allowHDR = false;
        bgCam.allowMSAA = false;
    }

    void ApplyLayout()
    {
        lastScreenW = Screen.width;
        lastScreenH = Screen.height;
        lastReserved = ExperimentUI.ReservedPixelWidth;

        float reservedFrac = Mathf.Clamp01((float)lastReserved / Mathf.Max(1, lastScreenW));

        mainCam.rect = new Rect(reservedFrac, 0f, 1f - reservedFrac, 1f);

        if (bgCam != null)
            bgCam.rect = new Rect(0f, 0f, 1f, 1f);
    }
}

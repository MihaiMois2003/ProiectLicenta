using UnityEngine;
using System.Collections.Generic;

// Gestioneaza obstacolele din scena: 3 fixe + 2 mobile.
// Permite activarea/dezactivarea lor live (din UI) ca sa poti masura
// "cu obstacole" vs "fara obstacole" fara sa repornesti scena.
//
// SETUP IN EDITOR:
//  - Pune fiecare obstacol (fix sau mobil) ca GameObject in scena, cu pozitia
//    decisa de tine in Inspector.
//  - Fixed obstacles: doar mesh + collider + (optional) NavMeshObstacle cu Carving.
//  - Mobile obstacles: au in plus scriptul MovingObstacle.
//  - Layerul fiecaruia setat pe "Obstacle" ca sa blocheze line-of-sight.
//  - Trage referintele in listele de mai jos.
public class ObstacleManager : MonoBehaviour
{
    public static ObstacleManager Instance;

    [Header("Obstacole fixe (3)")]
    public List<GameObject> fixedObstacles = new List<GameObject>();

    [Header("Obstacole mobile (2)")]
    public List<GameObject> mobileObstacles = new List<GameObject>();

    [Header("Stare initiala")]
    public bool fixedActiveOnStart = true;
    public bool mobileActiveOnStart = true;

    public bool FixedActive { get; private set; }
    public bool MobileActive { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }
    }

    void Start()
    {
        SetFixedActive(fixedActiveOnStart);
        SetMobileActive(mobileActiveOnStart);
    }

    public void SetFixedActive(bool active)
    {
        FixedActive = active;
        foreach (GameObject o in fixedObstacles)
            if (o != null) o.SetActive(active);
    }

    public void SetMobileActive(bool active)
    {
        MobileActive = active;
        foreach (GameObject o in mobileObstacles)
            if (o != null) o.SetActive(active);
    }

    public void ToggleFixed() => SetFixedActive(!FixedActive);
    public void ToggleMobile() => SetMobileActive(!MobileActive);
}
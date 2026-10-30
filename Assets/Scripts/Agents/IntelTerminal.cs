using UnityEngine;

public sealed class IntelTerminal : MonoBehaviour
{
    public IntelTheftMission mission;
    public GameObject insertedUsb;
    public TextMesh display;
    public Transform interactionFrame;
    public Renderer highlightTarget;
    public Material outlineMaterial;
    public Transform InteractionFrame => interactionFrame ? interactionFrame : transform;
    public FloatingObjectiveIcon LocationIcon { get; private set; }
    public void SetUsbInserted(bool inserted) { if (insertedUsb) insertedUsb.SetActive(inserted); }
    public void RefreshDisplay() { if (display) display.text = string.Empty; }
    void Start()
    {
        if (!highlightTarget) return;
        var bounds = highlightTarget.bounds;
        Vector3 point = bounds.center + Vector3.up * (bounds.extents.y + 0.22f);
        LocationIcon = FloatingObjectiveIcon.Create(InteractionFrame, InteractionFrame.InverseTransformPoint(point),
            mission ? mission.playerCamera : Camera.main, "Computer location icon");
    }
    void Update()
    {
        if (LocationIcon) LocationIcon.gameObject.SetActive(mission && !mission.HasIntel);
    }
}

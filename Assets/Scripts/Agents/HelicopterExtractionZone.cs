using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class HelicopterExtractionZone : MonoBehaviour
{
    public GameObject marker;
    public FloatingObjectiveIcon LocationIcon { get; private set; }
    void Start()
    {
        if (!marker) return;
        foreach (var renderer in marker.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = false;
        foreach (var collider in marker.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var mission = FindAnyObjectByType<IntelTheftMission>();
        LocationIcon = FloatingObjectiveIcon.Create(marker.transform, Vector3.up * 1.8f,
            mission ? mission.playerCamera : Camera.main, "Extraction location icon");
    }
    public bool Contains(Vector3 point)
    {
        var box = GetComponent<BoxCollider>();
        return box && box.enabled && new Bounds(box.center, box.size).Contains(transform.InverseTransformPoint(point));
    }
    public void SetMarked(bool active) { if (marker) marker.SetActive(active); }
}

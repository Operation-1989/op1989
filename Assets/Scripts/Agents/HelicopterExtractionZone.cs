using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class HelicopterExtractionZone : MonoBehaviour
{
    public GameObject marker;
    public Transform helicopter;
    [Min(0)] public float boardingClearance = 1;
    [Tooltip("Body footprint in metres in the helicopter's heading frame; excludes rotors.")]
    public Vector2[] bodyFootprint;
    public float minimumBoardingHeight = -.5f, maximumBoardingHeight = 3;
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
        if (!box || !box.enabled) return false;
        if (!helicopter || bodyFootprint == null || bodyFootprint.Length < 3)
            return new Bounds(box.center, box.size).Contains(transform.InverseTransformPoint(point));
        // Import scale is centimetres; use a rotation-only frame so clearance is in game metres.
        Vector3 local = Quaternion.Inverse(helicopter.rotation) * (point - helicopter.position);
        if (local.y < minimumBoardingHeight || local.y > maximumBoardingHeight) return false;
        Vector2 query = new Vector2(local.x, local.z);
        bool inside = false;
        float distanceSquared = float.PositiveInfinity;
        for (int i = 0, j = bodyFootprint.Length - 1; i < bodyFootprint.Length; j = i++)
        {
            Vector2 a = bodyFootprint[j], b = bodyFootprint[i], edge = b - a;
            float t = edge.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector2.Dot(query - a, edge) / edge.sqrMagnitude) : 0;
            distanceSquared = Mathf.Min(distanceSquared, (query - a - edge * t).sqrMagnitude);
            if ((a.y > query.y) != (b.y > query.y) &&
                query.x < (b.x - a.x) * (query.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside || distanceSquared <= boardingClearance * boardingClearance;
    }
    public void SetMarked(bool active) { if (marker) marker.SetActive(active); }
}

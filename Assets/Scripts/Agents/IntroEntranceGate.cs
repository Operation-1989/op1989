using UnityEngine;

/// <summary>Entrance leaves with a swept-volume safety interlock.</summary>
public sealed class IntroEntranceGate : MonoBehaviour
{
    public Transform panel;
    public Transform leftHinge;
    public Transform rightHinge;
    public float leftOpenAngle = -100;
    public float rightOpenAngle = 100;
    public Vector3 openOffset = new Vector3(18, 0, 0);
    [Min(0.1f)] public float travelTime = 2.5f;
    public Bounds safetyVolume = new Bounds(new Vector3(4, 3, -91), new Vector3(36, 8, 8));
    public Transform player;
    public Transform truck;
    public bool IsOpen => fraction >= 0.999f;
    public bool IsClosed => fraction <= 0.001f;
    public bool RequestedOpen { get; private set; }
    Vector3 closedPosition;
    Quaternion leftClosed, rightClosed;
    float fraction;
    bool initialized;

    void Awake()
    {
        if (panel) closedPosition = panel.position;
        if (leftHinge) leftClosed = leftHinge.localRotation;
        if (rightHinge) rightClosed = rightHinge.localRotation;
        initialized = true;
    }

    public void SetOpen(bool value) => RequestedOpen = value;

    public void ResetGate()
    {
        if (!initialized) return;
        RequestedOpen = false;
        fraction = 0;
        ApplyPose();
    }

    bool Occupied()
    {
        foreach (var collider in Physics.OverlapBox(safetyVolume.center, safetyVolume.extents,
                     Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if ((panel && collider.transform.IsChildOf(panel)) ||
                (leftHinge && collider.transform.IsChildOf(leftHinge)) ||
                (rightHinge && collider.transform.IsChildOf(rightHinge))) continue;
            if ((player && collider.transform.IsChildOf(player)) ||
                (truck && collider.transform.IsChildOf(truck)) ||
                collider.attachedRigidbody || collider is CharacterController)
                return true;
        }
        return false;
    }

    void Update()
    {
        // Check throughout closing, including when somebody enters after closing began.
        bool safeTarget = RequestedOpen || (!IsClosed && Occupied());
        fraction = Mathf.MoveTowards(fraction, safeTarget ? 1 : 0,
            Time.deltaTime / Mathf.Max(0.1f, travelTime));
        ApplyPose();
    }

    void ApplyPose()
    {
        float t = Mathf.SmoothStep(0, 1, fraction);
        if (panel) panel.position = closedPosition + openOffset * t;
        if (leftHinge) leftHinge.localRotation = leftClosed * Quaternion.Euler(0, leftOpenAngle * t, 0);
        if (rightHinge) rightHinge.localRotation = rightClosed * Quaternion.Euler(0, rightOpenAngle * t, 0);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(safetyVolume.center, safetyVolume.size);
    }
}

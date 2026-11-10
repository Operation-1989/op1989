using UnityEngine;

/// <summary>The only elite component allowed to sample the player's position.</summary>
public sealed class ElitePerception : MonoBehaviour
{
    public Transform target;
    public Vector3 eyeOffset = new Vector3(0, 1.55f, 0);
    public Vector3 targetOffset = new Vector3(0, .45f, 0);
    [Min(0)] public float range = 26;
    [Range(0, 360)] public float fieldOfView = 120;
    [Min(.1f)] public float memorySeconds = 8;
    public LayerMask obstacleLayers = ~0;
    public bool Visible { get; private set; }
    public bool HasMemory { get; private set; }
    public Vector3 LastPosition { get; private set; }
    public Vector3 LastAim { get; private set; }
    public float ObservedAt { get; private set; } = float.NegativeInfinity;
    public float Confidence => HasMemory ? Mathf.Clamp01(1 - (Time.time - ObservedAt) / Mathf.Max(.1f, memorySeconds)) : 0;
    public Vector3 Eye => transform.TransformPoint(eyeOffset);

    public static bool InField(Vector3 forward, Vector3 delta, float distance, float angle)
        => delta.sqrMagnitude <= distance * distance && Vector3.Angle(forward, delta) <= angle * .5f;

    public static bool Clear(Vector3 from, Vector3 to, Transform owner, Transform allowedTarget, int layers = ~0)
    {
        Vector3 delta = to - from;
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, layers, QueryTriggerInteraction.Ignore))
        {
            if (owner && (hit.transform == owner || hit.transform.IsChildOf(owner))) continue;
            if (allowedTarget && (hit.transform == allowedTarget || hit.transform.IsChildOf(allowedTarget))) continue;
            return false;
        }
        return true;
    }

    public void Sense()
    {
        Visible = false;
        if (!target) return;
        var health = target.GetComponent<CombatHealth>();
        var controls = target.GetComponent<FirstPersonController>();
        if ((health && health.Dead) || (controls && !controls.isActiveAndEnabled)) { Forget(); return; }
        // Reading a candidate for a visibility test is permitted. No hidden sample escapes this method.
        Vector3 aim = target.position + targetOffset;
        if (!InField(transform.forward, aim - Eye, Mathf.Max(0, range), fieldOfView) ||
            !Clear(Eye, aim, transform, target, obstacleLayers)) return;
        Visible = HasMemory = true;
        LastPosition = target.position;
        LastAim = aim;
        ObservedAt = Time.time;
    }

    public void Forget() { Visible = HasMemory = false; ObservedAt = float.NegativeInfinity; }
}

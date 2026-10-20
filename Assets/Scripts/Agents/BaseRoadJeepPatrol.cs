using UnityEngine;

/// <summary>Loops the base perimeter route, yielding to obstacles and staying on its road surfaces.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class BaseRoadJeepPatrol : MonoBehaviour
{
    public Transform[] waypoints;
    public Collider[] groundSurfaces;
    [Min(.1f)] public float speed = 5;
    [Min(.1f)] public float acceleration = 2;
    [Min(1)] public float turnSpeed = 100;
    [Min(.01f)] public float arrivalDistance = .12f;
    [Min(.05f)] public float obstacleClearance = .6f;
    public int TargetPoint { get; private set; }
    public int CompletedLaps { get; private set; }
    public float CurrentSpeed { get; private set; }
    public float DistanceTravelled { get; private set; }
    public string BlockedBy { get; private set; }
    Rigidbody body;
    BoxCollider footprint;

    void Awake() => ResetPatrol();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BeginPlaySession()
    {
        foreach (var patrol in FindObjectsByType<BaseRoadJeepPatrol>())
            if (patrol.isActiveAndEnabled) patrol.ResetPatrol();
    }

    public void ResetPatrol()
    {
        body = GetComponent<Rigidbody>();
        footprint = GetComponent<BoxCollider>();
        CurrentSpeed = DistanceTravelled = 0;
        CompletedLaps = 0;
        TargetPoint = 1;
        BlockedBy = "";
        if (!ValidRoute()) return;
        var direction = waypoints[1].position - waypoints[0].position;
        direction.y = 0;
        var rotation = direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : transform.rotation;
        transform.SetPositionAndRotation(waypoints[0].position, rotation);
        if (body)
        {
            body.position = transform.position;
            body.rotation = rotation;
        }
    }

    bool ValidRoute()
    {
        if (waypoints == null || waypoints.Length < 3) return false;
        foreach (var point in waypoints) if (!point) return false;
        return true;
    }

    void FixedUpdate() => StepPatrol(Time.fixedDeltaTime);

    // Also permits deterministic route validation in Edit mode, without changing other agents.
    public void StepPatrol(float dt)
    {
        if (dt <= 0 || !ValidRoute()) return;
        if (!body) body = GetComponent<Rigidbody>();
        if (!footprint) footprint = GetComponent<BoxCollider>();
        var position = transform.position;
        var delta = waypoints[TargetPoint].position - position;
        delta.y = 0;
        float distance = delta.magnitude;
        if (distance <= arrivalDistance)
        {
            if (TargetPoint == 0) CompletedLaps++;
            TargetPoint = (TargetPoint + 1) % waypoints.Length;
            delta = waypoints[TargetPoint].position - position;
            delta.y = 0;
            distance = delta.magnitude;
        }
        if (distance < .001f) return;
        var direction = delta / distance;
        var rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * dt);
        var outgoing = waypoints[(TargetPoint + 1) % waypoints.Length].position - waypoints[TargetPoint].position;
        outgoing.y = 0;
        float corner = outgoing.sqrMagnitude > .001f ? Vector3.Angle(direction, outgoing) : 0;
        float cornerSpeed = speed * Mathf.Lerp(1, .4f, Mathf.Clamp01(corner / 90));
        float desiredSpeed = Mathf.Min(speed, Mathf.Sqrt(cornerSpeed * cornerSpeed + 2 * acceleration * distance));
        desiredSpeed *= Mathf.Lerp(1, .2f, Mathf.Clamp01(Vector3.Angle(transform.forward, direction) / 90));
        float nextSpeed = Mathf.MoveTowards(CurrentSpeed, desiredSpeed, acceleration * dt);
        float step = Mathf.Min(distance, nextSpeed * dt);
        BlockedBy = "";
        if (footprint)
        {
            // Lift the cast slightly above the road to avoid treating curbs/road seams as vehicles.
            var center = position + rotation * (footprint.center + Vector3.up * .08f);
            var half = footprint.size * .46f;
            foreach (var hit in Physics.BoxCastAll(center, half, direction, rotation,
                         step + obstacleClearance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (groundSurfaces != null && System.Array.IndexOf(groundSurfaces, hit.collider) >= 0) continue;
                if (hit.normal.y > .7f) continue;
                BlockedBy = hit.transform.name;
                CurrentSpeed = 0;
                return;
            }
        }
        var next = position + direction * step;
        float ground = float.NegativeInfinity;
        if (groundSurfaces != null)
            foreach (var surface in groundSurfaces)
                if (surface && surface.Raycast(new Ray(next + Vector3.up * 2, Vector3.down), out var hit, 4) &&
                    hit.normal.y > .7f && Mathf.Abs(hit.point.y - position.y) < .5f)
                    ground = Mathf.Max(ground, hit.point.y);
        if (!float.IsNegativeInfinity(ground)) next.y = ground + .02f;
        CurrentSpeed = nextSpeed;
        DistanceTravelled += step;
        if (Application.isPlaying)
        {
            body.MovePosition(next);
            body.MoveRotation(rotation);
        }
        else transform.SetPositionAndRotation(next, rotation);
    }

    void OnDisable() { CurrentSpeed = 0; }

    void OnDrawGizmosSelected()
    {
        if (!ValidRoute()) return;
        Gizmos.color = new Color(1, .15f, .1f);
        for (int i = 0; i < waypoints.Length; i++)
        {
            Gizmos.DrawLine(waypoints[i].position + Vector3.up * .15f,
                waypoints[(i + 1) % waypoints.Length].position + Vector3.up * .15f);
            Gizmos.DrawSphere(waypoints[i].position + Vector3.up * .15f, .3f);
        }
    }
}

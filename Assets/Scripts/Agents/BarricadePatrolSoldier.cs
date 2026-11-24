using System.Collections.Generic;
using UnityEngine;
using Operation1989.AI.Jeep;

[RequireComponent(typeof(CharacterController), typeof(RifleWeapon), typeof(CombatHealth))]
public sealed class BarricadePatrolSoldier : MonoBehaviour
{
    public const float PatrolMaximumHealth = 200f; // Eight hits from the 25-damage player rifle.
    public Transform player;
    public Transform[] waypoints;
    public Animator animator;
    public float detectionRange = 32, fieldOfView = 120, walkSpeed = 1.5f, runSpeed = 3.5f;
    [HideInInspector] public float maximumAdvance = 4; // Legacy scene field; no pursuit cap.
    public float minimumDistance = 14, shotInterval = .18f, loseSightSeconds = 8;
    public string State { get; private set; } = "Patrol";
    public float AdvancedDistance { get; private set; }
    CharacterController controller;
    RifleWeapon rifle;
    CombatHealth health, targetHealth;
    IntroTruckSequence intro;
    JeepNavigationGrid navigation;
    readonly List<Vector3> path = new List<Vector3>();
    int waypoint, burst, pathIndex;
    float lastSeen = -100, waitUntil, nextShot, nextPlan, blockedSeconds;
    Vector3 previous, lastKnownPosition, plannedGoal;
    Collider lastBlocker;

    void Awake()
    {
        controller = GetComponent<CharacterController>(); rifle = GetComponent<RifleWeapon>(); health = GetComponent<CombatHealth>();
        intro = FindAnyObjectByType<IntroTruckSequence>();
        previous = transform.position; ResolvePlayer();
    }
    void ResolvePlayer()
    {
        if (!player)
        {
            var found = FindAnyObjectByType<FirstPersonController>();
            if (found) player = found.transform;
        }
        targetHealth = player ? player.GetComponent<CombatHealth>() : null;
    }
    public void ResetPatrol()
    {
        State = "Patrol"; AdvancedDistance = 0; waypoint = burst = pathIndex = 0;
        lastSeen = -100; waitUntil = nextShot = nextPlan = blockedSeconds = 0;
        previous = transform.position; path.Clear();
    }
    public void Alert()
    {
        if (!player || (health && health.Dead)) return;
        if (State == "Patrol") { State = "Chase"; AdvancedDistance = 0; nextPlan = 0; path.Clear(); }
        lastSeen = Time.time; lastKnownPosition = player.position;
    }
    Vector3 Target
    {
        get
        {
            var body = player.GetComponent<CharacterController>();
            return body ? body.bounds.center : player.position + Vector3.up;
        }
    }
    bool Visible(bool angleCheck)
    {
        Vector3 eye = controller.bounds.center + Vector3.up * controller.height * .25f;
        Vector3 delta = Target - eye;
        if (delta.magnitude > detectionRange || (angleCheck && Vector3.Angle(transform.forward, delta) > fieldOfView * .5f)) return false;
        return !RifleWeapon.Trace(eye, delta.normalized, delta.magnitude, transform, out var hit) ||
            hit.transform == player || hit.transform.IsChildOf(player);
    }
    void Update()
    {
        if (!player) ResolvePlayer();
        if (!player || !controller || !controller.enabled || (health && health.Dead)) return;
        var movement = player.GetComponent<FirstPersonController>();
        bool active = (!movement || movement.isActiveAndEnabled) && (!targetHealth || !targetHealth.Dead) &&
            (!intro || (intro.HandedOff && intro.Entered));
        bool visible = active && Visible(State == "Patrol");
        if (visible) Alert();
        if (State != "Patrol" && (!active || Time.time - lastSeen > loseSightSeconds))
        {
            State = "Patrol"; path.Clear(); nextPlan = 0; waitUntil = 0;
        }
        Vector3 move = Vector3.zero;
        if (State == "Patrol")
        {
            if (waypoints != null && waypoints.Length > 0)
            {
                waypoint %= waypoints.Length;
                var point = waypoints[waypoint];
                if (!point) { waypoint = (waypoint + 1) % waypoints.Length; path.Clear(); }
                else if (Flat(point.position - transform.position).magnitude < .6f ||
                    (path.Count > 0 && pathIndex == path.Count - 1 && Flat(path[pathIndex] - transform.position).magnitude < .6f))
                {
                    if (waitUntil == 0) waitUntil = Time.time + 1f;
                    if (Time.time >= waitUntil) { waypoint = (waypoint + 1) % waypoints.Length; waitUntil = 0; nextPlan = 0; path.Clear(); }
                }
                else move = Navigate(point.position, walkSpeed, .3f);
            }
        }
        else
        {
            float distance = Flat(player.position - transform.position).magnitude;
            if (State == "Fire" && (!visible || distance > minimumDistance * 1.5f)) { State = "Chase"; nextPlan = 0; path.Clear(); }
            if (State == "Chase" && visible && distance <= minimumDistance) { State = "Fire"; path.Clear(); }
            if (State == "Chase") move = Navigate(lastKnownPosition, runSpeed, visible ? minimumDistance - .5f : .5f);
            if (State == "Fire" && visible)
            {
                Face(Flat(player.position - transform.position));
                if (Time.time >= nextShot && Vector3.Angle(transform.forward, Flat(player.position - transform.position)) < 12)
                {
                    Vector3 origin = rifle.muzzle ? rifle.muzzle.position : controller.bounds.center + Vector3.up * .4f;
                    Vector3 aim = Target + Random.insideUnitSphere * .35f;
                    rifle.Shoot(origin, (aim - origin).normalized);
                    if (animator) animator.SetTrigger("Fire");
                    burst++; nextShot = Time.time + (burst % 3 == 0 ? 1.1f : shotInterval);
                }
            }
        }
        if (move.sqrMagnitude > .001f) Face(move);
        controller.Move((move + Vector3.down * 2) * Time.deltaTime);
        Vector3 traveled = Flat(transform.position - previous);
        if (State == "Chase") AdvancedDistance += traveled.magnitude;
        if (animator) animator.SetFloat("Speed", traveled.magnitude > .001f ? (State == "Chase" ? 2 : 1) : 0, .12f, Time.deltaTime);
        if (move.sqrMagnitude > .1f && traveled.magnitude < Time.deltaTime * .1f)
        {
            blockedSeconds += Time.deltaTime;
            if (blockedSeconds > .6f && navigation && lastBlocker)
            {
                var bounds = lastBlocker.bounds;
                if (bounds.size.x > 8 || bounds.size.z > 8) bounds = new Bounds(transform.position + move.normalized, Vector3.one);
                navigation.MarkTemporaryObstacle(bounds, 2f);
                nextPlan = 0; path.Clear(); blockedSeconds = 0;
            }
        }
        else blockedSeconds = 0;
        previous = transform.position;
    }
    Vector3 Navigate(Vector3 goal, float speed, float stopDistance)
    {
        if (Flat(goal - transform.position).magnitude <= stopDistance) return Vector3.zero;
        EnsureNavigation(goal);
        if (Time.time >= nextPlan && (path.Count == 0 || Flat(goal - plannedGoal).magnitude > 1f))
        {
            nextPlan = Time.time + .5f; plannedGoal = goal;
            if (!navigation.FindPath(transform.position, goal, path, 4f, true)) return Vector3.zero;
            pathIndex = path.Count > 1 ? 1 : 0;
        }
        if (path.Count == 0) return Vector3.zero;
        while (pathIndex < path.Count - 1 && Flat(path[pathIndex] - transform.position).magnitude < .3f) pathIndex++;
        Vector3 delta = Flat(path[pathIndex] - transform.position);
        if (delta.magnitude < .15f) { path.Clear(); return Vector3.zero; }
        return delta.normalized * Mathf.Min(speed, delta.magnitude / Mathf.Max(.001f, Time.deltaTime));
    }
    void EnsureNavigation(Vector3 goal)
    {
        if (!navigation)
        {
            navigation = GetComponent<JeepNavigationGrid>();
            if (!navigation) navigation = gameObject.AddComponent<JeepNavigationGrid>();
            navigation.cellSize = .5f; navigation.autoBoundsMargin = 25f;
            navigation.vehicleHalfWidth = controller.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            navigation.safetyMargin = .12f; navigation.comfortDistance = 0;
            navigation.obstacleBottom = Mathf.Max(.15f, controller.stepOffset);
            navigation.obstacleTop = controller.height; navigation.maxStepHeight = Mathf.Max(.15f, controller.stepOffset);
            navigation.roadCost = navigation.offRoadCost = 1f; navigation.nearObstacleCost = 0; navigation.drawPath = false;
        }
        if (navigation.IsBuilt && navigation.WorldToCell(transform.position, out _, out _) && navigation.WorldToCell(goal, out _, out _)) return;
        var bounds = new Bounds(transform.position, Vector3.zero); bounds.Encapsulate(goal);
        if (waypoints != null) foreach (var point in waypoints)
            if (point && Flat(point.position - transform.position).magnitude < 150) bounds.Encapsulate(point.position);
        navigation.Build(transform, controller.bounds.min.y, bounds);
        path.Clear(); nextPlan = 0;
    }
    void OnControllerColliderHit(ControllerColliderHit hit) { if (Mathf.Abs(hit.normal.y) < .7f) lastBlocker = hit.collider; }
    void Face(Vector3 direction)
    {
        if (direction.sqrMagnitude > .01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 240 * Time.deltaTime);
    }
    static Vector3 Flat(Vector3 value) { value.y = 0; return value; }
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int i = 1; i < path.Count; i++) Gizmos.DrawLine(path[i - 1] + Vector3.up, path[i] + Vector3.up);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ElitePerception), typeof(EliteMovement), typeof(CombatHealth))]
[RequireComponent(typeof(RifleWeapon))]
public sealed class EliteGuardController : MonoBehaviour
{
    public Transform guardPost;
    public EliteCoverPoint[] coverPoints = Array.Empty<EliteCoverPoint>();
    public EliteUtility utility = new EliteUtility();
    [Min(1)] public float defensiveRadius = 16;
    [Min(.05f)] public float decisionInterval = .25f;
    [Min(0)] public float exposurePenalty = 2;
    [Min(.1f)] public float preferredRange = 10, searchSeconds = 7, searchRadius = 2.5f;
    [Min(.05f)] public float shotInterval = .35f;
    [Range(0, 45)] public float aimTolerance = 10;
    [Range(0, 1)] public float memoryThreshold = .12f;
    public bool debugOverlay;
    public EliteAction Action { get; private set; }
    public float[] Scores { get; private set; } = new float[5];
    public EliteCoverPoint Cover { get; private set; }
    ElitePerception perception;
    EliteMovement movement;
    CombatHealth health;
    RifleWeapon rifle;
    Vector3 spawn, post, searchGoal;
    Quaternion facing;
    float nextDecision, selectedAt, nextShot, previousHealth, nextSearchPoint, failedUntil;
    bool wasDead, hadSight;
    int searchIndex;
    readonly List<Vector3> candidatePath = new List<Vector3>();

    void Awake()
    {
        perception = GetComponent<ElitePerception>(); movement = GetComponent<EliteMovement>();
        health = GetComponent<CombatHealth>(); rifle = GetComponent<RifleWeapon>();
        spawn = transform.position; post = guardPost ? guardPost.position : spawn;
        facing = guardPost ? guardPost.rotation : transform.rotation;
        previousHealth = health.maximumHealth;
        ResetGuard();
    }

    public void ResetGuard()
    {
        if (!perception) return;
        post = guardPost ? guardPost.position : spawn;
        if (guardPost) facing = guardPost.rotation;
        perception.Forget(); movement.ResetRoute(); Action = EliteAction.Defend;
        Cover = null; nextDecision = nextShot = selectedAt = nextSearchPoint = failedUntil = 0; hadSight = wasDead = false;
        searchIndex = 0; previousHealth = health.Current;
    }

    void Update()
    {
        if (health.Dead) { if (!wasDead) movement.Stop(); wasDead = true; return; }
        // CombatHealth.Restore retains this component; detect restoration without changing patrol health code.
        if (wasDead) ResetGuard();
        if (health.Current < previousHealth) nextDecision = 0; // damage alerts without providing an attacker position
        previousHealth = health.Current;
        perception.Sense();
        if (perception.Visible) { hadSight = true; searchIndex = 0; }
        else if (hadSight) { hadSight = false; nextSearchPoint = 0; }
        bool memory = perception.Confidence > memoryThreshold && Time.time - perception.ObservedAt <= searchSeconds;
        bool inside = perception.HasMemory && EliteNavigationGraph.Inside(perception.LastPosition, post, defensiveRadius);
        bool home = FlatDistance(transform.position, post) < .35f;
        // Invalid actions interrupt on this frame, independently of the utility interval/commitment.
        bool invalid = (Action == EliteAction.Engage && (!perception.Visible || !inside)) ||
                       (Action == EliteAction.Search && (!memory || !inside || perception.Visible)) ||
                       (Action == EliteAction.Cover && (!memory || !inside || !Cover || !Cover.ProtectedFrom(perception.LastAim, transform, perception.obstacleLayers))) ||
                       (Action == EliteAction.Defend && !home);
        if (Time.time >= nextDecision || invalid)
        {
            Cover = memory && inside ? ChooseCover() : null;
            float distance = FlatDistance(transform.position, post) / Mathf.Max(1, defensiveRadius);
            float targetDistance = FlatDistance(transform.position, perception.LastPosition);
            float suitability = 1 - Mathf.Clamp01(Mathf.Abs(targetDistance - preferredRange) / Mathf.Max(1, preferredRange));
            bool exposed = memory && ElitePerception.Clear(perception.LastAim, perception.Eye, transform, perception.target, perception.obstacleLayers);
            Scores = utility.Scores(perception.Visible, memory, Cover, home, inside, health.Current / Mathf.Max(1, health.maximumHealth), suitability, perception.Confidence, distance, exposed ? 1 : 0);
            if (Time.time < failedUntil) { Scores[(int)EliteAction.Cover] = -1; Scores[(int)EliteAction.Search] = -1; }
            if (invalid) Scores[(int)Action] = -1;
            EliteAction selected = utility.Select(Action, Scores, Time.time - selectedAt);
            if (selected != Action) { Action = selected; selectedAt = Time.time; movement.ResetRoute(); }
            nextDecision = Time.time + Mathf.Max(.05f, decisionInterval);
        }
        Vector3 goal = post;
        bool move = !home;
        Vector3? look = null;
        switch (Action)
        {
            case EliteAction.Defend: move = false; look = transform.position + facing * Vector3.forward; break;
            case EliteAction.Return: if (home) look = transform.position + facing * Vector3.forward; break;
            case EliteAction.Engage:
                // Hold a defensive firing position. Do not chase a player across the base.
                move = false; look = perception.LastAim; break;
            case EliteAction.Cover:
                goal = Cover ? Cover.transform.position : post; move = FlatDistance(transform.position, goal) > .2f;
                look = perception.LastAim; break;
            case EliteAction.Search:
                if (Time.time >= nextSearchPoint && (nextSearchPoint == 0 || movement.Arrived || movement.Failed)) ChooseSearchPoint();
                goal = searchGoal; move = FlatDistance(transform.position, goal) > .2f;
                if (!move) look = transform.position + Quaternion.Euler(0, Mathf.Sin(Time.time * .8f) * 80, 0) * facing * Vector3.forward;
                break;
        }
        Vector3? threat = memory ? perception.LastAim : (Vector3?)null;
        movement.Tick(goal, post, defensiveRadius, threat, exposurePenalty, move, look, Time.deltaTime);
        if (movement.Failed && Action != EliteAction.Return)
        {
            failedUntil = Time.time + movement.retryInterval;
            Action = EliteAction.Return; selectedAt = Time.time; nextDecision = Time.time + decisionInterval; movement.ResetRoute();
        }
        Fire();
    }

    EliteCoverPoint ChooseCover()
    {
        if (!movement.graph) return null;
        EliteCoverPoint best = null; float bestCost = float.PositiveInfinity;
        movement.graph.BeginQueryBatch();
        try
        {
            foreach (var point in coverPoints)
            {
                if (!point || Vector3.Distance(transform.position, point.transform.position) >= bestCost ||
                    !EliteNavigationGraph.Inside(point.transform.position, post, defensiveRadius) ||
                    !point.ProtectedFrom(perception.LastAim, transform, perception.obstacleLayers)) continue;
                if (movement.graph.FindPath(transform.position, point.transform.position, transform, perception.LastAim, exposurePenalty,
                    post, defensiveRadius, candidatePath, out float cost, out _) && cost < bestCost)
                { best = point; bestCost = cost; }
            }
            return best;
        }
        finally { movement.graph.EndQueryBatch(); }
    }

    void ChooseSearchPoint()
    {
        nextSearchPoint = Time.time + 1.4f;
        searchGoal = post;
        if (!movement.graph) return;
        for (int i = 0; i < 9; i++)
        {
            int n = searchIndex++ % 9;
            Vector3 candidate = perception.LastPosition + (n == 0 ? Vector3.zero : Quaternion.Euler(0, (n - 1) * 45, 0) * Vector3.forward * searchRadius);
            candidate.y = post.y;
            if (movement.graph.FindPath(transform.position, candidate, transform, perception.LastAim, exposurePenalty, post, defensiveRadius,
                candidatePath, out _, out _)) { searchGoal = candidate; return; }
        }
    }

    void Fire()
    {
        if (!perception.Visible || Time.time < nextShot || (Action != EliteAction.Engage && Action != EliteAction.Cover)) return;
        Vector3 origin = rifle.muzzle ? rifle.muzzle.position : perception.Eye;
        Vector3 aim = perception.LastAim;
        if (Vector3.Distance(origin, aim) > rifle.range || Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(aim - origin, Vector3.up)) > aimTolerance) return;
        // Unity raycasts omit a solid collider that already contains the ray origin.
        // A barrel pushed into cover must not become a shot through that cover.
        foreach (var collider in Physics.OverlapSphere(origin, .025f, ~0, QueryTriggerInteraction.Ignore))
            if (collider.transform != transform && !collider.transform.IsChildOf(transform) &&
                (!perception.target || (collider.transform != perception.target && !collider.transform.IsChildOf(perception.target)))) return;
        // Physical shots always use all solid layers, even if the perception mask was tuned differently.
        if (!ElitePerception.Clear(perception.Eye, aim, transform, perception.target) || !ElitePerception.Clear(origin, aim, transform, perception.target)) return;
        rifle.Shoot(origin, (aim - origin).normalized);
        if (movement.animator) movement.animator.SetTrigger("Fire");
        nextShot = Time.time + Mathf.Max(.05f, shotInterval);
    }

    static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }
    void OnDisable() { if (movement) movement.Stop(); }
    void OnGUI()
    {
        if (!debugOverlay || !perception) return;
        GUILayout.BeginArea(new Rect(15, 135, 350, 240), GUI.skin.box);
        GUILayout.Label($"Elite: {(health.Dead ? "Dead" : Action.ToString())} | Visible {perception.Visible}");
        GUILayout.Label($"Memory {perception.Confidence:F2} | Last seen {perception.ObservedAt:F1}s");
        foreach (EliteAction action in Enum.GetValues(typeof(EliteAction))) GUILayout.Label($"{action}: {(Scores[(int)action] < 0 ? "ineligible" : Scores[(int)action].ToString("F2"))}");
        GUILayout.Label($"A*: cost {movement.PathCost:F2} | expanded {movement.Expanded} | failed {movement.Failed}");
        GUILayout.EndArea();
    }
    void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? post : guardPost ? guardPost.position : transform.position;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(center, defensiveRadius);
        if (!perception) return;
        Gizmos.color = perception.Visible ? Color.red : Color.magenta;
        if (perception.HasMemory) Gizmos.DrawWireSphere(perception.LastPosition, .35f);
        Gizmos.DrawRay(perception.Eye, Quaternion.Euler(0, -perception.fieldOfView / 2, 0) * transform.forward * perception.range);
        Gizmos.DrawRay(perception.Eye, Quaternion.Euler(0, perception.fieldOfView / 2, 0) * transform.forward * perception.range);
        Gizmos.color = Color.cyan;
        for (int i = 1; i < movement.Path.Count; i++) Gizmos.DrawLine(movement.Path[i - 1] + Vector3.up * .1f, movement.Path[i] + Vector3.up * .1f);
        if (Cover) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(Cover.transform.position, .5f); }
    }
}

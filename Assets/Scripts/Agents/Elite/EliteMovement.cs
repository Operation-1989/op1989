using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class EliteMovement : MonoBehaviour
{
    public EliteNavigationGraph graph;
    public Animator animator;
    public float speed = 2.7f, acceleration = 8, turnSpeed = 240;
    [Min(.1f)] public float retryInterval = .65f;
    [Min(.1f)] public float goalChange = .65f;
    [Min(.2f)] public float stallSeconds = 1.5f;
    public readonly List<Vector3> Path = new List<Vector3>();
    public float PathCost { get; private set; }
    public int Expanded { get; private set; }
    public bool Failed { get; private set; }
    public bool Arrived => !Failed && index >= Path.Count;
    CharacterController body;
    int index;
    float nextPlan, stalled, currentSpeed;
    Vector3 plannedGoal;

    void Awake() { body = GetComponent<CharacterController>(); if (animator) animator.applyRootMotion = false; }
    public void Stop() { Path.Clear(); index = 0; currentSpeed = 0; if (animator && animator.enabled) animator.SetFloat("Speed", 0); }
    public void ResetRoute() { Stop(); Failed = false; nextPlan = stalled = 0; plannedGoal = Vector3.positiveInfinity; }
    public void Face(Vector3 direction, float dt)
    { direction.y = 0; if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * dt); }

    public void Tick(Vector3 goal, Vector3 post, float radius, Vector3? threat, float exposurePenalty, bool move, Vector3? look, float dt)
    {
        if (!body) body = GetComponent<CharacterController>();
        if (!body.enabled) { Stop(); return; }
        Vector3 previous = transform.position;
        if (!move) Stop();
        else if (!graph) { Stop(); Failed = true; }
        else
        {
            bool changed = Vector3.Distance(goal, plannedGoal) > goalChange;
            bool blocked = index < Path.Count && !graph.ClearSegment(transform.position, Path[index], transform);
            if (changed || blocked) { Path.Clear(); index = 0; currentSpeed = 0; }
            if ((changed || blocked || Failed || stalled > stallSeconds || Path.Count == 0) && Time.time >= nextPlan)
            {
                Failed = !graph.FindPath(transform.position, goal, transform, threat, exposurePenalty, post, radius, Path, out float cost, out int expanded);
                PathCost = cost; Expanded = expanded; plannedGoal = goal; index = Path.Count > 1 ? 1 : 0;
                nextPlan = Time.time + Mathf.Max(.1f, retryInterval); stalled = 0;
            }
            if (!Failed && index < Path.Count)
            {
                Vector3 delta = Path[index] - transform.position; delta.y = 0;
                if (delta.magnitude < .12f) { index++; currentSpeed = 0; }
                else
                {
                    currentSpeed = Mathf.MoveTowards(currentSpeed, speed, acceleration * dt);
                    Vector3 step = delta.normalized * Mathf.Min(currentSpeed * dt, delta.magnitude);
                    Vector3 candidate = transform.position + step;
                    if (EliteNavigationGraph.Inside(candidate, post, radius) && graph.ClearSegment(transform.position, candidate, transform)) body.Move(step);
                    if (!look.HasValue) Face(delta, dt);
                }
            }
        }
        body.Move(Vector3.down * 2 * dt);
        if (look.HasValue) Face(look.Value - transform.position, dt);
        Vector3 actual = transform.position - previous; actual.y = 0;
        stalled = move && index < Path.Count && actual.magnitude < .005f ? stalled + dt : 0;
        if (animator && animator.enabled) animator.SetFloat("Speed", actual.magnitude / Mathf.Max(dt, .001f) > .08f ? 2 : 0, .12f, dt);
    }
}

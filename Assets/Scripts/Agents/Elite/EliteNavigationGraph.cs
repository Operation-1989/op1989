using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Small authored ground graph; all edges and endpoint connectors are capsule checked at query time.</summary>
public sealed class EliteNavigationGraph : MonoBehaviour
{
    public Transform[] nodes = Array.Empty<Transform>();
    [Min(.1f)] public float connectionDistance = 2.3f;
    [Min(.05f)] public float radius = .34f;
    [Min(.2f)] public float height = 1.9f;
    public LayerMask obstacleLayers = ~0;
    public bool drawGraph;

    public bool ClearSegment(Vector3 a, Vector3 b, Transform owner)
    {
        Vector3 bottom = a + Vector3.up * (radius + .08f);
        Vector3 top = a + Vector3.up * Mathf.Max(radius + .08f, height - radius);
        foreach (var c in Physics.OverlapCapsule(bottom, top, radius, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (!Ignored(c.transform, owner)) return false;
        var delta = b - a;
        foreach (var hit in Physics.CapsuleCastAll(bottom, top, radius, delta.normalized, delta.magnitude, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (!Ignored(hit.transform, owner)) return false;
        bottom += delta; top += delta;
        foreach (var c in Physics.OverlapCapsule(bottom, top, radius, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (!Ignored(c.transform, owner)) return false;
        return true;
    }

    static bool Ignored(Transform t, Transform owner) => owner && (t == owner || t.IsChildOf(owner));

    public bool FindPath(Vector3 start, Vector3 goal, Transform owner, Vector3? threat, float exposurePenalty,
        Vector3 post, float defensiveRadius, List<Vector3> path, out float cost, out int expanded)
    {
        path.Clear(); cost = 0; expanded = 0;
        if (!Finite(start) || !Finite(goal) || !Inside(goal, post, defensiveRadius)) return false;
        if (Vector3.Distance(start, goal) < .01f)
        {
            if (!ClearSegment(start, goal, owner)) return false;
            path.Add(goal); return true;
        }
        // Include exact endpoints; never snap through a wall to a nearby graph node.
        var points = new List<Vector3> { start, goal };
        foreach (var node in nodes)
            if (node && Inside(node.position, post, defensiveRadius) && Finite(node.position)) points.Add(node.position);
        var result = Search(points, 0, 1,
            (a, b) => (Vector3.Distance(points[a], points[b]) <= connectionDistance || (a < 2 && b < 2 && Vector3.Distance(points[a], points[b]) < .05f)) &&
                      ClearSegment(points[a], points[b], owner),
            p => threat.HasValue && ElitePerception.Clear(threat.Value, p + Vector3.up * 1.3f, owner, null, obstacleLayers) ? 1 : 0,
            Mathf.Max(0, exposurePenalty), out cost, out expanded);
        if (result == null) return false;
        foreach (int index in result) path.Add(points[index]);
        return true;
    }

    public static bool Inside(Vector3 p, Vector3 post, float radius)
    { p.y = post.y; return (p - post).sqrMagnitude <= radius * radius; }
    static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

    // Edge costs are metres + penalty metres. Euclidean h is a lower bound by the triangle inequality.
    public static List<int> Search(IReadOnlyList<Vector3> points, int start, int goal, Func<int, int, bool> edge,
        Func<Vector3, float> exposure, float penalty, out float cost, out int expanded)
    {
        cost = 0; expanded = 0;
        if (points == null || start < 0 || goal < 0 || start >= points.Count || goal >= points.Count ||
            !Finite(points[start]) || !Finite(points[goal])) return null;
        int count = points.Count;
        var g = new float[count]; var parent = new int[count]; var closed = new bool[count];
        for (int i = 0; i < count; i++) { g[i] = float.PositiveInfinity; parent[i] = -1; }
        g[start] = 0;
        while (true)
        {
            int best = -1; float bestF = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (closed[i] || !Finite(points[i])) continue;
                float f = g[i] + Vector3.Distance(points[i], points[goal]);
                if (f < bestF) { best = i; bestF = f; }
            }
            if (best < 0) return null;
            expanded++;
            if (best == goal)
            {
                cost = g[goal]; var route = new List<int>();
                for (int i = goal; i >= 0; i = parent[i]) route.Add(i);
                route.Reverse(); return route;
            }
            closed[best] = true;
            for (int i = 0; i < count; i++)
            {
                if (closed[i] || i == best || !Finite(points[i]) || !edge(best, i)) continue;
                float candidate = g[best] + Vector3.Distance(points[best], points[i]) + Mathf.Max(0, penalty) * Mathf.Clamp01(exposure(points[i]));
                if (candidate < g[i]) { g[i] = candidate; parent[i] = best; }
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!drawGraph || nodes == null) return;
        Gizmos.color = new Color(0, .7f, 1, .3f);
        for (int i = 0; i < nodes.Length; i++) if (nodes[i])
        {
            Gizmos.DrawWireSphere(nodes[i].position, .08f);
            for (int j = i + 1; j < nodes.Length; j++)
                if (nodes[j] && Vector3.Distance(nodes[i].position, nodes[j].position) <= connectionDistance && ClearSegment(nodes[i].position, nodes[j].position, null))
                    Gizmos.DrawLine(nodes[i].position, nodes[j].position);
        }
    }
}

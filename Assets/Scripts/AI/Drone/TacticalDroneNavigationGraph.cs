using System;
using System.Collections.Generic;
using UnityEngine;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Represents a 3D aerial waypoint node for flight pathfinding.
    /// </summary>
    [Serializable]
    public class FlightNode
    {
        public int id;
        public string label;
        public Vector3 position;
        public List<int> neighborIndices = new List<int>();
    }

    /// <summary>
    /// 3D Spatial Navigation Graph implementing the A* (A-Star) Search Algorithm.
    /// Uses Euclidean 3D distance heuristic (admissible & consistent) and supports dynamic edge re-planning.
    /// Evaluates: f(n) = g(n) + h(n)
    /// </summary>
    [DisallowMultipleComponent]
    public class TacticalDroneNavigationGraph : MonoBehaviour
    {
        [Header("Flight Graph Configuration")]
        [SerializeField] private List<FlightNode> nodes = new List<FlightNode>();
        [Tooltip("Maximum distance between nodes to automatically connect an edge in meters.")]
        [SerializeField] private float maxConnectionDistance = 90f;
        [Tooltip("Layers treated as dynamic obstacles for edge clearance testing.")]
        [SerializeField] private LayerMask obstacleLayers = 0;

        [Header("Gizmo Visualization (VIVA Defense)")]
        [SerializeField] private bool drawGraphInEditor = true;
        [SerializeField] private bool showActivePath = true;

        // Dynamic edge blockage tracking: Key = (from << 16) | to
        private readonly HashSet<int> blockedEdges = new HashSet<int>();
        private List<Vector3> lastCalculatedPath = new List<Vector3>();

        public IReadOnlyList<FlightNode> Nodes => nodes;
        public IReadOnlyList<Vector3> LastCalculatedPath => lastCalculatedPath;
        public int LastNodesExploredCount { get; private set; }

        private void Awake()
        {
            if (nodes.Count == 0)
            {
                GenerateDefaultAirspaceNodes();
            }
        }

        /// <summary>
        /// Populates default base perimeter aerial waypoints if none exist.
        /// </summary>
        public void GenerateDefaultAirspaceNodes()
        {
            nodes.Clear();
            AddNode("Center Vehicle Depot", new Vector3(0f, 18f, 0f));
            AddNode("Command HQ Rooftop", new Vector3(35f, 22f, 20f));
            AddNode("Storage Warehouse", new Vector3(30f, 18f, -20f));
            AddNode("Helipad Extraction", new Vector3(50f, 18f, -40f));
            AddNode("Entrance Watchtower", new Vector3(10f, 18f, -65f));
            AddNode("Watchtower South", new Vector3(-35f, 18f, -50f));
            AddNode("Watchtower North", new Vector3(-35f, 18f, 50f));
            AutoConnectNeighbors();
        }

        public int AddNode(string label, Vector3 position)
        {
            int id = nodes.Count;
            nodes.Add(new FlightNode { id = id, label = label, position = position });
            return id;
        }

        public void AutoConnectNeighbors()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].neighborIndices.Clear();
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (i == j) continue;
                    float dist = Vector3.Distance(nodes[i].position, nodes[j].position);
                    if (dist <= maxConnectionDistance)
                    {
                        // Verify line-of-sight clearance between waypoints
                        Vector3 dir = (nodes[j].position - nodes[i].position).normalized;
                        if (!Physics.Raycast(nodes[i].position, dir, dist, obstacleLayers, QueryTriggerInteraction.Ignore))
                        {
                            nodes[i].neighborIndices.Add(j);
                        }
                    }
                }
            }
        }

        public int GetNearestNode(Vector3 position)
        {
            if (nodes.Count == 0) return -1;
            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = Vector3.Distance(position, nodes[i].position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        public void SetEdgeBlocked(int fromIdx, int toIdx, bool blocked)
        {
            int edgeKey1 = (fromIdx << 16) | toIdx;
            int edgeKey2 = (toIdx << 16) | fromIdx;
            if (blocked)
            {
                blockedEdges.Add(edgeKey1);
                blockedEdges.Add(edgeKey2);
                Debug.LogWarning($"[A* Navigation] Edge ({fromIdx} <-> {toIdx}) dynamically BLOCKED by obstacle.");
            }
            else
            {
                blockedEdges.Remove(edgeKey1);
                blockedEdges.Remove(edgeKey2);
            }
        }

        public bool IsEdgeBlocked(int fromIdx, int toIdx)
        {
            return blockedEdges.Contains((fromIdx << 16) | toIdx);
        }

        /// <summary>
        /// 3D Euclidean distance heuristic: h(n) = ||goal - n||
        /// Admissible: Never overestimates actual distance because straight-line distance is minimum in Euclidean space.
        /// Consistent: Satisfies triangle inequality: h(n) <= c(n, n') + h(n').
        /// </summary>
        public static float CalculateHeuristic(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dy = to.y - from.y;
            float dz = to.z - from.z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Core 3D A* Search Implementation.
        /// Finds the shortest optimal path between startNode and goalNode.
        /// </summary>
        public bool FindPathAStar(int startIndex, int goalIndex, out List<Vector3> path, out int nodesExplored)
        {
            path = new List<Vector3>();
            nodesExplored = 0;

            if (startIndex < 0 || startIndex >= nodes.Count || goalIndex < 0 || goalIndex >= nodes.Count)
            {
                return false;
            }

            if (startIndex == goalIndex)
            {
                path.Add(nodes[startIndex].position);
                lastCalculatedPath = new List<Vector3>(path);
                return true;
            }

            // Priority Queue / Open Set evaluated by f(n) = g(n) + h(n)
            var openSet = new List<int> { startIndex };
            var closedSet = new HashSet<int>();

            // CameFrom map for path reconstruction
            var cameFrom = new Dictionary<int, int>();

            // Cost from start to n: g(n)
            var gScore = new Dictionary<int, float>();
            for (int i = 0; i < nodes.Count; i++) gScore[i] = float.MaxValue;
            gScore[startIndex] = 0f;

            // Estimated total cost f(n) = g(n) + h(n)
            var fScore = new Dictionary<int, float>();
            for (int i = 0; i < nodes.Count; i++) fScore[i] = float.MaxValue;
            fScore[startIndex] = CalculateHeuristic(nodes[startIndex].position, nodes[goalIndex].position);

            while (openSet.Count > 0)
            {
                // Select node in openSet with lowest fScore
                int current = openSet[0];
                float lowestF = fScore[current];
                for (int i = 1; i < openSet.Count; i++)
                {
                    int candidate = openSet[i];
                    if (fScore[candidate] < lowestF)
                    {
                        lowestF = fScore[candidate];
                        current = candidate;
                    }
                }

                nodesExplored++;

                // Goal reached! Reconstruct path.
                if (current == goalIndex)
                {
                    ReconstructPath(cameFrom, current, path);
                    LastNodesExploredCount = nodesExplored;
                    lastCalculatedPath = new List<Vector3>(path);
                    return true;
                }

                openSet.Remove(current);
                closedSet.Add(current);

                var currentNode = nodes[current];
                foreach (int neighbor in currentNode.neighborIndices)
                {
                    if (closedSet.Contains(neighbor)) continue;
                    if (IsEdgeBlocked(current, neighbor)) continue; // Dynamic obstacle avoidance

                    float edgeDist = Vector3.Distance(currentNode.position, nodes[neighbor].position);
                    float tentativeG = gScore[current] + edgeDist;

                    if (tentativeG < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeG;
                        fScore[neighbor] = tentativeG + CalculateHeuristic(nodes[neighbor].position, nodes[goalIndex].position);

                        if (!openSet.Contains(neighbor))
                        {
                            openSet.Add(neighbor);
                        }
                    }
                }
            }

            LastNodesExploredCount = nodesExplored;
            return false; // Path not found (disconnected graph or blocked edges)
        }

        public void ConfigureCruiseHeight(float height, float groundReference)
        {
            foreach (var node in nodes)
            {
                Vector3 point = node.position;
                point.y = GroundHeightAt(point, groundReference) + Mathf.Max(3, height);
                node.position = point;
            }
        }

        public float GroundHeightAt(Vector3 point, float referenceY)
        {
            float best = referenceY, difference = float.PositiveInfinity;
            bool knownGround = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(point.x, referenceY + 40f, point.z), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.normal.y < .8f || PlayerTreePassage.IsTreeCollider(hit.collider) ||
                    hit.collider.GetComponentInParent<CombatHealth>() || hit.collider.GetComponentInParent<DroneDamage>()) continue;
                bool ground = hit.collider is TerrainCollider;
                for (var parent = hit.transform; parent && !ground; parent = parent.parent)
                    ground = parent.name == "Plane" || parent.name.StartsWith("Straight_Road", StringComparison.Ordinal);
                float delta = Mathf.Abs(hit.point.y - referenceY);
                if ((ground && !knownGround) || (ground == knownGround && delta < difference))
                { best = hit.point.y; difference = delta; knownGround = ground; }
            }
            return best;
        }

        /// <summary>A* with live, sphere-cleared connections for the actual flight endpoints.</summary>
        public bool FindFlightPath(Vector3 start, Vector3 goal, out List<Vector3> path)
        {
            path = new List<Vector3>();
            // Low flight needs lateral waypoints around foliage/buildings, not only the
            // authored high perimeter nodes. These points are local to this search.
            var points = new List<Vector3>();
            foreach (var node in nodes) points.Add(node.position);
            Vector3 direct = goal - start;
            if (direct.sqrMagnitude > .01f)
            {
                int mask = obstacleLayers.value == 0 ? ~0 : obstacleLayers.value;
                var hits = Physics.SphereCastAll(start, .8f, direct.normalized, direct.magnitude, mask, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                var seen = new HashSet<Collider>();
                foreach (var hit in hits)
                {
                    if (hit.transform.IsChildOf(transform) || !seen.Add(hit.collider)) continue;
                    var bounds = hit.collider.bounds;
                    if (bounds.size.x > 120 || bounds.size.z > 120) bounds = new Bounds(hit.point, new Vector3(16, 8, 16));
                    bounds.Expand(new Vector3(4, 0, 4));
                    foreach (int x in new[] { -1, 0, 1 })
                        foreach (int z in new[] { -1, 0, 1 })
                        {
                            if (x == 0 && z == 0) continue;
                            points.Add(new Vector3(bounds.center.x + x * bounds.extents.x, goal.y,
                                bounds.center.z + z * bounds.extents.z));
                        }
                    if (seen.Count >= 6) break;
                }
            }
            int first = points.Count; points.Add(start);
            int last = points.Count; points.Add(goal);
            int count = points.Count;
            var positions = points.ToArray();
            var cost = new float[count]; var parent = new int[count]; var closed = new bool[count];
            for (int i = 0; i < count; i++) { cost[i] = float.PositiveInfinity; parent[i] = -1; }
            cost[first] = 0;
            var open = new List<int> { first };
            LastNodesExploredCount = 0;
            while (open.Count > 0)
            {
                int current = open[0];
                foreach (int candidate in open)
                    if (cost[candidate] + Vector3.Distance(positions[candidate], goal) < cost[current] + Vector3.Distance(positions[current], goal)) current = candidate;
                open.Remove(current); closed[current] = true; LastNodesExploredCount++;
                if (current == last)
                {
                    for (int i = last; i >= 0; i = parent[i]) path.Add(positions[i]);
                    path.Reverse(); lastCalculatedPath = new List<Vector3>(path); return true;
                }
                for (int i = 0; i < count; i++)
                {
                    if (i == current || closed[i]) continue;
                    float distance = Vector3.Distance(positions[current], positions[i]);
                    if (distance > maxConnectionDistance && !(current == first && i == last)) continue;
                    if (current < nodes.Count && i < nodes.Count && IsEdgeBlocked(current, i)) continue;
                    if (cost[current] + distance >= cost[i] || !FlightSegmentClear(positions[current], positions[i])) continue;
                    cost[i] = cost[current] + distance; parent[i] = current;
                    if (!open.Contains(i)) open.Add(i);
                }
            }
            return false;
        }

        bool FlightSegmentClear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .001f) return true;
            int mask = obstacleLayers.value == 0 ? ~0 : obstacleLayers.value;
            foreach (var hit in Physics.SphereCastAll(from, .8f, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform)) return false;
            foreach (var collider in Physics.OverlapSphere(to, .8f, mask, QueryTriggerInteraction.Ignore))
                if (!collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        private void ReconstructPath(Dictionary<int, int> cameFrom, int current, List<Vector3> path)
        {
            var totalPath = new List<Vector3> { nodes[current].position };
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                totalPath.Add(nodes[current].position);
            }
            totalPath.Reverse();
            path.AddRange(totalPath);
        }

        private void OnDrawGizmos()
        {
            if (!drawGraphInEditor && !Application.isPlaying) return;

            // Draw nodes and edges
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(n.position, 1.0f);

                foreach (int neighborIdx in n.neighborIndices)
                {
                    if (neighborIdx < i) continue; // Avoid drawing duplicate lines
                    bool blocked = IsEdgeBlocked(i, neighborIdx);
                    Gizmos.color = blocked ? Color.red : new Color(0f, 1f, 0.4f, 0.4f);
                    Gizmos.DrawLine(n.position, nodes[neighborIdx].position);
                }
            }

            // Draw active calculated A* path in bold yellow
            if (showActivePath && lastCalculatedPath != null && lastCalculatedPath.Count > 1)
            {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < lastCalculatedPath.Count - 1; i++)
                {
                    Gizmos.DrawLine(lastCalculatedPath[i], lastCalculatedPath[i + 1]);
                    Gizmos.DrawWireSphere(lastCalculatedPath[i], 0.6f);
                }
                Gizmos.DrawWireSphere(lastCalculatedPath[lastCalculatedPath.Count - 1], 0.6f);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Operation1989.AI.Jeep
{
    /// <summary>
    /// Intelligent Systems – A* search space for the patrol jeep.
    ///
    /// 1. PERCEIVE THE WORLD: the base is sampled into a 2D grid. For every cell a ray finds the
    ///    drivable ground and an overlap box finds static obstacles (walls, fences, trees, props).
    /// 2. CONFIGURATION SPACE: a distance transform gives each cell its clearance (metres to the
    ///    nearest obstacle). Cells closer than the jeep's half width + safety margin are blocked,
    ///    so any path A* returns keeps the whole vehicle away from walls (no crashing).
    /// 3. COST MAP ("easiest way"): roads are cheapest, off-road ground costs more and cells close
    ///    to obstacles get an extra penalty, so the jeep prefers wide, open roads.
    /// 4. A* SEARCH: 8-connected grid, f(n) = g(n) + h(n), octile heuristic scaled by the cheapest
    ///    cell cost (admissible and consistent, so the returned path is optimal for the cost map).
    /// 5. PATH SMOOTHING: grid zig-zags are replaced by straight segments only when the straight
    ///    line is collision free AND not more expensive, so the jeep drives quickly in straight lines.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JeepNavigationGrid : MonoBehaviour
    {
        [Header("Grid area")]
        [Tooltip("Fit the grid around the road surfaces automatically when it is built.")]
        public bool autoBounds = true;
        public Vector2 areaCenter;
        public Vector2 areaSize = new Vector2(200, 200);
        [Min(.5f)] public float cellSize = 1f;
        [Min(0)] public float autoBoundsMargin = 15f;

        [Header("Ground")]
        [Tooltip("Road colliders. Taken from BaseRoadJeepPatrol.groundSurfaces when left empty.")]
        public Collider[] roadSurfaces = Array.Empty<Collider>();
        [Tooltip("Only ground within this height of the jeep counts as drivable (filters roofs).")]
        [Min(.5f)] public float maxHeightFromReference = 4f;
        [Range(0, 1)] public float minGroundNormalY = .85f;
        [Tooltip("Largest height step the jeep can drive over between neighbouring cells.")]
        [Min(.05f)] public float maxStepHeight = .6f;

        [Header("Obstacles and clearance")]
        public LayerMask obstacleLayers = ~0;
        [Tooltip("Obstacle scan starts this far above the ground, so curbs and road seams are ignored.")]
        [Min(.05f)] public float obstacleBottom = .4f;
        [Min(.5f)] public float obstacleTop = 2.6f;
        [Tooltip("Half of the jeep's width. Set from the jeep's BoxCollider at runtime.")]
        [Min(.1f)] public float vehicleHalfWidth = 1f;
        [Tooltip("Extra free space kept between the jeep's side and any obstacle.")]
        [Min(0)] public float safetyMargin = .5f;
        [Tooltip("Within this extra distance of an obstacle cells get the near-obstacle penalty.")]
        [Min(0)] public float comfortDistance = 4f;

        [Header("A* costs (per metre)")]
        [Min(.1f)] public float roadCost = 1f;
        [Min(.1f)] public float offRoadCost = 2.2f;
        [Min(0)] public float nearObstacleCost = 3f;

        [Header("Debug")]
        public bool drawPath = true;
        public bool drawBlockedCells;
        [Min(5)] public float debugDrawRadius = 45f;

        // ---- Results of the last search (shown in the Inspector overlay / validation report) ----
        public bool IsBuilt { get; private set; }
        public int Width { get; private set; }
        public int Depth { get; private set; }
        public int BlockedCellCount { get; private set; }
        public double BuildMilliseconds { get; private set; }
        public int LastExpandedNodes { get; private set; }
        public float LastPathCost { get; private set; }
        public double LastSearchMilliseconds { get; private set; }
        public readonly List<Vector3> LastRawPath = new List<Vector3>();
        public readonly List<Vector3> LastPath = new List<Vector3>();

        const float Sqrt2 = 1.41421356f;
        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        float[] groundY;      // drivable ground height, NaN when none
        bool[] staticSolid;   // obstacle (or no ground) found when baked
        bool[] road;          // ground is one of the road colliders
        float[] tempUntil;    // dynamic obstacles reported by the jeep, with expiry time
        float[] clearance;    // metres from cell centre to nearest solid cell centre
        bool[] blocked;       // final traversability (configuration space)
        float[] cellCost;     // A* cost per metre

        // Search buffers (re-used, no allocation per search)
        float[] g; int[] parent; int[] closedStamp; int[] openStamp; int stamp;
        readonly MinHeap open = new MinHeap();

        Vector3 origin;
        float referenceY;
        Transform ignoreRoot;
        HashSet<Collider> roadSet = new HashSet<Collider>();
        float nextTemporaryExpiry = float.PositiveInfinity;

        int CellCount => Width * Depth;
        float MinCellCost => Mathf.Min(roadCost, offRoadCost);
        float HardClearance => vehicleHalfWidth + safetyMargin;

        // =====================================================================================
        // 1. Build the grid
        // =====================================================================================

        /// <param name="owner">The jeep. Its own colliders are never treated as obstacles.</param>
        /// <param name="referenceHeight">Height of the jeep's wheels; ground near it is drivable.</param>
        /// <param name="extraBounds">Optional area that must be inside the grid (e.g. patrol route).</param>
        public void Build(Transform owner, float referenceHeight, Bounds? extraBounds = null)
        {
            var timer = Stopwatch.StartNew();
            Physics.SyncTransforms();
            ignoreRoot = owner;
            referenceY = referenceHeight;
            roadSet = new HashSet<Collider>();
            if (roadSurfaces != null)
                foreach (var c in roadSurfaces) if (c) roadSet.Add(c);

            if (autoBounds) FitBounds(extraBounds);

            // Keep the grid a sensible size (max 600 x 600 cells).
            float cell = Mathf.Max(cellSize, Mathf.Max(areaSize.x, areaSize.y) / 600f);
            cellSize = cell;
            Width = Mathf.Max(2, Mathf.CeilToInt(areaSize.x / cell));
            Depth = Mathf.Max(2, Mathf.CeilToInt(areaSize.y / cell));
            origin = new Vector3(areaCenter.x - Width * cell * .5f, referenceY, areaCenter.y - Depth * cell * .5f);

            int n = CellCount;
            groundY = new float[n]; staticSolid = new bool[n]; road = new bool[n]; tempUntil = new float[n];
            clearance = new float[n]; blocked = new bool[n]; cellCost = new float[n];
            g = new float[n]; parent = new int[n]; closedStamp = new int[n]; openStamp = new int[n]; stamp = 0;
            nextTemporaryExpiry = float.PositiveInfinity;

            var hits = new RaycastHit[32];
            var overlaps = new Collider[32];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                    SampleCell(x, z, hits, overlaps);

            RecomputeClearanceAndCost();
            IsBuilt = true;
            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
            Debug.Log($"[Jeep A*] Navigation grid built: {Width}x{Depth} cells of {cellSize:F2} m, " +
                      $"{BlockedCellCount} blocked, {BuildMilliseconds:F0} ms.");
        }

        void FitBounds(Bounds? extra)
        {
            bool has = false;
            var b = new Bounds();
            foreach (var c in roadSet)
            {
                if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
            }
            if (extra.HasValue)
            {
                if (!has) { b = extra.Value; has = true; } else b.Encapsulate(extra.Value);
            }
            if (!has) return; // keep the authored area
            areaCenter = new Vector2(b.center.x, b.center.z);
            areaSize = new Vector2(b.size.x + autoBoundsMargin * 2, b.size.z + autoBoundsMargin * 2);
        }

        void SampleCell(int x, int z, RaycastHit[] hits, Collider[] overlaps)
        {
            int i = Index(x, z);
            Vector3 c = CellCenterFlat(x, z);

            // Ground: the highest walkable surface near the jeep's height. Roofs are too high,
            // low crates create a step that the neighbour test rejects.
            int count = Physics.RaycastNonAlloc(new Vector3(c.x, referenceY + 60f, c.z), Vector3.down, hits, 120f,
                ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN; bool bestRoad = false;
            for (int h = 0; h < count; h++)
            {
                var hit = hits[h];
                if (IgnoredCollider(hit.collider) || hit.normal.y < minGroundNormalY) continue;
                if (Mathf.Abs(hit.point.y - referenceY) > maxHeightFromReference) continue;
                if (float.IsNaN(best) || hit.point.y > best)
                {
                    best = hit.point.y;
                    bestRoad = roadSet.Contains(hit.collider);
                }
            }
            groundY[i] = best;
            road[i] = bestRoad;
            if (float.IsNaN(best)) { staticSolid[i] = true; return; }

            // Obstacles: anything occupying the body height of the jeep above this ground cell.
            float half = (obstacleTop - obstacleBottom) * .5f;
            var center = new Vector3(c.x, best + obstacleBottom + half, c.z);
            int o = Physics.OverlapBoxNonAlloc(center, new Vector3(cellSize * .5f, half, cellSize * .5f), overlaps,
                Quaternion.identity, obstacleLayers, QueryTriggerInteraction.Ignore);
            bool solid = false;
            for (int k = 0; k < o && !solid; k++)
            {
                var col = overlaps[k];
                if (IgnoredCollider(col) || roadSet.Contains(col) || col is TerrainCollider) continue;
                solid = true;
            }
            staticSolid[i] = solid;
        }

        /// <summary>
        /// Moving things are not baked into the static map: the player (the goal!), soldiers,
        /// vehicles and anything with a Rigidbody. The jeep reports them at runtime instead.
        /// </summary>
        bool IgnoredCollider(Collider c)
        {
            if (!c || c.isTrigger) return true;
            if (ignoreRoot && c.transform.IsChildOf(ignoreRoot)) return true;
            if (c.attachedRigidbody) return true;
            if (c is CharacterController) return true;
            return false;
        }

        // =====================================================================================
        // 2 + 3. Clearance (distance transform) and cost map
        // =====================================================================================

        void RecomputeClearanceAndCost()
        {
            float now = Application.isPlaying ? Time.time : 0f;
            nextTemporaryExpiry = float.PositiveInfinity;
            int n = CellCount;
            for (int i = 0; i < n; i++)
            {
                bool temp = tempUntil[i] > now;
                if (temp) nextTemporaryExpiry = Mathf.Min(nextTemporaryExpiry, tempUntil[i]);
                clearance[i] = staticSolid[i] || temp ? 0f : float.PositiveInfinity;
            }

            // Two-pass chamfer distance transform (8 neighbours). Outside the grid counts as solid.
            float s = cellSize, d = cellSize * Sqrt2;
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    int i = Index(x, z);
                    float v = clearance[i];
                    v = Mathf.Min(v, Dist(x - 1, z) + s);
                    v = Mathf.Min(v, Dist(x - 1, z - 1) + d);
                    v = Mathf.Min(v, Dist(x, z - 1) + s);
                    v = Mathf.Min(v, Dist(x + 1, z - 1) + d);
                    clearance[i] = v;
                }
            for (int z = Depth - 1; z >= 0; z--)
                for (int x = Width - 1; x >= 0; x--)
                {
                    int i = Index(x, z);
                    float v = clearance[i];
                    v = Mathf.Min(v, Dist(x + 1, z) + s);
                    v = Mathf.Min(v, Dist(x + 1, z + 1) + d);
                    v = Mathf.Min(v, Dist(x, z + 1) + s);
                    v = Mathf.Min(v, Dist(x - 1, z + 1) + d);
                    clearance[i] = v;
                }

            int blockedCount = 0;
            float hard = HardClearance;
            for (int i = 0; i < n; i++)
            {
                // Cell centre distance minus half a cell = conservative distance to the obstacle edge.
                float free = clearance[i] - cellSize * .5f;
                blocked[i] = free < hard;
                if (blocked[i]) { blockedCount++; cellCost[i] = float.PositiveInfinity; continue; }

                float cost = road[i] ? roadCost : offRoadCost;
                if (comfortDistance > 0 && free < hard + comfortDistance)
                    cost += nearObstacleCost * (1f - (free - hard) / comfortDistance);
                cellCost[i] = cost;
            }
            BlockedCellCount = blockedCount;
        }

        float Dist(int x, int z) => InGrid(x, z) ? clearance[Index(x, z)] : 0f;

        /// <summary>Marks a dynamic obstacle (parked truck, soldier, barricade) for a while.</summary>
        public void MarkTemporaryObstacle(Bounds bounds, float seconds)
        {
            if (!IsBuilt) return;
            float until = Time.time + Mathf.Max(.1f, seconds);
            WorldToCell(bounds.min, out int x0, out int z0);
            WorldToCell(bounds.max, out int x1, out int z1);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(Depth - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Width - 1, x1); x++)
                    tempUntil[Index(x, z)] = Mathf.Max(tempUntil[Index(x, z)], until);
            RecomputeClearanceAndCost();
        }

        void RefreshExpiredObstacles()
        {
            if (Application.isPlaying && Time.time >= nextTemporaryExpiry) RecomputeClearanceAndCost();
        }

        // =====================================================================================
        // 4. A* search
        // =====================================================================================

        /// <summary>
        /// Finds the cheapest collision-free route from start to the drivable cell nearest the goal.
        /// The result is a smoothed list of world positions (on the ground), starting at the jeep.
        /// </summary>
        public bool FindPath(Vector3 start, Vector3 goal, List<Vector3> result, float goalSearchRadius = 30f)
        {
            result.Clear();
            LastRawPath.Clear(); LastPath.Clear();
            LastExpandedNodes = 0; LastPathCost = 0;
            if (!IsBuilt) return false;
            var timer = Stopwatch.StartNew();
            RefreshExpiredObstacles();

            // The jeep may stand inside the safety margin (e.g. stopped at a wall): use the nearest free cell.
            int startCell = NearestTraversable(start, 6f);
            // The player may be inside a building: drive to the nearest reachable spot outside.
            int goalCell = NearestTraversable(goal, goalSearchRadius);
            if (startCell < 0 || goalCell < 0)
            {
                LastSearchMilliseconds = timer.Elapsed.TotalMilliseconds;
                return false;
            }

            if (++stamp == int.MaxValue) { Array.Clear(closedStamp, 0, closedStamp.Length); Array.Clear(openStamp, 0, openStamp.Length); stamp = 1; }
            open.Clear();
            g[startCell] = 0; parent[startCell] = -1; openStamp[startCell] = stamp;
            open.Push(startCell, Heuristic(startCell, goalCell));
            int expanded = 0;
            bool found = false;

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closedStamp[current] == stamp) continue;   // stale heap entry
                closedStamp[current] = stamp;
                expanded++;
                if (current == goalCell) { found = true; break; }

                int cx = current % Width, cz = current / Width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InGrid(nx, nz)) continue;
                    int next = Index(nx, nz);
                    if (closedStamp[next] == stamp || !CanStep(current, next)) continue;
                    bool diagonal = k >= 4;
                    // No corner cutting: a diagonal move needs both side cells to be free.
                    if (diagonal && (!CanStep(current, Index(nx, cz)) || !CanStep(current, Index(cx, nz)))) continue;

                    float stepLength = diagonal ? cellSize * Sqrt2 : cellSize;
                    float tentative = g[current] + stepLength * (cellCost[current] + cellCost[next]) * .5f;
                    if (openStamp[next] != stamp || tentative < g[next])
                    {
                        openStamp[next] = stamp;
                        g[next] = tentative;
                        parent[next] = current;
                        open.Push(next, tentative + Heuristic(next, goalCell));
                    }
                }
            }

            LastExpandedNodes = expanded;
            if (!found)
            {
                LastSearchMilliseconds = timer.Elapsed.TotalMilliseconds;
                return false;
            }

            var cells = new List<int>();
            for (int c = goalCell; c >= 0; c = parent[c]) cells.Add(c);
            cells.Reverse();
            LastPathCost = g[goalCell];

            foreach (int c in cells) LastRawPath.Add(CellCenterOnGround(c));
            SmoothPath(cells, result);
            if (result.Count > 0)
            {
                // Begin at the jeep itself so the first segment is driven from where it stands.
                result[0] = new Vector3(start.x, result[0].y, start.z);
            }
            LastPath.AddRange(result);
            LastSearchMilliseconds = timer.Elapsed.TotalMilliseconds;
            return true;
        }

        /// <summary>Octile distance x cheapest cost per metre: never overestimates the true cost.</summary>
        float Heuristic(int a, int b)
        {
            int dx = Mathf.Abs(a % Width - b % Width), dz = Mathf.Abs(a / Width - b / Width);
            int diag = Mathf.Min(dx, dz), straight = Mathf.Max(dx, dz) - diag;
            return (diag * Sqrt2 + straight) * cellSize * MinCellCost;
        }

        bool CanStep(int a, int b) => !blocked[b] && !blocked[a] && Mathf.Abs(groundY[a] - groundY[b]) <= maxStepHeight;

        public bool IsTraversable(Vector3 world)
        {
            if (!IsBuilt || !WorldToCell(world, out int x, out int z)) return false;
            return !blocked[Index(x, z)];
        }

        /// <summary>Breadth-first ring search for the closest free cell (handles goals inside buildings).</summary>
        int NearestTraversable(Vector3 world, float radius)
        {
            WorldToCell(world, out int x, out int z);
            x = Mathf.Clamp(x, 0, Width - 1); z = Mathf.Clamp(z, 0, Depth - 1);
            if (!blocked[Index(x, z)]) return Index(x, z);
            int maxRing = Mathf.CeilToInt(radius / cellSize);
            Vector2 p = new Vector2(world.x, world.z);
            for (int r = 1; r <= maxRing; r++)
            {
                int best = -1; float bestD = float.PositiveInfinity;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // ring only
                        int cx = x + dx, cz = z + dz;
                        if (!InGrid(cx, cz)) continue;
                        int i = Index(cx, cz);
                        if (blocked[i]) continue;
                        Vector3 c = CellCenterFlat(cx, cz);
                        float d = (new Vector2(c.x, c.z) - p).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        // =====================================================================================
        // 5. Path smoothing (never increases cost, never enters a blocked cell)
        // =====================================================================================

        void SmoothPath(List<int> cells, List<Vector3> output)
        {
            int count = cells.Count;
            if (count == 0) return;
            // Cost of the raw A* path up to every cell, for fair comparison with shortcuts.
            var prefix = new float[count];
            for (int i = 1; i < count; i++)
            {
                int a = cells[i - 1], b = cells[i];
                bool diagonal = a % Width != b % Width && a / Width != b / Width;
                prefix[i] = prefix[i - 1] + (diagonal ? cellSize * Sqrt2 : cellSize) * (cellCost[a] + cellCost[b]) * .5f;
            }

            output.Add(CellCenterOnGround(cells[0]));
            int anchor = 0;
            while (anchor < count - 1)
            {
                int bestNext = anchor + 1;
                int limit = Mathf.Min(count - 1, anchor + 80);
                for (int j = anchor + 2; j <= limit; j++)
                {
                    float straight = LineCost(cells[anchor], cells[j]);
                    if (straight <= (prefix[j] - prefix[anchor]) * 1.001f + 1e-3f) bestNext = j;
                }
                output.Add(CellCenterOnGround(cells[bestNext]));
                anchor = bestNext;
            }
        }

        /// <summary>Cost of driving a straight line between two cells; infinity if it is not safe.</summary>
        float LineCost(int a, int b)
        {
            Vector3 pa = CellCenterFlat(a % Width, a / Width), pb = CellCenterFlat(b % Width, b / Width);
            float length = Vector2.Distance(new Vector2(pa.x, pa.z), new Vector2(pb.x, pb.z));
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / (cellSize * .25f)));
            float step = length / samples, total = 0f;
            int previous = a;
            for (int s = 1; s <= samples; s++)
            {
                Vector3 p = Vector3.Lerp(pa, pb, s / (float)samples);
                if (!WorldToCell(p, out int x, out int z)) return float.PositiveInfinity;
                int c = Index(x, z);
                if (c != previous && !CanStep(previous, c)) return float.PositiveInfinity;
                total += step * (cellCost[previous] + cellCost[c]) * .5f;
                previous = c;
            }
            return total;
        }

        // =====================================================================================
        // Helpers
        // =====================================================================================

        int Index(int x, int z) => z * Width + x;
        bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

        public bool WorldToCell(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt((p.x - origin.x) / cellSize);
            z = Mathf.FloorToInt((p.z - origin.z) / cellSize);
            return InGrid(x, z);
        }

        Vector3 CellCenterFlat(int x, int z) =>
            new Vector3(origin.x + (x + .5f) * cellSize, referenceY, origin.z + (z + .5f) * cellSize);

        Vector3 CellCenterOnGround(int i)
        {
            var c = CellCenterFlat(i % Width, i / Width);
            if (!float.IsNaN(groundY[i])) c.y = groundY[i];
            return c;
        }

        void OnDrawGizmosSelected()
        {
            if (drawPath && LastPath.Count > 1)
            {
                Gizmos.color = new Color(1f, .55f, 0f, .5f);
                for (int i = 1; i < LastRawPath.Count; i++)
                    Gizmos.DrawLine(LastRawPath[i - 1] + Vector3.up * .3f, LastRawPath[i] + Vector3.up * .3f);
                Gizmos.color = Color.green;
                for (int i = 1; i < LastPath.Count; i++)
                {
                    Gizmos.DrawLine(LastPath[i - 1] + Vector3.up * .5f, LastPath[i] + Vector3.up * .5f);
                    Gizmos.DrawSphere(LastPath[i] + Vector3.up * .5f, .35f);
                }
            }
            if (!drawBlockedCells || !IsBuilt) return;
            WorldToCell(transform.position, out int jx, out int jz);
            int r = Mathf.CeilToInt(debugDrawRadius / cellSize);
            var size = new Vector3(cellSize * .9f, .05f, cellSize * .9f);
            for (int z = Mathf.Max(0, jz - r); z <= Mathf.Min(Depth - 1, jz + r); z++)
                for (int x = Mathf.Max(0, jx - r); x <= Mathf.Min(Width - 1, jx + r); x++)
                {
                    int i = Index(x, z);
                    var c = CellCenterOnGround(i) + Vector3.up * .1f;
                    if (staticSolid[i]) Gizmos.color = new Color(1, 0, 0, .55f);
                    else if (blocked[i]) Gizmos.color = new Color(1, .5f, 0, .35f);
                    else if (road[i]) Gizmos.color = new Color(0, .6f, 1, .15f);
                    else continue;
                    Gizmos.DrawCube(c, size);
                }
        }

        /// <summary>Binary min-heap priority queue for the A* open list (lazy deletion).</summary>
        sealed class MinHeap
        {
            int[] items = new int[256];
            float[] keys = new float[256];
            public int Count { get; private set; }
            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == items.Length)
                {
                    Array.Resize(ref items, Count * 2);
                    Array.Resize(ref keys, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (keys[p] <= key) break;
                    items[i] = items[p]; keys[i] = keys[p]; i = p;
                }
                items[i] = item; keys[i] = key;
            }

            public int Pop()
            {
                int top = items[0];
                int lastItem = items[--Count];
                float lastKey = keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int m = r < Count && keys[r] < keys[l] ? r : l;
                    if (keys[m] >= lastKey) break;
                    items[i] = items[m]; keys[i] = keys[m]; i = m;
                }
                if (Count > 0) { items[i] = lastItem; keys[i] = lastKey; }
                return top;
            }
        }
    }
}


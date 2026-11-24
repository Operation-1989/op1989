using System.IO;
using System.Text;
using Operation1989.AI.Jeep;
using Operation1989.Interaction.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Editor tools for the drone-guided A* jeep pursuit (Intelligent Systems part).</summary>
public static class JeepAStarPursuitTools
{
    const string ReportFolder = "Temp/JeepPatrol";

    /// <summary>Adds the components permanently so their settings can be tuned in the Inspector.</summary>
    [MenuItem("Operation 1989/Jeep/Install A* Drone Pursuit")]
    static void Install()
    {
        var patrols = Object.FindObjectsByType<BaseRoadJeepPatrol>();
        if (patrols.Length == 0)
        {
            EditorUtility.DisplayDialog("A* Drone Pursuit", "No BaseRoadJeepPatrol found in the open scene.", "OK");
            return;
        }
        int added = 0;
        foreach (var patrol in patrols)
            if (!patrol.GetComponent<JeepAStarPursuit>()) { Undo.AddComponent<JeepAStarPursuit>(patrol.gameObject); added++; }

        foreach (var drone in Object.FindObjectsByType<Operation1989.AI.Drone.TacticalDroneController>())
        {
            if (!Object.FindAnyObjectByType<DroneAlertSystem>()) { Undo.AddComponent<DroneAlertSystem>(new GameObject("Drone Pursuit Alerts")); added++; }
            if (!drone.GetComponent<DronePlayerSurveillance>()) { Undo.AddComponent<DronePlayerSurveillance>(drone.gameObject); added++; }
        }
        EditorSceneManager.MarkSceneDirty(patrols[0].gameObject.scene);
        Debug.Log($"[Jeep A*] Installed {added} component(s). Save the scene to keep them.");
    }

    /// <summary>
    /// Builds the navigation grid in Edit mode and plans A* routes from the jeep to the player
    /// and to a set of sample points around the base, checking every route is collision free.
    /// </summary>
    [MenuItem("Operation 1989/Jeep/Validate A* Pursuit")]
    static void Validate()
    {
        var patrol = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
        if (!patrol) { Debug.LogError("[Jeep A*] No BaseRoadJeepPatrol in the open scene."); return; }

        var jeep = patrol.transform;
        var grid = jeep.GetComponent<JeepNavigationGrid>();
        bool temporary = !grid;
        if (temporary) grid = jeep.gameObject.AddComponent<JeepNavigationGrid>();

        var report = new StringBuilder();
        try
        {
            JeepAStarPursuit.ConfigureGrid(grid, patrol, jeep.GetComponent<BoxCollider>(), jeep);
            grid.Build(jeep, jeep.position.y, JeepAStarPursuit.PatrolBounds(patrol));
            report.AppendLine($"Grid {grid.Width}x{grid.Depth} @ {grid.cellSize:F2} m, blocked {grid.BlockedCellCount} " +
                              $"({100f * grid.BlockedCellCount / (grid.Width * grid.Depth):F1}%), build {grid.BuildMilliseconds:F0} ms");
            report.AppendLine($"Vehicle half width {grid.vehicleHalfWidth:F2} m + margin {grid.safetyMargin:F2} m; " +
                              $"road colliders {grid.roadSurfaces?.Length ?? 0}");

            int pass = 0, fail = 0;
            var path = new System.Collections.Generic.List<Vector3>();

            void Check(string label, Vector3 goal)
            {
                bool ok = grid.FindPath(jeep.position, goal, path, 30f);
                bool safe = ok;
                float length = 0;
                for (int i = 0; ok && i < grid.LastRawPath.Count; i++)
                    if (i > 0 && !grid.IsTraversable(grid.LastRawPath[i])) safe = false;
                for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
                if (ok && safe) pass++; else fail++;
                report.AppendLine($"{(ok && safe ? "PASS" : "FAIL")} {label}: goal {goal:F1} | cells {grid.LastRawPath.Count} → " +
                                  $"{path.Count} waypoints, {length:F1} m, cost {grid.LastPathCost:F1}, " +
                                  $"expanded {grid.LastExpandedNodes}, {grid.LastSearchMilliseconds:F2} ms");
            }

            var player = Object.FindAnyObjectByType<FirstPersonController>();
            if (player) Check("jeep → player", player.transform.position);

            // Sample goals: every 10th patrol waypoint plus a ring around the base centre.
            if (patrol.waypoints != null)
                for (int i = 0; i < patrol.waypoints.Length; i += 10)
                    if (patrol.waypoints[i]) Check($"jeep → route point {i}", patrol.waypoints[i].position);
            var c = new Vector3(grid.areaCenter.x, jeep.position.y, grid.areaCenter.y);
            for (int a = 0; a < 8; a++)
            {
                float ang = a * Mathf.PI / 4f;
                Check($"jeep → sample {a}", c + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * Mathf.Min(grid.areaSize.x, grid.areaSize.y) * .3f);
            }
            report.Insert(0, $"A* PURSUIT VALIDATION: {pass} passed, {fail} failed\n");

            // Leave the jeep → player route visible in the Scene view.
            if (player) grid.FindPath(jeep.position, player.transform.position, path, 30f);
            Selection.activeGameObject = jeep.gameObject;
            SceneView.RepaintAll();
        }
        finally
        {
            if (temporary) Object.DestroyImmediate(grid);
        }

        Directory.CreateDirectory(ReportFolder);
        File.WriteAllText(Path.Combine(ReportFolder, "AStarPursuitReport.txt"), report.ToString());
        Debug.Log("[Jeep A*] " + report);
    }
    [InitializeOnLoadMethod]
    static void RegisterAgentNavigationChecks() => EditorApplication.update += PollAgentNavigationChecks;

    static void PollAgentNavigationChecks()
    {
        const string request = "Temp/AgentCoordination/validate-editmode.request";
        const string preview = "Temp/AgentCoordination/preview-briefing.request";
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (ConsumeRequest(request)) ValidateAgentNavigation();
        if (ConsumeRequest(preview)) CaptureAgentBriefingFrames();
    }

    static bool ConsumeRequest(string path)
    {
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch (IOException) { return false; } // The writer may still be closing the file; retry next tick.
    }

    [MenuItem("Operation 1989/Intro/Preview Agent Briefing In Edit Mode")]
    static void CaptureAgentBriefingFrames()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string folder = "Temp/AgentBriefing";
        Directory.CreateDirectory(folder);
        var intro = Object.FindAnyObjectByType<IntroTruckSequence>();
        if (!intro || !intro.introCamera) { File.WriteAllText(folder + "/preview.txt", "Open army base to preview the briefing."); return; }
        var originalScene = EditorSceneManager.GetActiveScene();
        var previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(previewScene);
        var cameraObject = new GameObject("Temporary briefing preview camera");
        var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(intro.introCamera); camera.enabled = false;
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(originalScene);
        var target = new RenderTexture(960, 540, 24);
        var previousTexture = RenderTexture.active;
        var report = new StringBuilder("Agent briefing previews; Edit Mode only.\n");
        using (var briefing = new AgentBriefingCinematic())
        try
        {
            if (!briefing.Begin(intro, camera)) { report.AppendLine("No agents found in this scene."); return; }
            briefing.Tick(briefing.OpeningDuration + 1, false, intro.briefingMusicVolume);
            camera.targetTexture = target;
            for (int i = 0; i < briefing.ShotCount; i++)
            {
                briefing.ApplyCamera(); camera.Render(); RenderTexture.active = target;
                var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
                try
                {
                    pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                    File.WriteAllBytes($"{folder}/shot-{i + 1:00}.png", pixels.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(pixels); }
                report.AppendLine($"{i + 1}: {briefing.CurrentTitle} / camera {camera.transform.position:F2}");
                if (i < briefing.ShotCount - 1) briefing.Tick(briefing.ShotDuration, false, intro.briefingMusicVolume);
            }
        }
        catch (System.Exception error) { report.AppendLine(error.ToString()); }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previousTexture;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
            EditorSceneManager.CloseScene(previewScene, true);
            if (originalScene.IsValid() && originalScene.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(originalScene);
            File.WriteAllText(folder + "/preview.txt", report.ToString());
        }
    }

    [MenuItem("Operation 1989/Jeep/Validate Coordination Paths In Edit Mode")]
    static void ValidateAgentNavigation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Edit Mode only.");
        var scene = EditorSceneManager.GetActiveScene();
        var checkScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(checkScene);
        var root = new GameObject("Temporary agent navigation checks");
        var report = new StringBuilder();
        int passed = 0, failed = 0;
        void Check(string name, bool condition)
        {
            if (condition) passed++; else failed++;
            report.AppendLine((condition ? "PASS " : "FAIL ") + name);
        }
        GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root.transform); box.transform.position = position; box.transform.localScale = size;
            return box;
        }
        Vector3 center = new Vector3(5000, 0, 5000);
        void InitializeForCheck(MonoBehaviour component)
        {
            // SendMessage broadcasts to every component on the object, which
            // would also construct live rifle effects in this Edit Mode layout.
            component.GetType().GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?.Invoke(component, null);
        }
        try
        {
            // More than the initial 32 results verifies that performance buffers
            // grow instead of silently dropping obstacles in a dense scene.
            var denseRoot = new GameObject("Dense physics comparison"); denseRoot.transform.SetParent(root.transform);
            Vector3 denseOrigin = center + Vector3.up * 30 + Vector3.back * 2;
            for (int i = 0; i < 48; i++)
            {
                var item = Box("Dense obstacle " + i, center + Vector3.up * 30 + Vector3.forward * (i * .3f), Vector3.one * .2f);
                item.transform.SetParent(denseRoot.transform);
            }
            Physics.SyncTransforms();
            var query = new PhysicsQueryBuffer();
            bool SameHits(RaycastHit[] expected, int count)
            {
                if (expected.Length != count) return false;
                foreach (var hit in expected)
                {
                    bool found = false;
                    for (int i = 0; i < count; i++) found |= query.Hits[i].collider == hit.collider;
                    if (!found) return false;
                }
                return true;
            }
            bool SameOverlaps(Collider[] expected, int count)
            {
                if (expected.Length != count) return false;
                foreach (var collider in expected)
                {
                    bool found = false;
                    for (int i = 0; i < count; i++) found |= query.Colliders[i] == collider;
                    if (!found) return false;
                }
                return true;
            }
            Check("Buffered raycast retains all dense-scene hits", SameHits(Physics.RaycastAll(denseOrigin, Vector3.forward, 20, ~0, QueryTriggerInteraction.Ignore), query.Raycast(denseOrigin, Vector3.forward, 20)));
            Check("Buffered sphere cast matches all original hits", SameHits(Physics.SphereCastAll(denseOrigin, .1f, Vector3.forward, 20, ~0, QueryTriggerInteraction.Ignore), query.SphereCast(denseOrigin, .1f, Vector3.forward, 20)));
            Check("Buffered box cast matches all original hits", SameHits(Physics.BoxCastAll(denseOrigin, Vector3.one * .1f, Vector3.forward, Quaternion.identity, 20, ~0, QueryTriggerInteraction.Ignore), query.BoxCast(denseOrigin, Vector3.one * .1f, Vector3.forward, Quaternion.identity, 20)));
            Check("Buffered capsule cast matches all original hits", SameHits(Physics.CapsuleCastAll(denseOrigin - Vector3.up * .2f, denseOrigin + Vector3.up * .2f, .1f, Vector3.forward, 20, ~0, QueryTriggerInteraction.Ignore), query.CapsuleCast(denseOrigin - Vector3.up * .2f, denseOrigin + Vector3.up * .2f, .1f, Vector3.forward, 20)));
            Vector3 overlapCenter = denseOrigin + Vector3.forward * 9;
            Check("Buffered overlap box retains all dense-scene colliders", SameOverlaps(Physics.OverlapBox(overlapCenter, new Vector3(.5f, .5f, 10), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore), query.OverlapBox(overlapCenter, new Vector3(.5f, .5f, 10), Quaternion.identity)));
            Check("Buffered overlap sphere matches all original colliders", SameOverlaps(Physics.OverlapSphere(overlapCenter, 10, ~0, QueryTriggerInteraction.Ignore), query.OverlapSphere(overlapCenter, 10)));
            Check("Buffered overlap capsule matches all original colliders", SameOverlaps(Physics.OverlapCapsule(denseOrigin, denseOrigin + Vector3.forward * 18, .5f, ~0, QueryTriggerInteraction.Ignore), query.OverlapCapsule(denseOrigin, denseOrigin + Vector3.forward * 18, .5f)));
            query.Raycast(denseOrigin, Vector3.forward, 20);
            var retainedHits = query.Hits;
            long counterBefore = System.GC.GetAllocatedBytesForCurrentThread();
            var counterProbe = new byte[4096]; System.GC.KeepAlive(counterProbe);
            bool allocationCounterAvailable = System.GC.GetAllocatedBytesForCurrentThread() > counterBefore;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) query.Raycast(denseOrigin, Vector3.forward, 20);
            long bufferedBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) Physics.RaycastAll(denseOrigin, Vector3.forward, 20, ~0, QueryTriggerInteraction.Ignore);
            long originalBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check("Warmed physics queries retain their result storage without resizing", ReferenceEquals(retainedHits, query.Hits));
            if (allocationCounterAvailable)
            {
                report.AppendLine($"Dense raycast managed allocations / 200 calls: original={originalBytes} bytes, buffered={bufferedBytes} bytes.");
                Check("Warmed physics queries remove recurring result-array allocations", bufferedBytes < originalBytes / 10);
            }
            else report.AppendLine("Managed allocation byte counter is unavailable in this Unity runtime; buffer reuse was verified, no byte saving measured.");
            var self = new GameObject("Raycast owner"); self.transform.SetParent(denseRoot.transform);
            denseRoot.transform.GetChild(0).SetParent(self.transform);
            Check("Rifle still hits the nearest non-owner surface", RifleWeapon.Trace(denseOrigin, Vector3.forward, 20, self.transform, out var rifleHit) && rifleHit.collider.name == "Dense obstacle 1");
            Object.DestroyImmediate(denseRoot); Physics.SyncTransforms();

            var floor = Box("Test ground", center + Vector3.down * .5f, new Vector3(40, 1, 40));
            var owner = new GameObject("Test navigator"); owner.transform.SetParent(root.transform);
            var grid = owner.AddComponent<JeepNavigationGrid>();
            grid.autoBounds = false; grid.areaCenter = new Vector2(center.x, center.z); grid.areaSize = new Vector2(40, 40);
            grid.vehicleHalfWidth = 1.2f; grid.safetyMargin = .25f; grid.roadSurfaces = new[] { floor.GetComponent<Collider>() };
            grid.drawPath = false;
            var path = new System.Collections.Generic.List<Vector3>();
            Vector3 start = center + Vector3.left * 8, goal = center + Vector3.right * 8;
            bool SameSmoothingAsBefore()
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(JeepNavigationGrid);
                var cells = (System.Collections.Generic.List<int>)type.GetField("pathCells", flags).GetValue(grid);
                var costs = type.GetMethod("SearchCost", flags);
                var line = type.GetMethod("LineCost", flags);
                var point = type.GetMethod("CellCenterOnGround", flags);
                float Cost(int cell) => (float)costs.Invoke(grid, new object[] { cell });
                Vector3 Point(int cell) => (Vector3)point.Invoke(grid, new object[] { cell });
                var prefix = new float[cells.Count];
                for (int i = 1; i < cells.Count; i++)
                {
                    int a = cells[i - 1], b = cells[i];
                    bool diagonal = a % grid.Width != b % grid.Width && a / grid.Width != b / grid.Width;
                    prefix[i] = prefix[i - 1] + (diagonal ? grid.cellSize * 1.41421356f : grid.cellSize) * (Cost(a) + Cost(b)) * .5f;
                }
                var original = new System.Collections.Generic.List<Vector3> { Point(cells[0]) };
                int anchor = 0, originalChecks = 0;
                while (anchor < cells.Count - 1)
                {
                    int next = anchor + 1;
                    for (int j = anchor + 2; j <= Mathf.Min(cells.Count - 1, anchor + 80); j++)
                    {
                        originalChecks++;
                        float straight = (float)line.Invoke(grid, new object[] { cells[anchor], cells[j] });
                        if (straight <= (prefix[j] - prefix[anchor]) * 1.001f + 1e-3f) next = j;
                    }
                    original.Add(Point(cells[next])); anchor = next;
                }
                if (Vector3.Distance(start, original[0]) > .1f) original.Insert(0, start); else original[0] = start;
                report.AppendLine($"Jeep shortcut checks: previous={originalChecks}, optimized={grid.LastSmoothingChecks}.");
                if (original.Count != path.Count) return false;
                for (int i = 0; i < path.Count; i++) if (Vector3.Distance(path[i], original[i]) > .00001f) return false;
                return true;
            }
            bool SafePath()
            {
                for (int i = 1; i < path.Count; i++)
                {
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(path[i - 1], path[i]) * 10));
                    for (int j = 0; j <= steps; j++)
                        if (!grid.IsTraversable(Vector3.Lerp(path[i - 1], path[i], j / (float)steps))) return false;
                }
                return path.Count > 0;
            }
            grid.Build(owner.transform, 0);
            Check("Open ground returns shortest grid route", grid.FindPath(start, goal, path, 2, true) && Mathf.Abs(grid.LastPathCost - 16) < .01f && SafePath());
            Check("Faster open-road smoothing preserves the previous route exactly", SameSmoothingAsBefore());
            var wall = Box("Building across patrol route", center + Vector3.up, new Vector3(2, 2, 18));
            grid.Build(owner.transform, 0);
            bool detour = grid.FindPath(start, goal, path, 2, true);
            Check("Building forces a clearance-safe detour", detour && grid.LastPathCost > 20 && SafePath());
            Check("Faster detour smoothing preserves the previous route exactly", SameSmoothingAsBefore());
            Check("Weighted route still matches previous smoothing", grid.FindPath(start, goal, path, 2, false) && SameSmoothingAsBefore());
            const System.Reflection.BindingFlags privateFields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var solidCells = (bool[])typeof(JeepNavigationGrid).GetField("staticSolid", privateFields).GetValue(grid);
            var blockedCells = (bool[])typeof(JeepNavigationGrid).GetField("blocked", privateFields).GetValue(grid);
            float footprintDistance = grid.vehicleHalfWidth + grid.safetyMargin + grid.cellSize * .707107f;
            int footprintCells = Mathf.CeilToInt(footprintDistance / grid.cellSize);
            bool sameClearance = true;
            for (int z = 0; z < grid.Depth; z++)
                for (int x = 0; x < grid.Width; x++)
                {
                    bool originalBlocked = false;
                    for (int dz = -footprintCells; dz <= footprintCells && !originalBlocked; dz++)
                        for (int dx = -footprintCells; dx <= footprintCells && !originalBlocked; dx++)
                        {
                            if ((dx * dx + dz * dz) * grid.cellSize * grid.cellSize > footprintDistance * footprintDistance) continue;
                            originalBlocked = x + dx < 0 || x + dx >= grid.Width || z + dz < 0 || z + dz >= grid.Depth || solidCells[(z + dz) * grid.Width + x + dx];
                        }
                    sameClearance &= originalBlocked == blockedCells[z * grid.Width + x];
                }
            Check("Precomputed footprint preserves every blocked/free grid cell", sameClearance);
            Check("A start inside a solid wall cannot teleport to a route", !grid.FindPath(center, goal, path, 2, true));
            Object.DestroyImmediate(wall);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Enclosure side", goal + new Vector3(side * 4, 1, 0), new Vector3(1, 2, 9));
                Box("Enclosure end", goal + new Vector3(0, 1, side * 4), new Vector3(9, 2, 1));
            }
            grid.Build(owner.transform, 0);
            bool reachable = grid.FindPath(start, goal, path, 8, true);
            Check("An unreachable nearest goal falls back to a reachable exterior cell", reachable && SafePath() &&
                Vector3.Distance(path[path.Count - 1], goal) > 4 && Vector3.Distance(path[path.Count - 1], goal) <= 8.1f);
            Vector3 tracking = Operation1989.AI.Drone.TacticalDroneController.TrackingPoint(center + Vector3.up * 1.6f,
                Vector3.forward, 0, 12, 6);
            Check("Tracking target stays low and ahead, within a comfortable aiming angle", Mathf.Abs(tracking.y - 6) < .01f &&
                Vector3.Distance(new Vector3(tracking.x, 0, tracking.z), center) > 11.9f &&
                Vector3.Angle(Vector3.forward, tracking - (center + Vector3.up * 1.6f)) < 30);
            var tree = Box("TreeOld_NavigationTest", center + Vector3.up * 5, new Vector3(4, 10, 8));
            var player = new GameObject("Tree passage test player"); player.transform.SetParent(root.transform);
            player.transform.position = start + Vector3.up;
            var playerBody = player.AddComponent<CharacterController>();
            var other = new GameObject("Tree passage other actor"); other.transform.SetParent(root.transform);
            other.transform.position = start + new Vector3(0, 1, 3);
            var otherBody = other.AddComponent<CharacterController>();
            using (var passage = new PlayerTreePassage())
            {
                passage.Refresh(player.transform);
                var treeCollider = tree.GetComponent<Collider>();
                Check("Tree passage ignores only player pairs, retaining other actors' collision", Physics.GetIgnoreCollision(playerBody, treeCollider) &&
                    !Physics.GetIgnoreCollision(otherBody, treeCollider) && treeCollider.enabled && !treeCollider.isTrigger);
                Physics.SyncTransforms();
                Check("Player-passable tree remains visible to flight obstacle queries", Physics.Raycast(center + new Vector3(-10, 6, 0),
                    Vector3.right, out var treeHit, 20, ~0, QueryTriggerInteraction.Ignore) && treeHit.collider == treeCollider);
                var flight = owner.AddComponent<Operation1989.AI.Drone.TacticalDroneNavigationGraph>();
                bool flightPath = flight.FindFlightPath(center + new Vector3(-10, 6, 0), center + new Vector3(10, 6, 0), out var airPath);
                bool low = flightPath && airPath.Count > 2;
                foreach (var point in airPath) low &= Mathf.Abs(point.y - 6) < .01f;
                Check("Low drone route detours around a tree without climbing overhead", low);
                var originalAirPath = new System.Collections.Generic.List<Vector3>(airPath);
                var reusableAirPath = new System.Collections.Generic.List<Vector3>();
                bool reusableSame = flight.FindFlightPath(center + new Vector3(-10, 6, 0), center + new Vector3(10, 6, 0), reusableAirPath) && originalAirPath.Count == reusableAirPath.Count;
                for (int i = 0; i < Mathf.Min(originalAirPath.Count, reusableAirPath.Count); i++) reusableSame &= originalAirPath[i] == reusableAirPath[i];
                Check("Reused drone search workspace preserves the complete obstacle detour", reusableSame);
                var newTree = Box("TreeOld_SpawnedDuringGame", start + Vector3.up * 4, Vector3.one);
                passage.Refresh(player.transform);
                Check("Repeated tree scan discovers newly spawned foliage", Physics.GetIgnoreCollision(playerBody, newTree.GetComponent<Collider>()));
                Object.DestroyImmediate(newTree);
            }

            Quaternion facing = Quaternion.Euler(45, 20, 80);
            bool upright = true;
            for (int i = 0; i < 600; i++)
            {
                var velocity = i < 200 ? new Vector3(0, -8, 0) : i < 400 ? Vector3.right * 8 : Vector3.left * 8;
                facing = Operation1989.AI.Drone.TacticalDroneMovement.LevelFacing(facing, velocity, 1.5f);
                upright &= Vector3.Dot(facing * Vector3.up, Vector3.up) > .9999f;
            }
            Check("Drone stays upright through repeated descents and reversing turns", upright);

            // Verify the imported airframe too: a level root alone does not catch
            // the erroneous extra 90-degree rotation on the model child.
            var droneAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agents/TacticalDrone.prefab");
            var droneModel = Object.Instantiate(droneAsset, root.transform);
            droneModel.transform.position = center + Vector3.up * 60;
            Bounds modelBounds = default;
            bool firstMesh = true;
            foreach (var renderer in droneModel.GetComponentsInChildren<MeshRenderer>())
            {
                if (firstMesh) { modelBounds = renderer.bounds; firstMesh = false; }
                else modelBounds.Encapsulate(renderer.bounds);
            }
            report.AppendLine($"Imported drone airframe bounds: {modelBounds.size:F3}");
            Check("Actual drone prefab airframe lies horizontally", !firstMesh &&
                modelBounds.size.y < modelBounds.size.x && modelBounds.size.y < modelBounds.size.z);
            Object.DestroyImmediate(droneModel);

            var vehicle = new GameObject("Jeep independent sight test"); vehicle.transform.SetParent(root.transform);
            vehicle.transform.position = center + Vector3.forward * 35;
            var driver = vehicle.AddComponent<JeepAStarPursuit>();
            InitializeForCheck(driver);
            var entrance = new GameObject("Test main building entrance"); entrance.transform.SetParent(root.transform);
            entrance.transform.position = vehicle.transform.position + Vector3.right * 40;
            driver.mainBuildingEntrance = entrance.transform;
            var visiblePlayer = new GameObject("Visible test player"); visiblePlayer.transform.SetParent(root.transform);
            visiblePlayer.transform.position = vehicle.transform.position + Vector3.forward * 20 + Vector3.up * 1.25f;
            // A capsule represents the player's body in this Edit Mode physics scene.
            // CharacterController actors are initialized by the Play Mode player loop.
            var capsule = visiblePlayer.AddComponent<CapsuleCollider>(); capsule.center = Vector3.zero; capsule.height = 2; capsule.radius = .5f;
            // Match the army-base player's mid-body pivot, including its actual
            // CharacterController configuration. Use the capsule for Edit Mode rays.
            var characterBody = visiblePlayer.AddComponent<CharacterController>();
            characterBody.center = Vector3.zero; characterBody.height = 2; characterBody.radius = .5f; characterBody.enabled = false;
            Physics.SyncTransforms();
            Check("Jeep sees the actual player body with a mid-body pivot", driver.HasDirectSight(visiblePlayer.transform));
            var cover = Box("Jeep vision cover", vehicle.transform.position + new Vector3(0, 1.5f, 10), new Vector3(4, 3, 1));
            Physics.SyncTransforms();
            Check("Jeep cannot see the player through solid cover", !driver.HasDirectSight(visiblePlayer.transform));
            Object.DestroyImmediate(cover);
            visiblePlayer.transform.position = vehicle.transform.position + Vector3.back * 20;
            Physics.SyncTransforms();
            Check("Jeep respects its forward field of view at distance", !driver.HasDirectSight(visiblePlayer.transform));

            visiblePlayer.transform.position = vehicle.transform.position + Vector3.right * 20 + Vector3.up * 1.25f;
            driver.HandlePlayerSpotted(visiblePlayer.transform.position);
            driver.HandlePlayerLost(); // The drone is gone; no further reports arrive.
            Physics.SyncTransforms();
            bool reacquired = driver.RefreshVisualContact(visiblePlayer.transform);
            Check("Jeep reacquires the player after drone reports stop, including while broadside", reacquired &&
                driver.HasDirectContact && !driver.DroneHasPlayer && driver.CurrentState == JeepAStarPursuit.State.Pursuit);
            Vector3 toEntrance = entrance.transform.position - driver.TargetPosition;
            Vector3 blockLead = driver.InterceptionPosition - driver.TargetPosition;
            Check("Independent jeep sight chooses a blocking point towards the main entrance", blockLead.magnitude >= 3 &&
                Vector3.Dot(blockLead.normalized, toEntrance.normalized) > .99f && blockLead.magnitude < toEntrance.magnitude);
            visiblePlayer.transform.position += Vector3.forward * 3;
            Physics.SyncTransforms();
            Check("Jeep updates its interception target using its own sight after drone loss", driver.RefreshVisualContact(visiblePlayer.transform) &&
                Vector3.Distance(driver.TargetPosition, visiblePlayer.transform.position) < .01f);
            var sideCover = Box("Cover after drone loss", vehicle.transform.position + new Vector3(10, 1.5f, 1.5f), new Vector3(1, 3, 6));
            Physics.SyncTransforms();
            Check("Jeep still requires clear sight after the drone is gone", !driver.RefreshVisualContact(visiblePlayer.transform));
            Object.DestroyImmediate(sideCover);

            var jeepHealth = vehicle.GetComponent<JeepDamage>();
            jeepHealth.TakeDamage(25, vehicle.transform.position);
            Check("One rifle hit damages the jeep without disabling it", jeepHealth.Current == 125 && !jeepHealth.Disabled);
            for (int i = 0; i < 5; i++) jeepHealth.TakeDamage(25, vehicle.transform.position);
            float remaining = jeepHealth.RepairRemaining;
            jeepHealth.TakeDamage(25, vehicle.transform.position);
            Check("Six rifle hits disable the jeep and extra hits preserve its repair deadline", jeepHealth.Disabled &&
                Mathf.Abs(remaining - jeepHealth.RepairRemaining) < .01f);
            jeepHealth.TickRepair(Time.time + 29);
            Check("Disabled jeep remains stalled before thirty seconds", jeepHealth.Disabled);
            jeepHealth.TickRepair(Time.time + 30.1f);
            Check("Jeep fully repairs after thirty seconds", !jeepHealth.Disabled && jeepHealth.Current == jeepHealth.maximumHealth);

            var truck = Box("Intro Entrance Truck", center + Vector3.left * 35, new Vector3(3, 3, 7));
            truck.AddComponent<IntroTruckAudio>();
            typeof(Operation1989.Interaction.Environment.WorldInteractionBootstrap).GetMethod("DisablePassageCollider",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { truck.transform });
            Check("Entrance-named intro truck retains its solid collision", truck.GetComponent<Collider>().enabled && !truck.GetComponent<Collider>().isTrigger);

            var patrolObject = new GameObject("Patrol durability and theft reset check"); patrolObject.transform.SetParent(root.transform);
            Vector3 patrolSpawn = center + Vector3.back * 35;
            patrolObject.transform.position = patrolSpawn;
            var soldier = patrolObject.AddComponent<BarricadePatrolSoldier>();
            var soldierHealth = patrolObject.GetComponent<CombatHealth>();
            InitializeForCheck(soldierHealth);
            InitializeForCheck(soldier);
            for (int hit = 0; hit < 7; hit++) soldierHealth.TakeDamage(25);
            Check("Patrol soldier survives seven default rifle hits", soldierHealth.maximumHealth == 200 &&
                soldierHealth.Current == 25 && !soldierHealth.Dead);
            soldierHealth.TakeDamage(25);
            Check("The eighth hit kills and disables the patrol soldier", soldierHealth.Dead && !soldier.enabled &&
                !patrolObject.GetComponent<CharacterController>().enabled);
            patrolObject.transform.position += Vector3.right * 3;
            var missionObject = new GameObject("Theft patrol reset check"); missionObject.transform.SetParent(root.transform);
            var theft = missionObject.AddComponent<IntelTheftMission>();
            theft.SendMessage("RestorePatrolsAfterTheft", SendMessageOptions.DontRequireReceiver);
            Check("Stealing intel restores a dead patrol at its original post and resumes patrol", soldierHealth.Current == 200 &&
                !soldierHealth.Dead && soldier.enabled && soldier.State == "Patrol" && patrolObject.GetComponent<CharacterController>().enabled &&
                Vector3.Distance(patrolObject.transform.position, patrolSpawn) < .01f && Quaternion.Angle(patrolObject.transform.rotation, Quaternion.identity) < .1f);

            var sentryObject = new GameObject("Elite doorway facing check"); sentryObject.transform.SetParent(root.transform);
            sentryObject.transform.position = center + Vector3.left * 40;
            var sentry = sentryObject.AddComponent<EliteGuardController>();
            var watchedDoor = new GameObject("Fixed control room doorway"); watchedDoor.transform.SetParent(root.transform);
            watchedDoor.transform.position = sentry.transform.position + Vector3.right * 5;
            sentry.doorwayToWatch = watchedDoor.transform;
            InitializeForCheck(sentry.GetComponent<CombatHealth>());
            InitializeForCheck(sentry.GetComponent<EliteMovement>());
            InitializeForCheck(sentry);
            Check("Elite immediately faces its control room doorway", sentry.WatchesDoorway &&
                Vector3.Dot(sentry.transform.forward, Vector3.right) > .999f &&
                Vector3.Distance(sentry.DoorwayLookPoint, watchedDoor.transform.position) < .01f);

            vehicle.AddComponent<BaseRoadJeepPatrol>();
            var briefingDrone = Object.Instantiate(droneAsset, root.transform);
            briefingDrone.transform.position = center + new Vector3(0, 8, -50);
            var previewCameraObject = new GameObject("Briefing check camera"); previewCameraObject.transform.SetParent(root.transform);
            var briefingCamera = previewCameraObject.AddComponent<Camera>(); briefingCamera.enabled = false;
            var introObject = new GameObject("Agent briefing check"); introObject.transform.SetParent(root.transform);
            var briefingIntro = introObject.AddComponent<IntroTruckSequence>();
            briefingIntro.introCamera = briefingCamera; briefingIntro.briefingShotSeconds = 8;
            briefingIntro.briefingOpeningSeconds = 4; briefingIntro.intelMission = theft;
            var extractionObject = Box("Briefing extraction helicopter", center + Vector3.forward * 65, new Vector3(5, 3, 10));
            var briefingOutro = extractionObject.AddComponent<HelicopterOutroSequence>();
            briefingOutro.helicopter = extractionObject.transform;
            briefingOutro.outroMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Ambiance/OutroMusic.mp3");
            theft.outro = briefingOutro;
            float originalNear = briefingCamera.nearClipPlane, originalFov = briefingCamera.fieldOfView;
            using (var briefing = new AgentBriefingCinematic())
            {
                Check("Briefing opens with Operation 1989 before the longer agent sequence", briefing.Begin(briefingIntro) &&
                    briefing.ShotCount == 5 && briefing.Duration == 44 && briefing.ShowingOpening && briefing.CurrentTitle == "OPERATION 1989");
                Check("Briefing reuses the provided outro music", briefing.MusicClip && briefing.MusicClip == briefingOutro.outroMusic);
                briefing.Tick(briefing.OpeningDuration, false, .35f); briefing.ApplyCamera();
                Check("Title opening leads into the drone introduction", !briefing.ShowingOpening && briefing.CurrentTitle == "RECON DRONE");
                string[] expectedShots = { "PATROL JEEP", "PATROL SOLDIER", "ELITE SOLDIER", "STEAL THE SECRET. GET OUT." };
                bool order = true;
                for (int expectedIndex = 0; expectedIndex < expectedShots.Length; expectedIndex++)
                {
                    string expectedShot = expectedShots[expectedIndex];
                    briefing.Tick(briefing.ShotDuration, false, .35f); briefing.ApplyCamera();
                    order &= briefing.CurrentTitle == expectedShot && briefing.Active;
                }
                Check("Briefing advances from drone through jeep, patrol, elite and objective", order);
                Check("Natural briefing completion signals a single truck handoff and restores the camera", briefing.Tick(briefing.ShotDuration, false, .35f) &&
                    !briefing.Active && !briefing.Tick(0, false, .35f) && Mathf.Abs(briefingCamera.nearClipPlane - originalNear) < .001f &&
                    Mathf.Abs(briefingCamera.fieldOfView - originalFov) < .001f);
                briefing.Begin(briefingIntro);
                Check("Space skips the title opening and only signals completion once", briefing.Tick(.1f, true, .35f) &&
                    !briefing.Active && !briefing.Tick(.1f, true, .35f));
                briefing.Begin(briefingIntro); briefing.Tick(briefing.OpeningDuration + 1, false, .35f);
                Check("Space also skips from an agent shot into the truck sequence", briefing.Tick(.1f, true, .35f) && !briefing.Active);

                void CheckStableObserver(Transform subject, int shotIndex, string label)
                {
                    briefing.Begin(briefingIntro);
                    briefing.Tick(briefing.OpeningDuration + shotIndex * briefing.ShotDuration, false, .35f);
                    briefing.ApplyCamera();
                    Vector3 anchor = briefingCamera.transform.position, originalPosition = subject.position;
                    Quaternion originalAim = briefingCamera.transform.rotation;
                    float originalZoom = briefingCamera.fieldOfView, largestAimChange = 0, largestZoomChange = 0;
                    Vector3 jitterAxis = briefingCamera.transform.right;
                    try
                    {
                        for (int frame = 0; frame < 60; frame++)
                        {
                            subject.position = originalPosition + jitterAxis * (frame % 2 == 0 ? .2f : -.2f);
                            briefing.Tick(1f / 60, false, .35f); briefing.ApplyCamera();
                            largestAimChange = Mathf.Max(largestAimChange, Quaternion.Angle(originalAim, briefingCamera.transform.rotation));
                            largestZoomChange = Mathf.Max(largestZoomChange, Mathf.Abs(originalZoom - briefingCamera.fieldOfView));
                        }
                        Check(label + " filters rapid subject jitter without zoom pumping", largestAimChange < .3f && largestZoomChange < .1f);

                        subject.position = originalPosition + jitterAxis * 8;
                        briefing.Tick(1f / 60, false, .35f); briefing.ApplyCamera();
                        Quaternion initialPan = briefingCamera.transform.rotation;
                        Check(label + " pans gradually instead of snapping to movement", Quaternion.Angle(originalAim, initialPan) < 1);
                        briefing.ApplyCamera();
                        Check(label + " does not advance damping twice in the same frame", Quaternion.Angle(initialPan, briefingCamera.transform.rotation) < .001f);
                        for (int frame = 0; frame < 120; frame++)
                        { briefing.Tick(1f / 60, false, .35f); briefing.ApplyCamera(); }
                        Vector3 inView = briefingCamera.WorldToViewportPoint(subject.position + Vector3.up * (shotIndex == 2 ? 1 : 0));
                        Check(label + " stays stationary and tracks sustained travel into view",
                            Vector3.Distance(anchor, briefingCamera.transform.position) < .001f && Quaternion.Angle(originalAim, briefingCamera.transform.rotation) > 1 &&
                            inView.z > 0 && inView.x > 0 && inView.x < 1 && inView.y > 0 && inView.y < 1);
                    }
                    finally { subject.position = originalPosition; Physics.SyncTransforms(); }
                }
                CheckStableObserver(briefingDrone.transform, 0, "Drone camera");
                CheckStableObserver(patrolObject.transform, 2, "Patrol camera");
            }

            bool streamingUnchanged = true;
            long decodedBytes = 0;
            foreach (string audioPath in new[] { "Vehicles/DroneFlying", "Vehicles/JeepDriving", "Vehicles/TruckMoving", "Ambiance/ControlRoomChatter", "Ambiance/OutroMusic" })
            {
                string assetPath = "Assets/Audio/SFX/" + audioPath + ".mp3";
                var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                streamingUnchanged &= importer && importer.defaultSampleSettings.loadType == AudioClipLoadType.Streaming &&
                    importer.defaultSampleSettings.quality == 1 && importer.defaultSampleSettings.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate && !importer.forceToMono;
                if (clip) decodedBytes += (long)clip.samples * clip.channels * sizeof(float);
            }
            Check("Long recordings stream with original quality, sample rate and stereo", streamingUnchanged);
            report.AppendLine($"Full float PCM size of the five streamed recordings: {decodedBytes / (1024.0 * 1024):F1} MiB (not an observed total RAM saving).");

            float savedScale = Time.timeScale;
            bool savedAudioPause = AudioListener.pause, savedCursorVisible = Cursor.visible;
            CursorLockMode savedCursorLock = Cursor.lockState;
            var pauseObject = new GameObject("Pause menu check"); pauseObject.transform.SetParent(root.transform);
            var pauseMenu = pauseObject.AddComponent<GamePauseMenu>();
            try
            {
                InitializeForCheck(pauseMenu);
                Time.timeScale = .75f; AudioListener.pause = false;
                var oldCursorLock = Cursor.lockState; bool oldCursorVisible = Cursor.visible;
                using (var briefing = new AgentBriefingCinematic())
                {
                    briefing.Begin(briefingIntro);
                    briefing.Tick(briefing.OpeningDuration + 1, false, .35f); briefing.ApplyCamera();
                    string pausedTitle = briefing.CurrentTitle;
                    Vector3 pausedCamera = briefingCamera.transform.position;
                    pauseMenu.Pause();
                    float pausedClock = GamePauseMenu.UnscaledTime;
                    Check("Pause stops simulation and audio and releases the cursor", GamePauseMenu.IsPaused && GamePauseMenu.BlocksInput &&
                        Time.timeScale == 0 && AudioListener.pause && Cursor.lockState == CursorLockMode.None && Cursor.visible);
                    Check("Paused briefing ignores elapsed time and skip input", !briefing.Tick(100, true, .35f) && briefing.Active &&
                        briefing.CurrentTitle == pausedTitle && GamePauseMenu.UnscaledTime == pausedClock);
                    briefing.ApplyCamera();
                    Check("Paused briefing camera holds its composition", Vector3.Distance(pausedCamera, briefingCamera.transform.position) < .00001f);
                    var rifle = patrolObject.GetComponent<RifleWeapon>();
                    int shotsBeforePause = rifle.ShotsFired;
                    rifle.Shoot(center, Vector3.forward);
                    Check("Pause menu clicks cannot fire a rifle", rifle.ShotsFired == shotsBeforePause);
                    var deadPlayerObject = new GameObject("Paused respawn check"); deadPlayerObject.transform.SetParent(root.transform);
                    var deadPlayerHealth = deadPlayerObject.AddComponent<CombatHealth>(); deadPlayerHealth.isPlayer = true;
                    // Current defaults to zero until Awake; an expired respawn must still be held.
                    deadPlayerHealth.GetType().GetMethod("Update", privateFields).Invoke(deadPlayerHealth, null);
                    Check("Pause prevents an expired death timer from respawning the player", deadPlayerHealth.Dead);
                    pauseMenu.Pause(); // Repeated pause must not overwrite the original globals.
                    pauseMenu.Resume();
                    Check("Resume restores the previous speed, audio and cursor state", !GamePauseMenu.IsPaused && Time.timeScale == .75f &&
                        !AudioListener.pause && Cursor.lockState == oldCursorLock && Cursor.visible == oldCursorVisible);
                    Check("Resume consumes its input frame to prevent click-through", GamePauseMenu.BlocksInput);
                }
                Check("Restart refuses an unsaved test scene instead of loading a checkpoint", !pauseMenu.CanRestart &&
                    !pauseMenu.RestartFromBeginning() && EditorSceneManager.GetActiveScene() == checkScene);
                pauseMenu.Pause();
                pauseMenu.enabled = false;
                // Edit Mode does not run the component lifecycle automatically.
                typeof(GamePauseMenu).GetMethod("OnDisable", privateFields).Invoke(pauseMenu, null);
                Check("Disabling the pause menu releases its global pause state", !GamePauseMenu.IsPaused && Time.timeScale == .75f && !AudioListener.pause);
            }
            finally
            {
                pauseMenu.Resume();
                typeof(GamePauseMenu).GetMethod("ResetSession", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
                Time.timeScale = savedScale; AudioListener.pause = savedAudioPause;
                Cursor.lockState = savedCursorLock; Cursor.visible = savedCursorVisible;
            }

        }
        catch (System.Exception error) { failed++; report.AppendLine(error.ToString()); }
        finally
        {
            Object.DestroyImmediate(root); Physics.SyncTransforms();
            EditorSceneManager.CloseScene(checkScene, true);
            if (scene.IsValid() && scene.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            Directory.CreateDirectory("Temp/AgentCoordination");
            report.Insert(0, $"Edit Mode navigation checks: {passed} passed; {failed} failed. No Play Mode entered.\n");
            File.WriteAllText("Temp/AgentCoordination/editmode-validation.txt", report.ToString());
        }
    }

}


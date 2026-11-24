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

        foreach (var drone in Object.FindObjectsByType<DroneMapPatrol>())
        {
            if (!Object.FindAnyObjectByType<DroneAlertSystem>()) { Undo.AddComponent<DroneAlertSystem>(drone.gameObject); added++; }
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
}


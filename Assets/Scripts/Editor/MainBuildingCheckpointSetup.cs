using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MainBuildingCheckpointSetup
{
    public const string Work = "Temp/BuildingCheckpoint";
    [InitializeOnLoadMethod]
    static void Resume() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    static void Poll()
    {
        string path = Work + "/request.txt";
        if (!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "install") Install();
            else if (command == "validate") MainBuildingCheckpointValidation.Run();
            else throw new InvalidOperationException("Unknown checkpoint command: " + command);
        }
        catch (Exception e) { Directory.CreateDirectory(Work); File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Operation 1989/Combat/Install Main Building Entrance Checkpoint")]
    public static void Install()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        var intro = Object.FindAnyObjectByType<IntroTruckSequence>();
        var building = GameObject.Find("building_1"); var floor = GameObject.Find("w");
        if (!intro || !intro.player || !building || !floor || !floor.GetComponent<Renderer>()) throw new InvalidOperationException("Main building, floor, and player are required.");
        var door = building.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "door_middle");
        var arch = building.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "arch_middle");
        var health = intro.player.GetComponent<CombatHealth>(); var body = intro.player.GetComponent<CharacterController>();
        if (!door || !arch || !health || !health.isPlayer || !body) throw new InvalidOperationException("Authored entrance geometry and player collision are required.");
        Bounds doors = door.bounds; float floorY = floor.GetComponent<Renderer>().bounds.max.y;
        bool zAxis = doors.size.z >= doors.size.x;
        float length = zAxis ? doors.size.z : doors.size.x;
        if (length < 4) throw new InvalidOperationException("Cannot identify the paired main-building entrance planes.");
        float gateSide = zAxis ? intro.entryPlanePoint.z : intro.entryPlanePoint.x;
        float middle = zAxis ? doors.center.z : doors.center.x;
        Vector3 inward = (zAxis ? Vector3.forward : Vector3.right) * (gateSide < middle ? 1 : -1);
        Vector3 center = doors.center;
        if (zAxis) center.x = arch.bounds.center.x; else center.z = arch.bounds.center.z;
        center.y = floorY + 1.4f;
        Vector3 front = center - inward * (length * .5f);
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        float bottom = body.center.y * body.transform.lossyScale.y - body.height * body.transform.lossyScale.y * .5f;
        Vector3 position = default; bool found = false;
        foreach (float depth in new[] { 2.5f, 3.25f, 4f })
        {
            if (depth >= length - .6f) continue;
            foreach (float offset in new[] { 0f, -.6f, .6f, -1f, 1f })
            {
                Vector3 p = front + inward * depth + side * offset; p.y = floorY - bottom + .04f;
                if (!ClearPlayerSpace(p, body)) continue;
                if (!RifleWeapon.Trace(p + Vector3.up * .5f, Vector3.down, body.height + 2, body.transform, out var ground) ||
                    ground.normal.y < .6f || Mathf.Abs(ground.point.y - floorY) > .2f) continue;
                position = p; found = true; break;
            }
            if (found) break;
        }
        if (!found) throw new InvalidOperationException("No supported, player-sized respawn position is clear just inside the entrance.");
        Directory.CreateDirectory(Work); var scene = SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, Work + "/before-install.unity", true);
        var root = GameObject.Find("Main Building Entrance Checkpoint");
        if (!root) { root = new GameObject("Main Building Entrance Checkpoint"); Undo.RegisterCreatedObjectUndo(root, "Create building checkpoint"); }
        Undo.RegisterFullObjectHierarchyUndo(root, "Configure building checkpoint");
        root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(inward));
        var volume = root.GetComponent<BoxCollider>(); if (!volume) volume = Undo.AddComponent<BoxCollider>(root);
        volume.center = Vector3.zero; volume.size = new Vector3(3.4f, 2.8f, length - 1.2f); volume.isTrigger = true;
        var checkpoint = root.GetComponent<PlayerCheckpoint>(); if (!checkpoint) checkpoint = Undo.AddComponent<PlayerCheckpoint>(root);
        var marker = root.transform.Find("Respawn Point");
        if (!marker) { marker = new GameObject("Respawn Point").transform; marker.SetParent(root.transform); }
        marker.SetPositionAndRotation(position, Quaternion.LookRotation(inward));
        checkpoint.player = health; checkpoint.activationVolume = volume; checkpoint.respawnPoint = marker;
        checkpoint.checkpointName = "Main building entrance"; checkpoint.priority = 10;
        if (!health.GetComponent<PlayerCheckpointState>()) Undo.AddComponent<PlayerCheckpointState>(health.gameObject);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Work + "/installation.txt", $"Door planes: {doors}\nActivation volume: {volume.bounds}\nRespawn: {position}\nFacing inward: {inward}\nPASS Supported player clearance; activation begins 0.6m inside either main doorway.\n");
    }

    public static bool ClearPlayerSpace(Vector3 rootPosition, CharacterController body)
    {
        float radius = body.radius * Mathf.Max(body.transform.lossyScale.x, body.transform.lossyScale.z);
        float height = body.height * body.transform.lossyScale.y;
        Vector3 center = rootPosition + body.transform.TransformVector(body.center);
        float axis = Mathf.Max(0, height * .5f - radius);
        foreach (var c in Physics.OverlapCapsule(center - Vector3.up * axis, center + Vector3.up * axis, radius, ~0, QueryTriggerInteraction.Ignore))
            if (c.transform != body.transform && !c.transform.IsChildOf(body.transform)) return false;
        return true;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class SceneClearanceFix
{
    const string Work = "Temp/SceneClearance";
    [InitializeOnLoadMethod]
    static void Initialize()
    {
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/ready.txt", "2");
        EditorApplication.update += Poll;
        if (SessionState.GetBool("HangarClearanceSmoke", false)) EditorApplication.update += SmokeTick;
    }
    static void Poll()
    {
        string path = Work + "/request.txt";
        if (!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
        string command = File.ReadAllText(path).Trim();
        File.Delete(path);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "apply") Apply();
            else if (command == "smoke")
            {
                SessionState.SetBool("HangarClearanceBackground", Application.runInBackground);
                SessionState.SetBool("HangarClearanceSmoke", true);
                EditorApplication.update += SmokeTick;
                EditorApplication.isPaused = false;
                EditorApplication.isPlaying = true;
            }
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }
    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }
    static Bounds WestRoad()
    {
        var roads = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .Where(g => g.name.StartsWith("Straight_Road") && g.transform.position.x < -72).ToArray();
        if (roads.Length == 0) throw new InvalidOperationException("West patrol road not found.");
        var bounds = BoundsOf(roads[0]);
        foreach (var road in roads) bounds.Encapsulate(BoundsOf(road));
        return bounds;
    }
    static void Inspect()
    {
        var report = new StringBuilder();
        report.AppendLine("WEST ROAD " + WestRoad());
        report.AppendLine("HANGAR " + BoundsOf(GameObject.Find("NATO_Shelter_V1")));
        foreach (var box in Object.FindObjectsByType<BoxCollider>().Where(b => b.name == "Room neon bloom volume"))
            report.AppendLine($"ROOM EFFECT BOX {box.name} worldBounds={box.bounds}");
        foreach (var r in Object.FindObjectsByType<Renderer>().Where(r => r.name.Contains("Ceiling channel") || r.name.Contains("Cyan ceiling inset")))
            report.AppendLine($"CEILING {r.transform.parent.name}/{r.name} {r.bounds}");
        foreach (var r in Object.FindObjectsByType<Renderer>().Where(r => r.bounds.center.z > 15 && r.bounds.center.z < 33 && r.bounds.center.x > -20 && r.bounds.center.x < 20 && r.bounds.size.y < .6f && (r.bounds.min.x < -10.5f || r.bounds.max.x > 15)))
            report.AppendLine($"EXTERIOR OVERHANG {r.name} bounds={r.bounds} materials={string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name))}");
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
        Previews("before");
    }
    [MenuItem("Operation 1989/Environment/Fix Room Effects and Hangar Clearance")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        if (!File.Exists(Work + "/before-clearance.unity")) EditorSceneManager.SaveScene(scene, Work + "/before-clearance.unity", true);
        FixGeometryAndEffects();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        var hangar = BoundsOf(GameObject.Find("NATO_Shelter_V1"));
        float gap = hangar.min.x - WestRoad().max.x;
        var report = new StringBuilder();
        report.AppendLine($"{(gap >= 1.5f ? "PASS" : "FAIL")} Hangar completely outside west carriageway; verge gap={gap:F2}m");
        report.AppendLine($"{(!Object.FindObjectsByType<Volume>().Any(v => v.name == "Room neon bloom volume") ? "PASS" : "FAIL")} Both protruding room effect boxes removed");
        bool noStudioFloors = !Object.FindObjectsByType<Renderer>().Any(r => r.isVisible && r.transform.root.name.StartsWith("server") && r.sharedMaterials.Any(m => m && m.name == "Studio floor material"));
        report.AppendLine($"{(noStudioFloors ? "PASS" : "FAIL")} Imported server presentation floors removed");
        foreach (var r in Object.FindObjectsByType<Renderer>().Where(r => r.name.Contains("Ceiling channel") || r.name.Contains("Cyan ceiling inset")))
            report.AppendLine($"{(r.bounds.min.x >= -10.25f && r.bounds.max.x <= 14.5f ? "PASS" : "FAIL")} Interior rail contained: {r.transform.parent.name}/{r.name}, x={r.bounds.min.x:F2}..{r.bounds.max.x:F2}");
        File.WriteAllText(Work + "/validation.txt", report.ToString());
        Previews("after");
    }
    // Shared by the lighting authoring command so rebuilding it cannot restore these obstructions.
    public static void FixGeometryAndEffects()
    {
        foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include).Where(v => v.name == "Room neon bloom volume").ToArray())
            Undo.DestroyObjectImmediate(volume.gameObject);
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include).Where(r => r.transform.root.name.StartsWith("server") && r.sharedMaterials.Any(m => m && m.name == "Studio floor material")))
        {
            Undo.RecordObject(r.gameObject, "Remove server presentation floor");
            r.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);
        }
        foreach (var t in Object.FindObjectsByType<Transform>().Where(t => t.name == "Violet equipment trim fixture light" && t.position.x > 0))
        {
            var position = t.position;
            Move(t, Vector3.right * (13.6f - position.x));
        }
        foreach (var r in Object.FindObjectsByType<Renderer>().Where(r => r.name.Contains("Ceiling channel") || r.name.Contains("Cyan ceiling inset")))
        {
            Undo.RecordObject(r.transform, "Keep room rail inside building");
            var scale = r.transform.localScale;
            scale.x = r.name.Contains("Ceiling channel") ? 3.6f : 3.4f;
            r.transform.localScale = scale;
        }
        var hangar = GameObject.Find("NATO_Shelter_V1");
        if (!hangar) throw new InvalidOperationException("Hangar missing.");
        float shift = Mathf.Max(0, Mathf.Ceil(WestRoad().max.x + 1.5f - BoundsOf(hangar).min.x));
        if (shift > 0)
        {
            var movement = Vector3.right * shift;
            Move(hangar.transform, movement);
            var overhead = GameObject.Find("Hangar overhead lighting");
            if (overhead) Move(overhead.transform, movement);
            foreach (var t in Object.FindObjectsByType<Transform>().Where(t => t.name.StartsWith("NATO_Shelter_V1 ") && t.name.EndsWith("wall light"))) Move(t, movement);
            File.WriteAllText(Work + "/hangar-movement.txt", $"Moved hangar and its fixtures east by {shift:F1}m; patrol road and route retained.\n");
        }
        var b = BoundsOf(hangar);
        float roadEdge = WestRoad().max.x;
        foreach (var lamp in Object.FindObjectsByType<Transform>().Where(t => t.name == "Warm path lamp" && t.position.z >= b.min.z - .6f && t.position.z <= b.max.z + .6f && t.position.x > roadEdge && t.position.x < b.min.x + 1))
            Move(lamp, Vector3.right * ((roadEdge + b.min.x) * .5f - lamp.position.x));
        Physics.SyncTransforms();
    }
    static void Move(Transform t, Vector3 delta)
    {
        Undo.RecordObject(t, "Clear west jeep road");
        t.position += delta;
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }
    static void SmokeTick()
    {
        if (!Application.isPlaying || Time.time < .1f) return;
        Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();
        var jeep = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
        if (!jeep) return;
        bool passed = jeep.transform.position.x < -72 && jeep.transform.position.z > 28;
        if (!passed && Time.time < 60) return;
        EditorApplication.update -= SmokeTick;
        SessionState.SetBool("HangarClearanceSmoke", false);
        try
        {
            File.WriteAllText(Work + "/playmode.txt", $"{(passed ? "PASS" : "FAIL")} Jeep drives behind and beyond hangar on its normal patrol\nTime={Time.time:F1}s position={jeep.transform.position:F2} distance={jeep.DistanceTravelled:F1}m blockedBy={jeep.BlockedBy}\n");
            Capture(new Vector3(-84, 4, jeep.transform.position.z - 10), jeep.transform.position + Vector3.up, "jeep-passing-hangar");
        }
        finally
        {
            Application.runInBackground = SessionState.GetBool("HangarClearanceBackground", false);
            EditorApplication.isPlaying = false;
        }
    }
    static void Previews(string suffix)
    {
        Capture(new Vector3(-84, 7, -20), new Vector3(-71, 2, 8), "hangar-road-" + suffix);
        Capture(new Vector3(-20, 5, 18), new Vector3(-8, 2.5f, 24), "building-west-" + suffix);
        Capture(new Vector3(23, 5, 26), new Vector3(11, 2.5f, 23), "building-east-" + suffix);
    }
    static void Capture(Vector3 position, Vector3 target, string name)
    {
        var go = new GameObject("Clearance inspection camera");
        var camera = go.AddComponent<Camera>();
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        go.transform.position = position; go.transform.LookAt(target); camera.nearClipPlane = .05f;
        var rt = new RenderTexture(1100, 750, 24);
        var prior = RenderTexture.active;
        var texture = new Texture2D(1100, 750, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1100, 750), 0, 0); texture.Apply();
            File.WriteAllBytes(Work + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally { RenderTexture.active = prior; camera.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(go); }
    }
}

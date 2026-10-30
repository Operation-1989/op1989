using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class IntelMissionRefinement
{
    const string Work = "Temp/IntelTheft";
    [InitializeOnLoadMethod]
    static void Resume() { if (SessionState.GetBool("ReviseIntelMission", false)) EditorApplication.update += Pending; }
    [MenuItem("Operation 1989/Intel/Apply Wall Beacons and Right Computer")]
    public static void Schedule()
    {
        SessionState.SetBool("ReviseIntelMission", true);
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorApplication.update -= Pending; EditorApplication.update += Pending;
    }
    static void Pending()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= Pending; SessionState.SetBool("ReviseIntelMission", false);
        try { Apply(); } catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }
    static Material Material(string name, Color color, bool glass = false, bool unlit = false)
    {
        string path = "Assets/IntelTheft/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        if (!unlit)
        {
            material.SetFloat("_Smoothness", glass ? 0.8f : 0.55f);
            if (glass)
            {
                material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black);
                material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
        }
        EditorUtility.SetDirty(material); return material;
    }
    static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
    public static void Apply()
    {
        var mission = Object.FindAnyObjectByType<IntelTheftMission>();
        var computer = GameObject.Find("ScifiDesk");
        if (!mission || !computer || Application.isPlaying) throw new InvalidOperationException("Open the army base scene in Edit mode.");
        Directory.CreateDirectory(Work);
        if (!File.Exists(Work + "/before-wall-beacons.unity")) EditorSceneManager.SaveScene(mission.gameObject.scene, Work + "/before-wall-beacons.unity", true);
        Undo.SetCurrentGroupName("Refine intel mission guidance and emergency lights");
        int undo = Undo.GetCurrentGroup();
        var metal = Material("Beacon brushed metal", new Color(0.2f, 0.22f, 0.23f));
        var mirror = Material("Beacon reflector", new Color(0.7f, 0.72f, 0.75f));
        var glass = Material("Rotating beacon red glass", new Color(0.8f, 0.015f, 0.008f, 0.42f), true);
        var outline = Material("Computer target outline", MissionHudStyle.Accent, false, true);
        var graphite = AssetDatabase.LoadAssetAtPath<Material>("Assets/IntelTheft/Terminal graphite.mat");
        var plug = AssetDatabase.LoadAssetAtPath<Material>("Assets/IntelTheft/USB connector.mat");

        var old = mission.terminal;
        var terminal = computer.GetComponent<IntelTerminal>();
        if (!terminal) terminal = Undo.AddComponent<IntelTerminal>(computer);
        Undo.RecordObject(mission, "Use the right computer"); Undo.RecordObject(terminal, "Configure computer interaction");
        mission.terminal = terminal; terminal.mission = mission; terminal.display = null;
        terminal.highlightTarget = computer.GetComponent<MeshRenderer>(); terminal.outlineMaterial = outline;
        var hitbox = computer.GetComponent<BoxCollider>();
        if (!hitbox) hitbox = Undo.AddComponent<BoxCollider>(computer);
        Undo.RecordObject(hitbox, "Fit computer interaction hitbox");
        var computerMesh = computer.GetComponent<MeshFilter>().sharedMesh;
        hitbox.center = computerMesh.bounds.center; hitbox.size = computerMesh.bounds.size; hitbox.isTrigger = false;
        var oldFrame = computer.transform.Find("USB interaction frame");
        if (oldFrame) Undo.DestroyObjectImmediate(oldFrame.gameObject);
        var frame = new GameObject("USB interaction frame"); Undo.RegisterCreatedObjectUndo(frame, "Attach USB reader");
        var bounds = terminal.highlightTarget.bounds;
        frame.transform.position = new Vector3(bounds.max.x + 0.04f, bounds.min.y + 0.8f, bounds.center.z);
        frame.transform.rotation = Quaternion.Euler(0, -90, 0);
        frame.transform.SetParent(computer.transform, true); terminal.interactionFrame = frame.transform;
        Shape(frame.transform, "USB reader", PrimitiveType.Cube, new Vector3(0.55f, 0.04f, -0.01f), new Vector3(0.16f, 0.1f, 0.18f), graphite);
        var usb = new GameObject("Inserted USB drive"); usb.transform.SetParent(frame.transform, false);
        usb.transform.localPosition = new Vector3(0.55f, 0.04f, -0.15f);
        Shape(usb.transform, "Metal USB plug", PrimitiveType.Cube, Vector3.zero, new Vector3(0.04f, 0.018f, 0.08f), plug);
        Shape(usb.transform, "USB body", PrimitiveType.Cube, new Vector3(0, 0, -0.06f), new Vector3(0.055f, 0.028f, 0.08f), graphite);
        terminal.insertedUsb = usb; usb.SetActive(false);
        if (old && old != terminal) Undo.DestroyObjectImmediate(old.gameObject);

        var alarm = mission.alarm; Undo.RecordObject(alarm, "Replace standing alarm lamps");
        foreach (Transform child in alarm.transform.Cast<Transform>().ToArray()) Undo.DestroyObjectImmediate(child.gameObject);
        var lamps = new List<Light>(); var domes = new List<Renderer>(); var beacons = new List<RotatingAlarmBeacon>();
        var probes = new List<MeshCollider>(); var temporary = new List<GameObject>();
        bool previousBackfaces = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
        try
        {
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>())
            {
                if (renderer.transform.IsChildOf(mission.transform) || renderer.bounds.size.y < 2 ||
                    Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z) < 3) continue;
                var mf = renderer.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var probe = new GameObject("Temporary beacon wall probe") { hideFlags = HideFlags.HideAndDontSave };
                probe.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation); probe.transform.localScale = renderer.transform.lossyScale;
                var collider = probe.AddComponent<MeshCollider>(); collider.sharedMesh = mf.sharedMesh;
                probes.Add(collider); temporary.Add(probe);
            }
            Physics.SyncTransforms();
            Vector3[] points = {
                new Vector3(-10.4f, 3.5f, 22), new Vector3(-10.4f, 3.5f, 26),
                new Vector3(13.6f, 3.5f, 22), new Vector3(13.6f, 3.5f, 26),
                new Vector3(-4, 3.2f, 18), new Vector3(8, 3.2f, 18),
                new Vector3(-4, 3.2f, 29), new Vector3(8, 3.2f, 29),
                new Vector3(-47, 3.6f, 15.65f), new Vector3(-47, 3.6f, -4.45f)
            };
            Vector3[] normals = { Vector3.right, Vector3.right, Vector3.left, Vector3.left,
                Vector3.back, Vector3.back, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.back };
            var report = new System.Text.StringBuilder();
            for (int i = 0; i < points.Length; i++)
            {
                Ray ray = new Ray(points[i] + normals[i] * 2, -normals[i]);
                RaycastHit nearest = default; float distance = 5;
                foreach (var probe in probes)
                    if (probe.Raycast(ray, out var hit, distance)) { nearest = hit; distance = hit.distance; }
                if (!nearest.collider) { report.AppendLine("SKIP no wall at " + points[i]); continue; }
                Vector3 normal = nearest.normal;
                if (Vector3.Dot(normal, normals[i]) < 0) normal = -normal;
                normal.y = 0; normal.Normalize();
                var fixture = new GameObject(i < 4 ? "Control room rotating wall beacon" : "Exterior rotating wall beacon");
                Undo.RegisterCreatedObjectUndo(fixture, "Mount red emergency beacon"); fixture.transform.SetParent(alarm.transform, false);
                fixture.transform.SetPositionAndRotation(nearest.point + normal * 0.015f, Quaternion.LookRotation(normal, Vector3.up));
                Shape(fixture.transform, "Wall mounting plate", PrimitiveType.Cube, Vector3.zero, new Vector3(0.2f, 0.32f, 0.04f), metal);
                Shape(fixture.transform, "Wall beacon bracket", PrimitiveType.Cube, new Vector3(0, -0.04f, 0.15f), new Vector3(0.07f, 0.07f, 0.3f), metal);
                Shape(fixture.transform, "Beacon metal base", PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.27f), new Vector3(0.27f, 0.035f, 0.27f), metal);
                var dome = Shape(fixture.transform, "Red beacon glass dome", PrimitiveType.Sphere, new Vector3(0, 0.17f, 0.27f), new Vector3(0.24f, 0.3f, 0.24f), glass).GetComponent<Renderer>(); domes.Add(dome);
                var rotor = new GameObject("Motor driven reflector").transform; rotor.SetParent(fixture.transform, false); rotor.localPosition = new Vector3(0, 0.15f, 0.27f);
                rotor.localRotation = Quaternion.Euler(0, i * 47, 0);
                Shape(rotor, "Rotating silver reflector", PrimitiveType.Cube, Vector3.zero, new Vector3(0.14f, 0.17f, 0.025f), mirror);
                var fixtureLamps = new List<Light>();
                foreach (int side in new[] { 0, 180 })
                {
                    var beamObject = new GameObject("Rotating red beam"); beamObject.transform.SetParent(rotor, false);
                    beamObject.transform.localRotation = Quaternion.Euler(20, side, 0);
                    var beam = beamObject.AddComponent<Light>(); beam.type = LightType.Spot; beam.color = new Color(1, 0.01f, 0.005f);
                    beam.range = i < 4 ? 10 : 16; beam.intensity = 22; beam.spotAngle = 55; beam.innerSpotAngle = 22; beam.enabled = false;
                    fixtureLamps.Add(beam); lamps.Add(beam);
                }
                var glowObject = new GameObject("Beacon red bulb glow"); glowObject.transform.SetParent(rotor, false);
                var glow = glowObject.AddComponent<Light>(); glow.color = new Color(1, 0.01f, 0.005f); glow.intensity = 1; glow.range = 2.5f; glow.enabled = false;
                fixtureLamps.Add(glow); lamps.Add(glow);
                var beacon = fixture.AddComponent<RotatingAlarmBeacon>(); beacon.rotor = rotor; beacon.lamps = fixtureLamps.ToArray(); beacon.domes = new[] { dome }; beacons.Add(beacon);
                report.AppendLine($"MOUNT {fixture.name}: {nearest.point} normal={normal}");
            }
            alarm.emergencyLights = lamps.ToArray(); alarm.lenses = domes.ToArray(); alarm.rotatingBeacons = beacons.ToArray();
            File.WriteAllText(Work + "/wall-mounts.txt", report.ToString());
        }
        finally { foreach (var probe in temporary) Object.DestroyImmediate(probe); Physics.queriesHitBackfaces = previousBackfaces; }
        if (beacons.Count < 4) throw new InvalidOperationException("Not enough building wall surfaces found for beacons.");
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(mission.gameObject.scene); EditorSceneManager.SaveScene(mission.gameObject.scene);
        Undo.CollapseUndoOperations(undo);
        File.WriteAllText(Work + "/revised.txt", $"Right computer: {computer.name}\nWall beacons: {beacons.Count}\nOld standing lamps and computer labels removed.\n");
        IntelTheftSetup.Capture(frame.transform.TransformPoint(new Vector3(0.5f, 0.7f, -2.3f)), bounds.center, "right-computer");
        var first = beacons[0].transform;
        IntelTheftSetup.Capture(first.position + first.forward * 2 + Vector3.right * 0.5f, first.position + Vector3.up * 0.1f, "wall-beacon");
    }
}

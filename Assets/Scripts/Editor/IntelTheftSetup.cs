using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class IntelTheftSetup
{
    const string Work = "Temp/IntelTheft";
    [InitializeOnLoadMethod]
    static void Resume() => EditorApplication.update += Poll;
    static void Poll()
    {
        string request = Work + "/request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            Directory.CreateDirectory(Work);
            if (command == "inspect") Inspect();
            else if (command == "install") Install();
            else if (command == "validate") IntelTheftValidation.ScheduleRun();
            else if (command == "refine") Refine();
            else if (command == "revise") IntelMissionRefinement.Schedule();
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Operation 1989/Intel/Inspect Computer and Extraction")]
    public static void Inspect()
    {
        Directory.CreateDirectory(Work);
        var report = new StringBuilder();
        report.AppendLine("Scene=" + EditorSceneManager.GetActiveScene().path + " dirty=" + EditorSceneManager.GetActiveScene().isDirty + " playing=" + Application.isPlaying);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t =>
            t.name == "ScifiDesk" || t.name == "desk" || t.name.StartsWith("navy_uh") || t.name.StartsWith("Helipad")))
        {
            report.AppendLine($"ROOT {t.name} position={t.position} scale={t.lossyScale} forward={t.forward}");
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
                report.AppendLine($"  MESH {r.name} bounds={r.bounds} parent={r.transform.parent?.name}");
        }
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
    }

    static Material Material(string name, Color color, float emission = 0)
    {
        string path = "Assets/IntelTheft/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.color = color;
        material.SetFloat("_Smoothness", 0.3f);
        if (emission > 0) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * emission); }
        EditorUtility.SetDirty(material);
        return material;
    }
    static GameObject Part(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collider = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
    static Bounds BoundsOf(Transform root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }
    [MenuItem("Operation 1989/Intel/Install USB Theft and Helicopter Extraction")]
    public static void Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        if (Object.FindAnyObjectByType<IntelTheftMission>()) throw new InvalidOperationException("Intel mission is already installed.");
        var intro = Object.FindAnyObjectByType<IntroTruckSequence>();
        if (!intro || !intro.player || !intro.playerCamera) throw new InvalidOperationException("Player and intro setup missing.");
        var desks = Object.FindObjectsByType<Transform>().Where(t => t.name == "ScifiDesk" || t.name == "desk").ToArray();
        var desk = desks.FirstOrDefault(t => t.name == "desk") ?? desks.FirstOrDefault();
        var helicopter = GameObject.Find("navy_uh-1n (1)") ?? GameObject.Find("navy_uh-1n");
        if (!desk || !helicopter) throw new InvalidOperationException("Existing computer desk or helicopter missing.");
        Directory.CreateDirectory(Work);
        EditorSceneManager.SaveScene(scene, Work + "/before-intel.unity", true);
        if (!AssetDatabase.IsValidFolder("Assets/IntelTheft")) AssetDatabase.CreateFolder("Assets", "IntelTheft");
        var housing = Material("Terminal graphite", new Color(0.035f, 0.05f, 0.045f));
        var screen = Material("Archive screen", new Color(0.005f, 0.035f, 0.025f), 0.5f);
        var metal = Material("USB connector", new Color(0.55f, 0.57f, 0.5f));
        var red = Material("Emergency beacon red", new Color(0.3f, 0.008f, 0.005f), 1);
        var green = Material("Extraction marking", new Color(0.2f, 0.9f, 0.55f), 2);
        var root = new GameObject("Secret Intel Mission");
        Undo.RegisterCreatedObjectUndo(root, "Install USB theft mission");
        var mission = root.AddComponent<IntelTheftMission>();
        mission.player = intro.player; mission.playerCamera = intro.playerCamera; mission.intro = intro;
        var bounds = BoundsOf(desk);
        var terminalObject = new GameObject("Secret archive computer");
        terminalObject.transform.SetParent(root.transform, false);
        terminalObject.transform.position = new Vector3(bounds.center.x, bounds.min.y + 0.8f, bounds.center.z);
        terminalObject.transform.rotation = Quaternion.Euler(0, desk.eulerAngles.y, 0);
        var existingScreen = desk.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name.StartsWith("SCREEN MAIN"));
        if (existingScreen)
        {
            terminalObject.transform.position = existingScreen.bounds.center + Vector3.right * 0.065f - Vector3.up * 0.45f;
            terminalObject.transform.rotation = Quaternion.Euler(0, -90, 0);
        }
        var terminal = terminalObject.AddComponent<IntelTerminal>(); terminal.mission = mission; mission.terminal = terminal;
        Part(terminal.transform, "Monitor foot", new Vector3(0, 0.025f, 0), new Vector3(0.42f, 0.05f, 0.26f), housing);
        Part(terminal.transform, "Monitor stand", new Vector3(0, 0.16f, 0.04f), new Vector3(0.08f, 0.27f, 0.08f), housing);
        Part(terminal.transform, "Archive monitor", new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.53f, 0.08f), housing, true);
        Part(terminal.transform, "Archive display", new Vector3(0, 0.45f, -0.045f), new Vector3(0.82f, 0.45f, 0.009f), screen);
        Part(terminal.transform, "USB reader", new Vector3(0.48f, 0.25f, -0.03f), new Vector3(0.22f, 0.14f, 0.24f), housing, true);
        var usb = new GameObject("Inserted USB drive"); usb.transform.SetParent(terminal.transform, false);
        usb.transform.localPosition = new Vector3(0.48f, 0.25f, -0.22f);
        Part(usb.transform, "Metal USB plug", Vector3.zero, new Vector3(0.04f, 0.018f, 0.08f), metal);
        Part(usb.transform, "USB body", new Vector3(0, 0, -0.065f), new Vector3(0.055f, 0.028f, 0.08f), housing);
        terminal.insertedUsb = usb; usb.SetActive(false);
        var label = new GameObject("Archive screen text"); label.transform.SetParent(terminal.transform, false);
        label.transform.localPosition = new Vector3(0, 0.45f, -0.052f);
        label.transform.localRotation = Quaternion.identity;
        var display = label.AddComponent<TextMesh>(); display.anchor = TextAnchor.MiddleCenter; display.alignment = TextAlignment.Center;
        display.characterSize = 0.018f; display.fontSize = 48; display.text = "SECURE ARCHIVE\nINSERT USB"; display.color = new Color(0.25f, 1, 0.7f);
        terminal.display = display;

        var extractionObject = new GameObject("Helicopter extraction point"); extractionObject.transform.SetParent(root.transform, false);
        var helicopterBounds = BoundsOf(helicopter.transform);
        // Board beside the existing grounded helicopter, clear of its solid hull.
        Vector3 boarding = helicopterBounds.center + helicopter.transform.right * 4.2f;
        var cargoDoor = helicopter.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name == "cargo_door_r");
        if (cargoDoor) boarding = cargoDoor.bounds.center + helicopter.transform.right * 2.2f;
        Physics.SyncTransforms();
        var floor = Physics.RaycastAll(new Vector3(boarding.x, 6, boarding.z), Vector3.down, 15, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => !h.transform.IsChildOf(helicopter.transform) && h.normal.y > 0.65f).OrderBy(h => h.distance).ToArray();
        boarding.y = floor.Length > 0 ? floor[0].point.y + 0.025f : 0.05f;
        extractionObject.transform.position = boarding;
        var extraction = extractionObject.AddComponent<HelicopterExtractionZone>(); mission.extraction = extraction;
        var zone = extractionObject.GetComponent<BoxCollider>(); zone.isTrigger = true; zone.center = Vector3.up * 1.5f; zone.size = new Vector3(3, 5, 3);
        var markings = new GameObject("Extraction ground markings"); markings.transform.SetParent(extraction.transform, false);
        extraction.marker = markings;
        foreach (int side in new[] { -1, 1 })
        {
            Part(markings.transform, "Boarding zone edge", new Vector3(side * 1.4f, 0, 0), new Vector3(0.06f, 0.015f, 2.8f), green);
            Part(markings.transform, "Boarding zone edge", new Vector3(0, 0, side * 1.4f), new Vector3(2.8f, 0.015f, 0.06f), green);
        }
        markings.SetActive(false);

        var alarmObject = new GameObject("Base emergency alarm"); alarmObject.transform.SetParent(root.transform, false);
        var alarm = alarmObject.AddComponent<BaseTheftAlarm>(); mission.alarm = alarm;
        alarm.sirenClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Ambiance/MilitaryAlarm.mp3");
        var lamps = new System.Collections.Generic.List<Light>(); var lenses = new System.Collections.Generic.List<Renderer>();
        Vector3[] positions = {
            new Vector3(-8.4f, 4.3f, 21), new Vector3(-8.4f, 4.3f, 27), new Vector3(12, 4.3f, 21), new Vector3(12, 4.3f, 27),
            new Vector3(1, 4, 15), new Vector3(4, 4, -86), new Vector3(-60, 4, 5), new Vector3(50, 4, 15),
            new Vector3(-50, 4, -50), new Vector3(50, 4, -50), new Vector3(-50, 4, 55), new Vector3(50, 4, 55)
        };
        foreach (Vector3 position in positions)
        {
            var fixture = new GameObject("Emergency red beacon"); fixture.transform.SetParent(alarm.transform, false); fixture.transform.position = position;
            bool outdoors = position.y <= 4;
            Part(fixture.transform, outdoors ? "Beacon support pole" : "Ceiling beacon mount",
                outdoors ? Vector3.down * 2 : Vector3.up * 0.15f,
                outdoors ? new Vector3(0.07f, 4, 0.07f) : new Vector3(0.08f, 0.3f, 0.08f), housing);
            Part(fixture.transform, "Beacon housing", Vector3.zero, new Vector3(0.35f, 0.1f, 0.25f), housing);
            lenses.Add(Part(fixture.transform, "Red beacon lens", Vector3.down * 0.08f, new Vector3(0.26f, 0.09f, 0.18f), red).GetComponent<Renderer>());
            var lightObject = new GameObject("Emergency red illumination"); lightObject.transform.SetParent(fixture.transform, false); lightObject.transform.localPosition = Vector3.down * 0.2f;
            var lamp = lightObject.AddComponent<Light>(); lamp.color = new Color(1, 0.01f, 0.005f); lamp.intensity = 14; lamp.range = 14; lamp.enabled = false; lamps.Add(lamp);
        }
        alarm.emergencyLights = lamps.ToArray(); alarm.lenses = lenses.ToArray();
        Undo.RecordObject(intro, "Connect secret theft objectives"); intro.intelMission = mission;
        EditorUtility.SetDirty(intro); AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Work + "/installed.txt", $"Terminal: {terminal.transform.position} on {desk.name}\nHelicopter: {helicopter.name}\nExtraction: {boarding}\nEmergency lights: {lamps.Count}\n");
        Capture(terminal.transform.TransformPoint(new Vector3(0, 0.8f, -2)), terminal.transform.position + Vector3.up * 0.4f, "archive-computer");
        Capture(boarding + new Vector3(6, 4, -7), helicopter.transform.position + Vector3.up * 1.5f, "helicopter-extraction");
        IntelMissionRefinement.Schedule();
    }
    public static void Capture(Vector3 position, Vector3 target, string name)
    {
        var go = new GameObject("Intel mission preview"); var camera = go.AddComponent<Camera>();
        go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
        camera.transform.position = position; camera.transform.LookAt(target); camera.nearClipPlane = 0.04f;
        var rt = new RenderTexture(1000, 720, 24); var prior = RenderTexture.active;
        var texture = new Texture2D(1000, 720, TextureFormat.RGB24, false);
        try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, 1000, 720), 0, 0); texture.Apply(); File.WriteAllBytes(Work + "/" + name + ".png", texture.EncodeToPNG()); }
        finally { RenderTexture.active = prior; camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); }
    }
    public static void Refine()
    {
        IntelMissionRefinement.Schedule();
    }
}

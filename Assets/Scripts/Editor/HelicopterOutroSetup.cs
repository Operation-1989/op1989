using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class HelicopterOutroSetup
{
    const string Work = "Temp/HelicopterOutro";
    [InitializeOnLoadMethod]
    static void Initialize()
    {
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/ready.txt", "1");
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        string request = Work + "/request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try
        {
            if (command == "inspect") Inspect();
            if (command == "install") Install();
            if (command == "validate") HelicopterOutroValidation.Run();
            if (command == "validate-late") HelicopterOutroValidation.RunLateBoarding();
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Operation 1989/Outro/Install Helicopter Outro")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install in Edit mode.");
        var mission = Object.FindAnyObjectByType<IntelTheftMission>();
        var heli = GameObject.Find("navy_uh-1n (1)").transform;
        if (!mission || !mission.extraction) throw new InvalidOperationException("Intel mission is missing.");
        if (PrefabUtility.IsPartOfPrefabInstance(heli))
            PrefabUtility.UnpackPrefabInstance(heli.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var t in heli.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        var outro = mission.GetComponent<HelicopterOutroSequence>();
        if (!outro) outro = Undo.AddComponent<HelicopterOutroSequence>(mission.gameObject);
        Undo.RecordObject(mission, "Connect helicopter outro");
        mission.outro = outro; outro.mission = mission; outro.helicopter = heli;
        Transform Child(string name) => heli.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        var mainGround = Child("prop_ground"); var mainFly = Child("prop_fly");
        var tailGround = Child("sub_prop"); var tailFly = Child("sub_prop_fly");
        Transform Pivot(string name, Vector3 local, params Transform[] meshes)
        {
            var pivot = heli.Find(name);
            if (!pivot) { pivot = new GameObject(name).transform; pivot.SetParent(heli, false); pivot.localPosition = local; }
            foreach (var mesh in meshes) mesh.SetParent(pivot, true);
            return pivot;
        }
        outro.mainRotor = Pivot("Outro main rotor shaft", new Vector3(0, 509.558f, 0), mainGround, mainFly, Child("main_prop"));
        outro.tailRotor = Pivot("Outro tail rotor shaft", new Vector3(38.081f, 431.064f, -1059.741f), tailGround, tailFly);
        outro.parkedBlades = mainGround.GetComponent<Renderer>(); outro.flyingBlades = mainFly.GetComponent<Renderer>();
        outro.parkedTail = tailGround.GetComponent<Renderer>(); outro.flyingTail = tailFly.GetComponent<Renderer>();
        outro.flyingBlades.enabled = outro.flyingTail.enabled = false;
        outro.parkedBlades.enabled = outro.parkedTail.enabled = true;
        // The imported flight discs use an opaque material. Give them a restrained transparent blur.
        const string materialPath = "Assets/IntelTheft/Helicopter rotor blur.mat";
        var blur = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!blur)
        {
            blur = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            blur.SetFloat("_Surface", 1); blur.SetFloat("_Blend", 0);
            blur.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            blur.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            blur.SetFloat("_ZWrite", 0); blur.SetFloat("_Cull", 0);
            blur.SetColor("_BaseColor", new Color(.22f, .25f, .26f, .12f));
            blur.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); blur.renderQueue = 3000;
            AssetDatabase.CreateAsset(blur, materialPath);
        }
        foreach (var renderer in new[] { outro.flyingBlades, outro.flyingTail })
        { renderer.sharedMaterial = blur; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        var eyes = heli.Find("Outro pilot eyes");
        if (!eyes) { eyes = new GameObject("Outro pilot eyes").transform; eyes.SetParent(heli, false); }
        eyes.position = heli.position + heli.rotation * new Vector3(.85f, 2.65f, 4.25f);
        eyes.rotation = heli.rotation * Quaternion.Euler(0, -5, 0);
        outro.pilotEyes = eyes;
        outro.startupClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Vehicles/HelicopterStart.mp3");
        outro.chatterClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Ambiance/HelicopterChatter.mp3");
        outro.outroMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Ambiance/OutroMusic.mp3");
        if (!outro.outroMusic) throw new InvalidOperationException("OutroMusic.mp3 is missing or has not imported.");
        outro.outroMusicVolume = .28f; outro.musicFadeInSeconds = 1.75f;
        outro.approachEngineVolume = .45f; outro.cabinEngineVolume = .2f; outro.chatterVolume = .75f;
        outro.engineHandoffSeconds = 1.2f; outro.engineFadeOutSeconds = 6; outro.visualFadeSeconds = 3.5f;
        outro.musicBlackHoldSeconds = 1; outro.musicFadeOutSeconds = 3;
        outro.spoolSeconds = 2; outro.boardingLeadSeconds = 2;
        outro.takeoffAudioSeconds = 55; outro.afterChatterSeconds = 3;
        outro.gatePosition = mission.intro ? mission.intro.entryPlanePoint : new Vector3(4, 0, -90);
        var anchor = mission.transform.Find("Outro main building camera");
        if (!anchor) { anchor = new GameObject("Outro main building camera").transform; anchor.SetParent(mission.transform, false); }
        // Front of the main building, beside the entrance barricades in the reference image.
        anchor.position = new Vector3(-8, 2.3f, 10);
        outro.exteriorCameraAnchor = anchor;
        outro.exteriorTravelOffset = new Vector3(14, 0, 0);
        anchor.LookAt(heli.position + Vector3.up * 18);
        outro.exteriorFieldOfView = 70;
        var sunset = mission.GetComponent<OutroSunsetTransition>();
        if (!sunset) sunset = Undo.AddComponent<OutroSunsetTransition>(mission.gameObject);
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/IntelTheft/Departure Sunset.mat");
        if (!sky)
        {
            sky = new Material(Shader.Find("Operation1989/Departure Sunset"));
            AssetDatabase.CreateAsset(sky, "Assets/IntelTheft/Departure Sunset.mat");
        }
        sunset.sunsetSky = sky;
        sunset.sunlight = RenderSettings.sun ? RenderSettings.sun : Object.FindObjectsByType<Light>().First(l => l.type == LightType.Directional && l.enabled && l.intensity > 0);
        outro.sunset = sunset; EditorUtility.SetDirty(sunset);
        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>();
        outro.engineMuffleRoom = ambience ? ambience.controlRoom : null;
        outro.indoorEngineGain = .01f; outro.nearEngineDistance = 8; outro.engineFadeDistance = 110;
        ConfigureBoarding(mission.extraction, heli);
        ConfigureObserverViews(mission, outro, heli);
        EditorUtility.SetDirty(outro); EditorUtility.SetDirty(mission);
        EditorSceneManager.MarkSceneDirty(heli.gameObject.scene);
        EditorSceneManager.SaveScene(heli.gameObject.scene); AssetDatabase.SaveAssets();
        InspectMixLevels(outro);
        File.WriteAllText(Work + "/installed.txt", $"Installed in {heli.gameObject.scene.path}\nPilot eyes: {eyes.position:F3}\nUSB removal starts the running-engine audio and rotors. Boarding cuts to the takeoff lead-in with a 0.2s crossfade.\nTakeoff: {outro.boardingLeadSeconds:F3}s after boarding, aligned to the recording's {outro.takeoffAudioSeconds:F3}s cue.\nChatter: {outro.chatterClip.length:F3}s; exterior begins exactly 3s after chatter.\nGate: {outro.gatePosition}\nOne {outro.exteriorFieldOfView} degree shot travels {outro.exteriorTravelOffset} across the road, tracking the helicopter.\nDeparture-only orange dusk transition.\n");
        IntelTheftSetup.Capture(eyes.position, eyes.position + eyes.forward * 10, "outro-pilot-installed");
    }
    static void InspectMixLevels(HelicopterOutroSequence outro)
    {
        float Rms(AudioClip clip, float start, float seconds)
        {
            clip.LoadAudioData();
            int frames = Mathf.RoundToInt(Mathf.Min(seconds, clip.length - start) * clip.frequency);
            var samples = new float[frames * clip.channels];
            if (!clip.GetData(samples, Mathf.RoundToInt(start * clip.frequency))) return 0;
            double sum = 0; foreach (float sample in samples) sum += sample * sample;
            return (float)Math.Sqrt(sum / samples.Length);
        }
        File.WriteAllText(Work + "/audio-mix-levels.txt",
            $"Music first 6s RMS: {Rms(outro.outroMusic, 0, 6):F5}\nMusic 6-20s RMS: {Rms(outro.outroMusic, 6, 14):F5}\nEngine takeoff RMS: {Rms(outro.startupClip, 53, 12):F5}\nMusic gain: {outro.outroMusicVolume:F2}; cabin engine gain: {outro.cabinEngineVolume:F2}; approach engine gain: {outro.approachEngineVolume:F2}\n");
    }
    static void ConfigureBoarding(HelicopterExtractionZone extraction, Transform heli)
    {
        var bodyNames = new HashSet<string> { "defalt", "cargo_door_l", "cargo_door_r", "pilot_door", "co_pilot_door", "grass", "navy_camera", "navy_camera1" };
        var points = new List<Vector2>();
        Quaternion inverse = Quaternion.Inverse(heli.rotation);
        foreach (var mesh in heli.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!bodyNames.Contains(mesh.name) || !mesh.sharedMesh) continue;
            foreach (var vertex in mesh.sharedMesh.vertices)
            {
                Vector3 p = inverse * (mesh.transform.TransformPoint(vertex) - heli.position);
                points.Add(new Vector2(p.x, p.z));
            }
        }
        var sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToArray();
        if (sorted.Length < 3) throw new InvalidOperationException("Helicopter body geometry is missing.");
        float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        var hull = new List<Vector2>();
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= .00001f) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        int lower = hull.Count;
        for (int i = sorted.Length - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= .00001f) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count - 1);
        Undo.RecordObject(extraction, "Expand helicopter automatic boarding");
        extraction.helicopter = heli; extraction.bodyFootprint = hull.ToArray(); extraction.boardingClearance = 1;
        extraction.minimumBoardingHeight = -.5f; extraction.maximumBoardingHeight = 3;
        Vector2 side = hull.OrderByDescending(p => p.x).First();
        Vector3 markerPosition = heli.position + heli.rotation * new Vector3(side.x + .5f, 0, side.y);
        markerPosition.y = extraction.transform.position.y;
        Undo.RecordObject(extraction.transform, "Keep boarding marker inside expanded zone");
        extraction.transform.position = markerPosition;
        var box = extraction.GetComponent<BoxCollider>();
        Undo.RecordObject(box, "Resize boarding trigger");
        var bounds = new Bounds(heli.position + heli.rotation * new Vector3(hull[0].x, 1.25f, hull[0].y), Vector3.zero);
        foreach (var p in hull) bounds.Encapsulate(heli.position + heli.rotation * new Vector3(p.x, 1.25f, p.y));
        box.center = extraction.transform.InverseTransformPoint(bounds.center);
        Vector3 size = new Vector3(bounds.size.x + 2, 3.5f, bounds.size.z + 2);
        Vector3 scale = extraction.transform.lossyScale;
        box.size = new Vector3(size.x / Mathf.Abs(scale.x), size.y / Mathf.Abs(scale.y), size.z / Mathf.Abs(scale.z));
        box.isTrigger = true;
        EditorUtility.SetDirty(box); EditorUtility.SetDirty(extraction);
        File.WriteAllText(Work + "/boarding-footprint.txt", $"Body-only hull: {hull.Count} vertices, 1m clearance; rotor meshes excluded.\n" + string.Join("\n", hull));
    }
    static void ConfigureObserverViews(IntelTheftMission mission, HelicopterOutroSequence outro, Transform heli)
    {
        var root = mission.transform.Find("Helicopter observer views");
        if (!root) { root = new GameObject("Helicopter observer views").transform; root.SetParent(mission.transform, false); }
        var views = root.GetComponent<HelicopterObserverViews>();
        if (!views) views = Undo.AddComponent<HelicopterObserverViews>(root.gameObject);
        Camera MakeCamera(string name, int display, float fov)
        {
            var child = root.Find(name);
            if (!child) { child = new GameObject(name).transform; child.SetParent(root, false); }
            var camera = child.GetComponent<Camera>();
            if (!camera) camera = Undo.AddComponent<Camera>(child.gameObject);
            camera.CopyFrom(mission.playerCamera); camera.targetTexture = null;
            camera.targetDisplay = display; camera.fieldOfView = fov; camera.nearClipPlane = .05f;
            camera.rect = new Rect(0, 0, 1, 1); camera.enabled = true;
            var data = child.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (!data) data = Undo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(child.gameObject);
            data.renderPostProcessing = true; EditorUtility.SetDirty(camera); EditorUtility.SetDirty(data);
            return camera;
        }
        views.helicopter = heli;
        views.frontCamera = MakeCamera("Helicopter Front Camera (Display 4)", 3, 55);
        views.cinematicCamera = MakeCamera("Helicopter Cinematic Camera (Display 5)", 4, 55);
        views.ResetViews(); outro.observerViews = views;
        EditorUtility.SetDirty(views);
    }
    static void Inspect()
    {
        var heli = GameObject.Find("navy_uh-1n (1)").transform;
        var report = new StringBuilder();
        foreach (var t in heli.GetComponentsInChildren<Transform>(true))
        {
            var mesh = t.GetComponent<MeshFilter>();
            if (!mesh) continue;
            var b = mesh.sharedMesh.bounds;
            report.AppendLine($"{t.name} active={t.gameObject.activeSelf} pos={t.localPosition:F3} rot={t.localEulerAngles:F2} bounds={b} worldCenter={mesh.GetComponent<Renderer>().bounds.center:F3} rootCenter={heli.InverseTransformPoint(mesh.GetComponent<Renderer>().bounds.center):F3}");
        }
        foreach (string path in new[]{"Assets/Audio/SFX/Vehicles/HelicopterStart.mp3", "Assets/Audio/SFX/Ambiance/HelicopterChatter.mp3", "Assets/Audio/SFX/Vehicles/HelicopterFlyBy.mp3"})
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            report.AppendLine($"AUDIO {path} length={clip.length} load={clip.loadState}");
        }
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
        // Imported model units are centimeters; use meter offsets in its unscaled heading frame.
        Vector3 P(Vector3 offset) => heli.position + heli.rotation * offset;
        IntelTheftSetup.Capture(P(new Vector3(0.65f, 2.4f, 3.2f)), P(new Vector3(.65f, 2.2f, 12)), "outro-cockpit");
        IntelTheftSetup.Capture(P(new Vector3(10, 5, 12)), P(new Vector3(0, 2, 0)), "outro-exterior");
    }
}

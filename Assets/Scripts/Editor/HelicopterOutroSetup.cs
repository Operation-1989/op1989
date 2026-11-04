using System;
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
        outro.spoolSeconds = 12; outro.takeoffAudioSeconds = 55; outro.afterChatterSeconds = 3;
        outro.gatePosition = mission.intro ? mission.intro.entryPlanePoint : new Vector3(4, 0, -90);
        var anchor = mission.transform.Find("Outro main building camera");
        if (!anchor) { anchor = new GameObject("Outro main building camera").transform; anchor.SetParent(mission.transform, false); }
        // Front of the main building, beside the entrance barricades in the reference image.
        anchor.position = new Vector3(-8, 2.3f, 10);
        outro.exteriorCameraAnchor = anchor;
        outro.exteriorLookStart = new Vector3(35, 18, -20);
        outro.exteriorLookEnd = new Vector3(0, 20, -110);
        anchor.LookAt(outro.exteriorLookStart);
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
        EditorUtility.SetDirty(outro); EditorUtility.SetDirty(mission);
        EditorSceneManager.MarkSceneDirty(heli.gameObject.scene);
        EditorSceneManager.SaveScene(heli.gameObject.scene); AssetDatabase.SaveAssets();
        File.WriteAllText(Work + "/installed.txt", $"Installed in {heli.gameObject.scene.path}\nPilot eyes: {eyes.position:F3}\nUSB removal starts the recording and rotors. Takeoff cue: {outro.takeoffAudioSeconds:F3}s.\nChatter: {outro.chatterClip.length:F3}s; exterior begins exactly 3s after chatter.\nStartup: {outro.startupClip.length:F3}s, retained through departure.\nGate: {outro.gatePosition}\nSingle wide shot at {anchor.position:F3}, {outro.exteriorFieldOfView} degree FOV, with a gentle pan.\nDeparture-only orange dusk transition.\n");
        IntelTheftSetup.Capture(eyes.position, eyes.position + eyes.forward * 10, "outro-pilot-installed");
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

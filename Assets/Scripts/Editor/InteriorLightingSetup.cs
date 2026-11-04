using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Explicit refinement of the existing dusk scene.</summary>
public static class InteriorLightingSetup
{
    const string Work = "Temp/InteriorLighting";
    const string Folder = "Assets/DuskLighting";
    const string Rig = "Interior equipment lighting";
    [Serializable] public class Tuning
    {
        public Vector3 tableOffset = new Vector3(0, 0, -1.2f);
        public string tableName = "Sci Fi Conference";
    }
    [InitializeOnLoadMethod]
    static void Initialize()
    {
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/ready.txt", "5");
        EditorApplication.update += Poll;
        if (SessionState.GetBool("InteriorBlinkCheck", false)) EditorApplication.update += ObserveBlinking;
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
            if (command == "install") Install();
            if (command == "validate") Validate();
            if (command == "smoke")
            {
                observed.Clear();
                SessionState.SetBool("InteriorBlinkCheck", true);
                EditorApplication.update += ObserveBlinking;
                File.WriteAllText("Temp/DuskLighting/request.txt", "smoke");
            }
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }
    static bool Relevant(Transform t) => t.name == "Sci Fi Conference" || t.name == "ScifiDesk" || t.name == "desk" || t.name.StartsWith("server") || t.name.StartsWith("electronictower") || t.name.StartsWith("navy_uh") || t.name == "NATO_Shelter_V1";
    static void Inspect()
    {
        var report = new StringBuilder();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(Relevant))
        {
            report.AppendLine($"OBJECT {t.name} pos={t.position:F3} scale={t.lossyScale:F3} rot={t.eulerAngles:F1}");
            foreach (var r in t.GetComponentsInChildren<Renderer>())
                report.AppendLine($" MESH {r.name} center={r.bounds.center:F3} size={r.bounds.size:F3} materials={string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name))}");
            foreach (var light in t.GetComponentsInChildren<Light>(true))
                report.AppendLine($" LIGHT {light.name} enabled={light.enabled} pos={light.transform.position:F2} intensity={light.intensity}");
        }
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
        Previews("before");
    }
    static Material Mat(string name, Color color, float glow = 0)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.color = color;
        mat.SetFloat("_Smoothness", .35f);
        if (glow > 0)
        {
            mat.SetColor("_EmissionColor", color * glow);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            MaterialEditor.FixupEmissiveFlag(mat);
            mat.EnableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
    static Transform Group(Transform parent, string name, Vector3 position)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = position;
        return t;
    }
    static Renderer Strip(Transform parent, string name, Vector3 position, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name + " fixture light"; // Excluded from generated environment colliders.
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = mat;
        return renderer;
    }
    static Light Lamp(Transform parent, string name, Vector3 position, Color color, float intensity, float range, bool spot = false)
    {
        var go = Group(parent, name, position).gameObject;
        var light = go.AddComponent<Light>();
        light.type = spot ? LightType.Spot : LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        if (spot) { light.spotAngle = 115; light.innerSpotAngle = 75; go.transform.localRotation = Quaternion.Euler(90, 0, 0); }
        go.AddComponent<UniversalAdditionalLightData>();
        return light;
    }
    static readonly Color Cyan = new Color(.08f, .72f, 1);
    static readonly Color Violet = new Color(.65f, .12f, 1);
    static readonly Color Warm = new Color(1, .86f, .7f);

    [MenuItem("Operation 1989/Lighting/Refine Interiors and Aircraft")]
    public static void Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        if (!File.Exists(Work + "/before-refinement.unity")) EditorSceneManager.SaveScene(scene, Work + "/before-refinement.unity", true);
        var tuning = File.Exists(Work + "/tuning.json") ? JsonUtility.FromJson<Tuning>(File.ReadAllText(Work + "/tuning.json")) : new Tuning();
        var old = GameObject.Find(Rig);
        if (old) Undo.DestroyObjectImmediate(old);
        var root = Group(null, Rig, Vector3.zero);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Interior equipment lights");
        var cyan = Mat("Equipment cyan", Cyan, 3.5f);
        var violet = Mat("Equipment violet", Violet, 3);
        var charcoal = Mat("Interior lamp housing", new Color(.045f, .055f, .065f));
        var red = Mat("Aircraft beacon red", new Color(1, .02f, .01f), 5);
        var table = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(t => t.name == tuning.tableName && t.gameObject.scene == scene)?.gameObject;
        if (!table) throw new InvalidOperationException("Conference table not found.");
        string originalPath = Folder + "/Conference table original position.json";
        if (!File.Exists(originalPath)) File.WriteAllText(originalPath, JsonUtility.ToJson(new Tuning { tableOffset = table.transform.position }));
        var original = JsonUtility.FromJson<Tuning>(File.ReadAllText(originalPath)).tableOffset;
        Undo.RecordObject(table.transform, "Move table away from computer access");
        table.transform.position = original + tuning.tableOffset;
        PrefabUtility.RecordPrefabInstancePropertyModifications(table.transform);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t => t.name == "Sci Fi Conference" || t.name == "ScifiDesk" || t.name == "desk"))
            foreach (var light in t.GetComponentsInChildren<Light>(true))
            { Undo.RecordObject(light, "Remove table lighting"); light.enabled = false; EditorUtility.SetDirty(light); PrefabUtility.RecordPrefabInstancePropertyModifications(light); }
        foreach (var renderer in table.GetComponentsInChildren<Renderer>().Where(r => r.name != "Hud"))
        {
            Undo.RecordObject(renderer, "Remove table emission");
            renderer.sharedMaterials = renderer.sharedMaterials.Select(originalMat =>
            {
                if (!originalMat) return originalMat;
                string path = Folder + "/Table unlit emission " + originalMat.name.Replace("Table unlit emission ", "") + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!mat) { mat = new Material(originalMat); AssetDatabase.CreateAsset(mat, path); }
                mat.DisableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
                EditorUtility.SetDirty(mat);
                return mat;
            }).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        foreach (var vehicleRig in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t => t.name == "Dusk Vehicle Lights").ToArray())
            if (vehicleRig.parent && vehicleRig.parent.name != "Patrol Jeep (UAZ)" && vehicleRig.parent.name != "Intro Entrance Truck") Undo.DestroyObjectImmediate(vehicleRig.gameObject);

        // Server indicators and equipment edge strips belong to the equipment, not the desk.
        foreach (var t in scene.GetRootGameObjects().Select(g => g.transform).Where(t => t.name == "server" || t.name.StartsWith("server (")))
        {
            var rack = Group(root, t.name + " cyan status lights", t.TransformPoint(new Vector3(-.035f, 0, .593f)));
            rack.rotation = t.rotation;
            foreach (float face in new[] { -.375f, .375f })
            {
                for (int side = -1; side <= 1; side += 2)
                    Strip(rack, "Rack edge", new Vector3(side * .35f, 1.1f, face), new Vector3(.028f, 1.9f, .025f), cyan);
                for (int row = 0; row < 6; row++)
                    Strip(rack, "Drive activity", new Vector3(.18f, .5f + row * .23f, face), new Vector3(.09f, .025f, .025f), row % 3 == 0 ? violet : cyan);
            }
        }
        foreach (var t in scene.GetRootGameObjects().Select(g => g.transform).Where(t => t.name.StartsWith("electronictower")))
        {
            var bounds = t.GetComponentInChildren<Renderer>().bounds;
            var rack = Group(root, t.name + " equipment lights", new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            rack.rotation = t.rotation;
            Strip(rack, "Equipment edge", new Vector3(0, 1.4f, -.63f), new Vector3(.7f, .025f, .025f), cyan);
            Strip(rack, "Equipment edge reverse", new Vector3(0, 1.4f, .63f), new Vector3(.7f, .025f, .025f), cyan);
        }
        Room(root, "Server room", -8.4f, cyan, violet, charcoal);
        Room(root, "East equipment room", 11.2f, cyan, violet, charcoal);

        var helicopter = GameObject.Find("navy_uh-1n (1)");
        if (!helicopter) throw new InvalidOperationException("Ground helicopter missing.");
        var previous = helicopter.transform.Find("Aircraft ground beacon lights");
        if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
        var heliRig = Group(helicopter.transform, "Aircraft ground beacon lights", Vector3.zero);
        var scale = helicopter.transform.lossyScale;
        heliRig.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
        // World-space mounting is resolved to local coordinates so the beacons follow the aircraft.
        Beacon(heliRig, "Top anti-collision beacon", heliRig.InverseTransformPoint(new Vector3(50.7f, 3.3f, 14.8f)), red, Color.red, AircraftLightPulse.Pattern.RedBeacon);
        var bottom = Beacon(heliRig, "Belly anti-collision beacon", heliRig.InverseTransformPoint(new Vector3(50.7f, .72f, 14.8f)), red, Color.red, AircraftLightPulse.Pattern.RedBeacon);
        bottom.phaseOffset = .6f;
        // Repair imported/default emission flags before URP validates the material keywords.
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!mat || !mat.HasProperty("_EmissionColor") || mat.GetColor("_EmissionColor").maxColorComponent <= 0) continue;
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            MaterialEditor.FixupEmissiveFlag(mat);
            mat.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);
        }
        SceneClearanceFix.FixGeometryAndEffects();
        LightingPlacementSetup.FixPlacement();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Validate();
    }
    static void Room(Transform root, string name, float x, Material cyan, Material violet, Material housing)
    {
        var room = Group(root, name, Vector3.zero);
        // Low profile ceiling rails leave doors and computer approaches open.
        foreach (float z in new[] { 19.2f, 27.8f })
        {
            Strip(room, "Ceiling channel", new Vector3(x, 4.7f, z), new Vector3(3.6f, .1f, .14f), housing);
            Strip(room, "Cyan ceiling inset", new Vector3(x, 4.63f, z), new Vector3(3.4f, .035f, .085f), cyan);
            Lamp(room, "Cool overhead equipment wash", new Vector3(x, 4.5f, z), new Color(.55f, .77f, 1), 3.3f, 9, true);
        }
        float wallX = x < 0 ? -10.4f : 13.6f;
        Strip(room, "Violet equipment trim", new Vector3(wallX, 2.5f, 23.5f), new Vector3(.035f, .045f, 6.5f), violet);
        Lamp(room, "Violet side wash", new Vector3(x < 0 ? -9.5f : 13.5f, 2.4f, 25), Violet, .6f, 4.5f);
        Lamp(room, "Cyan equipment wash", new Vector3(x + 1.5f, 1.4f, 26), Cyan, 1.5f, 4.5f);
    }
    static AircraftLightPulse Beacon(Transform parent, string name, Vector3 position, Material material, Color color, AircraftLightPulse.Pattern pattern)
    {
        var group = Group(parent, name, position);
        var lens = Strip(group, "Beacon lens", Vector3.zero, new Vector3(.12f, .09f, .12f), material);
        var light = Lamp(group, "Beacon glow", Vector3.zero, color, .65f, 2);
        var pulse = group.gameObject.AddComponent<AircraftLightPulse>();
        pulse.pattern = pattern; pulse.source = light; pulse.lens = lens; pulse.color = color;
        return pulse;
    }
    [MenuItem("Operation 1989/Lighting/Validate Interior Refinement")]
    public static void Validate()
    {
        var report = new StringBuilder();
        var rigs = Object.FindObjectsByType<Transform>().Where(t => t.name == "Dusk Vehicle Lights").ToArray();
        report.AppendLine($"Moving vehicle light rigs={rigs.Length}; parked vehicle light rigs={rigs.Count(t => t.parent.name != "Patrol Jeep (UAZ)" && t.parent.name != "Intro Entrance Truck")}");
        foreach (var pulse in Object.FindObjectsByType<AircraftLightPulse>())
        {
            bool timing = pulse.IsLitAt(-pulse.phaseOffset) && !pulse.IsLitAt(.7f - pulse.phaseOffset);
            report.AppendLine($"{(timing && pulse.source && pulse.lens ? "PASS" : "FAIL")} {pulse.name}: {pulse.pattern}, cycle={pulse.period}, offset={pulse.phaseOffset}");
        }
        report.AppendLine($"Helipad helicopter has no beacon rig={GameObject.Find("navy_uh-1n").transform.Find("Aircraft ground beacon lights") == null}");
        report.AppendLine($"Fog remains off={!RenderSettings.fog}");
        File.WriteAllText(Work + "/validation.txt", report.ToString());
        Previews("after");
    }
    static void Previews(string suffix)
    {
        Capture(new Vector3(-3.2f, 2.1f, 23.5f), new Vector3(-10, 1.5f, 23), "server-room-" + suffix);
        Capture(new Vector3(10.6f, 2.1f, 27), new Vector3(13.3f, 1.7f, 20.5f), "east-room-" + suffix);
        Capture(new Vector3(-29, 2.5f, 5), new Vector3(-60, 3, 5), "hangar-" + suffix);
        Capture(new Vector3(61, 4, 5), new Vector3(51.66f, 2.8f, 15.58f), "ground-helicopter-" + suffix);
    }
    static void Capture(Vector3 position, Vector3 target, string name)
    {
        var go = new GameObject("Interior preview camera");
        var camera = go.AddComponent<Camera>();
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        go.transform.position = position; go.transform.LookAt(target);
        camera.nearClipPlane = .05f;
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

    static readonly Dictionary<AircraftLightPulse, int> observed = new Dictionary<AircraftLightPulse, int>();
    static void ObserveBlinking()
    {
        if (!Application.isPlaying || Time.time < .5f) return;
        foreach (var pulse in Object.FindObjectsByType<AircraftLightPulse>())
        {
            if (!pulse.source || !pulse.lens) continue;
            observed.TryGetValue(pulse, out int states);
            observed[pulse] = states | (pulse.source.enabled ? 1 : 2);
        }
        if (Time.time < 6) return;
        var report = new StringBuilder();
        report.AppendLine($"Observed aircraft lighting over {Time.time:F1} seconds of gameplay");
        foreach (var entry in observed)
            report.AppendLine($"{(entry.Value == 3 ? "PASS" : "FAIL")} {entry.Key.transform.root.name}/{entry.Key.name}: light visibly switched on and off; pattern={entry.Key.pattern}");
        report.AppendLine($"{(observed.Count == 6 && observed.Values.All(v => v == 3) ? "PASS" : "FAIL")} All six drone / helicopter flashing channels animate");
        File.WriteAllText(Work + "/runtime-blinking.txt", report.ToString());
        EditorApplication.update -= ObserveBlinking;
        SessionState.SetBool("InteriorBlinkCheck", false);
    }
}

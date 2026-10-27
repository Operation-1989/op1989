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

/// <summary>Explicit, repeatable lighting pass for the army base; does not run in builds.</summary>
public static class DuskLightingSetup
{
    const string Work = "Temp/DuskLighting";
    const string Folder = "Assets/DuskLighting";
    const string Rig = "Dusk Practical Lighting";

    [InitializeOnLoadMethod]
    static void Initialize()
    {
        EditorApplication.update += Poll;
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/ready.txt", "6");
    }

    static void Poll()
    {
        string path = Work + "/request.txt";
        if (!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Application.isPlaying) { EditorApplication.isPlaying = false; return; }
        string command = File.ReadAllText(path).Trim();
        File.Delete(path);
        try
        {
            if (command == "inspect") Inspect();
            if (command == "install") Install();
            if (command == "validate") Validate();
            if (command == "smoke") StartSmoke();
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    static void Inspect()
    {
        Directory.CreateDirectory(Work);
        var report = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        report.AppendLine($"SCENE {scene.path} playing={Application.isPlaying}");
        foreach (var go in scene.GetRootGameObjects())
            report.AppendLine($"ROOT {go.name} position={go.transform.position:F2} rotation={go.transform.eulerAngles:F1} bounds={BoundsOf(go)}");
        foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (go.name.Contains("Military+base") || go.name == "Intro Entrance Truck" || go.name == "Patrol Jeep (UAZ)" || go.GetComponent<DroneMapPatrol>())
                foreach (Transform child in go) report.AppendLine($"CHILD {go.name}/{child.name} bounds={BoundsOf(child.gameObject)}");
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            report.AppendLine($"LIGHT {light.name} {light.type} intensity={light.intensity} position={light.transform.position}");
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
    }

    [MenuItem("Operation 1989/Lighting/Apply Warm Dusk")]
    public static void Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode before applying dusk lighting.");
        Directory.CreateDirectory(Work);
        if (!File.Exists(Work + "/before-dusk.unity")) EditorSceneManager.SaveScene(scene, Work + "/before-dusk.unity", true);
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "DuskLighting");
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Apply warm dusk lighting");
        int undo = Undo.GetCurrentGroup();
        var old = GameObject.Find(Rig);
        if (old) Undo.DestroyObjectImmediate(old);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Where(t => t.name == "Dusk Vehicle Lights" || t.name == "Dusk Drone Lights").ToArray())
            Undo.DestroyObjectImmediate(t.gameObject);
        var root = new GameObject(Rig);
        Undo.RegisterCreatedObjectUndo(root, "Dusk lighting rig");
        // Replace the broad scene lights, retaining the physical searchlight housing.
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional || light.name == "Tower_Searchlight")
            {
                // Imported URP light data requires its Light component; disable the source
                // and record the prefab override instead of allowing Unity to recreate it.
                Undo.RecordObject(light, "Disable original scene light");
                light.enabled = false;
                light.intensity = 0;
                EditorUtility.SetDirty(light);
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            }
            else
            {
                Undo.RecordObject(light, "Warm imported fixture");
                light.color = new Color(1, .84f, .65f);
                light.intensity = Mathf.Min(light.intensity, 1.3f);
                light.range = Mathf.Min(light.range, 7);
                light.shadows = LightShadows.None;
                EditorUtility.SetDirty(light);
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            }
        }
        var sun = AddLight(root.transform, "Last sunlight", new Vector3(0, 30, 0), LightType.Directional, .48f, 0, true);
        sun.color = new Color(1f, .76f, .53f);
        sun.transform.rotation = Quaternion.Euler(7, 245, 0);
        sun.shadowStrength = .85f;
        RenderSettings.sun = sun;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.19f, .235f, .32f);
        RenderSettings.ambientEquatorColor = new Color(.14f, .14f, .17f);
        RenderSettings.ambientGroundColor = new Color(.065f, .057f, .05f);
        RenderSettings.reflectionIntensity = .4f;
        var sky = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Dusk Sky.mat");
        if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, Folder + "/Dusk Sky.mat"); }
        sky.SetColor("_SkyTint", new Color(.38f, .43f, .53f));
        sky.SetColor("_GroundColor", new Color(.12f, .10f, .09f));
        sky.SetFloat("_Exposure", .65f);
        sky.SetFloat("_AtmosphereThickness", 1.15f);
        sky.SetFloat("_SunSize", .025f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;

        var metal = Material("Fixture charcoal", new Color(.09f, .105f, .105f), false);
        var warm = Material("Warm white lens", new Color(1, .83f, .61f), true);
        var red = Material("Red marker lens", new Color(1, .035f, .015f), true);
        var green = Material("Green marker lens", new Color(.08f, 1, .3f), true);
        var roads = new GameObject("Road and path lamps").transform;
        roads.SetParent(root.transform, false);
        // Outside the carriageways and the entrance truck / jeep swept paths.
        for (int i = 0; i < 6; i++) Pole(roads, new Vector3(-70.5f, 0, -65 + i * 21), metal, warm);
        for (int i = 0; i < 6; i++) Pole(roads, new Vector3(72.5f, 0, -66 + i * 24), metal, warm);
        // The last grid station falls inside the control room; its light is wall mounted instead.
        for (int i = 0; i < 4; i++) Pole(roads, new Vector3(-8.8f, 0, -65 + i * 22), metal, warm);
        for (int i = 0; i < 5; i++) Pole(roads, new Vector3(-58 + i * 27, 0, -69), metal, warm);
        for (int i = 0; i < 4; i++) Pole(roads, new Vector3(-56 + i * 27, 0, 37), metal, warm);
        Pole(roads, new Vector3(-3, 0, -103), metal, warm);
        Pole(roads, new Vector3(11, 0, -103), metal, warm);

        var buildings = new GameObject("Building and watchtower fixtures").transform;
        buildings.SetParent(root.transform, false);
        var candidates = scene.GetRootGameObjects().Where(g =>
            g.name.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0 ||
            g.name == "Security_Guard_Room" || g.name == "fbxTower" ||
            g.name == "NATO_Shelter_V1" || g.name.StartsWith("TEnts+Free")).ToList();
        foreach (var pack in scene.GetRootGameObjects().Where(g => g.name.Contains("Military+base")))
            foreach (Transform child in pack.transform)
                if (child.name.IndexOf("barrack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.IndexOf("hangar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.StartsWith("container") || child.name == "military tent")
                    candidates.Add(child.gameObject);
        foreach (var building in candidates.Distinct())
        {
            var b = BoundsOf(building);
            // The HQ import includes oversized auxiliary meshes outside its walls.
            if (building.name == "building_1") b = new Bounds(new Vector3(0, 7.5f, 22.5f), new Vector3(30, 15, 14));
            if (b.size.x < 2 || b.size.z < 2) continue;
            float y = Mathf.Clamp(b.min.y + b.size.y * .55f, 2.7f, 5);
            WallLamp(buildings, building.name + " south", new Vector3(b.center.x, y, b.min.z - .22f), Vector3.back, metal, warm);
            if (b.size.x > 15) WallLamp(buildings, building.name + " west", new Vector3(b.min.x - .22f, y, b.center.z), Vector3.left, metal, warm);
            if (b.size.z > 15) WallLamp(buildings, building.name + " north", new Vector3(b.center.x, y, b.max.z + .22f), Vector3.forward, metal, warm);
        }
        WallLamp(buildings, "Gate watchtower", new Vector3(-65, 10.8f, 50.9f), Vector3.back, metal, warm);
        WallLamp(buildings, "Gatehouse entrance", new Vector3(9, 3, -87), Vector3.back, metal, warm);
        var housing = GameObject.Find("Tower_Searchlight_Body");
        if (housing)
        {
            var light = AddLight(buildings, "Watchtower directed beam", housing.transform.position + Vector3.back * .8f,
                LightType.Spot, 9, 35, true);
            light.spotAngle = 65;
            light.transform.rotation = Quaternion.Euler(48, 180, 0);
        }
        foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name == "Intro Entrance Truck" || t.name == "Patrol Jeep (UAZ)").ToArray())
            Vehicle(go, metal, warm, red);
        var drone = Object.FindAnyObjectByType<DroneMapPatrol>();
        if (drone)
        {
            var b = LocalBounds(drone.transform);
            var group = new GameObject("Dusk Drone Lights").transform;
            group.SetParent(drone.transform, false);
            Marker(group, "Port red", new Vector3(b.min.x, b.center.y, b.center.z), red, new Color(1, .04f, .02f));
            Marker(group, "Starboard green", new Vector3(b.max.x, b.center.y, b.center.z), green, new Color(.1f, 1, .3f));
            Marker(group, "Rear white", new Vector3(b.center.x, b.center.y, b.min.z), warm, new Color(1, .83f, .61f));
            var search = AddLight(group, "Downward survey light", new Vector3(b.center.x, b.min.y - .1f, b.center.z), LightType.Spot, 6, 25, false);
            search.transform.localRotation = Quaternion.Euler(90, 0, 0);
            search.spotAngle = 58;
            var pad = drone.launchPoint.parent;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Shape(buildings, "Landing pad marker", pad.position + new Vector3(x * 1.4f, .18f, z * 1.4f), new Vector3(.13f, .06f, .13f), warm);
        }

        // Let local beams cast shadows without paying for shadows on every path lamp.
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline)
        {
            Undo.RecordObject(pipeline, "Enable local light shadows");
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            serialized.ApplyModifiedProperties();
        }
        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undo);
        InteriorLightingSetup.Install();
        Validate();
    }

    static Material Material(string name, Color color, bool emission)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.color = color;
        mat.SetFloat("_Smoothness", .3f);
        if (emission)
        {
            mat.SetColor("_EmissionColor", color * 2.2f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            MaterialEditor.FixupEmissiveFlag(mat);
            mat.EnableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Light AddLight(Transform parent, string name, Vector3 position, LightType type, float intensity, float range, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var light = go.AddComponent<Light>();
        light.type = type;
        light.color = new Color(1, .84f, .65f); // Approximately 3200 K warm white.
        light.intensity = intensity;
        light.range = range;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.shadowBias = .04f;
        light.shadowNormalBias = .15f;
        light.spotAngle = 100;
        light.innerSpotAngle = 55;
        var data = go.AddComponent<UniversalAdditionalLightData>();
        var serialized = new SerializedObject(data);
        serialized.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return light;
    }

    static GameObject Shape(Transform parent, string name, Vector3 position, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name + " fixture light";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static void Pole(Transform parent, Vector3 position, Material metal, Material lens)
    {
        var go = new GameObject("Warm path lamp").transform;
        go.SetParent(parent, false);
        go.position = position;
        Shape(go, "Concrete plinth", new Vector3(0, .15f, 0), new Vector3(.5f, .3f, .5f), metal);
        Shape(go, "Steel post", new Vector3(0, 2.6f, 0), new Vector3(.12f, 5.2f, .12f), metal);
        Shape(go, "Shielded housing", new Vector3(0, 5.25f, 0), new Vector3(.85f, .16f, .65f), metal);
        Shape(go, "Warm diffuser", new Vector3(0, 5.15f, 0), new Vector3(.7f, .045f, .5f), lens);
        var light = AddLight(go, "Downlight", new Vector3(0, 5.05f, 0), LightType.Spot, 9.5f, 18, false);
        light.transform.localRotation = Quaternion.Euler(90, 0, 0);
        light.spotAngle = 120;
        light.innerSpotAngle = 75;
    }

    static void WallLamp(Transform parent, string name, Vector3 position, Vector3 outward, Material metal, Material lens)
    {
        var go = new GameObject(name + " wall light").transform;
        go.SetParent(parent, false);
        go.position = position;
        go.rotation = Quaternion.LookRotation(outward);
        Shape(go, "Wall bracket", Vector3.zero, new Vector3(.38f, .28f, .22f), metal);
        Shape(go, "Warm diffuser", new Vector3(0, -.14f, .08f), new Vector3(.3f, .07f, .25f), lens);
        bool shadows = name.StartsWith("building_1") || name.StartsWith("Security_Guard_Room");
        var light = AddLight(go, "Door and facade downlight", new Vector3(0, -.22f, .16f), LightType.Spot, 4, 11, shadows);
        light.transform.localRotation = Quaternion.Euler(65, 0, 0);
        light.spotAngle = 110;
    }

    static Bounds LocalBounds(Transform root)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var b = r.localBounds;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var p = root.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                        if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                    }
        }
        return bounds;
    }

    static void Vehicle(Transform vehicle, Material metal, Material warm, Material red)
    {
        var b = LocalBounds(vehicle);
        var group = new GameObject("Dusk Vehicle Lights").transform;
        group.SetParent(vehicle, false);
        // Imported static jeeps may use X as their longitudinal axis.
        bool alongX = vehicle.name != "Intro Entrance Truck" && vehicle.name != "Patrol Jeep (UAZ)" && b.size.x > b.size.z;
        Vector3 forward = alongX ? Vector3.right : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        float length = (alongX ? b.size.x : b.size.z) * .5f;
        float width = (alongX ? b.size.z : b.size.x) * .32f;
        var center = new Vector3(b.center.x, b.min.y + Mathf.Min(b.size.y * .4f, 1.25f), b.center.z);
        foreach (int sign in new[] { -1, 1 })
        {
            Vector3 p = center + forward * (length + .06f) + side * (width * sign);
            Shape(group, "Headlamp housing", p, new Vector3(.25f, .22f, .16f), metal);
            Shape(group, "Warm headlamp lens", p + forward * .09f, new Vector3(.19f, .16f, .08f), warm);
            var light = AddLight(group, "Low beam", p + forward * .16f, LightType.Spot, 7, 29, vehicle.name == "Intro Entrance Truck" || vehicle.name == "Patrol Jeep (UAZ)");
            light.transform.localRotation = Quaternion.LookRotation((forward + Vector3.down * .12f).normalized);
            light.spotAngle = 57;
            light.innerSpotAngle = 28;
            Marker(group, "Rear tail lamp", center - forward * (length + .06f) + side * width * sign, red, new Color(1, .035f, .015f));
        }
    }

    static void Marker(Transform parent, string name, Vector3 position, Material mat, Color color)
    {
        Shape(parent, name, position, new Vector3(.12f, .1f, .1f), mat);
        var light = AddLight(parent, name + " glow", position, LightType.Point, .25f, 1.2f, false);
        light.color = color;
    }

    [MenuItem("Operation 1989/Lighting/Validate Warm Dusk")]
    public static void Validate()
    {
        Directory.CreateDirectory(Work);
        var lights = Object.FindObjectsByType<Light>().Where(l => l.isActiveAndEnabled).ToArray();
        var report = new StringBuilder();
        report.AppendLine($"Scene={EditorSceneManager.GetActiveScene().path}; fog={RenderSettings.fog}; lights={lights.Length}");
        report.AppendLine($"Directional lights={lights.Count(l => l.type == LightType.Directional)}; spotlights={lights.Count(l => l.type == LightType.Spot)}");
        report.AppendLine($"Vehicle rigs={Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name == "Dusk Vehicle Lights")}");
        report.AppendLine($"Drone lights attached={Object.FindAnyObjectByType<DroneMapPatrol>()?.transform.Find("Dusk Drone Lights") != null}");
        foreach (var light in lights) report.AppendLine($"{light.name} type={light.type} intensity={light.intensity} range={light.range} position={light.transform.position:F2}");
        File.WriteAllText(Work + "/validation.txt", report.ToString());
        Capture(new Vector3(48, 35, -114), new Vector3(-5, 1, -25), "base-dusk");
        Capture(new Vector3(9, 2.3f, -108), new Vector3(0, 3, -60), "entrance-dusk");
        Capture(new Vector3(-22, 3, -49), new Vector3(-4, 2, -7), "paths-dusk");
    }

    static void Capture(Vector3 position, Vector3 target, string name)
    {
        var go = new GameObject("Dusk preview camera");
        var camera = go.AddComponent<Camera>();
        camera.allowHDR = true;
        camera.farClipPlane = 500;
        go.AddComponent<UniversalAdditionalCameraData>();
        go.transform.position = position;
        go.transform.LookAt(target);
        var rt = new RenderTexture(1280, 800, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Work + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(go);
        }
    }

    static double smokeStarted;
    static bool enteredPlay;
    static void StartSmoke()
    {
        SessionState.SetBool("DuskPreviousBackground", Application.runInBackground);
        enteredPlay = false;
        smokeStarted = EditorApplication.timeSinceStartup;
        EditorApplication.update += SmokeTick;
        EditorApplication.isPlaying = true;
        // Persist the pending check across Play mode's domain reload.
        SessionState.SetBool("DuskLightingSmoke", true);
    }

    [InitializeOnLoadMethod]
    static void ResumeSmoke()
    {
        if (!SessionState.GetBool("DuskLightingSmoke", false)) return;
        enteredPlay = false;
        smokeStarted = EditorApplication.timeSinceStartup;
        EditorApplication.update += SmokeTick;
    }

    static void SmokeTick()
    {
        if (EditorApplication.isCompiling) return;
        if (Application.isPlaying) enteredPlay = true;
        if (!enteredPlay || !Application.isPlaying) return;
        Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.time < 14 || EditorApplication.timeSinceStartup - smokeStarted < 18) return;
        EditorApplication.update -= SmokeTick;
        SessionState.SetBool("DuskLightingSmoke", false);
        try
        {
            var drone = Object.FindAnyObjectByType<DroneMapPatrol>();
            var jeep = GameObject.Find("Patrol Jeep (UAZ)");
            var truck = GameObject.Find("Intro Entrance Truck");
            var report = new StringBuilder();
            report.AppendLine($"PASS Gameplay runs at dusk; time={Time.time:F1}; fog={RenderSettings.fog}");
            report.AppendLine($"Drone lights follow patrol={drone && drone.transform.Find("Dusk Drone Lights")}; drone position={drone?.transform.position}");
            report.AppendLine($"Jeep lights follow vehicle={jeep && jeep.transform.Find("Dusk Vehicle Lights")}");
            report.AppendLine($"Truck lights follow vehicle={truck && truck.transform.Find("Dusk Vehicle Lights")}");
            report.AppendLine($"Directional lights={Object.FindObjectsByType<Light>().Count(l => l.type == LightType.Directional && l.isActiveAndEnabled)}");
            File.WriteAllText(Work + "/playmode.txt", report.ToString());
            var camera = Object.FindObjectsByType<Camera>().FirstOrDefault(c => c.isActiveAndEnabled && c.targetDisplay == 0);
            if (camera) Capture(camera.transform.position, camera.transform.position + camera.transform.forward * 20, "gameplay-dusk");
            if (drone) Capture(drone.transform.position + new Vector3(5, 3, -6), drone.transform.position, "drone-dusk");
        }
        catch (Exception e) { File.WriteAllText(Work + "/smoke-error.txt", e.ToString()); }
        finally
        {
            Application.runInBackground = SessionState.GetBool("DuskPreviousBackground", false);
            EditorApplication.isPlaying = false;
        }
    }
}

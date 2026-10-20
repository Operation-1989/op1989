using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class BaseJeepPatrolSetup
{
    const string Work = "Temp/JeepPatrol";
    const string Request = Work + "/request.txt";

    // A file request is created explicitly by tooling; importing this script alone changes nothing.
    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (File.Exists(Request)) EditorApplication.update += RunRequest;
    }

    static void RunRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        EditorApplication.update -= RunRequest;
        var command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "install") { Install(); Validate(); }
            else if (command == "validate") Validate();
            else if (command == "update-route") { UpdateRoute(); Validate(); }
            else if (command == "smoke") SmokeTestPlayMode();
            else throw new InvalidOperationException("Unknown jeep setup request: " + command);
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Operation 1989/Jeep/Install Base Road Patrol")]
    public static void Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        if (Object.FindAnyObjectByType<BaseRoadJeepPatrol>())
            throw new InvalidOperationException("A base jeep patrol already exists; edit its route rather than installing a duplicate.");
        Directory.CreateDirectory(Work);
        EditorSceneManager.SaveScene(scene, Work + "/before-jeep-patrol.unity", true);
        const string folder = "Assets/JeepPatrol";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "JeepPatrol");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D model/UAZfbx.fbx");
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/3D model/УАЗИК.png");
        if (!source || !texture) throw new InvalidOperationException("Committed UAZ model or texture is missing.");
        var root = new GameObject("Base Jeep Patrol");
        Undo.RegisterCreatedObjectUndo(root, "Install base jeep patrol");
        var routeRoot = new GameObject("Perimeter Road Route").transform;
        routeRoot.SetParent(root.transform, false);
        // Matches the marked perimeter: west road, north road, northeast dirt bend,
        // east road, then the staggered south roads above the main entrance.
        var corners = PatrolCorners();
        var points = RoundedRoute(corners);
        var surfaces = scene.GetRootGameObjects()
            .Where(go => go.name == "Plane" || go.name.StartsWith("Straight_Road"))
            .SelectMany(go => go.GetComponentsInChildren<Collider>()).ToArray();
        var waypoints = points.Select((p, i) => {
            p.y = SurfaceHeight(p, surfaces) + .02f;
            var point = new GameObject("Road " + i.ToString("00")).transform;
            point.SetParent(routeRoot, false); point.position = p; return point;
        }).ToArray();
        var vehicle = new GameObject("Patrol Jeep (UAZ)");
        vehicle.transform.SetParent(root.transform, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source, vehicle.transform);
        model.name = "UAZ Visual";
        // The source nose points toward -Z; the patrol vehicle's forward is +Z.
        model.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var bounds = BoundsOf(model);
        float scale = 4.5f / bounds.size.z;
        model.transform.localScale *= scale;
        bounds = BoundsOf(model);
        model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/UAZ Patrol.mat");
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "UAZ Patrol";
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .22f);
            AssetDatabase.CreateAsset(material, folder + "/UAZ Patrol.mat");
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        foreach (var camera in model.GetComponentsInChildren<Camera>()) camera.enabled = false;
        foreach (var light in model.GetComponentsInChildren<Light>()) light.enabled = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        bounds = BoundsOf(model);
        var collider = vehicle.AddComponent<BoxCollider>();
        collider.center = bounds.center;
        collider.size = new Vector3(bounds.size.x * .9f, bounds.size.y * .9f, bounds.size.z * .9f);
        var rb = vehicle.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false; rb.mass = 1500;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var patrol = vehicle.AddComponent<BaseRoadJeepPatrol>();
        patrol.waypoints = waypoints; patrol.groundSurfaces = surfaces;
        patrol.ResetPatrol();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = vehicle;
        Capture(vehicle.transform.position + new Vector3(7, 4, -7), vehicle.transform.position + Vector3.up, "jeep-placed");
        CaptureRoute(patrol);
        File.WriteAllText(Work + "/installed.txt", $"UAZ installed with {waypoints.Length} route points.\n");
    }

    static Vector3[] PatrolCorners() => new[] {
            new Vector3(-62, 0, -76.3f), new Vector3(-77.45f, 0, -76.3f),
            new Vector3(-77.45f, 0, 44.5f), new Vector3(20, 0, 44.5f),
            new Vector3(34, 0, 48), new Vector3(46, 0, 65),
            new Vector3(57, 0, 77), new Vector3(69, 0, 83),
            new Vector3(78, 0, 81), new Vector3(81.6f, 0, 73),
            // Use the south lane to clear the entrance truck when it is parked at (23, -77.8).
            new Vector3(81.6f, 0, -81.2f), new Vector3(8, 0, -81.2f),
            new Vector3(1, 0, -76.3f)
        };

    [MenuItem("Operation 1989/Jeep/Refresh Perimeter Waypoints")]
    public static void UpdateRoute()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit mode.");
        var patrol = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
        var points = RoundedRoute(PatrolCorners());
        if (!patrol || patrol.waypoints.Length != points.Count)
            throw new InvalidOperationException("Expected the installed perimeter route.");
        for (int i = 0; i < points.Count; i++)
        {
            Undo.RecordObject(patrol.waypoints[i], "Update jeep perimeter route");
            var p = points[i]; p.y = SurfaceHeight(p, patrol.groundSurfaces) + .02f;
            patrol.waypoints[i].position = p;
        }
        patrol.ResetPatrol();
        EditorSceneManager.MarkSceneDirty(patrol.gameObject.scene);
        EditorSceneManager.SaveScene(patrol.gameObject.scene);
        CaptureRoute(patrol);
    }

    static void CaptureRoute(BaseRoadJeepPatrol patrol)
    {
        var go = new GameObject("Route preview");
        var line = go.AddComponent<LineRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", Color.red);
        line.sharedMaterial = mat; line.widthMultiplier = .45f;
        line.positionCount = patrol.waypoints.Length; line.loop = true;
        line.SetPositions(patrol.waypoints.Select(p => new Vector3(p.position.x, 35, p.position.z)).ToArray());
        try { Capture(new Vector3(0, 230, -5), new Vector3(0, 0, -5), "patrol-route", true); }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(mat); }
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static float SurfaceHeight(Vector3 p, Collider[] surfaces)
    {
        float y = 0;
        foreach (var surface in surfaces)
            if (surface.Raycast(new Ray(p + Vector3.up * 2, Vector3.down), out var hit, 4) && hit.normal.y > .7f)
                y = Mathf.Max(y, hit.point.y);
        return y;
    }

    static List<Vector3> RoundedRoute(Vector3[] corners)
    {
        var points = new List<Vector3>();
        for (int i = 0; i < corners.Length; i++)
        {
            var p = corners[i];
            var previous = corners[(i + corners.Length - 1) % corners.Length];
            var next = corners[(i + 1) % corners.Length];
            float radius = Mathf.Min(3, Vector3.Distance(p, previous) * .3f, Vector3.Distance(p, next) * .3f);
            var enter = p + (previous - p).normalized * radius;
            var exit = p + (next - p).normalized * radius;
            for (int step = 0; step <= 6; step++)
            {
                float t = step / 6f;
                points.Add((1-t)*(1-t)*enter + 2*(1-t)*t*p + t*t*exit);
            }
        }
        return points;
    }

    [MenuItem("Operation 1989/Jeep/Validate Road Patrol")]
    public static void Validate()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit mode.");
        var patrol = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
        if (!patrol) throw new InvalidOperationException("Install the patrol first.");
        var report = new StringBuilder();
        var visited = new HashSet<int>();
        var savedPosition = patrol.transform.position;
        var savedRotation = patrol.transform.rotation;
        var intro = Object.FindAnyObjectByType<IntroTruckSequence>();
        var truckPosition = intro ? intro.truck.position : Vector3.zero;
        var truckRotation = intro ? intro.truck.rotation : Quaternion.identity;
        try
        {
            // A patrol must still loop once the entrance truck occupies its parking place.
            if (intro)
                intro.truck.SetPositionAndRotation(intro.route[intro.route.Length - 1].position,
                    Quaternion.LookRotation(intro.route[intro.route.Length - 1].position - intro.route[intro.route.Length - 2].position));
            patrol.ResetPatrol();
            for (int step = 0; step < 16000 && patrol.CompletedLaps < 2; step++)
            {
                Physics.SyncTransforms();
                patrol.StepPatrol(.05f);
                visited.Add(patrol.TargetPoint);
                if (!string.IsNullOrEmpty(patrol.BlockedBy))
                {
                    report.AppendLine($"BLOCKED {patrol.BlockedBy} point={patrol.TargetPoint} at={patrol.transform.position}");
                    break;
                }
            }
            report.AppendLine($"{(patrol.CompletedLaps >= 2 ? "PASS" : "FAIL")} two complete patrol laps: {patrol.CompletedLaps}");
            if (intro) report.AppendLine($"{(patrol.CompletedLaps >= 2 ? "PASS" : "FAIL")} patrol clears the parked entrance truck");
            report.AppendLine($"{(visited.Count == patrol.waypoints.Length ? "PASS" : "FAIL")} every waypoint visited: {visited.Count}/{patrol.waypoints.Length}");
            report.AppendLine($"Distance={patrol.DistanceTravelled:F2}m position={patrol.transform.position:F3}");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Patrol validation obstacle";
            try
            {
                patrol.ResetPatrol();
                blocker.transform.position = patrol.transform.position + patrol.transform.forward * 4 + Vector3.up;
                blocker.transform.localScale = new Vector3(3, 2, 2);
                bool stopped = false;
                for (int step = 0; step < 100; step++)
                {
                    Physics.SyncTransforms(); patrol.StepPatrol(.05f);
                    if (patrol.BlockedBy == blocker.name) { stopped = true; break; }
                }
                report.AppendLine($"{(stopped ? "PASS" : "FAIL")} yields to obstacle without crossing it");
            }
            finally { Object.DestroyImmediate(blocker); }
            Physics.SyncTransforms();
            var before = patrol.transform.position;
            for (int step = 0; step < 50; step++) { Physics.SyncTransforms(); patrol.StepPatrol(.05f); }
            report.AppendLine($"{(Vector3.Distance(before, patrol.transform.position) > 1 ? "PASS" : "FAIL")} resumes after obstacle clears");
            patrol.ResetPatrol();
            report.AppendLine($"{(patrol.CompletedLaps == 0 && patrol.CurrentSpeed == 0 ? "PASS" : "FAIL")} replay resets patrol state");
        }
        finally
        {
            if (intro) intro.truck.SetPositionAndRotation(truckPosition, truckRotation);
            patrol.transform.SetPositionAndRotation(savedPosition, savedRotation);
            Physics.SyncTransforms();
            File.WriteAllText(Work + "/validation.txt", report.ToString());
        }
        Debug.Log(report.ToString());
    }

    [MenuItem("Operation 1989/Jeep/Smoke Test Play Mode")]
    public static void SmokeTestPlayMode()
    {
        // Exercise the real Rigidbody loop and return to Edit mode after four game seconds.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start the smoke test from Edit mode.");
        SessionState.SetInt("BaseJeepSmokeStage", 1);
        EditorApplication.update -= SmokeTick;
        EditorApplication.update += SmokeTick;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void ResumeSmokeTest()
    {
        if (SessionState.GetInt("BaseJeepSmokeStage", 0) > 0) EditorApplication.update += SmokeTick;
    }

    static void SmokeTick()
    {
        int stage = SessionState.GetInt("BaseJeepSmokeStage", 0);
        if (stage == 0) { EditorApplication.update -= SmokeTick; return; }
        if (!Application.isPlaying)
        {
            if (stage == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetInt("BaseJeepSmokeStage", 0);
                EditorApplication.update -= SmokeTick;
                // Scene reload is disabled in this project; put the patrol back at its saved start.
                var p = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
                if (p) p.ResetPatrol();
                File.AppendAllText(Work + "/playmode.txt", "Returned to Edit mode.\n");
            }
            return;
        }
        var patrol = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
        if (!patrol) return;
        if (stage == 1)
        {
            SessionState.SetFloat("BaseJeepSmokeStart", Time.time);
            SessionState.SetVector3("BaseJeepSmokePosition", patrol.transform.position);
            SessionState.SetInt("BaseJeepSmokeStage", 2);
        }
        else if (stage == 2 && Time.time - SessionState.GetFloat("BaseJeepSmokeStart", 0) >= 4)
        {
            float distance = Vector3.Distance(SessionState.GetVector3("BaseJeepSmokePosition", Vector3.zero), patrol.transform.position);
            var intro = Object.FindAnyObjectByType<IntroTruckSequence>();
            string result = $"{(distance > 3 ? "PASS" : "FAIL")} kinematic jeep moves in Play mode: {distance:F2}m\n";
            result += $"{(patrol.GetComponent<Rigidbody>().isKinematic ? "PASS" : "FAIL")} kinematic physics retained\n";
            result += $"{(intro && intro.playerCamera.targetDisplay == 0 && intro.introCamera.targetDisplay == 0 ? "PASS" : "FAIL")} intro and FPV remain assigned to Display 1\n";
            File.WriteAllText(Work + "/playmode.txt", result);
            Capture(patrol.transform.position + new Vector3(7, 4, -7), patrol.transform.position + Vector3.up, "jeep-runtime");
            SessionState.SetInt("BaseJeepSmokeStage", 3);
            EditorApplication.isPlaying = false;
        }
        EditorApplication.QueuePlayerLoopUpdate();
    }

    [MenuItem("Operation 1989/Jeep/Inspect Base and Model")]
    public static void Inspect()
    {
        Directory.CreateDirectory(Work);
        var report = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        report.AppendLine($"SCENE {scene.path} dirty={scene.isDirty}");
        foreach (var go in scene.GetRootGameObjects())
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            report.AppendLine($"ROOT {go.name} position={go.transform.position:F3} bounds={bounds} colliders={go.GetComponentsInChildren<Collider>().Length}");
        }
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D model/UAZfbx.fbx");
        var model = Object.Instantiate(asset);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var t in model.GetComponentsInChildren<Transform>())
        {
            var r = t.GetComponent<Renderer>();
            report.AppendLine($"MODEL {t.name} localPosition={t.localPosition:F3} rotation={t.localEulerAngles:F3} scale={t.localScale:F3}" +
                (r ? $" bounds={r.bounds} materials={string.Join(",", r.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "null"))}" : ""));
        }
        Object.DestroyImmediate(model);
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
        Capture(new Vector3(0, 230, -5), new Vector3(0, 0, -5), "base-top", true);
    }

    public static void Capture(Vector3 position, Vector3 target, string name, bool top = false)
    {
        Directory.CreateDirectory(Work);
        var go = new GameObject("Jeep inspection camera");
        var camera = go.AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = top ? Quaternion.Euler(90, 0, 0) : Quaternion.LookRotation(target - position);
        camera.orthographic = top;
        camera.orthographicSize = 108;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 500;
        var rt = new RenderTexture(1200, 1200, 24);
        camera.targetTexture = rt;
        var previous = RenderTexture.active;
        Texture2D texture = null;
        try
        {
            camera.Render();
            RenderTexture.active = rt;
            texture = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Work + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(go);
            if (texture) Object.DestroyImmediate(texture);
            Object.DestroyImmediate(rt);
        }
    }
}

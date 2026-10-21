using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DronePatrolSetup
{
    const string Work = "Temp/DronePatrol";
    // Inset from the concrete perimeter fences, including clearance for the whole drone.
    const float PatrolWest = -78, PatrolEast = 78, PatrolSouth = -82, PatrolNorth = 74;
    [InitializeOnLoadMethod]
    static void Resume() => EditorApplication.update += Poll;

    static void Poll()
    {
        string request = Work + "/request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var command = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "install") Install();
            else if (command == "validate") Validate();
            else if (command == "smoke") StartSmoke();
            else if (command == "refine") Refine();
            else if (command == "tune-visibility") TuneVisibility();
            else if (command == "confine-route") ConfineRoute();
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material Material(string name, Color color)
    {
        string path = "Assets/DronePatrol/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    [MenuItem("Operation 1989/Drone/Install Tower Launch and Map Patrol")]
    public static void Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        if (Object.FindAnyObjectByType<DroneMapPatrol>()) throw new InvalidOperationException("Drone already installed. Edit the existing patrol.");
        var tower = GameObject.Find("TOWER HEAD and door");
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D model/RUKO F11GIM.fbx");
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Vehicles/DroneFlying.mp3");
        if (!tower || !modelAsset || !clip) throw new InvalidOperationException("Tower, RUKO model or drone recording missing.");
        EditorSceneManager.SaveScene(scene, Work + "/before-drone.unity", true);
        if (!AssetDatabase.IsValidFolder("Assets/DronePatrol")) AssetDatabase.CreateFolder("Assets", "DronePatrol");
        var deck = Material("Pad charcoal", new Color(.12f, .16f, .13f));
        var yellow = Material("Pad safety yellow", new Color(.95f, .66f, .08f));
        var white = Material("Pad marking", new Color(.86f, .9f, .82f));
        var body = Material("Drone graphite", new Color(.19f, .22f, .23f));
        var black = Material("Drone rotors", new Color(.045f, .055f, .06f));
        var root = new GameObject("Drone Surveillance Patrol");
        Undo.RegisterCreatedObjectUndo(root, "Install surveillance drone");
        var bounds = BoundsOf(tower);
        var pad = new GameObject("Watchtower Drone Launch Pad").transform;
        pad.SetParent(root.transform, false);
        pad.position = new Vector3(bounds.center.x, bounds.max.y + .16f, bounds.center.z);
        Box(pad, "Raised landing deck", Vector3.zero, new Vector3(3.2f, .2f, 3.2f), deck);
        for (int side = -1; side <= 1; side += 2)
        {
            Box(pad, "Safety edge X", new Vector3(side * 1.5f, .115f, 0), new Vector3(.12f, .025f, 3.1f), yellow);
            Box(pad, "Safety edge Z", new Vector3(0, .115f, side * 1.5f), new Vector3(3.1f, .025f, .12f), yellow);
            Box(pad, "H upright", new Vector3(side * .46f, .12f, 0), new Vector3(.16f, .025f, 1.4f), white);
        }
        Box(pad, "H crossbar", new Vector3(0, .12f, 0), new Vector3(.92f, .025f, .16f), white);
        var launch = new GameObject("Launch Position").transform;
        launch.SetParent(pad, false);
        launch.localPosition = Vector3.up * .16f;
        var drone = new GameObject("RUKO Survey Drone");
        drone.transform.SetParent(root.transform, false);
        drone.transform.position = launch.position;
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        visual.name = "RUKO Drone Visual";
        visual.transform.SetParent(drone.transform, false);
        var modelBounds = BoundsOf(visual);
        visual.transform.localScale *= 1.8f / Mathf.Max(modelBounds.size.x, modelBounds.size.z);
        modelBounds = BoundsOf(visual);
        visual.transform.position += new Vector3(drone.transform.position.x - modelBounds.center.x,
            launch.position.y - modelBounds.min.y, drone.transform.position.z - modelBounds.center.z);
        foreach (var r in visual.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(_ => r.name.Contains("Blade") || r.name.Contains("Lens") ? black : body).ToArray();
        // Each imported propeller mesh rotates about its own bounds center.
        var rotors = visual.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith("Blades")).Select(r =>
        {
            var pivot = new GameObject(r.name + " Spin Pivot").transform;
            pivot.SetParent(drone.transform, false);
            pivot.position = r.bounds.center;
            r.transform.SetParent(pivot, true);
            return pivot;
        }).ToArray();
        var route = new GameObject("Map Coverage Sweep Route").transform;
        route.SetParent(root.transform, false);
        var points = new System.Collections.Generic.List<Transform>();
        // Ten overlapping camera swaths span the entire base, including the entrance and northeast bend.
        for (int row = 0; row < 10; row++)
        {
            float z = Mathf.Lerp(PatrolSouth, PatrolNorth, row / 9f);
            for (int end = 0; end < 2; end++)
            {
                float x = ((row % 2 == 0) == (end == 0)) ? PatrolWest : PatrolEast;
                var point = new GameObject("Sweep " + (row + 1) + (end == 0 ? " start" : " end")).transform;
                point.SetParent(route, false);
                point.position = new Vector3(x, 18, z);
                points.Add(point);
            }
        }
        // Return down the western edge to restart the same complete survey.
        var home = new GameObject("West edge return").transform;
        home.SetParent(route, false);
        home.position = new Vector3(PatrolWest, 18, PatrolSouth);
        points.Add(home);
        var patrol = drone.AddComponent<DroneMapPatrol>();
        patrol.launchPoint = launch;
        patrol.waypoints = points.ToArray();
        patrol.rotors = rotors;
        patrol.flyingClip = clip;
        patrol.cruiseLoop = BakeLoop(clip);
        var camGo = new GameObject("Drone View Camera - Display 3");
        camGo.transform.SetParent(drone.transform, false);
        camGo.transform.localPosition = new Vector3(0, 2.4f, -4.8f);
        camGo.transform.localRotation = Quaternion.Euler(26, 0, 0);
        var camera = camGo.AddComponent<Camera>();
        camera.fieldOfView = 78;
        camera.nearClipPlane = .08f;
        camera.farClipPlane = 400;
        camera.targetDisplay = 2;
        camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        patrol.droneCamera = camera;
        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>();
        if (ambience)
        {
            Undo.RecordObject(ambience, "Quiet aircraft ambience for drone");
            ambience.flybyVolume = .3f;
            ambience.flybyInterval = new Vector2(60, 60);
            EditorUtility.SetDirty(ambience);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        TuneVisibility();
        Capture(pad.position + new Vector3(9, 6, -10), pad.position, "launch-pad");
    }

    [MenuItem("Operation 1989/Drone/Apply Lower Visible Patrol")]
    public static void TuneVisibility()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Tune the drone in Edit mode.");
        var patrol = Object.FindAnyObjectByType<DroneMapPatrol>();
        if (!patrol) throw new InvalidOperationException("Install the drone first.");
        var corners = new System.Collections.Generic.List<Vector3>();
        for (int row = 0; row < 10; row++)
            for (int end = 0; end < 2; end++)
                corners.Add(new Vector3(((row % 2 == 0) == (end == 0)) ? PatrolWest : PatrolEast, 18, Mathf.Lerp(PatrolSouth, PatrolNorth, row / 9f)));
        corners.Add(new Vector3(PatrolWest, 18, PatrolSouth));
        var positions = new System.Collections.Generic.List<Vector3>();
        var renderers = Object.FindObjectsByType<Renderer>().Where(r => !r.transform.IsChildOf(patrol.transform) &&
            r.bounds.size.x < 28 && r.bounds.size.z < 28 && r.bounds.max.y < 45).ToArray();
        for (int i = 0; i < corners.Count - 1; i++)
        {
            int count = Mathf.CeilToInt(Vector3.Distance(corners[i], corners[i + 1]) / 6);
            for (int step = 0; step < count; step++)
            {
                var p = Vector3.Lerp(corners[i], corners[i + 1], (float)step / count);
                // Stay low over open ground, rising only where roofs or tree canopies require it.
                if (Physics.SphereCast(new Vector3(p.x, 100, p.z), 2, Vector3.down, out var ground, 100, ~0, QueryTriggerInteraction.Ignore))
                    p.y = Mathf.Max(p.y, ground.point.y + 4);
                foreach (var r in renderers)
                {
                    var b = r.bounds;
                    if (p.x > b.min.x - 2 && p.x < b.max.x + 2 && p.z > b.min.z - 2 && p.z < b.max.z + 2)
                        p.y = Mathf.Max(p.y, b.max.y + 4);
                }
                positions.Add(p);
            }
        }
        // Anticipate climbs/descents so there are no abrupt vertical steps near an obstacle.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < positions.Count; i++)
                foreach (int next in new[] { (i + 1) % positions.Count, (i + positions.Count - 1) % positions.Count })
                {
                    var p = positions[next];
                    float horizontal = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(positions[i].x, positions[i].z));
                    p.y = Mathf.Max(p.y, positions[i].y - horizontal * .35f);
                    positions[next] = p;
                }
        // Check full segments as well as samples, raising both ends over any intersecting collider.
        for (int pass = 0; pass < 4; pass++)
            for (int i = 0; i < positions.Count; i++)
            {
                int next = (i + 1) % positions.Count;
                var delta = positions[next] - positions[i];
                if (delta.magnitude > .01f && Physics.SphereCast(positions[i], 1, delta.normalized, out var obstacle, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    float height = obstacle.collider.bounds.max.y + 4;
                    var a = positions[i]; var b = positions[next];
                    a.y = Mathf.Max(a.y, height); b.y = Mathf.Max(b.y, height);
                    positions[i] = a; positions[next] = b;
                }
            }
        var route = patrol.waypoints[0].parent;
        Undo.RecordObject(patrol, "Lower drone patrol and increase audibility");
        foreach (var child in route.Cast<Transform>().ToArray()) Undo.DestroyObjectImmediate(child.gameObject);
        patrol.waypoints = positions.Select((p, i) =>
        {
            var t = new GameObject("Survey " + i.ToString("D3")).transform;
            t.SetParent(route, false); t.position = p;
            Undo.RegisterCreatedObjectUndo(t.gameObject, "Create lower survey route");
            return t;
        }).ToArray();
        patrol.speed = 6;
        patrol.volume = .95f;
        var rig = patrol.transform.Find("Drone Visual Rig");
        if (!rig)
        {
            rig = new GameObject("Drone Visual Rig").transform;
            rig.SetParent(patrol.transform, false);
            var visual = patrol.transform.Find("RUKO Drone Visual");
            if (visual) visual.SetParent(rig, true);
            foreach (var rotor in patrol.rotors) if (rotor) rotor.SetParent(rig, true);
        }
        rig.localScale = Vector3.one * 1.35f;
        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>();
        if (ambience)
        {
            Undo.RecordObject(ambience, "Soften helicopter ambience");
            ambience.helicopterVolumeMultiplier = .3f;
            ambience.helicopterFadeInSeconds = 4;
            EditorUtility.SetDirty(ambience);
        }
        patrol.ResetFlight();
        EditorUtility.SetDirty(patrol);
        EditorSceneManager.MarkSceneDirty(patrol.gameObject.scene);
        EditorSceneManager.SaveScene(patrol.gameObject.scene);
        Validate();
        File.WriteAllText(Work + "/visibility.txt", $"Waypoints={positions.Count} altitude min={positions.Min(p => p.y):F1} max={positions.Max(p => p.y):F1} speed={patrol.speed} visual width=2.43 drone volume={patrol.volume} helicopter volume={ambience.VolumeFor(ambience.helicopterFlyby):F2}\n");
    }

    [MenuItem("Operation 1989/Drone/Confine Existing Route to Base")]
    public static void ConfineRoute()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Confine the route in Edit mode.");
        var patrol = Object.FindAnyObjectByType<DroneMapPatrol>();
        if (!patrol || patrol.waypoints == null) throw new InvalidOperationException("Drone route missing.");
        var oldPositions = patrol.waypoints.Select(p => p.position).ToArray();
        var positions = oldPositions.Select(p => new Vector3(Mathf.Clamp(p.x, PatrolWest, PatrolEast), p.y,
            Mathf.Clamp(p.z, PatrolSouth, PatrolNorth))).ToArray();
        // Retain the existing altitude profile; only add local clearance if moving an outer
        // segment inward puts it over a roof or canopy that was outside its previous path.
        var obstacles = Object.FindObjectsByType<Renderer>().Where(r => !r.transform.IsChildOf(patrol.transform) &&
            r.bounds.size.x < 28 && r.bounds.size.z < 28 && r.bounds.max.y < 45).ToArray();
        for (int i = 0; i < positions.Length; i++)
        {
            if (positions[i] == oldPositions[i]) continue;
            var p = positions[i];
            if (Physics.SphereCast(new Vector3(p.x, 100, p.z), 2, Vector3.down, out var surface, 100, ~0, QueryTriggerInteraction.Ignore))
                p.y = Mathf.Max(p.y, surface.point.y + 4);
            foreach (var r in obstacles)
            {
                var b = r.bounds;
                if (p.x > b.min.x - 2 && p.x < b.max.x + 2 && p.z > b.min.z - 2 && p.z < b.max.z + 2)
                    p.y = Mathf.Max(p.y, b.max.y + 4);
            }
            positions[i] = p;
        }
        for (int pass = 0; pass < 4; pass++)
            for (int i = 0; i < positions.Length; i++)
            {
                int next = (i + 1) % positions.Length;
                var delta = positions[next] - positions[i];
                if (delta.magnitude > .01f && Physics.SphereCast(positions[i], 1.3f, delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    var a = positions[i]; var b = positions[next];
                    a.y = Mathf.Max(a.y, hit.collider.bounds.max.y + 4);
                    b.y = Mathf.Max(b.y, hit.collider.bounds.max.y + 4);
                    positions[i] = a; positions[next] = b;
                }
            }
        Undo.RecordObjects(patrol.waypoints.Cast<Object>().ToArray(), "Keep drone patrol inside base");
        for (int i = 0; i < positions.Length; i++) patrol.waypoints[i].position = positions[i];
        var probe = new GameObject("Drone route boundary verification");
        probe.hideFlags = HideFlags.HideAndDontSave;
        var report = new StringBuilder();
        bool confined = true, clear = true;
        try
        {
            var flight = probe.AddComponent<DroneMapPatrol>();
            flight.launchPoint = patrol.launchPoint;
            flight.waypoints = patrol.waypoints;
            flight.speed = patrol.speed;
            flight.launchDelay = patrol.launchDelay;
            flight.spoolSeconds = patrol.spoolSeconds;
            flight.climbSeconds = patrol.climbSeconds;
            flight.ResetFlight();
            for (int step = 0; step < 10000 && flight.CompletedSweeps < 2; step++)
            {
                flight.StepFlight(.1f);
                var p = flight.transform.position;
                confined &= p.x >= PatrolWest - .001f && p.x <= PatrolEast + .001f && p.z >= PatrolSouth - .001f && p.z <= PatrolNorth + .001f;
            }
            confined &= flight.CompletedSweeps >= 2;
            for (int i = 0; i < positions.Length; i++)
            {
                var delta = positions[(i + 1) % positions.Length] - positions[i];
                if (delta.magnitude > .01f && Physics.SphereCast(positions[i], 1.3f, delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                { clear = false; report.AppendLine("OBSTACLE " + hit.collider.name); }
            }
            report.AppendLine((confined ? "PASS " : "FAIL ") + "Takeoff and two complete patrol sweeps stay inside the inset base boundary");
            report.AppendLine((clear ? "PASS " : "FAIL ") + "All route segments clear with drone body clearance");
            report.AppendLine($"Bounds X={PatrolWest}..{PatrolEast} Z={PatrolSouth}..{PatrolNorth}; route points={positions.Length}");
        }
        finally { Object.DestroyImmediate(probe); }
        File.WriteAllText(Work + "/boundary-validation.txt", report.ToString());
        if (!confined || !clear)
        {
            for (int i = 0; i < oldPositions.Length; i++) patrol.waypoints[i].position = oldPositions[i];
            throw new InvalidOperationException("Route boundary validation failed; original route restored. " + report);
        }
        EditorSceneManager.MarkSceneDirty(patrol.gameObject.scene);
        EditorSceneManager.SaveScene(patrol.gameObject.scene);
        Debug.Log(report.ToString());
    }

    static void Refine()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Refine in Edit mode.");
        var patrol = Object.FindAnyObjectByType<DroneMapPatrol>();
        var tower = GameObject.Find("TOWER HEAD and door");
        var b = BoundsOf(tower);
        var pad = patrol.launchPoint.parent;
        Undo.RecordObjects(new Object[] { pad, patrol.transform, patrol.droneCamera.transform }, "Place drone on center road watchtower");
        pad.position = new Vector3(b.center.x, b.max.y + .16f, b.center.z);
        patrol.droneCamera.transform.localPosition = new Vector3(0, 2.4f, -4.8f);
        patrol.droneCamera.transform.localRotation = Quaternion.Euler(26, 0, 0);
        patrol.ResetFlight();
        EditorSceneManager.MarkSceneDirty(patrol.gameObject.scene);
        EditorSceneManager.SaveScene(patrol.gameObject.scene);
        Validate();
        Capture(pad.position + new Vector3(9, 6, -10), pad.position, "launch-pad");
        Capture(pad.position + new Vector3(12, 7, -15), pad.position - Vector3.up * 3, "watchtower-launch");
    }

    static AudioClip BakeLoop(AudioClip source)
    {
        source.LoadAudioData();
        int channels = source.channels, rate = source.frequency;
        int frames = rate * 20, overlap = rate / 4;
        var samples = new float[(frames + overlap) * channels];
        if (!source.GetData(samples, 12 * rate)) throw new InvalidOperationException("Could not read drone recording.");
        // Fold the tail onto the start so the sustained section has a smooth repeating seam.
        for (int frame = 0; frame < overlap; frame++)
        {
            float t = (float)frame / overlap;
            for (int c = 0; c < channels; c++)
                samples[frame * channels + c] = Mathf.Lerp(samples[(frames + frame) * channels + c], samples[frame * channels + c], t);
        }
        string path = "Assets/DronePatrol/DroneCruiseLoop.wav";
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            int bytes = frames * channels * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)channels); writer.Write(rate);
            writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            for (int i = 0; i < frames * channels; i++) writer.Write((short)(Mathf.Clamp(samples[i], -1, 1) * 32767));
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    [MenuItem("Operation 1989/Drone/Validate Patrol")]
    public static void Validate()
    {
        var patrol = Object.FindAnyObjectByType<DroneMapPatrol>();
        if (!patrol) throw new InvalidOperationException("No drone patrol installed.");
        var report = new StringBuilder();
        Action<string, bool> check = (name, pass) => report.AppendLine((pass ? "PASS " : "FAIL ") + name);
        check("Display 3 camera without extra listener", patrol.droneCamera && patrol.droneCamera.targetDisplay == 2 && !patrol.droneCamera.GetComponent<AudioListener>());
        check("Takeoff and sustained audio assigned", patrol.flyingClip && patrol.cruiseLoop);
        check("Intruder detection deferred", !patrol.GetComponent<Operation1989.Interaction.AI.DroneSpotter>());
        bool clear = true;
        for (int i = 0; i < patrol.waypoints.Length; i++)
        {
            var a = patrol.waypoints[i].position;
            var delta = patrol.waypoints[(i + 1) % patrol.waypoints.Length].position - a;
            if (delta.sqrMagnitude > .01f && Physics.SphereCast(a, 1, delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            { clear = false; report.AppendLine("OBSTACLE " + hit.collider.name); }
        }
        check("All sweep segments clear of colliders", clear);
        var position = patrol.transform.position; var rotation = patrol.transform.rotation;
        patrol.ResetFlight();
        patrol.StepFlight(1);
        check("Drone initially rests on pad", Vector3.Distance(patrol.transform.position, patrol.launchPoint.position) < .01f);
        for (int i = 0; i < 35; i++) patrol.StepFlight(.1f);
        check("Visible vertical takeoff", patrol.transform.position.y > patrol.launchPoint.position.y + .2f && patrol.transform.position.y < patrol.waypoints[0].position.y);
        int steps = 0;
        while (patrol.CompletedSweeps == 0 && steps++ < 8000) patrol.StepFlight(.1f);
        check("Entire sweep completes and repeats", patrol.CompletedSweeps > 0);
        check("Flight follows lower safe survey altitude", patrol.transform.position.y >= 18 && patrol.transform.position.y <= patrol.waypoints.Max(p => p.position.y) + .01f);
        report.AppendLine($"Route points={patrol.waypoints.Length} survey seconds={steps * .1f:F1} launch={patrol.launchPoint.position}");
        patrol.ResetFlight();
        patrol.transform.SetPositionAndRotation(position, rotation);
        File.WriteAllText(Work + "/validation.txt", report.ToString());
        Debug.Log(report.ToString());
    }

    static double smokeStart;
    static bool priorBackground;
    static void StartSmoke()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start drone smoke test from Edit mode.");
        SessionState.SetBool("DroneSmoke", true);
        EditorApplication.playModeStateChanged += SmokeState;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void ResumeSmoke()
    {
        if (SessionState.GetBool("DroneSmoke", false)) EditorApplication.playModeStateChanged += SmokeState;
    }

    static void SmokeState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= SmokeState;
        smokeStart = EditorApplication.timeSinceStartup;
        priorBackground = Application.runInBackground;
        Application.runInBackground = true;
        EditorApplication.update += SmokeTick;
    }

    static void SmokeTick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= SmokeTick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        var drone = Object.FindAnyObjectByType<DroneMapPatrol>();
        if (!drone || EditorApplication.timeSinceStartup - smokeStart < 18 || drone.FlightTime < 14) return;
        var report = new StringBuilder();
        report.AppendLine($"Flight time={drone.FlightTime} position={drone.transform.position} target={drone.TargetPoint}");
        var sources = drone.GetComponentsInChildren<AudioSource>();
        foreach (var s in sources) report.AppendLine($"AUDIO {s.name} playing={s.isPlaying} loop={s.loop} spatial={s.spatialBlend} volume={s.volume} time={s.time}");
        report.AppendLine((drone.Cruising && sources.Any(s => s.isPlaying && s.loop && s.spatialBlend == 1) ? "PASS" : "FAIL") + " Runtime cruise and spatial loop");
        report.AppendLine((drone.droneCamera.isActiveAndEnabled && drone.droneCamera.targetDisplay == 2 ? "PASS" : "FAIL") + " Runtime Display 3 camera");
        var truck = Object.FindAnyObjectByType<IntroTruckAudio>();
        var localSources = sources.Concat(truck ? truck.GetComponents<AudioSource>() : new AudioSource[0]).ToArray();
        foreach (var source in localSources)
        {
            var curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            float near = curve.Evaluate(source.minDistance / source.maxDistance);
            float middle = curve.Evaluate(.53f);
            float far = curve.Evaluate(1.2f);
            bool pass = source.spatialBlend == 1 && source.rolloffMode == AudioRolloffMode.Custom &&
                near > .99f && middle > 0 && middle < .1f && far == 0;
            report.AppendLine((pass ? "PASS " : "FAIL ") + $"Distance falloff {source.name}: near={near:F3} moderate={middle:F3} beyond={far:F3} cutoff={source.maxDistance}");
        }
        var listeners = Object.FindObjectsByType<AudioListener>().Where(l => l.isActiveAndEnabled).ToArray();
        report.AppendLine((listeners.Length == 1 && !listeners[0].transform.IsChildOf(drone.transform) ? "PASS " : "FAIL ") + "One listener independent of drone camera");
        File.WriteAllText(Work + "/playmode.txt", report.ToString());
        Capture(drone.transform.position + new Vector3(4, 2, -5), drone.transform.position, "drone-flying");
        Capture(new Vector3(drone.transform.position.x + 12, 1.7f, drone.transform.position.z + 18), drone.transform.position, "drone-player-height");
        Capture(drone.droneCamera.transform.position, drone.droneCamera.transform.position + drone.droneCamera.transform.forward * 30, "drone-view");
        SessionState.SetBool("DroneSmoke", false);
        Application.runInBackground = priorBackground;
        EditorApplication.update -= SmokeTick;
        EditorApplication.isPlaying = false;
    }

    static void Inspect()
    {
        var report = new StringBuilder();
        report.AppendLine("SCENE " + EditorSceneManager.GetActiveScene().path + " playing=" + Application.isPlaying);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.name.ToLowerInvariant().Contains("tower") || t.name.ToLowerInvariant().Contains("terrain"))
            {
                var renderers = t.GetComponentsInChildren<Renderer>();
                var b = new Bounds(t.position, Vector3.zero);
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                report.AppendLine($"OBJECT {t.name} pos={t.position:F2} bounds={b} parent={(t.parent ? t.parent.name : "ROOT")}");
            }
        }
        foreach (var t in Terrain.activeTerrains) report.AppendLine($"TERRAIN {t.name} {t.transform.position} size={t.terrainData.size}");
        foreach (var c in Object.FindObjectsByType<Camera>()) report.AppendLine($"CAMERA {c.name} display={c.targetDisplay} enabled={c.enabled}");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D model/RUKO F11GIM.fbx");
        if (model)
        {
            var copy = Object.Instantiate(model);
            foreach (var r in copy.GetComponentsInChildren<Renderer>()) report.AppendLine($"MODEL {r.name} bounds={r.bounds} materials={string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "NULL"))}");
            Object.DestroyImmediate(copy);
        }
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Vehicles/DroneFlying.mp3");
        report.AppendLine("AUDIO " + (clip ? clip.length.ToString() : "MISSING"));
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
        Capture(new Vector3(6, 22, 4), new Vector3(-13.6f, 8, 23.8f), "tower-before");
        Capture(new Vector3(0, 230, 0), Vector3.zero, "map-before");
    }

    static void Capture(Vector3 position, Vector3 target, string name)
    {
        var go = new GameObject("Drone validation camera");
        var camera = go.AddComponent<Camera>();
        go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        go.transform.position = position;
        go.transform.LookAt(target);
        camera.farClipPlane = 600;
        var rt = new RenderTexture(1000, 800, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1000, 800, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1000, 800), 0, 0);
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
}

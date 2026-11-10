using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Non-destructive setup commands. Never changes the patrol prefab/controller.</summary>
public static class EliteSoldierSetup
{
    public const string Work = "Temp/EliteSoldier";
    public const string PrefabPath = "Assets/Prefabs/Agents/EliteSoldier.prefab";
    public const string TestScene = "Assets/Scenes/EliteSoldierTest.unity";
    const string ControllerPath = "Assets/Animations/EliteSoldier.controller";

    [InitializeOnLoadMethod]
    static void Resume() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    static void Poll()
    {
        string path = Work + "/request.txt";
        if (!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "build") BuildTestScene();
            else if (command == "validate") EliteSoldierValidation.Run();
            else if (command == "integrate") Integrate();
            else if (command == "repair-post") RepairGuardPost();
            else if (command == "check-post") EliteGuardPostValidation.Run();
            else if (command == "computer-room") InstallComputerRoomDefense();
            else if (command == "inspect-cover") InspectComputerRoomCover();
            else throw new InvalidOperationException("Unknown elite command: " + command);
        }
        catch (Exception e) { Directory.CreateDirectory(Work); File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Operation 1989/Elite/1 Inspect Control Room")]
    public static void Inspect()
    {
        Directory.CreateDirectory(Work);
        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>();
        var terminal = Object.FindAnyObjectByType<IntelTerminal>();
        File.WriteAllText(Work + "/inspection.txt", $"Scene: {SceneManager.GetActiveScene().path}\nDirty: {SceneManager.GetActiveScene().isDirty}\n" +
            $"Control room: {(ambience && ambience.controlRoom ? ambience.controlRoom.bounds.ToString() : "missing")}\nTerminal: {(terminal ? terminal.transform.position.ToString() : "missing")}\n" +
            string.Join("\n", Object.FindObjectsByType<MeshRenderer>()
                .Where(r => ambience && ambience.controlRoom && ambience.controlRoom.bounds.Intersects(r.bounds))
                .Select(r => r.name + " " + r.bounds)));
        Debug.Log("Elite inspection written to " + Work);
    }

    public static GameObject BuildPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing) return existing;
        if (!AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) &&
            !AssetDatabase.CopyAsset("Assets/PlayerSoldier/PlayerSoldier.controller", ControllerPath))
            throw new InvalidOperationException("The existing masked rifle animation controller is missing.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agents/PatrolSoldier.prefab");
        if (!source) throw new InvalidOperationException("Patrol soldier model missing.");
        var root = new GameObject("Elite Soldier");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "Soldier Visual"; visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one * .75f;
            foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            var animator = visual.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var idle = AssetDatabase.LoadAllAssetsAtPath("Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            idle.SampleAnimation(visual, 0);
            var body = root.AddComponent<CharacterController>(); body.height = 1.9f; body.radius = .32f;
            body.center = Vector3.up * .95f; body.stepOffset = .25f; body.skinWidth = .02f;
            var health = root.AddComponent<CombatHealth>(); health.maximumHealth = 160;
            var rifle = root.AddComponent<RifleWeapon>(); rifle.damage = 10; rifle.range = 30;
            rifle.shotSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Weapons/MachineGun.mp3");
            var hand = visual.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.Contains("R Hand"));
            if (!hand) throw new InvalidOperationException("Patrol rig rifle hand missing.");
            var muzzle = new GameObject("Elite Rifle Muzzle").transform; muzzle.SetParent(hand, false);
            muzzle.position = root.transform.position + Vector3.up * 1.35f + Vector3.forward * .8f + Vector3.right * .18f;
            rifle.muzzle = muzzle;
            root.AddComponent<ElitePerception>();
            root.AddComponent<EliteMovement>().animator = animator;
            root.AddComponent<EliteGuardController>();
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static Transform Marker(string name, Transform parent, Vector3 position)
    { var t = new GameObject(name).transform; t.SetParent(parent); t.position = position; return t; }

    static GameObject Block(string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.position = position; go.transform.localScale = scale; return go;
    }

    public static EliteNavigationGraph Grid(Transform parent, Bounds area, float groundY, float spacing = 1.4f)
    {
        var graph = new GameObject("Elite Ground Graph").AddComponent<EliteNavigationGraph>(); graph.transform.SetParent(parent);
        var nodes = new System.Collections.Generic.List<Transform>();
        Physics.SyncTransforms();
        for (float x = area.min.x + .5f; x < area.max.x - .5f; x += spacing)
            for (float z = area.min.z + .5f; z < area.max.z - .5f; z += spacing)
            {
                Vector3 p = new Vector3(x, groundY, z);
                if (graph.ClearSegment(p, p, null)) nodes.Add(Marker("Node " + nodes.Count, graph.transform, p));
            }
        graph.nodes = nodes.ToArray();
        return graph;
    }

    [MenuItem("Operation 1989/Elite/2 Build Isolated Test Scene")]
    public static void BuildTestScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        if (File.Exists(TestScene)) throw new InvalidOperationException("Test scene already exists; open it to validate or edit it.");
        var prefab = BuildPrefab();
        var sourcePlayer = Object.FindAnyObjectByType<PlayerSoldierPresentation>();
        if (!sourcePlayer) throw new InvalidOperationException("Open army base so its existing player can be copied without changing controls.");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            Block("Test floor", new Vector3(0, -.25f, 0), new Vector3(30, .5f, 30));
            Block("Chamber rear wall", new Vector3(0, 1.5f, 9), new Vector3(20, 3, .5f));
            Block("Chamber entrance left", new Vector3(-5, 1.5f, 5), new Vector3(7, 3, .5f));
            Block("Chamber entrance right", new Vector3(5, 1.5f, 5), new Vector3(7, 3, .5f));
            Block("Route divider", new Vector3(0, 1.5f, -1), new Vector3(2, 3, 4));
            Block("Cover left", new Vector3(-4, 1.2f, 2), new Vector3(2, 2.4f, 1));
            Block("Cover right", new Vector3(4, 1.2f, 2), new Vector3(2, 2.4f, 1));
            var root = new GameObject("Elite Test Setup").transform;
            var cover = new[] { Marker("Left cover", root, new Vector3(-4, .04f, 3)).gameObject.AddComponent<EliteCoverPoint>(),
                Marker("Right cover", root, new Vector3(4, .04f, 3)).gameObject.AddComponent<EliteCoverPoint>() };
            var graph = Grid(root, new Bounds(Vector3.zero, new Vector3(26, 4, 26)), .04f);
            var post = Marker("Chamber Guard Post", root, new Vector3(0, .04f, 6.5f)); post.rotation = Quaternion.Euler(0, 180, 0);
            var player = Object.Instantiate(sourcePlayer.gameObject); player.name = "Elite Test Player";
            SceneManager.MoveGameObjectToScene(player, scene);
            player.transform.SetPositionAndRotation(new Vector3(0, 1, -10), Quaternion.identity);
            player.GetComponent<FirstPersonController>().enabled = true;
            foreach (var c in player.GetComponentsInChildren<Camera>(true)) { c.gameObject.SetActive(true); c.enabled = true; }
            var listener = player.GetComponentInChildren<AudioListener>(true);
            if (listener) listener.enabled = true;
            var guard = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<EliteGuardController>();
            guard.transform.SetPositionAndRotation(post.position, post.rotation); guard.guardPost = post; guard.coverPoints = cover;
            guard.debugOverlay = true; guard.defensiveRadius = 12;
            guard.GetComponent<EliteMovement>().graph = graph; guard.GetComponent<ElitePerception>().target = player.transform;
            var light = new GameObject("Test sunlight").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0); light.intensity = 1.5f;
            EditorSceneManager.SaveScene(scene, TestScene);
            Directory.CreateDirectory(Work); File.WriteAllText(Work + "/built.txt", "Prefab and isolated scene built. Main scene unchanged.\n");
        }
        finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Operation 1989/Elite/4 Integrate Into Control Room")]
    public static void Integrate()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        if (!File.Exists(Work + "/passed.txt")) throw new InvalidOperationException("Run elite validation successfully before integration.");
        if (Object.FindAnyObjectByType<EliteGuardController>()) throw new InvalidOperationException("An elite guard is already present.");
        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>(); var terminal = Object.FindAnyObjectByType<IntelTerminal>();
        var player = Object.FindAnyObjectByType<PlayerSoldierPresentation>();
        var floor = GameObject.Find("w");
        if (!ambience || !ambience.controlRoom || !terminal || !player || !floor) throw new InvalidOperationException("The authored control room, floor, terminal, and player must be present.");
        Bounds room = ambience.controlRoom.bounds;
        if (!room.Contains(terminal.transform.position)) throw new InvalidOperationException("Archive terminal is outside the identified control room; inspect before integrating.");
        var scene = SceneManager.GetActiveScene(); Directory.CreateDirectory(Work);
        EditorSceneManager.SaveScene(scene, Work + "/before-integration.unity", true);
        var root = new GameObject("Control Room Elite Defense").transform; Undo.RegisterCreatedObjectUndo(root.gameObject, "Install elite defense");
        var props = Object.FindObjectsByType<MeshFilter>()
            .Where(m => m.sharedMesh && m.GetComponent<Renderer>() && m.GetComponent<Renderer>().bounds.size.y > .8f &&
                Mathf.Max(m.GetComponent<Renderer>().bounds.size.x, m.GetComponent<Renderer>().bounds.size.z) > .3f && room.Intersects(m.GetComponent<Renderer>().bounds) &&
                new[] { "server", "desk", "cabinet", "wall", "pillar", "column" }.Any(s => m.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
        foreach (var mesh in props)
            if (!mesh.GetComponent<Collider>()) Undo.AddComponent<MeshCollider>(mesh.gameObject).sharedMesh = mesh.sharedMesh;
        float y = floor.GetComponent<Renderer>().bounds.max.y + .04f;
        var graph = Grid(root, room, y);
        if (graph.nodes.Length == 0) { Object.DestroyImmediate(root.gameObject); throw new InvalidOperationException("No clear ground nodes inside the control room."); }
        ChooseEntrancePost(graph, room, null, out Vector3 position, out Quaternion rotation, out _);
        var post = Marker("Control Room Guard Post", root, position); post.rotation = rotation;
        var covers = new System.Collections.Generic.List<EliteCoverPoint>();
        foreach (var mesh in props.Where(m => new[] { "server", "desk", "cabinet", "pillar", "column" }.Any(s => m.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            Bounds b = mesh.GetComponent<Renderer>().bounds;
            // Use existing physical objects. Candidate markers are subsequently tested against the actual known threat.
            foreach (Vector3 direction in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                Vector3 p = b.center + Vector3.Scale(direction, b.extents + Vector3.one * .6f); p.y = y;
                if (covers.Count >= 24 || covers.Any(c => Vector3.Distance(c.transform.position, p) < .8f)) continue;
                if (!room.Contains(p + Vector3.up) || !graph.ClearSegment(p, p, null)) continue;
                if (!graph.nodes.Any(n => Vector3.Distance(n.position, p) <= graph.connectionDistance && graph.ClearSegment(n.position, p, null))) continue;
                covers.Add(Marker("Cover behind " + mesh.name, root, p).gameObject.AddComponent<EliteCoverPoint>());
            }
        }
        var guard = ((GameObject)PrefabUtility.InstantiatePrefab(BuildPrefab(), scene)).GetComponent<EliteGuardController>();
        guard.transform.SetParent(root); guard.transform.SetPositionAndRotation(post.position, post.rotation);
        guard.guardPost = post; guard.coverPoints = covers.ToArray(); guard.defensiveRadius = Mathf.Min(18, Mathf.Max(room.extents.x, room.extents.z));
        guard.GetComponent<EliteMovement>().graph = graph; guard.GetComponent<ElitePerception>().target = player.transform;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Work + "/integration.txt", $"Control room {room}\nGuard post {post.position}\nNodes {graph.nodes.Length}\nCover candidates {covers.Count}\nExisting room props reused. Playtest door and cover clearances in the main scene.\n");
        InstallComputerRoomDefense();
        File.AppendAllText(Work + "/integration.txt", "\nFinal computer-room placement:\n" + File.ReadAllText(Work + "/computer-room.txt"));
    }

    // The floor bounds already identify the main building's central entrance corridor
    // in MainBuildingEntrancePassage. Watch its interior approach, not the wall beside the terminal.
    public static void ChooseEntrancePost(EliteNavigationGraph graph, Bounds room, Transform owner,
        out Vector3 position, out Quaternion rotation, out Vector3 approach)
    {
        if (graph && graph.navigationArea)
        {
            ChooseComputerRoomPost(graph, owner, out position, out rotation, out approach); return;
        }
        if (!graph || graph.nodes == null || graph.nodes.Length == 0) throw new InvalidOperationException("Guard navigation graph is missing.");
        Physics.SyncTransforms();
        var candidates = graph.nodes.Where(n => n && graph.ClearSegment(n.position, n.position, owner)).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("No clear standing positions in the control room.");
        Vector3 entrance = new Vector3(room.center.x, candidates[0].position.y, room.min.z + 2);
        approach = candidates.OrderBy(n => Vector3.Distance(n.position, entrance)).First().position;
        Vector3 aim = approach + Vector3.up * 1.45f;
        Vector3 preferred = approach + Vector3.forward * 5;
        var path = new System.Collections.Generic.List<Vector3>();
        foreach (var node in candidates.OrderBy(n => Vector3.Distance(n.position, preferred)))
        {
            Vector3 delta = approach - node.position; delta.y = 0;
            if (delta.magnitude < 3 || delta.magnitude > 10) continue;
            Quaternion facing = Quaternion.LookRotation(delta);
            Vector3 muzzle = node.position + Vector3.up * 1.35f + facing * new Vector3(.18f, 0, .8f);
            if (!ElitePerception.Clear(node.position + Vector3.up * 1.55f, aim, owner, null) ||
                !ElitePerception.Clear(muzzle, aim, owner, null)) continue;
            if (!graph.FindPath(node.position, approach, owner, null, 0, node.position, 16, path, out _, out _)) continue;
            position = node.position; rotation = facing; return;
        }
        throw new InvalidOperationException("No reachable guard post has a clear view of the entrance approach. Inspect the room colliders.");
    }

    [MenuItem("Operation 1989/Elite/5 Repair Entrance Guard Post")]
    public static void RepairGuardPost()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        var guard = Object.FindAnyObjectByType<EliteGuardController>();
        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>();
        if (!guard || !guard.guardPost || !ambience || !ambience.controlRoom) throw new InvalidOperationException("The integrated guard and control room are required.");
        ChooseEntrancePost(guard.GetComponent<EliteMovement>().graph, ambience.controlRoom.bounds, guard.transform,
            out Vector3 position, out Quaternion rotation, out Vector3 approach);
        Directory.CreateDirectory(Work);
        EditorSceneManager.SaveScene(guard.gameObject.scene, Work + "/before-post-repair.unity", true);
        Undo.RecordObjects(new Object[] { guard.transform, guard.guardPost }, "Face elite toward the entrance");
        guard.transform.SetPositionAndRotation(position, rotation);
        guard.guardPost.SetPositionAndRotation(position, rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(guard.transform);
        EditorSceneManager.MarkSceneDirty(guard.gameObject.scene); EditorSceneManager.SaveScene(guard.gameObject.scene);
        File.WriteAllText(Work + "/post-repair.txt", $"Post {position}\nFacing {rotation.eulerAngles}\nEntrance approach {approach}\nPASS Standing clearance, eye and muzzle sightlines, and A* approach reachability.\n");
    }

    public static Bounds ComputerRoom(out Renderer door, out Vector3 inward)
    {
        var terminal = Object.FindAnyObjectByType<IntelTerminal>();
        if (!terminal) throw new InvalidOperationException("Archive computer is missing.");
        var shell = Object.FindObjectsByType<Renderer>().Where(r => r.name == "entry_3" && r.bounds.Contains(terminal.transform.position))
            .OrderBy(r => r.bounds.size.sqrMagnitude).FirstOrDefault();
        if (!shell) throw new InvalidOperationException("Cannot identify the enclosed room around the archive computer.");
        Transform root = shell.transform;
        while (root.parent && !root.name.StartsWith("Sci-Fi_door_3_with_animation", StringComparison.OrdinalIgnoreCase)) root = root.parent;
        door = root.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "entry_3_door");
        if (!door) throw new InvalidOperationException("Computer room doorway is missing.");
        bool planeX = door.bounds.size.x < door.bounds.size.z;
        inward = planeX ? Vector3.right * Mathf.Sign(terminal.transform.position.x - door.bounds.center.x) :
            Vector3.forward * Mathf.Sign(terminal.transform.position.z - door.bounds.center.z);
        Bounds area = shell.bounds;
        Vector3 min = area.min + new Vector3(.35f, 0, .35f), max = area.max - new Vector3(.35f, 0, .35f);
        if (planeX) { if (inward.x < 0) max.x = door.bounds.center.x - .45f; else min.x = door.bounds.center.x + .45f; }
        else { if (inward.z < 0) max.z = door.bounds.center.z - .45f; else min.z = door.bounds.center.z + .45f; }
        area.SetMinMax(min, max);
        if (!area.Contains(terminal.transform.position)) throw new InvalidOperationException("Computer is outside the derived server-room interior.");
        return area;
    }

    public static void ChooseComputerRoomPost(EliteNavigationGraph graph, Transform owner,
        out Vector3 position, out Quaternion rotation, out Vector3 approach)
    {
        ComputerRoom(out Renderer door, out Vector3 inward);
        var terminal = Object.FindAnyObjectByType<IntelTerminal>();
        var nodes = graph.nodes.Where(n => n && graph.ClearSegment(n.position, n.position, owner)).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("No standing clearance in the computer room.");
        Vector3 entry = door.bounds.center + inward * 1.5f; entry.y = nodes[0].position.y;
        approach = nodes.OrderBy(n => Vector3.Distance(n.position, entry)).First().position;
        Vector3 preferred = terminal.transform.position - inward * 2.5f; preferred.y = approach.y;
        var path = new System.Collections.Generic.List<Vector3>();
        foreach (var node in nodes.OrderBy(n => Vector3.Distance(n.position, preferred)))
        {
            Vector3 delta = approach - node.position; delta.y = 0;
            if (delta.magnitude < 2 || delta.magnitude > 7) continue;
            Quaternion facing = Quaternion.LookRotation(delta);
            Vector3 muzzle = node.position + Vector3.up * 1.35f + facing * new Vector3(.18f, 0, .8f);
            if (!ElitePerception.Clear(node.position + Vector3.up * 1.55f, approach + Vector3.up * 1.45f, owner, null) ||
                !ElitePerception.Clear(muzzle, approach + Vector3.up * 1.45f, owner, null)) continue;
            if (!graph.FindPath(node.position, approach, owner, null, 0, node.position, 16, path, out _, out _)) continue;
            position = node.position; rotation = facing; return;
        }
        throw new InvalidOperationException("No post inside the computer room can see and reach its doorway approach.");
    }

    [MenuItem("Operation 1989/Elite/7 Place Guard Inside Computer Room")]
    public static void InstallComputerRoomDefense()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        var guard = Object.FindAnyObjectByType<EliteGuardController>();
        if (!guard || !guard.guardPost) throw new InvalidOperationException("The integrated elite guard is required.");
        Bounds room = ComputerRoom(out Renderer door, out Vector3 inward);
        Directory.CreateDirectory(Work);
        EditorSceneManager.SaveScene(guard.gameObject.scene, Work + "/before-computer-room.unity", true);
        var defense = guard.guardPost.parent;
        var oldGraph = guard.GetComponent<EliteMovement>().graph;
        var oldBoundary = oldGraph ? oldGraph.navigationArea : null;
        var oldCovers = guard.coverPoints;
        // Match the static collision that WorldInteractionBootstrap installs for this
        // doorway prefab at runtime. Authoring paths without its walls gives false routes.
        Transform shellRoot = door.transform;
        while (shellRoot.parent && !shellRoot.name.StartsWith("Sci-Fi_door_3_with_animation", StringComparison.OrdinalIgnoreCase)) shellRoot = shellRoot.parent;
        foreach (var mesh in shellRoot.GetComponentsInChildren<MeshFilter>())
        {
            if (!mesh.sharedMesh || mesh.name == "entry_3_door") continue;
            var solids = mesh.GetComponents<Collider>().Where(c => !c.isTrigger).ToArray();
            foreach (var solid in solids) { Undo.RecordObject(solid, "Match runtime room collision"); solid.enabled = true; }
            if (solids.Length == 0) Undo.AddComponent<MeshCollider>(mesh.gameObject).sharedMesh = mesh.sharedMesh;
        }
        // The surrounding building shell is another runtime-generated obstacle.
        // Give authoring the same mesh surfaces, including the outer base walls.
        var building = GameObject.Find("building_1");
        if (building)
            foreach (var mesh in building.GetComponentsInChildren<MeshFilter>())
                if (mesh.sharedMesh && mesh.GetComponent<Renderer>() && room.Intersects(mesh.GetComponent<Renderer>().bounds) &&
                    mesh.name != "door_middle" && !mesh.GetComponent<Collider>())
                    Undo.AddComponent<MeshCollider>(mesh.gameObject).sharedMesh = mesh.sharedMesh;
        var roomRacks = SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == "server" || g.name.StartsWith("server (")).ToArray();
        foreach (var rack in roomRacks)
        {
            var renderers = rack.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) continue;
            Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (!room.Intersects(b)) continue;
            var proxy = rack.transform.Find("Elite Server Collision");
            if (!proxy)
            {
                proxy = new GameObject("Elite Server Collision").transform; proxy.SetParent(rack.transform, true);
                Undo.RegisterCreatedObjectUndo(proxy.gameObject, "Match server rack collision"); proxy.gameObject.AddComponent<BoxCollider>();
            }
            proxy.SetPositionAndRotation(b.center, Quaternion.identity);
            proxy.GetComponent<BoxCollider>().size = b.size + Vector3.one * .02f;
            var obstacle = rack.GetComponent<EliteServerObstacle>();
            if (!obstacle) obstacle = Undo.AddComponent<EliteServerObstacle>(rack);
            obstacle.volume = proxy.GetComponent<BoxCollider>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(obstacle);
        }
        Physics.SyncTransforms();
        float groundY = guard.guardPost.position.y;
        var boundary = new GameObject("Computer Room Defense Area").AddComponent<BoxCollider>();
        boundary.transform.SetParent(defense); boundary.transform.position = room.center;
        boundary.size = room.size; boundary.isTrigger = true;
        var graph = Grid(defense, room, groundY, .6f); graph.connectionDistance = 1.75f; graph.navigationArea = boundary;
        var roomNodes = graph.nodes.ToList();
        foreach (var rack in roomRacks)
        {
            var proxy = rack.transform.Find("Elite Server Collision"); if (!proxy) continue;
            Bounds b = proxy.GetComponent<BoxCollider>().bounds;
            foreach (float x in new[] { -1f, 0, 1f }) foreach (float z in new[] { -1f, 0, 1f })
            {
                if (x == 0 && z == 0) continue;
                Vector3 p = b.center + new Vector3(x * (b.extents.x + .47f), 0, z * (b.extents.z + .47f)); p.y = groundY;
                if (!graph.Contains(p) || !graph.ClearSegment(p, p, guard.transform) || roomNodes.Any(n => Vector3.Distance(n.position, p) < .25f)) continue;
                roomNodes.Add(Marker("Server corner " + roomNodes.Count, graph.transform, p));
            }
        }
        graph.nodes = roomNodes.ToArray();
        var covers = new System.Collections.Generic.List<EliteCoverPoint>();
        bool saved = false;
        try
        {
            ChooseComputerRoomPost(graph, guard.transform, out Vector3 position, out Quaternion rotation, out Vector3 approach);
            var route = new System.Collections.Generic.List<Vector3>();
            var racks = roomRacks.AsEnumerable();
            foreach (var rack in racks.OrderBy(g => Vector3.Distance(g.transform.position, position)))
            {
                var renderers = rack.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length == 0) continue;
                Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                if (!room.Intersects(b) || b.size.y < 1.35f) continue;
                foreach (var direction in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                {
                    Vector3 p = b.center + Vector3.Scale(direction, b.extents + Vector3.one * .48f); p.y = groundY;
                    if (covers.Count >= 10 || !graph.Contains(p) || covers.Any(c => Vector3.Distance(c.transform.position, p) < .7f) ||
                        !graph.ClearSegment(p, p, guard.transform) || ElitePerception.Clear(approach + Vector3.up * 1.45f, p + Vector3.up * 1.3f, guard.transform, null)) continue;
                    if (!graph.FindPath(position, p, guard.transform, null, 0, position, 16, route, out _, out _)) continue;
                    covers.Add(Marker("Server cover - " + rack.name, defense, p).gameObject.AddComponent<EliteCoverPoint>());
                }
            }
            if (covers.Count == 0) throw new InvalidOperationException("No protective reachable server cover found inside the actual room.");
            Undo.RecordObjects(new Object[] { guard, guard.transform, guard.guardPost, guard.GetComponent<EliteMovement>() }, "Place elite inside the computer room");
            guard.transform.SetPositionAndRotation(position, rotation); guard.guardPost.SetPositionAndRotation(position, rotation);
            guard.coverPoints = covers.ToArray(); guard.defensiveRadius = 14;
            guard.GetComponent<EliteMovement>().graph = graph;
            foreach (var component in new Object[] { guard, guard.transform, guard.GetComponent<EliteMovement>() }) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (oldGraph) Undo.DestroyObjectImmediate(oldGraph.gameObject);
            if (oldBoundary) Undo.DestroyObjectImmediate(oldBoundary.gameObject);
            foreach (var cover in oldCovers) if (cover) Undo.DestroyObjectImmediate(cover.gameObject);
            EditorSceneManager.MarkSceneDirty(guard.gameObject.scene); EditorSceneManager.SaveScene(guard.gameObject.scene);
            saved = true;
            File.WriteAllText(Work + "/computer-room.txt", $"Room {room}\nDoor {door.bounds.center}\nPost {position}\nFacing {rotation.eulerAngles}\nApproach {approach}\nNodes {graph.nodes.Length}\nReachable protective server covers {covers.Count}\nPASS Post, graph and covers inside the enclosed computer room.\n");
            PlayerSoldierSetup.Capture(approach + Vector3.up * 2, position + Vector3.up * 1.1f, "elite-computer-room");
        }
        catch
        {
            if (!saved)
            {
                foreach (var cover in covers) if (cover) Object.DestroyImmediate(cover.gameObject);
                Object.DestroyImmediate(graph.gameObject); Object.DestroyImmediate(boundary.gameObject);
            }
            throw;
        }
    }

    public static void InspectComputerRoomCover()
    {
        var guard = Object.FindAnyObjectByType<EliteGuardController>();
        var graph = guard.GetComponent<EliteMovement>().graph;
        var senses = guard.GetComponent<ElitePerception>();
        var lines = new System.Collections.Generic.List<string> { $"Playing {Application.isPlaying} | Actor {guard.transform.position} | Start clear {graph.ClearSegment(guard.transform.position, guard.transform.position, guard.transform)}",
            $"Free nodes {graph.nodes.Count(n => n && graph.ClearSegment(n.position, n.position, guard.transform))}/{graph.nodes.Length}" };
        var path = new System.Collections.Generic.List<Vector3>();
        foreach (var cover in guard.coverPoints)
        {
            Vector3 p = cover.transform.position;
            bool reachable = graph.FindPath(guard.transform.position, p, guard.transform, null, 0, guard.guardPost.position, guard.defensiveRadius, path, out _, out _);
            lines.Add($"{cover.name} {p} | Clear {graph.ClearSegment(p, p, guard.transform)} | Protected {cover.ProtectedFrom(senses.HasMemory ? senses.LastAim : guard.transform.position + Vector3.up * 1.45f, guard.transform, ~0)} | Reachable {reachable}");
            foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (graph.radius + .08f), p + Vector3.up * (graph.height - graph.radius), graph.radius, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(guard.transform)) lines.Add("  Blocker " + c.name + " " + c.bounds);
        }
        Directory.CreateDirectory(Work); File.WriteAllLines(Work + "/cover-inspection.txt", lines);
    }
}

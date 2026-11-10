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

    public static EliteNavigationGraph Grid(Transform parent, Bounds area, float groundY)
    {
        var graph = new GameObject("Elite Ground Graph").AddComponent<EliteNavigationGraph>(); graph.transform.SetParent(parent);
        const float spacing = 1.4f;
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
        var desired = terminal.transform.position + Vector3.back * 4; desired.y = y;
        var chosen = graph.nodes.OrderBy(n => Vector3.Distance(n.position, desired)).First();
        var post = Marker("Control Room Guard Post", root, chosen.position); post.rotation = Quaternion.Euler(0, 180, 0);
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
    }
}

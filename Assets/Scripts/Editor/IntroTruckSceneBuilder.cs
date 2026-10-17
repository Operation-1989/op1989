using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Object = UnityEngine.Object;

/// <summary>Explicit editor commands; never modifies scenes on import or startup.</summary>
public static class IntroTruckSceneBuilder
{
    const string Folder = "Assets/IntroTruck";
    [Serializable] public class Geometry { public Part[] parts; }
    [Serializable] public class Part { public string name; public Vector3[] vertices; public Submesh[] submeshes; }
    [Serializable] public class Submesh { public string material; public int[] triangles; }
    [Serializable] public class SceneSettings { public Pick[] parts; public Vector3[] route; public Vector3 playerPosition = new Vector3(14, 1.25f, -99.2f); }
    [Serializable] public class Pick { public string name; public int[] left; public int[] right; }

    [MenuItem("Operation 1989/Intro/Export Gate Geometry")]
    public static void ExportGeometry()
    {
        Directory.CreateDirectory("Temp/IntroTruck");
        var parts = new List<Part>();
        foreach (var mf in GameObject.Find("gateFbx").GetComponentsInChildren<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            var mats = mf.GetComponent<Renderer>().sharedMaterials;
            parts.Add(new Part { name = mf.name, vertices = mesh.vertices.Select(v => mf.transform.TransformPoint(v)).ToArray(),
                submeshes = Enumerable.Range(0, mesh.subMeshCount).Select(s => new Submesh {
                    material = s < mats.Length && mats[s] ? AssetDatabase.GetAssetPath(mats[s]) : "", triangles = mesh.GetTriangles(s) }).ToArray() });
        }
        File.WriteAllText("Temp/IntroTruck/gate-geometry.json", JsonUtility.ToJson(new Geometry { parts = parts.ToArray() }));
    }

    [MenuItem("Operation 1989/Intro/Build Scene")]
    public static void BuildScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit Mode first.");
        if (GameObject.Find("Intro Truck Cutscene")) throw new InvalidOperationException("Intro already exists; edit its Inspector settings instead of building twice.");
        var settings = JsonUtility.FromJson<SceneSettings>(File.ReadAllText("Temp/IntroTruck/selection.json"));
        if (settings.parts == null || settings.parts.Length == 0) throw new InvalidOperationException("No gate triangle selection supplied.");
        Directory.CreateDirectory("Temp/IntroTruck");
        File.Copy(scene.path, "Temp/IntroTruck/army-base-before-intro.unity", true);
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "IntroTruck");
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Install intro truck cutscene");
        var root = new GameObject("Intro Truck Cutscene");
        Undo.RegisterCreatedObjectUndo(root, "Intro root");
        var sequence = root.AddComponent<IntroTruckSequence>();
        var gate = new GameObject("White Gate Controller").AddComponent<IntroEntranceGate>();
        gate.transform.SetParent(root.transform);
        sequence.gate = gate;
        var gateRoot = GameObject.Find("gateFbx");
        Bounds doors = new Bounds(); bool haveDoor = false;
        foreach (var pick in settings.parts)
        {
            var mf = gateRoot.GetComponentsInChildren<MeshFilter>().First(m => m.name == pick.name);
            var verts = mf.sharedMesh.vertices;
            var indices = new HashSet<int>((pick.left ?? new int[0]).Concat(pick.right ?? new int[0]));
            int ordinal = 0;
            for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
            {
                var tris = mf.sharedMesh.GetTriangles(sub);
                for (int i = 0; i < tris.Length; i += 3, ordinal++) if (indices.Contains(ordinal))
                    for (int v = 0; v < 3; v++)
                    {
                        var point = mf.transform.TransformPoint(verts[tris[i + v]]);
                        if (!haveDoor) { doors = new Bounds(point, Vector3.zero); haveDoor = true; } else doors.Encapsulate(point);
                    }
            }
        }
        if (!haveDoor) { Object.DestroyImmediate(root); throw new InvalidOperationException("Gate selection is empty."); }
        // Measured clear aperture of this imported arch at truck height: x=0.5..7.3.
        // The source grille extends behind the masonry; clip it at the real jambs.
        doors.SetMinMax(new Vector3(.5f, doors.min.y, doors.min.z), new Vector3(7.3f, doors.max.y, doors.max.z));
        gate.leftHinge = NewPoint("Left Gate Hinge", gate.transform, new Vector3(doors.min.x, doors.min.y, doors.center.z));
        gate.rightHinge = NewPoint("Right Gate Hinge", gate.transform, new Vector3(doors.max.x, doors.min.y, doors.center.z));
        gate.safetyVolume = new Bounds(new Vector3(doors.center.x, 3, doors.center.z), new Vector3(doors.size.x + 3, 7, 11));
        foreach (var pick in settings.parts)
        {
            var mf = gateRoot.GetComponentsInChildren<MeshFilter>().First(m => m.name == pick.name);
            var original = mf.sharedMesh;
            var left = new HashSet<int>(pick.left ?? new int[0]);
            var right = new HashSet<int>(pick.right ?? new int[0]);
            var frame = Slice(original, mf.transform, null, ordinal => !left.Contains(ordinal) && !right.Contains(ordinal));
            AssetDatabase.CreateAsset(frame, Folder + "/Frame-" + Safe(pick.name) + ".asset");
            Undo.RecordObject(mf, "Carve opening on scene instance");
            mf.sharedMesh = frame;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
            AddLeaf(mf, original, left, gate.leftHinge, "Left", doors.min.x, doors.center.x);
            AddLeaf(mf, original, right, gate.rightHinge, "Right", doors.center.x, doors.max.x);
            if (!mf.GetComponent<Collider>()) Undo.AddComponent<MeshCollider>(mf.gameObject).sharedMesh = frame;
        }
        AddHingeCollider(gate.leftHinge);
        AddHingeCollider(gate.rightHinge);

        // Copy the working player and its child camera; retain the source scene and prefab unchanged.
        var source = EditorSceneManager.OpenScene("Assets/Scenes/AgentTesting.unity", OpenSceneMode.Additive);
        var sourcePlayer = source.GetRootGameObjects().First(go => go.GetComponent<FirstPersonController>());
        var player = Object.Instantiate(sourcePlayer);
        SceneManager.MoveGameObjectToScene(player, scene);
        EditorSceneManager.CloseScene(source, true);
        SceneManager.SetActiveScene(scene);
        Undo.RegisterCreatedObjectUndo(player, "Reuse existing player setup");
        player.name = "Intro Player";
        player.tag = "Player";
        player.transform.position = settings.playerPosition;
        player.transform.rotation = Quaternion.Euler(0, -35, 0);
        sequence.player = player.transform;
        gate.player = player.transform;
        sequence.playerCamera = player.GetComponentInChildren<Camera>();
        sequence.playerCamera.tag = "MainCamera";
        sequence.playerInput = player.GetComponentsInChildren<MonoBehaviour>().Where(m => m is FirstPersonController || m is TestPlayerMovement).Cast<Behaviour>().ToArray();
        var firstPerson = player.GetComponent<FirstPersonController>();
        firstPerson.playerCamera = sequence.playerCamera.transform;
        var character = player.GetComponent<CharacterController>();
        character.stepOffset = 0.5f;
        var aerial = scene.GetRootGameObjects().First(go => go.GetComponent<Camera>());
        Undo.RecordObject(aerial, "Reuse scene camera for intro");
        aerial.tag = "Untagged";
        sequence.introCamera = aerial.GetComponent<Camera>();
        Undo.RecordObject(sequence.introCamera, "Configure aerial camera");
        sequence.introCamera.enabled = false;
        Undo.RecordObject(aerial.GetComponent<AudioListener>(), "Configure aerial listener");
        aerial.GetComponent<AudioListener>().enabled = false;
        if (!sequence.playerCamera.GetComponent<AudioListener>()) sequence.playerCamera.gameObject.AddComponent<AudioListener>();

        var truck = new GameObject("Intro Entrance Truck");
        truck.transform.SetParent(root.transform);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/M923_Transport_Truck_3D_TRADER.FBX"), truck.transform);
        model.transform.localRotation = Quaternion.Euler(0, -90, 0);
        model.transform.localPosition = Vector3.zero;
        Bounds truckBounds = model.GetComponentsInChildren<Renderer>()[0].bounds;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>()) truckBounds.Encapsulate(renderer.bounds);
        model.transform.localPosition -= Vector3.up * truckBounds.min.y;
        truckBounds.center -= Vector3.up * truckBounds.min.y;
        var truckCollider = truck.AddComponent<BoxCollider>();
        truckCollider.center = truckBounds.center;
        truckCollider.size = truckBounds.size;
        truck.AddComponent<Rigidbody>().isKinematic = true;
        sequence.truck = truck.transform;
        gate.truck = truck.transform;
        sequence.collisionCenter = truckBounds.center + Vector3.up * 0.12f;
        sequence.collisionHalfExtents = truckBounds.extents - new Vector3(0.05f, 0.15f, 0.04f);
        sequence.speed = 3;
        sequence.acceleration = 1.5f;
        sequence.turnSpeed = 45;
        sequence.cutsceneDuration = 3.5f;
        sequence.entryWindow = 20;
        sequence.entryPlanePoint = new Vector3(doors.center.x, 0, doors.center.z);
        sequence.cameraPosition = new Vector3(20, 18, -113);
        sequence.cameraDrift = new Vector3(-3, 0, 3);
        sequence.cameraLookOffset = new Vector3(0, 1.2f, 3);
        var routeRoot = new GameObject("Truck Route").transform;
        routeRoot.SetParent(root.transform);
        var points = settings.route ?? new[] { new Vector3(3.78f,.2f,-99.2f), new Vector3(3.78f,.2f,-96.3f), new Vector3(3.78f,.2f,-84), new Vector3(4.5f,.2f,-81.5f), new Vector3(6.3f,.2f,-79.5f), new Vector3(9,.2f,-78), new Vector3(14,.2f,-77.8f), new Vector3(18,.2f,-77.8f), new Vector3(23,.2f,-77.8f) };
        sequence.route = points.Select((point, index) => NewPoint(index == points.Length - 1 ? "Parking" : "Route " + index, routeRoot, point)).ToArray();
        truck.transform.SetPositionAndRotation(points[0], Quaternion.LookRotation(points[1] - points[0]));
        var surfaces = new List<Collider>();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>())
        {
            if (mf.transform.IsChildOf(root.transform) || !mf.GetComponent<Renderer>()) continue;
            Bounds b = mf.GetComponent<Renderer>().bounds;
            bool road = mf.name == "Road" && b.size.y < 0.3f;
            if (road)
            {
                var c = mf.GetComponent<Collider>();
                if (!c) c = Undo.AddComponent<MeshCollider>(mf.gameObject);
                if (c is MeshCollider meshCollider) meshCollider.sharedMesh = mf.sharedMesh;
                surfaces.Add(c);
            }
            if (mf.name.StartsWith("Sandbags") && b.center.z < -90 && !mf.GetComponent<Collider>())
                Undo.AddComponent<MeshCollider>(mf.gameObject);
        }
        var ground = scene.GetRootGameObjects().First(go => go.name == "Plane" && go.GetComponent<Collider>());
        surfaces.Add(ground.GetComponent<Collider>());
        // The original ground ends at z=-100, just short of the road's outside edge.
        // A small matching patch supports the footpath around the existing outer gate leaves.
        var footing = GameObject.CreatePrimitive(PrimitiveType.Plane);
        footing.name = "Outside Entrance Ground";
        footing.transform.SetParent(root.transform);
        footing.transform.position = new Vector3(0, -0.015f, -103);
        footing.transform.localScale = new Vector3(4, 1, 1);
        footing.GetComponent<Renderer>().sharedMaterials = ground.GetComponent<Renderer>().sharedMaterials;
        surfaces.Add(footing.GetComponent<Collider>());
        // Keep the long curb elsewhere and open only the actual entrance lane.
        foreach (var mf in Object.FindObjectsByType<MeshFilter>())
        {
            var renderer = mf.GetComponent<Renderer>();
            if (!renderer || mf.transform.IsChildOf(root.transform)) continue;
            var b = renderer.bounds;
            if (b.size.x < 100 || Mathf.Abs(b.center.z + 92.9f) > .1f || b.size.y > .6f) continue;
            Undo.RecordObject(renderer, "Open entrance curb");
            renderer.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            float lo = doors.min.x - .3f, hi = doors.max.x + .3f;
            foreach (var interval in new[] { new Vector2(b.min.x, lo), new Vector2(hi, b.max.x) })
            {
                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = "Entrance Curb Segment";
                segment.transform.SetParent(root.transform);
                segment.transform.position = new Vector3((interval.x + interval.y) / 2, b.center.y, b.center.z);
                segment.transform.localScale = new Vector3(interval.y - interval.x, b.size.y, b.size.z);
                segment.GetComponent<Renderer>().sharedMaterials = renderer.sharedMaterials;
            }
        }
        // A short asphalt threshold bridges the existing road surface to the forecourt.
        var roadMaterial = Object.FindObjectsByType<MeshRenderer>().First(r => r.name == "Road").sharedMaterial;
        var ramp = new GameObject("Entrance Threshold");
        ramp.transform.SetParent(root.transform);
        float leftEdge = doors.min.x + .05f, rightEdge = doors.max.x - .05f;
        var rampMesh = new Mesh { vertices = new[] { new Vector3(leftEdge,.2f,-93.5f), new Vector3(rightEdge,.2f,-93.5f), new Vector3(leftEdge,.02f,-91.7f), new Vector3(rightEdge,.02f,-91.7f) }, triangles = new[] {0,2,1,1,2,3}, uv = new[] {Vector2.zero,Vector2.right,Vector2.up,Vector2.one} };
        rampMesh.RecalculateNormals(); rampMesh.RecalculateBounds();
        AssetDatabase.CreateAsset(rampMesh, Folder + "/EntranceThreshold.asset");
        ramp.AddComponent<MeshFilter>().sharedMesh = rampMesh;
        ramp.AddComponent<MeshRenderer>().sharedMaterial = roadMaterial;
        surfaces.Add(ramp.AddComponent<MeshCollider>());
        sequence.groundSurfaces = surfaces.Where(c => c).ToArray();
        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(gate);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        File.WriteAllText("Temp/IntroTruck/setup-result.txt", $"Built scene. Gate bounds {doors}; truck bounds {truckBounds}. Scene is dirty; inspect before saving.");
        UnityEditor.Selection.activeGameObject = root;
    }

    static string Safe(string name) => name.Replace(':', '_');
    static Transform NewPoint(string name, Transform parent, Vector3 position)
    {
        var point = new GameObject(name).transform;
        point.SetParent(parent);
        point.position = position;
        return point;
    }
    static Mesh Slice(Mesh source, Transform sourceTransform, Transform destination, Func<int, bool> keep)
    {
        var mesh = new Mesh { name = "Intro " + source.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var originalVertices = source.vertices;
        var originalUV = source.uv;
        var remap = new Dictionary<int, int>();
        var submeshes = new List<int[]>();
        int ordinal = 0;
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var original = source.GetTriangles(sub);
            var selected = new List<int>();
            for (int i = 0; i < original.Length; i += 3, ordinal++)
                if (keep(ordinal))
                    for (int v = 0; v < 3; v++)
                    {
                        int old = original[i + v];
                        if (!remap.TryGetValue(old, out int mapped))
                        {
                            mapped = vertices.Count;
                            remap.Add(old, mapped);
                            vertices.Add(destination ? destination.InverseTransformPoint(sourceTransform.TransformPoint(originalVertices[old])) : originalVertices[old]);
                            if (originalUV.Length == originalVertices.Length) uv.Add(originalUV[old]);
                        }
                        selected.Add(mapped);
                    }
            submeshes.Add(selected.ToArray());
        }
        mesh.SetVertices(vertices);
        if (uv.Count == vertices.Count) mesh.SetUVs(0, uv);
        mesh.subMeshCount = source.subMeshCount;
        for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
    struct ClipVertex { public Vector3 position; public Vector2 uv; public ClipVertex(Vector3 p, Vector2 u) { position = p; uv = u; } }
    static Mesh ClipAtJambs(Mesh source, Transform transform, float minX, float maxX)
    {
        var vertices = source.vertices;
        var uv = source.uv;
        var resultVertices = new List<Vector3>();
        var resultUV = new List<Vector2>();
        var indices = new List<int[]>();
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var result = new List<int>();
            var triangles = source.GetTriangles(sub);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var polygon = new List<ClipVertex>();
                for (int v = 0; v < 3; v++) { int index = triangles[i+v]; polygon.Add(new ClipVertex(transform.TransformPoint(vertices[index]), uv.Length == vertices.Length ? uv[index] : Vector2.zero)); }
                foreach (bool lower in new[] { true, false })
                {
                    float plane = lower ? minX : maxX;
                    var clipped = new List<ClipVertex>();
                    for (int edge = 0; edge < polygon.Count; edge++)
                    {
                        var a = polygon[edge]; var b = polygon[(edge+1) % polygon.Count];
                        bool insideA = lower ? a.position.x >= plane : a.position.x <= plane;
                        bool insideB = lower ? b.position.x >= plane : b.position.x <= plane;
                        if (insideA) clipped.Add(a);
                        if (insideA != insideB)
                        {
                            float t = (plane - a.position.x) / (b.position.x - a.position.x);
                            clipped.Add(new ClipVertex(Vector3.Lerp(a.position,b.position,t),Vector2.Lerp(a.uv,b.uv,t)));
                        }
                    }
                    polygon = clipped;
                }
                for (int v = 1; v + 1 < polygon.Count; v++)
                    foreach (int index in new[] {0,v,v+1})
                    {
                        result.Add(resultVertices.Count);
                        resultVertices.Add(transform.InverseTransformPoint(polygon[index].position));
                        resultUV.Add(polygon[index].uv);
                    }
            }
            indices.Add(result.ToArray());
        }
        var mesh = new Mesh { name = source.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(resultVertices); mesh.SetUVs(0,resultUV); mesh.subMeshCount = source.subMeshCount;
        for (int sub=0;sub<indices.Count;sub++) mesh.SetTriangles(indices[sub],sub);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
    static void AddLeaf(MeshFilter source, Mesh original, HashSet<int> selected, Transform hinge, string side, float minX, float maxX)
    {
        if (selected.Count == 0) return;
        var go = new GameObject(side + " " + source.name);
        go.transform.SetParent(hinge, false);
        var uncut = Slice(original, source.transform, go.transform, ordinal => selected.Contains(ordinal));
        var mesh = ClipAtJambs(uncut, go.transform, minX, maxX);
        Object.DestroyImmediate(uncut);
        string path = Folder + "/" + side + "-" + Safe(source.name) + ".asset";
        AssetDatabase.CreateAsset(mesh, path);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = source.GetComponent<Renderer>().sharedMaterials;
    }
    static void AddHingeCollider(Transform hinge)
    {
        var renderers = hinge.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        var collider = hinge.gameObject.AddComponent<BoxCollider>();
        collider.center = hinge.InverseTransformPoint(bounds.center);
        collider.size = bounds.size;
    }
}

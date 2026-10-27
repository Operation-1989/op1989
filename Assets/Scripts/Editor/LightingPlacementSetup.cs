using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Mounts lighting to actual mesh surfaces rather than import bounds.</summary>
public static class LightingPlacementSetup
{
    const string Work = "Temp/LightingPlacement";
    const string Folder = "Assets/DuskLighting/";
    static readonly Color Warm = new Color(1, .85f, .68f);
    static readonly Color Cyan = new Color(.08f, .72f, 1);
    static readonly Color Violet = new Color(.65f, .12f, 1);
    static readonly List<string> mounts = new List<string>();
    static Material metal, warm, cyan, violet, red, green;

    [InitializeOnLoadMethod]
    static void Initialize()
    {
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/ready.txt", "8");
        EditorApplication.update += Poll;
        if (SessionState.GetBool("PlacementSmoke", false)) EditorApplication.update += SmokeTick;
    }
    static void Poll()
    {
        string path = Work + "/request.txt";
        if (!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "inspect") Inspect();
            if (command == "apply") Apply();
            if (command == "helicopter-green") AdjustHelicopterGreenLights();
            if (command == "remove-helicopter-steady-red")
            {
                var lamp = Find("navy_uh-1n (1)").transform.Find("Helicopter visibility lighting/Port red navigation lamp");
                if (lamp) Undo.DestroyObjectImmediate(lamp.gameObject);
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                File.WriteAllText(Work + "/steady-red-removed.txt", "Removed helicopter steady red side fixture and saved scene.");
            }
            if (command == "preview") { EditorApplication.QueuePlayerLoopUpdate(); EditorApplication.delayCall += Previews; }
            if (command == "smoke")
            {
                states.Clear();
                SessionState.SetBool("PlacementBackground", Application.runInBackground);
                SessionState.SetBool("PlacementSmoke", true);
                EditorApplication.update -= SmokeTick;
                EditorApplication.update += SmokeTick;
                EditorApplication.isPaused = false;
                EditorApplication.isPlaying = true;
            }
        }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); Debug.LogException(e); }
    }
    static GameObject Find(string name) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).FirstOrDefault(t => t.name == name && t.gameObject.scene == EditorSceneManager.GetActiveScene())?.gameObject;
    static Renderer[] Models(GameObject go) => go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer) && !r.name.Contains("fixture light") && !r.sharedMaterials.Any(m => m && m.name == "Studio floor material")).ToArray();
    static Bounds BoundsOf(GameObject go)
    {
        var rs = Models(go); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Imported lamps and motors contain multiple disconnected parts in one mesh.
    static List<Bounds> Islands(Renderer renderer)
    {
        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        var vertices = mesh.vertices; var triangles = mesh.triangles;
        var parents = Enumerable.Range(0, vertices.Length).ToArray();
        int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parents[b] = a; }
        var welded = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var key = Vector3Int.RoundToInt(vertices[i] * 10000);
            if (welded.TryGetValue(key, out int other)) Join(i, other); else welded[key] = i;
        }
        for (int i = 0; i < triangles.Length; i += 3) { Join(triangles[i], triangles[i + 1]); Join(triangles[i], triangles[i + 2]); }
        var bounds = new Dictionary<int, Bounds>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int root = Root(i); var p = renderer.transform.TransformPoint(vertices[i]);
            if (bounds.TryGetValue(root, out var b)) { b.Encapsulate(p); bounds[root] = b; } else bounds[root] = new Bounds(p, Vector3.zero);
        }
        return bounds.Values.ToList();
    }
    static List<Bounds> Cluster(IEnumerable<Bounds> parts, float distance)
    {
        var result = new List<Bounds>();
        foreach (var part in parts)
        {
            int index = result.FindIndex(b => Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(part.center.x, part.center.z)) < distance);
            if (index < 0) result.Add(part);
            else { var b = result[index]; b.Encapsulate(part); result[index] = b; }
        }
        return result;
    }
    static List<Bounds> PendantAnchors()
    {
        var lamp = Find("NATO_Shelter_V1").GetComponentsInChildren<Renderer>().First(r => r.name == "Lampe_1");
        return Cluster(Islands(lamp).Where(b => b.center.y > 4 && b.size.x < 1.4f && b.size.z < 1.4f && b.size.sqrMagnitude > .005f), .4f);
    }
    static List<Bounds> MotorAnchors()
    {
        var drone = Object.FindAnyObjectByType<DroneMapPatrol>();
        var motor = drone.GetComponentsInChildren<Renderer>().First(r => r.name.Trim() == "Motors");
        return Cluster(Islands(motor).Where(b => b.size.x > .025f && b.size.z > .025f), .12f);
    }
    static void Inspect()
    {
        var report = new StringBuilder();
        var lamps = PendantAnchors(); var motors = MotorAnchors();
        report.AppendLine("Existing hangar pendant anchors=" + lamps.Count);
        foreach (var b in lamps) report.AppendLine("PENDANT " + b);
        report.AppendLine("Drone motor anchors=" + motors.Count);
        foreach (var b in motors) report.AppendLine("MOTOR " + b);
        var heli = Find("navy_uh-1n (1)");
        report.AppendLine($"HELI rotation={heli.transform.eulerAngles} bounds={BoundsOf(heli)}");
        foreach (var r in Models(heli)) report.AppendLine($"HELI MESH {r.name} {r.bounds}");
        foreach (var g in EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name.StartsWith("server") || g.name.StartsWith("electronictower")))
            report.AppendLine($"RACK {g.name} position={g.transform.position} bounds={BoundsOf(g)}");
        foreach (var light in Object.FindObjectsByType<Light>().Where(l => l.isActiveAndEnabled && (l.name.Contains("wash") || l.name.Contains("downlight"))))
            report.AppendLine($"LIGHT {light.name} position={light.transform.position:F2} parent={light.transform.parent?.name}");
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
    }

    // Temporary collider copies provide exact surface hits without editing imported objects.
    sealed class Surfaces : IDisposable
    {
        readonly List<GameObject> probes = new List<GameObject>();
        readonly List<Collider> colliders = new List<Collider>();
        readonly bool previousBackfaces;
        public Surfaces(IEnumerable<Renderer> renderers)
        {
            previousBackfaces = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var go = new GameObject("Temporary lighting surface probe") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation); go.transform.localScale = r.transform.lossyScale;
                var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mf.sharedMesh;
                probes.Add(go); colliders.Add(collider);
            }
            Physics.SyncTransforms();
        }
        public bool Hit(Vector3 start, Vector3 direction, float length, out RaycastHit closest)
        {
            closest = default; float distance = float.PositiveInfinity;
            foreach (var collider in colliders)
                if (collider.Raycast(new Ray(start, direction), out var hit, length) && hit.distance < distance) { closest = hit; distance = hit.distance; }
            return distance < float.PositiveInfinity;
        }
        public void Dispose() { foreach (var go in probes) Object.DestroyImmediate(go); Physics.queriesHitBackfaces = previousBackfaces; }
    }
    static Material Mat(string name, Color color, float glow = 0)
    {
        string path = Folder + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.color = color; mat.SetFloat("_Smoothness", .3f);
        if (glow > 0) { mat.SetColor("_EmissionColor", color * glow); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; MaterialEditor.FixupEmissiveFlag(mat); mat.EnableKeyword("_EMISSION"); }
        EditorUtility.SetDirty(mat); return mat;
    }
    static Transform Group(Transform parent, string name, Vector3 worldPosition)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = worldPosition;
        var s = parent ? parent.lossyScale : Vector3.one; t.localScale = new Vector3(1 / s.x, 1 / s.y, 1 / s.z);
        return t;
    }
    static Renderer Shape(Transform parent, string name, Vector3 localPosition, Vector3 size, Material mat, PrimitiveType type = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name + " fixture light"; go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition; go.transform.localScale = size; Object.DestroyImmediate(go.GetComponent<Collider>());
        var r = go.GetComponent<Renderer>(); r.sharedMaterial = mat; return r;
    }
    static Light LightAt(Transform parent, string name, Vector3 localPosition, Color color, float intensity, float range, bool spot, bool shadows = false)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPosition;
        var light = go.AddComponent<Light>(); light.type = spot ? LightType.Spot : LightType.Point; light.color = color;
        light.intensity = intensity; light.range = range; light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.shadowBias = .035f; light.shadowNormalBias = .12f; light.spotAngle = 110; light.innerSpotAngle = 65;
        var data = go.AddComponent<UniversalAdditionalLightData>(); var serialized = new SerializedObject(data);
        serialized.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 0; serialized.ApplyModifiedPropertiesWithoutUndo();
        return light;
    }
    static void Remove(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t => t.name == name).ToArray()) Undo.DestroyObjectImmediate(t.gameObject);
    }
    [MenuItem("Operation 1989/Lighting/Mount Fixtures and Improve Coverage")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        if (!File.Exists(Work + "/before-placement.unity")) EditorSceneManager.SaveScene(scene, Work + "/before-placement.unity", true);
        FixPlacement(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Validate(); EditorApplication.QueuePlayerLoopUpdate(); EditorApplication.delayCall += Previews;
    }
    public static void FixPlacement()
    {
        var lamps = PendantAnchors(); var motors = MotorAnchors();
        if (lamps.Count < 4 || motors.Count < 4) throw new InvalidOperationException($"Inspect geometry first: pendants={lamps.Count}, motors={motors.Count}");
        mounts.Clear();
        metal = Mat("Fixture charcoal", new Color(.07f, .085f, .09f)); warm = Mat("Hangar warm diffuser", Warm, 3);
        cyan = Mat("Equipment cyan", Cyan, 3.5f); violet = Mat("Equipment violet", Violet, 3);
        red = Mat("Aircraft beacon red", new Color(1, .02f, .01f), 4); green = Mat("Corner navigation green", new Color(.08f, 1, .22f), 4);
        var table = Find("Sci Fi Conference");
        if (table) { Undo.RecordObject(table, "Hide meeting table and chairs"); table.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(table); }
        FixHangar(lamps); FixDrone(motors); FixRooms(); FixControlRoom(); FixBuildings(); FixRoads(); FixAircraftAndVehicles(); HideGameGizmos();
        File.WriteAllText(Work + "/mounts.txt", string.Join("\n", mounts));
    }
    static void FixHangar(List<Bounds> lamps)
    {
        Remove("Hangar overhead lighting");
        var hangar = Find("NATO_Shelter_V1");
        var root = Group(hangar.transform, "Hangar overhead lighting", hangar.transform.position); root.rotation = Quaternion.identity;
        foreach (var b in lamps)
        {
            var point = new Vector3(b.center.x, b.min.y + .012f, b.center.z);
            var fitting = Group(root, "Existing pendant light", point); fitting.rotation = Quaternion.identity;
            float width = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * .6f, .09f, .28f);
            Shape(fitting, "Pendant bulb", Vector3.zero, new Vector3(width, .008f, width), warm, PrimitiveType.Cylinder);
            var light = LightAt(fitting, "Pendant downlight", Vector3.down * .04f, Warm, 7, 12, true);
            light.transform.localRotation = Quaternion.Euler(90, 0, 0);
            mounts.Add($"HANGAR existing shade bottom {point:F3}");
        }
    }
    static void FixDrone(List<Bounds> motors)
    {
        var drone = Object.FindAnyObjectByType<DroneMapPatrol>();
        var body = drone.GetComponentsInChildren<Renderer>().First(r => r.name == "Body");
        Remove("Dusk Drone Lights");
        var rig = Group(drone.transform, "Dusk Drone Lights", drone.transform.position); rig.localRotation = Quaternion.identity;
        var points = motors.Select(b => drone.transform.InverseTransformPoint(b.center)).ToArray();
        var lo = points.Aggregate(Vector3.Min); var hi = points.Aggregate(Vector3.Max); var used = new HashSet<int>();
        foreach (int side in new[] { -1, 1 }) foreach (int end in new[] { -1, 1 })
        {
            var target = new Vector3(side < 0 ? lo.x : hi.x, 0, end < 0 ? lo.z : hi.z);
            int index = Enumerable.Range(0, points.Length).Where(i => !used.Contains(i)).OrderBy(i => new Vector2(points[i].x - target.x, points[i].z - target.z).sqrMagnitude).First(); used.Add(index);
            var b = motors[index]; var position = new Vector3(b.center.x, b.min.y - .014f, b.center.z);
            var fitting = Group(rig, (side < 0 ? "Port red " : "Starboard green ") + (end < 0 ? "rear corner" : "front corner"), position);
            fitting.rotation = drone.transform.rotation;
            Shape(fitting, "Motor LED mount", Vector3.up * .012f, new Vector3(.08f, .035f, .08f), metal);
            var color = side < 0 ? new Color(1, .04f, .02f) : new Color(.08f, 1, .22f);
            var lens = Shape(fitting, "Corner LED lens", Vector3.down * .01f, new Vector3(.065f, .014f, .065f), side < 0 ? red : green);
            var light = LightAt(fitting, "Corner navigation glow", Vector3.down * .025f, color, .4f, 1.6f, false);
            var pulse = fitting.gameObject.AddComponent<AircraftLightPulse>(); pulse.source = light; pulse.lens = lens; pulse.color = color;
            pulse.pattern = AircraftLightPulse.Pattern.NavigationPulse; pulse.phaseOffset = end < 0 ? .6f : 0;
            mounts.Add($"DRONE {fitting.name} motor underside={b.min.y:F3} fitting={position:F3}");
        }
        var center = body.bounds.center; center.y = body.bounds.min.y - .015f;
        var landing = Group(rig, "Landing light housing", center); landing.rotation = drone.transform.rotation;
        Shape(landing, "Landing lamp mount", Vector3.zero, new Vector3(.16f, .04f, .16f), metal);
        Shape(landing, "Landing lamp lens", Vector3.down * .025f, new Vector3(.13f, .008f, .13f), warm, PrimitiveType.Cylinder);
        var beam = LightAt(landing, "Downward survey light", Vector3.down * .045f, Warm, 65, 45, true);
        beam.transform.localRotation = Quaternion.Euler(90, 0, 0); beam.spotAngle = 48; beam.innerSpotAngle = 32;
        var pad = drone.launchPoint.parent; var deck = pad.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name.Contains("Raised landing deck"));
        if (deck)
        {
            foreach (var marker in Object.FindObjectsByType<Transform>().Where(t => t.name.StartsWith("Landing pad marker")))
            { Undo.RecordObject(marker, "Seat pad markers on deck"); var p = marker.position; p.y = deck.bounds.max.y + marker.GetComponent<Renderer>().bounds.extents.y; marker.position = p; }
            Remove("Mounted pad lights");
            var padLights = Group(pad, "Mounted pad lights", pad.position); padLights.rotation = Quaternion.identity;
            foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                var point = new Vector3(deck.bounds.center.x + x * 1.4f, deck.bounds.max.y + .012f, deck.bounds.center.z + z * 1.4f);
                var fitting = Group(padLights, "Recessed pad marker", point); fitting.rotation = Quaternion.identity;
                Shape(fitting, "Pad marker lens", Vector3.zero, new Vector3(.07f, .024f, .07f), warm);
            }
        }
    }
    static void FixRoads()
    {
        var roads = Find("Road and path lamps"); if (!roads) return;
        foreach (Transform pole in roads.transform.Cast<Transform>().Where(t => t.position.x > -11 && t.position.x < 14 && t.position.z > 17.5f && t.position.z < 29.8f).ToArray())
            Undo.DestroyObjectImmediate(pole.gameObject);
        foreach (var light in roads.GetComponentsInChildren<Light>()) { Undo.RecordObject(light, "Brighten road lighting"); light.intensity = 9.5f; light.range = 18; }
        using (var ground = new Surfaces(Models(Find("Plane"))))
            foreach (Transform pole in roads.transform)
                if (ground.Hit(pole.position + Vector3.up * 50, Vector3.down, 100, out var hit))
                { Undo.RecordObject(pole, "Ground road lamp plinth"); var p = pole.position; p.y = hit.point.y; pole.position = p; }
    }
    static void FixRooms()
    {
        var building = Find("building_1");
        using (var walls = new Surfaces(Models(building)))
        {
            foreach (var room in Object.FindObjectsByType<Transform>().Where(t => t.name == "Server room" || t.name == "East equipment room").ToArray())
            {
                float x = room.name == "Server room" ? -8.4f : 11.2f;
                foreach (Transform child in room.Cast<Transform>().Where(t => t.name == "Wall mounted cyan fitting" || t.name.Contains("Ceiling channel") || t.name.Contains("Cyan ceiling inset") || t.name == "Cool overhead equipment wash").ToArray()) Undo.DestroyObjectImmediate(child.gameObject);
                foreach (int side in new[] { -1, 1 })
                {
                    RaycastHit hit = default; bool found = false;
                    foreach (float y in new[] { 4.2f, 3.7f, 3.2f })
                        if (walls.Hit(new Vector3(x, y, 23.5f), Vector3.forward * side, 12, out hit)) { found = true; break; }
                    if (!found) throw new InvalidOperationException("No wall support for " + room.name);
                    var normal = -Vector3.forward * side;
                    var fitting = Group(room, "Wall mounted cyan fitting", hit.point + normal * .055f); fitting.rotation = Quaternion.LookRotation(normal);
                    Shape(fitting, "Wall rail backing", Vector3.zero, new Vector3(3.4f, .12f, .1f), metal);
                    Shape(fitting, "Cyan diffuser", new Vector3(0, -.015f, .06f), new Vector3(3.2f, .04f, .025f), cyan);
                    var light = LightAt(fitting, "Mounted room downlight", new Vector3(0, -.12f, .13f), new Color(.55f, .77f, 1), 4, 10, true, true);
                    light.transform.localRotation = Quaternion.Euler(65, 0, 0);
                    mounts.Add($"ROOM {room.name} wall={hit.point:F3} fixture offset=.055m");
                }
                var trim = room.Cast<Transform>().FirstOrDefault(t => t.name.Contains("Violet equipment trim") || t.name.Contains("Surface mounted violet trim"));
                if (trim && walls.Hit(new Vector3(x, 2.5f, 23.5f), x < 0 ? Vector3.left : Vector3.right, 8, out var sideHit))
                {
                    var normal = x < 0 ? Vector3.right : Vector3.left;
                    Undo.RecordObject(trim, "Seat violet trim on wall"); trim.name = "Surface mounted violet trim fixture light"; trim.position = sideHit.point + normal * .025f;
                    var wash = room.Find("Violet side wash");
                    if (wash) { Undo.RecordObject(wash, "Align violet illumination with trim"); wash.position = trim.position + normal * .09f; }
                }
                var equipment = room.Find("Cyan equipment wash");
                if (equipment)
                {
                    var source = Object.FindObjectsByType<Renderer>().Where(r => r.name.Contains("Equipment edge") || r.name.Contains("Rack edge")).OrderBy(r => Vector3.Distance(r.bounds.center, new Vector3(x, 1.4f, 26))).First();
                    Undo.RecordObject(equipment, "Align equipment illumination with LED"); equipment.position = source.bounds.center + Vector3.forward * .08f;
                }
            }
        }
    }
    static void FixControlRoom()
    {
        Remove("Control room rack spotlights");
        var building = Find("building_1");
        var root = Group(building.transform, "Control room rack spotlights", building.transform.position); root.rotation = Quaternion.identity;
        using (var walls = new Surfaces(Models(building)))
        {
            if (!walls.Hit(new Vector3(-8.8f, 3.6f, 23), Vector3.back, 10, out var wall))
                throw new InvalidOperationException("Control room wall not found.");
            var fitting = Group(root, "Control room warm wall sconce", wall.point + Vector3.forward * .07f);
            Shape(fitting, "Sconce backplate", Vector3.zero, new Vector3(.55f, .18f, .14f), metal);
            Shape(fitting, "Sconce diffuser", new Vector3(0, -.06f, .085f), new Vector3(.43f, .06f, .04f), warm);
            var lamp = LightAt(fitting, "Control room wall light", new Vector3(0, -.15f, .12f), Warm, 12, 12, true, true);
            lamp.transform.rotation = Quaternion.LookRotation(new Vector3(0, -1, .65f));
            var racks = Object.FindObjectsByType<Transform>().Where(t => t.name.EndsWith("equipment lights")).Where(t => t.position.x < 0 && t.position.x > -12 && t.position.z > 17 && t.position.z < 30).ToArray();
            foreach (var rack in racks)
            {
                var edges = rack.GetComponentsInChildren<Renderer>(); if (edges.Length == 0) continue;
                var target = edges[0].bounds; foreach (var edge in edges) target.Encapsulate(edge.bounds);
                foreach (float offset in new[] { -.38f, .38f })
                {
                    var start = new Vector3(target.center.x + offset, Mathf.Max(3.3f, target.max.y + .5f), target.center.z);
                    RaycastHit best = default; float nearest = float.PositiveInfinity;
                    foreach (var direction in new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward })
                        if (walls.Hit(start, direction, 15, out var hit) && hit.distance < nearest) { nearest = hit.distance; best = hit; }
                    if (float.IsPositiveInfinity(nearest)) throw new InvalidOperationException("No support for rack spotlight: " + rack.name);
                    var inward = (start - best.point).normalized;
                    var head = Group(root, rack.name + " wall spotlight", best.point + inward * .12f);
                    head.rotation = Quaternion.LookRotation((target.center - head.position).normalized);
                    Shape(head, "Spot housing", Vector3.zero, new Vector3(.17f, .17f, .24f), metal);
                    Shape(head, "Spot lens", Vector3.forward * .125f, new Vector3(.13f, .13f, .015f), warm);
                    var spot = LightAt(head, "Rack display spotlight", Vector3.forward * .15f, new Color(.7f, .87f, 1), 18, 9, true, true);
                    spot.spotAngle = 52; spot.innerSpotAngle = 30;
                    mounts.Add($"RACK spotlight {rack.name} wall={best.point:F2} target={target.center:F2}");
                }
            }
        }
    }
    static IEnumerable<GameObject> Buildings()
    {
        var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var go in roots)
            if (go.name.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0 || go.name == "Security_Guard_Room" || go.name == "NATO_Shelter_V1" || go.name == "fbxTower" || go.name.StartsWith("TEnts+Free")) yield return go;
        foreach (var pack in roots.Where(g => g.name.Contains("Military+base"))) foreach (Transform t in pack.transform)
            if (t.name.StartsWith("container") || t.name == "military tent") yield return t.gameObject;
    }
    static void FixBuildings()
    {
        Remove("Building and watchtower fixtures"); Remove("Mounted building lighting");
        var occupied = new HashSet<Vector3Int>();
        foreach (var building in Buildings().ToArray())
        {
            var rs = Models(building); if (rs.Length == 0) continue;
            var bounds = BoundsOf(building); if (bounds.size.x < 2 || bounds.size.z < 2) continue;
            var root = Group(building.transform, "Mounted building lighting", building.transform.position); root.rotation = Quaternion.identity;
            using (var surfaces = new Surfaces(rs))
            {
                var center = bounds.center;
                if (building.name == "building_1") center = new Vector3(1.5f, 3, 23.5f);
                float y = building.name.StartsWith("TEnts") || building.name == "military tent" ? 1.8f : 3.2f;
                foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    int count = building.name == "IBuilding49" && Mathf.Abs(direction.z) > .5f ? 4 : building.name == "building_1" && Mathf.Abs(direction.z) > .5f ? 2 : 1;
                    for (int i = 0; i < count; i++)
                    {
                        var offset = Vector3.right * (count > 1 ? Mathf.Lerp(-1, 1, i / (float)(count - 1)) * (building.name == "IBuilding49" ? 25 : 5) : 0);
                        var target = new Vector3(center.x, y, center.z) + offset;
                        float distance = Mathf.Max(bounds.size.x, bounds.size.z) + 4;
                        if (!surfaces.Hit(target + direction * distance, -direction, distance * 2, out var hit) || Mathf.Abs(hit.normal.y) > .65f) continue;
                        if (!occupied.Add(Vector3Int.RoundToInt(hit.point * 4))) continue;
                        var fitting = Group(root, building.name + " surface fixture", hit.point + direction * .095f); fitting.rotation = Quaternion.LookRotation(direction);
                        Shape(fitting, "Surface backplate", Vector3.zero, new Vector3(.35f, .24f, .18f), metal);
                        Shape(fitting, "Warm lens", new Vector3(0, -.045f, .105f), new Vector3(.26f, .08f, .025f), warm);
                        var light = LightAt(fitting, "Facade downlight", new Vector3(0, -.17f, .14f), Warm, 6, 13, true, building.name == "building_1" || building.name == "Security_Guard_Room");
                        light.transform.localRotation = Quaternion.Euler(65, 0, 0);
                        mounts.Add($"BUILDING {building.name} support={hit.point:F3} bracket offset=.095m");
                    }
                }
            }
        }
        var body = Find("Tower_Searchlight_Body"); var lens = Find("Tower_Searchlight_Lens");
        if (body && lens)
        {
            Remove("Mounted tower searchlight");
            var root = Group(body.transform, "Mounted tower searchlight", lens.transform.position + Vector3.back * .03f); root.rotation = Quaternion.Euler(48, 180, 0);
            var light = LightAt(root, "Watchtower directed beam", Vector3.zero, Warm, 9, 35, true, true); light.spotAngle = 65;
            Undo.RecordObject(lens.GetComponent<Renderer>(), "Illuminate existing searchlight lens"); lens.GetComponent<Renderer>().sharedMaterial = warm;
        }
    }
    static void FixAircraftAndVehicles()
    {
        var helicopter = Find("navy_uh-1n (1)");
        if (helicopter)
            using (var hull = new Surfaces(Models(helicopter).Where(r => !r.name.Contains("prop") && !r.name.Contains("fixture"))))
                foreach (var pulse in helicopter.GetComponentsInChildren<AircraftLightPulse>().Where(p => p.transform.parent.name == "Aircraft ground beacon lights"))
                {
                    bool top = pulse.name.StartsWith("Top"); var p = pulse.transform.position;
                    if (hull.Hit(new Vector3(p.x, top ? 30 : -5, p.z), top ? Vector3.down : Vector3.up, 40, out var hit))
                    { Undo.RecordObject(pulse.transform, "Attach helicopter beacon to hull"); pulse.transform.position = hit.point + (top ? Vector3.up : Vector3.down) * .035f; mounts.Add("HELICOPTER beacon on hull " + hit.point); }
                    Undo.RecordObject(pulse, "Brighten helicopter beacon"); pulse.emission = 10;
                    Undo.RecordObject(pulse.source, "Brighten helicopter beacon"); pulse.source.intensity = 3; pulse.source.range = 7;
                }
        if (helicopter) FixHelicopterVisibility(helicopter);
        foreach (var vehicle in new[] { Find("Patrol Jeep (UAZ)"), Find("Intro Entrance Truck") }.Where(g => g))
        {
            var rig = vehicle.transform.Find("Dusk Vehicle Lights"); if (!rig) continue;
            using (var hull = new Surfaces(Models(vehicle)))
                foreach (var light in rig.GetComponentsInChildren<Light>())
                {
                    var direction = light.name == "Low beam" ? light.transform.forward : -vehicle.transform.forward;
                    if (!hull.Hit(light.transform.position + direction, -direction, 4, out var hit)) continue;
                    var old = light.transform.position; var delta = hit.point + direction * .12f - old;
                    var nearby = rig.GetComponentsInChildren<Renderer>().Where(r => Vector3.Distance(r.bounds.center, old) < .4f).ToArray();
                    Undo.RecordObject(light.transform, "Attach vehicle lamp to body"); light.transform.position += delta;
                    foreach (var renderer in nearby) { Undo.RecordObject(renderer.transform, "Attach vehicle lens to body"); renderer.transform.position += delta; }
                }
        }
    }
    static void FixHelicopterVisibility(GameObject helicopter)
    {
        Remove("Helicopter visibility lighting");
        var root = Group(helicopter.transform, "Helicopter visibility lighting", helicopter.transform.position); root.rotation = helicopter.transform.rotation;
        var white = Mat("Helicopter strobe white", Color.white, 10);
        var amber = Mat("Helicopter cabin amber", Warm, 3);
        using (var hull = new Surfaces(Models(helicopter).Where(r => !r.name.Contains("prop"))))
        {
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 outward = root.right * side;
                foreach (bool strobe in new[] { false, true })
                {
                    if (!strobe && side < 0) continue;
                    var start = root.TransformPoint(new Vector3(side * 4, 2.55f, strobe ? -1.4f : 1.25f));
                    if (!hull.Hit(start, -outward, 5, out var hit)) throw new InvalidOperationException("Helicopter side mounting surface missing.");
                    var mount = Group(root, strobe ? "Side double strobe" : side < 0 ? "Port red navigation lamp" : "Starboard green navigation lamp", hit.point + outward * .035f);
                    mount.rotation = Quaternion.LookRotation(outward);
                    if (!strobe && side > 0)
                    {
                        if (!hull.Hit(root.TransformPoint(new Vector3(0, -2, 1.25f)), Vector3.up, 6, out var belly))
                            throw new InvalidOperationException("Helicopter belly mounting surface missing.");
                        mount.position = belly.point + Vector3.down * .035f;
                        mount.rotation = Quaternion.LookRotation(Vector3.down, root.forward);
                    }
                    Shape(mount, "Flush lamp base", Vector3.zero, new Vector3(.16f, .12f, .09f), metal);
                    var color = strobe ? Color.white : side < 0 ? Color.red : new Color(.08f, 1, .22f);
                    var lens = Shape(mount, "Aircraft lens", Vector3.forward * .05f, new Vector3(.12f, .08f, .025f), strobe ? white : side < 0 ? red : green);
                    var lamp = LightAt(mount, strobe ? "White strobe illumination" : "Navigation illumination", Vector3.forward * .08f, color, strobe ? 5 : 1, strobe ? 9 : 4, false);
                    if (strobe)
                    {
                        var pulse = mount.gameObject.AddComponent<AircraftLightPulse>(); pulse.lens = lens; pulse.source = lamp; pulse.color = color;
                        pulse.pattern = AircraftLightPulse.Pattern.DoubleStrobe; pulse.phaseOffset = side < 0 ? 0 : .6f; pulse.emission = 12;
                    }
                }
                // Amber door-sill accents sit on the fuselage, with a small contained wash.
                var sillStart = root.TransformPoint(new Vector3(side * 4, 1.25f, .3f));
                if (hull.Hit(sillStart, -outward, 5, out var sill))
                {
                    var mount = Group(root, "Cargo door sill accent", sill.point + outward * .025f); mount.rotation = root.rotation;
                    Shape(mount, "Sill accent backing", Vector3.zero, new Vector3(.075f, .09f, 1.2f), metal);
                    Shape(mount, "Amber door sill strip", new Vector3(side * .045f, 0, 0), new Vector3(.025f, .035f, 1.1f), amber);
                    LightAt(mount, "Door access glow", new Vector3(side * .08f, -.05f, 0), Warm, .65f, 2.5f, false);
                }
            }
            foreach (float z in new[] { .6f, 3f })
            {
                var start = root.TransformPoint(new Vector3(0, 2, z));
                if (!hull.Hit(start, Vector3.up, 2, out var roof)) throw new InvalidOperationException("Helicopter cabin roof missing.");
                var mount = Group(root, "Cabin ceiling fixture", roof.point + Vector3.down * .045f); mount.rotation = root.rotation;
                Shape(mount, "Cabin light housing", Vector3.zero, new Vector3(.36f, .065f, .18f), metal);
                Shape(mount, "Cabin light diffuser", Vector3.down * .04f, new Vector3(.29f, .02f, .12f), z < 1 ? cyan : green);
                var lamp = LightAt(mount, "Interior cabin downlight", Vector3.down * .07f, z < 1 ? new Color(.35f, .7f, 1) : new Color(.08f, 1, .22f), 4, 4.5f, true, true);
                lamp.transform.rotation = Quaternion.Euler(90, 0, 0); lamp.spotAngle = 120; lamp.innerSpotAngle = 90;
            }
            var tailStart = root.TransformPoint(new Vector3(0, 8, -10));
            if (hull.Hit(tailStart, Vector3.down, 9, out var tail))
            {
                var mount = Group(root, "Tail red beacon", tail.point + Vector3.up * .035f);
                var lens = Shape(mount, "Tail beacon lens", Vector3.zero, new Vector3(.12f, .10f, .12f), red, PrimitiveType.Sphere);
                var lamp = LightAt(mount, "Tail beacon illumination", Vector3.up * .04f, Color.red, 2, 6, false);
                var pulse = mount.gameObject.AddComponent<AircraftLightPulse>(); pulse.source = lamp; pulse.lens = lens; pulse.color = Color.red;
                pulse.pattern = AircraftLightPulse.Pattern.RedBeacon; pulse.phaseOffset = .35f; pulse.emission = 10;
            }
        }
    }
    static void AdjustHelicopterGreenLights()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        var helicopter = Find("navy_uh-1n (1)");
        var root = helicopter.transform.Find("Helicopter visibility lighting");
        var navigation = root.Find("Starboard green navigation lamp");
        var interior = root.GetComponentsInChildren<Light>().Single(l => l.name == "Interior cabin downlight" && l.color.r > .8f);
        var diffuser = interior.transform.parent.GetComponentsInChildren<Renderer>().Single(r => r.name.StartsWith("Cabin light diffuser"));
        var greenMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Corner navigation green.mat");
        if (!greenMaterial) throw new InvalidOperationException("Existing green lens material missing.");
        using (var hull = new Surfaces(Models(helicopter).Where(r => !r.name.Contains("prop"))))
        {
            if (!hull.Hit(root.TransformPoint(new Vector3(0, -2, 1.25f)), Vector3.up, 6, out var belly))
                throw new InvalidOperationException("Helicopter belly mounting surface missing.");
            Undo.RecordObjects(new Object[] { interior, diffuser, navigation }, "Adjust helicopter green lights");
            interior.color = new Color(.08f, 1, .22f);
            diffuser.sharedMaterial = greenMaterial;
            navigation.position = belly.point + Vector3.down * .035f;
            navigation.rotation = Quaternion.LookRotation(Vector3.down, root.forward);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Work + "/helicopter-green.txt", $"PASS White interior light changed to green: {interior.color}\nPASS Existing side green lamp moved beneath hull: {navigation.position:F3}\nOnly the interior light, its diffuser material, and the existing green navigation fixture transform were edited.\n");
    }
    static int HideGameGizmos()
    {
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"); int count = 0;
        if (type == null) return count;
        foreach (var window in Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>())
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var property = current.GetProperty("drawGizmos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property?.CanWrite == true && property.PropertyType == typeof(bool)) { property.SetValue(window, false); count++; break; }
                var field = current.GetField("m_Gizmos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(bool)) { field.SetValue(window, false); count++; break; }
            }
            window.Repaint();
        }
        File.WriteAllText(Work + "/game-view.txt", "Game views with Gizmos disabled=" + count); return count;
    }
    static void Validate()
    {
        var drone = Object.FindAnyObjectByType<DroneMapPatrol>(); var rig = drone.transform.Find("Dusk Drone Lights");
        var pulses = rig.GetComponentsInChildren<AircraftLightPulse>(); var landing = rig.GetComponentsInChildren<Light>().First(l => l.name == "Downward survey light");
        var road = Find("Road and path lamps").GetComponentsInChildren<Light>();
        var report = new StringBuilder();
        report.AppendLine($"{(!Find("Sci Fi Conference").activeSelf ? "PASS" : "FAIL")} Meeting table, chairs and displays hidden together");
        report.AppendLine($"{(pulses.Length == 4 ? "PASS" : "FAIL")} Four motor-mounted drone corner lights");
        report.AppendLine($"{(landing.intensity == 65 ? "PASS" : "FAIL")} Landing light intensity={landing.intensity}, range={landing.range}");
        report.AppendLine($"{(road.All(l => l.intensity == 9.5f) ? "PASS" : "FAIL")} Road lighting increased from 5.5 to 9.5; lamps={road.Length}");
        report.AppendLine($"Existing hangar pendants lit={Find("Hangar overhead lighting").GetComponentsInChildren<Light>().Length}; suspended rectangular bars={Object.FindObjectsByType<Transform>().Count(t => t.name == "Suspended hangar luminaire")}");
        report.AppendLine($"Surface-mounted facade lights={Object.FindObjectsByType<Light>().Count(l => l.name == "Facade downlight")}");
        report.AppendLine($"{(!Find("Road and path lamps").transform.Cast<Transform>().Any(t => t.position.x > -11 && t.position.x < 14 && t.position.z > 17.5f && t.position.z < 29.8f) ? "PASS" : "FAIL")} No street-lamp posts inside control room");
        report.AppendLine($"{(Find("Control room rack spotlights").GetComponentsInChildren<Light>().Count(l => l.name == "Rack display spotlight") == 2 ? "PASS" : "FAIL")} Two mounted data-rack spotlights");
        report.AppendLine($"Helicopter accent/cabin/navigation lights={Find("Helicopter visibility lighting").GetComponentsInChildren<Light>().Length}; flashing channels={Find("navy_uh-1n (1)").GetComponentsInChildren<AircraftLightPulse>().Length}");
        report.AppendLine("Mesh attachment details: mounts.txt");
        File.WriteAllText(Work + "/validation.txt", report.ToString());
    }
    static void Previews()
    {
        var drone = Object.FindAnyObjectByType<DroneMapPatrol>();
        Capture(drone.transform.position + new Vector3(3, 1.7f, -3), drone.transform.position, "drone-mounted");
        Capture(new Vector3(-23, 2.5f, 7), new Vector3(-53, 4, 5.6f), "hangar-existing-fixtures");
        Capture(new Vector3(-3.2f, 2.1f, 23.5f), new Vector3(-10, 1.5f, 23), "room-meeting-table-hidden");
        Capture(new Vector3(-62, 8, 44), new Vector3(5, 3, 64), "other-building-lighting");
        Capture(new Vector3(4, 2.3f, -65), new Vector3(0, 1, 10), "road-lighting");
        Capture(new Vector3(61, 4, 5), new Vector3(51.66f, 2.4f, 15.58f), "helicopter-visibility");
        Capture(new Vector3(-3.2f, 2.1f, 23.5f), new Vector3(-5.2f, 1.5f, 18.4f), "control-room-rack");
    }
    static void Capture(Vector3 position, Vector3 target, string name)
    {
        var go = new GameObject("Placement preview camera"); var camera = go.AddComponent<Camera>();
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        go.transform.position = position; go.transform.LookAt(target); camera.nearClipPlane = .04f;
        var rt = new RenderTexture(1100, 750, 24); var prior = RenderTexture.active;
        var texture = new Texture2D(1100, 750, TextureFormat.RGB24, false);
        try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, 1100, 750), 0, 0); texture.Apply(); File.WriteAllBytes(Work + "/" + name + ".png", texture.EncodeToPNG()); }
        finally { RenderTexture.active = prior; camera.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(go); }
    }
    static readonly Dictionary<AircraftLightPulse, int> states = new Dictionary<AircraftLightPulse, int>();
    static void SmokeTick()
    {
        if (!Application.isPlaying) return;
        Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
        foreach (var pulse in Object.FindObjectsByType<AircraftLightPulse>()) { states.TryGetValue(pulse, out int value); if (pulse.source) states[pulse] = value | (pulse.source.enabled ? 1 : 2); }
        if (Time.time < 15) return;
        SessionState.SetBool("PlacementSmoke", false); EditorApplication.update -= SmokeTick;
        try
        {
            var drone = Object.FindAnyObjectByType<DroneMapPatrol>(); var jeep = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
            File.WriteAllText(Work + "/playmode.txt", $"{(states.Count >= 8 && states.Values.All(v => v == 3) ? "PASS" : "FAIL")} All {states.Count} drone and helicopter flashing channels switch on and off\nDrone cruise={drone.Cruising}; position={drone.transform.position}; jeep distance={jeep.DistanceTravelled:F1}; blockedBy={jeep.BlockedBy}\n");
            var table = Find("Sci Fi Conference");
            File.AppendAllText(Work + "/playmode.txt", $"{(!table.activeInHierarchy && table.GetComponentsInChildren<Renderer>(true).All(r => !r.gameObject.activeInHierarchy) ? "PASS" : "FAIL")} Table and all meeting chairs absent from gameplay\n");
            Previews();
            Capture(drone.transform.position + new Vector3(3, 2, -4), drone.transform.position, "drone-in-flight");
        }
        finally { Application.runInBackground = SessionState.GetBool("PlacementBackground", false); EditorApplication.isPlaying = false; }
    }
}

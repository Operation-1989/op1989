using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BaseTexturePolishSetup
{
    const string Work = "Temp/BaseTexturePolish";
    static BaseTexturePolishSetup() { Directory.CreateDirectory(Work); File.WriteAllText(Work + "/ready.txt", "4"); EditorApplication.update += Poll; }
    static void Poll()
    {
        string request = Work + "/request.txt";
        if (File.Exists(Work + "/hangar-plane-request.txt")) request = Work + "/hangar-plane-request.txt";
        if (File.Exists(Work + "/main-building-request.txt")) request = Work + "/main-building-request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try { if (command == "inspect") Inspect(); if (command == "preview-before") Preview("before"); if (command == "preview") Preview("after"); if (command == "apply") Apply(); if (command == "refine") Refine(); if (command == "hangar-plane") PaintHangarAndPlane(); if (command == "main-building") PaintMainBuilding(); }
        catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); }
    }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    const string Folder = "Assets/BaseTexturePolish";
    static Material Painted(Material source, string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!result) { result = new Material(source) { name = name }; AssetDatabase.CreateAsset(result, path); }
        result.SetColor("_BaseColor", color); result.SetColor("_Color", color);
        result.SetFloat("_Metallic", .1f); result.SetFloat("_Smoothness", .2f);
        EditorUtility.SetDirty(result);
        return result;
    }
    [MenuItem("Operation 1989/Materials/Pale Army Green Main Building")]
    public static void PaintMainBuilding()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/army base.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open army base in Edit mode.");
        var building = GameObject.Find("building_1");
        if (!building) throw new InvalidOperationException("Main building not found.");
        int slots = 0;
        foreach (var renderer in building.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i]; if (!source) continue;
                string name = source.name;
                bool masonry = name.StartsWith("b1_brick", StringComparison.Ordinal);
                bool trim = name == "b1_sandstone" && renderer.name != "floor_planks";
                bool existingGreen = name == "Army_Green" && new[] { "base_bricksFloor", "arches_tall", "balconies", "ridge_top" }.Contains(renderer.name);
                if (!masonry && !trim && !existingGreen && !name.StartsWith("Main Building Pale Green ", StringComparison.Ordinal)) continue;
                string label = name.StartsWith("Main Building Pale Green ", StringComparison.Ordinal) ? name : "Main Building Pale Green " + name;
                bool detail = trim || name.Contains("sandstone");
                materials[i] = Painted(source, label, detail ? new Color(.55f, .61f, .46f) : new Color(.62f, .68f, .52f));
                materials[i].SetFloat("_Metallic", 0); EditorUtility.SetDirty(materials[i]);
                slots++; changed = true;
            }
            if (!changed) continue;
            Undo.RecordObject(renderer, "Paint main building pale army green"); renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        if (slots == 0) throw new InvalidOperationException("No exterior building surfaces found.");
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Work + "/main-building-painted.txt", $"Pale army green main building exterior material slots={slots}.");
    }
    [MenuItem("Operation 1989/Materials/Green Hangar and Black Aircraft Wings")]
    public static void PaintHangarAndPlane()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/army base.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open army base in Edit mode.");
        var hangar = GameObject.Find("NATO_Shelter_V1");
        var plane = GameObject.Find("Mystere+IV_French");
        if (!hangar || !plane) throw new InvalidOperationException("Hangar or aircraft not found.");
        int hangarSlots = 0, wingSlots = 0;
        foreach (var root in new[] { hangar, plane })
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i]; if (!source) continue;
                    string name = source.name;
                    bool shell = root == hangar && (name == "SHELTER_EXTERIEUR_V1" || name == "DOOR_Global_V1" ||
                        (renderer.name == "SHELTER" && name == "Door_Dark") || renderer.name.StartsWith("Exterieur DOOR", StringComparison.Ordinal) || name.StartsWith("Hangar Green ", StringComparison.Ordinal));
                    bool wing = root == plane && (name == "Main_Wings" || name == "Arr_Wing" ||
                        renderer.name == "Main_Wing_Flap 1" || name == "Aircraft Wings Black");
                    if (shell)
                    {
                        materials[i] = Painted(source, name.StartsWith("Hangar Green ", StringComparison.Ordinal) ? name : "Hangar Green " + name,
                            new Color(.16f, .22f, .105f));
                        hangarSlots++; changed = true;
                    }
                    else if (wing)
                    {
                        materials[i] = Painted(source, "Aircraft Wings Black", new Color(.045f, .05f, .055f));
                        wingSlots++; changed = true;
                    }
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Paint hangar and aircraft wings"); renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        if (hangarSlots == 0 || wingSlots == 0) throw new InvalidOperationException("No matching surface slots found.");
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Work + "/hangar-plane-painted.txt", $"Green hangar slots={hangarSlots}; black aircraft wing slots={wingSlots}.");
    }
    static void Refine()
    {
        foreach (string name in new[] { "Aircraft Painted Metal.png", "Foliage Cutout.png" })
        {
            var importer = AssetImporter.GetAtPath(Folder + "/Textures/" + name) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing generated texture: " + name);
            importer.maxTextureSize = 1024; importer.mipmapEnabled = true;
            importer.wrapMode = name.StartsWith("Foliage", StringComparison.Ordinal) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (name.StartsWith("Foliage", StringComparison.Ordinal))
            {
                importer.alphaIsTransparency = true; importer.mipMapsPreserveCoverage = true; importer.alphaTestReferenceValue = .35f;
            }
            importer.SaveAndReimport();
        }
        var foliage = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Tree Foliage.mat");
        var texture = Texture(Folder + "/Textures/Foliage Cutout.png");
        foliage.SetTexture("_BaseMap", texture); foliage.SetTexture("_MainTex", texture);
        foliage.SetTexture("_BumpMap", null); foliage.DisableKeyword("_NORMALMAP");
        foliage.SetColor("_BaseColor", new Color(.9f, .97f, .86f));
        foliage.SetFloat("_AlphaClip", 1); foliage.SetFloat("_Cutoff", .35f);
        foliage.EnableKeyword("_ALPHATEST_ON"); foliage.SetOverrideTag("RenderType", "TransparentCutout");
        foliage.renderQueue = 2450; EditorUtility.SetDirty(foliage);
        var deck = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Helipad Army_Green.mat");
        deck.SetColor("_BaseColor", new Color(.52f, .54f, .51f)); EditorUtility.SetDirty(deck);
        foreach (string name in new[] { "Helipad Iron_2", "Helipad Iron_frames" })
        {
            var frame = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
            frame.SetColor("_BaseColor", new Color(.3f, .32f, .33f)); EditorUtility.SetDirty(frame);
        }
        AssetDatabase.SaveAssets();
        Inspect(); EditorApplication.delayCall += () => Preview("after");
        File.WriteAllText(Work + "/refined.txt", "Foliage uses transparent alpha-clipped cards; concrete deck and metal frame tints corrected. Generated maps limited to 1024px with mipmaps.");
    }
    static Texture2D Texture(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    static Material Surface(Material source, string name, Texture2D diffuse, Texture2D normal, Vector2 tiling, float metallic, float smoothness)
    {
        string path = Folder + "/" + name + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result) return result;
        result = new Material(source) { name = name };
        result.shader = Shader.Find("Universal Render Pipeline/Lit");
        result.SetTexture("_BaseMap", diffuse); result.SetTexture("_MainTex", diffuse);
        result.SetTextureScale("_BaseMap", tiling); result.SetTextureScale("_MainTex", tiling);
        result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", smoothness);
        result.SetFloat("_SmoothnessTextureChannel", 0);
        if (normal)
        {
            result.SetTexture("_BumpMap", normal); result.SetFloat("_BumpScale", .45f); result.EnableKeyword("_NORMALMAP");
        }
        AssetDatabase.CreateAsset(result, path);
        return result;
    }
    [MenuItem("Operation 1989/Materials/Texture Trees Helicopters and Helipads")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/army base.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open army base in Edit mode.");
        Directory.CreateDirectory(Folder);
        if (!File.Exists(Work + "/before-texturing.unity")) EditorSceneManager.SaveScene(scene, Work + "/before-texturing.unity", true);
        if (!File.Exists(Work + "/tree-before.png")) Preview("before");
        var paint = Texture(Folder + "/Textures/Aircraft Painted Metal.png");
        var bark = Texture("Assets/Flooded_Grounds/Content/Trees/SrcTextures/FOL_Trunk3_D.tif");
        var barkNormal = Texture("Assets/Flooded_Grounds/Content/Trees/SrcTextures/FOL_Trunk3_N.tif");
        var leaves = Texture("Assets/Flooded_Grounds/Content/Trees/SrcTextures/FOL_Branch1_AS.tif");
        var leafNormal = Texture("Assets/Flooded_Grounds/Content/Trees/SrcTextures/FOL_Branch1_N.tif");
        var concrete = Texture("Assets/Flooded_Grounds/Content/Textures/DET_Concrete1_A.tif");
        var concreteNormal = Texture("Assets/Flooded_Grounds/Content/Textures/DET_Concrete1_N.tif");
        var rubber = Texture("Assets/IntroTruck/Materials/Truck Tire Rubber.png");
        if (!paint || !bark || !leaves || !concrete) throw new InvalidOperationException("A required texture is missing.");
        int trees = 0, helicopters = 0, pads = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            bool tree = root.name.StartsWith("TreeOld_4", StringComparison.Ordinal);
            bool helicopter = root.name.StartsWith("navy_uh-1n", StringComparison.Ordinal);
            bool pad = root.name == "Helipad" || root.name == "Helipad+gameReady(1)";
            if (!tree && !helicopter && !pad) continue;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (!source || AssetDatabase.GetAssetPath(source).StartsWith(Folder, StringComparison.Ordinal)) continue;
                    string name = source.name.ToLowerInvariant();
                    Material replacement = null;
                    if (tree)
                    {
                        bool trunk = name == "tank";
                        replacement = Surface(source, trunk ? "Tree Bark" : "Tree Foliage", trunk ? bark : leaves,
                            trunk ? barkNormal : leafNormal, trunk ? new Vector2(2, 4) : Vector2.one, 0, trunk ? .18f : .25f);
                        replacement.SetColor("_BaseColor", trunk ? new Color(.58f, .49f, .39f) : new Color(.78f, .88f, .66f));
                        if (!trunk) { replacement.SetFloat("_Cull", 0); replacement.doubleSidedGI = true; }
                        trees++;
                    }
                    else if (helicopter && AssetDatabase.GetAssetPath(source) == "Assets/navy_uh-1n.fbx" &&
                        !new[] { "clear", "lenz", "red_grow", "mat4", "mat5", "mat3" }.Contains(name) && !source.mainTexture)
                    {
                        bool soft = new[] { "gum", "seat", "floar", "blade", "inside" }.Contains(name);
                        replacement = Surface(source, "Helicopter " + source.name, soft && rubber ? rubber : paint, null,
                            Vector2.one * 3, soft ? 0 : name.Contains("steel") || name == "minigun" ? .7f : .25f, soft ? .18f : .3f);
                        helicopters++;
                    }
                    else if (pad && !source.mainTexture)
                    {
                        bool frame = name.Contains("iron");
                        replacement = Surface(source, "Helipad " + source.name, frame ? paint : concrete,
                            frame ? null : concreteNormal, Vector2.one * (frame ? 5 : 14), frame ? .65f : 0, frame ? .25f : .12f);
                        pads++;
                    }
                    if (!replacement) continue;
                    materials[i] = replacement; EditorUtility.SetDirty(replacement); changed = true;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Apply realistic base textures"); renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Work + "/applied.txt", $"Updated material slots: trees={trees}; helicopters={helicopters}; helipads={pads}");
        Refine();
    }
    static void Inspect()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/army base.unity") throw new InvalidOperationException("Open army base in Edit mode.");
        var report = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects()) report.AppendLine("ROOT " + root.name);
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include).Where(r => r.gameObject.scene == scene))
        {
            string path = PathOf(r.transform);
            report.AppendLine("RENDERER " + path + " bounds=" + r.bounds);
            foreach (var m in r.sharedMaterials)
            {
                if (!m) { report.AppendLine("  NULL MATERIAL"); continue; }
                report.AppendLine("  MATERIAL " + m.name + " shader=" + m.shader.name + " path=" + AssetDatabase.GetAssetPath(m));
                foreach (string property in m.GetTexturePropertyNames())
                    if (m.GetTexture(property)) report.AppendLine("    " + property + "=" + AssetDatabase.GetAssetPath(m.GetTexture(property)));
            }
        }
        foreach (var terrain in Terrain.activeTerrains) report.AppendLine("TERRAIN " + terrain.name + " tree prototypes=" + string.Join(",", terrain.terrainData.treePrototypes.Select(p => p.prefab ? p.prefab.name : "null")));
        File.WriteAllText(Work + "/inspection.txt", report.ToString());
    }
    static void Preview(string suffix)
    {
        Capture(GameObject.Find("TreeOld_4"), "tree-" + suffix);
        Capture(GameObject.Find("navy_uh-1n (1)"), "helicopter-" + suffix);
        Capture(GameObject.Find("Helipad"), "helipad-" + suffix);
    }
    static void Capture(GameObject target, string name)
    {
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var go = new GameObject("Texture preview camera");
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .19f, .22f);
        camera.fieldOfView = 35; camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
        camera.transform.position = bounds.center + new Vector3(1, .55f, -1).normalized * bounds.size.magnitude * 1.05f;
        camera.transform.LookAt(bounds.center);
        var rt = new RenderTexture(1024, 768, 24); var previous = RenderTexture.active;
        var texture = new Texture2D(1024, 768, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0); texture.Apply();
            File.WriteAllBytes(Work + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        }
    }
}

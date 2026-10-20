using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

public static class PlayerSoldierSetup
{
    const string Folder = "Assets/PlayerSoldier";

    [MenuItem("Operation 1989/Player/Align First Person Camera")]
    public static void AlignCamera()
    {
        if(Application.isPlaying) throw new System.InvalidOperationException("Run in Edit mode.");
        var p=Object.FindAnyObjectByType<PlayerSoldierPresentation>();
        Clip("Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX").SampleAnimation(p.animator.gameObject,0);
        Vector3 eye=p.transform.InverseTransformPoint(p.body.bones.First(b=>b.name.Contains("Head")).position);
        Undo.RecordObject(p.firstPersonCamera.transform,"Align soldier eyes");
        p.firstPersonCamera.transform.localPosition=new Vector3(0,eye.y+0.12f,Mathf.Clamp(eye.z+0.16f,0.28f,0.42f));
        File.WriteAllText("Temp/PlayerSoldier/camera.txt",$"Head={eye} Camera={p.firstPersonCamera.transform.localPosition}");
        EditorSceneManager.MarkSceneDirty(p.gameObject.scene);EditorSceneManager.SaveScene(p.gameObject.scene);
    }

    [MenuItem("Operation 1989/Player/Capture Isolated Previews")]
    public static void Previews()
    {
        var player=GameObject.Find("Intro Player");
        var visual=Object.Instantiate(player.transform.Find("Soldier Visual").gameObject);
        visual.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(0,-90,0));
        Clip("Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX").SampleAnimation(visual,0);
        foreach(var t in visual.GetComponentsInChildren<Transform>()) t.gameObject.layer=31;
        Capture(new Vector3(3,1.5f,4),Vector3.up,"soldier-isolated",1<<31);
        Object.DestroyImmediate(visual);
        var truck=Object.Instantiate(GameObject.Find("Intro Entrance Truck"));
        truck.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        foreach(var t in truck.GetComponentsInChildren<Transform>()) t.gameObject.layer=31;
        Capture(new Vector3(8,4,8),Vector3.up*1.3f,"truck-isolated",1<<31);
        Object.DestroyImmediate(truck);
    }

    [MenuItem("Operation 1989/Player/Build Soldier And Truck Materials")]
    public static void Build()
    {
        if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/army base.unity")
            throw new System.InvalidOperationException("Open army base in Edit mode first.");
        var player = GameObject.Find("Intro Player");
        if (player.transform.Find("Soldier Visual")) throw new System.InvalidOperationException("Soldier already configured.");
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        Undo.RegisterFullObjectHierarchyUndo(player, "Add player soldier");
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LowPolySoldiers_demo/Soldier_demo_Prefab.prefab"));
        visual.name = "Soldier Visual";
        visual.transform.SetParent(player.transform, false);
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * 0.75f;
        visual.transform.localPosition = new Vector3(0,-1,0);
        var animator = visual.GetComponentInChildren<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = CreateController(visual.transform);
        // Sample the existing idle so the mesh and camera can be aligned to the actual rig.
        Clip("Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX").SampleAnimation(visual,0);
        var body = visual.GetComponentInChildren<SkinnedMeshRenderer>();
        var full = body.sharedMesh;
        var headless = Object.Instantiate(full);
        headless.name = "Soldier First Person";
        var weights = full.boneWeights;
        var headBones = new HashSet<int>(body.bones.Select((b,i)=>new{b,i}).Where(x=>x.b.name.Contains("Head")).Select(x=>x.i));
        System.Func<int,bool> head = i =>
        {
            var w = weights[i];
            return (headBones.Contains(w.boneIndex0) ? w.weight0 : 0) + (headBones.Contains(w.boneIndex1) ? w.weight1 : 0) +
                (headBones.Contains(w.boneIndex2) ? w.weight2 : 0) + (headBones.Contains(w.boneIndex3) ? w.weight3 : 0) > 0.35f;
        };
        for(int s=0;s<full.subMeshCount;s++)
        {
            var triangles = full.GetTriangles(s); var kept = new List<int>();
            for(int i=0;i<triangles.Length;i+=3)
                if(!head(triangles[i]) && !head(triangles[i+1]) && !head(triangles[i+2]))
                    kept.AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});
            headless.SetTriangles(kept,s);
        }
        AssetDatabase.CreateAsset(headless,Folder+"/SoldierFirstPerson.asset");
        var presentation = player.AddComponent<PlayerSoldierPresentation>();
        presentation.movement = player.GetComponent<FirstPersonController>();
        presentation.animator = animator;
        presentation.body = body;
        presentation.fullBodyMesh = full;
        presentation.firstPersonMesh = headless;
        presentation.firstPersonCamera = player.GetComponentInChildren<Camera>();
        var camera = presentation.firstPersonCamera;
        var headBone = body.bones.First(b=>b.name.Contains("Head"));
        Vector3 eye = player.transform.InverseTransformPoint(headBone.position);
        camera.transform.localPosition = new Vector3(0,eye.y+0.12f,Mathf.Clamp(eye.z+0.16f,0.28f,0.42f));
        camera.nearClipPlane = 0.04f;
        player.GetComponent<MeshRenderer>().enabled = false;
        presentation.movement.moveSpeed = presentation.walkSpeed;
        TextureTruck();
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        EditorSceneManager.MarkSceneDirty(player.scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(player.scene);
        Inspect();
        File.AppendAllText("Temp/PlayerSoldier/inspection.txt",$"\nCAMERA eye={camera.transform.localPosition} BODY {body.bounds}\n");
        Capture(player.transform.position + player.transform.forward*4 + Vector3.up*0.8f, player.transform.position+Vector3.up*0.1f,"soldier");
        var truck = GameObject.Find("Intro Entrance Truck").transform;
        Capture(truck.position+new Vector3(8,4,8), truck.position+Vector3.up*1.2f,"truck-textured");
    }

    static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));

    static AnimatorController CreateController(Transform root)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(Folder+"/PlayerSoldier.controller");
        controller.AddParameter("Speed",AnimatorControllerParameterType.Float);
        controller.AddParameter("Fire",AnimatorControllerParameterType.Trigger);
        var sm = controller.layers[0].stateMachine;
        var locomotion = sm.AddState("Locomotion");
        var blend = new BlendTree { name="Idle Run Sprint",blendType=BlendTreeType.Simple1D,blendParameter="Speed",useAutomaticThresholds=false };
        AssetDatabase.AddObjectToAsset(blend,controller);
        blend.AddChild(Clip("Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX"),0);
        blend.AddChild(Clip("Assets/LowPolySoldiers_demo/animation/demo_combat_run.FBX"),1);
        blend.AddChild(Clip("Assets/LowPolySoldiers_demo/animation/demo_combat_run.FBX"),2);
        locomotion.motion = blend; sm.defaultState = locomotion;
        var mask = new AvatarMask {name="Rifle Upper Body"};
        var transforms = root.GetComponentsInChildren<Transform>();
        mask.transformCount = transforms.Length;
        for(int i=0;i<transforms.Length;i++)
        {
            string path=AnimationUtility.CalculateTransformPath(transforms[i],root);
            mask.SetTransformPath(i,path);
            mask.SetTransformActive(i,path.Contains("Spine"));
        }
        AssetDatabase.CreateAsset(mask,Folder+"/RifleUpperBody.mask");
        controller.AddLayer("Rifle Fire");
        var layers=controller.layers;
        layers[1].defaultWeight=1; layers[1].avatarMask=mask;
        controller.layers=layers;
        var fireSM=layers[1].stateMachine;
        var empty=fireSM.AddState("Ready"); empty.writeDefaultValues=false; fireSM.defaultState=empty;
        var fire=fireSM.AddState("Fire"); fire.writeDefaultValues=false;
        fire.motion=Clip("Assets/LowPolySoldiers_demo/animation/demo_combat_shoot.FBX");
        var enter=empty.AddTransition(fire); enter.hasExitTime=false; enter.duration=0.04f; enter.AddCondition(AnimatorConditionMode.If,0,"Fire");
        var exit=fire.AddTransition(empty); exit.hasExitTime=true; exit.exitTime=1; exit.duration=0.08f;
        return controller;
    }

    static void TextureTruck()
    {
        string folder="Assets/IntroTruck/Materials";
        Directory.CreateDirectory(folder);
        Material Make(string name,Color color,bool rubber)
        {
            var texture=new Texture2D(256,256,TextureFormat.RGB24,true){name=name+" Albedo",wrapMode=TextureWrapMode.Repeat};
            var pixels=new Color[256*256];
            for(int y=0;y<256;y++) for(int x=0;x<256;x++)
            {
                uint hash=(uint)(x*374761393+y*668265263); hash=(hash^(hash>>13))*1274126177;
                float noise=(hash%1000)/999f;
                float value=rubber ? (0.65f+0.35f*Mathf.SmoothStep(0,1,Mathf.PingPong((x+y*0.3f)/16f,1))) : 0.92f+noise*0.16f;
                if(!rubber && y%57==0 && x%91<16) value=1.32f;
                pixels[y*256+x]=new Color(color.r*value,color.g*value,color.b*value,1);
            }
            texture.SetPixels(pixels); texture.Apply();
            string path=folder+"/"+name+".png";
            File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
            mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            mat.SetColor("_BaseColor",Color.white);
            mat.SetFloat("_Metallic",rubber?0:0.25f); mat.SetFloat("_Smoothness",rubber?0.15f:0.3f);
            AssetDatabase.CreateAsset(mat,folder+"/"+name+".mat");
            return mat;
        }
        var paint=Make("Truck Olive Paint",new Color(0.28f,0.32f,0.17f),false);
        var rubber=Make("Truck Tire Rubber",new Color(0.065f,0.07f,0.065f),true);
        var metal=Make("Truck Wheel Hubs",new Color(0.21f,0.24f,0.14f),false);
        foreach(var renderer in GameObject.Find("Intro Entrance Truck").GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterial=paint;
            if(!renderer.name.Contains("WHEEL")) continue;
            var filter=renderer.GetComponent<MeshFilter>(); var mesh=Object.Instantiate(filter.sharedMesh);
            var vertices=mesh.vertices; var triangles=mesh.triangles;
            var tires=new List<int>(); var hubs=new List<int>();
            // Axles run along model Z. Rear assemblies contain two axles; front meshes contain one.
            float centerY=mesh.bounds.center.y;
            float minX=mesh.bounds.min.x, maxX=mesh.bounds.max.x;
            foreach(int i in Enumerable.Range(0,triangles.Length/3))
            {
                var center=(vertices[triangles[i*3]]+vertices[triangles[i*3+1]]+vertices[triangles[i*3+2]])/3;
                float axleX=mesh.bounds.center.x;
                if(mesh.bounds.size.x>2) axleX=Mathf.Abs(center.x-(minX+0.55f))<Mathf.Abs(center.x-(maxX-0.55f))?minX+0.55f:maxX-0.55f;
                float radius=new Vector2(center.x-axleX,center.y-centerY).magnitude;
                var destination=radius<0.29f ? hubs:tires;
                destination.AddRange(new[]{triangles[i*3],triangles[i*3+1],triangles[i*3+2]});
            }
            mesh.subMeshCount=2; mesh.SetTriangles(tires,0); mesh.SetTriangles(hubs,1);
            AssetDatabase.CreateAsset(mesh,folder+"/"+renderer.name+".asset");
            filter.sharedMesh=mesh; renderer.sharedMaterials=new[]{rubber,metal};
        }
    }

    public static void Capture(Vector3 position,Vector3 target,string name,int mask=-1)
    {
        var go=new GameObject("Soldier inspection camera"); var camera=go.AddComponent<Camera>();
        camera.cullingMask=mask;
        go.transform.position=position; go.transform.LookAt(target); camera.nearClipPlane=0.03f;
        var rt=new RenderTexture(1200,900,24); camera.targetTexture=rt;
        camera.Render(); var previous=RenderTexture.active; RenderTexture.active=rt;
        var image=new Texture2D(1200,900,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1200,900),0,0); image.Apply();
        Directory.CreateDirectory("Temp/PlayerSoldier"); File.WriteAllBytes("Temp/PlayerSoldier/"+name+".png",image.EncodeToPNG());
        RenderTexture.active=previous; Object.DestroyImmediate(image); Object.DestroyImmediate(go); Object.DestroyImmediate(rt);
    }

    [MenuItem("Operation 1989/Player/Inspect Assets")]
    public static void Inspect()
    {
        var report = new StringBuilder();
        var soldier = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LowPolySoldiers_demo/Soldier_demo_Prefab.prefab"));
        foreach (var animator in soldier.GetComponentsInChildren<Animator>())
            report.AppendLine($"ANIMATOR {animator.name} avatar={animator.avatar} human={animator.isHuman}");
        foreach (var renderer in soldier.GetComponentsInChildren<Renderer>())
        {
            report.AppendLine($"SOLDIER {renderer.name} bounds={renderer.bounds} materials={string.Join(",", renderer.sharedMaterials.Select(m=>m ? m.name : "null"))}");
            if (renderer is SkinnedMeshRenderer skin)
                report.AppendLine("BONES " + string.Join(",", skin.bones.Select((b,i)=>i+":"+b.name)));
        }
        Object.DestroyImmediate(soldier);
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/M923_Transport_Truck_3D_TRADER.FBX"))
        {
            if (asset is Mesh mesh) report.AppendLine($"TRUCK MESH {mesh.name} vertices={mesh.vertexCount} submeshes={mesh.subMeshCount} UV={mesh.uv.Length}");
            if (asset is Material mat) report.AppendLine($"TRUCK MAT {mat.name} shader={mat.shader.name} texture={mat.mainTexture}");
            if (asset is Texture tex) report.AppendLine($"TRUCK TEX {tex.name}");
        }
        foreach (var r in GameObject.Find("Intro Entrance Truck").GetComponentsInChildren<Renderer>())
            report.AppendLine($"TRUCK RENDERER {r.name}: {string.Join(",",r.sharedMaterials.Select(m=>m ? m.name : "null"))}");
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[]{"Assets/LowPolySoldiers_demo", "Assets/Animations"}))
            foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")))
                report.AppendLine($"CLIP {AssetDatabase.GUIDToAssetPath(guid)} | {clip.name} {clip.length}");
        Directory.CreateDirectory("Temp/PlayerSoldier");
        File.WriteAllText("Temp/PlayerSoldier/inspection.txt",report.ToString());
    }
}

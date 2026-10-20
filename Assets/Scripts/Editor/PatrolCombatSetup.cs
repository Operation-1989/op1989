using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PatrolCombatSetup
{
    [MenuItem("Operation 1989/Combat/Align Building Checkpoint Patrol")]
    public static void AlignCheckpoint()
    {
        if(Application.isPlaying)throw new System.InvalidOperationException("Use Edit mode.");
        var guard=Object.FindAnyObjectByType<BarricadePatrolSoldier>();
        Undo.RecordObject(guard.transform,"Align checkpoint patrol");
        guard.transform.SetPositionAndRotation(new Vector3(-12,.25f,2),Quaternion.Euler(0,90,0));
        Vector3[] points={new Vector3(-12,.25f,2),new Vector3(-4,.25f,2),new Vector3(4,.25f,2),new Vector3(10,.25f,2)};
        for(int i=0;i<points.Length;i++){Undo.RecordObject(guard.waypoints[i],"Align patrol waypoint");guard.waypoints[i].position=points[i];}
        foreach(var mf in Object.FindObjectsByType<MeshFilter>())
        {
            var t=mf.transform;bool cover=false;
            while(t){if(t.name.ToLower().Contains("concrete_barrier")){cover=true;break;}t=t.parent;}
            if(cover && mf.sharedMesh && !mf.GetComponent<Collider>())Undo.AddComponent<MeshCollider>(mf.gameObject).sharedMesh=mf.sharedMesh;
        }
        EditorSceneManager.MarkSceneDirty(guard.gameObject.scene);EditorSceneManager.SaveScene(guard.gameObject.scene);
        Inspect();
        PlayerSoldierSetup.Capture(new Vector3(0,40,-5),new Vector3(0,0,10),"checkpoint-overview");
        PlayerSoldierSetup.Capture(new Vector3(-15,3,-5),guard.transform.position+Vector3.up,"checkpoint-soldier");
    }
    [MenuItem("Operation 1989/Combat/Install Barricade Patrol")]
    public static void Install()
    {
        if(Application.isPlaying || EditorSceneManager.GetActiveScene().path!="Assets/army base.unity")
            throw new System.InvalidOperationException("Open army base in Edit mode.");
        Directory.CreateDirectory("Assets/Combat");AssetDatabase.Refresh();
        var p=Object.FindAnyObjectByType<PlayerSoldierPresentation>();
        if(!p)throw new System.InvalidOperationException("Player soldier is missing.");
        Undo.RegisterFullObjectHierarchyUndo(p.gameObject,"Install automatic rifle");
        var sound=MakeSound();
        var playerRifle=p.GetComponent<RifleWeapon>();if(!playerRifle)playerRifle=Undo.AddComponent<RifleWeapon>(p.gameObject);
        playerRifle.playerWeapon=true;playerRifle.shotSound=sound;playerRifle.damage=25;
        playerRifle.muzzle=MakeMuzzle(p.animator.transform);
        p.rifle=playerRifle;p.fireInterval=.1f;
        var playerHealth=p.GetComponent<CombatHealth>();if(!playerHealth)playerHealth=Undo.AddComponent<CombatHealth>(p.gameObject);
        playerHealth.isPlayer=true;
        var existing=GameObject.Find("Barricade Patrol Soldier");
        if(!existing)
        {
            var root=new GameObject("Barricade Patrol Soldier");Undo.RegisterCreatedObjectUndo(root,"Place barricade patrol");
            root.transform.SetPositionAndRotation(new Vector3(-12,.25f,2),Quaternion.Euler(0,90,0));
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agents/PatrolSoldier.prefab"),root.transform);
            visual.name="Patrol Soldier Visual";visual.transform.localPosition=Vector3.zero;
            visual.transform.localRotation=Quaternion.identity;visual.transform.localScale=Vector3.one*.75f;
            foreach(var script in visual.GetComponentsInChildren<MonoBehaviour>())Object.DestroyImmediate(script);
            foreach(var collider in visual.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
            var animator=visual.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=p.animator.runtimeAnimatorController;
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            AssetDatabase.LoadAllAssetsAtPath("Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__")).SampleAnimation(visual,0);
            var cc=root.AddComponent<CharacterController>();cc.height=1.9f;cc.radius=.32f;cc.center=Vector3.up*.95f;cc.stepOffset=.3f;
            var health=root.AddComponent<CombatHealth>();health.maximumHealth=100;
            var rifle=root.AddComponent<RifleWeapon>();rifle.shotSound=sound;rifle.damage=8;rifle.range=45;rifle.muzzle=MakeMuzzle(visual.transform);
            var guard=root.AddComponent<BarricadePatrolSoldier>();guard.player=p.transform;guard.animator=animator;
            var route=new GameObject("Barricade Patrol Route");Undo.RegisterCreatedObjectUndo(route,"Patrol waypoints");
            // Walk the open southern edge of the concrete checkpoint opposite the main entrance.
            Vector3[] points={new Vector3(-12,.25f,2),new Vector3(-4,.25f,2),new Vector3(4,.25f,2),new Vector3(10,.25f,2)};
            guard.waypoints=points.Select((point,i)=>{var go=new GameObject("Patrol "+(i+1));go.transform.SetParent(route.transform);go.transform.position=point;return go.transform;}).ToArray();
            foreach(var r in visual.GetComponentsInChildren<Renderer>())PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        foreach(var mf in Object.FindObjectsByType<MeshFilter>())
        {
            var t=mf.transform;
            bool sandbag=false;
            while(t) {if(t.name.ToLower().Contains("sandbag")){sandbag=true;break;}t=t.parent;}
            if((sandbag || mf.name.ToLower().Contains("barrier")) && mf.sharedMesh && !mf.GetComponent<Collider>())Undo.AddComponent<MeshCollider>(mf.gameObject).sharedMesh=mf.sharedMesh;
        }
        EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(p.gameObject.scene);EditorSceneManager.SaveScene(p.gameObject.scene);
        Inspect();
    }
    static Transform MakeMuzzle(Transform root)
    {
        var old=root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="Rifle Muzzle");if(old)return old;
        var weapon=root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name.ToLower().Contains("weapon") || t.name.ToLower().Contains("rifle"));
        if(!weapon)weapon=root.GetComponentsInChildren<Transform>().First(t=>t.name.Contains("R Hand"));
        var muzzle=new GameObject("Rifle Muzzle").transform;muzzle.SetParent(weapon,false);
        // Position in the sampled ready pose, then inherit the animated weapon bone.
        muzzle.position=root.position+Vector3.up*1.35f+root.forward*.8f+root.right*.18f;
        return muzzle;
    }
    static AudioClip MakeSound()
    {
        const string path="Assets/Combat/RifleShot.wav";
        if(!File.Exists(path))
        {
            const int rate=44100,count=11025;var random=new System.Random(1989);
            using(var file=new BinaryWriter(File.Create(path)))
            {
                file.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));file.Write(36+count*2);file.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                file.Write(16);file.Write((short)1);file.Write((short)1);file.Write(rate);file.Write(rate*2);file.Write((short)2);file.Write((short)16);
                file.Write(System.Text.Encoding.ASCII.GetBytes("data"));file.Write(count*2);
                float low=0;
                for(int i=0;i<count;i++)
                {
                    float t=(float)i/rate,noise=(float)random.NextDouble()*2-1;low=Mathf.Lerp(low,noise,.13f);
                    float value=noise*Mathf.Exp(-t*65)*.65f+low*Mathf.Exp(-t*19)*.8f+Mathf.Sin(2*Mathf.PI*(115*t-90*t*t))*Mathf.Exp(-t*32)*.4f;
                    file.Write((short)(Mathf.Clamp(value,-1,1)*28000));
                }
            }
        }
        AssetDatabase.ImportAsset(path);return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
    [MenuItem("Operation 1989/Combat/Inspect Placement")]
    public static void Inspect()
    {
        Directory.CreateDirectory("Temp/PatrolCombat");
        var lines = Object.FindObjectsByType<Transform>()
            .Where(t => t.name.ToLower().Contains("sandbag") || t.name.ToLower().Contains("barrier") || t.name.ToLower().Contains("barric") || t.name=="bUILDING" || t.name=="gateFbx")
            .Select(t => t.name+" position="+t.position+" bounds="+BoundsOf(t));
        File.WriteAllLines("Temp/PatrolCombat/placement.txt",lines);
        PlayerSoldierSetup.Capture(new Vector3(10,85,-55),new Vector3(10,0,-55),"patrol-overview");
    }
    static Bounds BoundsOf(Transform t)
    {
        var rs=t.GetComponentsInChildren<Renderer>();
        var b=new Bounds(t.position,Vector3.zero);
        if(rs.Length>0) {b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);}
        return b;
    }
}

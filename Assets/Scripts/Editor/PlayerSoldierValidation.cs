using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class PlayerSoldierValidation
{
    const string Marker="Temp/PlayerSoldier/testing";
    const string Report="Temp/PlayerSoldier/validation.txt";
    static Keyboard keyboard;
    static Mouse mouse;
    static float until;
    static int stage { get=>SessionState.GetInt("PlayerSoldierTestStage",0); set=>SessionState.SetInt("PlayerSoldierTestStage",value); }
    [InitializeOnLoadMethod]
    static void Resume(){if(File.Exists(Marker)) EditorApplication.update+=Tick;}
    [MenuItem("Operation 1989/Player/Validate Soldier")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new System.InvalidOperationException("Save army base and run from Edit mode.");
        Directory.CreateDirectory("Temp/PlayerSoldier");File.WriteAllText(Report,"Player soldier Play Mode validation\n");File.WriteAllText(Marker,"");
        stage=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void Check(string label,bool pass,string detail="")=>File.AppendAllText(Report,(pass?"PASS ":"FAIL ")+label+" "+detail+"\n");
    static void Tick()
    {
        if(!File.Exists(Marker)){EditorApplication.update-=Tick;return;}
        if(!Application.isPlaying)
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode && stage==6)
            {File.Delete(Marker);EditorApplication.update-=Tick;File.AppendAllText(Report,"Returned to Edit mode.\n");}
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();
        var p=Object.FindAnyObjectByType<PlayerSoldierPresentation>();
        var sequence=Object.FindAnyObjectByType<IntroTruckSequence>();
        if(!p || !sequence)return;
        switch(stage)
        {
            case 0:
                Check("Capsule visual disabled",!p.GetComponent<MeshRenderer>().enabled);
                Check("Player rig and controller",p.animator && p.animator.avatar.isValid && !p.animator.applyRootMotion);
                Check("No patrol AI on player",!p.GetComponentInChildren<AgentPathFollower>() && !p.GetComponentInChildren<AgentChaseController>());
                Check("Full body during aerial",p.body.sharedMesh==p.fullBodyMesh);
                Check("All truck renderers textured",sequence.truck.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m && m.GetTexture("_BaseMap"))));
                stage=1;break;
            case 1:
                if(!sequence.HandedOff || sequence.Elapsed<sequence.cutsceneDuration+0.6f)break;
                Check("Input and first person handoff",p.movement.enabled && p.firstPersonCamera.enabled);
                Check("First person head removed",p.body.sharedMesh==p.firstPersonMesh && p.firstPersonMesh.triangles.Length<p.fullBodyMesh.triangles.Length);
                Check("Idle animation",p.animator.GetCurrentAnimatorClipInfo(0).Any(c=>c.clip.name.Contains("idle") && c.weight>.9f));
                Check("Single AudioListener",Object.FindObjectsByType<AudioListener>().Count(l=>l.isActiveAndEnabled)==1);
                PlayerSoldierSetup.Capture(p.firstPersonCamera.transform.position,p.firstPersonCamera.transform.position+p.firstPersonCamera.transform.forward,"first-person-forward");
                var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.SetPositionAndRotation(new Vector3(3.78f,1.25f,-105),Quaternion.identity);cc.enabled=true;
                keyboard=InputSystem.AddDevice<Keyboard>("PlayerVisualTestKeyboard");mouse=InputSystem.AddDevice<Mouse>("PlayerVisualTestMouse");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));until=Time.time+1;stage=2;break;
            case 2:
                if(Time.time<until)break;
                Check("Walk input drives animation",p.animator.GetFloat("Speed")>.8f && p.animator.GetFloat("Speed")<1.2f,p.animator.GetFloat("Speed").ToString());
                Check("Walk clip plays",p.animator.GetCurrentAnimatorClipInfo(0).Any(c=>c.clip.name.Contains("mixamo") && c.weight>.8f));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.LeftShift));until=Time.time+1;stage=3;break;
            case 3:
                if(Time.time<until)break;
                Check("Sprint speed",Mathf.Approximately(p.movement.moveSpeed,p.runSpeed));
                Check("Run clip plays",p.animator.GetCurrentAnimatorClipInfo(0).Any(c=>c.clip.name.Contains("run") && c.weight>.8f));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                Cursor.lockState=CursorLockMode.Locked;
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));until=Time.time+0.15f;stage=4;break;
            case 4:
                if(Time.time<until)break;
                Check("Fire animation",p.animator.GetCurrentAnimatorStateInfo(1).IsName("Fire"));
                InputSystem.QueueStateEvent(mouse,new MouseState());until=Time.time+1;stage=5;break;
            case 5:
                if(Time.time<until)break;
                Check("Idle restored",p.animator.GetFloat("Speed")<.1f);
                Check("Fire layer returns ready",p.animator.GetCurrentAnimatorStateInfo(1).IsName("Ready"));
                p.firstPersonCamera.transform.localRotation=Quaternion.Euler(55,0,0);
                PlayerSoldierSetup.Capture(p.firstPersonCamera.transform.position,p.firstPersonCamera.transform.position+p.firstPersonCamera.transform.forward,"first-person-down");
                InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);
                stage=6;EditorApplication.isPlaying=false;break;
        }
    }
}

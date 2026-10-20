using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class PatrolCombatValidation
{
    const string Folder="Temp/PatrolCombat/",Marker=Folder+"testing",Report=Folder+"validation.txt";
    static int stage {get=>SessionState.GetInt("PatrolCombatStage",0);set=>SessionState.SetInt("PatrolCombatStage",value);}
    static float until;
    static Mouse mouse;
    static Vector3 patrolStart,advanceStart;
    static int shots;
    static int fireStartFrame;
    static InputSettings.BackgroundBehavior previousBackground;
    static bool previousRunInBackground;
    static GameObject floor,wall;
    [InitializeOnLoadMethod] static void Resume(){if(File.Exists(Marker))EditorApplication.update+=Tick;}
    [MenuItem("Operation 1989/Combat/Validate Combat")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);File.WriteAllText(Report,"Patrol combat Play Mode checks\n");File.WriteAllText(Marker,"");
        stage=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void Check(string name,bool pass)=>File.AppendAllText(Report,(pass?"PASS ":"FAIL ")+name+"\n");
    static void Teleport(Transform t,Vector3 p,Quaternion r)
    {var cc=t.GetComponent<CharacterController>();cc.enabled=false;t.SetPositionAndRotation(p,r);cc.enabled=true;Physics.SyncTransforms();}
    static void Tick()
    {
        if(!File.Exists(Marker)){EditorApplication.update-=Tick;return;}
        if(!Application.isPlaying)
        {
            if(stage==7 && !EditorApplication.isPlayingOrWillChangePlaymode){File.Delete(Marker);EditorApplication.update-=Tick;}
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();
        var p=Object.FindAnyObjectByType<PlayerSoldierPresentation>();var guard=Object.FindAnyObjectByType<BarricadePatrolSoldier>();
        var intro=Object.FindAnyObjectByType<IntroTruckSequence>();
        if(!p || !guard || !intro)return;
        try
        {
            switch(stage)
            {
                case 0:
                    previousBackground=InputSystem.settings.backgroundBehavior;
                    previousRunInBackground=Application.runInBackground;
                    InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                    Application.runInBackground=true;
                    Check("Scene guard and four patrol waypoints",guard.waypoints.Length==4);
                    Check("Both rifles have sounds and animated muzzle",p.rifle.shotSound && guard.GetComponent<RifleWeapon>().shotSound && p.rifle.muzzle && guard.GetComponent<RifleWeapon>().muzzle);
                    patrolStart=guard.transform.position;until=Time.time+2;stage=1;break;
                case 1:
                    if(Time.time<until || !intro.HandedOff)break;
                    Check("Guard walks near barricades",Vector3.Distance(guard.transform.position,patrolStart)>1 && guard.State=="Patrol");
                    floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Temporary combat test floor";floor.transform.position=new Vector3(300,9,300);floor.transform.localScale=new Vector3(100,1,100);
                    Teleport(p.transform,new Vector3(300,10.55f,280),Quaternion.identity);p.firstPersonCamera.transform.localRotation=Quaternion.identity;
                    Teleport(guard.transform,new Vector3(300,9.6f,305),Quaternion.Euler(0,180,0));
                    advanceStart=guard.transform.position;guard.ResetPatrol();guard.Alert();until=Time.time+2;stage=2;break;
                case 2:
                    if(Time.time<until)break;
                    Check("Short advance then ranged fire",guard.State=="Fire" && guard.AdvancedDistance<=4.1f && Vector3.Distance(advanceStart,guard.transform.position)<=4.2f);
                    Check("Guard fired real shots",guard.GetComponent<RifleWeapon>().ShotsFired>0);
                    guard.enabled=false;p.GetComponent<CombatHealth>().Restore();
                    Teleport(guard.transform,new Vector3(300,9.6f,305),Quaternion.Euler(0,180,0));
                    // Aim above the guard to check held input without killing the test target.
                    p.firstPersonCamera.transform.localRotation=Quaternion.Euler(-25,0,0);
                    mouse=InputSystem.AddDevice<Mouse>("CombatValidationMouse");Cursor.lockState=CursorLockMode.Locked;
                    shots=p.rifle.ShotsFired;fireStartFrame=Time.frameCount;InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));until=Time.time+1;stage=3;break;
                case 3:
                    Cursor.lockState=CursorLockMode.Locked;
                    if(Time.time<until || Time.frameCount-fireStartFrame<15)break;
                    Check("Hold mouse fires repeatedly",p.rifle.ShotsFired-shots>=5);
                    InputSystem.QueueStateEvent(mouse,new MouseState());shots=p.rifle.ShotsFired;until=Time.time+.3f;stage=4;break;
                case 4:
                    if(Time.time<until)break;
                    Check("Release mouse stops shooting",p.rifle.ShotsFired==shots);
                    var health=guard.GetComponent<CombatHealth>();health.Restore();guard.enabled=false;
                    Teleport(guard.transform,new Vector3(300,9.6f,305),Quaternion.Euler(0,180,0));
                    Vector3 eye=p.firstPersonCamera.transform.position,target=guard.transform.position+Vector3.up;
                    wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(300,11,295);wall.transform.localScale=new Vector3(5,4,1);Physics.SyncTransforms();
                    p.rifle.Shoot(eye,(target-eye).normalized);Check("Cover blocks damage",health.Current==health.maximumHealth);
                    Object.DestroyImmediate(wall);Physics.SyncTransforms();
                    p.rifle.Shoot(eye,(target-eye).normalized);Check("Player shots damage guard",health.Current<health.maximumHealth);
                    for(int i=0;i<4;i++)p.rifle.Shoot(eye,(target-eye).normalized);
                    Check("Guard can be defeated",health.Dead && !guard.enabled);
                    var playerHealth=p.GetComponent<CombatHealth>();playerHealth.TakeDamage(8);Check("Enemy damage affects player",playerHealth.Current==playerHealth.maximumHealth-8);
                    stage=5;until=Time.time+.3f;break;
                case 5:
                    if(Time.time<until)break;
                    Check("Exactly one active listener",System.Linq.Enumerable.Count(Object.FindObjectsByType<AudioListener>(),l=>l.isActiveAndEnabled)==1);
                    if(mouse!=null)InputSystem.RemoveDevice(mouse);
                    InputSystem.settings.backgroundBehavior=previousBackground;Application.runInBackground=previousRunInBackground;
                    stage=7;EditorApplication.isPlaying=false;break;
            }
        }
        catch(System.Exception e){File.AppendAllText(Report,"FAIL exception "+e+"\n");if(mouse!=null)InputSystem.RemoveDevice(mouse);InputSystem.settings.backgroundBehavior=previousBackground;Application.runInBackground=previousRunInBackground;stage=7;EditorApplication.isPlaying=false;}
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class CheckpointFlowValidation
{
    const string Folder="Temp/CheckpointFlow/", Marker=Folder+"testing", Report=Folder+"validation.txt";
    static int stage {get=>SessionState.GetInt("CheckpointFlowStage",0);set=>SessionState.SetInt("CheckpointFlowStage",value);}
    static Keyboard keyboard;
    static InputSettings.BackgroundBehavior background;
    static bool runInBackground;
    static int frame;
    static float deadline, elapsedBeforeDeath, originalDelay, enemyHealth;
    static Vector3 initialPosition;
    [InitializeOnLoadMethod] static void Resume(){if(File.Exists(Marker))EditorApplication.update+=Tick;}
    [MenuItem("Operation 1989/Combat/Validate Checkpoint And Skip")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        Directory.CreateDirectory(Folder);File.WriteAllText(Report,"Checkpoint, skip and HUD Play Mode validation\n");File.WriteAllText(Marker,"");
        stage=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void Check(string label,bool passed)=>File.AppendAllText(Report,(passed?"PASS ":"FAIL ")+label+"\n");
    static void Finish()
    {
        if(keyboard!=null && keyboard.added)InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior=background;Application.runInBackground=runInBackground;
        stage=6;EditorApplication.isPlaying=false;
    }
    static void Tick()
    {
        if(!File.Exists(Marker)){EditorApplication.update-=Tick;return;}
        if(!Application.isPlaying)
        {
            if(stage==6 && !EditorApplication.isPlayingOrWillChangePlaymode){File.Delete(Marker);EditorApplication.update-=Tick;}
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();
        var intro=Object.FindAnyObjectByType<IntroTruckSequence>();
        var player=Object.FindAnyObjectByType<PlayerSoldierPresentation>();
        var guard=Object.FindAnyObjectByType<BarricadePatrolSoldier>();
        if(!intro || !player || !guard)return;
        var health=player.GetComponent<CombatHealth>();
        try
        {
            switch(stage)
            {
                case 0:
                    background=InputSystem.settings.backgroundBehavior;runInBackground=Application.runInBackground;
                    InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;Application.runInBackground=true;
                    keyboard=InputSystem.AddDevice<Keyboard>("CheckpointFlowKeyboard");
                    intro.Retry();initialPosition=player.transform.position;
                    Check("Intro starts in aerial view",!intro.HandedOff && intro.introCamera.enabled && !player.movement.enabled);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));frame=Time.frameCount;stage=1;break;
                case 1:
                    if(Time.frameCount-frame<2)break;
                    Check("Space skips straight to first person",intro.HandedOff && intro.playerCamera.enabled && !intro.introCamera.enabled && player.movement.enabled);
                    Check("Skip preserves player position and entry opportunity",Vector3.Distance(player.transform.position,initialPosition)<.3f && !intro.MissedEntry);
                    Check("One active audio listener after skip",System.Linq.Enumerable.Count(Object.FindObjectsByType<AudioListener>(),l=>l.isActiveAndEnabled)==1);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                    originalDelay=health.respawnDelay;health.respawnDelay=.25f;
                    guard.GetComponent<CombatHealth>().TakeDamage(25);guard.enabled=false;enemyHealth=guard.GetComponent<CombatHealth>().Current;
                    elapsedBeforeDeath=intro.Elapsed;health.TakeDamage(1000);
                    Check("Death stops controls",health.Dead && !player.movement.enabled);
                    deadline=Time.unscaledTime+5;stage=2;break;
                case 2:
                    if(health.Dead && Time.unscaledTime<deadline)break;
                    Check("Automatic respawn restores health and control",!health.Dead && health.Current==health.maximumHealth && player.movement.enabled);
                    Vector3 offset=player.transform.position-intro.RespawnPosition;offset.y=0;
                    Check("Respawn is inside gate at checkpoint",offset.magnitude<.3f && Vector3.Dot(player.transform.position-intro.entryPlanePoint,intro.insideDirection.normalized)>intro.entryDepth);
                    Check("Respawn preserves convoy timeline and mission",intro.Elapsed>=elapsedBeforeDeath && intro.HandedOff && intro.Entered && !intro.MissedEntry && intro.playerCamera.enabled);
                    Check("Respawn preserves soldier damage",guard.GetComponent<CombatHealth>().Current==enemyHealth);
                    Check("HUD uses mission fonts",MissionHudStyle.Regular && MissionHudStyle.Semibold);
                    health.respawnDelay=30;health.TakeDamage(1000);
                    ScreenCapture.CaptureScreenshot(Folder+"death-panel.png");frame=Time.frameCount;stage=3;break;
                case 3:
                    if(Time.frameCount-frame<2)break;
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.R));frame=Time.frameCount;stage=4;break;
                case 4:
                    if(Time.frameCount-frame<2)break;
                    Check("R respawns immediately without replay",!health.Dead && intro.HandedOff && intro.Entered && player.movement.enabled && intro.Elapsed>=elapsedBeforeDeath);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());health.respawnDelay=originalDelay;
                    ScreenCapture.CaptureScreenshot(Folder+"combat-panel.png");frame=Time.frameCount;stage=5;break;
                case 5:
                    if(Time.frameCount-frame<2)break;
                    Finish();break;
            }
        }
        catch(System.Exception e){File.AppendAllText(Report,"FAIL "+e+"\n");health.respawnDelay=originalDelay;Finish();}
    }
}

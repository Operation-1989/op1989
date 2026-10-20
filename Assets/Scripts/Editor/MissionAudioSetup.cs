using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MissionAudioSetup
{
    const string Folder = "Temp/MissionAudio";
    const string Request = Folder + "/request.txt";

    [InitializeOnLoadMethod]
    static void Resume()
    {
        EditorApplication.update += CheckRequest;
        if (SessionState.GetBool("MissionAudioSmoke", false)) EditorApplication.update += SmokeTick;
    }

    static void CheckRequest()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "install") Install();
            else if (command == "validate") Validate();
            else if (command == "smoke") SmokeTest();
            else if (command == "flyby-smoke") FlybyPlaybackValidation.Run();
            else if (command == "flyby-state") FlybyPlaybackValidation.StopAndReport();
            else throw new InvalidOperationException("Unknown audio request: " + command);
        }
        catch (Exception e) { File.WriteAllText(Folder + "/error.txt", e.ToString()); Debug.LogException(e); }
    }

    static AudioClip Clip(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/" + path + ".mp3");
        if (!clip || clip.length <= 0) throw new InvalidOperationException("Missing or invalid recording: " + path);
        return clip;
    }

    [MenuItem("Operation 1989/Audio/Apply Full Flyby Playback")]
    public static void ConfigureFlybys()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>();
        if (!ambience) throw new InvalidOperationException("Mission ambience is missing.");
        Undo.RecordObject(ambience, "Configure full flyby recordings");
        ambience.flybyClips = new[] { Clip("Ambiance/PlaneFlyBy"), Clip("Ambiance/HelicopterFlyBy") };
        ambience.helicopterFlyby = ambience.flybyClips[1];
        ambience.helicopterFadeInSeconds = 2;
        ambience.flybyVolume = .55f;
        ambience.flybyInterval = new Vector2(30, 30);
        EditorUtility.SetDirty(ambience);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Operation 1989/Audio/Install Mission Ambience")]
    public static void Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        var intro = Object.FindAnyObjectByType<IntroTruckSequence>();
        var patrol = Object.FindAnyObjectByType<BaseRoadJeepPatrol>();
        var floor = GameObject.Find("w");
        var roof = GameObject.Find("HQ_Roof");
        if (!intro || !patrol || !floor || !roof) throw new InvalidOperationException("Main building, player or jeep is missing.");
        var driving = Clip("Vehicles/JeepDriving");
        var chatter = Clip("Ambiance/ControlRoomChatter");
        var plane = Clip("Ambiance/PlaneFlyBy");
        var helicopter = Clip("Ambiance/HelicopterFlyBy");
        var jeep = patrol.GetComponent<JeepDrivingAudio>();
        if (!jeep) jeep = Undo.AddComponent<JeepDrivingAudio>(patrol.gameObject);
        Undo.RecordObject(jeep, "Assign jeep driving audio");
        jeep.drivingClip = driving;
        EditorUtility.SetDirty(jeep);

        var ambience = Object.FindAnyObjectByType<MissionAmbientAudio>();
        if (!ambience)
        {
            var go = new GameObject("Mission Ambience");
            Undo.RegisterCreatedObjectUndo(go, "Install mission ambience");
            ambience = Undo.AddComponent<MissionAmbientAudio>(go);
        }
        Undo.RecordObject(ambience, "Assign mission recordings");
        ambience.player = intro.player;
        ambience.intro = intro;
        ambience.controlRoomChatter = chatter;
        ambience.flybyClips = new[] { plane, helicopter };
        ambience.helicopterFlyby = helicopter;
        if (!ambience.controlRoom)
        {
            var zone = new GameObject("Main Building Chatter Zone");
            Undo.RegisterCreatedObjectUndo(zone, "Create building audio zone");
            zone.transform.SetParent(ambience.transform, false);
            ambience.controlRoom = Undo.AddComponent<BoxCollider>(zone);
        }
        var box = ambience.controlRoom;
        Undo.RecordObject(box, "Fit main building audio zone");
        // The separate interior floor marks the occupied building, unlike the model's oversized bounds.
        Bounds bounds = floor.GetComponent<Renderer>().bounds;
        float ceiling = roof.GetComponent<Renderer>().bounds.min.y;
        box.transform.position = new Vector3(bounds.center.x, (bounds.min.y + ceiling) * .5f, bounds.center.z);
        box.center = Vector3.zero;
        box.size = new Vector3(bounds.size.x - .6f, ceiling - bounds.min.y, bounds.size.z - .6f);
        box.isTrigger = true;
        EditorUtility.SetDirty(box);
        EditorUtility.SetDirty(ambience);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Validate();
    }

    [MenuItem("Operation 1989/Audio/Validate Mission Ambience")]
    public static void Validate()
    {
        Directory.CreateDirectory(Folder);
        var audio = Object.FindAnyObjectByType<MissionAmbientAudio>();
        var jeep = Object.FindAnyObjectByType<JeepDrivingAudio>();
        var report = new StringBuilder();
        Action<string, bool> check = (name, pass) => report.AppendLine((pass ? "PASS " : "FAIL ") + name);
        check("Jeep recording assigned", jeep && jeep.drivingClip && jeep.drivingClip.length > 0);
        check("Main building zone and player assigned", audio && audio.player && audio.controlRoom && audio.intro);
        if (audio && audio.controlRoom)
        {
            var center = audio.controlRoom.transform.TransformPoint(audio.controlRoom.center);
            check("Interior center detected", audio.InsideControlRoom(center));
            check("Computer/server area detected", audio.InsideControlRoom(new Vector3(-9.8f, 1, 23.9f)));
            check("Entrance approach stays outside", !audio.InsideControlRoom(new Vector3(0, 1, 10)));
            check("Above roof stays outside", !audio.InsideControlRoom(new Vector3(center.x, 20, center.z)));
            check("Chatter recording imports", audio.controlRoomChatter && audio.controlRoomChatter.length > 0);
            check("Flyby recordings import", audio.flybyClips != null && audio.flybyClips.Length > 0 && Array.TrueForAll(audio.flybyClips, c => c && c.length > 0));
            report.AppendLine("ZONE " + audio.controlRoom.bounds);
            report.AppendLine("CHATTER seconds=" + audio.controlRoomChatter.length);
            foreach (var clip in audio.flybyClips) report.AppendLine("FLYBY " + clip.name + " seconds=" + clip.length);
        }
        File.WriteAllText(Folder + "/validation.txt", report.ToString());
        Debug.Log(report.ToString());
    }

    [MenuItem("Operation 1989/Audio/Smoke Test In Play Mode")]
    public static void SmokeTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start audio smoke test from Edit mode.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/playmode.txt", "Mission audio Play Mode checks\n");
        SessionState.SetBool("MissionAudioSmoke", true);
        SessionState.SetInt("MissionAudioSmokeStage", 0);
        EditorApplication.update -= SmokeTick;
        EditorApplication.update += SmokeTick;
        EditorApplication.isPlaying = true;
    }

    static double smokeUntil;
    static void SmokeTick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.QueuePlayerLoopUpdate();
        var audio = Object.FindAnyObjectByType<MissionAmbientAudio>();
        if (!audio) return;
        int stage = SessionState.GetInt("MissionAudioSmokeStage", 0);
        if (stage > 0 && Time.time < smokeUntil) return;
        Action<string, bool> check = (name, pass) => File.AppendAllText(Folder + "/playmode.txt", (pass ? "PASS " : "FAIL ") + name + "\n");
        Action<Vector3> move = position => {
            var cc = audio.player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            audio.player.position = position;
            if (cc) cc.enabled = true;
            Physics.SyncTransforms();
        };
        try
        {
            if (stage == 0)
            {
                SessionState.SetBool("MissionAudioPreviousBackground", Application.runInBackground);
                Application.runInBackground = true;
                if (audio.intro) audio.intro.SkipIntro();
                move(new Vector3(-9.8f, 1, 23.9f));
                smokeUntil = Time.time + 3;
            }
            else if (stage == 1)
            {
                var chatter = audio.transform.Find("Control room chatter").GetComponent<AudioSource>();
                check("Chatter plays inside main building", chatter.isPlaying && chatter.volume > .1f);
                File.AppendAllText(Folder + "/playmode.txt", "Indoor position=" + audio.player.position + " handoff=" + audio.intro.HandedOff + " volume=" + chatter.volume + " time=" + Time.time + "\n");
                move(new Vector3(-30, 1, -40));
                audio.flybyInterval = new Vector2(10, 10);
                audio.SendMessage("ScheduleFlyby");
                smokeUntil = Time.time + 13;
            }
            else if (stage == 2)
            {
                var chatter = audio.transform.Find("Control room chatter").GetComponent<AudioSource>();
                var aircraft = audio.transform.Find("Distant aircraft pass").GetComponent<AudioSource>();
                check("Chatter stops outdoors", !chatter.isPlaying);
                check("Outdoor recording plays at configured volume", aircraft.isPlaying && !aircraft.mute && Mathf.Approximately(aircraft.volume, audio.flybyVolume));
                File.AppendAllText(Folder + "/playmode.txt", "Outdoor position=" + audio.player.position + " outside=" + audio.IsOutside(audio.player.position) + " volume=" + aircraft.volume + " time=" + Time.time + "\n");
                move(new Vector3(-9.8f, 1, 23.9f));
                smokeUntil = Time.time + 3;
            }
            else
            {
                var aircraft = audio.transform.Find("Distant aircraft pass").GetComponent<AudioSource>();
                check("Aircraft is muted on building entry", aircraft.mute);
                var jeep = Object.FindAnyObjectByType<JeepDrivingAudio>();
                var source = jeep.GetComponent<AudioSource>();
                check("Jeep creates a spatial looping driving source", source && source.clip == jeep.drivingClip && source.loop && source.spatialBlend == 1);
                SessionState.SetBool("MissionAudioSmoke", false);
                Application.runInBackground = SessionState.GetBool("MissionAudioPreviousBackground", false);
                EditorApplication.update -= SmokeTick;
                EditorApplication.isPlaying = false;
                return;
            }
            SessionState.SetInt("MissionAudioSmokeStage", stage + 1);
        }
        catch (Exception e)
        {
            check("Exception: " + e, false);
            SessionState.SetBool("MissionAudioSmoke", false);
            Application.runInBackground = SessionState.GetBool("MissionAudioPreviousBackground", false);
            EditorApplication.update -= SmokeTick;
            EditorApplication.isPlaying = false;
        }
    }
}

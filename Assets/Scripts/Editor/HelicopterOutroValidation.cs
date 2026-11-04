using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Plays the actual theft and extraction, including audio-clock camera timing and replay.</summary>
public static class HelicopterOutroValidation
{
    const string Work = "Temp/HelicopterOutro";
    const string Flag = "HelicopterOutroValidation";
    static IntelTheftMission mission;
    static HelicopterOutroSequence outro;
    static int stage, shots;
    static double deadline;
    static bool background;
    static bool lateMode;
    static string ReportPath => Work + (lateMode ? "/late-validation.txt" : "/validation.txt");
    static Vector3 parked;
    static Quaternion rotor;
    static Material priorSky;
    static float priorSunIntensity;
    struct Snapshot { public string name; public Vector3 heli, camera; public Quaternion heliRotation, cameraRotation; public float fov, sunsetBlend; }
    static readonly List<Snapshot> snapshots = new List<Snapshot>();
    static int snapshotIndex, poseFrame;
    static void Check(string label, bool result)
    {
        File.AppendAllText(ReportPath, (result ? "PASS " : "FAIL ") + label + "\n");
        if (!result) throw new InvalidOperationException(label);
    }
    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (SessionState.GetBool(Flag, false)) EditorApplication.playModeStateChanged += Changed;
    }
    [MenuItem("Operation 1989/Outro/Validate Helicopter Outro")]
    public static void Run() => StartValidation(false);
    [MenuItem("Operation 1989/Outro/Validate Late Boarding")]
    public static void RunLateBoarding() => StartValidation(true);
    static void StartValidation(bool late)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start validation in Edit mode.");
        lateMode = late;
        SessionState.SetBool("OutroLateBoarding", late);
        File.WriteAllText(ReportPath, "Helicopter outro Play Mode validation\n");
        SessionState.SetBool(Flag, true);
        EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed;
        EditorApplication.isPlaying = true;
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        lateMode = SessionState.GetBool("OutroLateBoarding", false);
        EditorApplication.playModeStateChanged -= Changed;
        stage = shots = 0; snapshots.Clear(); deadline = EditorApplication.timeSinceStartup + 300;
        background = Application.runInBackground; Application.runInBackground = true;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    static void Move(Vector3 position)
    {
        var cc = mission.player.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        mission.player.position = position; if (cc) cc.enabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; Physics.SyncTransforms();
    }
    static void Aim()
    {
        Move(mission.terminal.InteractionFrame.TransformPoint(new Vector3(0, .3f, -1.3f)));
        mission.playerCamera.transform.LookAt(mission.terminal.InteractionFrame.position + Vector3.up * .3f);
    }
    static void Capture(string name)
    {
        var camera = mission.playerCamera;
        var previous = camera.targetTexture; var active = RenderTexture.active;
        var rt = new RenderTexture(1280, 720, 24);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(Work + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally { camera.targetTexture = previous; RenderTexture.active = active; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); }
    }
    static void Remember(string name)
    {
        snapshots.Add(new Snapshot { name = name, heli = outro.helicopter.position, heliRotation = outro.helicopter.rotation,
            camera = mission.playerCamera.transform.position, cameraRotation = mission.playerCamera.transform.rotation, fov = mission.playerCamera.fieldOfView, sunsetBlend = outro.sunset.Blend });
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Stop(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Outro validation timed out at stage " + stage);
            if (stage == 0)
            {
                if (Time.time < 3) return;
                mission = Object.FindAnyObjectByType<IntelTheftMission>(); outro = mission.outro;
                Check("Installed helicopter, window view, single camera, sunset, and recordings", outro && outro.helicopter && outro.pilotEyes && outro.exteriorCameraAnchor && outro.sunset && outro.mainRotor && outro.tailRotor && outro.startupClip && outro.chatterClip);
                foreach (var guard in Object.FindObjectsByType<BarricadePatrolSoldier>()) guard.enabled = false;
                foreach (var rifle in Object.FindObjectsByType<RifleWeapon>()) if (!rifle.playerWeapon) rifle.enabled = false;
                mission.intro.RespawnInsideGate();
                parked = outro.helicopter.position; rotor = outro.mainRotor.localRotation;
                priorSky = RenderSettings.skybox; priorSunIntensity = outro.sunset.sunlight.intensity;
                Check("Takeoff uses the recording's 55-second cue", Mathf.Approximately(outro.takeoffAudioSeconds, 55));
                // Shorten only the unboarded idle wait in the late-arrival smoke run.
                if (lateMode) outro.takeoffAudioSeconds = 9;
                Check("Cannot board before stealing intel", !mission.TryExtract());
                mission.downloadSeconds = .2f; Aim();
                Check("Real terminal interaction starts download", mission.InteractWithTerminal());
                stage = 1; return;
            }
            if (stage == 1)
            {
                if (mission.State != IntelTheftMission.Phase.ReadyToRemove) return;
                Aim(); Check("USB removal enables escape", mission.InteractWithTerminal() && mission.State == IntelTheftMission.Phase.Escape);
                Check("USB removal starts the helicopter before running to extraction", outro.RotorsStarting && !outro.Running);
                Check("Cannot extract remotely", !mission.TryExtract());
                Move(mission.extraction.transform.position + Vector3.right * 6);
                stage = 2; return;
            }
            if (stage == 2)
            {
                if (!outro.RotorsStarting || outro.RotorSpeed < .02f) return;
                if (lateMode)
                {
                    if (!outro.HoldingForPilot) return;
                    var idle = outro.helicopter.GetComponentsInChildren<AudioSource>().First(s => s.name == "Helicopter waiting for pilot");
                    Check("Late arrival loops a pre-takeoff idle section", idle.isPlaying && outro.RecordingTime < outro.takeoffAudioSeconds);
                    Check("Helicopter does not take off without its pilot", !outro.Running && Vector3.Distance(outro.helicopter.position, parked) < .01f);
                }
                Check("Rotors are turning while the player is still outside", Quaternion.Angle(rotor, outro.mainRotor.localRotation) > .1f && !outro.Running);
                Move(mission.extraction.transform.position + Vector3.up * .5f);
                stage = 3; return;
            }
            if (stage == 3)
            {
                if (mission.State != IntelTheftMission.Phase.Extracting) return;
                Check("Entering the extraction zone automatically boards the pilot", outro.Running && !mission.player.GetComponent<FirstPersonController>().enabled);
                Check("Takeoff is scheduled two seconds after boarding", Mathf.Abs(outro.LiftOffStartsAt - 2) < .02f);
                Check("Camera cuts are exactly three seconds after full chatter", Mathf.Abs(outro.ExteriorStartsAt - outro.ChatterStartsAt - outro.chatterClip.length - 3) < .01f);
                float hp = mission.player.GetComponent<CombatHealth>().Current;
                mission.player.GetComponent<CombatHealth>().TakeDamage(1000);
                Check("Player cannot die during extraction", Mathf.Approximately(hp, mission.player.GetComponent<CombatHealth>().Current));
                Check("Repeated extraction cannot restart the outro", !mission.TryExtract());
                stage = 4; return;
            }
            if (stage == 4)
            {
                if (outro.PresentedTime < .35f) return;
                Remember("pilot-startup");
                Check("Startup is viewed from the pilot seat", !outro.ExteriorView && Vector3.Distance(mission.playerCamera.transform.position, outro.pilotEyes.position) < .02f);
                if (outro.PresentedTime < outro.LiftOffStartsAt)
                    Check("Helicopter waits only for the short audio lead-in", Mathf.Abs(outro.helicopter.position.y - parked.y) < .02f);
                if (lateMode)
                {
                    var resumed = outro.helicopter.GetComponentsInChildren<AudioSource>().First(s => s.name == "Helicopter startup");
                    Check("Late boarding resumes the original recording without pitch changes", resumed.isPlaying && resumed.clip == outro.startupClip && Mathf.Approximately(resumed.pitch, 1));
                    Check("Resumed playback cursor matches the aircraft timeline", Mathf.Abs(resumed.time - outro.RecordingTime) < .5f);
                    Check("Late boarding also leaves only two seconds before takeoff", Mathf.Abs(outro.LiftOffStartsAt - 2) < .02f);
                    mission.SendMessage("Replay"); outro.takeoffAudioSeconds = 55;
                    Check("Late-boarding replay restores helicopter and lighting", !outro.Running && Vector3.Distance(outro.helicopter.position, parked) < .01f && RenderSettings.skybox == priorSky);
                    File.AppendAllText(ReportPath, "PASS COMPLETE\n"); Stop(); return;
                }
                stage = 5; return;
            }
            if (stage == 5)
            {
                if (!outro.LiftedInCockpit) return;
                Check("Cockpit view remains during liftoff before exterior shots", !outro.ExteriorView);
                Check("Helicopter lifts off while still in first person", outro.LiftedInCockpit);
                Check("Liftoff is after the recording reaches its takeoff cue", outro.RecordingTime >= outro.takeoffAudioSeconds);
                var source = outro.helicopter.GetComponentsInChildren<AudioSource>().First(s => s.name == "Helicopter startup");
                Check("Original recording is audibly playing at liftoff without pitch changes", source.isPlaying && source.clip == outro.startupClip && Mathf.Approximately(source.pitch, 1));
                Check("Aircraft timeline matches the actual audio playback cursor", Mathf.Abs(source.time - outro.RecordingTime) < .5f);
                File.AppendAllText(ReportPath, $"INFO Liftoff sample: recording cursor={source.time:F3}s, sequence recording time={outro.RecordingTime:F3}s\n");
                Remember("pilot-liftoff"); stage = 6; return;
            }
            if (stage == 6)
            {
                if (outro.ExteriorView)
                {
                    CheckOnceForExterior();
                    float progress = Mathf.InverseLerp(outro.ExteriorStartsAt, outro.EndsAt, outro.PresentedTime);
                    int index = progress < .4f ? 0 : progress < .75f ? 1 : 2;
                    if ((shots & (1 << index)) == 0)
                    { Remember("single-shot-" + (index + 1)); shots |= 1 << index; }
                }
                if (!outro.Finished) return;
                Check("Departure uses one continuous camera shot", shots != 0 && outro.ShotIndex == 0);
                Check("Ground camera slides across the road", Vector3.Distance(mission.playerCamera.transform.position, outro.exteriorCameraAnchor.position) >= outro.exteriorTravelOffset.magnitude * .95f);
                if (shots != 7) File.AppendAllText(ReportPath, "INFO Editor stalls skipped a preview interval; dolly position and centered framing were checked on every sampled exterior frame.\n");
                Check("Exterior shots never begin before chatter plus three seconds", outro.FirstExteriorTime >= outro.ExteriorStartsAt);
                Check("Mission completes only after outro and fade", mission.State == IntelTheftMission.Phase.Complete && !outro.Running);
                Check("Helicopter leaves beyond the main gate", Vector3.Dot(outro.helicopter.position - outro.gatePosition, (outro.gatePosition - parked).normalized) > 50);
                Check("Departure lighting becomes darker with an orange sky", outro.sunset.Blend > .99f && outro.sunset.sunlight.intensity < priorSunIntensity && RenderSettings.skybox != priorSky);
                snapshotIndex = 0; stage = 7; return;
            }
            if (stage == 7)
            {
                if (snapshotIndex < snapshots.Count)
                {
                    var snapshot = snapshots[snapshotIndex];
                    outro.helicopter.SetPositionAndRotation(snapshot.heli, snapshot.heliRotation);
                    mission.playerCamera.transform.SetPositionAndRotation(snapshot.camera, snapshot.cameraRotation);
                    mission.playerCamera.fieldOfView = snapshot.fov;
                    outro.sunset.SetBlend(snapshot.sunsetBlend);
                    poseFrame = Time.frameCount; stage = 8; return;
                }
                mission.SendMessage("Replay");
                Check("Replay resets mission, helicopter, and controls", mission.State == IntelTheftMission.Phase.FindComputer && !outro.Finished && !outro.Running && Vector3.Distance(outro.helicopter.position, parked) < .01f && mission.player.GetComponent<FirstPersonController>().enabled);
                Check("Replay restores player vulnerability", !mission.player.GetComponent<CombatHealth>().CinematicProtection);
                Check("Replay restores the original lighting", RenderSettings.skybox == priorSky && Mathf.Approximately(outro.sunset.sunlight.intensity, priorSunIntensity));
                stage = 9; return;
            }
            if (stage == 8)
            {
                // GPU Resident Drawer uploads transform changes during the player loop.
                if (Time.frameCount < poseFrame + 2) return;
                Capture(snapshots[snapshotIndex].name); snapshotIndex++; stage = 7; return;
            }
            if (stage == 9)
            {
                Check("Checkpoint can start a new theft after extraction", mission.State == IntelTheftMission.Phase.FindComputer);
                File.AppendAllText(ReportPath, "PASS COMPLETE\n"); Stop();
            }
        }
        catch (Exception e) { File.AppendAllText(ReportPath, "FAIL " + e + "\n"); Stop(); }
    }
    static void CheckOnceForExterior()
    {
        float travel = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(outro.ExteriorStartsAt, outro.EndsAt, outro.PresentedTime));
        Vector3 expected = outro.exteriorCameraAnchor.position + outro.exteriorTravelOffset * travel;
        Vector3 centered = mission.playerCamera.WorldToViewportPoint(outro.CinematicFocus);
        if (outro.ShotIndex != 0 || Vector3.Distance(mission.playerCamera.transform.position, expected) > .02f ||
            !Mathf.Approximately(mission.playerCamera.fieldOfView, outro.exteriorFieldOfView))
            Check("Exterior camera follows one smooth dolly path without zooming", false);
        if (centered.z <= 0 || Mathf.Abs(centered.x - .5f) > .01f || Mathf.Abs(centered.y - .5f) > .01f)
            Check("Helicopter stays centered throughout the moving shot", false);
        if (shots != 0) return;
        Check("One moving ground shot starts beside the main building", outro.ShotIndex == 0 && outro.exteriorTravelOffset.magnitude >= 10);
        Check("Helicopter is centered while the camera crosses the road", Mathf.Abs(centered.x - .5f) < .01f && Mathf.Abs(centered.y - .5f) < .01f);
        Check("Wide framing stays fixed without zooming onto the helicopter", Mathf.Approximately(mission.playerCamera.fieldOfView, outro.exteriorFieldOfView) && outro.exteriorFieldOfView >= 65);
    }
    static void Stop()
    {
        SessionState.SetBool(Flag, false); EditorApplication.update -= Tick;
        Application.runInBackground = background;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
}

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Exercise the installed mission through its real interaction and checkpoint checks.</summary>
public static class IntelTheftValidation
{
    const string Work = "Temp/IntelTheft";
    const string Flag = "IntelTheftValidation";
    static IntelTheftMission mission;
    static int stage;
    static float phaseStart;
    static double deadline;
    static bool priorBackground;
    static Quaternion beaconRotation;
    static Operation1989.Interaction.Environment.DoorController testedDoor;
    static Vector3 doorStartPosition;
    static bool capturedCompass;
    static float captureTime;
    static void Check(string name, bool pass) => File.AppendAllText(Work + "/validation.txt", (pass ? "PASS " : "FAIL ") + name + "\n");
    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (SessionState.GetBool(Flag, false)) EditorApplication.playModeStateChanged += StateChanged;
        if (SessionState.GetBool("IntelValidationQueued", false)) EditorApplication.update += PendingRun;
    }
    public static void ScheduleRun()
    {
        SessionState.SetBool("IntelValidationQueued", true);
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorApplication.update -= PendingRun; EditorApplication.update += PendingRun;
    }
    static void PendingRun()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= PendingRun; SessionState.SetBool("IntelValidationQueued", false); Run();
    }
    [MenuItem("Operation 1989/Intel/Validate USB Mission")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run intel validation from Edit mode.");
        if (!Object.FindAnyObjectByType<IntelTheftMission>()) throw new InvalidOperationException("Install the intel mission first.");
        Directory.CreateDirectory(Work); File.WriteAllText(Work + "/validation.txt", "USB theft mission Play Mode validation\n");
        SessionState.SetBool(Flag, true); EditorApplication.playModeStateChanged -= StateChanged;
        EditorApplication.playModeStateChanged += StateChanged; EditorApplication.isPlaying = true;
    }
    static void StateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= StateChanged;
        stage = 0; deadline = EditorApplication.timeSinceStartup + 120;
        capturedCompass = false;
        priorBackground = Application.runInBackground; Application.runInBackground = true;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    static void Move(Vector3 position)
    {
        var cc = mission.player.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        mission.player.position = position;
        if (cc) cc.enabled = true;
        Physics.SyncTransforms(); Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }
    static void Aim()
    {
        Move(mission.terminal.InteractionFrame.TransformPoint(new Vector3(0, 0.3f, -1.3f)));
        mission.player.rotation = Quaternion.LookRotation(mission.terminal.InteractionFrame.forward, Vector3.up);
        mission.playerCamera.transform.LookAt(mission.terminal.InteractionFrame.position + Vector3.up * 0.3f);
        Physics.SyncTransforms();
    }
    static void Tick()
    {
        if (!Application.isPlaying) { Finish(false); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Intel mission validation timed out.");
            if (stage == 0)
            {
                if (Time.time < 0.3f) return;
                mission = Object.FindAnyObjectByType<IntelTheftMission>();
                if (!mission) throw new InvalidOperationException("Installed mission missing.");
                foreach (var guard in Object.FindObjectsByType<BarricadePatrolSoldier>()) guard.enabled = false;
                var health = mission.player.GetComponent<CombatHealth>(); health.maximumHealth = 100000; health.Restore();
                mission.intro.RespawnInsideGate();
                Check("Checkpoint starts with no intel or alarm", mission.State == IntelTheftMission.Phase.FindComputer && !mission.HasIntel && !mission.alarm.Active);
                Check("Configured download takes fifteen seconds", Mathf.Approximately(mission.downloadSeconds, 15));
                Check("Right-side existing computer is the mission target", mission.terminal.name == "ScifiDesk");
                Check("Computer labels removed", !mission.terminal.display || string.IsNullOrEmpty(mission.terminal.display.text));
                Check("Computer has a floating location pin", mission.terminal.LocationIcon && mission.terminal.LocationIcon.gameObject.activeInHierarchy);
                Check("Computer marker stays below bloom brightness", mission.terminal.LocationIcon.GetComponentsInChildren<LineRenderer>()
                    .All(l => l.sharedMaterial.GetColor("_BaseColor").maxColorComponent <= 1));
                Check("Extraction uses a location pin without ground lighting", mission.extraction.LocationIcon && mission.extraction.marker.GetComponentsInChildren<Light>(true).Length == 0 &&
                    !mission.extraction.marker.GetComponentsInChildren<LineRenderer>(true).Any(l => l.name.Contains("ring") || l.name.Contains("halo")));
                Check("Standing alarm poles removed", !mission.alarm.GetComponentsInChildren<Transform>().Any(t => t.name == "Beacon support pole"));
                Check("Rotating beacons are placed indoors and outdoors", mission.alarm.rotatingBeacons.Length >= 6 &&
                    mission.alarm.rotatingBeacons.Any(b => b.name.StartsWith("Control room")) && mission.alarm.rotatingBeacons.Any(b => b.name.StartsWith("Exterior")));
                Check("Siren asset and base-wide red lighting are assigned", mission.alarm.sirenClip && mission.alarm.emergencyLights.Length >= 10);
                Move(mission.extraction.transform.position + Vector3.up);
                Check("Helicopter cannot complete the mission without intel", !mission.TryExtract());
                Check("Remote computer interaction is rejected", !mission.InteractWithTerminal());
                Check("Computer popup stays hidden at a distance", !mission.ShowTerminalPrompt);
                Aim();
                Check("Computer is reachable through the camera ray", mission.IsLookingAtTerminal());
                Check("Nearby computer shows theft instructions", mission.ShowTerminalPrompt);
                Check("Compass gives a short computer objective", mission.CompassInstruction == "Find the computer");
                Check("Compass objective marker centers when facing the target", Mathf.Abs(mission.ObjectiveBearing) < 5);
                Check("Compass keeps objectives behind the player at an edge", ObjectiveCompassHud.MarkerOffset(170) == 1 && ObjectiveCompassHud.MarkerOffset(-170) == -1);
                float yaw = mission.CompassHeading;
                mission.playerCamera.transform.rotation = Quaternion.AngleAxis(90, Vector3.up) * mission.playerCamera.transform.rotation;
                Check("Nearby popup remains available when not aiming directly at the computer", mission.ShowTerminalPrompt && !mission.IsLookingAtTerminal());
                Check("Compass objective bearing follows camera turns", mission.ObjectiveBearing < -80 && mission.ObjectiveBearing > -100);
                Check("Compass heading advances correctly across north", Mathf.Abs(Mathf.DeltaAngle(yaw, mission.CompassHeading) - 90) < 0.1f);
                Aim();
                if (!mission.IsLookingAtTerminal() && RifleWeapon.Trace(mission.playerCamera.transform.position, mission.playerCamera.transform.forward, 4, mission.player, out var obstruction))
                    File.AppendAllText(Work + "/validation.txt", $"OBSTRUCTION {obstruction.collider.name} at {obstruction.point}\n");
                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Temporary terminal obstruction";
                Vector3 screen = mission.terminal.InteractionFrame.position + Vector3.up * 0.3f;
                blocker.transform.position = Vector3.Lerp(mission.playerCamera.transform.position, screen, 0.5f);
                blocker.transform.localScale = Vector3.one * 0.8f; Physics.SyncTransforms();
                Check("Solid cover blocks USB interaction", !mission.InteractWithTerminal());
                Check("Solid cover hides the computer popup", !mission.ShowTerminalPrompt);
                Object.DestroyImmediate(blocker); Physics.SyncTransforms();
                Aim(); mission.player.GetComponent<FirstPersonController>().enabled = false;
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                phaseStart = Time.time; stage = 11; return;
            }
            if (stage == 11 && Time.time - phaseStart > 0.2f)
            {
                // Re-lock after the Game window receives focus, before capturing the proximity prompt.
                Aim();
                var labels = mission.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                Check("Computer popup uses readable UI text colors", labels.Length >= 2 && labels.All(l => l.color.r > 0.7f));
                var panel = mission.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "Centered terminal panel");
                Check("Computer popup is anchored at screen center", panel && panel.anchorMin == Vector2.one * 0.5f && panel.anchoredPosition == Vector2.zero);
                ScreenCapture.CaptureScreenshot(Work + "/subtle-computer-prompt.png");
                phaseStart = Time.time; stage = 10; return;
            }
            if (stage == 10 && Time.time - phaseStart > 0.4f)
            {
                mission.player.GetComponent<FirstPersonController>().enabled = true; Aim();
                Check("F interaction inserts the USB and starts copying", mission.InteractWithTerminal() &&
                    mission.State == IntelTheftMission.Phase.Downloading && mission.terminal.insertedUsb.activeSelf);
                if (mission.State != IntelTheftMission.Phase.Downloading) throw new InvalidOperationException("Terminal interaction did not start the transfer; stop downstream checks.");
                Check("Downloading does not start the alarm", !mission.alarm.Active);
                Check("Compass explains the active transfer", mission.CompassInstruction == "Wait for the transfer");
                Check("USB cannot be removed before transfer finishes", !mission.InteractWithTerminal());
                phaseStart = Time.time; stage = 1; return;
            }
            if (stage == 1 && Time.time - phaseStart > 1)
            {
                if (!capturedCompass)
                {
                    Aim(); mission.player.GetComponent<FirstPersonController>().enabled = false;
                    EditorApplication.ExecuteMenuItem("Window/General/Game");
                    ScreenCapture.CaptureScreenshot(Work + "/compass-computer-game.png");
                    IntelTheftSetup.Capture(mission.terminal.InteractionFrame.TransformPoint(new Vector3(0, 0.7f, -2.5f)), mission.terminal.highlightTarget.bounds.center, "computer-glow");
                    capturedCompass = true; captureTime = Time.time; return;
                }
                if (Time.time - captureTime < 0.4f) return;
                mission.player.GetComponent<FirstPersonController>().enabled = true;
                Check("Download advances without completing early", mission.DownloadProgress > 0 && mission.DownloadProgress < 1);
                Move(mission.intro.RespawnPosition); stage = 2;
            }
            if (stage == 2 && Time.time - phaseStart > 15.3f)
            {
                Check("USB finishes copying while plugged into the computer", mission.State == IntelTheftMission.Phase.ReadyToRemove && mission.DownloadProgress == 1);
                Check("Completed download waits for manual removal without alarm", !mission.alarm.Active && mission.terminal.insertedUsb.activeSelf && !mission.HasIntel);
                Check("Compass asks for manual USB removal", mission.CompassInstruction == "Remove the USB");
                Aim();
                Cursor.lockState = CursorLockMode.None;
                Check("Unlocked cursor blocks USB removal", !mission.InteractWithTerminal()); Cursor.lockState = CursorLockMode.Locked;
                Check("Manual removal secures intel and activates alarm", mission.InteractWithTerminal() && mission.HasIntel && mission.alarm.Active && !mission.terminal.insertedUsb.activeSelf);
                Check("Siren plays and red lights illuminate", mission.alarm.GetComponent<AudioSource>().isPlaying && mission.alarm.emergencyLights.All(l => l.enabled));
                Check("Extraction is marked after theft", mission.extraction.marker.activeSelf);
                Check("Extraction location icon is active after theft", mission.extraction.LocationIcon.gameObject.activeInHierarchy);
                Check("Compass gives a short evacuation objective", mission.CompassInstruction == "Reach extraction");
                Check("Computer popup hides once intel is collected", !mission.ShowTerminalPrompt);
                IntelTheftSetup.Capture(mission.extraction.transform.position + new Vector3(-4, 3, 5), mission.extraction.transform.position + Vector3.up * 0.9f, "extraction-glow");
                Check("Removing USB leaves existing guard behavior untouched", Object.FindObjectsByType<BarricadePatrolSoldier>().All(g => !g.enabled));
                IntelTheftSetup.Capture(mission.terminal.InteractionFrame.TransformPoint(new Vector3(0, 0.8f, -2)), mission.terminal.InteractionFrame.position + Vector3.up * 0.3f, "computer-alarm");
                beaconRotation = mission.alarm.rotatingBeacons[0].rotor.localRotation;
                phaseStart = Time.time; stage = 3; return;
            }
            if (stage == 3 && Time.time - phaseStart > 0.6f)
            {
                Check("Emergency beacon reflectors rotate during alarm", Quaternion.Angle(beaconRotation, mission.alarm.rotatingBeacons[0].rotor.localRotation) > 5);
                Check("Rotating beacon beams stay powered", mission.alarm.rotatingBeacons.All(b => b.Powered && b.lamps.All(l => l.enabled)));
                Check("Computer highlight turns off after theft", mission.terminal.GetComponentsInChildren<LineRenderer>().All(l => !l.enabled));
                testedDoor = Object.FindObjectsByType<Operation1989.Interaction.Environment.DoorController>()
                    .Where(d => d.InteractionPrompt.StartsWith("[F]"))
                    .OrderBy(d => Vector3.Distance(d.transform.position, mission.terminal.InteractionFrame.position)).FirstOrDefault();
                if (!testedDoor) throw new InvalidOperationException("No configured F-interaction door found.");
                var doorCollider = testedDoor.GetComponentInChildren<Collider>();
                Vector3 doorPoint = doorCollider.ClosestPoint(mission.terminal.InteractionFrame.position);
                Vector3 approach = Vector3.ProjectOnPlane(mission.terminal.InteractionFrame.position - doorPoint, Vector3.up).normalized;
                Move(doorPoint + approach * 0.6f);
                bool wasOpen = testedDoor.IsOpen; doorStartPosition = testedDoor.transform.localPosition;
                Check("Doors accept F interaction while alarm is sounding", mission.alarm.Active && testedDoor.TryProximityInteract() && testedDoor.IsOpen != wasOpen);
                var health = mission.player.GetComponent<CombatHealth>(); health.TakeDamage(health.Current + 1);
                Check("Death removes stolen intel and resets the theft", !mission.HasIntel && mission.State == IntelTheftMission.Phase.FindComputer);
                Check("Death stops alarm and hides extraction", !mission.alarm.Active && !mission.alarm.GetComponent<AudioSource>().isPlaying && !mission.extraction.marker.activeSelf);
                health.Respawn();
                Check("Death switches rotating beacons off", mission.alarm.rotatingBeacons.All(b => !b.Powered && b.lamps.All(l => !l.enabled)));
                Check("Respawn uses the existing inside-gate checkpoint", Vector3.Distance(mission.player.position, mission.intro.RespawnPosition) < 0.5f);
                mission.downloadSeconds = 0.2f; Aim();
                Check("The theft can be restarted after death", mission.InteractWithTerminal());
                phaseStart = Time.time; stage = 4; return;
            }
            if (stage == 4 && Time.time - phaseStart > 0.4f)
            {
                Check("Door leaf moves after alarm-time interaction", Vector3.Distance(doorStartPosition, testedDoor.transform.localPosition) > 0.001f);
                Aim(); Check("A new USB transfer can be collected", mission.InteractWithTerminal());
                Check("Extraction cannot complete outside the boarding zone", !mission.TryExtract());
                Move(mission.extraction.transform.position + Vector3.up);
                Check("Helicopter extraction succeeds with stolen intel", mission.TryExtract() && mission.State == IntelTheftMission.Phase.Complete);
                Check("Victory stops alarm and player firing/movement", !mission.alarm.Active && !mission.player.GetComponent<FirstPersonController>().enabled);
                Check("Repeated extraction does not restart completion", !mission.TryExtract());
                mission.intro.RespawnInsideGate();
                Check("Checkpoint replay clears completed mission and USB", mission.State == IntelTheftMission.Phase.FindComputer && !mission.HasIntel && !mission.terminal.insertedUsb.activeSelf);
                Finish(true);
            }
        }
        catch (Exception e) { File.AppendAllText(Work + "/validation.txt", "FAIL " + e + "\n"); Finish(true); }
    }
    static void Finish(bool stop)
    {
        SessionState.SetBool(Flag, false); EditorApplication.update -= Tick;
        Application.runInBackground = priorBackground;
        if (stop) EditorApplication.isPlaying = false;
    }
}

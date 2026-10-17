using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;

/// <summary>Explicit Play Mode smoke test; survives domain reload without an MCP connection.</summary>
public static class IntroTruckValidation
{
    const string Marker = "Temp/IntroTruck/validation-active";
    const string Report = "Temp/IntroTruck/validation.txt";
    static Keyboard keyboard;
    static int waypoint;
    static readonly Vector3[] Walk = { new Vector3(14,0,-103), new Vector3(9,0,-103), new Vector3(6,0,-101), new Vector3(3.78f,0,-97), new Vector3(3.78f,0,-86), new Vector3(3.78f,0,-82) };

    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (File.Exists(Marker)) EditorApplication.update += Tick;
    }

    [MenuItem("Operation 1989/Intro/Validate Sequence")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new System.InvalidOperationException("Save the intro scene and run from Edit Mode.");
        Directory.CreateDirectory("Temp/IntroTruck");
        File.WriteAllText(Report, "Intro truck Play Mode validation\n");
        File.WriteAllText(Marker, "0");
        SessionState.SetInt("IntroValidationStage", 0);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static void Check(string name, bool pass, string details = "") => File.AppendAllText(Report, (pass ? "PASS " : "FAIL ") + name + " " + details + "\n");
    static void Stage(int value) => SessionState.SetInt("IntroValidationStage", value);
    static void Snapshot(string name)
    {
        IntroTruckSetup.ReportRuntime();
        File.Copy("Temp/IntroTruck/runtime.txt", "Temp/IntroTruck/" + name + ".txt", true);
        IntroTruckSetup.CaptureActiveCamera();
        File.Copy("Temp/IntroTruck/active-camera.png", "Temp/IntroTruck/" + name + ".png", true);
    }
    static void PlacePlayer(IntroTruckSequence sequence, Vector3 position)
    {
        var controller = sequence.player.GetComponent<CharacterController>();
        controller.enabled = false;
        sequence.player.position = position;
        controller.enabled = true;
        Physics.SyncTransforms();
    }
    static void Tick()
    {
        if (!File.Exists(Marker)) { EditorApplication.update -= Tick; return; }
        int stage = SessionState.GetInt("IntroValidationStage", 0);
        if (!Application.isPlaying)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (stage == 9) { Stage(10); EditorApplication.isPlaying = true; }
            else if (stage == 12)
            {
                File.Delete(Marker);
                File.AppendAllText(Report, "Validation finished; returned to Edit Mode.\n");
                EditorApplication.update -= Tick;
            }
            return;
        }
        var sequence = Object.FindAnyObjectByType<IntroTruckSequence>();
        if (!sequence) return;
        EditorApplication.QueuePlayerLoopUpdate();
        int listeners = Object.FindObjectsByType<AudioListener>().Count(l => l.isActiveAndEnabled);
        switch (stage)
        {
            case 0:
                Check("Initial input disabled", sequence.playerInput.All(b => !b.enabled));
                Check("Initial aerial camera", sequence.introCamera.enabled && !sequence.playerCamera.enabled);
                Check("Initial one AudioListener", listeners == 1, listeners.ToString());
                Snapshot("initial"); Stage(1); break;
            case 1:
                if (sequence.Elapsed < sequence.cutsceneDuration + .5f) break;
                Check("First-person handoff", sequence.HandedOff && sequence.playerCamera.enabled && !sequence.introCamera.enabled);
                Check("Input restored", sequence.playerInput.All(b => b.enabled));
                Check("Handoff one AudioListener", listeners == 1);
                Snapshot("handoff"); Stage(2); break;
            case 2:
                if (sequence.Elapsed < sequence.cutsceneDuration + sequence.entryWindow + sequence.gate.travelTime + 8) break;
                Check("Truck parked", sequence.Parked, $"position={sequence.truck.position} blocked={sequence.BlockedBy} target={sequence.TargetPoint}");
                Check("Parking position", Vector3.Distance(sequence.truck.position, sequence.route.Last().position) < .35f);
                Check("Missed entry prompt state", sequence.MissedEntry);
                Check("Gate closes after clearance", sequence.gate.IsClosed);
                Snapshot("missed-entry");
                sequence.Retry();
                Check("Retry resets truck", Vector3.Distance(sequence.truck.position, sequence.route[0].position) < .01f);
                Check("Retry resets input and camera", !sequence.HandedOff && !sequence.playerInput.Any(b => b.enabled) && sequence.introCamera.enabled);
                keyboard = InputSystem.AddDevice<Keyboard>("IntroValidationKeyboard");
                waypoint = 0; Stage(3); break;
            case 3:
                if (!sequence.HandedOff) break;
                if (waypoint < Walk.Length)
                {
                    Vector3 delta = Walk[waypoint] - sequence.player.position; delta.y = 0;
                    if (delta.magnitude < .4f) waypoint++;
                    else sequence.player.rotation = Quaternion.LookRotation(delta);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                }
                else InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                if (sequence.Elapsed < sequence.cutsceneDuration + sequence.entryWindow + sequence.gate.travelTime + 5) break;
                Check("Player follows via actual movement input", waypoint == Walk.Length, $"waypoint={waypoint} position={sequence.player.position}");
                Check("Successful entry suppresses retry", !sequence.MissedEntry);
                Check("Truck parks with following player", sequence.Parked, sequence.BlockedBy);
                Check("One listener after entry", listeners == 1);
                Snapshot("successful-entry");
                InputSystem.RemoveDevice(keyboard); keyboard = null;
                sequence.enabled = false;
                sequence.gate.SetOpen(true);
                SessionState.SetFloat("IntroValidationWait", Time.time + sequence.gate.travelTime + .5f);
                Stage(4); break;
            case 4:
                if (Time.time < SessionState.GetFloat("IntroValidationWait", 0)) break;
                PlacePlayer(sequence, sequence.entryPlanePoint + Vector3.up * 1.25f);
                sequence.gate.SetOpen(false);
                SessionState.SetFloat("IntroValidationWait", Time.time + sequence.gate.travelTime + .5f);
                Stage(5); break;
            case 5:
                if (Time.time < SessionState.GetFloat("IntroValidationWait", 0)) break;
                Check("Gate holds open for player", sequence.gate.IsOpen);
                PlacePlayer(sequence, new Vector3(3.78f,1.25f,-82));
                sequence.truck.position = sequence.entryPlanePoint + Vector3.up * .2f;
                Physics.SyncTransforms();
                SessionState.SetFloat("IntroValidationWait", Time.time + sequence.gate.travelTime + .5f);
                Stage(6); break;
            case 6:
                if (Time.time < SessionState.GetFloat("IntroValidationWait", 0)) break;
                Check("Gate holds open for truck", sequence.gate.IsOpen);
                sequence.truck.position = sequence.route.Last().position;
                Physics.SyncTransforms();
                SessionState.SetFloat("IntroValidationWait", Time.time + sequence.gate.travelTime + .5f);
                Stage(7); break;
            case 7:
                if (Time.time < SessionState.GetFloat("IntroValidationWait", 0)) break;
                Check("Gate closes once occupied area clears", sequence.gate.IsClosed);
                Stage(9); EditorApplication.isPlaying = false; break;
            case 10:
                Check("Scene replay starts at aerial", !sequence.HandedOff && sequence.introCamera.enabled && !sequence.playerInput.Any(b => b.enabled));
                Check("Scene replay resets parking and retry", !sequence.Parked && !sequence.MissedEntry);
                Check("Scene replay one listener", listeners == 1);
                Stage(11); break;
            case 11:
                if (!sequence.HandedOff) break;
                Check("Replay restores first-person and input", sequence.playerCamera.enabled && sequence.playerInput.All(b => b.enabled));
                Stage(12); EditorApplication.isPlaying = false; break;
        }
    }
}

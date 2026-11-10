using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Operation1989.Interaction.Environment;
using Object = UnityEngine.Object;

/// <summary>Exercises gate fallback, actual building entry, manual/automatic/R respawns, and new-run reset.</summary>
[InitializeOnLoad]
public static class MainBuildingCheckpointValidation
{
    const string Active = "BuildingCheckpointValidationActive";
    static readonly string Report = MainBuildingCheckpointSetup.Work + "/validation.txt";
    static bool ready;
    static int stage, frame;
    static double deadline;
    static IntroTruckSequence intro;
    static PlayerCheckpoint checkpoint;
    static PlayerCheckpointState state;
    static CombatHealth health, enemy;
    static FirstPersonController controls;
    static CharacterController body;
    static Keyboard keyboard;
    static float enemyHealth, elapsed;
    static bool originalProtection, originalRegeneration;
    static float originalSensitivity, originalMoveSpeed, originalRespawnDelay;
    static DoorController entranceDoor;
    static Vector3 closedDoorPosition;
    static float doorReadyAt;

    static MainBuildingCheckpointValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += StateChanged;
    }
    [MenuItem("Operation 1989/Combat/Validate Main Building Checkpoint")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        Directory.CreateDirectory(MainBuildingCheckpointSetup.Work);
        File.WriteAllText(Report, "Main building checkpoint Play Mode checks\n");
        SessionState.SetString("BuildingCheckpointPreviousStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool("BuildingCheckpointPreviousBackground", Application.runInBackground);
        SessionState.SetInt("BuildingCheckpointInputBackground", (int)InputSystem.settings.backgroundBehavior);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/army base.unity");
        Application.runInBackground = true; SessionState.SetBool(Active, true); ready = false; EditorApplication.isPlaying = true;
    }
    static void StateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Active, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) { stage = 0; ready = true; deadline = EditorApplication.timeSinceStartup + 90; }
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            ready = false; SessionState.SetBool(Active, false); CleanupInput();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString("BuildingCheckpointPreviousStart", ""));
            Application.runInBackground = SessionState.GetBool("BuildingCheckpointPreviousBackground", false);
        }
    }
    static void Check(string label, bool pass) => File.AppendAllText(Report, (pass ? "PASS " : "FAIL ") + label + "\n");
    static void Damage(float amount) { health.CinematicProtection = false; health.TakeDamage(amount); health.CinematicProtection = true; }
    static void Teleport(Vector3 position)
    {
        body.enabled = false; health.transform.position = position; body.enabled = true; Physics.SyncTransforms();
    }
    static bool At(Vector3 position) => Vector3.Distance(health.transform.position, position) < .25f;
    static void CleanupInput()
    {
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt("BuildingCheckpointInputBackground", 0);
    }
    static void Stop()
    {
        ready = false;
        if (health)
        {
            health.CinematicProtection = originalProtection; health.regeneratePlayerHealth = originalRegeneration;
            health.respawnDelay = originalRespawnDelay;
        }
        if (controls) { controls.mouseSensitivity = originalSensitivity; controls.moveSpeed = originalMoveSpeed; }
        CleanupInput(); EditorApplication.isPlaying = false;
    }
    static void Tick()
    {
        if (!ready || !Application.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) { Check("Completes without stalling", false); Stop(); return; }
            switch (stage)
            {
                case 0:
                    Application.runInBackground = true;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    keyboard = InputSystem.AddDevice<Keyboard>("BuildingCheckpointValidationKeyboard");
                    intro = Object.FindAnyObjectByType<IntroTruckSequence>(); checkpoint = Object.FindAnyObjectByType<PlayerCheckpoint>();
                    if (!intro || !checkpoint) throw new InvalidOperationException("The installed checkpoint and intro are required.");
                    Check("Environment setup preserves the checkpoint trigger", checkpoint.activationVolume.enabled && checkpoint.activationVolume.isTrigger);
                    health = intro.player.GetComponent<CombatHealth>(); controls = health.GetComponent<FirstPersonController>();
                    originalProtection = health.CinematicProtection; originalRegeneration = health.regeneratePlayerHealth;
                    originalRespawnDelay = health.respawnDelay; originalSensitivity = controls.mouseSensitivity; originalMoveSpeed = controls.moveSpeed;
                    controls.mouseSensitivity = 0; controls.moveSpeed = 0;
                    body = health.GetComponent<CharacterController>(); state = health.GetComponent<PlayerCheckpointState>();
                    foreach (var guard in Object.FindObjectsByType<BarricadePatrolSoldier>()) guard.enabled = false;
                    foreach (var guard in Object.FindObjectsByType<EliteGuardController>()) guard.enabled = false;
                    enemy = Object.FindAnyObjectByType<BarricadePatrolSoldier>().GetComponent<CombatHealth>();
                    enemy.TakeDamage(10); enemyHealth = enemy.Current;
                    intro.SkipIntro(); intro.RespawnInsideGate(); intro.enabled = false;
                    health.GetComponent<PlayerSoldierPresentation>().enabled = false;
                    health.regeneratePlayerHealth = false; health.respawnDelay = 120; health.Restore(); health.CinematicProtection = true;
                    Check("Gate remains the default before building entry", !state.HasCheckpoint && At(intro.RespawnPosition) && !checkpoint.TryActivate());
                    elapsed = intro.Elapsed; Damage(1000); health.Respawn();
                    Check("Death before entry still respawns inside the base gate", !health.Dead && At(intro.RespawnPosition));
                    Check("Gate respawn restores controls and the first-person camera", controls.enabled && intro.playerCamera.enabled && !intro.introCamera.enabled);
                    Damage(1000); Teleport(checkpoint.activationVolume.bounds.center.WithY(checkpoint.respawnPoint.position.y));
                    Check("Dead players cannot earn the building checkpoint", !checkpoint.TryActivate() && !state.HasCheckpoint);
                    health.Respawn();
                    Teleport(checkpoint.activationVolume.bounds.center.WithY(checkpoint.respawnPoint.position.y)); controls.enabled = false;
                    Check("Cutscene or disabled player controls cannot activate a checkpoint", !checkpoint.TryActivate() && !state.HasCheckpoint);
                    controls.enabled = true;
                    checkpoint.player = enemy;
                    Check("NPCs cannot activate the player checkpoint", !checkpoint.TryActivate()); checkpoint.player = health;
                    Vector3 outside = checkpoint.activationVolume.transform.TransformPoint(new Vector3(0, 0, -checkpoint.activationVolume.size.z * .5f - .3f));
                    outside.y = checkpoint.respawnPoint.position.y; Teleport(outside);
                    Check("Standing before the interior entry boundary does not activate it", !checkpoint.TryActivate() && !state.HasCheckpoint);
                    foreach (var door in Object.FindObjectsByType<DoorController>()) if (door.name == "door_middle") entranceDoor = door;
                    if (!entranceDoor) throw new InvalidOperationException("The normal main entrance door is missing.");
                    closedDoorPosition = entranceDoor.transform.position;
                    Check("The normal main entrance door opens before walking inside", entranceDoor.IsOpen || entranceDoor.TryProximityInteract());
                    doorReadyAt = Time.time + 1.5f; stage = 10; break;
                case 10:
                    if (Time.time < doorReadyAt) return;
                    float slide = Vector3.Dot(entranceDoor.transform.position - closedDoorPosition, checkpoint.transform.right);
                    // Walk through the gap opposite the sliding leaf, not through its remaining mesh.
                    Vector3 entry = checkpoint.activationVolume.transform.TransformPoint(new Vector3(slide <= 0 ? .5f : -.5f, 0, -checkpoint.activationVolume.size.z * .5f - .3f));
                    entry.y = checkpoint.respawnPoint.position.y; Teleport(entry);
                    Damage(20); body.Move(checkpoint.transform.forward * .9f); Physics.SyncTransforms();
                    frame = Time.frameCount; stage = 1; break;
                case 1:
                    if (Time.frameCount - frame < 2) return;
                    // Normal walking gets another frame after the entrance passage adjusts collision.
                    if (!state.HasCheckpoint && !checkpoint.Contains(health.transform.position) && Time.frameCount - frame < 20)
                    {
                        body.Move(checkpoint.transform.forward * .25f); Physics.SyncTransforms(); return;
                    }
                    File.AppendAllText(Report, $"Entry position {health.transform.position} | Inside {checkpoint.Contains(health.transform.position)} | Trigger enabled {checkpoint.activationVolume.enabled}\n");
                    Check("Walking through the interior entrance activates the checkpoint", state.HasCheckpoint && state.DisplayName == "Main building entrance");
                    Check("Checkpoint activation preserves current player health", Mathf.Approximately(health.Current, 80));
                    Check("Checkpoint reached notification is available", state.NotificationVisible);
                    Check("The same checkpoint cannot repeatedly reactivate", !checkpoint.TryActivate());
                    Damage(1000); Check("Death preserves the earned checkpoint", state.HasCheckpoint);
                    health.Respawn(); health.GetComponent<PlayerSoldierPresentation>().enabled = false;
                    Check("Manual respawn uses the building entrance with full health", At(checkpoint.respawnPoint.position) && Mathf.Approximately(health.Current, health.maximumHealth));
                    Check("Building respawn restores inward facing and first-person controls", Quaternion.Angle(health.transform.rotation, checkpoint.respawnPoint.rotation) < 1 && controls.enabled && intro.playerCamera.enabled);
                    Check("Respawn preserves the convoy timeline and entered state", intro.Elapsed >= elapsed && intro.HandedOff && intro.Entered && !intro.MissedEntry);
                    Check("Player respawn preserves enemy damage", Mathf.Approximately(enemy.Current, enemyHealth));
                    Teleport(intro.RespawnPosition);
                    Check("Leaving the building does not downgrade the checkpoint", state.HasCheckpoint && !checkpoint.TryActivate());
                    health.respawnDelay = .2f; Damage(1000); stage = 2; break;
                case 2:
                    if (health.Dead) return;
                    Check("Timed automatic respawn uses the earned building checkpoint", At(checkpoint.respawnPoint.position) && state.HasCheckpoint);
                    health.respawnDelay = 120; Damage(1000);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); frame = Time.frameCount; stage = 3; break;
                case 3:
                    if (Time.frameCount - frame < 3) return;
                    Check("R immediately respawns at the building checkpoint", !health.Dead && At(checkpoint.respawnPoint.position) && state.HasCheckpoint);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    intro.Retry();
                    Check("A full intro retry clears checkpoint progress", !state.HasCheckpoint && !intro.HandedOff && !intro.Entered);
                    intro.SkipIntro(); intro.RespawnAtCurrentCheckpoint();
                    Check("After a new-run reset the gate is the fallback again", !state.HasCheckpoint && At(intro.RespawnPosition));
                    Check("Respawn marker has safe player clearance", MainBuildingCheckpointSetup.ClearPlayerSpace(checkpoint.respawnPoint.position, body));
                    Stop(); break;
            }
        }
        catch (Exception e) { File.AppendAllText(Report, "FAIL " + e + "\n"); Stop(); }
    }
    static Vector3 WithY(this Vector3 value, float y) { value.y = y; return value; }
}

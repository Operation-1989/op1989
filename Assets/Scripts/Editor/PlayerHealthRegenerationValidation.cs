using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Actual component/clock checks in the existing isolated combat scene; no main-scene edits.</summary>
[InitializeOnLoad]
public static class PlayerHealthRegenerationValidation
{
    const string Work = "Temp/PlayerHealth", Active = "PlayerHealthValidationActive";
    static bool ready;
    static int stage;
    static float hitAt, until, damagedHealth, enemyDamagedHealth, pausedHealth;
    static double deadline, pauseUntil;
    static CombatHealth player, enemy;

    static PlayerHealthRegenerationValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += StateChanged;
    }
    static void Poll()
    {
        string request = Work + "/request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(request);
        try { Run(); } catch (Exception e) { File.WriteAllText(Work + "/error.txt", e.ToString()); }
    }

    [MenuItem("Operation 1989/Combat/Validate Player Health Regeneration")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EliteSoldierSetup.TestScene);
        if (!scene) throw new InvalidOperationException("The isolated combat test scene is missing.");
        Directory.CreateDirectory(Work); File.WriteAllText(Work + "/validation.txt", "Player health regeneration Play Mode checks\n");
        SessionState.SetString("PlayerHealthPreviousStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool("PlayerHealthPreviousBackground", Application.runInBackground);
        EditorSceneManager.playModeStartScene = scene; Application.runInBackground = true;
        SessionState.SetBool(Active, true); ready = false; EditorApplication.isPlaying = true;
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Active, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            stage = 0; ready = true; deadline = EditorApplication.timeSinceStartup + 120;
            SessionState.SetFloat("PlayerHealthPreviousTimeScale", Time.timeScale);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            ready = false; SessionState.SetBool(Active, false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString("PlayerHealthPreviousStart", ""));
            Application.runInBackground = SessionState.GetBool("PlayerHealthPreviousBackground", false);
        }
    }
    static void Check(string label, bool pass) => File.AppendAllText(Work + "/validation.txt", (pass ? "PASS " : "FAIL ") + label + "\n");
    static void Stop()
    {
        ready = false; Time.timeScale = SessionState.GetFloat("PlayerHealthPreviousTimeScale", 1);
        EditorApplication.isPlaying = false;
    }
    static void Tick()
    {
        if (!ready || !Application.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup >= deadline) { Check("Completes without clock stalls", false); Stop(); return; }
            switch (stage)
            {
                case 0:
                    Application.runInBackground = true; Time.timeScale = 5;
                    var presentation = Object.FindAnyObjectByType<PlayerSoldierPresentation>();
                    player = presentation.GetComponent<CombatHealth>();
                    presentation.enabled = false; player.GetComponent<FirstPersonController>().enabled = false;
                    var guard = Object.FindAnyObjectByType<EliteGuardController>(); guard.enabled = false;
                    guard.GetComponent<RifleWeapon>().enabled = false; enemy = guard.GetComponent<CombatHealth>();
                    player.maximumHealth = 100; player.CinematicProtection = false; player.respawnDelay = 120; player.Restore();
                    Check("Default total recovery time is 15 gameplay seconds", player.regeneratePlayerHealth && Mathf.Approximately(player.regenerationDelay + player.regenerationDuration, 15));
                    enemy.Restore(); enemy.TakeDamage(40); enemyDamagedHealth = enemy.Current;
                    player.TakeDamage(60); hitAt = Time.time; until = hitAt + 1; stage = 1; break;
                case 1:
                    if (Time.time < until) return;
                    Check("Damage-free delay prevents immediate healing", Mathf.Approximately(player.Current, 40) && !player.IsRegenerating);
                    until = hitAt + 9; stage = 2; break;
                case 2:
                    if (Time.time < until) return;
                    Check("Player heals gradually after the delay", player.Current > 40 && player.Current < 95 && player.IsRegenerating);
                    damagedHealth = player.Current;
                    player.CinematicProtection = true; player.TakeDamage(50); player.CinematicProtection = false;
                    player.TakeDamage(0); player.TakeDamage(-10);
                    Check("Protected and nonpositive hits do not change health", Mathf.Approximately(player.Current, damagedHealth));
                    until = hitAt + 15.5f; stage = 3; break;
                case 3:
                    if (Time.time < until) return;
                    Check("Full health after 15 seconds with frame tolerance; ignored hits do not reset recovery", Mathf.Approximately(player.Current, 100) && !player.IsRegenerating);
                    Check("Enemy health does not regenerate", Mathf.Approximately(enemy.Current, enemyDamagedHealth));
                    player.TakeDamage(40); hitAt = Time.time; until = hitAt + 7; stage = 4; break;
                case 4:
                    if (Time.time < until) return;
                    Check("Second recovery has started", player.Current > 60 && player.Current < 100);
                    player.TakeDamage(10); damagedHealth = player.Current; hitAt = Time.time; until = hitAt + 1; stage = 5; break;
                case 5:
                    if (Time.time < until) return;
                    Check("A new hit interrupts healing and restarts the damage-free delay", Mathf.Approximately(player.Current, damagedHealth) && !player.IsRegenerating);
                    until = hitAt + 6; stage = 6; break;
                case 6:
                    if (Time.time < until) return;
                    Time.timeScale = 0; pauseUntil = EditorApplication.timeSinceStartup + .3; stage = 7; break;
                case 7:
                    if (EditorApplication.timeSinceStartup < pauseUntil) return;
                    pausedHealth = player.Current; pauseUntil = EditorApplication.timeSinceStartup + .5; stage = 8; break;
                case 8:
                    if (EditorApplication.timeSinceStartup < pauseUntil) return;
                    Check("Pausing gameplay pauses regeneration", Mathf.Approximately(player.Current, pausedHealth));
                    Time.timeScale = 5; until = hitAt + 15.5f; stage = 9; break;
                case 9:
                    if (Time.time < until) return;
                    Check("Recovery finishes after the latest hit without exceeding maximum health", Mathf.Approximately(player.Current, 100));
                    player.regeneratePlayerHealth = false; player.TakeDamage(20); damagedHealth = player.Current;
                    until = Time.time + 16; stage = 10; break;
                case 10:
                    if (Time.time < until) return;
                    Check("Inspector toggle disables player regeneration", Mathf.Approximately(player.Current, damagedHealth));
                    player.regeneratePlayerHealth = true; player.Restore(); player.TakeDamage(1000);
                    until = Time.time + 16; stage = 11; break;
                case 11:
                    if (Time.time < until) return;
                    Check("Regeneration never revives a dead player", player.Dead && Mathf.Approximately(player.Current, 0) && !player.IsRegenerating);
                    player.Restore();
                    Check("Restore clears recovery state and restores full health", Mathf.Approximately(player.Current, 100) && !player.IsRegenerating);
                    Stop(); break;
            }
        }
        catch (Exception e) { File.AppendAllText(Work + "/validation.txt", "FAIL " + e + "\n"); Stop(); }
    }
}

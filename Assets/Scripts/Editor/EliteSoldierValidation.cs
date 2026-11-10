using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real physics/combat Play Mode checks, with pure utility/A* assertions. Results stay outside Assets.</summary>
[InitializeOnLoad]
public static class EliteSoldierValidation
{
    const string Active = "EliteValidationActive";
    const string Report = EliteSoldierSetup.Work + "/validation.txt";
    static EliteGuardController guard;
    static ElitePerception senses;
    static EliteMovement movement;
    static Transform player;
    static CombatHealth playerHealth, guardHealth;
    static GameObject wall;
    static int stage, shots;
    static float until;
    static Vector3 remembered;
    static bool previousBackground;
    static bool ready;
    static readonly Vector3 Origin = new Vector3(1000, 0, 1000);

    static EliteSoldierValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    [MenuItem("Operation 1989/Elite/3 Validate In Play Mode")]
    public static void Run()
    {
        if (Application.isPlaying || SessionState.GetBool(Active, false)) throw new InvalidOperationException("Finish the active Play session first.");
        Directory.CreateDirectory(EliteSoldierSetup.Work);
        if (File.Exists(EliteSoldierSetup.Work + "/passed.txt")) File.Delete(EliteSoldierSetup.Work + "/passed.txt");
        File.WriteAllText(Report, "Elite Soldier automated Play Mode checks\n");
        var testScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EliteSoldierSetup.TestScene);
        if (!testScene) throw new InvalidOperationException("Build the isolated scene first.");
        SessionState.SetString("ElitePreviousStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        // Unity backs up and restores the open editor scenes, including unsaved changes.
        // Only the isolated scene enters Play Mode; main-scene bootstrap/cinematics cannot interfere.
        EditorSceneManager.playModeStartScene = testScene;
        SessionState.SetBool(Active, true); SessionState.SetInt("EliteValidationStage", 0);
        ready = false;
        EditorApplication.isPlaying = true;
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Active, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            stage = 0; ready = true;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            ready = false;
            SessionState.SetBool(Active, false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString("ElitePreviousStartScene", ""));
            if (File.Exists(Report) && !File.ReadAllText(Report).Contains("FAIL") && SessionState.GetInt("EliteValidationStage", 0) == 9)
                File.WriteAllText(EliteSoldierSetup.Work + "/passed.txt", "Core elite checks passed at " + DateTime.UtcNow.ToString("O"));
        }
    }

    static void Check(string name, bool pass)
    { File.AppendAllText(Report, (pass ? "PASS " : "FAIL ") + name + "\n"); }
    static void Teleport(Transform t, Vector3 position, Quaternion rotation)
    {
        var cc = t.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        t.SetPositionAndRotation(position, rotation); if (cc) cc.enabled = true; Physics.SyncTransforms();
    }

    static void Tick()
    {
        if (!ready || !SessionState.GetBool(Active, false) || !Application.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            switch (stage)
            {
                case 0:
                    previousBackground = Application.runInBackground; Application.runInBackground = true;
                    guard = Object.FindAnyObjectByType<EliteGuardController>();
                    if (!guard) return;
                    senses = guard.GetComponent<ElitePerception>(); movement = guard.GetComponent<EliteMovement>();
                    guardHealth = guard.GetComponent<CombatHealth>(); player = senses.target; playerHealth = player.GetComponent<CombatHealth>();
                    guard.enabled = false; guard.GetComponent<RifleWeapon>().damage = 1;
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Validation floor";
                    floor.transform.position = Origin + Vector3.down * .25f; floor.transform.localScale = new Vector3(40, .5f, 40);
                    Teleport(guard.transform, Origin + Vector3.up * .04f, Quaternion.identity);
                    guard.guardPost.position = guard.transform.position; guard.guardPost.rotation = Quaternion.identity;
                    movement.graph = EliteSoldierSetup.Grid(floor.transform, new Bounds(Origin, new Vector3(24, 3, 24)), .04f);
                    // Grid uses absolute Y; the fixture origin is at Y=0.
                    guard.defensiveRadius = 10; senses.memorySeconds = 1; guard.searchSeconds = 1;
                    Teleport(player, Origin + new Vector3(0, 1, 7), Quaternion.Euler(0, 180, 0));
                    senses.Sense(); Check("Detects visible player inside FOV", senses.Visible);
                    Teleport(player, Origin + new Vector3(0, 1, -7), Quaternion.identity);
                    senses.Sense(); Check("Rejects player outside FOV", !senses.Visible);
                    Teleport(player, Origin + new Vector3(0, 1, 35), Quaternion.identity);
                    senses.Sense(); Check("Rejects player outside range", !senses.Visible);
                    Teleport(player, Origin + new Vector3(0, 1, 7), Quaternion.identity);
                    senses.Sense(); remembered = senses.LastPosition;
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Validation blocking wall";
                    wall.transform.position = Origin + new Vector3(0, 1.5f, 3); wall.transform.localScale = new Vector3(20, 3, .5f);
                    Physics.SyncTransforms(); senses.Sense(); Check("Wall blocks detection", !senses.Visible);
                    Teleport(player, Origin + new Vector3(4, 1, 7), Quaternion.identity);
                    senses.Sense(); Check("Hidden movement does not change last observation", senses.LastPosition == remembered);
                    until = Time.time + 1.2f; stage = 1; break;
                case 1:
                    if (Time.time < until) return;
                    senses.Sense(); Check("Memory confidence decays to zero behind wall", senses.Confidence == 0 && senses.LastPosition == remembered);
                    CheckPureLogic();
                    var path = new List<Vector3>();
                    Check("Blocked route reports unreachable", !movement.graph.FindPath(guard.transform.position, Origin + new Vector3(0, .04f, 6), guard.transform, null, 0, guard.transform.position, 10, path, out _, out _));
                    wall.transform.localScale = new Vector3(3, 3, .5f); Physics.SyncTransforms();
                    Check("A* finds alternative route around obstacle", movement.graph.FindPath(guard.transform.position, Origin + new Vector3(0, .04f, 6), guard.transform, null, 0, guard.transform.position, 10, path, out _, out _));
                    Check("Returned path edges have clearance", path.Count > 1 && Enumerable.Range(1, path.Count - 1).All(i => movement.graph.ClearSegment(path[i - 1], path[i], guard.transform)));
                    movement.graph.BeginQueryBatch();
                    movement.graph.FindPath(guard.transform.position, Origin + new Vector3(0, .04f, 6), guard.transform, null, 0, guard.transform.position, 10, path, out _, out _);
                    movement.graph.EndQueryBatch();
                    wall.transform.localScale = new Vector3(20, 3, .5f); Physics.SyncTransforms();
                    movement.graph.BeginQueryBatch();
                    bool batchBlocked = !movement.graph.FindPath(guard.transform.position, Origin + new Vector3(0, .04f, 6), guard.transform, null, 0, guard.transform.position, 10, path, out _, out _);
                    movement.graph.EndQueryBatch();
                    Check("A later query batch detects a newly blocked route", batchBlocked);
                    wall.transform.localScale = new Vector3(3, 3, .5f); Physics.SyncTransforms();
                    var coverObject = new GameObject("Validation cover"); coverObject.transform.position = Origin + new Vector3(0, .04f, 2);
                    var cover = coverObject.AddComponent<EliteCoverPoint>();
                    Check("Authored cover blocks known threat", cover.ProtectedFrom(Origin + new Vector3(0, 1.5f, 7), guard.transform, ~0));
                    Check("Cover is reachable", movement.graph.FindPath(guard.transform.position, cover.transform.position, guard.transform, null, 0, guard.transform.position, 10, path, out _, out _));
                    guard.coverPoints = Array.Empty<EliteCoverPoint>();
                    Object.DestroyImmediate(wall); Physics.SyncTransforms();
                    movement.ResetRoute();
                    movement.Tick(Origin + new Vector3(0, .04f, 6), guard.guardPost.position, 10, null, 0, true, null, .02f);
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = Origin + new Vector3(0, 1.5f, 1.4f);
                    wall.transform.localScale = new Vector3(20, 3, .5f); Physics.SyncTransforms();
                    until = Time.time + movement.retryInterval + .2f; stage = 8; break;
                case 8:
                    if (Time.time < until) return;
                    movement.Tick(Origin + new Vector3(0, .04f, 6), guard.guardPost.position, 10, null, 0, true, null, .02f);
                    Check("Newly blocked edge replans safely without crossing wall", movement.Failed && movement.Path.Count == 0 && guard.transform.position.z < Origin.z + 1);
                    Object.DestroyImmediate(wall); Physics.SyncTransforms();
                    guard.enabled = true; guard.ResetGuard();
                    Teleport(player, Origin + new Vector3(0, 1, 7), Quaternion.identity);
                    shots = guard.GetComponent<RifleWeapon>().ShotsFired; until = Time.time + 1.5f; stage = 2; break;
                case 2:
                    if (Time.time < until) return;
                    Check("Visible aligned target receives rifle shots", guard.GetComponent<RifleWeapon>().ShotsFired > shots && playerHealth.Current < playerHealth.maximumHealth);
                    if (guard.GetComponent<RifleWeapon>().ShotsFired == shots || playerHealth.Current >= playerHealth.maximumHealth)
                        File.AppendAllText(Report, $"DETAIL action={guard.Action} visible={senses.Visible} aim={senses.LastAim} muzzle={guard.GetComponent<RifleWeapon>().muzzle.position} health={playerHealth.Current}\n");
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Muzzle-only cover";
                    wall.transform.position = guard.GetComponent<RifleWeapon>().muzzle.position;
                    wall.transform.localScale = new Vector3(.5f, .2f, .5f); Physics.SyncTransforms();
                    shots = guard.GetComponent<RifleWeapon>().ShotsFired; until = Time.time + .6f; stage = 10; break;
                case 10:
                    if (Time.time < until) return;
                    Check("Barrel embedded in cover cannot fire despite clear eye ray", senses.Visible && guard.GetComponent<RifleWeapon>().ShotsFired == shots);
                    Object.DestroyImmediate(wall);
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = Origin + new Vector3(0, 1.5f, 3); wall.transform.localScale = new Vector3(20, 3, .5f); Physics.SyncTransforms();
                    shots = guard.GetComponent<RifleWeapon>().ShotsFired; remembered = senses.LastPosition;
                    Teleport(player, Origin + new Vector3(5, 1, 7), Quaternion.identity);
                    until = Time.time + .7f; stage = 3; break;
                case 3:
                    if (Time.time < until) return;
                    Check("Occlusion stops firing and hidden tracking", guard.GetComponent<RifleWeapon>().ShotsFired == shots && senses.LastPosition == remembered);
                    Object.DestroyImmediate(wall); Physics.SyncTransforms();
                    Teleport(player, Origin + new Vector3(0, 1, 18), Quaternion.identity);
                    until = Time.time + .5f; stage = 4; break;
                case 4:
                    if (Time.time < until) return;
                    Check("Retreat beyond defensive limit cannot lure guard", Vector3.Distance(guard.transform.position, guard.guardPost.position) < 1 && guard.Action != EliteAction.Engage && guard.Action != EliteAction.Search);
                    guard.enabled = false;
                    Teleport(guard.transform, Origin + new Vector3(-5, .04f, 0), Quaternion.identity); senses.Forget(); guard.ResetGuard();
                    Teleport(player, Origin + new Vector3(0, 1, -25), Quaternion.identity);
                    guard.enabled = true; until = Time.time + 5; stage = 5; break;
                case 5:
                    if (Time.time < until) return;
                    Check("Returns to original post and facing", Vector3.Distance(guard.transform.position, guard.guardPost.position) < .5f && Vector3.Angle(guard.transform.forward, guard.guardPost.forward) < 10);
                    guardHealth.TakeDamage(15); Check("Reuses existing damage interface", guardHealth.Current == guardHealth.maximumHealth - 15);
                    guardHealth.TakeDamage(1000); shots = guard.GetComponent<RifleWeapon>().ShotsFired;
                    until = Time.time + .4f; stage = 6; break;
                case 6:
                    if (Time.time < until) return;
                    Check("Death stops movement, firing and animator", guardHealth.Dead && !guard.GetComponent<CharacterController>().enabled && !movement.animator.enabled && guard.GetComponent<RifleWeapon>().ShotsFired == shots);
                    guardHealth.Restore(); until = Time.time + .3f; stage = 7; break;
                case 7:
                    if (Time.time < until) return;
                    Check("Restore resets elite memory and re-enables body", !guardHealth.Dead && guard.GetComponent<CharacterController>().enabled && movement.animator.enabled && !senses.HasMemory);
                    // The health spawn belongs to the saved isolated scene, so restart restoration uses its authored spawn.
                    stage = 9; SessionState.SetInt("EliteValidationStage", 9);
                    Application.runInBackground = previousBackground; EditorApplication.isPlaying = false; break;
            }
        }
        catch (Exception e)
        {
            File.AppendAllText(Report, "FAIL exception " + e + "\n");
            Application.runInBackground = previousBackground; EditorApplication.isPlaying = false;
        }
    }

    public static void CheckPureLogic()
    {
        var utility = new EliteUtility();
        var high = utility.Scores(true, true, true, true, true, 1, 1, 1, 0, 1);
        var low = utility.Scores(true, true, true, true, true, .1f, 1, 1, 0, 1);
        Check("Healthy guard engages; wounded guard prefers cover", utility.Select(EliteAction.Defend, high, 10) == EliteAction.Engage && utility.Select(EliteAction.Defend, low, 10) == EliteAction.Cover);
        Check("Minimum commitment prevents rapid switching", utility.Select(EliteAction.Engage, low, .1f) == EliteAction.Engage);
        high[(int)EliteAction.Engage] = -1;
        Check("Invalid action interrupts commitment immediately", utility.Select(EliteAction.Engage, high, 0) != EliteAction.Engage);
        var points = new[] { Vector3.zero, Vector3.right, Vector3.right * 2, new Vector3(1, 0, 1) };
        bool Edge(int a, int b) => (a == 0 && (b == 1 || b == 3)) || ((a == 1 || a == 3) && b == 2);
        var path = EliteNavigationGraph.Search(points, 0, 2, Edge, p => p == points[1] ? 1 : 0, 5, out float cost, out _);
        Check("Nonnegative exposure cost chooses protected route", path != null && path.SequenceEqual(new[] { 0, 3, 2 }) && cost > 2);
        path = EliteNavigationGraph.Search(points, 0, 0, Edge, _ => 0, 0, out cost, out _);
        Check("A* start equals goal has zero cost", path != null && path.Count == 1 && cost == 0);
        Check("A* invalid index and unreachable goal fail safely", EliteNavigationGraph.Search(points, -1, 0, Edge, _ => 0, 0, out _, out _) == null && EliteNavigationGraph.Search(points, 2, 0, Edge, _ => 0, 0, out _, out _) == null);
    }
}

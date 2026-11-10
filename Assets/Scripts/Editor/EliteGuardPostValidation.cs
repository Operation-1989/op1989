using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Tests the actual army-base placement after runtime environment colliders have been installed.</summary>
[InitializeOnLoad]
public static class EliteGuardPostValidation
{
    const string Active = "ElitePostSmokeActive";
    static readonly string Report = EliteSoldierSetup.Work + "/post-validation.txt";
    static bool ready, previousBackground;
    static int stage, initialShots, initialFrame;
    static float until;
    static double deadline;
    static EliteGuardController guard;
    static bool roomCase, movedToCover, reachedCover, leftRoom;
    static Vector3 combatPosition;

    static EliteGuardPostValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    [MenuItem("Operation 1989/Elite/6 Validate Main Scene Entrance Reaction")]
    public static void Run()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit mode.");
        Directory.CreateDirectory(EliteSoldierSetup.Work);
        File.WriteAllText(Report, "Main-scene elite entrance reaction\n");
        SessionState.SetBool("ElitePostPreviousBackground", Application.runInBackground);
        Application.runInBackground = true;
        SessionState.SetString("ElitePostPreviousStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/army base.unity");
        SessionState.SetBool(Active, true); ready = false; EditorApplication.isPlaying = true;
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Active, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { stage = 0; ready = true; }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            ready = false; SessionState.SetBool(Active, false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString("ElitePostPreviousStart", ""));
        }
    }

    static void Check(string label, bool pass) => File.AppendAllText(Report, (pass ? "PASS " : "FAIL ") + label + "\n");
    static void Tick()
    {
        if (!ready || !Application.isPlaying || !SessionState.GetBool(Active, false)) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (stage == 0)
            {
                previousBackground = SessionState.GetBool("ElitePostPreviousBackground", false);
                Application.runInBackground = true; Time.timeScale = 1;
                guard = Object.FindAnyObjectByType<EliteGuardController>();
                var senses = guard.GetComponent<ElitePerception>();
                var player = senses.target;
                if (!player) throw new InvalidOperationException("Scene guard has no player reference.");
                var intro = Object.FindAnyObjectByType<IntroTruckSequence>(); if (intro) intro.enabled = false;
                foreach (var patrol in Object.FindObjectsByType<BarricadePatrolSoldier>()) patrol.enabled = false;
                var presentation = player.GetComponent<PlayerSoldierPresentation>(); if (presentation) presentation.enabled = false;
                var controls = player.GetComponent<FirstPersonController>(); controls.enabled = true;
                var health = player.GetComponent<CombatHealth>(); health.maximumHealth = 10000; health.Restore(); health.CinematicProtection = false;
                var room = Object.FindAnyObjectByType<MissionAmbientAudio>().controlRoom.bounds;
                EliteSoldierSetup.ChooseEntrancePost(guard.GetComponent<EliteMovement>().graph, room, guard.transform,
                    out _, out _, out Vector3 approach);
                var body = player.GetComponent<CharacterController>(); body.enabled = false;
                player.SetPositionAndRotation(approach + Vector3.up, Quaternion.LookRotation(Vector3.ProjectOnPlane(guard.transform.position - approach, Vector3.up)));
                body.enabled = true; controls.ResetLook(); Physics.SyncTransforms();
                guard.ResetGuard(); senses.Sense();
                Check("Authored guard faces the reachable entrance approach", ElitePerception.InField(guard.transform.forward, approach + Vector3.up * 1.45f - senses.Eye, senses.range, senses.fieldOfView));
                Check("Actual runtime room geometry permits detection", senses.Visible);
                var graph = guard.GetComponent<EliteMovement>().graph;
                roomCase = graph.navigationArea;
                if (roomCase)
                {
                    Check("Guard post is inside the enclosed computer/server room", graph.Contains(guard.transform.position));
                    var rackObstacles = Object.FindObjectsByType<EliteServerObstacle>();
                    int colliderCount = 0; foreach (var rack in rackObstacles) colliderCount += rack.GetComponentsInChildren<Collider>().Length;
                    Check("Server collision uses rack volumes without thousands of generated mesh colliders", rackObstacles.Length > 0 && colliderCount <= rackObstacles.Length * 16);
                    File.AppendAllText(Report, $"Server racks {rackObstacles.Length} | Rack colliders {colliderCount}\n");
                    int reachable = 0, detours = 0;
                    var path = new List<Vector3>();
                    graph.BeginQueryBatch();
                    try
                    {
                    foreach (var cover in guard.coverPoints)
                        if (cover && graph.Contains(cover.transform.position) && cover.ProtectedFrom(approach + Vector3.up * 1.45f, guard.transform, ~0) &&
                            graph.FindPath(guard.transform.position, cover.transform.position, guard.transform, approach + Vector3.up * 1.45f, 2,
                                guard.guardPost.position, guard.defensiveRadius, path, out _, out _))
                        {
                            reachable++;
                            if (!graph.ClearSegment(guard.transform.position, cover.transform.position, guard.transform)) detours++;
                        }
                    }
                    finally { graph.EndQueryBatch(); }
                    Check("Server cover remains protective and A* reachable in the runtime room", reachable > 0);
                    Check("A* finds a detour around a server rather than crossing through it", detours > 0);
                    Check("Room boundary rejects hallway destinations", !graph.Contains(graph.navigationArea.bounds.center + Vector3.right * 20));
                    File.AppendAllText(Report, $"Reachable server covers {reachable} | Obstacle detours {detours}\n");
                    if (reachable == 0) EliteSoldierSetup.InspectComputerRoomCover();
                }
                initialShots = guard.GetComponent<RifleWeapon>().ShotsFired;
                File.AppendAllText(Report, $"Post {guard.transform.position} | Approach {approach}\n");
                initialFrame = Time.frameCount; until = Time.time + 2;
                deadline = EditorApplication.timeSinceStartup + 60; stage = 1;
            }
            else if (stage == 1 && ((Time.time >= until && Time.frameCount - initialFrame >= 20) || EditorApplication.timeSinceStartup >= deadline))
            {
                var senses = guard.GetComponent<ElitePerception>();
                Check("Guard reacts with an eligible combat action", senses.Visible && (guard.Action == EliteAction.Engage || guard.Action == EliteAction.Cover));
                Check("Guard fires at the player entering its view", guard.GetComponent<RifleWeapon>().ShotsFired > initialShots);
                Check("Rifle damages the real player through a clear shot", senses.target.GetComponent<CombatHealth>().Current < senses.target.GetComponent<CombatHealth>().maximumHealth);
                File.AppendAllText(Report, $"Action {guard.Action} | Visible {senses.Visible} | Shots {guard.GetComponent<RifleWeapon>().ShotsFired - initialShots} | Frames {Time.frameCount - initialFrame} | Time scale {Time.timeScale} | Guard enabled {guard.enabled} | Health {guard.GetComponent<CombatHealth>().Current} | Scores {string.Join(", ", guard.Scores)}\n");
                if (!roomCase) { Stop(); return; }
                combatPosition = guard.transform.position; movedToCover = reachedCover = leftRoom = false;
                guard.GetComponent<CombatHealth>().TakeDamage(guard.GetComponent<CombatHealth>().maximumHealth * .8f);
                until = Time.time + 7; deadline = EditorApplication.timeSinceStartup + 90; stage = 2;
            }
            else if (stage == 2)
            {
                var graph = guard.GetComponent<EliteMovement>().graph;
                leftRoom |= !graph.Contains(guard.transform.position);
                movedToCover |= guard.Action == EliteAction.Cover && Vector3.Distance(guard.transform.position, combatPosition) > .6f;
                reachedCover |= movedToCover && guard.Cover && Vector3.Distance(guard.transform.position, guard.Cover.transform.position) < .45f;
                if (!reachedCover && Time.time < until && EditorApplication.timeSinceStartup < deadline) return;
                Check("Wounded guard follows its custom A* route to server cover", movedToCover && reachedCover);
                Check("Cover movement stays inside the computer room", !leftRoom);
                var senses = guard.GetComponent<ElitePerception>();
                var body = senses.target.GetComponent<CharacterController>(); body.enabled = false;
                senses.target.position = graph.navigationArea.bounds.center + Vector3.right * 40;
                body.enabled = true; Physics.SyncTransforms();
                until = Time.time + senses.memorySeconds + 8; deadline = EditorApplication.timeSinceStartup + 90; stage = 3;
            }
            else if (stage == 3)
            {
                var senses = guard.GetComponent<ElitePerception>();
                bool returned = senses.Confidence == 0 && Vector3.Distance(guard.transform.position, guard.guardPost.position) < .4f;
                if (!returned && Time.time < until && EditorApplication.timeSinceStartup < deadline) return;
                Check("After memory expires the guard returns to its post beside the computer", returned);
                Check("Return path stays inside the server room", guard.GetComponent<EliteMovement>().graph.Contains(guard.transform.position));
                Stop();
            }
        }
        catch (Exception e) { File.AppendAllText(Report, "FAIL " + e + "\n"); Stop(); }
    }

    static void Stop()
    {
        ready = false; Application.runInBackground = previousBackground; EditorApplication.isPlaying = false;
    }
}

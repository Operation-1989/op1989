using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class IntroTruckUpgrade
{
    [MenuItem("Operation 1989/Intro/Upgrade Cinematic Approach")]
    public static void Apply()
    {
        var s = Object.FindAnyObjectByType<IntroTruckSequence>();
        if (Application.isPlaying || !s || s.gameObject.scene.path != "Assets/army base.unity")
            throw new InvalidOperationException("Open army base in Edit Mode first.");
        Undo.RegisterFullObjectHierarchyUndo(s.gameObject, "Upgrade intro cinematic");
        var points = new List<Vector3> { new Vector3(-35, .2f, -99.2f), new Vector3(-9, .2f, -99.2f), new Vector3(-1.2f, .2f, -99.2f) };
        // Five metre quarter-circle: road approach eastward, finish northward in the entrance lane.
        for (int i = 1; i <= 10; i++)
        {
            float angle = i * Mathf.PI / 20;
            points.Add(new Vector3(-1.2f + 5 * Mathf.Sin(angle), .2f, -94.2f - 5 * Mathf.Cos(angle)));
        }
        points.AddRange(new[] { new Vector3(3.8f,.2f,-84), new Vector3(4.5f,.2f,-81.5f), new Vector3(6.3f,.2f,-79.5f), new Vector3(9,.2f,-78), new Vector3(14,.2f,-77.8f), new Vector3(18,.2f,-77.8f), new Vector3(23,.2f,-77.8f) });
        var root = s.route[0].parent;
        foreach (var old in s.route) if (old) Undo.DestroyObjectImmediate(old.gameObject);
        s.route = points.Select((p, i) => {
            var go = new GameObject(i == points.Count - 1 ? "Parking" : "Route " + i);
            Undo.RegisterCreatedObjectUndo(go, "New truck route");
            go.transform.SetParent(root); go.transform.position = p;
            return go.transform;
        }).ToArray();
        s.gateWaitPoint = 10;
        s.speed = 4; s.acceleration = 1.5f; s.turnSpeed = 65;
        s.cutsceneDuration = 16; s.entryWindow = 26; s.gateOpenDelay = 11;
        s.followOffset = new Vector3(-2, 7, -12);
        s.shoulderOffset = new Vector3(.6f, 1.5f, -4);
        s.truck.SetPositionAndRotation(points[0], Quaternion.Euler(0,90,0));
        var audio = s.truck.GetComponent<IntroTruckAudio>();
        if (!audio) audio = Undo.AddComponent<IntroTruckAudio>(s.truck.gameObject);
        s.truckAudio = audio;
        // Swing the existing outer road leaf around its hinge; retain the original model and scenery.
        const string hingeName = "Outer Approach Gate Open Hinge";
        if (!GameObject.Find(hingeName))
        {
            var leaf = Object.FindObjectsByType<MeshRenderer>().FirstOrDefault(r => r.name == "gate.002" && r.bounds.center.x < -5 && r.bounds.center.x > -6 && r.bounds.center.z < -95);
            if (!leaf) throw new InvalidOperationException("Cannot locate outer road gate leaf.");
            var b = leaf.bounds;
            var hinge = new GameObject(hingeName).transform;
            Undo.RegisterCreatedObjectUndo(hinge.gameObject, "Open road gate");
            hinge.SetParent(s.transform);
            hinge.position = new Vector3(b.center.x, b.min.y, b.max.z);
            Undo.SetTransformParent(leaf.transform, hinge, "Open outer gate leaf");
            hinge.rotation = Quaternion.Euler(0,90,0);
        }
        EditorUtility.SetDirty(s);
        EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
        EditorSceneManager.SaveScene(s.gameObject.scene);
        Debug.Log("Intro updated: opposite-side road approach, curved entry, drone-to-eyes camera, briefing and spatial truck audio.");
    }
}

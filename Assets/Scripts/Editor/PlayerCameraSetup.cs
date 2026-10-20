using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class PlayerCameraSetup
{
    [MenuItem("Operation 1989/Player/Configure Camera Displays")]
    public static void Configure()
    {
        if (Application.isPlaying)
            throw new System.InvalidOperationException("Run in Edit mode.");
        var player = Object.FindAnyObjectByType<PlayerSoldierPresentation>();
        if (!player || !player.firstPersonCamera)
            throw new System.InvalidOperationException("Open the soldier scene first.");

        Undo.RecordObject(player.firstPersonCamera, "Configure first person display");
        player.firstPersonCamera.targetDisplay = 0;
        player.firstPersonCamera.enabled = true;
        var sequence = Object.FindAnyObjectByType<IntroTruckSequence>();
        foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (other.gameObject.scene != player.gameObject.scene || other == player.firstPersonCamera ||
                other.targetDisplay != 0 || other.cameraType != CameraType.Game) continue;
            Undo.RecordObject(other, "Disable competing display camera");
            other.enabled = false;
        }
        if (sequence && sequence.gameObject.scene == player.gameObject.scene)
        {
            Undo.RecordObject(sequence, "Bind player first person camera");
            sequence.playerCamera = player.firstPersonCamera;
            if (sequence.introCamera)
            {
                Undo.RecordObject(sequence.introCamera, "Configure intro display");
                sequence.introCamera.targetDisplay = 0;
                sequence.introCamera.enabled = false;
            }
        }
        var existing = player.transform.Find("Third Person Camera");
        var go = existing ? existing.gameObject : new GameObject("Third Person Camera");
        if (!existing) Undo.RegisterCreatedObjectUndo(go, "Add third person camera");
        Undo.RecordObject(go.transform, "Position third person camera");
        go.transform.SetParent(player.transform, false);
        go.transform.localPosition = new Vector3(0f, 1.5f, -3.5f);
        go.transform.localScale = Vector3.one;
        go.transform.LookAt(player.transform.TransformPoint(new Vector3(0, 0.1f, 0)));
        var camera = go.GetComponent<Camera>();
        if (!camera) camera = Undo.AddComponent<Camera>(go);
        Undo.RecordObject(camera, "Configure third person display");
        camera.CopyFrom(player.firstPersonCamera);
        camera.fieldOfView = 60;
        camera.nearClipPlane = 0.1f;
        camera.targetDisplay = 1;
        camera.enabled = true;
        if (!go.GetComponent<UniversalAdditionalCameraData>())
            Undo.AddComponent<UniversalAdditionalCameraData>(go);
        // Keep the existing listener on the first person camera; both views render together.
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene);
        Debug.Log("Camera displays configured: Display 1 = first person, Display 2 = third person.");
    }
}

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;

public static class IntroTruckSetup
{
    [MenuItem("Operation 1989/Intro/Report Runtime")]
    public static void ReportRuntime()
    {
        var sequence = Object.FindAnyObjectByType<IntroTruckSequence>();
        var report = new StringBuilder();
        report.AppendLine($"playing={Application.isPlaying} time={Time.time:F3}");
        foreach (var camera in Object.FindObjectsByType<Camera>())
            report.AppendLine($"CAMERA {camera.name} enabled={camera.enabled} pos={camera.transform.position:F3}");
        report.AppendLine($"ACTIVE LISTENERS {Object.FindObjectsByType<AudioListener>().Count(l => l.isActiveAndEnabled)}");
        if (sequence)
        {
            report.AppendLine($"handoff={sequence.HandedOff} parked={sequence.Parked} missed={sequence.MissedEntry}");
            report.AppendLine($"TRUCK {sequence.truck.position:F3} rotation={sequence.truck.eulerAngles:F3}");
            report.AppendLine($"PLAYER {sequence.player.position:F3}");
            report.AppendLine($"GATE open={sequence.gate.IsOpen} closed={sequence.gate.IsClosed} requested={sequence.gate.RequestedOpen}");
            foreach (var input in sequence.playerInput) report.AppendLine($"INPUT {input.GetType().Name} enabled={input.enabled}");
        }
        Directory.CreateDirectory("Temp/IntroTruck");
        File.WriteAllText("Temp/IntroTruck/runtime.txt", report.ToString());
    }

    [MenuItem("Operation 1989/Intro/Retry")]
    public static void Retry() => Object.FindAnyObjectByType<IntroTruckSequence>().Retry();

    [MenuItem("Operation 1989/Intro/Capture Active Camera")]
    public static void CaptureActiveCamera()
    {
        var camera = Object.FindObjectsByType<Camera>().First(c => c.isActiveAndEnabled && c.cameraType == CameraType.Game);
        Capture(camera.transform.position, camera.transform.eulerAngles, "active-camera", false);
    }

    [MenuItem("Operation 1989/Intro/Inspect Entrance")]
    public static void Inspect()
    {
        Directory.CreateDirectory("Temp/IntroTruck");
        var report = new StringBuilder();
        foreach (var r in Object.FindObjectsByType<Renderer>())
            if (r.bounds.center.x > -16 && r.bounds.center.x < 22 && r.bounds.center.z < -40 && r.bounds.center.z > -140)
                report.AppendLine($"{r.name}: center={r.bounds.center:F3} size={r.bounds.size:F3}");
        foreach (var c in Object.FindObjectsByType<Collider>())
            if (c.bounds.Intersects(new Bounds(new Vector3(3, 2, -85), new Vector3(40, 10, 80))))
                report.AppendLine($"COLLIDER {c.name}: center={c.bounds.center:F3} size={c.bounds.size:F3}");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/M923_Transport_Truck_3D_TRADER.FBX"));
        foreach (var r in model.GetComponentsInChildren<Renderer>())
            report.AppendLine($"TRUCK {r.name}: center={r.bounds.center:F3} size={r.bounds.size:F3}");
        Object.DestroyImmediate(model);
        File.WriteAllText("Temp/IntroTruck/inspection.txt", report.ToString());
        Capture(new Vector3(5, 65, -90), new Vector3(90, 0, 0), "entrance-top", true);
        Capture(new Vector3(32, 18, -125), new Vector3(18, -40, 0), "entrance-perspective", false);
        var gate = GameObject.Find("gateFbx");
        foreach (var renderer in gate.GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
            Capture(new Vector3(10, 7, -111), new Vector3(10, -15, 0), "without-" + renderer.name.Replace(':', '_'), false);
            renderer.enabled = true;
        }
        model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/M923_Transport_Truck_3D_TRADER.FBX"));
        model.transform.position = new Vector3(0, 0, -120);
        Capture(new Vector3(10, 6, -130), new Vector3(15, -45, 0), "truck", false);
        Object.DestroyImmediate(model);
    }

    static void Capture(Vector3 position, Vector3 angles, string name, bool ortho)
    {
        var go = new GameObject("Intro inspection camera");
        var camera = go.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(position, Quaternion.Euler(angles));
        camera.orthographic = ortho;
        camera.orthographicSize = 40;
        var target = new RenderTexture(1200, 900, 24);
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0);
        File.WriteAllBytes("Temp/IntroTruck/" + name + ".png", texture.EncodeToPNG());
        RenderTexture.active = previous;
        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(target);
    }
}

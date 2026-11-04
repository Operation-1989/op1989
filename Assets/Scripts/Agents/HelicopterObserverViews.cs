using UnityEngine;

/// <summary>Additional display views; never replaces the player's gameplay or outro camera.</summary>
[DefaultExecutionOrder(200)]
public sealed class HelicopterObserverViews : MonoBehaviour
{
    public Transform helicopter;
    public Camera frontCamera, cinematicCamera;
    public Vector3 frontOffset = new Vector3(2.5f, 4.8f, 13);
    public Vector3 focusOffset = new Vector3(0, 2.2f, -2);
    public Vector3[] cinematicOffsets =
    {
        new Vector3(12, 5, 12), new Vector3(-14, 6, 4),
        new Vector3(-10, 8, -15), new Vector3(13, 4, -11)
    };
    [Min(2)] public float shotSeconds = 5;
    public int CinematicShotIndex { get; private set; }
    double startedAt;

    void OnEnable() => ResetViews();
    void Start()
    {
        foreach (var camera in new[] { frontCamera, cinematicCamera })
            if (camera && Display.displays.Length > camera.targetDisplay)
                Display.displays[camera.targetDisplay].Activate();
    }
    public void ResetViews()
    {
        startedAt = AudioSettings.dspTime; CinematicShotIndex = 0;
        ApplyViews(0);
    }
    void LateUpdate() => ApplyViews(Mathf.Max(0, (float)(AudioSettings.dspTime - startedAt)));
    public void ApplyViews(float elapsed)
    {
        if (!helicopter) return;
        Vector3 focus = helicopter.position + helicopter.rotation * focusOffset;
        if (frontCamera)
        {
            frontCamera.transform.position = helicopter.position + helicopter.rotation * frontOffset;
            frontCamera.transform.LookAt(focus);
        }
        if (cinematicCamera && cinematicOffsets != null && cinematicOffsets.Length > 0)
        {
            float shotTime = elapsed / Mathf.Max(2, shotSeconds);
            CinematicShotIndex = Mathf.FloorToInt(shotTime) % cinematicOffsets.Length;
            float drift = Mathf.SmoothStep(0, 1, shotTime - Mathf.Floor(shotTime));
            Vector3 offset = cinematicOffsets[CinematicShotIndex] + new Vector3(1.5f, .5f, 0) * drift;
            cinematicCamera.transform.position = helicopter.position + helicopter.rotation * offset;
            cinematicCamera.transform.LookAt(focus);
            cinematicCamera.fieldOfView = CinematicShotIndex % 2 == 0 ? 55 : 48;
        }
    }
}

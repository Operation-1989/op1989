using UnityEngine;

/// <summary>Visible launch followed by a repeating survey sweep. Detection is deliberately separate.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DroneDamage))]
public sealed class DroneMapPatrol : MonoBehaviour
{
    public Transform launchPoint;
    public Transform[] waypoints;
    public Transform[] rotors;
    public Camera droneCamera;
    public AudioClip flyingClip;
    public AudioClip cruiseLoop;
    [Min(0)] public float launchDelay = 1;
    [Min(0)] public float spoolSeconds = 2.5f;
    [Min(.1f)] public float climbSeconds = 5;
    [Min(.1f)] public float speed = 8;
    [Min(1)] public float turnSpeed = 100;
    [Range(0, 1)] public float volume = .95f;
    public float cruiseAudioStart = 12;
    public int TargetPoint { get; private set; }
    public int CompletedSweeps { get; private set; }
    public float FlightTime { get; private set; }
    public bool Cruising => FlightTime >= launchDelay + spoolSeconds + climbSeconds;
    AudioSource takeoffAudio, flightAudio;
    bool audioStarted;
    double audioStartDsp;
    Vector3 launchPosition;
    Quaternion launchRotation;

    void Awake()
    {
        // Also upgrade drones already saved in scenes before DroneDamage was introduced.
        if (!GetComponent<DroneDamage>()) gameObject.AddComponent<DroneDamage>();
    }

    void OnEnable() => ResetFlight();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BeginSession()
    {
        foreach (var drone in FindObjectsByType<DroneMapPatrol>())
            if (drone.isActiveAndEnabled) drone.ResetFlight();
    }

    public void ResetFlight()
    {
        if (!launchPoint) return;
        launchPosition = launchPoint.position;
        launchRotation = launchPoint.rotation;
        transform.SetPositionAndRotation(launchPosition, launchRotation);
        FlightTime = 0;
        TargetPoint = CompletedSweeps = 0;
        audioStarted = false;
        if (takeoffAudio) takeoffAudio.Stop();
        if (flightAudio) flightAudio.Stop();
        if (Application.isPlaying && droneCamera && Display.displays.Length > 2)
            Display.displays[2].Activate();
    }

    AudioSource MakeSource(string label, AudioClip clip, bool loop)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = clip;
        source.loop = loop;
        VehicleAudioFalloff.Configure(source, 8, 65, 120);
        source.dopplerLevel = .15f;
        source.volume = volume;
        return source;
    }

    void Update()
    {
        StepFlight(Time.deltaTime);
        if (!audioStarted && FlightTime >= launchDelay && flyingClip)
        {
            if (!takeoffAudio) takeoffAudio = MakeSource("Drone takeoff recording", flyingClip, false);
            if (!flightAudio) flightAudio = MakeSource("Drone steady flight loop", cruiseLoop, true);
            double start = AudioSettings.dspTime + .1;
            audioStartDsp = start;
            takeoffAudio.PlayScheduled(start);
            if (cruiseLoop)
            {
                takeoffAudio.SetScheduledEndTime(start + cruiseAudioStart + .25);
                flightAudio.PlayScheduled(start + cruiseAudioStart);
            }
            audioStarted = true;
        }
        // Briefly blend the steady recording in over the matching section of the original.
        if (flightAudio && takeoffAudio)
        {
            float blend = Mathf.Clamp01((float)(AudioSettings.dspTime - audioStartDsp - cruiseAudioStart) / .25f);
            takeoffAudio.volume = volume * (1 - blend);
            flightAudio.volume = volume * blend;
        }
    }

    // Shared by runtime and editor route verification; no physics or frame-rate dependent arrival tests.
    public void StepFlight(float dt)
    {
        if (dt <= 0 || !launchPoint || waypoints == null || waypoints.Length == 0) return;
        FlightTime += dt;
        float liftTime = FlightTime - launchDelay - spoolSeconds;
        if (liftTime < climbSeconds)
        {
            var hover = new Vector3(launchPosition.x, waypoints[0].position.y, launchPosition.z);
            transform.position = Vector3.Lerp(launchPosition, hover, Mathf.SmoothStep(0, 1, Mathf.Clamp01(liftTime / climbSeconds)));
            transform.rotation = launchRotation;
        }
        else
        {
            // Spend any remaining distance at the next segment, even at low frame rates.
            float distance = speed * dt;
            int guard = waypoints.Length + 1;
            while (distance > 0 && guard-- > 0)
            {
                var point = waypoints[TargetPoint];
                if (!point) break;
                Vector3 delta = point.position - transform.position;
                float length = delta.magnitude;
                if (length > .001f)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(delta.normalized, Vector3.up), turnSpeed * dt);
                    transform.position = Vector3.MoveTowards(transform.position, point.position, distance);
                }
                distance -= length;
                if (distance < 0) break;
                TargetPoint++;
                if (TargetPoint == waypoints.Length) { TargetPoint = 0; CompletedSweeps++; }
            }
        }
        float rpm = Mathf.Clamp01((FlightTime - launchDelay) / Mathf.Max(.1f, spoolSeconds));
        if (rotors != null)
            for (int i = 0; i < rotors.Length; i++)
                if (rotors[i]) rotors[i].Rotate(Vector3.up, (i % 2 == 0 ? 1 : -1) * rpm * 2100 * dt, Space.Self);
    }

    void OnDisable()
    {
        if (takeoffAudio) takeoffAudio.Stop();
        if (flightAudio) flightAudio.Stop();
        audioStarted = false;
    }

    void OnDrawGizmosSelected()
    {
        if (waypoints == null || waypoints.Length < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < waypoints.Length; i++)
            if (waypoints[i] && waypoints[(i + 1) % waypoints.Length])
                Gizmos.DrawLine(waypoints[i].position, waypoints[(i + 1) % waypoints.Length].position);
    }
}

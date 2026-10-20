using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Dedicated entrance transport, independent of patrol AI and patrol vehicles.</summary>
[DefaultExecutionOrder(-200)]
public sealed class IntroTruckSequence : MonoBehaviour
{
    [Header("Existing player and cameras")]
    public Transform player;
    public Behaviour[] playerInput;
    public Camera playerCamera;
    public Camera introCamera;
    public IntroEntranceGate gate;
    [Header("Truck route (world positions, first point is the spawn)")]
    public Transform truck;
    public Transform[] route;
    [Min(0.1f)] public float speed = 4;
    [Min(0.1f)] public float acceleration = 2;
    [Min(1)] public float turnSpeed = 60;
    [Tooltip("Route index immediately before the gate. Truck waits here for full opening.")]
    public int gateWaitPoint = 1;
    [Header("Aerial shot")]
    [Min(0.1f)] public float cutsceneDuration = 5;
    public Vector3 cameraPosition = new Vector3(24, 22, -117);
    public Vector3 cameraDrift = new Vector3(-5, 0, 5);
    public Vector3 cameraLookOffset = new Vector3(0, 1.5f, 4);
    public Vector3 followOffset = new Vector3(-2, 7, -12);
    public Vector3 shoulderOffset = new Vector3(.6f, 1.5f, -4);
    [Header("Briefing")]
    public string missionTitle = "OPERATION 1989";
    [TextArea] public string missionBriefing = "A secret is stored on a computer in the base's main building. Your mission is to steal it.";
    [TextArea] public string entryBriefing = "First, get inside. Secretly follow the supply truck through the gate. Stay out of sight and use its arrival as your opening.";
    public IntroTruckAudio truckAudio;
    [Header("Gate timing")]
    [Min(0)] public float gateOpenDelay = 1;
    [Tooltip("Maximum entry window after the camera handoff. Normally the truck's first inside right turn closes the gate sooner.")]
    [Min(1)] public float entryWindow = 18;
    public Vector3 entryPlanePoint = new Vector3(4, 0, -90);
    public Vector3 insideDirection = Vector3.forward;
    [Min(0)] public float entryDepth = 4;
    [Tooltip("Respawn offset from the gate: X across the entrance, Z into the base. Kept left of the truck route.")]
    public Vector3 respawnOffset = new Vector3(-3, 0, 12);
    [Header("Grounding and collision")]
    public LayerMask groundLayers = ~0;
    [Tooltip("Height from route root to tire contact, measured during setup.")]
    public float groundOffset;
    public Collider[] groundSurfaces;
    public Vector3 collisionCenter = new Vector3(0, 1.8f, 0);
    public Vector3 collisionHalfExtents = new Vector3(1.4f, 1.5f, 4);
    [Header("Runtime status")]
    [SerializeField] bool handedOff;
    [SerializeField] bool parked;
    [SerializeField] bool missedEntry;
    public bool HandedOff => handedOff;
    public bool Parked => parked;
    public bool MissedEntry => missedEntry;
    bool[] inputStates;
    float elapsed, windowEnd, currentSpeed;
    int targetPoint;
    bool entered;
    bool gateClosingRequested;
    public bool GateClosingRequested => gateClosingRequested;
    public float Elapsed => elapsed;
    public int TargetPoint => targetPoint;
    public string BlockedBy { get; private set; }
    Vector3 playerStart;
    Quaternion playerRotation;
    bool initialized;
    Quaternion cameraStartRotation;
    readonly IntroMissionHud missionHud = new IntroMissionHud();
    public bool MissionCapturesPointer => missionHud.CapturesPointer;
    public bool MissionVisible => missionHud.Visible;
    public bool MissionExpanded => missionHud.Expanded;
    public bool Entered => entered;
    public float CurrentSpeed => currentSpeed;
    public Vector3 RespawnPosition
    {
        get
        {
            Vector3 inward = insideDirection.normalized;
            Vector3 point = entryPlanePoint + Vector3.Cross(Vector3.up, inward) * respawnOffset.x + inward * respawnOffset.z;
            point.y = playerStart.y + respawnOffset.y;
            return point;
        }
    }
    public float HandoffPositionError { get; private set; }
    public float HandoffAngleError { get; private set; }
    static readonly Vector3[] GroundProbes =
    {
        new Vector3(-1.018f, 1.5f, -1.67f), new Vector3(1.018f, 1.5f, -1.67f),
        new Vector3(-1.018f, 1.5f, 2.668f), new Vector3(1.018f, 1.5f, 2.668f)
    };

    void Awake()
    {
        if (Initialize()) BeginSequence();
    }

    bool Initialize()
    {
        if (initialized) return true;
        if (!player || !playerCamera || !introCamera || !truck || !gate || route == null || route.Length < 2 ||
            System.Array.Exists(route, point => !point))
        {
            Debug.LogError("Intro truck setup is incomplete. Sequence disabled; player controls retained.", this);
            enabled = false;
            return false;
        }
        playerStart = player.position;
        playerRotation = player.rotation;
        cameraStartRotation = playerCamera.transform.localRotation;
        if (playerInput == null) playerInput = new Behaviour[0];
        inputStates = new bool[playerInput.Length];
        for (int i = 0; i < playerInput.Length; i++)
            inputStates[i] = playerInput[i] && playerInput[i].enabled;
        initialized = true;
        return true;
    }

    // Also runs when the editor has both domain reload and scene reload disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BeginPlaySession()
    {
        foreach (var sequence in FindObjectsByType<IntroTruckSequence>())
            if (sequence.isActiveAndEnabled && sequence.Initialize())
            {
                sequence.ResetPlayer();
                sequence.gate.ResetGate();
                sequence.BeginSequence();
            }
    }

    void BeginSequence()
    {
        elapsed = 0;
        currentSpeed = 0;
        targetPoint = 1;
        handedOff = parked = missedEntry = entered = false;
        gateClosingRequested = false;
        BlockedBy = "";
        windowEnd = float.PositiveInfinity;
        missionHud.Reset(player.position);
        if (truckAudio) truckAudio.ResetSounds();
        truck.SetPositionAndRotation(route[0].position,
            Quaternion.LookRotation((route[1].position - route[0].position).normalized, Vector3.up));
        SetInput(false);
        SwitchCamera(introCamera);
        UpdateShot();
    }

    void SetInput(bool restore)
    {
        for (int i = 0; i < playerInput.Length; i++)
            if (playerInput[i]) playerInput[i].enabled = restore && inputStates[i];
    }

    void SwitchCamera(Camera active)
    {
        playerCamera.enabled = active == playerCamera;
        introCamera.enabled = active == introCamera;
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
            listener.enabled = listener.gameObject == active.gameObject;
    }

    void Update()
    {
        missionHud.Tick(this);
        elapsed += Time.deltaTime;
        if (!handedOff && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            SkipIntro();
        if (!handedOff)
        {
            if (elapsed >= cutsceneDuration)
            {
                UpdateShot();
                HandoffPositionError = Vector3.Distance(introCamera.transform.position, playerCamera.transform.position);
                HandoffAngleError = Quaternion.Angle(introCamera.transform.rotation, playerCamera.transform.rotation);
                Handoff();
            }
        }
        if (!entered && handedOff && Vector3.Dot(player.position - entryPlanePoint, insideDirection.normalized) > entryDepth)
        {
            entered = true;
        }
        if (!parked) MoveTruck();
        // Latch the first right turn inside the entrance, independently of route point numbering.
        // The gate's swept-volume interlock still holds the leaves open for anything in their path.
        Vector3 inward = insideDirection.normalized;
        bool truckInside = Vector3.Dot(truck.position - entryPlanePoint, inward) > collisionHalfExtents.z + .5f;
        bool turningRight = Vector3.Dot(truck.forward, Vector3.Cross(Vector3.up, inward)) > .12f;
        if (handedOff && !gateClosingRequested && ((truckInside && turningRight) || elapsed >= windowEnd))
            gateClosingRequested = true;
        if (elapsed >= gateOpenDelay) gate.SetOpen(!gateClosingRequested);
        // Being in the opening during closing is still a successful attempt; fail only once shut.
        if (gateClosingRequested && gate.IsClosed) missedEntry = !entered;
        if (truckAudio) truckAudio.Tick(currentSpeed, speed, parked, Time.deltaTime);
        if (missedEntry && player.GetComponent<FirstPersonController>().enabled &&
            Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            Retry();
    }

    void Handoff()
    {
        handedOff = true;
        windowEnd = elapsed + entryWindow;
        SwitchCamera(playerCamera);
        SetInput(true);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void SkipIntro()
    {
        if (handedOff || !Initialize()) return;
        elapsed = Mathf.Max(elapsed, cutsceneDuration);
        Handoff();
    }

    public void RespawnInsideGate()
    {
        if (!Initialize()) return;
        SetInput(false);
        var controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller && controller.enabled;
        if (controller) controller.enabled = false;
        player.SetPositionAndRotation(RespawnPosition, Quaternion.LookRotation(insideDirection.normalized));
        playerCamera.transform.localRotation = cameraStartRotation;
        var movement = player.GetComponent<FirstPersonController>();
        if (movement) movement.ResetLook();
        if (controller) controller.enabled = wasEnabled;
        Physics.SyncTransforms();
        elapsed = Mathf.Max(elapsed, cutsceneDuration);
        entered = true;
        missedEntry = false;
        missionHud.Reset(player.position);
        Handoff();
    }

    void LateUpdate()
    {
        if (!handedOff) UpdateShot();
    }

    void UpdateShot()
    {
        float t = Mathf.Clamp01(elapsed / Mathf.Max(.1f, cutsceneDuration));
        Vector3 follow = truck.TransformPoint(followOffset);
        Quaternion followRotation = Quaternion.LookRotation(truck.position + cameraLookOffset - follow);
        Vector3 behind = player.TransformPoint(shoulderOffset);
        Quaternion behindRotation = Quaternion.LookRotation(playerCamera.transform.position + player.forward * 8 - behind);
        float drift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .78f, t));
        float eyes = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.8f, 1, t));
        introCamera.transform.SetPositionAndRotation(
            Vector3.Lerp(Vector3.Lerp(follow, behind, drift), playerCamera.transform.position, eyes),
            Quaternion.Slerp(Quaternion.Slerp(followRotation, behindRotation, drift), playerCamera.transform.rotation, eyes));
        introCamera.fieldOfView = Mathf.Lerp(58, playerCamera.fieldOfView, eyes);
    }

    void MoveTruck()
    {
        Vector3 destination = route[targetPoint].position;
        Vector3 delta = destination - truck.position;
        delta.y = 0;
        float distance = delta.magnitude;
        if (distance < 0.08f)
        {
            if (targetPoint == route.Length - 1) { parked = true; currentSpeed = 0; return; }
            // Do not enter until the panel has completely cleared the lane.
            if (targetPoint == gateWaitPoint && !gate.IsOpen) { currentSpeed = 0; return; }
            targetPoint++;
            return;
        }
        var desired = Quaternion.LookRotation(delta.normalized, Vector3.up);
        truck.rotation = Quaternion.RotateTowards(truck.rotation, desired, turnSpeed * Time.deltaTime);
        float stoppingSpeed = Mathf.Sqrt(2 * acceleration * distance);
        float desiredSpeed = targetPoint == route.Length - 1 || targetPoint == gateWaitPoint
            ? Mathf.Min(speed, stoppingSpeed) : speed;
        if (targetPoint < route.Length - 1)
        {
            Vector3 outgoing = route[targetPoint + 1].position - destination;
            outgoing.y = 0;
            if (Vector3.Angle(delta, outgoing) > 6) desiredSpeed = Mathf.Min(desiredSpeed, speed * .6f);
        }
        if (Quaternion.Angle(truck.rotation, desired) > 30) desiredSpeed = 0;
        float step = Mathf.Min(distance, Mathf.MoveTowards(currentSpeed, desiredSpeed,
            acceleration * Time.deltaTime) * Time.deltaTime);
        Vector3 move = delta.normalized * step;
        bool blocked = false;
        BlockedBy = "";
        foreach (var hit in Physics.BoxCastAll(truck.TransformPoint(collisionCenter), collisionHalfExtents,
                     delta.normalized, truck.rotation, step + 0.3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(truck)) continue;
            if (groundSurfaces != null && System.Array.IndexOf(groundSurfaces, hit.collider) >= 0) continue;
            if (hit.normal.y > 0.7f) continue;
            blocked = true;
            BlockedBy = hit.transform.name;
            break;
        }
        if (blocked) { currentSpeed = 0; return; }
        currentSpeed = step / Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 next = truck.position + move;
        // Probe beneath each axle rather than snapping onto the truck or nearby props.
        float rearGround = 0, frontGround = 0;
        int rearContacts = 0, frontContacts = 0;
        Quaternion yaw = Quaternion.Euler(0, truck.eulerAngles.y, 0);
        for (int probeIndex = 0; probeIndex < GroundProbes.Length; probeIndex++)
        {
            Vector3 origin = next + yaw * GroundProbes[probeIndex];
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 4, groundLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(truck) && hit.normal.y > 0.8f &&
                    (groundSurfaces == null || groundSurfaces.Length == 0 || System.Array.IndexOf(groundSurfaces, hit.collider) >= 0) &&
                    Mathf.Abs(hit.point.y + groundOffset - next.y) < 0.6f)
                    best = Mathf.Max(best, hit.point.y);
            if (!float.IsNegativeInfinity(best))
            {
                if (probeIndex < 2) { rearGround += best; rearContacts++; }
                else { frontGround += best; frontContacts++; }
            }
        }
        if (rearContacts > 0 && frontContacts > 0)
        {
            rearGround /= rearContacts;
            frontGround /= frontContacts;
            float wheelbase = GroundProbes[2].z - GroundProbes[0].z;
            next.y = Mathf.Lerp(rearGround, frontGround, -GroundProbes[0].z / wheelbase) + groundOffset;
            Vector3 forward = yaw * Vector3.forward + Vector3.up * ((frontGround - rearGround) / wheelbase);
            truck.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
        else if (rearContacts + frontContacts > 0)
            next.y = (rearGround + frontGround) / (rearContacts + frontContacts) + groundOffset;
        truck.position = next;
    }

    public void Retry()
    {
        // Reset only intro-owned state; agents and all other gameplay state remain untouched.
        SetInput(false);
        ResetPlayer();
        BeginSequence();
        gate.ResetGate();
    }

    void ResetPlayer()
    {
        var controller = player.GetComponent<CharacterController>();
        bool controllerEnabled = controller && controller.enabled;
        if (controller) controller.enabled = false;
        player.SetPositionAndRotation(playerStart, playerRotation);
        playerCamera.transform.localRotation = cameraStartRotation;
        if (controller) controller.enabled = controllerEnabled;
    }

    void OnDisable()
    {
        missionHud.ReleaseCursor();
        if (inputStates == null) return;
        SetInput(true);
        if (playerCamera && introCamera) SwitchCamera(playerCamera);
        if (truckAudio) truckAudio.StopSounds();
    }

    void OnGUI()
    {
        var health = player ? player.GetComponent<CombatHealth>() : null;
        if (!health || !health.Dead) missionHud.Draw(this);
    }

    void OnDrawGizmosSelected()
    {
        if (route == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < route.Length; i++)
            if (route[i])
            {
                Gizmos.DrawWireSphere(route[i].position, 0.5f);
                if (i > 0 && route[i - 1]) Gizmos.DrawLine(route[i - 1].position, route[i].position);
            }
    }
}

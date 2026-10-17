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
    [Header("Gate timing")]
    [Min(0)] public float gateOpenDelay = 1;
    [Min(1)] public float entryWindow = 18;
    public Vector3 entryPlanePoint = new Vector3(4, 0, -90);
    public Vector3 insideDirection = Vector3.forward;
    [Min(0)] public float entryDepth = 4;
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
    public float Elapsed => elapsed;
    public int TargetPoint => targetPoint;
    public string BlockedBy { get; private set; }
    Vector3 playerStart;
    Quaternion playerRotation;
    bool initialized;
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
        BlockedBy = "";
        windowEnd = float.PositiveInfinity;
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
        elapsed += Time.deltaTime;
        if (elapsed >= gateOpenDelay && elapsed < windowEnd) gate.SetOpen(true);
        if (!handedOff)
        {
            UpdateShot();
            if (elapsed >= cutsceneDuration)
            {
                handedOff = true;
                windowEnd = elapsed + entryWindow;
                SwitchCamera(playerCamera);
                SetInput(true);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        entered |= Vector3.Dot(player.position - entryPlanePoint, insideDirection.normalized) > entryDepth;
        if (handedOff && elapsed >= windowEnd)
        {
            bool truckClear = Vector3.Dot(truck.position - entryPlanePoint, insideDirection.normalized) >
                collisionHalfExtents.z + entryDepth;
            gate.SetOpen(!truckClear);
            missedEntry = !entered;
        }
        if (!parked) MoveTruck();
        if (missedEntry && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            Retry();
    }

    void UpdateShot()
    {
        introCamera.transform.position = cameraPosition + cameraDrift * Mathf.SmoothStep(0, 1,
            Mathf.Clamp01(elapsed / Mathf.Max(0.1f, cutsceneDuration)));
        introCamera.transform.LookAt(truck.position + cameraLookOffset);
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
        if (controller) controller.enabled = controllerEnabled;
    }

    void OnDisable()
    {
        if (inputStates == null) return;
        SetInput(true);
        if (playerCamera && introCamera) SwitchCamera(playerCamera);
    }

    void OnGUI()
    {
        if (!handedOff) return;
        string message = missedEntry ? "Entry window missed — press R to retry the truck intro" :
            !entered ? $"Follow the truck through the gate   {Mathf.CeilToInt(Mathf.Max(0, windowEnd - elapsed))}s" : "";
        if (message.Length == 0) return;
        GUI.Box(new Rect(Screen.width / 2f - 245, Screen.height - 90, 490, 45), message);
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

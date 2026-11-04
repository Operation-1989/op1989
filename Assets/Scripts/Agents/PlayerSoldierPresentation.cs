using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>Visuals and animation for the existing first person controller; no AI or root motion.</summary>
[DefaultExecutionOrder(-20)]
[RequireComponent(typeof(FirstPersonController))]
public sealed class PlayerSoldierPresentation : MonoBehaviour
{
    public FirstPersonController movement;
    public Animator animator;
    public Camera firstPersonCamera;
    public SkinnedMeshRenderer body;
    public Mesh fullBodyMesh;
    public Mesh firstPersonMesh;
    public RifleWeapon rifle;
    [Min(0.1f)] public float walkSpeed = 4f;
    [Min(0.1f)] public float runSpeed = 7f;
    [Min(0.05f)] public float fireInterval = 0.18f;

    [Header("Scope")]
    public bool toggleScope;
    [Range(5f, 60f)] public float scopedFieldOfView = 20f;
    [Min(0.01f)] public float scopeTransitionSeconds = 0.15f;
    [Min(0.1f)] public float scopedWalkSpeed = 2f;
    [Range(0.1f, 1f)] public float scopedSensitivity = 0.8f;
    public bool IsScoped => scopeRequested && CanAim;
    bool CanAim => movement && movement.isActiveAndEnabled && rifle && rifle.isActiveAndEnabled &&
        firstPersonCamera && firstPersonCamera.isActiveAndEnabled &&
        Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0 &&
        (!health || !health.Dead) && (!intro || !intro.MissionCapturesPointer) &&
        !(Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);

    static readonly int Speed = Animator.StringToHash("Speed");
    static readonly int Fire = Animator.StringToHash("Fire");
    Vector3 previousPosition;
    float nextFire;
    bool sprinting;
    bool scopeRequested, bodyWasHidden;
    float normalFieldOfView, scopeBlend;
    CombatHealth health;
    IntroTruckSequence intro;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
        previousPosition = transform.position;
        nextFire = 0;
        if (!movement) movement = GetComponent<FirstPersonController>();
        health = GetComponent<CombatHealth>();
        intro = FindAnyObjectByType<IntroTruckSequence>();
        if (firstPersonCamera) normalFieldOfView = firstPersonCamera.fieldOfView;
        scopeRequested = false;
        scopeBlend = 0;
    }

    void Update()
    {
        bool controlled = movement && movement.isActiveAndEnabled;
        if (!CanAim || Mouse.current == null) scopeRequested = false;
        else if (toggleScope)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame) scopeRequested = !scopeRequested;
        }
        else scopeRequested = Mouse.current.rightButton.isPressed;

        scopeBlend = CanAim ? Mathf.MoveTowards(scopeBlend, IsScoped ? 1f : 0f,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, scopeTransitionSeconds)) : 0f;
        if (firstPersonCamera)
        {
            firstPersonCamera.fieldOfView = Mathf.Lerp(normalFieldOfView,
                Mathf.Min(normalFieldOfView, scopedFieldOfView), scopeBlend);
            // Match mouse movement to the magnification so small targets remain easy to track.
            float zoomRatio = Mathf.Tan(firstPersonCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) /
                Mathf.Tan(normalFieldOfView * Mathf.Deg2Rad * 0.5f);
            if (movement) movement.LookSensitivityMultiplier = Mathf.Lerp(1f, zoomRatio * scopedSensitivity, scopeBlend);
        }
        sprinting = controlled && !IsScoped && Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        if (movement) movement.moveSpeed = IsScoped ? scopedWalkSpeed : sprinting ? runSpeed : walkSpeed;
    }

    void FireWeapon()
    {
        if (CanAim &&
            Mouse.current != null && Mouse.current.leftButton.isPressed && Time.time >= nextFire &&
            (!health || !health.Dead))
        {
            if (animator) animator.SetTrigger(Fire);
            Ray aim = firstPersonCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            rifle.Shoot(firstPersonCamera.transform.position, aim.direction);
            nextFire = Time.time + fireInterval;
        }
    }

    void LateUpdate()
    {
        // Mouse look runs in Update; shoot using this frame's final camera direction.
        FireWeapon();
        Vector3 delta = transform.position - previousPosition;
        delta.y = 0;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        // Ignore retry teleports, and animate only movement that actually occurred.
        bool moving = movement && movement.isActiveAndEnabled && speed > 0.1f && delta.magnitude < 2f;
        if (animator) animator.SetFloat(Speed, moving ? (sprinting ? 2f : 1f) : 0f, 0.1f, Time.deltaTime);
        previousPosition = transform.position;
    }

    void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (body)
        {
            bodyWasHidden = body.forceRenderingOff;
            if (camera == firstPersonCamera && IsScoped) body.forceRenderingOff = true;
        }
        if (body && fullBodyMesh && firstPersonMesh)
            body.sharedMesh = camera == firstPersonCamera ||
                (firstPersonCamera && Vector3.Distance(camera.transform.position, firstPersonCamera.transform.position) < .65f)
                ? firstPersonMesh : fullBodyMesh;
    }

    void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (body) body.forceRenderingOff = bodyWasHidden;
        if (body && fullBodyMesh) body.sharedMesh = fullBodyMesh;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        if (body && fullBodyMesh) body.sharedMesh = fullBodyMesh;
        scopeRequested = false;
        scopeBlend = 0;
        if (firstPersonCamera) firstPersonCamera.fieldOfView = normalFieldOfView;
        if (movement) movement.LookSensitivityMultiplier = 1f;
    }
}

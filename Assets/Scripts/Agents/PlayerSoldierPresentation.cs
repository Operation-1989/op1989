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
    [Min(0.1f)] public float walkSpeed = 4f;
    [Min(0.1f)] public float runSpeed = 7f;
    [Min(0.05f)] public float fireInterval = 0.18f;

    static readonly int Speed = Animator.StringToHash("Speed");
    static readonly int Fire = Animator.StringToHash("Fire");
    Vector3 previousPosition;
    float nextFire;
    bool sprinting;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
        previousPosition = transform.position;
        nextFire = 0;
        if (!movement) movement = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        bool controlled = movement && movement.isActiveAndEnabled;
        sprinting = controlled && Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        if (movement) movement.moveSpeed = sprinting ? runSpeed : walkSpeed;
        if (controlled && animator && Cursor.lockState == CursorLockMode.Locked &&
            Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Time.time >= nextFire)
        {
            // Presentation only: this does not spawn projectiles or apply damage.
            animator.SetTrigger(Fire);
            nextFire = Time.time + fireInterval;
        }
    }

    void LateUpdate()
    {
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
        if (body && fullBodyMesh && firstPersonMesh)
            body.sharedMesh = camera == firstPersonCamera ||
                (firstPersonCamera && Vector3.Distance(camera.transform.position, firstPersonCamera.transform.position) < .65f)
                ? firstPersonMesh : fullBodyMesh;
    }

    void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (body && fullBodyMesh) body.sharedMesh = fullBodyMesh;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        if (body && fullBodyMesh) body.sharedMesh = fullBodyMesh;
    }
}

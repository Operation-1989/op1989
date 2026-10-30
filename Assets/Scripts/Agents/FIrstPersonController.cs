
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float gravity = -9.81f;

    [Header("Ladder Movement")]
    [Tooltip("Vertical speed while the player is inside a LadderClimbZone.")]
    public float ladderClimbSpeed = 3.5f;
    [Tooltip("Sideways movement used to step off a ladder.")]
    public float ladderExitSpeed = 2.5f;

    [Header("Mouse Look")]
    public Transform playerCamera;
    public float mouseSensitivity = 0.15f;
    public float maxLookAngle = 80f;
    public float LookSensitivityMultiplier { get; set; } = 1f;

    private CharacterController controller;
    private float verticalRotation;
    private float verticalVelocity;
    private IntroTruckSequence introSequence;
    private Operation1989.Interaction.Environment.LadderClimbZone currentLadder;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        introSequence = FindAnyObjectByType<IntroTruckSequence>();

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main.transform;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleMouseLook();
        HandleMovement();

        // Mission buttons and their close shortcut own this click; do not recapture the cursor.
        if (introSequence && introSequence.MissionCapturesPointer) return;

        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void ResetLook()
    {
        verticalRotation = 0;
        verticalVelocity = 0;
        if (playerCamera) playerCamera.localRotation = Quaternion.identity;
    }

    void HandleMouseLook()
    {
        if (Mouse.current == null ||
            Cursor.lockState != CursorLockMode.Locked ||
            playerCamera == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * LookSensitivityMultiplier;

        transform.Rotate(Vector3.up * mouseDelta.x * mouseSensitivity);

        verticalRotation -= mouseDelta.y * mouseSensitivity;
        verticalRotation = Mathf.Clamp(
            verticalRotation, -maxLookAngle, maxLookAngle
        );

        playerCamera.localRotation =
            Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    void HandleMovement()
    {
        if (Keyboard.current == null)
            return;

        // CharacterController trigger callbacks are not reliable on every imported
        // ladder prefab, so also query the climb volumes directly each frame.
        currentLadder = FindNearbyLadder();

        Vector2 input = Vector2.zero;

        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;

        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 moveDirection =
            transform.forward * input.y +
            transform.right * input.x;

        if (currentLadder != null)
        {
            // W/S climb. A/D lets the player step away from the ladder; leaving the
            // trigger naturally restores normal gravity and movement.
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                currentLadder = null;
                verticalVelocity = 0f;
                controller.Move(transform.forward * ladderExitSpeed * Time.deltaTime);
                return;
            }

            verticalVelocity = 0f;
            if (input.y > 0f && currentLadder.ShouldExitAtTop(transform.position))
            {
                Vector3 dismountVelocity = currentLadder.GetTopExitDirection(transform.position) * ladderExitSpeed +
                                            Vector3.up * (ladderClimbSpeed * 0.35f);
                controller.Move(dismountVelocity * Time.deltaTime);
                return;
            }

            Vector3 ladderVelocity = Vector3.up * input.y * ladderClimbSpeed +
                                     transform.right * input.x * ladderExitSpeed;
            controller.Move(ladderVelocity * Time.deltaTime);
            return;
        }

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity =
            moveDirection * moveSpeed +
            Vector3.up * verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        var ladder = other.GetComponentInParent<Operation1989.Interaction.Environment.LadderClimbZone>();
        if (ladder != null)
            currentLadder = ladder;
    }

    private void OnTriggerExit(Collider other)
    {
        var ladder = other.GetComponentInParent<Operation1989.Interaction.Environment.LadderClimbZone>();
        if (ladder != null && ladder == currentLadder)
            currentLadder = null;
    }

    private Operation1989.Interaction.Environment.LadderClimbZone FindNearbyLadder()
    {
        Vector3 center = transform.TransformPoint(controller.center);
        float halfHeight = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 bottom = center - Vector3.up * halfHeight;
        Vector3 top = center + Vector3.up * halfHeight;

        foreach (var hit in Physics.OverlapCapsule(bottom, top, controller.radius + 0.15f,
                     ~0, QueryTriggerInteraction.Collide))
        {
            var ladder = hit.GetComponentInParent<Operation1989.Interaction.Environment.LadderClimbZone>();
            if (ladder != null)
                return ladder;
        }

        return null;
    }
}

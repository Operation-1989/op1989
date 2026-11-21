using UnityEngine;
using UnityEngine.InputSystem;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Interactive First-Person Controller for test scenes.
    /// Provides WASD exploration, smooth mouse look, cursor locking, and left-click rifle shooting.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public class TestPlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float moveSpeed = 6.0f;
        public float mouseSensitivity = 0.15f;
        public float gravity = -18.0f;

        [Header("Camera & Weapon")]
        public Camera playerCamera;
        public RifleWeapon rifle;

        private CharacterController controller;
        private float verticalRotation = 0f;
        private float verticalVelocity = 0f;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (!rifle) rifle = GetComponent<RifleWeapon>();
            if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            HandleCursorLock();
            HandleMouseLook();
            HandleMovement();
            HandleWeaponFiring();
        }

        private void HandleCursorLock()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void HandleMouseLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked || Mouse.current == null || !playerCamera) return;

            Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(Vector3.up * mouseDelta.x);

            verticalRotation -= mouseDelta.y;
            verticalRotation = Mathf.Clamp(verticalRotation, -85f, 85f);
            playerCamera.transform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
        }

        private void HandleMovement()
        {
            float h = 0f;
            float v = 0f;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) v += 1f;
                if (Keyboard.current.sKey.isPressed) v -= 1f;
                if (Keyboard.current.aKey.isPressed) h -= 1f;
                if (Keyboard.current.dKey.isPressed) h += 1f;
            }

            Vector3 move = (transform.forward * v + transform.right * h).normalized * moveSpeed;

            if (controller.isGrounded)
            {
                verticalVelocity = -0.5f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            move.y = verticalVelocity;
            controller.Move(move * Time.deltaTime);
        }

        private void HandleWeaponFiring()
        {
            if (Cursor.lockState != CursorLockMode.Locked || Mouse.current == null || !rifle || !playerCamera) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                rifle.Shoot(playerCamera.transform.position, playerCamera.transform.forward);
            }
        }

        private void OnGUI()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(Screen.width * 0.5f - 100, Screen.height * 0.5f - 40, 200, 30), "Click Game View to Play / Look");
                return;
            }

            // Simple center dot crosshair
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Box(new Rect(cx - 3, cy - 3, 6, 6), GUIContent.none);
        }
    }
}

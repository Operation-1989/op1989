using UnityEngine;
using UnityEngine.InputSystem;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Interactive First-Person Controller for test scenes.
    /// Provides WASD exploration, smooth mouse look, cursor locking, left-click rifle shooting,
    /// health management, [R] respawn, and [G] godmode toggle for testing.
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

        [Header("Testing Controls")]
        public bool godmode = false;

        private CharacterController controller;
        private CombatHealth health;
        private float verticalRotation = 0f;
        private float verticalVelocity = 0f;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<CombatHealth>();
            if (health)
            {
                health.isPlayer = true;
                health.maximumHealth = 500f;
                health.regeneratePlayerHealth = true;
            }
            if (!rifle) rifle = GetComponent<RifleWeapon>();
            if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();

            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            // Godmode protection
            if (health)
            {
                health.CinematicProtection = godmode;
            }

            // [G] to toggle Godmode
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            {
                godmode = !godmode;
                Debug.Log($"[Test Player] Godmode: {(godmode ? "ENABLED (Invulnerable)" : "DISABLED")}");
            }

            // Handle Death & Respawn
            if (health && health.Dead)
            {
                if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                {
                    Respawn();
                }
                return;
            }

            HandleCursorLock();
            HandleMouseLook();
            HandleMovement();
            HandleWeaponFiring();
        }

        public void Respawn()
        {
            if (controller) controller.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            verticalRotation = 0f;
            verticalVelocity = 0f;
            if (playerCamera) playerCamera.transform.localRotation = Quaternion.identity;
            if (controller) controller.enabled = true;

            if (health)
            {
                health.Restore();
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log("[Test Player] Respawned at base standing upright.");
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
            // Death Screen Overlay
            if (health && health.Dead)
            {
                GUI.color = Color.red;
                GUI.Box(new Rect(Screen.width * 0.5f - 160, Screen.height * 0.5f - 40, 320, 80), GUIContent.none);
                GUI.color = Color.white;
                GUI.Label(new Rect(Screen.width * 0.5f - 140, Screen.height * 0.5f - 30, 280, 25), "<b>ELIMINATED BY TACTICAL DRONE</b>");
                GUI.Label(new Rect(Screen.width * 0.5f - 140, Screen.height * 0.5f - 5, 280, 25), "Press <b>[R]</b> to Respawn upright");
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(Screen.width * 0.5f - 120, Screen.height * 0.5f - 40, 240, 30), "Click Game View to Play | [G] Godmode");
                return;
            }

            // Crosshair
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Box(new Rect(cx - 3, cy - 3, 6, 6), GUIContent.none);

            // Bottom-right health bar & Godmode status
            if (health)
            {
                GUI.color = godmode ? Color.cyan : (health.Current > 30f ? Color.green : Color.red);
                string hpText = godmode ? "HP: [GODMODE ACTIVE]" : $"HP: {health.Current:F0} / {health.maximumHealth:F0} | [G] Toggle Godmode";
                GUI.Label(new Rect(Screen.width - 280, Screen.height - 35, 260, 25), hpText);
            }
        }
    }
}

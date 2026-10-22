using UnityEngine;
using UnityEngine.InputSystem;

namespace Operation1989.Interaction
{
    /// <summary>
    /// Attached to Player Camera. Performs spherecasting to detect IInteractable targets
    /// and triggers interactions upon input key press.
    /// </summary>
    public class PlayerInteractionManager : MonoBehaviour
    {
        [Header("Detection Parameters")]
        [SerializeField] private float interactDistance = 3.0f;
        [SerializeField] private float sphereCastRadius = 0.25f;
        [SerializeField] private LayerMask interactableLayers = ~0; // Default to all layers

        [Header("Input Config")]
        [SerializeField] private KeyCode interactKey = KeyCode.E;

        private Camera playerCamera;
        private IInteractable currentInteractable;

        private void Start()
        {
            playerCamera = GetComponent<Camera>();
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        private void Update()
        {
            CheckForInteractable();
            HandleInput();
        }

        private void CheckForInteractable()
        {
            Ray ray = playerCamera != null
                ? new Ray(playerCamera.transform.position, playerCamera.transform.forward)
                : new Ray(transform.position, transform.forward);

            IInteractable detected = null;

            if (Physics.SphereCast(ray, sphereCastRadius, out RaycastHit hit, interactDistance, interactableLayers))
            {
                detected = hit.collider.GetComponentInParent<IInteractable>();
            }

            if (currentInteractable != detected)
            {
                if (currentInteractable != null)
                {
                    currentInteractable.OnHoverExit();
                    if (InteractionUI.Instance != null)
                        InteractionUI.Instance.HidePrompt();
                }

                currentInteractable = detected;

                if (currentInteractable != null)
                {
                    currentInteractable.OnHoverEnter();
                    if (InteractionUI.Instance != null)
                        InteractionUI.Instance.ShowPrompt(currentInteractable.InteractionPrompt);
                }
            }
        }

        private void HandleInput()
        {
            if (WasInteractPressed() && currentInteractable != null)
            {
                currentInteractable.Interact(transform);
            }
        }

        // This project uses the new Input System only. Keep the existing KeyCode
        // field so current prefabs stay compatible, but read it through Keyboard.
        private bool WasInteractPressed()
        {
            if (Keyboard.current == null) return false;

            return interactKey switch
            {
                KeyCode.E => Keyboard.current.eKey.wasPressedThisFrame,
                KeyCode.F => Keyboard.current.fKey.wasPressedThisFrame,
                KeyCode.Q => Keyboard.current.qKey.wasPressedThisFrame,
                _ => Keyboard.current.eKey.wasPressedThisFrame
            };
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 origin = transform.position;
            Vector3 direction = transform.forward;
            Gizmos.DrawRay(origin, direction * interactDistance);
            Gizmos.DrawWireSphere(origin + direction * interactDistance, sphereCastRadius);
        }
    }
}

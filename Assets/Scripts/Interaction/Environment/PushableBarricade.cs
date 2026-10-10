using UnityEngine;

namespace Operation1989.Interaction.Environment
{
    /// <summary>
    /// Attached to barricades, sandbags, and obstacles. Allows player to push or drag
    /// physical obstacles to create dynamic cover during stealth operations.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PushableBarricade : MonoBehaviour, IInteractable
    {
        [Header("Physics Settings")]
        [SerializeField] private float pushForceMultiplier = 5.0f;
        [SerializeField] private bool freezeRotationOnPush = true;
        [SerializeField] private string promptText = "[Hold E] Grab / Push Barricade";

        private Rigidbody rb;
        private bool isBeingHeld = false;
        private Transform currentInteractor;

        public string InteractionPrompt => promptText;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (freezeRotationOnPush)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }
        }

        public void OnHoverEnter() { }

        public void OnHoverExit() { }

        public void Interact(Transform interactor)
        {
            isBeingHeld = !isBeingHeld;
            currentInteractor = isBeingHeld ? interactor : null;

            if (isBeingHeld)
            {
                rb.drag = 5.0f;
            }
            else
            {
                rb.drag = 0.5f;
            }
        }

        private void FixedUpdate()
        {
            if (isBeingHeld && currentInteractor != null)
            {
                Vector3 moveDirection = currentInteractor.forward;
                moveDirection.y = 0; // Keep horizontal push
                rb.AddForce(moveDirection.normalized * pushForceMultiplier, ForceMode.Force);
            }
        }

        /// <summary>
        /// Allows direct passive pushing when player collides into the barricade.
        /// </summary>
        public void ApplyDirectPush(Vector3 pushDirection, float force)
        {
            if (rb != null && !rb.isKinematic)
            {
                pushDirection.y = 0;
                rb.AddForce(pushDirection.normalized * force, ForceMode.Impulse);
            }
        }
    }
}

using UnityEngine;

namespace Operation1989.Interaction.Core
{
    /// <summary>
    /// Attached to objects that can be picked up, carried, and thrown by the player.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PickableObject : MonoBehaviour, IInteractable
    {
        [Header("Pickable Settings")]
        [SerializeField] private string promptText = "[E] Pick Up Object";
        [SerializeField] private bool isObjectiveItem = false;
        [SerializeField] private string objectiveName = "Secret Intel Laptop";

        private Rigidbody rb;

        public string InteractionPrompt => promptText;
        public bool IsObjectiveItem => isObjectiveItem;
        public string ObjectiveName => objectiveName;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        public void OnHoverEnter() { }

        public void OnHoverExit() { }

        public void Interact(Transform interactor)
        {
            PhysicsGrabber grabber = interactor.GetComponentInParent<PhysicsGrabber>();
            if (grabber != null)
            {
                grabber.ToggleGrab(this);
            }
        }

        public void OnGrab(Transform holdPoint)
        {
            rb.useGravity = false;
            rb.linearDamping = 10f;
            rb.angularDamping = 5f;
        }

        public void OnRelease(Vector3 throwForce)
        {
            rb.useGravity = true;
            rb.linearDamping = 1f;
            rb.angularDamping = 0.05f;
            rb.AddForce(throwForce, ForceMode.Impulse);
        }
    }
}

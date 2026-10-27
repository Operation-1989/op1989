using UnityEngine;
using UnityEngine.InputSystem;

namespace Operation1989.Interaction.Core
{
    /// <summary>
    /// Handles physical object carrying and throwing mechanics.
    /// Attached to the Player Camera.
    /// </summary>
    public class PhysicsGrabber : MonoBehaviour
    {
        [Header("Hold Parameters")]
        [SerializeField] private Transform holdPoint;
        [SerializeField] private float holdDistance = 2.0f;
        [SerializeField] private float moveSpeed = 15.0f;
        [SerializeField] private float throwForce = 12.0f;

        [Header("Controls")]
        [SerializeField] private KeyCode throwKey = KeyCode.Mouse0;
        [SerializeField] private KeyCode dropKey = KeyCode.G;

        private PickableObject heldObject;
        private Rigidbody heldRigidbody;

        private void Start()
        {
            if (holdPoint == null)
            {
                GameObject defaultHold = new GameObject("HoldPoint");
                defaultHold.transform.SetParent(transform);
                defaultHold.transform.localPosition = new Vector3(0, -0.2f, holdDistance);
                holdPoint = defaultHold.transform;
            }
        }

        public void ToggleGrab(PickableObject target)
        {
            if (heldObject == null)
            {
                GrabObject(target);
            }
            else
            {
                DropObject();
            }
        }

        private void GrabObject(PickableObject target)
        {
            heldObject = target;
            heldRigidbody = target.GetComponent<Rigidbody>();
            heldObject.OnGrab(holdPoint);

            if (target.IsObjectiveItem)
            {
                Debug.Log($"[Mission System] Secret Intel Captured: {target.ObjectiveName}");
            }
        }

        public void DropObject()
        {
            if (heldObject == null) return;

            heldObject.OnRelease(Vector3.zero);
            heldObject = null;
            heldRigidbody = null;
        }

        public void ThrowObject()
        {
            if (heldObject == null) return;

            Vector3 launchVelocity = transform.forward * throwForce;
            heldObject.OnRelease(launchVelocity);

            heldObject = null;
            heldRigidbody = null;
        }

        private void Update()
        {
            if (heldObject != null)
            {
                if (WasPressedThisFrame(throwKey))
                {
                    ThrowObject();
                }
                else if (WasPressedThisFrame(dropKey))
                {
                    DropObject();
                }
            }
        }

        private static bool WasPressedThisFrame(KeyCode key)
        {
            if (key == KeyCode.Mouse0)
                return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

            if (Keyboard.current == null) return false;
            return key switch
            {
                KeyCode.G => Keyboard.current.gKey.wasPressedThisFrame,
                KeyCode.E => Keyboard.current.eKey.wasPressedThisFrame,
                KeyCode.F => Keyboard.current.fKey.wasPressedThisFrame,
                _ => false
            };
        }

        private void FixedUpdate()
        {
            if (heldRigidbody != null && holdPoint != null)
            {
                Vector3 moveDir = holdPoint.position - heldRigidbody.position;
                heldRigidbody.linearVelocity = moveDir * moveSpeed;
            }
        }
    }
}

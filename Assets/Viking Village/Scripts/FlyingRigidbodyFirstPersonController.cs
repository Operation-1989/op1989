using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

[RequireComponent(typeof(RigidbodyFirstPersonController))]
public class FlyingRigidbodyFirstPersonController : MonoBehaviour
{

    private new Rigidbody rigidbody;
    private RigidbodyFirstPersonController rigidbodyFPC;
    private HeadBob headBob;

    public bool flying = false;
    public float flyingDrag = 5f;
    public float flightToggleTimeThreshold = 0.5f;
    private float lastAscendKeyHit = float.MinValue;

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        rigidbodyFPC = GetComponent<RigidbodyFirstPersonController>();
        headBob = GetComponentInChildren<HeadBob>();
    }

    void Update()
    {
        if (AscendKeyDoubleHit())
        {
            flying = !flying;
            rigidbody.useGravity = !flying;
            rigidbodyFPC.enabled = !flying;
            headBob.enabled = !flying;

            if (flying)
            {
                rigidbody.linearDamping = flyingDrag;
            }
        }

        if (flying)
        {
            Vector2 lookInput = rigidbodyFPC.Actions.Player.Look.ReadValue<Vector2>();
            rigidbodyFPC.mouseLook.LookRotation(transform, rigidbodyFPC.cam.transform, lookInput);
        }
    }

    void FixedUpdate()
    {
        if (flying)
        {
            Vector2 input = GetInput();
            bool ascend = rigidbodyFPC.Actions.Player.Jump.IsPressed();
            bool descend = rigidbodyFPC.Actions.Player.Crouch.IsPressed();
            Vector3 verticalInput = Vector3.up * ((ascend ? 1f : 0f) - (descend ? 1f : 0f));

            if ((Mathf.Abs(input.x) > float.Epsilon || Mathf.Abs(input.y) > float.Epsilon) || Mathf.Abs(verticalInput.y) > float.Epsilon)
            {
                Vector3 desiredMove = rigidbodyFPC.cam.transform.forward * input.y + rigidbodyFPC.cam.transform.right * input.x;
                desiredMove += verticalInput;
                desiredMove = desiredMove.normalized * rigidbodyFPC.movementSettings.CurrentTargetSpeed;
                if (rigidbodyFPC.Velocity.sqrMagnitude <
                    (rigidbodyFPC.movementSettings.CurrentTargetSpeed * rigidbodyFPC.movementSettings.CurrentTargetSpeed))
                {
                    rigidbody.AddForce(desiredMove, ForceMode.Impulse);
                }
            }
        }
    }

    private Vector2 GetInput()
    {
        Vector2 input = rigidbodyFPC.Actions.Player.Move.ReadValue<Vector2>();
        bool running = rigidbodyFPC.Actions.Player.Sprint.IsPressed();
        rigidbodyFPC.movementSettings.UpdateDesiredTargetSpeed(input, running);
        return input;
    }

    private bool AscendKeyDoubleHit()
    {
        bool result = false;
        if (rigidbodyFPC.Actions.Player.Jump.WasPressedThisFrame())
        {
            result = Time.time - lastAscendKeyHit < flightToggleTimeThreshold;
            lastAscendKeyHit = Time.time;
        }
        return result;
    }
}

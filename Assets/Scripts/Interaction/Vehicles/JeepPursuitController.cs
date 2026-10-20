using System.Collections;
using UnityEngine;
using Operation1989.Interaction.AI;
using Operation1989.Interaction.Environment;

namespace Operation1989.Interaction.Vehicles
{
    /// <summary>
    /// Controls the Military Jeep pursuit physics.
    /// Responds to DroneAlertSystem broadcasts and applies physical drive forces
    /// to pursue the player and smash through pushable barricades.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class JeepPursuitController : MonoBehaviour
    {
        public enum JeepState
        {
            Idle,
            Patrol,
            Pursuit,
            Searching
        }

        [Header("Physics Driving Parameters")]
        [SerializeField] private float pursuitSpeed = 14.0f;
        [SerializeField] private float accelerationForce = 6000.0f;
        [SerializeField] private float turnTorque = 4000.0f;
        [SerializeField] private float stopDistance = 3.5f;
        [SerializeField] private float barricadeImpactMultiplier = 2.5f;

        [Header("State")]
        [SerializeField] private JeepState currentState = JeepState.Idle;
        [SerializeField] private Vector3 targetPosition;
        [SerializeField] private bool hasActiveTarget = false;

        private Rigidbody rb;
        private Coroutine alertResponseCoroutine;

        public JeepState CurrentState => currentState;
        public bool HasActiveTarget => hasActiveTarget;
        public Vector3 TargetPosition => targetPosition;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.mass = 1500.0f; // Exact specification from Master Plan
            rb.centerOfMass = new Vector3(0, -0.5f, 0); // Low center of mass for stability
        }

        private void OnEnable()
        {
            if (DroneAlertSystem.Instance != null)
            {
                DroneAlertSystem.Instance.OnPlayerSpottedByDrone += HandleDroneAlert;
                DroneAlertSystem.Instance.OnPlayerLostByDrone += HandleDroneAlertCleared;
            }
        }

        private void Start()
        {
            // Fallback subscription if DroneAlertSystem initialized later in Awake
            if (DroneAlertSystem.Instance != null)
            {
                DroneAlertSystem.Instance.OnPlayerSpottedByDrone -= HandleDroneAlert;
                DroneAlertSystem.Instance.OnPlayerSpottedByDrone += HandleDroneAlert;
                DroneAlertSystem.Instance.OnPlayerLostByDrone -= HandleDroneAlertCleared;
                DroneAlertSystem.Instance.OnPlayerLostByDrone += HandleDroneAlertCleared;
            }
        }

        private void OnDisable()
        {
            if (DroneAlertSystem.Instance != null)
            {
                DroneAlertSystem.Instance.OnPlayerSpottedByDrone -= HandleDroneAlert;
                DroneAlertSystem.Instance.OnPlayerLostByDrone -= HandleDroneAlertCleared;
            }
        }

        private void HandleDroneAlert(Vector3 spottedCoordinates)
        {
            targetPosition = spottedCoordinates;
            hasActiveTarget = true;
            currentState = JeepState.Pursuit;

            Debug.Log($"[Military Jeep] 🚨 Alert received from Surveillance Drone! Dispatching to {spottedCoordinates}");
        }

        private void HandleDroneAlertCleared()
        {
            if (currentState == JeepState.Pursuit)
            {
                currentState = JeepState.Searching;
                Debug.Log("[Military Jeep] ⚠️ Target lost from drone optics. Transitioning to search mode at last known coordinates.");
            }
        }

        private void FixedUpdate()
        {
            if (currentState == JeepState.Pursuit && hasActiveTarget)
            {
                DriveTowardsTarget(targetPosition);
            }
        }

        private void DriveTowardsTarget(Vector3 destination)
        {
            Vector3 directionToTarget = destination - transform.position;
            directionToTarget.y = 0; // Maintain planar ground movement

            float distance = directionToTarget.magnitude;
            if (distance <= stopDistance)
            {
                // Reached target area
                rb.linearDamping = 2.0f;
                return;
            }

            rb.linearDamping = 0.2f;
            Vector3 normalizedDirection = directionToTarget.normalized;

            // Turn towards target
            Vector3 forward = transform.forward;
            forward.y = 0;
            forward.Normalize();

            float angleDifference = Vector3.SignedAngle(forward, normalizedDirection, Vector3.up);

            // Apply steering torque
            if (Mathf.Abs(angleDifference) > 5.0f)
            {
                float steerFactor = Mathf.Clamp(angleDifference / 45.0f, -1.0f, 1.0f);
                rb.AddTorque(Vector3.up * steerFactor * turnTorque, ForceMode.Force);
            }

            // Apply forward propulsion if roughly facing target
            if (Mathf.Abs(angleDifference) < 60.0f)
            {
                if (rb.linearVelocity.magnitude < pursuitSpeed)
                {
                    rb.AddForce(transform.forward * accelerationForce, ForceMode.Force);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Check if jeep collided with a pushable barricade
            PushableBarricade barricade = collision.collider.GetComponentInParent<PushableBarricade>();
            if (barricade != null)
            {
                Rigidbody barricadeRb = barricade.GetComponent<Rigidbody>();
                if (barricadeRb != null)
                {
                    Vector3 impactForce = rb.linearVelocity * (rb.mass * 0.1f * barricadeImpactMultiplier);
                    barricadeRb.AddForce(impactForce, ForceMode.Impulse);
                    Debug.Log($"[Military Jeep] 💥 Smashed through barricade! Applied impulse force: {impactForce.magnitude:F1} N");
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (hasActiveTarget)
            {
                Gizmos.color = currentState == JeepState.Pursuit ? Color.red : Color.yellow;
                Gizmos.DrawWireSphere(targetPosition, 1.0f);
                Gizmos.DrawLine(transform.position, targetPosition);
            }
        }
    }
}

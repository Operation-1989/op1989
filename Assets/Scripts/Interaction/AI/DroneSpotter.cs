using UnityEngine;
using Operation1989.Interaction.Vehicles;

namespace Operation1989.Interaction.AI
{
    /// <summary>
    /// Attached to the Surveillance Drone.
    /// Casts a downward vision cone to spot the player.
    /// Respects cover behind PushableBarricades and concealment inside SupplyTruckStealthZone.
    /// When spotted, triggers DroneAlertSystem to notify the Military Jeep.
    /// </summary>
    public class DroneSpotter : MonoBehaviour
    {
        [Header("Spotting Cone Parameters")]
        [SerializeField] private float spottingHeight = 12.0f;
        [SerializeField] private float spottingRadius = 6.0f;
        [SerializeField] private float scanFrequency = 0.2f;
        [SerializeField] private LayerMask playerLayer = 1 << 8; // Layer 8: Player
        [SerializeField] private LayerMask obstructionLayers = (1 << 0) | (1 << 10); // Default + Barricade (Layer 10)

        [Header("Hover Flight Physics")]
        [SerializeField] private bool enableHoverBobbing = true;
        [SerializeField] private float bobbingAmplitude = 0.3f;
        [SerializeField] private float bobbingSpeed = 1.5f;

        private DroneAlertSystem alertSystem;
        private Transform detectedPlayer;
        private float scanTimer = 0f;
        private Vector3 initialPosition;

        public bool IsCurrentlySpottingPlayer => detectedPlayer != null;

        private void Start()
        {
            alertSystem = GetComponent<DroneAlertSystem>();
            if (alertSystem == null)
            {
                alertSystem = DroneAlertSystem.Instance;
            }
            initialPosition = transform.position;
        }

        private void Update()
        {
            if (enableHoverBobbing)
            {
                ApplyHoverPhysics();
            }

            scanTimer += Time.deltaTime;
            if (scanTimer >= scanFrequency)
            {
                scanTimer = 0f;
                PerformOpticalScan();
            }
        }

        private void ApplyHoverPhysics()
        {
            float newY = initialPosition.y + Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmplitude;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
        }

        private void PerformOpticalScan()
        {
            // Ignore player if currently concealed inside Supply Truck stealth zone
            if (SupplyTruckStealthZone.IsPlayerConcealed)
            {
                if (detectedPlayer != null)
                {
                    LosePlayer();
                }
                return;
            }

            // Downward overlap sphere / cone detection
            Vector3 groundCenter = transform.position + Vector3.down * spottingHeight;
            Collider[] hits = Physics.OverlapSphere(groundCenter, spottingRadius, playerLayer);

            Transform candidate = null;
            foreach (Collider hit in hits)
            {
                Vector3 toTarget = hit.transform.position - transform.position;
                float distance = toTarget.magnitude;

                // Line of Sight check: ensure no walls or pushable barricades block optical view
                if (!Physics.Raycast(transform.position, toTarget.normalized, distance, obstructionLayers))
                {
                    candidate = hit.transform;
                    break;
                }
                else
                {
                    Debug.Log("[Surveillance Drone] Line of sight to player obstructed by cover/barricade.");
                }
            }

            if (candidate != null)
            {
                detectedPlayer = candidate;
                if (alertSystem != null)
                {
                    alertSystem.AlertJeep(detectedPlayer.position);
                }
            }
            else if (detectedPlayer != null)
            {
                LosePlayer();
            }
        }

        private void LosePlayer()
        {
            detectedPlayer = null;
            if (alertSystem != null)
            {
                alertSystem.ClearAlert();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = detectedPlayer != null ? Color.red : Color.cyan;
            Vector3 groundCenter = transform.position + Vector3.down * spottingHeight;

            // Draw apex to perimeter cone lines
            Gizmos.DrawLine(transform.position, groundCenter + Vector3.forward * spottingRadius);
            Gizmos.DrawLine(transform.position, groundCenter - Vector3.forward * spottingRadius);
            Gizmos.DrawLine(transform.position, groundCenter + Vector3.right * spottingRadius);
            Gizmos.DrawLine(transform.position, groundCenter - Vector3.right * spottingRadius);

            // Draw base circle
            Gizmos.DrawWireSphere(groundCenter, spottingRadius);
        }
    }
}

using System;
using UnityEngine;

namespace Operation1989.Interaction.Vehicles
{
    /// <summary>
    /// Attached to the trailing trigger box of the Supply Truck.
    /// Allows the human player to sneak behind/under the truck to bypass
    /// the base entrance gate and avoid surveillance detection.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SupplyTruckStealthZone : MonoBehaviour
    {
        public static bool IsPlayerConcealed { get; private set; } = false;

        public static event Action<bool> OnPlayerStealthStateChanged;

        [Header("Zone Configuration")]
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private LayerMask playerLayerMask = 1 << 8; // Layer 8: Player
        [SerializeField] private bool showDebugLogs = true;

        private Collider zoneCollider;

        private void Awake()
        {
            zoneCollider = GetComponent<Collider>();
            zoneCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayerCollider(other))
            {
                IsPlayerConcealed = true;
                OnPlayerStealthStateChanged?.Invoke(true);

                if (showDebugLogs)
                {
                    Debug.Log("[Supply Truck] 🕶️ Player entered trailing stealth zone. Player is concealed from gate sentries.");
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayerCollider(other))
            {
                IsPlayerConcealed = false;
                OnPlayerStealthStateChanged?.Invoke(false);

                if (showDebugLogs)
                {
                    Debug.Log("[Supply Truck] 👁️ Player exited trailing stealth zone. Player is exposed.");
                }
            }
        }

        private bool IsPlayerCollider(Collider col)
        {
            // Check by tag or layer
            if (col.CompareTag(playerTag)) return true;
            if (((1 << col.gameObject.layer) & playerLayerMask.value) != 0) return true;
            return false;
        }

        private void OnDisable()
        {
            if (IsPlayerConcealed)
            {
                IsPlayerConcealed = false;
                OnPlayerStealthStateChanged?.Invoke(false);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = IsPlayerConcealed ? new Color(0, 1, 0, 0.35f) : new Color(0.2f, 0.8f, 1f, 0.25f);
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                if (col is BoxCollider box)
                {
                    Gizmos.DrawCube(box.center, box.size);
                    Gizmos.DrawWireCube(box.center, box.size);
                }
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}

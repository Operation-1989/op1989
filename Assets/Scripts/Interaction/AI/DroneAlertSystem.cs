using System;
using UnityEngine;

namespace Operation1989.Interaction.AI
{
    /// <summary>
    /// Handles communication between the Surveillance Drone and the Military Jeep.
    /// When the aerial drone detects the player, it broadcasts the player's position
    /// to alert ground pursuit units.
    /// </summary>
    public class DroneAlertSystem : MonoBehaviour
    {
        public static DroneAlertSystem Instance { get; private set; }

        public event Action<Vector3> OnPlayerSpottedByDrone;
        public event Action OnPlayerLostByDrone;

        [Header("Alert Settings")]
        [SerializeField] private bool isPlayerCurrentlySpotted = false;
        [SerializeField] private Vector3 lastSpottedPosition;

        public bool IsPlayerSpotted => isPlayerCurrentlySpotted;
        public Vector3 LastSpottedPosition => lastSpottedPosition;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void AlertJeep(Vector3 playerPosition)
        {
            isPlayerCurrentlySpotted = true;
            lastSpottedPosition = playerPosition;
            OnPlayerSpottedByDrone?.Invoke(playerPosition);
            Debug.Log($"[Drone Alert System] 🚨 Player spotted at {playerPosition}! Alerting Military Jeep pursuit unit.");
        }

        public void ClearAlert()
        {
            isPlayerCurrentlySpotted = false;
            OnPlayerLostByDrone?.Invoke();
            Debug.Log("[Drone Alert System] 🟢 Player lost from drone optics. Resuming aerial patrol.");
        }
    }
}

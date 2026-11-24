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

        public float LastReportTime { get; private set; } = float.NegativeInfinity;
        public const float ContactTimeout = 1.25f;

        private void Update()
        {
            if (isPlayerCurrentlySpotted && Time.time - LastReportTime > ContactTimeout) ClearAlert();
        }

        private void OnEnable() { if (!Instance) Instance = this; }
        private void OnDisable() { ClearAlert(); if (Instance == this) Instance = null; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

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
            LastReportTime = Time.time;
            isPlayerCurrentlySpotted = true;
            Operation1989.AI.InferenceEngine.KnowledgeBase.Instance?.AssertFact(Operation1989.AI.InferenceEngine.Fact.DroneAlertActive);
            lastSpottedPosition = playerPosition;
            OnPlayerSpottedByDrone?.Invoke(playerPosition);
        }

        public void ClearAlert()
        {
            if (!isPlayerCurrentlySpotted) return;
            isPlayerCurrentlySpotted = false;
            Operation1989.AI.InferenceEngine.KnowledgeBase.Instance?.RetractFact(Operation1989.AI.InferenceEngine.Fact.DroneAlertActive);
            OnPlayerLostByDrone?.Invoke();
        }
    }
}

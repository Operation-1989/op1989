using System;
using UnityEngine;
using Operation1989.AI.InferenceEngine;
using Operation1989.Interaction.Environment;
using Operation1989.Interaction.Vehicles;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Multi-modal sensory perception system for the Tactical Recon Drone.
    /// Combines dual-cone optical sight (forward + downward spotlight cone) with acoustic gunfire/footstep hearing,
    /// obstacle/barricade occlusion checking, and temporal memory decay.
    /// Respects military base boundary fences and cutscene states.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class TacticalDronePerception : MonoBehaviour
    {
        [Header("Target & Reference")]
        [SerializeField] private Transform targetPlayer;
        [SerializeField] private LayerMask obstacleLayers = ~0; // All layers except triggers

        [Header("Optical Vision Parameters")]
        [Tooltip("Maximum sight range for visual detection in meters.")]
        [Range(20f, 200f)] [SerializeField] private float visualRange = 85f;
        [Tooltip("Horizontal Field of View angle in degrees.")]
        [Range(45f, 180f)] [SerializeField] private float fieldOfView = 110f;
        [Tooltip("Downward ground coverage radius directly beneath drone in meters.")]
        [Range(10f, 80f)] [SerializeField] private float downwardConeRadius = 35f;
        [Tooltip("Effective combat firing distance.")]
        [Range(10f, 80f)] [SerializeField] private float combatRange = 35f;

        [Header("Perimeter Constraints")]
        [Tooltip("Z coordinate of the base entrance gate. Targets behind this boundary are outside the base.")]
        [SerializeField] private float entranceGateZBoundary = -88f;

        [Header("Acoustic Sensor Parameters")]
        [Tooltip("Radius within which gunshots can be heard in meters.")]
        [Range(20f, 150f)] [SerializeField] private float acousticGunshotRange = 85f;
        [Tooltip("Radius within which rapid player footsteps can be heard in meters.")]
        [Range(5f, 35f)] [SerializeField] private float acousticFootstepRange = 25f;

        [Header("Memory & Decay")]
        [Tooltip("How long the drone remembers the player after losing line-of-sight (seconds).")]
        [Range(2f, 30f)] [SerializeField] private float memoryDuration = 8f;

        // Public Sensor State (Inspectable for Live VIVA Demonstration)
        public bool CanSeePlayer { get; private set; }
        public bool IsPlayerInCover { get; private set; }
        public bool IsInShootingRange { get; private set; }
        public bool HeardGunshot { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public Vector3 EstimatedVelocity { get; private set; }
        public float MemoryConfidence { get; private set; }
        public float TimeSinceLastSeen => Time.time - lastSeenTimestamp;
        public string Diagnostics { get; private set; } = "Initializing";
        public float DistanceToPlayer { get; private set; }

        private float lastSeenTimestamp = -100f;
        private float lastGunshotTimestamp = -100f;
        private Vector3 previousTargetPos;
        private float previousTargetTime = -1;
        private Vector3 sampledVelocity;
        private bool wasPreviouslySpotted = false;
        private DroneDamage damageHandler;
        private IntroTruckSequence intro;
        readonly PhysicsQueryBuffer sightQuery = new PhysicsQueryBuffer();
        [SerializeField] private bool logAcousticDetections;

        public Transform TargetPlayer => targetPlayer;

        private void Awake()
        {
            damageHandler = GetComponent<DroneDamage>();
            FindPlayerTarget();
        }

        private void OnEnable()
        {
            RifleWeapon.OnGunshotFired += HandleGunshotFired;
        }

        private void OnDisable()
        {
            RifleWeapon.OnGunshotFired -= HandleGunshotFired;
            CanSeePlayer = IsPlayerInCover = IsInShootingRange = false;
            RetractAllPerceptionFacts();
        }

        public void SetTargetPlayer(Transform player)
        {
            targetPlayer = player;
            previousTargetTime = -1; wasPreviouslySpotted = false;
            lastSeenTimestamp = -100; MemoryConfidence = 0; EstimatedVelocity = Vector3.zero;
            CanSeePlayer = IsPlayerInCover = IsInShootingRange = false;
        }

        private void FindPlayerTarget()
        {
            if (targetPlayer) return;

            var found = GameObject.FindGameObjectWithTag("Player");
            if (found)
            {
                var controller = found.GetComponentInParent<FirstPersonController>();
                targetPlayer = controller ? controller.transform : found.transform;
                return;
            }

            var fpc = FindAnyObjectByType<FirstPersonController>();
            if (fpc)
            {
                targetPlayer = fpc.transform;
            }
        }

        private void Update()
        {
            if (GamePauseMenu.IsPaused) return;
            if (damageHandler && damageHandler.Destroyed)
            {
                CanSeePlayer = false;
                IsPlayerInCover = false;
                IsInShootingRange = false;
                RetractAllPerceptionFacts();
                Diagnostics = "Drone Neutralized / Offline";
                enabled = false;
                return;
            }

            if (!targetPlayer) FindPlayerTarget();

            sampledVelocity = Vector3.zero;
            if (targetPlayer)
            {
                float elapsed = Time.time - previousTargetTime;
                if (previousTargetTime >= 0 && elapsed > .0001f && elapsed < .5f)
                    sampledVelocity = Vector3.ClampMagnitude((targetPlayer.position - previousTargetPos) / elapsed, 12f);
                previousTargetPos = targetPlayer.position; previousTargetTime = Time.time;
            }
            UpdateSensoryInputs();
            UpdateMemoryDecay();
            SynchronizeWithKnowledgeBase();
        }

        /// <summary>
        /// Evaluates optical raycasts, FOV, base perimeter boundary, and acoustic proximity.
        /// </summary>
        private void UpdateSensoryInputs()
        {
            // Every exit starts with a clean sensor state, including range and cutscene exits.
            CanSeePlayer = IsPlayerInCover = IsInShootingRange = false;
            if (!targetPlayer)
            {
                CanSeePlayer = false;
                IsPlayerInCover = false;
                IsInShootingRange = false;
                Diagnostics = "No Player Target Found in Scene";
                return;
            }

            // Check 1: Intro Cutscene Gatekeeper
            if (!intro) intro = FindAnyObjectByType<IntroTruckSequence>();
            if (intro && (!intro.HandedOff || !intro.Entered))
            {
                CanSeePlayer = false;
                IsPlayerInCover = false;
                IsInShootingRange = false;
                MemoryConfidence = 0f;
                wasPreviouslySpotted = false;
                lastSeenTimestamp = -100f;
                Diagnostics = "Intro Sequence Active / Player Outside Gate";
                return;
            }

            // Check 2: Base Perimeter Boundary (Outside entrance gate fence)
            if (targetPlayer.position.z < entranceGateZBoundary)
            {
                CanSeePlayer = false;
                IsPlayerInCover = false;
                IsInShootingRange = false;
                MemoryConfidence = 0f;
                wasPreviouslySpotted = false;
                lastSeenTimestamp = -100f;
                Diagnostics = $"Target Outside Base Perimeter Fence (Z: {targetPlayer.position.z:F0}m < {entranceGateZBoundary:F0}m)";
                return;
            }

            var playerMovement = targetPlayer.GetComponent<FirstPersonController>();
            if (playerMovement && !playerMovement.isActiveAndEnabled)
            {
                Diagnostics = "Player control inactive";
                return;
            }
            // Check 3: Player Dead
            var health = targetPlayer.GetComponent<CombatHealth>();
            if (health && health.Dead)
            {
                CanSeePlayer = false;
                IsPlayerInCover = false;
                IsInShootingRange = false;
                Diagnostics = "Target Player Dead";
                return;
            }

            Vector3 eyeOrigin = transform.position;
            var targetBody = targetPlayer.GetComponent<CharacterController>();
            Vector3 targetAimPoint = targetBody ? targetBody.bounds.center : targetPlayer.position + Vector3.up;
            Vector3 delta = targetAimPoint - eyeOrigin;
            float distance = delta.magnitude;
            DistanceToPlayer = distance;

            // Optical check 4: Supply truck stealth zone concealment
            if (SupplyTruckStealthZone.IsPlayerConcealed)
            {
                CanSeePlayer = false;
                IsPlayerInCover = true;
                Diagnostics = "Concealed in Supply Truck Stealth Zone";
                return;
            }

            // Optical check 5: Distance boundary
            if (distance > visualRange)
            {
                CanSeePlayer = false;
                IsPlayerInCover = false;
                IsInShootingRange = false;
                Diagnostics = $"Out of Range ({distance:F0}m > {visualRange:F0}m)";
                return;
            }

            // Optical check 6: Angle Check (Forward Vision Cone OR Downward Ground Cone)
            float angleToTarget = Vector3.Angle(transform.forward, delta);
            bool insideForwardCone = angleToTarget <= (fieldOfView * 0.5f);

            // Downward cone check: directly underneath drone in ground radius
            Vector3 horizontalOffset = targetPlayer.position - transform.position;
            horizontalOffset.y = 0;
            bool insideDownwardCone = (horizontalOffset.magnitude <= downwardConeRadius) && (eyeOrigin.y > targetPlayer.position.y);

            // Once acquired, the gimballed sensor keeps a lock in any direction within range.
            // A genuine obstruction still breaks sight; memory alone never produces reports.
            bool withinVisionAngle = insideForwardCone || insideDownwardCone || wasPreviouslySpotted;
            if (!withinVisionAngle)
            {
                CanSeePlayer = false;
                Diagnostics = $"Outside Vision Cone (Angle: {angleToTarget:F0}°, Ground Offset: {horizontalOffset.magnitude:F0}m > {downwardConeRadius:F0}m)";
                return;
            }

            // Optical check 7: Physical Line-of-Sight RaycastAll (ignores drone's own colliders)
            bool unobstructed = true;
            bool hitCover = false;
            string obstructionName = "";

            int count = sightQuery.Raycast(eyeOrigin, delta.normalized, distance, obstacleLayers);
            var hits = sightQuery.Hits;
            Array.Sort(hits, 0, count, PhysicsQueryBuffer.ByDistance);

            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                // Ignore triggers
                if (h.collider != null && h.collider.isTrigger) continue;

                // Ignore drone's own colliders and visuals
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue;

                // If hit target player or child of target player, line of sight reached!
                bool isTargetHit = (h.transform == targetPlayer) || 
                                   h.transform.IsChildOf(targetPlayer) || 
                                   h.collider.CompareTag("Player");

                if (isTargetHit)
                {
                    break;
                }

                // If hit an obstacle before reaching the player:
                unobstructed = false;
                obstructionName = h.collider.name;
                var barricade = h.collider.GetComponentInParent<PushableBarricade>();
                if (barricade != null || h.collider.gameObject.layer == 10)
                {
                    hitCover = true;
                }
                break;
            }

            if (unobstructed)
            {
                CanSeePlayer = true;
                IsPlayerInCover = false;
                Diagnostics = $"Spotted! (Distance: {distance:F0}m)";
                lastSeenTimestamp = Time.time;
                LastKnownPosition = targetPlayer.position;

                EstimatedVelocity = sampledVelocity;
            }
            else
            {
                CanSeePlayer = false;
                IsPlayerInCover = hitCover;
                Diagnostics = hitCover ? $"Target Behind Barricade Cover ({obstructionName})" : $"Occluded by [{obstructionName}]";
            }

            IsInShootingRange = CanSeePlayer && (distance <= combatRange);

            // Footstep acoustic sensor check: sprinting player close underneath
            if (!CanSeePlayer && distance <= acousticFootstepRange)
            {
                var fpc = targetPlayer.GetComponent<FirstPersonController>();
                if (fpc != null && sampledVelocity.magnitude > 3.0f)
                {
                    LastKnownPosition = targetPlayer.position;
                    lastSeenTimestamp = Time.time;
                }
            }
        }

        /// <summary>
        /// Acoustic listener: triggered via static event when any weapon shoots in the world.
        /// </summary>
        private void HandleGunshotFired(Vector3 origin)
        {
            // Acoustic gunshot is heard only if origin is inside the base or within perimeter
            if (!targetPlayer || origin.z < entranceGateZBoundary) return;
            if (!intro) intro = FindAnyObjectByType<IntroTruckSequence>();
            if (intro && (!intro.HandedOff || !intro.Entered)) return;
            // Ignore the guards' gunfire; only player shots should create player evidence.
            if (Vector3.Distance(origin, targetPlayer.position) > 4f) return;
            float dist = Vector3.Distance(transform.position, origin);
            if (dist <= acousticGunshotRange)
            {
                HeardGunshot = true;
                lastGunshotTimestamp = Time.time;
                LastKnownPosition = origin;
                if (logAcousticDetections) Debug.Log($"[Tactical Drone Perception] Acoustic sensor detected gunfire at {origin} (Distance: {dist:F1}m)");
            }
        }

        /// <summary>
        /// Calculates linear confidence decay for Last Known Position (LKP).
        /// </summary>
        private void UpdateMemoryDecay()
        {
            if (CanSeePlayer)
            {
                MemoryConfidence = 1.0f;
                wasPreviouslySpotted = true;
            }
            else
            {
                float elapsed = Time.time - lastSeenTimestamp;
                if (elapsed < memoryDuration)
                {
                    MemoryConfidence = Mathf.Clamp01(1.0f - (elapsed / memoryDuration));
                }
                else
                {
                    MemoryConfidence = 0.0f;
                }
            }

            // Gunshot memory expires after 4 seconds
            if (HeardGunshot && (Time.time - lastGunshotTimestamp > 4.0f))
            {
                HeardGunshot = false;
            }
        }

        /// <summary>
        /// Asserts and retracts facts in the Knowledge Base for Forward Chaining.
        /// </summary>
        private void SynchronizeWithKnowledgeBase()
        {
            var kb = KnowledgeBase.Instance;
            if (!kb) return;

            // PlayerSpotted
            if (CanSeePlayer) kb.AssertFact(Fact.PlayerSpotted);
            else kb.RetractFact(Fact.PlayerSpotted);

            // PlayerInCover
            if (IsPlayerInCover) kb.AssertFact(Fact.PlayerInCover);
            else kb.RetractFact(Fact.PlayerInCover);

            // PlayerInShootingRange
            if (IsInShootingRange) kb.AssertFact(Fact.PlayerInShootingRange);
            else kb.RetractFact(Fact.PlayerInShootingRange);

            // GunshotHeard
            if (HeardGunshot) kb.AssertFact(Fact.GunshotHeard);
            else kb.RetractFact(Fact.GunshotHeard);

            // TargetLost (previously spotted, now lost, but memory is still active)
            bool targetLost = !CanSeePlayer && wasPreviouslySpotted && (MemoryConfidence > 0f);
            if (targetLost) kb.AssertFact(Fact.TargetLost);
            else kb.RetractFact(Fact.TargetLost);

            if (MemoryConfidence <= 0f)
            {
                wasPreviouslySpotted = false;
            }
        }

        private void RetractAllPerceptionFacts()
        {
            var kb = KnowledgeBase.Instance;
            if (!kb) return;
            kb.RetractFact(Fact.PlayerSpotted);
            kb.RetractFact(Fact.PlayerInCover);
            kb.RetractFact(Fact.PlayerInShootingRange);
            kb.RetractFact(Fact.GunshotHeard);
            kb.RetractFact(Fact.TargetLost);
        }

        private void OnDrawGizmosSelected()
        {
            // Optical vision cone
            Gizmos.color = CanSeePlayer ? Color.red : new Color(0f, 1f, 1f, 0.3f);
            Gizmos.DrawRay(transform.position, Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward * visualRange);
            Gizmos.DrawRay(transform.position, Quaternion.Euler(0, fieldOfView * 0.5f, 0) * transform.forward * visualRange);

            // Downward cone
            Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.25f);
            Gizmos.DrawWireSphere(transform.position - Vector3.up * 10f, downwardConeRadius);

            // Acoustic gunshot radius
            Gizmos.color = new Color(0.8f, 0.2f, 0.8f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, acousticGunshotRange);

            // Last Known Position (LKP)
            if (MemoryConfidence > 0f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(LastKnownPosition, 1.0f);
                Gizmos.DrawLine(transform.position, LastKnownPosition);
            }
        }
    }
}

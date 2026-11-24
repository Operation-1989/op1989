using System;
using System.Collections.Generic;
using UnityEngine;
using Operation1989.AI.InferenceEngine;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Master Coordinator for the Autonomous Tactical Recon Drone.
    /// Orchestrates the Sense-Think-Act autonomous cycle:
    /// 1. SENSE: Multi-modal perception (optical vision, acoustic hearing, obstacle occlusion)
    /// 2. THINK: Forward Chaining inference engine over KnowledgeBase facts
    /// 3. ACT: 3D A* Search Pathfinding with dynamic edge re-planning and player tracking and jeep dispatch
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TacticalDronePerception))]
    [RequireComponent(typeof(TacticalDroneInferenceEngine))]
    [RequireComponent(typeof(TacticalDroneNavigationGraph))]
    [RequireComponent(typeof(TacticalDroneMovement))]
    [RequireComponent(typeof(TacticalDroneCombat))]
    public class TacticalDroneController : MonoBehaviour
    {
        [Header("Patrol Loop")]
        [SerializeField] private int[] patrolNodeSequence;
        [SerializeField] private float searchDurationAtLkp = 5.0f;

        [Header("Shootable tracking position")]
        [Min(4)] public float trackingDistance = 12f;
        [Min(2)] public float trackingHeight = 6f;
        [Min(3)] public float patrolHeight = 8f;

        public InferredAction CurrentAction => inferenceEngine ? inferenceEngine.CurrentAction : InferredAction.PatrolWaypoints;
        public int CurrentPatrolIndex { get; private set; }

        private TacticalDronePerception perception;
        private TacticalDroneInferenceEngine inferenceEngine;
        private TacticalDroneNavigationGraph navGraph;
        private TacticalDroneMovement movement;
        private TacticalDroneCombat combat;
        private DroneDamage damageHandler;

        private int currentGoalNode = -1;
        private float searchTimer = 0f;
        private float patrolRetryTimer = 0f;
        private Vector3 activeDestination;
        readonly List<Vector3> plannedPath = new List<Vector3>();
        static readonly float[] trackingAngles = { 0f, -35f, 35f, -65f, 65f };
        [SerializeField] private bool logPathDecisions;

        private void Awake()
        {
            damageHandler = GetComponent<DroneDamage>();
            perception = GetComponent<TacticalDronePerception>();
            inferenceEngine = GetComponent<TacticalDroneInferenceEngine>();
            navGraph = GetComponent<TacticalDroneNavigationGraph>();
            movement = GetComponent<TacticalDroneMovement>();
            combat = GetComponent<TacticalDroneCombat>();
        }

        private void OnEnable()
        {
            movement.OnDynamicObstacleDetected += HandleDynamicObstacleDetected;
            // Script reloads can retain the previous high flight nodes and active route.
            if (Application.isPlaying)
            {
                navGraph.ConfigureCruiseHeight(patrolHeight, 0f);
                movement.ClearPath();
                nextTrackingPlan = 0;
                trackingHeading = Vector3.zero;
            }
        }

        private void OnDisable()
        {
            movement.OnDynamicObstacleDetected -= HandleDynamicObstacleDetected;
        }

        private void Start()
        {
            if (patrolNodeSequence == null || patrolNodeSequence.Length == 0)
            {
                // Default sequence through all graph nodes
                int count = navGraph.Nodes.Count;
                patrolNodeSequence = new int[count];
                for (int i = 0; i < count; i++) patrolNodeSequence[i] = i;
            }

            navGraph.ConfigureCruiseHeight(patrolHeight, 0f);
            CurrentPatrolIndex = (patrolNodeSequence.Length > 1) ? 1 : 0;
            PlanPathToNextPatrolNode();
        }

        private void Update()
        {
            if (GamePauseMenu.IsPaused) return;
            if (damageHandler && damageHandler.Destroyed)
            {
                combat.ShutDownAllCombatSystems();
                movement.ClearPath();
                enabled = false;
                return;
            }

            InferredAction action = inferenceEngine.CurrentAction;

            switch (action)
            {
                case InferredAction.DispatchJeepPursuit:
                case InferredAction.EngageRangedCombat:
                    ExecuteTrackAction();
                    break;

                case InferredAction.InvestigateLastKnownPos:
                    ExecuteInvestigateAction();
                    break;

                case InferredAction.PatrolWaypoints:
                default:
                    ExecutePatrolAction();
                    break;
            }
        }

        float nextTrackingPlan;
        Vector3 lastTrackingGoal;
        Vector3 trackingHeading, headingAnchor;

        private void ExecuteTrackAction()
        {
            if (!perception.CanSeePlayer || !perception.TargetPlayer) return;
            // Reporting is handled by DronePlayerSurveillance after sensing, independently of flight.
            searchTimer = 0;
            Vector3 target = perception.LastKnownPosition;
            if (Time.time < nextTrackingPlan) return;
            nextTrackingPlan = Time.time + .75f;
            Vector3 moved = target - headingAnchor; moved.y = 0;
            // Camera-only aiming must not drag the target around the player.
            if (trackingHeading.sqrMagnitude < .01f || moved.magnitude > 1f)
            {
                trackingHeading = perception.TargetPlayer.forward; trackingHeading.y = 0;
                if (trackingHeading.sqrMagnitude < .01f) trackingHeading = Vector3.forward;
                trackingHeading.Normalize(); headingAnchor = target;
            }
            float ground = navGraph.GroundHeightAt(target, target.y);
            Vector3 desired = TrackingPoint(target, trackingHeading, ground, trackingDistance, trackingHeight);
            if (movement.IsMoving && Vector3.Distance(desired, lastTrackingGoal) < 2f) return;
            lastTrackingGoal = desired;
            // If the preferred spot is occupied, choose another forward spot at the same low height.
            for (int distanceIndex = 0; distanceIndex < 2; distanceIndex++)
                foreach (float angle in trackingAngles)
                {
                    float distance = trackingDistance + distanceIndex * 4f;
                    Vector3 heading = Quaternion.Euler(0, angle, 0) * trackingHeading;
                    Vector3 candidate = TrackingPoint(target, heading, ground, distance, trackingHeight);
                    candidate.y = navGraph.GroundHeightAt(candidate, ground) + trackingHeight;
                    if (!navGraph.FindFlightPath(transform.position, candidate, plannedPath)) continue;
                    movement.SetPath(plannedPath); return;
                }
            // Keep the existing safe route; never retreat to the old 18 m overhead position.
        }

        public static Vector3 TrackingPoint(Vector3 playerPosition, Vector3 heading, float groundY, float ahead, float height)
        {
            heading.y = 0;
            if (heading.sqrMagnitude < .01f) heading = Vector3.forward;
            Vector3 point = playerPosition + heading.normalized * Mathf.Max(4, ahead);
            point.y = groundY + Mathf.Max(2, height);
            return point;
        }

        private void ExecuteInvestigateAction()
        {
            Vector3 lkp = perception.LastKnownPosition;

            // Plan A* path to the node closest to LKP if not already traveling there
            int targetNode = navGraph.GetNearestNode(lkp);
            if (targetNode != currentGoalNode && targetNode >= 0)
            {
                PlanPathToNode(targetNode);
            }

            // Once drone arrives near LKP, conduct search sweep
            if (!movement.IsMoving)
            {
                searchTimer += Time.deltaTime;
                if (searchTimer >= searchDurationAtLkp)
                {
                    searchTimer = 0f;
                    // Resume perimeter patrol
                    PlanPathToNextPatrolNode();
                }
            }
        }

        private void ExecutePatrolAction()
        {
            searchTimer = 0f;

            if (!movement.IsMoving)
            {
                patrolRetryTimer += Time.deltaTime;
                if (patrolRetryTimer >= 0.5f)
                {
                    patrolRetryTimer = 0f;
                    if (patrolNodeSequence == null || patrolNodeSequence.Length == 0) return;
                    CurrentPatrolIndex = (CurrentPatrolIndex + 1) % patrolNodeSequence.Length;
                    PlanPathToNextPatrolNode();
                }
            }
            else
            {
                patrolRetryTimer = 0f;
            }
        }

        public void PlanPathToNode(int goalIndex)
        {
            if (goalIndex < 0 || goalIndex >= navGraph.Nodes.Count) return;

            currentGoalNode = goalIndex;

            if (navGraph.FindFlightPath(transform.position, navGraph.Nodes[goalIndex].position, plannedPath))
            {
                movement.SetPath(plannedPath);
                activeDestination = navGraph.Nodes[goalIndex].position;
                if (logPathDecisions) Debug.Log($"[Tactical Drone Controller] A* Path calculated to Node {goalIndex} ({navGraph.Nodes[goalIndex].label})");
            }
        }

        private void PlanPathToNextPatrolNode()
        {
            if (patrolNodeSequence == null || patrolNodeSequence.Length == 0) return;
            int nextNode = patrolNodeSequence[CurrentPatrolIndex];
            PlanPathToNode(nextNode);
        }

        /// <summary>
        /// Dynamic Re-Planning: When forward whisker detects a dynamic barrier,
        /// marks the edge blocked and recalculates A* path immediately.
        /// </summary>
        private void HandleDynamicObstacleDetected()
        {
            movement.ClearPath();
            nextTrackingPlan = 0;
            if (!perception.CanSeePlayer && currentGoalNode >= 0) PlanPathToNode(currentGoalNode);
        }

    }
}

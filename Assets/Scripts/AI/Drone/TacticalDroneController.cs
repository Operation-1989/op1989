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
    /// 3. ACT: 3D A* Search Pathfinding with dynamic edge re-planning and combat engagement
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

        [Header("Debug & VIVA Presentation")]
        [SerializeField] private bool showDebugOverlay = true;

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

            CurrentPatrolIndex = (patrolNodeSequence.Length > 1) ? 1 : 0;
            PlanPathToNextPatrolNode();
        }

        private void Update()
        {
            if (damageHandler && damageHandler.Destroyed)
            {
                combat.ShutDownAllCombatSystems();
                movement.ClearPath();
                enabled = false;
                return;
            }

            InferredAction action = inferenceEngine.CurrentAction;
            combat.SetSearchlightState(action);

            switch (action)
            {
                case InferredAction.EngageRangedCombat:
                    ExecuteEngageAction();
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

        private void ExecuteEngageAction()
        {
            if (!perception.TargetPlayer) return;

            Vector3 playerPos = perception.TargetPlayer.position;
            combat.SetAimTarget(playerPos, true);

            // Aim rotation towards player
            Vector3 aimDir = (playerPos - transform.position).normalized;
            aimDir.y = 0;
            if (aimDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(aimDir), 120f * Time.deltaTime);
            }

            combat.FireSuppressiveBurst(perception.TargetPlayer);

            // Dynamic tactical flight during combat:
            // If the player is far (> 35m) and drone is not moving, navigate to nearest vantage node
            float distToPlayer = Vector3.Distance(transform.position, playerPos);
            if (distToPlayer > 35f && !movement.IsMoving)
            {
                int vantage = navGraph.GetNearestNode(playerPos);
                if (vantage >= 0 && vantage != currentGoalNode)
                {
                    PlanPathToNode(vantage);
                }
            }
        }

        private void ExecuteInvestigateAction()
        {
            Vector3 lkp = perception.LastKnownPosition;
            combat.SetAimTarget(lkp, true);

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
                // Sweep searchlight in an area circle
                float angle = searchTimer * 90f * Mathf.Deg2Rad;
                Vector3 sweepOffset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 6.0f;
                combat.SetAimTarget(lkp + sweepOffset, true);

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
            combat.SetAimTarget(Vector3.zero, false);
            searchTimer = 0f;

            if (!movement.IsMoving)
            {
                patrolRetryTimer += Time.deltaTime;
                if (patrolRetryTimer >= 0.5f)
                {
                    patrolRetryTimer = 0f;
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

            int startNode = navGraph.GetNearestNode(transform.position);
            currentGoalNode = goalIndex;

            if (navGraph.FindPathAStar(startNode, goalIndex, out List<Vector3> path, out int nodesExplored))
            {
                movement.SetPath(path);
                activeDestination = navGraph.Nodes[goalIndex].position;
                Debug.Log($"[Tactical Drone Controller] A* Path calculated to Node {goalIndex} ({navGraph.Nodes[goalIndex].label}) | Explored Nodes: {nodesExplored}");
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
            int currentNode = navGraph.GetNearestNode(transform.position);
            if (currentGoalNode >= 0 && currentNode != currentGoalNode)
            {
                navGraph.SetEdgeBlocked(currentNode, currentGoalNode, true);
                // Trigger dynamic A* re-plan
                PlanPathToNode(currentGoalNode);
            }
        }

        /// <summary>
        /// Live GUI overlay for the VIVA presentation.
        /// Demonstrates live Knowledge Base facts, Forward Chaining rule fired, and A* statistics.
        /// </summary>
        private void OnGUI()
        {
            if (!showDebugOverlay) return;

            if (damageHandler && damageHandler.Destroyed)
            {
                GUILayout.BeginArea(new Rect(15, 15, 380, 70), "Tactical Drone AI Telemetry", GUI.skin.window);
                GUILayout.Label("<color=red><b>DRONE STATUS: NEUTRALIZED / OFFLINE</b></color>");
                GUILayout.Label("Sensors, flight engines, and combat lasers terminated.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginArea(new Rect(15, 15, 420, 260), "Tactical Drone AI Telemetry (Student 2)", GUI.skin.window);

            GUILayout.Label($"<b>Action:</b> <color=yellow>{CurrentAction}</color>");
            GUILayout.Label($"<b>Active Rule:</b> {inferenceEngine.ActiveRuleName}");
            GUILayout.Label($"<b>Rule Logic:</b> {inferenceEngine.RuleExplanation}");

            GUILayout.Space(5);
            GUILayout.Label($"<b>Visual:</b> {(perception.CanSeePlayer ? "<color=red>SPOTTED</color>" : "<color=green>CLEAR</color>")}" +
                            $" | <b>Cover:</b> {(perception.IsPlayerInCover ? "<color=yellow>IN COVER</color>" : "NO")}" +
                            $" | <b>LKP Confidence:</b> {perception.MemoryConfidence:P0}");
            GUILayout.Label($"<b>Optical Status:</b> <color=cyan>{perception.Diagnostics}</color>");

            GUILayout.Label($"<b>A* Nodes Explored:</b> {navGraph.LastNodesExploredCount}" +
                            $" | <b>Target Node:</b> {(currentGoalNode >= 0 && currentGoalNode < navGraph.Nodes.Count ? navGraph.Nodes[currentGoalNode].label : "None")}");

            // Knowledge Base Facts
            var kb = KnowledgeBase.Instance;
            if (kb)
            {
                string facts = string.Join(", ", kb.GetActiveFacts());
                GUILayout.Label($"<b>KB Active Facts:</b> {facts}");
            }

            GUILayout.EndArea();
        }
    }
}

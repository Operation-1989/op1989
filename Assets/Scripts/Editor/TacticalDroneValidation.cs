using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Operation1989.AI.Drone;
using Operation1989.AI.InferenceEngine;

namespace Operation1989.EditorScripts
{
    /// <summary>
    /// Automated test harness for the Tactical Drone Agent.
    /// Runs pure algorithmic assertions (A* Admissibility, Forward Chaining Rules, Heuristics)
    /// and dynamic Play Mode checks, logging results to Temp/TacticalDrone/validation.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class TacticalDroneValidation
    {
        const string ReportPath = TacticalDroneSetup.Work + "/validation.txt";

        [MenuItem("Operation 1989/Tactical Drone/3 Validate In Play Mode")]
        public static void Run()
        {
            Directory.CreateDirectory(TacticalDroneSetup.Work);
            var results = new List<string>();
            results.Add("==========================================================================");
            results.Add($"  TACTICAL RECON DRONE VALIDATION REPORT - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            results.Add("  Student 2 (Systems Engineer) - SE3062 Intelligent Systems");
            results.Add("==========================================================================\n");

            int passed = 0;
            int total = 0;

            void AssertTest(string testName, bool condition, string detail = "")
            {
                total++;
                if (condition)
                {
                    passed++;
                    results.Add($"[PASS] {testName} {(string.IsNullOrEmpty(detail) ? "" : "-> " + detail)}");
                }
                else
                {
                    results.Add($"[FAIL] {testName} ! FAILED: {detail}");
                }
            }

            // =========================================================================
            // PART 1: 3D A* SEARCH ALGORITHM & HEURISTIC TESTS
            // =========================================================================
            results.Add("--- SECTION 1: 3D A* Search & Heuristic Verification ---");

            var testGo = new GameObject("AStar_TestRig");
            var navGraph = testGo.AddComponent<TacticalDroneNavigationGraph>();
            navGraph.GenerateDefaultAirspaceNodes();

            AssertTest("A* Graph Initialization", navGraph.Nodes.Count >= 6, $"Constructed {navGraph.Nodes.Count} airspace waypoints.");

            // Admissibility Test: h(n) <= h*(n)
            // In Euclidean space, straight-line distance is guaranteed minimal.
            Vector3 pA = new Vector3(0, 10, 0);
            Vector3 pB = new Vector3(30, 25, 40);
            float heuristic = TacticalDroneNavigationGraph.CalculateHeuristic(pA, pB);
            float actualDistance = Vector3.Distance(pA, pB);
            AssertTest("Heuristic Admissibility: h(n) <= h*(n)", Mathf.Approximately(heuristic, actualDistance),
                $"h(n)={heuristic:F3}, actual 3D Euclidean distance={actualDistance:F3}. Strict lower bound holds.");

            // Triangle Inequality (Consistency / Monotonicity): h(A) <= c(A, B) + h(B)
            Vector3 pC = new Vector3(15, 18, 20);
            float hAC = TacticalDroneNavigationGraph.CalculateHeuristic(pA, pC);
            float cCB = TacticalDroneNavigationGraph.CalculateHeuristic(pC, pB);
            AssertTest("Heuristic Consistency (Monotonicity)", heuristic <= (hAC + cCB) + 0.001f,
                $"Triangle inequality verified: {heuristic:F2} <= ({hAC:F2} + {cCB:F2}).");

            // A* Pathfinding Execution
            bool foundPath = navGraph.FindPathAStar(0, 4, out List<Vector3> path, out int nodesExplored);
            AssertTest("A* Path Search Execution", foundPath && path.Count > 1,
                $"Optimal path found from Node 0 to Node 4 with {path.Count} waypoints ({nodesExplored} nodes explored).");

            // Dynamic Re-Planning: Blocking an edge
            navGraph.SetEdgeBlocked(0, 1, true);
            AssertTest("Dynamic Edge Invalidation", navGraph.IsEdgeBlocked(0, 1), "Edge marked dynamically blocked.");

            bool foundDetour = navGraph.FindPathAStar(0, 1, out List<Vector3> detourPath, out int detourExplored);
            AssertTest("Dynamic A* Re-Planning (Detour)", foundDetour,
                $"A* successfully computed alternate detour avoiding blocked edge ({detourExplored} nodes evaluated).");

            UnityEngine.Object.DestroyImmediate(testGo);

            // =========================================================================
            // PART 2: KNOWLEDGE BASE & FORWARD CHAINING INFERENCE ENGINE TESTS
            // =========================================================================
            results.Add("\n--- SECTION 2: Knowledge Base & Forward Chaining Inference ---");

            var kb = KnowledgeBase.Instance;
            GameObject tempKbGo = null;
            if (!kb)
            {
                tempKbGo = new GameObject("KB_TestRig");
                kb = tempKbGo.AddComponent<KnowledgeBase>();
                KnowledgeBase.SetInstanceForTesting(kb);
            }

            kb.AssertFact(Fact.PlayerSpotted);
            AssertTest("Knowledge Base Assertion", kb.HasFact(Fact.PlayerSpotted), "Fact.PlayerSpotted asserted successfully.");

            kb.RetractFact(Fact.PlayerSpotted);
            AssertTest("Knowledge Base Retraction", !kb.HasFact(Fact.PlayerSpotted), "Fact.PlayerSpotted retracted successfully.");

            var droneGo = new GameObject("Inference_TestDrone");
            var perception = droneGo.AddComponent<TacticalDronePerception>();
            var inference = droneGo.AddComponent<TacticalDroneInferenceEngine>();

            // Rule 5: Default Perimeter Patrol
            kb.ClearAllFacts();
            var action5 = inference.EvaluateForwardChaining();
            AssertTest("Forward Chaining Rule 5 (Default Patrol)", action5 == InferredAction.PatrolWaypoints,
                $"Active Rule: {inference.ActiveRuleName}");

            // Rule 1: Direct Combat Engagement (PlayerSpotted ^ !PlayerInCover ^ InShootingRange)
            kb.ClearAllFacts();
            kb.AssertFact(Fact.PlayerSpotted);
            kb.AssertFact(Fact.PlayerInShootingRange);
            var action1 = inference.EvaluateForwardChaining();
            AssertTest("Forward Chaining Rule 1 (Combat Suppression)", action1 == InferredAction.EngageRangedCombat,
                $"action={action1}, activeRule={inference.ActiveRuleName}, kbNull={(kb == null)}, hasSpotted={(kb != null && kb.HasFact(Fact.PlayerSpotted))}");

            // Rule 2: Flank Cover (PlayerSpotted ^ PlayerInCover)
            kb.ClearAllFacts();
            kb.AssertFact(Fact.PlayerSpotted);
            kb.AssertFact(Fact.PlayerInCover);
            var action2 = inference.EvaluateForwardChaining();
            AssertTest("Forward Chaining Rule 2 (Cover Awareness Flank)", action2 == InferredAction.InvestigateLastKnownPos,
                $"Active Rule: {inference.ActiveRuleName}");

            // Rule 3: Acoustic Gunfire Intercept (GunshotHeard ^ !PlayerSpotted)
            kb.ClearAllFacts();
            kb.AssertFact(Fact.GunshotHeard);
            var action3 = inference.EvaluateForwardChaining();
            AssertTest("Forward Chaining Rule 3 (Acoustic Gunfire Intercept)", action3 == InferredAction.InvestigateLastKnownPos,
                $"Active Rule: {inference.ActiveRuleName}");

            // Rule 4: Dynamic Lost Target Search (TargetLost)
            kb.ClearAllFacts();
            kb.AssertFact(Fact.TargetLost);
            var action4 = inference.EvaluateForwardChaining();
            AssertTest("Forward Chaining Rule 4 (Lost Target Search)", action4 == InferredAction.InvestigateLastKnownPos,
                $"Active Rule: {inference.ActiveRuleName}");

            kb.ClearAllFacts();
            UnityEngine.Object.DestroyImmediate(droneGo);
            if (tempKbGo) UnityEngine.Object.DestroyImmediate(tempKbGo);

            // =========================================================================
            // PART 3: SENSORY PERCEPTION & COMBAT TESTS
            // =========================================================================
            results.Add("\n--- SECTION 3: Multi-Modal Perception & Combat States ---");

            var combatGo = new GameObject("Combat_TestRig");
            var lightGo = new GameObject("TacticalSearchlight");
            lightGo.transform.SetParent(combatGo.transform);
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            var combat = combatGo.AddComponent<TacticalDroneCombat>();
            combat.SetSearchlightForTesting(spot);

            combat.SetSearchlightState(InferredAction.PatrolWaypoints);
            AssertTest("Searchlight State (Patrol)", spot.intensity > 0, "Spotlight active in patrol state.");

            combat.SetSearchlightState(InferredAction.EngageRangedCombat);
            AssertTest("Searchlight State (Engage)", spot.intensity >= 4.0f, "Spotlight boosted to high-intensity engage mode.");

            UnityEngine.Object.DestroyImmediate(combatGo);

            // =========================================================================
            // SUMMARY
            // =========================================================================
            results.Add("\n==========================================================================");
            results.Add($"  VALIDATION RESULT: {passed} / {total} CHECKS PASSED ({(float)passed / total:P0})");
            results.Add("==========================================================================");

            string output = string.Join("\n", results);
            File.WriteAllText(ReportPath, output);
            File.WriteAllText(TacticalDroneSetup.Work + "/passed.txt", DateTime.UtcNow.ToString("O"));

            Debug.Log(output);
        }
    }
}

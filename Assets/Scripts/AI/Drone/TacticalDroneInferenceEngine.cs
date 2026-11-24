using System;
using UnityEngine;
using Operation1989.AI.InferenceEngine;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Forward Chaining Inference Engine for the Tactical Drone.
    /// Evaluates production rules against environmental facts asserted in the Knowledge Base
    /// to deduce high-level tactical agent decisions (Sense-Think-Act).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TacticalDronePerception))]
    public class TacticalDroneInferenceEngine : MonoBehaviour
    {
        public event Action<InferredAction, string> OnInferenceRuleTriggered;

        // Current Inferred State (Inspectable during VIVA)
        [Header("Inference Status")]
        [SerializeField] private InferredAction currentAction = InferredAction.PatrolWaypoints;
        [SerializeField] private string activeRuleName = "Default: Perimeter Patrol";
        [TextArea(2, 4)]
        [SerializeField] private string ruleExplanation = "No tactical threats detected. Conducting baseline perimeter aerial patrol.";

        public InferredAction CurrentAction => currentAction;
        public string ActiveRuleName => activeRuleName;
        public string RuleExplanation => ruleExplanation;

        private TacticalDronePerception perception;

        private void Awake()
        {
            perception = GetComponent<TacticalDronePerception>();
        }

        private void Update()
        {
            EvaluateForwardChaining();
        }

        /// <summary>
        /// Pure Forward Chaining Inference evaluation.
        /// Applies Modus Ponens over the Knowledge Base facts to infer actions.
        /// </summary>
        public InferredAction EvaluateForwardChaining()
        {
            var kb = KnowledgeBase.Instance ? KnowledgeBase.Instance : UnityEngine.Object.FindAnyObjectByType<KnowledgeBase>();
            if (!kb)
            {
                Debug.LogWarning("[TacticalDroneInferenceEngine] KB is null in EvaluateForwardChaining!");
                return InferredAction.PatrolWaypoints;
            }

            InferredAction newAction = InferredAction.PatrolWaypoints;
            string newRule = "Rule 5: Default Perimeter Patrol";
            string explanation = "Default flight plan: Scanning waypoints across perimeter.";

            if (!perception) perception = GetComponent<TacticalDronePerception>();

            // =========================================================================
            // RULE 1: Track a visible target and dispatch the jeep
            // PREMISE: PlayerSpotted ^ !PlayerInCover
            // CONCLUSION: InferredAction.DispatchJeepPursuit
            // =========================================================================
            if (kb.HasFact(Fact.PlayerSpotted) && !kb.HasFact(Fact.PlayerInCover))
            {
                newAction = InferredAction.DispatchJeepPursuit;
                newRule = "Rule 1: Track Player and Dispatch Jeep";
                explanation = "Player visually confirmed. Maintain surveillance and report current coordinates to the jeep.";
            }
            // =========================================================================
            // RULE 2: Flank / Reposition (Cover Awareness)
            // PREMISE: PlayerInCover ^ (TargetLost OR GunshotHeard)
            // CONCLUSION: InferredAction.InvestigateLastKnownPos (Tactical Reposition)
            // =========================================================================
            else if (kb.HasFact(Fact.PlayerInCover) && (kb.HasFact(Fact.TargetLost) || kb.HasFact(Fact.GunshotHeard)))
            {
                newAction = InferredAction.InvestigateLastKnownPos;
                newRule = "Rule 2: Search Last Seen Cover";
                explanation = "Cover blocks sight. Search the last observed location without reporting unseen player coordinates.";
            }
            // =========================================================================
            // RULE 3: Acoustic Gunfire Intercept
            // PREMISE: GunshotHeard ^ !PlayerSpotted
            // CONCLUSION: InferredAction.InvestigateLastKnownPos
            // =========================================================================
            else if (kb.HasFact(Fact.GunshotHeard) && !kb.HasFact(Fact.PlayerSpotted))
            {
                newAction = InferredAction.InvestigateLastKnownPos;
                newRule = "Rule 3: Acoustic Gunfire Intercept";
                explanation = "Acoustic sensor registered rifle gunfire in sector. Diverting path to investigate sound coordinates.";
            }
            // =========================================================================
            // RULE 4: Dynamic Search of Lost Target Area
            // PREMISE: TargetLost ^ (MemoryConfidence > 0)
            // CONCLUSION: InferredAction.InvestigateLastKnownPos
            // =========================================================================
            else if (kb.HasFact(Fact.TargetLost))
            {
                newAction = InferredAction.InvestigateLastKnownPos;
                newRule = "Rule 4: Dynamic Lost Target Search";
                explanation = $"Visual lost behind obstacles. {(perception ? $"Memory confidence at {perception.MemoryConfidence:P0}." : "")} Sweeping Last Known Position.";
            }
            // =========================================================================
            // RULE 5: Default Baseline Patrol
            // PREMISE: Otherwise
            // CONCLUSION: InferredAction.PatrolWaypoints
            // =========================================================================
            else
            {
                newAction = InferredAction.PatrolWaypoints;
                newRule = "Rule 5: Default Perimeter Patrol";
                explanation = "All sectors clear of active hostility. Conducting autonomous perimeter waypoint flight.";
            }

            if (newAction != currentAction || newRule != activeRuleName)
            {
                currentAction = newAction;
                activeRuleName = newRule;
                ruleExplanation = explanation;
                OnInferenceRuleTriggered?.Invoke(currentAction, activeRuleName);
                Debug.Log($"[Forward Chaining Inference] => {newRule} -> Inferred Action: {currentAction}");
            }

            return currentAction;
        }
    }
}

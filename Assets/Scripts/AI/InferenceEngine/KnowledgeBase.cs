using System.Collections.Generic;
using UnityEngine;

namespace Operation1989.AI.InferenceEngine
{
    /// <summary>
    /// Supported Facts in the Intelligent Systems (IS) Knowledge Base.
    /// </summary>
    public enum Fact
    {
        PlayerSpotted,
        PlayerInCover,
        PlayerInShootingRange,
        DroneAlertActive,
        HQDoorLocked,
        JeepInPursuit,
        SecretIntelCaptured
    }

    /// <summary>
    /// Inferred Actions produced by Forward Chaining inference.
    /// </summary>
    public enum InferredAction
    {
        PatrolWaypoints,
        InvestigateLastKnownPos,
        EngageRangedCombat,
        TakeCoverBehindBarricade,
        DispatchJeepPursuit,
        LockdownHQBuilding
    }

    /// <summary>
    /// Stores environmental facts for the Intelligent Systems (IS) module inference.
    /// Implemented by Student 2 (Systems Engineer) to connect interaction physics with AI decision making.
    /// </summary>
    public class KnowledgeBase : MonoBehaviour
    {
        public static KnowledgeBase Instance { get; private set; }

        private HashSet<Fact> activeFacts = new HashSet<Fact>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void AssertFact(Fact fact)
        {
            if (!activeFacts.Contains(fact))
            {
                activeFacts.Add(fact);
                Debug.Log($"[IS Knowledge Base] + Fact Asserted: {fact}");
            }
        }

        public void RetractFact(Fact fact)
        {
            if (activeFacts.Contains(fact))
            {
                activeFacts.Remove(fact);
                Debug.Log($"[IS Knowledge Base] - Fact Retracted: {fact}");
            }
        }

        public bool HasFact(Fact fact)
        {
            return activeFacts.Contains(fact);
        }
    }
}

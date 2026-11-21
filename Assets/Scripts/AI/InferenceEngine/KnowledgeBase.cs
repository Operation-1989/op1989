using System;
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
        SecretIntelCaptured,
        GunshotHeard,
        TargetLost
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

        public static void SetInstanceForTesting(KnowledgeBase kb)
        {
            Instance = kb;
        }

        public event Action<Fact, bool> OnFactChanged;

        private readonly HashSet<Fact> activeFacts = new HashSet<Fact>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        public void AssertFact(Fact fact)
        {
            if (!activeFacts.Contains(fact))
            {
                activeFacts.Add(fact);
                OnFactChanged?.Invoke(fact, true);
                Debug.Log($"[IS Knowledge Base] + Fact Asserted: {fact}");
            }
        }

        public void RetractFact(Fact fact)
        {
            if (activeFacts.Contains(fact))
            {
                activeFacts.Remove(fact);
                OnFactChanged?.Invoke(fact, false);
                Debug.Log($"[IS Knowledge Base] - Fact Retracted: {fact}");
            }
        }

        public bool HasFact(Fact fact)
        {
            return activeFacts.Contains(fact);
        }

        public IEnumerable<Fact> GetActiveFacts()
        {
            return activeFacts;
        }

        public void ClearAllFacts()
        {
            var copy = new List<Fact>(activeFacts);
            activeFacts.Clear();
            foreach (var f in copy)
            {
                OnFactChanged?.Invoke(f, false);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class AgentPathTester : MonoBehaviour
{
    public AgentPathFollower agent;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            if (agent == null)
                return;

            List<Vector3> newPath = new List<Vector3>
            {
                new Vector3(0, 1, 8),
                new Vector3(6, 1, 8),
                new Vector3(6, 1, 0),
                new Vector3(0, 1, 0)
            };

            agent.SetPath(newPath);

            Debug.Log("Agent received a new path!");
        }
    }
}
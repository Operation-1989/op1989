
using UnityEngine;

public class AgentChaseController : MonoBehaviour
{
    public Transform player;
    public AgentPathFollower pathFollower;

    public float detectionRadius = 6f;
    public float chaseSpeed = 4f;
    public float investigationTime = 3f;
    public float stoppingDistance = 1.5f;

    private enum AgentState
    {
        Patrol,
        Chase,
        Investigate
    }

    private AgentState currentState = AgentState.Patrol;
    private Vector3 lastKnownPosition;
    private float investigationTimer;
    private bool reachedInvestigationPoint;

    void Update()
    {
        if (player == null || pathFollower == null)
            return;

        Vector3 playerDirection = player.position - transform.position;
        playerDirection.y = 0f;

        float distance = playerDirection.magnitude;

        if (distance <= detectionRadius)
        {
            lastKnownPosition = player.position;
            currentState = AgentState.Chase;
            reachedInvestigationPoint = false;
        }
        else if (currentState == AgentState.Chase)
        {
            currentState = AgentState.Investigate;
            investigationTimer = investigationTime;
            reachedInvestigationPoint = false;
        }

        switch (currentState)
        {
            case AgentState.Patrol:
                pathFollower.enabled = true;
                break;

            case AgentState.Chase:
                pathFollower.enabled = false;
                MoveTowards(player.position, stoppingDistance);
                break;

            case AgentState.Investigate:
                pathFollower.enabled = false;

                if (!reachedInvestigationPoint)
                {
                    MoveTowards(lastKnownPosition, 0.3f);

                    Vector3 difference = lastKnownPosition - transform.position;
                    difference.y = 0f;

                    if (difference.magnitude <= 0.3f)
                    {
                        reachedInvestigationPoint = true;
                        Debug.Log("Soldier investigating last known position.");
                    }
                }
                else
                {
                    investigationTimer -= Time.deltaTime;

                    if (investigationTimer <= 0f)
                    {
                        currentState = AgentState.Patrol;
                        pathFollower.enabled = true;
                        Debug.Log("Soldier returning to patrol.");
                    }
                }
                break;
        }
    }

    void MoveTowards(Vector3 target, float stopDistance)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance > stopDistance)
        {
            float step = Mathf.Min(
                chaseSpeed * Time.deltaTime,
                distance - stopDistance
            );

            transform.position += direction.normalized * step;
        }

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                5f * Time.deltaTime
            );
        }
    }
}


using UnityEngine;

public class AgentChaseController : MonoBehaviour
{
    public Transform player;
    public AgentPathFollower pathFollower;
    public Animator animator;

    [Header("Detection Settings")]
    public float detectionRadius = 15f;
    [Range(0f, 360f)] public float fieldOfViewAngle = 120f;
    public LayerMask obstacleLayers = ~0;

    [Header("Movement Settings")]
    public float chaseSpeed = 4f;
    public float investigationTime = 3f;
    public float stoppingDistance = 6f;

    [Header("Shooting Settings")]
    public float attackRange = 7f;
    public float shootingInterval = 1.5f;

    private float shootingTimer;

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

    void Awake()
    {
        if (pathFollower == null)
            pathFollower = GetComponent<AgentPathFollower>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (player == null || pathFollower == null)
            return;

        shootingTimer -= Time.deltaTime;

        if (CanSeePlayer())
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
                HandleChase();
                break;

            case AgentState.Investigate:
                pathFollower.enabled = false;

                if (!reachedInvestigationPoint)
                {
                    MoveTowards(lastKnownPosition, 0.3f);

                    Vector3 difference =
                        lastKnownPosition - transform.position;
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

    void HandleChase()
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance > stoppingDistance)
        {
            MoveTowards(player.position, stoppingDistance);
        }
        else
        {
            FaceTarget(player.position);
        }

        if (distance <= attackRange && shootingTimer <= 0f)
        {
            if (animator != null)
                animator.SetTrigger("Shoot");

            shootingTimer = shootingInterval;
            Debug.Log("Soldier shooting at player!");
        }
    }

    bool CanSeePlayer()
    {
        Vector3 eyePosition =
            transform.position + Vector3.up * 0.5f;

        Vector3 targetPosition =
            player.position + Vector3.up * 0.5f;

        Vector3 direction = targetPosition - eyePosition;
        float distance = direction.magnitude;

        if (distance > detectionRadius)
            return false;

        if (Vector3.Angle(transform.forward, direction) >
            fieldOfViewAngle / 2f)
            return false;

        if (Physics.Raycast(
            eyePosition,
            direction.normalized,
            out RaycastHit hit,
            distance,
            obstacleLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.transform != player &&
                !hit.transform.IsChildOf(player))
                return false;
        }

        return true;
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

        FaceTarget(target);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                5f * Time.deltaTime
            );
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Vector3 leftDirection =
            Quaternion.Euler(0, -fieldOfViewAngle / 2f, 0)
            * transform.forward;

        Vector3 rightDirection =
            Quaternion.Euler(0, fieldOfViewAngle / 2f, 0)
            * transform.forward;

        Gizmos.color = Color.green;
        Gizmos.DrawRay(
            transform.position,
            leftDirection * detectionRadius
        );
        Gizmos.DrawRay(
            transform.position,
            rightDirection * detectionRadius
        );
    }
}

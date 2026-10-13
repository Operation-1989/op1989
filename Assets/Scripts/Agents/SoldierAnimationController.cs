
using UnityEngine;

public class SoldierAnimationController : MonoBehaviour
{
    private Animator animator;
    private AgentChaseController chaseController;
    private AgentPathFollower pathFollower;
    private Vector3 previousPosition;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        chaseController = GetComponent<AgentChaseController>();
        pathFollower = GetComponent<AgentPathFollower>();

        previousPosition = transform.position;
    }

    void LateUpdate()
    {
        if (animator == null) return;

        float movementSpeed =
            Vector3.Distance(transform.position, previousPosition)
            / Mathf.Max(Time.deltaTime, 0.0001f);

        bool isMoving = movementSpeed > 0.05f;

        float animationSpeed = 0f;

        if (isMoving)
        {
            // Path follower is active during normal patrol.
            // It is disabled during chase and investigation.
            animationSpeed = pathFollower != null && pathFollower.enabled
                ? 1f
                : 2f;
        }

        animator.SetFloat("Speed", animationSpeed);
        previousPosition = transform.position;
    }
}

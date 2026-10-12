using UnityEngine;

public class SoldierAnimationController : MonoBehaviour
{
    private Animator animator;
    private Vector3 previousPosition;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        previousPosition = transform.position;
    }

    void Update()
    {
        if (animator == null) return;

        float movementSpeed =
            Vector3.Distance(transform.position, previousPosition)
            / Mathf.Max(Time.deltaTime, 0.0001f);

        animator.SetFloat("Speed", movementSpeed > 0.05f ? 1f : 0f);

        previousPosition = transform.position;
    }
}
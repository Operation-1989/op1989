
using System.Collections.Generic;
using UnityEngine;

public class JeepMovementController : MonoBehaviour
{
    [Header("Path Settings")]
    public List<Transform> waypoints = new List<Transform>();
    public bool loopPath = true;
    public float waypointDistance = 0.5f;

    [Header("Movement Settings")]
    public float maxSpeed = 5f;
    public float acceleration = 3f;
    public float braking = 5f;
    public float rotationSpeed = 120f;

    private int currentWaypoint = 0;
    private float currentSpeed = 0f;

    void Update()
    {
        if (waypoints.Count == 0)
            return;

        Transform target = waypoints[currentWaypoint];

        if (target == null)
            return;

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance <= waypointDistance)
        {
            currentWaypoint++;

            if (currentWaypoint >= waypoints.Count)
            {
                if (loopPath)
                    currentWaypoint = 0;
                else
                {
                    currentSpeed = 0f;
                    enabled = false;
                    return;
                }
            }

            target = waypoints[currentWaypoint];
            direction = target.position - transform.position;
            direction.y = 0f;
            distance = direction.magnitude;
        }

        if (direction.sqrMagnitude < 0.001f)
            return;

        // Rotate smoothly toward the next waypoint
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );

        // Reduce speed when turning sharply
        float turnAngle = Vector3.Angle(transform.forward, direction);
        float targetSpeed = maxSpeed * Mathf.Lerp(
            0.3f, 1f, 1f - Mathf.Clamp01(turnAngle / 90f)
        );

        float rate = targetSpeed < currentSpeed ? braking : acceleration;
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.deltaTime
        );

        transform.position += transform.forward * currentSpeed * Time.deltaTime;
    }

    public void SetPath(List<Vector3> newPath)
    {
        if (newPath == null || newPath.Count == 0)
            return;

        foreach (Transform waypoint in waypoints)
        {
            if (waypoint != null && waypoint.name.StartsWith("RuntimeJeepWaypoint"))
                Destroy(waypoint.gameObject);
        }

        waypoints.Clear();

        foreach (Vector3 position in newPath)
        {
            GameObject point = new GameObject("RuntimeJeepWaypoint");
            point.transform.position = position;
            waypoints.Add(point.transform);
        }

        currentWaypoint = 0;
        currentSpeed = 0f;
        loopPath = false;
        enabled = true;
    }
}

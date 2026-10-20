
using System.Collections.Generic;
using UnityEngine;

public class AgentPathFollower : MonoBehaviour
{
    public List<Transform> waypoints;
    public float moveSpeed = 3f;
    public float rotationSpeed = 5f;
    public float acceleration = 5f;
    public float stoppingDistance = 0.2f;
    public bool loopPath = true;

    private int currentWaypoint = 0;
    private float currentSpeed = 0f;

    private List<Transform> runtimeWaypoints = new List<Transform>();

    void Update()
    {
        if (waypoints == null || waypoints.Count == 0)
            return;

        if (currentWaypoint >= waypoints.Count)
            return;

        Transform target = waypoints[currentWaypoint];
        if (target == null) return;

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance <= stoppingDistance)
        {
            if (currentWaypoint < waypoints.Count - 1)
                currentWaypoint++;
            else if (loopPath)
                currentWaypoint = 0;
            else
            {
                currentSpeed = 0f;
                return;
            }

            return;
        }

        float desiredSpeed = Mathf.Min(
            moveSpeed,
            Mathf.Sqrt(2f * acceleration * distance)
        );

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            desiredSpeed,
            acceleration * Time.deltaTime
        );

        float step = Mathf.Min(
            currentSpeed * Time.deltaTime,
            distance
        );

        transform.position += direction.normalized * step;

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    public void SetPath(List<Vector3> newPath)
    {
        if (newPath == null || newPath.Count == 0)
            return;

        foreach (Transform point in runtimeWaypoints)
        {
            if (point != null)
                Destroy(point.gameObject);
        }

        runtimeWaypoints.Clear();
        waypoints.Clear();

        foreach (Vector3 position in newPath)
        {
            GameObject point = new GameObject("RuntimeWaypoint");
            point.transform.position = position;

            runtimeWaypoints.Add(point.transform);
            waypoints.Add(point.transform);
        }

        currentWaypoint = 0;
        currentSpeed = 0f;
        loopPath = false;
    }
}

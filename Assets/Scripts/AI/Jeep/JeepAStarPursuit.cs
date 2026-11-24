using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Operation1989.Interaction.AI;

namespace Operation1989.AI.Jeep
{
    /// <summary>
    /// Intelligent Systems – drone-guided A* pursuit for the patrol jeep.
    ///
    /// Agent loop (Sense → Think → Act):
    ///  • SENSE  – the surveillance drone broadcasts the player's position (DroneAlertSystem).
    ///  • THINK  – A* on <see cref="JeepNavigationGrid"/> plans the cheapest collision-free route,
    ///             and re-plans when the reported position moves or the road is blocked.
    ///  • ACT    – the jeep leaves its perimeter patrol, drives the route with speed control for
    ///             corners and braking, stops short of the soldier, searches the last known
    ///             position when the drone loses the player, then drives back to its patrol road.
    ///
    /// States: Patrol → Pursuit → Search → Return → Patrol.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(JeepNavigationGrid), typeof(Rigidbody))]
    public sealed class JeepAStarPursuit : MonoBehaviour
    {
        public enum State { Patrol, Pursuit, Search, Return }

        [Header("Driving")]
        [Min(.5f)] public float pursuitSpeed = 10f;
        [Min(.5f)] public float returnSpeed = 6f;
        [Min(.1f)] public float acceleration = 4f;
        [Min(.1f)] public float braking = 7f;
        [Min(10)] public float turnSpeed = 110f;
        [Tooltip("A path point counts as reached within this distance.")]
        [Min(.2f)] public float waypointRadius = 1.6f;
        [Tooltip("Speed factor in a 90° corner.")]
        [Range(.1f, 1)] public float cornerSlowdown = .35f;
        [Tooltip("The jeep stops this far from the reported player position.")]
        [Min(1)] public float stopDistanceFromPlayer = 7f;

        [Header("Collision safety")]
        [Tooltip("Extra look-ahead distance of the forward box cast, on top of braking distance.")]
        [Min(.1f)] public float obstacleLookAhead = 1.2f;
        [Tooltip("When something blocks the road this long, it is added to the map and A* re-plans.")]
        [Min(.1f)] public float blockedReplanSeconds = 1f;
        [Min(1)] public float temporaryObstacleSeconds = 20f;

        [Header("Re-planning")]
        [Tooltip("Re-plan when the drone reports the player this far from the planned goal.")]
        [Min(.5f)] public float replanDistance = 4f;
        [Min(.1f)] public float minReplanInterval = 1f;

        [Header("Search")]
        [Min(0)] public float searchSeconds = 8f;
        [Min(1)] public float maxGoalSearchRadius = 30f;

        [Header("Debug")]
        public bool showStatusOverlay = true;
        public bool logDecisions = true;

        public State CurrentState { get; private set; } = State.Patrol;
        public float CurrentSpeed { get; private set; }
        public Vector3 TargetPosition { get; private set; }
        public bool DroneHasPlayer { get; private set; }
        public string BlockedBy { get; private set; } = "";
        public int ReplanCount { get; private set; }
        public IReadOnlyList<Vector3> Path => path;
        public bool IsDriving => CurrentState != State.Patrol;

        BaseRoadJeepPatrol patrol;
        JeepNavigationGrid grid;
        Rigidbody body;
        BoxCollider footprint;
        DroneAlertSystem subscribedAlerts;
        Transform player;

        readonly List<Vector3> path = new List<Vector3>();
        int pathIndex;
        Vector3 plannedGoal;
        float lastPlanTime = -999f;
        float blockedTimer;
        float searchTimer;
        int returnWaypoint = -1;
        bool pendingReplan;

        void Awake()
        {
            patrol = GetComponent<BaseRoadJeepPatrol>();
            grid = GetComponent<JeepNavigationGrid>();
            body = GetComponent<Rigidbody>();
            footprint = GetComponent<BoxCollider>();
        }

        void Start()
        {
            ConfigureGrid(grid, patrol, footprint, transform);
            grid.Build(transform, transform.position.y, PatrolBounds(patrol));
        }

        /// <summary>Shared by runtime and the editor validation tool.</summary>
        public static void ConfigureGrid(JeepNavigationGrid grid, BaseRoadJeepPatrol patrol, BoxCollider footprint, Transform jeep)
        {
            if ((grid.roadSurfaces == null || grid.roadSurfaces.Length == 0) && patrol && patrol.groundSurfaces != null)
                grid.roadSurfaces = patrol.groundSurfaces;
            if (footprint)
                grid.vehicleHalfWidth = Mathf.Max(.5f, footprint.size.x * Mathf.Abs(jeep.lossyScale.x) * .5f);
        }

        public static Bounds? PatrolBounds(BaseRoadJeepPatrol patrol)
        {
            if (!patrol || patrol.waypoints == null) return null;
            Bounds? b = null;
            foreach (var w in patrol.waypoints)
            {
                if (!w) continue;
                if (b.HasValue) { var v = b.Value; v.Encapsulate(w.position); b = v; }
                else b = new Bounds(w.position, Vector3.one);
            }
            return b;
        }

        // =====================================================================================
        // SENSE – drone messages
        // =====================================================================================

        void OnDisable() => Unsubscribe();

        void Update()
        {
            // The alert system may be created after this jeep (scene bootstrap), so keep checking.
            if (subscribedAlerts != DroneAlertSystem.Instance)
            {
                Unsubscribe();
                subscribedAlerts = DroneAlertSystem.Instance;
                if (subscribedAlerts)
                {
                    subscribedAlerts.OnPlayerSpottedByDrone += HandlePlayerSpotted;
                    subscribedAlerts.OnPlayerLostByDrone += HandlePlayerLost;
                }
            }
        }

        void Unsubscribe()
        {
            if (!subscribedAlerts) { subscribedAlerts = null; return; }
            subscribedAlerts.OnPlayerSpottedByDrone -= HandlePlayerSpotted;
            subscribedAlerts.OnPlayerLostByDrone -= HandlePlayerLost;
            subscribedAlerts = null;
        }

        /// <summary>Drone message: "player seen at this position".</summary>
        public void HandlePlayerSpotted(Vector3 position)
        {
            TargetPosition = position;
            DroneHasPlayer = true;
            if (CurrentState != State.Pursuit)
            {
                EnterPursuit();
                return;
            }
            // Only re-plan when the target really moved – keeps CPU use low and driving stable.
            if (Flat(position - plannedGoal).magnitude > replanDistance) pendingReplan = true;
        }

        /// <summary>Drone message: "player lost". The jeep continues to the last known position.</summary>
        public void HandlePlayerLost()
        {
            DroneHasPlayer = false;
            if (logDecisions && CurrentState == State.Pursuit)
                Debug.Log($"[Jeep A*] Drone lost the player. Driving on to last known position {TargetPosition}.");
        }

        // =====================================================================================
        // THINK – state transitions and A* planning
        // =====================================================================================

        void EnterPursuit()
        {
            if (patrol && patrol.enabled)
            {
                CurrentSpeed = patrol.CurrentSpeed; // keep moving smoothly out of the patrol
                patrol.enabled = false;
            }
            CurrentState = State.Pursuit;
            searchTimer = 0;
            if (logDecisions) Debug.Log($"[Jeep A*] Drone report received – pursuing player at {TargetPosition}.");
            Plan(TargetPosition, maxGoalSearchRadius);
        }

        bool Plan(Vector3 goal, float goalRadius)
        {
            lastPlanTime = Time.time;
            pendingReplan = false;
            plannedGoal = goal;
            if (!grid.IsBuilt) return false;
            if (!grid.FindPath(body.position, goal, path, goalRadius))
            {
                path.Clear();
                if (logDecisions) Debug.LogWarning($"[Jeep A*] No safe route to {goal} (expanded {grid.LastExpandedNodes} nodes). Waiting for a new report.");
                return false;
            }
            pathIndex = path.Count > 1 ? 1 : 0;
            ReplanCount++;
            if (logDecisions)
                Debug.Log($"[Jeep A*] Route planned: {grid.LastRawPath.Count} grid cells → {path.Count} waypoints, " +
                          $"cost {grid.LastPathCost:F1}, expanded {grid.LastExpandedNodes} nodes in {grid.LastSearchMilliseconds:F2} ms.");
            return true;
        }

        void FixedUpdate()
        {
            if (!grid.IsBuilt) return;
            float dt = Time.fixedDeltaTime;
            switch (CurrentState)
            {
                case State.Patrol:
                    return;

                case State.Pursuit:
                    // Re-plan when the drone reports a new position, or retry when no route was found.
                    if ((pendingReplan || path.Count == 0) && Time.time - lastPlanTime >= minReplanInterval)
                        Plan(TargetPosition, maxGoalSearchRadius);
                    bool arrived = Drive(dt, pursuitSpeed, TargetPosition, stopDistanceFromPlayer);
                    if (arrived)
                    {
                        FaceTowards(TargetPosition, dt);
                        if (!DroneHasPlayer)
                        {
                            CurrentState = State.Search;
                            searchTimer = 0;
                            if (logDecisions) Debug.Log("[Jeep A*] Reached last known position – searching.");
                        }
                    }
                    return;

                case State.Search:
                    Brake(dt);
                    searchTimer += dt;
                    if (searchTimer >= searchSeconds) BeginReturn();
                    return;

                case State.Return:
                    if (Drive(dt, returnSpeed, path.Count > 0 ? path[path.Count - 1] : body.position, .35f))
                        ResumePatrol();
                    return;
            }
        }

        void BeginReturn()
        {
            if (!patrol || patrol.waypoints == null || patrol.waypoints.Length == 0)
            {
                CurrentState = State.Patrol;
                return;
            }
            // Head for the nearest patrol waypoint, then continue the route from the next one.
            returnWaypoint = -1;
            float best = float.PositiveInfinity;
            for (int i = 0; i < patrol.waypoints.Length; i++)
            {
                var w = patrol.waypoints[i];
                if (!w) continue;
                float d = Flat(w.position - body.position).sqrMagnitude;
                if (d < best && grid.IsTraversable(w.position)) { best = d; returnWaypoint = i; }
            }
            if (returnWaypoint < 0 || !Plan(patrol.waypoints[returnWaypoint].position, 2f))
            {
                searchTimer = searchSeconds * .5f; // try again shortly
                return;
            }
            // Finish exactly on the route point so the patrol can take over without a jump.
            path[path.Count - 1] = patrol.waypoints[returnWaypoint].position;
            CurrentState = State.Return;
            if (logDecisions) Debug.Log($"[Jeep A*] Search over – returning to patrol waypoint {returnWaypoint}.");
        }

        void ResumePatrol()
        {
            CurrentState = State.Patrol;
            path.Clear();
            if (patrol)
            {
                patrol.ResumeAt((returnWaypoint + 1) % patrol.waypoints.Length, CurrentSpeed);
                patrol.enabled = true;
            }
            CurrentSpeed = 0;
            if (logDecisions) Debug.Log("[Jeep A*] Back on the perimeter road – patrol resumed.");
        }

        // =====================================================================================
        // ACT – path following with speed control and collision safety
        // =====================================================================================

        /// <returns>True when the jeep has stopped at the destination.</returns>
        bool Drive(float dt, float maxSpeed, Vector3 finalTarget, float stopDistance)
        {
            if (path.Count == 0) { Brake(dt); return CurrentSpeed < .05f; }

            Vector3 position = body.position;
            while (pathIndex < path.Count - 1 && Flat(path[pathIndex] - position).magnitude < waypointRadius)
                pathIndex++;

            Vector3 toPoint = Flat(path[pathIndex] - position);
            float pointDistance = toPoint.magnitude;

            // Distance left until the jeep must stand still.
            float remaining = pointDistance;
            for (int i = pathIndex + 1; i < path.Count; i++) remaining += Flat(path[i] - path[i - 1]).magnitude;
            float toStop = Mathf.Min(remaining, Flat(finalTarget - position).magnitude - stopDistance);
            if (toStop <= .05f || pointDistance < .05f)
            {
                Brake(dt);
                return CurrentSpeed < .05f;
            }

            Vector3 direction = toPoint / pointDistance;

            // Speed planning: brake for the end of the route and for the next corner.
            float desired = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * braking * toStop));
            if (pathIndex < path.Count - 1)
            {
                Vector3 outgoing = Flat(path[pathIndex + 1] - path[pathIndex]);
                float corner = outgoing.sqrMagnitude > .001f ? Vector3.Angle(direction, outgoing) : 0f;
                float cornerSpeed = maxSpeed * Mathf.Lerp(1f, cornerSlowdown, Mathf.Clamp01(corner / 90f));
                desired = Mathf.Min(desired, Mathf.Sqrt(cornerSpeed * cornerSpeed + 2f * braking * pointDistance));
            }
            // Slow down while the nose is not yet pointing along the route.
            float heading = Vector3.Angle(Flat(transform.forward), direction);
            desired *= Mathf.Lerp(1f, .25f, Mathf.Clamp01(heading / 90f));

            // Steering: like a real vehicle, turning is slower at very low speed.
            float turnRate = turnSpeed * Mathf.Clamp01(.35f + CurrentSpeed / 4f);
            Quaternion rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(direction), turnRate * dt);
            Vector3 forward = rotation * Vector3.forward;

            // Collision safety: look ahead by braking distance and slow/stop for anything in the way.
            float brakingDistance = CurrentSpeed * CurrentSpeed / (2f * braking);
            if (CheckAhead(position, rotation, forward, brakingDistance + obstacleLookAhead + CurrentSpeed * dt, out var hit, out bool isPlayer))
            {
                if (isPlayer) { Brake(dt); return true; } // the soldier is right in front: arrived
                float freeDistance = Mathf.Max(0f, hit.distance - .4f);
                desired = Mathf.Min(desired, Mathf.Sqrt(2f * braking * freeDistance));
                BlockedBy = hit.collider.name;
                if (freeDistance < .3f)
                {
                    CurrentSpeed = 0;
                    blockedTimer += dt;
                    if (blockedTimer >= blockedReplanSeconds)
                    {
                        // Unknown obstacle on our route: add it to the map and let A* find a way round.
                        blockedTimer = 0;
                        if (logDecisions) Debug.Log($"[Jeep A*] Route blocked by '{hit.collider.name}' – marking it and re-planning.");
                        grid.MarkTemporaryObstacle(hit.collider.bounds, temporaryObstacleSeconds);
                        Plan(plannedGoal, CurrentState == State.Return ? 2f : maxGoalSearchRadius);
                    }
                    return false;
                }
            }
            else BlockedBy = "";
            blockedTimer = 0;

            float rate = desired < CurrentSpeed ? braking : acceleration;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, desired, rate * dt);
            Vector3 next = position + forward * (CurrentSpeed * dt);
            next.y = GroundHeight(next, position.y);
            body.MovePosition(next);
            body.MoveRotation(rotation);
            return false;
        }

        bool CheckAhead(Vector3 position, Quaternion rotation, Vector3 forward, float distance, out RaycastHit result, out bool isPlayer)
        {
            result = default; isPlayer = false;
            if (!footprint) return false;
            Vector3 scale = transform.lossyScale;
            Vector3 size = Vector3.Scale(footprint.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 center = position + rotation * (Vector3.Scale(footprint.center, scale) + Vector3.up * .25f);
            Vector3 half = new Vector3(size.x * .5f, size.y * .4f, size.z * .5f);
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.BoxCastAll(center, half, forward, rotation, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (IsGround(hit.collider) || hit.normal.y > .7f) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    result = hit;
                    isPlayer = IsPlayer(hit.collider);
                }
            }
            return nearest < float.PositiveInfinity;
        }

        bool IsGround(Collider c)
        {
            if (c is TerrainCollider) return true;
            var surfaces = grid.roadSurfaces;
            if (surfaces != null) for (int i = 0; i < surfaces.Length; i++) if (surfaces[i] == c) return true;
            return false;
        }

        bool IsPlayer(Collider c)
        {
            if (!player)
            {
                var fp = FindAnyObjectByType<FirstPersonController>();
                if (fp) player = fp.transform;
            }
            return (player && c.transform.IsChildOf(player)) || c.CompareTag("Player");
        }

        float GroundHeight(Vector3 at, float currentY)
        {
            float best = currentY; float bestDelta = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(at + Vector3.up * 2f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.normal.y < .7f || hit.collider.attachedRigidbody) continue;
                float delta = Mathf.Abs(hit.point.y - currentY);
                if (delta < .6f && delta < bestDelta) { bestDelta = delta; best = hit.point.y + .02f; }
            }
            return best;
        }

        void Brake(float dt)
        {
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0, braking * dt);
            if (CurrentSpeed > .01f)
            {
                Vector3 next = body.position + transform.forward * (CurrentSpeed * dt);
                next.y = GroundHeight(next, body.position.y);
                body.MovePosition(next);
            }
        }

        void FaceTowards(Vector3 target, float dt)
        {
            Vector3 d = Flat(target - body.position);
            if (d.sqrMagnitude < .01f || CurrentSpeed > .5f) return;
            // Kept very slow: a parked vehicle only "creeps" round to watch the player.
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(d), 20f * dt));
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        // =====================================================================================
        // Debug
        // =====================================================================================

        void OnDrawGizmosSelected()
        {
            if (CurrentState == State.Patrol) return;
            Gizmos.color = CurrentState == State.Pursuit ? Color.red : CurrentState == State.Search ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(TargetPosition, stopDistanceFromPlayer);
            if (path.Count > 0 && pathIndex < path.Count)
                Gizmos.DrawLine(transform.position + Vector3.up, path[pathIndex] + Vector3.up);
        }

        GUIStyle overlayStyle;
        void OnGUI()
        {
            if (!showStatusOverlay || CurrentState == State.Patrol) return;
            overlayStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(1f, .85f, .3f) } };
            string text = $"JEEP A*: {CurrentState.ToString().ToUpper()}  |  speed {CurrentSpeed:F1} m/s  |  " +
                          $"drone contact: {(DroneHasPlayer ? "YES" : "lost")}  |  route {path.Count} pts, " +
                          $"expanded {grid.LastExpandedNodes}, re-plans {ReplanCount}" +
                          (string.IsNullOrEmpty(BlockedBy) ? "" : $"  |  yielding to {BlockedBy}");
            GUI.Label(new Rect(12, Screen.height - 30, Screen.width - 24, 24), text, overlayStyle);
        }
    }

    /// <summary>
    /// Adds the A* pursuit to the existing patrol jeep and the drone message sender to the
    /// surveillance drone when a scene starts, so no scene file edits are required.
    /// Use "Operation 1989/Jeep/Install A* Drone Pursuit" to add them permanently instead.
    /// </summary>
    public static class JeepPursuitBootstrap
    {
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialise()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            InstallAll();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallAll();

        public static void InstallAll()
        {
            if (!Enabled) return;
            bool anyJeep = false;
            foreach (var patrol in Object.FindObjectsByType<BaseRoadJeepPatrol>())
            {
                anyJeep = true;
                if (!patrol.GetComponent<JeepAStarPursuit>()) patrol.gameObject.AddComponent<JeepAStarPursuit>();
            }
            if (!anyJeep) return;
            foreach (var drone in Object.FindObjectsByType<DroneMapPatrol>())
            {
                if (!DroneAlertSystem.Instance && !drone.GetComponent<DroneAlertSystem>())
                    drone.gameObject.AddComponent<DroneAlertSystem>();
                if (!drone.GetComponent<DronePlayerSurveillance>())
                    drone.gameObject.AddComponent<DronePlayerSurveillance>();
            }
        }
    }
}

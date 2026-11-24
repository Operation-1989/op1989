using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Operation1989.Interaction.AI;
using Operation1989.AI.Drone;

namespace Operation1989.AI.Jeep
{
    /// <summary>One obstacle-aware driver owns patrol, interception, search and return.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(JeepNavigationGrid), typeof(Rigidbody), typeof(JeepDamage))]
    public sealed class JeepAStarPursuit : MonoBehaviour
    {
        public enum State { Patrol, Pursuit, Search, Return, Disabled }
        [Header("Independent visual detection")]
        public float sightRange = 48f;
        [Range(30, 180)] public float sightAngle = 130f;
        public bool HasDirectContact { get; private set; }
        public bool HasContact => DroneHasPlayer || HasDirectContact;
        public float pursuitSpeed = 10f, returnSpeed = 6f, acceleration = 4f, braking = 7f, turnSpeed = 110f;
        public float waypointRadius = .65f;
        [Range(.1f, 1)] public float cornerSlowdown = .35f;
        [Tooltip("Minimum distance to the player when choosing an interception point.")]
        public float stopDistanceFromPlayer = 6f;
        public float obstacleLookAhead = 1.2f, blockedReplanSeconds = 1f, temporaryObstacleSeconds = 3f;
        public float replanDistance = 1.25f, minReplanInterval = .35f;
        public float searchSeconds = 8f, maxGoalSearchRadius = 20f;
        [Tooltip("Entrance the jeep should prevent the player from reaching. Resolved automatically in army base.")]
        public Transform mainBuildingEntrance;
        public bool showStatusOverlay, logDecisions;
        public State CurrentState { get; private set; } = State.Patrol;
        public float CurrentSpeed { get; private set; }
        public Vector3 TargetPosition { get; private set; }
        public Vector3 InterceptionPosition { get; private set; }
        public bool DroneHasPlayer { get; private set; }
        public string BlockedBy { get; private set; } = "";
        public int ReplanCount { get; private set; }
        public IReadOnlyList<Vector3> Path => path;
        public bool IsDriving => ready && isActiveAndEnabled;

        BaseRoadJeepPatrol patrol;
        JeepNavigationGrid grid;
        Rigidbody body;
        BoxCollider footprint;
        DroneAlertSystem alerts;
        JeepDamage damage;
        FirstPersonController player;
        IntroTruckSequence intro;
        Vector3 droneReportPosition;
        float lastDroneReportTime = -999f, nextSightScan;
        readonly List<Vector3> path = new List<Vector3>();
        int pathIndex, patrolIndex;
        Vector3 plannedGoal, observedVelocity, lastPosition;
        float lastPlanTime = -999f, lastReportTime = -999f, blockedTimer, searchTimer, unreachableSince = -1;
        bool ready, pendingReplan;

        void Awake()
        {
            patrol = GetComponent<BaseRoadJeepPatrol>(); grid = GetComponent<JeepNavigationGrid>();
            body = GetComponent<Rigidbody>(); footprint = GetComponent<BoxCollider>();
            damage = GetComponent<JeepDamage>();
        }
        void Start()
        {
            patrolIndex = patrol ? patrol.TargetPoint : 0;
            CurrentSpeed = patrol ? patrol.CurrentSpeed : 0;
            if (patrol) patrol.enabled = false;
            // MovePosition/MoveRotation provide a single deterministic owner of the vehicle.
            body.isKinematic = true; body.useGravity = false;
            if (!mainBuildingEntrance)
            {
                var door = GameObject.Find("Main Building Entrance Checkpoint");
                if (!door) door = GameObject.Find("building_1");
                if (door) mainBuildingEntrance = door.transform;
            }
            ConfigureGrid(grid, patrol, footprint, transform);
            grid.Build(transform, body.position.y, PatrolBounds(patrol));
            lastPosition = body.position;
            ready = true;
            Subscribe();
        }
        public static void ConfigureGrid(JeepNavigationGrid grid, BaseRoadJeepPatrol patrol, BoxCollider footprint, Transform jeep)
        {
            if ((grid.roadSurfaces == null || grid.roadSurfaces.Length == 0) && patrol) grid.roadSurfaces = patrol.groundSurfaces;
            if (footprint)
            {
                Vector3 size = Vector3.Scale(footprint.size, jeep.lossyScale);
                // A circumscribed footprint remains safe while turning, including the rear corners.
                grid.vehicleHalfWidth = Mathf.Max(.5f, new Vector2(size.x, size.z).magnitude * .5f);
            }
        }
        public static Bounds? PatrolBounds(BaseRoadJeepPatrol patrol)
        {
            Bounds? bounds = null;
            if (patrol && patrol.waypoints != null)
                foreach (var point in patrol.waypoints)
                {
                    if (!point) continue;
                    var value = bounds ?? new Bounds(point.position, Vector3.zero);
                    value.Encapsulate(point.position); bounds = value;
                }
            return bounds;
        }
        void Subscribe()
        {
            if (alerts == DroneAlertSystem.Instance) return;
            Unsubscribe(); alerts = DroneAlertSystem.Instance;
            if (!alerts) { HandlePlayerLost(); return; }
            alerts.OnPlayerSpottedByDrone += HandlePlayerSpotted;
            alerts.OnPlayerLostByDrone += HandlePlayerLost;
            if (alerts.IsPlayerSpotted) HandlePlayerSpotted(alerts.LastSpottedPosition);
        }
        void Unsubscribe()
        {
            if (alerts)
            {
                alerts.OnPlayerSpottedByDrone -= HandlePlayerSpotted;
                alerts.OnPlayerLostByDrone -= HandlePlayerLost;
            }
            alerts = null;
        }
        void Update()
        {
            Subscribe();
            if (DroneHasPlayer && Time.time - lastDroneReportTime > DroneAlertSystem.ContactTimeout) HandlePlayerLost();
            if (damage && damage.Disabled) { HasDirectContact = false; return; }
            if (Time.time < nextSightScan) return;
            nextSightScan = Time.time + .15f;
            if (!player) player = Object.FindAnyObjectByType<FirstPersonController>();
            if (!intro) intro = Object.FindAnyObjectByType<IntroTruckSequence>();
            var health = player ? player.GetComponent<CombatHealth>() : null;
            bool canObserve = player && player.isActiveAndEnabled && (!health || !health.Dead) &&
                (!intro || (intro.HandedOff && intro.Entered));
            RefreshVisualContact(canObserve ? player.transform : null);
        }
        public bool RefreshVisualContact(Transform target)
        {
            if (damage && damage.Disabled) { HasDirectContact = false; return false; }
            HasDirectContact = HasDirectSight(target);
            if (HasDirectContact) UpdateContact(target.position);
            else if (DroneHasPlayer) UpdateContact(droneReportPosition);
            else observedVelocity = Vector3.zero;
            return HasDirectContact;
        }
        public bool HasDirectSight(Transform target)
        {
            if (!target) return false;
            Vector3 origin = transform.position + Vector3.up * 1.8f;
            Bounds targetBounds = SightBounds(target);
            Vector3 delta = targetBounds.center - origin;
            if (delta.magnitude > sightRange || delta.sqrMagnitude < .001f) return false;
            // Once alerted, the driver watches around the last contact instead of
            // forgetting a visible player when the jeep parks broadside to block.
            bool watchingContact = Time.time - lastReportTime <= searchSeconds &&
                (CurrentState == State.Pursuit || CurrentState == State.Search);
            if (!watchingContact && delta.magnitude > 8f &&
                Vector3.Angle(Flat(transform.forward), Flat(delta)) > sightAngle * .5f) return false;
            // Sample the torso and shoulders so a little low cover does not hide
            // the entire player. Every accepted sample still needs clear sight.
            foreach (float height in new[] { 0f, .55f, -.25f })
            {
                Vector3 aim = targetBounds.center + Vector3.up * (targetBounds.extents.y * height);
                Vector3 ray = aim - origin;
                if (RifleWeapon.Trace(origin, ray.normalized, ray.magnitude + .05f, transform, out var hit) &&
                    (hit.transform == target || hit.transform.IsChildOf(target))) return true;
            }
            return false;
        }
        static Bounds SightBounds(Transform target)
        {
            var character = target.GetComponent<CharacterController>();
            if (character)
            {
                // Use its actual center: the army-base player root is mid-body,
                // rather than at the feet as the previous fixed offset assumed.
                Vector3 scale = target.lossyScale;
                return new Bounds(target.TransformPoint(character.center), new Vector3(
                    character.radius * 2 * Mathf.Abs(scale.x), character.height * Mathf.Abs(scale.y),
                    character.radius * 2 * Mathf.Abs(scale.z)));
            }
            var collider = target.GetComponent<Collider>();
            if (!collider) collider = target.GetComponentInChildren<Collider>();
            return collider ? collider.bounds : new Bounds(target.position, Vector3.one);
        }
        void OnDisable()
        {
            Unsubscribe();
            if (ready && patrol && gameObject.activeInHierarchy) patrol.enabled = true;
        }
        public void HandlePlayerSpotted(Vector3 position)
        {
            droneReportPosition = position; lastDroneReportTime = Time.time; DroneHasPlayer = true;
            if (!HasDirectContact) UpdateContact(position);
        }
        void UpdateContact(Vector3 position)
        {
            if (damage && damage.Disabled) return;
            float elapsed = Time.time - lastReportTime;
            observedVelocity = elapsed > .05f && elapsed < DroneAlertSystem.ContactTimeout
                ? Vector3.ClampMagnitude(Flat(position - TargetPosition) / elapsed, 8f) : Vector3.zero;
            TargetPosition = position; lastReportTime = Time.time;
            InterceptionPosition = Intercept(position);
            bool changedState = CurrentState != State.Pursuit;
            CurrentState = State.Pursuit; searchTimer = 0;
            pendingReplan |= changedState || Flat(InterceptionPosition - plannedGoal).magnitude >= replanDistance;
            if (ready && changedState) Plan(InterceptionPosition, maxGoalSearchRadius);
        }
        public void HandlePlayerLost() { DroneHasPlayer = false; if (!HasDirectContact) observedVelocity = Vector3.zero; }
        Vector3 Intercept(Vector3 report)
        {
            if (!mainBuildingEntrance) return report;
            Vector3 toDoor = Flat(mainBuildingEntrance.position - report);
            float distance = toDoor.magnitude;
            if (distance < .1f) return report;
            float travelTime = Flat(report - body.position).magnitude / Mathf.Max(1, pursuitSpeed);
            float approachSpeed = Mathf.Max(0, Vector3.Dot(observedVelocity, toDoor.normalized));
            float lead = Mathf.Clamp(stopDistanceFromPlayer + approachSpeed * travelTime * .5f, 3f, 12f);
            return report + toDoor.normalized * Mathf.Min(lead, distance * .75f);
        }
        bool Plan(Vector3 goal, float radius)
        {
            lastPlanTime = Time.time; plannedGoal = goal; pendingReplan = false;
            bool found = grid.IsBuilt && grid.FindPath(body.position, goal, path, radius, CurrentState == State.Pursuit);
            if (!found)
            {
                path.Clear(); if (unreachableSince < 0) unreachableSince = Time.time; return false;
            }
            unreachableSince = -1;
            // The first point is the real vehicle position. Never substitute an unchecked final point.
            pathIndex = path.Count > 1 ? 1 : 0;
            ReplanCount++; return true;
        }
        void FixedUpdate()
        {
            if (!ready || !grid.IsBuilt) return;
            if (damage && damage.Disabled)
            {
                CurrentState = State.Disabled; CurrentSpeed = 0; path.Clear();
                if (patrol) patrol.ReportNavigation(0, 0, patrolIndex);
                lastPosition = body.position;
                return;
            }
            if (CurrentState == State.Disabled)
            {
                CurrentSpeed = 0; nextSightScan = 0;
                if (HasContact) UpdateContact(HasDirectContact && player ? player.transform.position : droneReportPosition);
                else BeginReturn();
            }
            float dt = Time.fixedDeltaTime;
            Vector3 previous = body.position;
            switch (CurrentState)
            {
                case State.Patrol:
                    Patrol(dt); break;
                case State.Pursuit:
                    if (!HasContact && path.Count == 0 && unreachableSince >= 0 && Time.time - unreachableSince >= searchSeconds)
                    { BeginReturn(); break; }
                    if ((pendingReplan || path.Count == 0) && Time.time - lastPlanTime >= minReplanInterval)
                        Plan(HasContact ? InterceptionPosition : TargetPosition, maxGoalSearchRadius);
                    if (Drive(dt, pursuitSpeed))
                    {
                        if (HasContact) BlockApproach(dt);
                        else { CurrentState = State.Search; searchTimer = 0; }
                    }
                    break;
                case State.Search:
                    Brake(dt); searchTimer += dt;
                    if (searchTimer >= searchSeconds) BeginReturn();
                    break;
                case State.Return:
                    if (path.Count == 0 && Time.time - lastPlanTime >= 1f) BeginReturn();
                    if (Drive(dt, returnSpeed)) { CurrentState = State.Patrol; AdvancePatrol(); }
                    break;
            }
            if (patrol) patrol.ReportNavigation(CurrentSpeed, Vector3.Distance(lastPosition, previous), patrolIndex);
            lastPosition = previous;
        }
        void Patrol(float dt)
        {
            if (!patrol || patrol.waypoints == null || patrol.waypoints.Length == 0) { Brake(dt); return; }
            patrolIndex = ((patrolIndex % patrol.waypoints.Length) + patrol.waypoints.Length) % patrol.waypoints.Length;
            if (path.Count == 0 && Time.time - lastPlanTime >= .5f)
            {
                var waypoint = patrol.waypoints[patrolIndex];
                if (!waypoint || !Plan(waypoint.position, 10f)) { AdvancePatrol(); return; }
            }
            if (Drive(dt, patrol.speed)) AdvancePatrol();
        }
        void AdvancePatrol()
        {
            path.Clear(); lastPlanTime = -999f;
            if (!patrol || patrol.waypoints == null || patrol.waypoints.Length == 0) return;
            patrolIndex = (patrolIndex + 1) % patrol.waypoints.Length;
            if (patrolIndex == 1) patrol.RecordCompletedLap();
        }
        void BeginReturn()
        {
            CurrentState = State.Return; path.Clear();
            if (!patrol || patrol.waypoints == null || patrol.waypoints.Length == 0)
            { CurrentState = State.Patrol; return; }
            var candidates = new List<int>();
            for (int i = 0; i < patrol.waypoints.Length; i++) if (patrol.waypoints[i]) candidates.Add(i);
            candidates.Sort((a, b) => Flat(patrol.waypoints[a].position - body.position).sqrMagnitude.CompareTo(
                Flat(patrol.waypoints[b].position - body.position).sqrMagnitude));
            foreach (int candidate in candidates)
                if (Plan(patrol.waypoints[candidate].position, 10f)) { patrolIndex = candidate; return; }
            lastPlanTime = Time.time;
        }
        bool Drive(float dt, float maximumSpeed)
        {
            // A failed route is not an arrival. Stay stopped and retry instead of skipping the obstacle.
            if (path.Count == 0) { Brake(dt); return false; }
            Vector3 position = body.position;
            while (pathIndex < path.Count - 1 && Flat(path[pathIndex] - position).magnitude < Mathf.Min(waypointRadius, .65f)) pathIndex++;
            Vector3 delta = Flat(path[pathIndex] - position);
            float distance = delta.magnitude;
            float remaining = distance;
            for (int i = pathIndex + 1; i < path.Count; i++) remaining += Flat(path[i] - path[i - 1]).magnitude;
            if (remaining < .65f && CurrentState == State.Patrol) return true;
            if (remaining < .45f) { Brake(dt); return CurrentSpeed < .1f; }
            if (distance < .01f) { Brake(dt); return false; }
            Vector3 direction = delta / distance;
            float exitSpeed = 0;
            if (CurrentState == State.Patrol && patrol && patrol.waypoints != null && patrol.waypoints.Length > 0)
            {
                var nextWaypoint = patrol.waypoints[(patrolIndex + 1) % patrol.waypoints.Length];
                if (nextWaypoint)
                {
                    float corner = Vector3.Angle(direction, Flat(nextWaypoint.position - path[path.Count - 1]));
                    exitSpeed = maximumSpeed * Mathf.Lerp(1, cornerSlowdown, Mathf.Clamp01(corner / 90));
                }
            }
            float desired = Mathf.Min(maximumSpeed, Mathf.Sqrt(exitSpeed * exitSpeed + 2f * braking * Mathf.Max(0, remaining - .25f)));
            if (pathIndex < path.Count - 1)
            {
                float corner = Vector3.Angle(direction, Flat(path[pathIndex + 1] - path[pathIndex]));
                float cornerSpeed = maximumSpeed * Mathf.Lerp(1, cornerSlowdown, Mathf.Clamp01(corner / 90));
                desired = Mathf.Min(desired, Mathf.Sqrt(cornerSpeed * cornerSpeed + 2 * braking * distance));
            }
            float heading = Vector3.Angle(Flat(transform.forward), direction);
            if (!grid.IsTraversable(position) && heading > 100)
            {
                // A stopped jeep may be inside an inflated wall margin. Back out without
                // rotating its rear corners through the wall before joining the safe route.
                Vector3 reverse = -(body.rotation * Vector3.forward);
                float step = Mathf.Min(2f, maximumSpeed) * dt;
                if (!CheckAhead(position, body.rotation, reverse, step + .5f, out _))
                {
                    Vector3 retreat = position + reverse * step; retreat.y = GroundHeight(retreat, position.y);
                    body.MovePosition(retreat); CurrentSpeed = step / dt; return false;
                }
            }
            if (heading > 55) desired = 0;
            else desired *= Mathf.Lerp(1, .35f, heading / 55);
            Quaternion rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(direction), turnSpeed * dt);
            Vector3 forward = rotation * Vector3.forward;
            float lookAhead = CurrentSpeed * CurrentSpeed / (2 * braking) + obstacleLookAhead + CurrentSpeed * dt;
            if (CheckAhead(position, rotation, forward, lookAhead, out var hit))
            {
                float free = Mathf.Max(0, hit.distance - .3f);
                desired = Mathf.Min(desired, Mathf.Sqrt(2 * braking * free));
                BlockedBy = hit.collider.name;
                if (free < .35f)
                {
                    CurrentSpeed = 0; blockedTimer += dt;
                    // Turning in place at a waypoint is safe within the circular grid clearance.
                    if (RotationClear(position, rotation)) body.MoveRotation(rotation);
                    if (blockedTimer >= blockedReplanSeconds)
                    {
                        blockedTimer = 0;
                        // A combined building collider can span the whole base. Mark the contact
                        // neighbourhood, not its enormous bounding box (which would erase all routes).
                        var bounds = hit.collider.bounds;
                        if (bounds.size.x > 12 || bounds.size.z > 12) bounds = new Bounds(hit.point, new Vector3(2, 2, 2));
                        grid.MarkTemporaryObstacle(bounds, temporaryObstacleSeconds);
                        Plan(plannedGoal, CurrentState == State.Patrol || CurrentState == State.Return ? 10 : maxGoalSearchRadius);
                    }
                    return false;
                }
            }
            else { BlockedBy = ""; blockedTimer = 0; }
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, desired, (desired < CurrentSpeed ? braking : acceleration) * dt);
            Vector3 next = position + forward * Mathf.Min(distance, CurrentSpeed * dt);
            next.y = GroundHeight(next, position.y);
            if (!RotationClear(position, rotation)) { CurrentSpeed = 0; return false; }
            body.MoveRotation(rotation); body.MovePosition(next);
            return false;
        }
        bool CheckAhead(Vector3 position, Quaternion rotation, Vector3 forward, float distance, out RaycastHit result)
        {
            result = default;
            if (!footprint) return false;
            Vector3 scale = transform.lossyScale;
            Vector3 size = Vector3.Scale(footprint.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 center = position + rotation * Vector3.Scale(footprint.center, scale) + Vector3.up * .25f;
            Vector3 half = new Vector3(size.x * .5f, size.y * .4f, size.z * .5f);
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.BoxCastAll(center, half, forward, rotation, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.normal.y > .7f || hit.collider is TerrainCollider) continue;
                if (grid.roadSurfaces != null && System.Array.IndexOf(grid.roadSurfaces, hit.collider) >= 0 && hit.normal.y > .5f) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance; result = hit;
            }
            return nearest < float.PositiveInfinity;
        }
        bool RotationClear(Vector3 position, Quaternion rotation)
        {
            if (!footprint) return true;
            Vector3 scale = transform.lossyScale;
            Vector3 size = Vector3.Scale(footprint.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 center = position + rotation * Vector3.Scale(footprint.center, scale) + Vector3.up * .25f;
            Vector3 half = new Vector3(size.x * .48f, size.y * .4f, size.z * .48f);
            foreach (var collider in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (collider.transform.IsChildOf(transform) || collider is TerrainCollider || collider.bounds.max.y <= position.y + .35f) continue;
                return false;
            }
            return true;
        }
        float GroundHeight(Vector3 position, float currentY)
        {
            float best = currentY, closest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 2, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.normal.y < .85f || hit.collider.GetComponentInParent<CombatHealth>()) continue;
                if (hit.collider.attachedRigidbody && !hit.collider.attachedRigidbody.isKinematic) continue;
                float delta = Mathf.Abs(hit.point.y - currentY);
                if (delta < .6f && delta < closest) { closest = delta; best = hit.point.y + .02f; }
            }
            return best;
        }
        void Brake(float dt) { CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0, braking * dt); }
        void BlockApproach(float dt)
        {
            if (!mainBuildingEntrance) return;
            Vector3 approach = Flat(mainBuildingEntrance.position - TargetPosition);
            if (approach.sqrMagnitude < .1f) return;
            // Park across the player's route, using the jeep's length as the barrier.
            Quaternion broadside = Quaternion.LookRotation(Vector3.Cross(Vector3.up, approach).normalized);
            Quaternion rotation = Quaternion.RotateTowards(body.rotation, broadside, turnSpeed * dt);
            if (RotationClear(body.position, rotation)) body.MoveRotation(rotation);
        }
        static Vector3 Flat(Vector3 value) { value.y = 0; return value; }
        void OnDrawGizmosSelected()
        {
            if (path.Count < 2) return;
            Gizmos.color = CurrentState == State.Pursuit ? Color.red : Color.green;
            for (int i = 1; i < path.Count; i++) Gizmos.DrawLine(path[i - 1] + Vector3.up, path[i] + Vector3.up);
        }
        void OnGUI()
        {
            if (showStatusOverlay && ready) GUI.Label(new Rect(12, Screen.height - 30, Screen.width - 24, 24),
                $"JEEP: {CurrentState} | contact: {DroneHasPlayer} | route: {path.Count} | blocked: {BlockedBy}");
        }
    }

    /// <summary>Connect the active tactical drone and keep exactly one driver per vehicle.</summary>
    public static class JeepPursuitBootstrap
    {
        public static bool Enabled = true;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialise()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; InstallAll();
        }
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallAll();
        public static void InstallAll()
        {
            if (!Enabled) return;
            var drones = Object.FindObjectsByType<TacticalDroneController>();
            if (!Operation1989.AI.InferenceEngine.KnowledgeBase.Instance && !Object.FindAnyObjectByType<Operation1989.AI.InferenceEngine.KnowledgeBase>())
                new GameObject("Agent Knowledge Base").AddComponent<Operation1989.AI.InferenceEngine.KnowledgeBase>();
            if (!DroneAlertSystem.Instance)
            {
                var existing = Object.FindAnyObjectByType<DroneAlertSystem>();
                if (!existing) new GameObject("Drone Pursuit Alerts").AddComponent<DroneAlertSystem>();
            }
            foreach (var drone in drones)
                if (drone.isActiveAndEnabled) Connect(drone.gameObject);
            // Compatibility with the older standalone patrol scene, without enabling its disabled dummy.
            if (drones.Length == 0)
                foreach (var drone in Object.FindObjectsByType<DroneMapPatrol>())
                    if (drone.isActiveAndEnabled) Connect(drone.gameObject);
            foreach (var patrol in Object.FindObjectsByType<BaseRoadJeepPatrol>())
            {
                var legacy = patrol.GetComponent<Operation1989.Interaction.Vehicles.JeepPursuitController>();
                if (legacy) legacy.enabled = false;
                var pursuit = patrol.GetComponent<JeepAStarPursuit>();
                if (!pursuit) pursuit = patrol.gameObject.AddComponent<JeepAStarPursuit>();
                pursuit.enabled = true;
            }
        }
        static void Connect(GameObject drone)
        {
            var legacy = drone.GetComponent<DroneSpotter>();
            if (legacy) legacy.enabled = false;
            var surveillance = drone.GetComponent<DronePlayerSurveillance>();
            if (!surveillance) surveillance = drone.AddComponent<DronePlayerSurveillance>();
            surveillance.scanInterval = .1f; surveillance.messageInterval = .2f; surveillance.enabled = true;
        }
    }
}

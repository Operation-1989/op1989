using UnityEngine;
using Operation1989.Interaction.AI;
using Operation1989.Interaction.Vehicles;

namespace Operation1989.AI.Jeep
{
    /// <summary>
    /// Surveillance drone "eyes": while the drone can see the player soldier it sends the
    /// player's position to the patrol jeep through <see cref="DroneAlertSystem"/>.
    ///
    /// The player is seen when he is inside the drone's ground footprint (horizontal radius),
    /// nothing blocks the line of sight (roofs, walls), he is not hiding in the supply truck,
    /// and the intro truck has handed control to the player.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class DronePlayerSurveillance : MonoBehaviour
    {
        [Tooltip("Horizontal radius of the area under the drone it can observe.")]
        [Min(1)] public float detectionRadius = 28f;
        [Min(.05f)] public float scanInterval = .1f;
        [Tooltip("How often the position message is repeated while the player stays visible.")]
        [Min(.1f)] public float messageInterval = .2f;
        public LayerMask lineOfSightMask = ~0;
        [Tooltip("Do not report the player while the intro truck still carries him.")]
        public bool waitForIntroHandOff = true;

        public bool PlayerVisible { get; private set; }

        Operation1989.AI.Drone.TacticalDronePerception perception;
        Transform player;
        CombatHealth playerHealth;
        DroneDamage damage;
        IntroTruckSequence intro;
        DroneAlertSystem alerts;
        float scanTimer, lastMessageTime = -999f;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Start()
        {
            damage = GetComponent<DroneDamage>();
            perception = GetComponent<Operation1989.AI.Drone.TacticalDronePerception>();
            intro = FindAnyObjectByType<IntroTruckSequence>();
        }

        void LateUpdate()
        {
            if (GamePauseMenu.IsPaused) return;
            scanTimer += Time.deltaTime;
            if (scanTimer < scanInterval) return;
            scanTimer = 0;

            if (!alerts) alerts = DroneAlertSystem.Instance ? DroneAlertSystem.Instance : GetComponent<DroneAlertSystem>();
            if (!alerts) return;

            bool visible = CanSeePlayer();
            if (visible)
            {
                if (!PlayerVisible || Time.time - lastMessageTime >= messageInterval)
                {
                    lastMessageTime = Time.time;
                    alerts.AlertJeep(player.position);   // the drone → jeep message
                }
            }
            else if (PlayerVisible)
            {
                alerts.ClearAlert();
            }
            PlayerVisible = visible;
        }

        bool CanSeePlayer()
        {
            if (damage && damage.Destroyed) return false;
            if (waitForIntroHandOff && intro && (!intro.HandedOff || !intro.Entered)) return false;
            if (SupplyTruckStealthZone.IsPlayerConcealed) return false;
            if (perception)
            {
                player = perception.TargetPlayer;
                return perception.isActiveAndEnabled && perception.CanSeePlayer && player;
            }
            if (!player)
            {
                var fp = FindAnyObjectByType<FirstPersonController>();
                if (fp) player = fp.transform;
                else { var tagged = GameObject.FindWithTag("Player"); if (tagged) player = tagged.transform; }
                if (!player) return false;
                playerHealth = player.GetComponentInParent<CombatHealth>();
            }
            if (!player.gameObject.activeInHierarchy || (playerHealth && playerHealth.Dead)) return false;

            Vector3 eye = transform.position;
            var body = player.GetComponent<CharacterController>();
            Vector3 chest = body ? body.bounds.center : player.position + Vector3.up * 1.2f;
            Vector2 flat = new Vector2(chest.x - eye.x, chest.z - eye.z);
            if (flat.magnitude > detectionRadius || chest.y > eye.y) return false;

            // Line of sight: the nearest thing the ray hits (ignoring the drone itself) must be the player.
            Vector3 toPlayer = chest - eye;
            float distance = toPlayer.magnitude;
            int count = Physics.RaycastNonAlloc(eye, toPlayer / distance, hits, distance + .5f, lineOfSightMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity; Transform first = null;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(transform)) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; first = hits[i].transform; }
            }
            return first == null || first.IsChildOf(player) || player.IsChildOf(first);
        }

        void OnDisable()
        {
            if (PlayerVisible && alerts) alerts.ClearAlert();
            PlayerVisible = false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = PlayerVisible ? Color.red : new Color(0f, 1f, 1f, .6f);
            Vector3 ground = transform.position;
            if (Physics.Raycast(transform.position, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)) ground = hit.point;
            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Gizmos.DrawLine(ground + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * detectionRadius,
                                ground + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * detectionRadius);
            }
            Gizmos.DrawLine(transform.position, ground);
        }
    }
}

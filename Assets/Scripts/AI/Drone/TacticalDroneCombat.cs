using UnityEngine;
using Operation1989.AI.InferenceEngine;
using Operation1989.Interaction.AI;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Handles hitscan combat engagement and base alert dispatching without tracking visuals.
    /// </summary>
    [DisallowMultipleComponent]
    public class TacticalDroneCombat : MonoBehaviour
    {
        // Retain legacy references so existing prefabs can shut their tracking visuals off.
        [SerializeField, HideInInspector] private Light searchlight;
        [SerializeField, HideInInspector] private LineRenderer laserTracer;

        [Header("Weapon Systems")]
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private float damagePerShot = 15f;
        [SerializeField] private float fireRate = 0.2f;
        [SerializeField] private int burstSize = 4;
        [SerializeField] private float burstCooldown = 1.5f;

        [Header("Audio")]
        [SerializeField] private AudioClip fireAudioClip;

        private AudioSource audioSource;
        private DroneDamage damageHandler;
        private float nextFireTime = 0f;
        private int currentBurstCount = 0;

        private void Awake()
        {
            damageHandler = GetComponent<DroneDamage>();
            audioSource = GetComponent<AudioSource>();
            if (!audioSource)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 4f;
                audioSource.maxDistance = 80f;
            }

            // Imported drone models can contain directional lights. Those rotate with the
            // aircraft and relight the entire scene, so all flight illumination stays off.
            foreach (var pulse in GetComponentsInChildren<AircraftLightPulse>(true))
                pulse.source = null;
            foreach (var lamp in GetComponentsInChildren<Light>(true))
                lamp.enabled = false;

            DisableTrackingVisuals();
        }

        private void DisableTrackingVisuals()
        {
            if (!searchlight)
            {
                var lightTransform = transform.Find("TacticalSearchlight");
                if (lightTransform) searchlight = lightTransform.GetComponent<Light>();
            }
            if (searchlight) searchlight.enabled = false;
            if (laserTracer) laserTracer.enabled = false;
            var legacyTracer = transform.Find("DroneLaserTracer");
            if (legacyTracer) legacyTracer.gameObject.SetActive(false);
        }

        public void ShutDownAllCombatSystems()
        {
            DisableTrackingVisuals();
            if (audioSource && audioSource.isPlaying) audioSource.Stop();
            enabled = false;
        }

        private void Update()
        {
            if (damageHandler && damageHandler.Destroyed)
            {
                ShutDownAllCombatSystems();
            }
        }

        /// <summary>
        /// Fires a suppressive hitscan burst towards the target.
        /// </summary>
        public void FireSuppressiveBurst(Transform target)
        {
            if (damageHandler && damageHandler.Destroyed) return;
            if (!target || Time.time < nextFireTime) return;

            Vector3 origin = muzzlePoint ? muzzlePoint.position : transform.position;
            Vector3 targetCenter = target.position + Vector3.up * 1.0f;
            Vector3 aimDir = (targetCenter - origin).normalized;

            // Hitscan trace
            if (Physics.Raycast(origin, aimDir, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore))
            {
                var health = hit.collider.GetComponentInParent<CombatHealth>();
                if (health)
                {
                    health.TakeDamage(damagePerShot);
                }
            }

            if (fireAudioClip && audioSource)
            {
                audioSource.PlayOneShot(fireAudioClip, 0.7f);
            }

            currentBurstCount++;
            if (currentBurstCount >= burstSize)
            {
                currentBurstCount = 0;
                nextFireTime = Time.time + burstCooldown;
            }
            else
            {
                nextFireTime = Time.time + fireRate;
            }

            // Alert base pursuit units (Military Jeep)
            if (DroneAlertSystem.Instance)
            {
                DroneAlertSystem.Instance.AlertJeep(target.position);
            }
            if (KnowledgeBase.Instance)
            {
                KnowledgeBase.Instance.AssertFact(Fact.DroneAlertActive);
            }
        }
    }
}

using System;
using UnityEngine;
using Operation1989.AI.InferenceEngine;
using Operation1989.Interaction.AI;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Handles tactical combat engagement, dynamic spotlight targeting, and base alert dispatching.
    /// Manages searchlight color changes (Patrol=Cyan, Investigate=Yellow, Engage=Red) and hitscan suppression bursts.
    /// </summary>
    [DisallowMultipleComponent]
    public class TacticalDroneCombat : MonoBehaviour
    {
        [Header("Spotlight Configuration")]
        [SerializeField] private Light searchlight;
        [SerializeField] private Color patrolColor = new Color(0.92f, 0.95f, 1f); // Natural Soft White (subtle, non-disturbing)
        [SerializeField] private Color investigateColor = new Color(1f, 0.85f, 0.1f); // Amber Yellow
        [SerializeField] private Color engageColor = new Color(1f, 0.1f, 0.1f); // Hostile Red
        [Range(2f, 25f)] [SerializeField] private float lightAimSpeed = 10f;

        [Header("Weapon Systems")]
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private float damagePerShot = 15f;
        [SerializeField] private float fireRate = 0.2f;
        [SerializeField] private int burstSize = 4;
        [SerializeField] private float burstCooldown = 1.5f;

        [Header("Audio & FX")]
        [SerializeField] private AudioClip fireAudioClip;
        [SerializeField] private LineRenderer laserTracer;

        private AudioSource audioSource;
        private DroneDamage damageHandler;
        private float nextFireTime = 0f;
        private int currentBurstCount = 0;
        private Vector3 targetAimPosition;
        private bool isTrackingTarget = false;

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

            if (!searchlight)
            {
                searchlight = GetComponentInChildren<Light>();
            }

            if (!laserTracer)
            {
                var tracerObj = new GameObject("DroneLaserTracer");
                tracerObj.transform.SetParent(transform, false);
                laserTracer = tracerObj.AddComponent<LineRenderer>();
                laserTracer.positionCount = 2;
                laserTracer.startWidth = 0.08f;
                laserTracer.endWidth = 0.04f;
                laserTracer.enabled = false;
                laserTracer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                laserTracer.material.color = Color.red;
            }

            SetSearchlightColor(patrolColor);
        }

        public void SetSearchlightForTesting(Light light)
        {
            searchlight = light;
        }

        public void SetAimTarget(Vector3 aimPos, bool active)
        {
            targetAimPosition = aimPos;
            isTrackingTarget = active;
        }

        public void SetSearchlightState(InferredAction action)
        {
            if (!searchlight)
            {
                var searchlightTrans = transform.Find("TacticalSearchlight");
                if (searchlightTrans) searchlight = searchlightTrans.GetComponent<Light>();
                if (!searchlight) searchlight = GetComponentInChildren<Light>();
                if (!searchlight) return;
            }

            switch (action)
            {
                case InferredAction.EngageRangedCombat:
                    SetSearchlightColor(engageColor);
                    searchlight.intensity = 4.0f;
                    break;
                case InferredAction.InvestigateLastKnownPos:
                    SetSearchlightColor(investigateColor);
                    searchlight.intensity = 2.5f;
                    break;
                default:
                    SetSearchlightColor(patrolColor);
                    searchlight.intensity = 1.2f;
                    break;
            }
        }

        public void ShutDownAllCombatSystems()
        {
            if (searchlight) searchlight.enabled = false;
            if (laserTracer) laserTracer.enabled = false;
            if (audioSource && audioSource.isPlaying) audioSource.Stop();
            isTrackingTarget = false;
            enabled = false;
        }

        private void SetSearchlightColor(Color color)
        {
            if (searchlight)
            {
                searchlight.color = Color.Lerp(searchlight.color, color, Time.deltaTime * 5f);
            }
        }

        private void Update()
        {
            if (damageHandler && damageHandler.Destroyed)
            {
                ShutDownAllCombatSystems();
                return;
            }

            UpdateSearchlightAim();
            if (laserTracer && laserTracer.enabled && Time.time > nextFireTime - 0.08f)
            {
                laserTracer.enabled = false;
            }
        }

        private void UpdateSearchlightAim()
        {
            if (!searchlight) return;

            if (isTrackingTarget)
            {
                Vector3 dir = (targetAimPosition - searchlight.transform.position).normalized;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    searchlight.transform.rotation = Quaternion.Slerp(searchlight.transform.rotation, targetRot, Time.deltaTime * lightAimSpeed);
                }
            }
            else
            {
                // Default downwards tilt forward
                Quaternion neutral = Quaternion.Euler(60f, transform.eulerAngles.y, 0f);
                searchlight.transform.rotation = Quaternion.Slerp(searchlight.transform.rotation, neutral, Time.deltaTime * 2f);
            }
        }

        /// <summary>
        /// Fires suppressive laser pulse towards target.
        /// </summary>
        public void FireSuppressiveBurst(Transform target)
        {
            if (damageHandler && damageHandler.Destroyed) return;
            if (!target || Time.time < nextFireTime) return;

            Vector3 origin = muzzlePoint ? muzzlePoint.position : transform.position;
            Vector3 targetCenter = target.position + Vector3.up * 1.0f;
            Vector3 aimDir = (targetCenter - origin).normalized;

            // Hitscan trace
            Vector3 hitPoint = origin + aimDir * 50f;
            if (Physics.Raycast(origin, aimDir, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore))
            {
                hitPoint = hit.point;
                var health = hit.collider.GetComponentInParent<CombatHealth>();
                if (health)
                {
                    health.TakeDamage(damagePerShot);
                }
            }

            // Visual Tracer
            if (laserTracer)
            {
                laserTracer.SetPosition(0, origin);
                laserTracer.SetPosition(1, hitPoint);
                laserTracer.enabled = true;
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

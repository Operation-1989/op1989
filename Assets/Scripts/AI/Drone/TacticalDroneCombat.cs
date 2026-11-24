using UnityEngine;
using Operation1989.AI.InferenceEngine;
using Operation1989.Interaction.AI;

namespace Operation1989.AI.Drone
{
    /// <summary>
    /// Handles hitscan laser beam combat engagement and base alert dispatching.
    /// Fires red laser beams at the player when spotted, dealing damage and showing visible beam tracers.
    /// </summary>
    [DisallowMultipleComponent]
    public class TacticalDroneCombat : MonoBehaviour
    {
        [Header("Weapon Systems")]
        [SerializeField] private Transform muzzlePoint;
        [Tooltip("Damage dealt per laser beam hit.")]
        [SerializeField] private float damagePerShot = 10f;
        [Tooltip("Interval between laser beam shots in seconds.")]
        [SerializeField] private float fireCooldown = 4.0f;
        [Tooltip("Laser beam visible duration in seconds.")]
        [SerializeField] private float beamDuration = 0.25f;

        [Header("Audio & FX")]
        [SerializeField] private AudioClip fireAudioClip;
        [SerializeField] private LineRenderer laserTracer;

        private AudioSource audioSource;
        private DroneDamage damageHandler;
        private float nextFireTime = 0f;
        private float beamHideTime = 0f;

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

            if (!laserTracer)
            {
                var tracerObj = transform.Find("DroneLaserTracer");
                if (tracerObj)
                {
                    laserTracer = tracerObj.GetComponent<LineRenderer>();
                }
                else
                {
                    var newTracer = new GameObject("DroneLaserTracer");
                    newTracer.transform.SetParent(transform, false);
                    laserTracer = newTracer.AddComponent<LineRenderer>();
                }
            }

            if (laserTracer)
            {
                laserTracer.positionCount = 2;
                laserTracer.startWidth = 0.12f;
                laserTracer.endWidth = 0.08f;
                laserTracer.useWorldSpace = true;
                laserTracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                laserTracer.receiveShadows = false;
                laserTracer.enabled = false;

                // Use Universal Render Pipeline particles or sprites unlit shader for red laser
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (!shader) shader = Shader.Find("Sprites/Default");
                if (!shader) shader = Shader.Find("Unlit/Color");
                
                if (shader)
                {
                    Material laserMat = new Material(shader);
                    laserMat.color = new Color(1f, 0.1f, 0.1f, 1f);
                    if (laserMat.HasProperty("_BaseColor")) laserMat.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 1f));
                    if (laserMat.HasProperty("_Color")) laserMat.SetColor("_Color", new Color(1f, 0.1f, 0.1f, 1f));
                    if (laserMat.HasProperty("_EmissionColor"))
                    {
                        laserMat.EnableKeyword("_EMISSION");
                        laserMat.SetColor("_EmissionColor", new Color(2f, 0.2f, 0.2f, 1f));
                    }
                    laserTracer.material = laserMat;
                }
            }
        }

        public void ShutDownAllCombatSystems()
        {
            if (laserTracer) laserTracer.enabled = false;
            if (audioSource && audioSource.isPlaying) audioSource.Stop();
            enabled = false;
        }

        private void Update()
        {
            if (damageHandler && damageHandler.Destroyed)
            {
                ShutDownAllCombatSystems();
                return;
            }

            if (laserTracer && laserTracer.enabled && Time.time >= beamHideTime)
            {
                laserTracer.enabled = false;
            }
        }

        /// <summary>
        /// Fires a laser beam towards the target player when spotted.
        /// Deals 10 damage and enforces a 4 second cooldown.
        /// </summary>
        public void FireSuppressiveBurst(Transform target)
        {
            if (damageHandler && damageHandler.Destroyed) return;
            if (!target || Time.time < nextFireTime) return;

            Vector3 origin = muzzlePoint ? muzzlePoint.position : transform.position;
            // Target chest height
            var cc = target.GetComponent<CharacterController>();
            Vector3 targetCenter = cc ? cc.bounds.center : (target.position + Vector3.up * 1.2f);
            Vector3 aimDir = (targetCenter - origin).normalized;

            // Hitscan trace (ignore triggers)
            Vector3 hitPoint = origin + aimDir * 70f;
            if (Physics.Raycast(origin, aimDir, out RaycastHit hit, 70f, ~0, QueryTriggerInteraction.Ignore))
            {
                hitPoint = hit.point;
                var health = hit.collider.GetComponentInParent<CombatHealth>();
                if (health)
                {
                    health.TakeDamage(damagePerShot);
                }
            }

            // Visual Laser Beam Tracer
            if (laserTracer)
            {
                laserTracer.SetPosition(0, origin);
                laserTracer.SetPosition(1, hitPoint);
                laserTracer.enabled = true;
                beamHideTime = Time.time + beamDuration;
            }

            if (fireAudioClip && audioSource)
            {
                audioSource.PlayOneShot(fireAudioClip, 0.8f);
            }

            // Enforce exact 4 second interval
            nextFireTime = Time.time + fireCooldown;

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

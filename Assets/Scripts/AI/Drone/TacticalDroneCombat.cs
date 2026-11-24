using UnityEngine;

namespace Operation1989.AI.Drone
{
    /// <summary>Legacy prefab component: recon drones are unarmed; flight effects remain disabled.</summary>
    [DisallowMultipleComponent]
    public class TacticalDroneCombat : MonoBehaviour
    {
        [SerializeField, HideInInspector] private Light searchlight;
        [SerializeField, HideInInspector] private LineRenderer laserTracer;
        DroneDamage damageHandler;

        void Awake()
        {
            damageHandler = GetComponent<DroneDamage>();
            foreach (var pulse in GetComponentsInChildren<AircraftLightPulse>(true)) pulse.source = null;
            foreach (var lamp in GetComponentsInChildren<Light>(true)) lamp.enabled = false;
            DisableLegacyEffects();
        }
        void DisableLegacyEffects()
        {
            if (!searchlight)
            {
                var lightTransform = transform.Find("TacticalSearchlight");
                if (lightTransform) searchlight = lightTransform.GetComponent<Light>();
            }
            if (searchlight) searchlight.enabled = false;
            if (laserTracer) laserTracer.enabled = false;
            var tracer = transform.Find("DroneLaserTracer");
            if (tracer) tracer.gameObject.SetActive(false);
        }
        public void ShutDownAllCombatSystems()
        {
            DisableLegacyEffects();
            foreach (var source in GetComponentsInChildren<AudioSource>()) source.Stop();
            enabled = false;
        }
        void Update() { if (damageHandler && damageHandler.Destroyed) ShutDownAllCombatSystems(); }
        public void FireSuppressiveBurst(Transform target)
        {
            // Compatibility API only. Existing prefabs can never damage the player through this component.
        }
    }
}

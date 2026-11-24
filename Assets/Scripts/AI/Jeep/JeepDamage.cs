using UnityEngine;
using UnityEngine.Rendering;

namespace Operation1989.AI.Jeep
{
    /// <summary>Rifle hits cause smoke and a temporary engine failure, followed by a full repair.</summary>
    [DisallowMultipleComponent]
    public sealed class JeepDamage : MonoBehaviour
    {
        [Min(1)] public float maximumHealth = 150f;
        [Min(1)] public float repairSeconds = 30f;
        public float Current { get; private set; }
        public bool Disabled => initialized && Current <= 0;
        public float RepairRemaining => Mathf.Max(0, repairAt - Time.time);
        public float RepairedAt { get; private set; } = float.NegativeInfinity;
        float repairAt = float.PositiveInfinity;
        bool initialized;
        ParticleSystem smoke, sparks;
        float nextSparkAt;
        Material smokeMaterial;
        Texture2D smokeTexture;

        void Awake() => Initialize();
        void Initialize()
        {
            if (initialized) return;
            initialized = true; Current = maximumHealth;
        }
        public void TakeDamage(float amount, Vector3 hitPoint)
        {
            Initialize();
            if (amount <= 0 || Disabled) return; // Extra hits never extend the disabled timer.
            Current = Mathf.Max(0, Current - amount);
            repairAt = Time.time + repairSeconds;
            if (!Application.isPlaying) return;
            EnsureSmoke();
            UpdateSmokeDensity();
            smoke.Play();
            var puff = new ParticleSystem.EmitParams { position = hitPoint, startSize = .65f };
            smoke.Emit(puff, Disabled ? 18 : 7);
            if (Disabled)
            {
                sparks.Play();
                sparks.Emit(10);
                nextSparkAt = Time.time + .7f;
            }
        }
        void Update()
        {
            TickRepair(Time.time);
            if (!smoke || Current >= maximumHealth) return;
            UpdateSmokeDensity();
            if (Disabled && !sparks)
            {
                EnsureSparks(smoke.transform.localPosition - Vector3.up * .2f);
                sparks.Play();
            }
            if (!Disabled || Time.time < nextSparkAt || !sparks) return;
            sparks.Emit(Random.Range(3, 7));
            nextSparkAt = Time.time + Random.Range(.8f, 1.8f);
        }
        void UpdateSmokeDensity()
        {
            var emission = smoke.emission;
            emission.rateOverTime = Disabled ? Mathf.Lerp(34, 48, Mathf.PerlinNoise(Time.time * 2, 0)) :
                Mathf.Lerp(7, 14, 1 - Current / Mathf.Max(1, maximumHealth));
        }
        // Explicit gameplay time makes the cooldown deterministic and permits Edit Mode checks.
        public void TickRepair(float gameplayTime)
        {
            if (!initialized || Current >= maximumHealth || gameplayTime < repairAt) return;
            Current = maximumHealth; repairAt = float.PositiveInfinity; RepairedAt = gameplayTime;
            if (smoke) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (sparks) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        void EnsureSmoke()
        {
            if (smoke)
            {
                if (!sparks) EnsureSparks(smoke.transform.localPosition - Vector3.up * .2f);
                return;
            }
            const int size = 32;
            smokeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            smokeTexture.name = "Jeep smoke falloff";
            smokeTexture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float alpha = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x, y), Vector2.one * 15.5f) / 15.5f);
                    smokeTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            smokeTexture.Apply(false, true);
            smokeMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            smokeMaterial.SetTexture("_BaseMap", smokeTexture);
            smokeMaterial.SetColor("_BaseColor", Color.white);
            smokeMaterial.SetFloat("_Surface", 1);
            smokeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            smokeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            smokeMaterial.SetFloat("_ZWrite", 0);
            smokeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            smokeMaterial.SetOverrideTag("RenderType", "Transparent");
            smokeMaterial.renderQueue = (int)RenderQueue.Transparent;
            smokeMaterial.SetShaderPassEnabled("ShadowCaster", false);
            var emitter = new GameObject("Jeep engine smoke");
            emitter.transform.SetParent(transform, false);
            var box = GetComponent<BoxCollider>();
            emitter.transform.localPosition = box ? box.center + new Vector3(0, box.size.y * .5f + .1f, box.size.z * .3f) : Vector3.up * 1.6f;
            smoke = emitter.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.65f, 1.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.08f, .09f, .1f, .9f), new Color(.2f, .21f, .22f, .85f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.maxParticles = 240;
            var shape = smoke.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .3f;
            var rise = smoke.velocityOverLifetime; rise.enabled = true; rise.space = ParticleSystemSimulationSpace.World;
            rise.x = .12f; rise.y = 1.15f; rise.z = .08f;
            var growth = smoke.sizeOverLifetime; growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .7f, 1, 3.5f));
            var fade = smoke.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(.3f, 0), new GradientAlphaKey(1, .1f), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = smoke.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = smokeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            EnsureSparks(emitter.transform.localPosition - Vector3.up * .2f);
        }
        void EnsureSparks(Vector3 enginePosition)
        {
            var emitter = new GameObject("Jeep damaged engine sparks");
            emitter.transform.SetParent(transform, false);
            emitter.transform.localPosition = enginePosition;
            emitter.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            sparks = emitter.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.04f, .09f);
            main.startColor = new Color(3f, 1.2f, .12f, 1);
            main.gravityModifier = .65f; main.maxParticles = 32;
            var emission = sparks.emission; emission.rateOverTime = 0;
            var shape = sparks.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = .15f;
            var fade = sparks.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, .2f, .02f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = sparks.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.lengthScale = 2; renderer.velocityScale = .15f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        void OnDestroy()
        {
            if (smokeMaterial) Destroy(smokeMaterial);
            if (smokeTexture) Destroy(smokeTexture);
        }
    }
}

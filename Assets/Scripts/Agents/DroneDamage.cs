using UnityEngine;
using UnityEngine.Rendering;
using Operation1989.Interaction.AI;

/// <summary>Rifle damage, motor failure, and a persistent physical drone wreck.</summary>
[DisallowMultipleComponent]
public sealed class DroneDamage : MonoBehaviour
{
    [Tooltip("250 health withstands nine hits from the default 25-damage rifle; the tenth destroys it.")]
    [Min(1)] public float maximumHealth = 250f;
    [Min(0.1f)] public float fallingSparksSeconds = 6f;
    [Min(1f)] public float wreckFireSeconds = 60f;
    public float Current { get; private set; }
    public bool Destroyed { get; private set; }
    public bool HasImpacted { get; private set; }
    public bool IsBurning { get; private set; }

    DroneMapPatrol patrol;
    Rigidbody wreck;
    BoxCollider wreckHitbox;
    ParticleSystem sparks, smoke, blast, fire;
    Material particleMaterial;
    Texture2D particleTexture;
    Light flash, fireLight;
    Vector3 lastPosition, flightVelocity;
    float destroyedAt, flashUntil, fireUntil;
    bool sampledPosition;

    void Awake()
    {
        Current = maximumHealth;
        patrol = GetComponent<DroneMapPatrol>();
        lastPosition = transform.position;
    }

    void Start()
    {
        // Fit a simple wreck collider to the visual. Keep any existing detailed
        // colliders for rifle hits during flight, without cooking complex convex hulls.
        bool hasSolidCollider = false;
        foreach (var collider in GetComponentsInChildren<Collider>())
            if (collider.enabled && !collider.isTrigger) hasSolidCollider = true;
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            bool first = true;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                Bounds local = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1,
                        (corner & 4) == 0 ? -1 : 1));
                    Vector3 point = transform.InverseTransformPoint(renderer.transform.TransformPoint(local.center + offset));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            wreckHitbox = gameObject.AddComponent<BoxCollider>();
            wreckHitbox.center = bounds.center;
            wreckHitbox.size = Vector3.Max(bounds.size, Vector3.one * 0.15f);
            wreckHitbox.enabled = !hasSolidCollider;
        }
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (Destroyed || amount <= 0) return;
        Current = Mathf.Max(0, Current - amount);
        EnsureEffects();
        EmitSparks(hitPoint, 8);
        if (Current > 0) return;

        Destroyed = true;
        destroyedAt = Time.time;
        if (!patrol) patrol = GetComponent<DroneMapPatrol>();
        if (patrol) patrol.enabled = false; // Stops transform movement, rotors, and scheduled motor audio.
        var spotter = GetComponent<DroneSpotter>();
        if (spotter)
        {
            bool wasSpotting = spotter.IsCurrentlySpottingPlayer;
            spotter.enabled = false;
            var alerts = GetComponent<DroneAlertSystem>();
            if (!alerts) alerts = DroneAlertSystem.Instance;
            if (wasSpotting && alerts) alerts.ClearAlert();
        }
        foreach (var animator in GetComponentsInChildren<Animator>()) animator.enabled = false;
        // Detailed flight meshes exceed PhysX's convex hull limits. Use the fitted
        // box for the wreck so it tumbles and lands without hull cooking warnings.
        foreach (var mesh in GetComponentsInChildren<MeshCollider>()) mesh.enabled = false;
        if (wreckHitbox) wreckHitbox.enabled = true;
        wreck = GetComponent<Rigidbody>();
        if (!wreck) wreck = gameObject.AddComponent<Rigidbody>();
        wreck.mass = 4f;
        wreck.isKinematic = false;
        wreck.useGravity = true;
        wreck.constraints = RigidbodyConstraints.None;
        wreck.interpolation = RigidbodyInterpolation.Interpolate;
        wreck.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        wreck.linearVelocity = Vector3.ClampMagnitude(flightVelocity, 12f);
        wreck.angularVelocity = transform.TransformDirection(new Vector3(2.8f, 1.2f, 1.7f));
        Burst(transform.position);
        sparks.Play();
        smoke.Play();
    }

    void LateUpdate()
    {
        if (!Destroyed && Time.deltaTime > 0)
        {
            if (sampledPosition) flightVelocity = (transform.position - lastPosition) / Time.deltaTime;
            lastPosition = transform.position;
            sampledPosition = true;
        }
        if (flash) flash.intensity = Mathf.Max(0, (flashUntil - Time.time) / 0.18f) * 5f;
        if (!Destroyed) return;
        if (Time.time - destroyedAt > fallingSparksSeconds && sparks && sparks.isEmitting)
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (IsBurning)
        {
            Vector3 center = wreckHitbox ? wreckHitbox.bounds.center : transform.position;
            fire.transform.position = center + Vector3.up * 0.1f;
            smoke.transform.position = center + Vector3.up * 0.3f;
            fireLight.transform.position = center + Vector3.up * 0.4f;
            float remaining = fireUntil - Time.time;
            fireLight.intensity = Mathf.Clamp01(remaining / 3f) * (2.5f + Mathf.PerlinNoise(Time.time * 11f, 0) * 1.5f);
            if (remaining <= 0)
            {
                IsBurning = false;
                fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                fireLight.enabled = false;
            }
        }
        if (!IsBurning && Time.time - destroyedAt > fallingSparksSeconds + 3 && smoke && smoke.isEmitting)
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!Destroyed || HasImpacted) return;
        HasImpacted = true;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Burst(point);
        EmitSparks(point, 45);
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        StartWreckFire();
    }

    void StartWreckFire()
    {
        foreach (var pulse in GetComponentsInChildren<AircraftLightPulse>(true)) pulse.SwitchOff();
        foreach (var lamp in GetComponentsInChildren<Light>(true))
            if (lamp != flash && lamp != fireLight) lamp.enabled = false;
        var lightRig = transform.Find("Dusk Drone Lights");
        if (lightRig)
        {
            var properties = new MaterialPropertyBlock();
            foreach (var lens in lightRig.GetComponentsInChildren<Renderer>(true))
            {
                lens.GetPropertyBlock(properties);
                properties.SetColor("_EmissionColor", Color.black);
                lens.SetPropertyBlock(properties);
                properties.Clear();
            }
        }
        IsBurning = true;
        fireUntil = Time.time + wreckFireSeconds;
        fire.Play();
        smoke.Play();
        fireLight.enabled = true;
    }

    void EmitSparks(Vector3 point, int count)
    {
        var emit = new ParticleSystem.EmitParams { position = point };
        sparks.Emit(emit, count);
    }

    void Burst(Vector3 point)
    {
        blast.transform.position = point;
        blast.Emit(24);
        flash.transform.position = point;
        flashUntil = Time.time + 0.18f;
    }

    void EnsureEffects()
    {
        if (sparks) return;
        particleTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        { name = "Drone effect falloff", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float alpha = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * 16) / 16);
                particleTexture.SetPixel(x, y, new Color(1, 1, 1, alpha * alpha));
            }
        particleTexture.Apply(false, true);
        particleMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        particleMaterial.SetTexture("_BaseMap", particleTexture);
        particleMaterial.SetColor("_BaseColor", Color.white);
        particleMaterial.SetFloat("_Surface", 1);
        particleMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        particleMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        particleMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        particleMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        particleMaterial.SetFloat("_ZWrite", 0);
        particleMaterial.SetOverrideTag("RenderType", "Transparent");
        particleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        particleMaterial.renderQueue = (int)RenderQueue.Transparent;
        particleMaterial.SetShaderPassEnabled("ShadowCaster", false);

        sparks = MakeParticles("Falling drone sparks", 0.65f, 0.065f, 5f, new Color(3f, 1.4f, 0.15f), 55);
        var sparkMain = sparks.main; sparkMain.gravityModifier = 1.3f;
        var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.lengthScale = 2;
        sparkRenderer.velocityScale = 0.08f;
        smoke = MakeParticles("Drone smoke trail", 2f, 0.6f, 0.8f, new Color(0.18f, 0.19f, 0.2f, 0.65f), 12);
        var smokeMain = smoke.main; smokeMain.gravityModifier = -0.08f;
        fire = MakeParticles("Drone wreck fire", 1.1f, 0.55f, 0, Color.white, 45);
        var fireMain = fire.main;
        fireMain.startSize3D = true;
        fireMain.startSizeX = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        fireMain.startSizeY = new ParticleSystem.MinMaxCurve(0.65f, 1.1f);
        fireMain.startSizeZ = new ParticleSystem.MinMaxCurve(1f, 1f);
        var fireShape = fire.shape; fireShape.radius = 0.28f;
        var rise = fire.velocityOverLifetime; rise.enabled = true;
        rise.space = ParticleSystemSimulationSpace.World;
        rise.x = 0f;
        rise.y = 1.5f;
        rise.z = 0f;
        var fireColor = fire.colorOverLifetime;
        var flameGradient = new Gradient();
        flameGradient.SetKeys(new[] {
            new GradientColorKey(new Color(1, 0.8f, 0.12f), 0),
            new GradientColorKey(new Color(1, 0.25f, 0.015f), 0.35f),
            new GradientColorKey(new Color(0.2f, 0.025f, 0.005f), 1)
        }, new[] {
            new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.12f),
            new GradientAlphaKey(0.65f, 0.65f), new GradientAlphaKey(0, 1)
        });
        fireColor.color = flameGradient;
        var fireSize = fire.sizeOverLifetime; fireSize.enabled = true;
        fireSize.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
            new Keyframe(0, 0.4f), new Keyframe(0.25f, 1), new Keyframe(1, 0.1f)));
        var fireRenderer = fire.GetComponent<ParticleSystemRenderer>();
        // Billboards retain their flame shape even when particles start at zero speed.
        fireRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        var fireLightObject = new GameObject("Drone wreck fire light");
        fireLightObject.transform.SetParent(transform, false);
        fireLight = fireLightObject.AddComponent<Light>();
        fireLight.color = new Color(1, 0.32f, 0.06f);
        fireLight.range = 4;
        fireLight.enabled = false;
        blast = MakeParticles("Drone small blast", 0.4f, 0.65f, 2.5f, new Color(3f, 0.7f, 0.08f), 0);
        var blastMain = blast.main; blastMain.loop = false;
        var size = blast.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.3f, 1, 1.5f));
        var lightObject = new GameObject("Drone blast light");
        lightObject.transform.SetParent(transform, false);
        flash = lightObject.AddComponent<Light>();
        flash.color = new Color(1, 0.45f, 0.1f);
        flash.range = 5;
        flash.intensity = 0;
    }

    ParticleSystem MakeParticles(string label, float lifetime, float size, float speed, Color color, float rate)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.maxParticles = 400;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission; emission.rateOverTime = rate;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.15f;
        var fade = particles.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    void OnDestroy()
    {
        if (particleMaterial) Destroy(particleMaterial);
        if (particleTexture) Destroy(particleTexture);
    }
}

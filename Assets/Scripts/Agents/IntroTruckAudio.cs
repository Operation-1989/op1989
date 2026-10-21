using UnityEngine;

/// <summary>Spatial truck sound driven by actual motion. Optional recordings override synthesized clips.</summary>
public sealed class IntroTruckAudio : MonoBehaviour
{
    public AudioClip engineClip, rollingClip, brakeClip;
    [Range(0, 1)] public float volume = .65f;
    AudioSource engine, rolling, brakes;
    AudioClip generatedEngine, generatedRolling, generatedBrake;
    float previousSpeed, brakeCooldown;
    bool stopping;
    public int BrakeEvents { get; private set; }

    AudioSource Source(bool loop)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        VehicleAudioFalloff.Configure(source, 4, 28, 55);
        source.dopplerLevel = .25f;
        return source;
    }

    void Initialize()
    {
        if (engine) return;
        engine = Source(true); rolling = Source(true); brakes = Source(false);
        engine.clip = engineClip ? engineClip : generatedEngine = Synthesize("Diesel engine", 2, 0);
        rolling.clip = rollingClip ? rollingClip : generatedRolling = Synthesize("Tire and chassis rumble", 2, 1);
        brakes.clip = brakeClip ? brakeClip : generatedBrake = Synthesize("Air brake release", 1.2f, 2);
    }

    public void ResetSounds()
    {
        Initialize();
        StopSounds();
        previousSpeed = brakeCooldown = 0;
        BrakeEvents = 0;
        stopping = false;
        engine.volume = volume * .45f;
        engine.pitch = .8f;
        rolling.volume = 0;
        engine.Play(); rolling.Play();
    }

    public void Tick(float speed, float maxSpeed, bool parked, float dt)
    {
        Initialize();
        float load = Mathf.Clamp01(speed / Mathf.Max(.1f, maxSpeed));
        engine.pitch = Mathf.Lerp(engine.pitch, Mathf.Lerp(.8f, 1.45f, load), 1 - Mathf.Exp(-3 * dt));
        engine.volume = volume * Mathf.Lerp(.4f, .85f, load);
        rolling.volume = volume * load * .35f;
        rolling.pitch = Mathf.Lerp(.65f, 1.3f, load);
        brakeCooldown -= dt;
        bool slowing = previousSpeed - speed > .35f * dt && previousSpeed > .6f;
        if (((slowing && !stopping) || (parked && previousSpeed > .05f)) && brakeCooldown <= 0)
        {
            brakes.PlayOneShot(brakes.clip, volume * (parked ? .8f : .55f));
            BrakeEvents++;
            brakeCooldown = 2;
        }
        stopping = slowing;
        previousSpeed = speed;
    }

    public void StopSounds()
    {
        if (engine) engine.Stop();
        if (rolling) rolling.Stop();
        if (brakes) brakes.Stop();
    }

    static AudioClip Synthesize(string name, float duration, int kind)
    {
        const int rate = 22050;
        var samples = new float[Mathf.RoundToInt(rate * duration)];
        var random = new System.Random(1989 + kind);
        float low = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float noise = (float)random.NextDouble() * 2 - 1;
            low = Mathf.Lerp(low, noise, .075f);
            float edge = Mathf.Min(1, Mathf.Min(t, duration - t) * 100);
            if (kind == 0)
            {
                float pulse = Mathf.Pow(.5f + .5f * Mathf.Sin(2 * Mathf.PI * 36 * t), 5);
                samples[i] = (.24f * Mathf.Sin(2 * Mathf.PI * 72 * t) + .12f * Mathf.Sin(2 * Mathf.PI * 108 * t)
                    + .32f * (pulse - .25f) + low * .3f) * edge;
            }
            else if (kind == 1) samples[i] = low * .65f * edge;
            else samples[i] = (noise - low) * .36f * Mathf.Exp(-3.7f * t) * Mathf.Clamp01(t * 45) * edge;
        }
        var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void OnDisable() => StopSounds();
    void OnDestroy()
    {
        if (generatedEngine) Destroy(generatedEngine);
        if (generatedRolling) Destroy(generatedRolling);
        if (generatedBrake) Destroy(generatedBrake);
    }
}

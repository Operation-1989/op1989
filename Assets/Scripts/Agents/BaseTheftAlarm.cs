using UnityEngine;

/// <summary>Audio and emergency lighting only; does not modify guard awareness or patrols.</summary>
public sealed class BaseTheftAlarm : MonoBehaviour
{
    public AudioClip sirenClip;
    public Light[] emergencyLights;
    public Renderer[] lenses;
    public RotatingAlarmBeacon[] rotatingBeacons;
    [Range(0, 1)] public float volume = 0.45f;
    public bool Active { get; private set; }
    AudioSource siren;
    MaterialPropertyBlock properties;
    bool lit;
    float startedAt;
    void Awake()
    {
        siren = gameObject.AddComponent<AudioSource>();
        siren.playOnAwake = false; siren.loop = true; siren.spatialBlend = 0; siren.volume = volume; siren.clip = sirenClip;
        SetActive(false);
    }
    public void SetActive(bool active)
    {
        if (Active == active && active) return;
        Active = active;
        startedAt = Time.time;
        if (siren)
        {
            if (active && sirenClip) { siren.clip = sirenClip; siren.Play(); }
            else siren.Stop();
        }
        Apply(active);
        if (rotatingBeacons != null) foreach (var beacon in rotatingBeacons) if (beacon) beacon.SetPowered(active);
    }
    void Update()
    {
        if (!Active) return;
        if (rotatingBeacons != null && rotatingBeacons.Length > 0) return;
        bool on = Mathf.Repeat(Time.time - startedAt, 0.9f) < 0.55f;
        if (on != lit) Apply(on);
    }
    void Apply(bool on)
    {
        lit = on;
        if (emergencyLights != null) foreach (var lamp in emergencyLights) if (lamp) lamp.enabled = on;
        if (properties == null) properties = new MaterialPropertyBlock();
        if (lenses != null) foreach (var lens in lenses)
        {
            if (!lens) continue;
            lens.GetPropertyBlock(properties);
            properties.SetColor("_EmissionColor", on ? new Color(6, 0.015f, 0.005f) : Color.black);
            lens.SetPropertyBlock(properties);
            properties.Clear();
        }
    }
    void OnDisable() { Active = false; if (siren) siren.Stop(); Apply(false); if (rotatingBeacons != null) foreach (var beacon in rotatingBeacons) if (beacon) beacon.SetPowered(false); }
}

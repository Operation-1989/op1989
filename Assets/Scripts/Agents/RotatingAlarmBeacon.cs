using UnityEngine;

/// <summary>A motor-driven red emergency beacon with two opposed sweeping beams.</summary>
public sealed class RotatingAlarmBeacon : MonoBehaviour
{
    public Transform rotor;
    public Light[] lamps;
    public Renderer[] domes;
    public float degreesPerSecond = 480;
    public bool Powered { get; private set; }
    MaterialPropertyBlock properties;
    public void SetPowered(bool active)
    {
        Powered = active;
        if (lamps != null) foreach (var lamp in lamps) if (lamp) lamp.enabled = active;
        if (properties == null) properties = new MaterialPropertyBlock();
        if (domes != null) foreach (var dome in domes)
        {
            if (!dome) continue;
            dome.GetPropertyBlock(properties);
            properties.SetColor("_EmissionColor", active ? new Color(1.8f, 0.008f, 0.003f) : Color.black);
            dome.SetPropertyBlock(properties); properties.Clear();
        }
    }
    void Update() { if (Powered && rotor) rotor.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self); }
    void OnDisable() => SetPowered(false);
}

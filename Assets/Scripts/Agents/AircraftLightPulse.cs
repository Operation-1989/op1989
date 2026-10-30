using UnityEngine;

/// <summary>Flashes the visible lens and its illumination together without material instances.</summary>
public sealed class AircraftLightPulse : MonoBehaviour
{
    public enum Pattern { NavigationPulse, RedBeacon, DoubleStrobe }
    public Pattern pattern;
    public Light source;
    public Renderer lens;
    public Color color = Color.red;
    public float period = 1.2f;
    public float phaseOffset;
    public float emission = 4;
    MaterialPropertyBlock properties;
    bool previous;
    bool initialized;
    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public bool IsLitAt(float seconds)
    {
        float phase = Mathf.Repeat(seconds + phaseOffset, Mathf.Max(.4f, period));
        if (pattern == Pattern.DoubleStrobe) return phase < .065f || (phase >= .17f && phase < .235f);
        return phase < (pattern == Pattern.RedBeacon ? .16f : .3f);
    }

    void OnEnable() { initialized = false; Apply(IsLitAt(Time.time)); }
    void Update() => Apply(IsLitAt(Time.time));

    public void SwitchOff()
    {
        enabled = false;
        // OnDisable restores the lamp's normal appearance; explicitly remove its
        // illumination and lens emission for a vehicle that has lost power.
        initialized = false;
        Apply(false);
    }

    void Apply(bool lit)
    {
        if (initialized && previous == lit) return;
        initialized = true;
        previous = lit;
        if (source) source.enabled = lit;
        if (!lens) return;
        if (properties == null) properties = new MaterialPropertyBlock();
        lens.GetPropertyBlock(properties);
        properties.SetColor(EmissionColor, lit ? color * emission : Color.black);
        properties.SetColor(BaseColor, lit ? color : color * .12f);
        lens.SetPropertyBlock(properties);
    }

    void OnDisable()
    {
        if (source) source.enabled = true;
        if (lens) lens.SetPropertyBlock(null);
        initialized = false;
    }
}

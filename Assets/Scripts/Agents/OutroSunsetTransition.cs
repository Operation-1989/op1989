using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Departure-only lighting, restored exactly when replaying the mission.</summary>
public sealed class OutroSunsetTransition : MonoBehaviour
{
    public Material sunsetSky;
    public Light sunlight;
    public float Blend { get; private set; }
    Material previousSky, instance;
    Color sky, equator, ground, sunColor;
    float sunIntensity, reflection;
    Quaternion sunRotation;
    AmbientMode ambientMode;
    bool active;

    public void Begin()
    {
        if (active || !sunsetSky) return;
        active = true;
        previousSky = RenderSettings.skybox;
        ambientMode = RenderSettings.ambientMode;
        sky = RenderSettings.ambientSkyColor; equator = RenderSettings.ambientEquatorColor;
        ground = RenderSettings.ambientGroundColor; reflection = RenderSettings.reflectionIntensity;
        if (sunlight)
        {
            sunColor = sunlight.color; sunIntensity = sunlight.intensity; sunRotation = sunlight.transform.rotation;
        }
        instance = new Material(sunsetSky) { name = "Departure sunset (runtime)" };
        RenderSettings.skybox = instance;
        SetBlend(0);
    }
    public void SetBlend(float blend)
    {
        if (!active || !instance) return;
        Blend = Mathf.Clamp01(blend);
        instance.SetFloat("_Blend", Blend);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(sky, new Color(.085f, .075f, .12f), Blend);
        RenderSettings.ambientEquatorColor = Color.Lerp(equator, new Color(.12f, .065f, .045f), Blend);
        RenderSettings.ambientGroundColor = Color.Lerp(ground, new Color(.035f, .025f, .025f), Blend);
        RenderSettings.reflectionIntensity = Mathf.Lerp(reflection, .22f, Blend);
        if (sunlight)
        {
            sunlight.color = Color.Lerp(sunColor, new Color(1, .42f, .16f), Blend);
            sunlight.intensity = Mathf.Lerp(sunIntensity, .26f, Blend);
            Vector3 euler = sunRotation.eulerAngles; euler.x = Mathf.LerpAngle(euler.x, 3, Blend);
            sunlight.transform.rotation = Quaternion.Euler(euler);
            Vector3 direction = -sunlight.transform.forward;
            instance.SetVector("_SunDirection", new Vector4(direction.x, direction.y, direction.z, 0));
        }
    }
    public void ResetLighting()
    {
        if (!active) return;
        active = false; Blend = 0;
        RenderSettings.skybox = previousSky; RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator;
        RenderSettings.ambientGroundColor = ground; RenderSettings.reflectionIntensity = reflection;
        if (sunlight)
        {
            sunlight.color = sunColor; sunlight.intensity = sunIntensity; sunlight.transform.rotation = sunRotation;
        }
        if (instance) Destroy(instance);
        instance = null;
    }
    void OnDisable() => ResetLighting();
}

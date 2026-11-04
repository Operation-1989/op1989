using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A small location pin without bloom, illumination, or ground effects.</summary>
public sealed class FloatingObjectiveIcon : MonoBehaviour
{
    Camera viewer;
    Vector3 anchor;
    Material ink, shadow;
    public static FloatingObjectiveIcon Create(Transform parent, Vector3 position, Camera camera, string name)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.transform.localScale = Vector3.one * 0.65f;
        var icon = go.AddComponent<FloatingObjectiveIcon>(); icon.viewer = camera; icon.anchor = position; icon.Build();
        return icon;
    }
    static Material MakeMaterial(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Subtle location icon" };
        material.SetColor("_BaseColor", color); material.SetFloat("_Surface", 1);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent; material.SetShaderPassEnabled("ShadowCaster", false);
        return material;
    }
    void Build()
    {
        ink = MakeMaterial(new Color(0.76f, 0.81f, 0.83f, 0.8f));
        shadow = MakeMaterial(new Color(0.015f, 0.025f, 0.03f, 0.45f));
        var points = new Vector3[26]; points[0] = Vector3.down * 0.15f;
        for (int i = 0; i < 25; i++)
        {
            float angle = Mathf.Lerp(210, -30, i / 24f) * Mathf.Deg2Rad;
            points[i + 1] = new Vector3(Mathf.Cos(angle) * 0.1f, 0.08f + Mathf.Sin(angle) * 0.1f, 0);
        }
        ObjectiveGlow.Line(transform, "Location pin shadow", shadow, 0.021f, points, false, true);
        ObjectiveGlow.Line(transform, "Location pin outline", ink, 0.009f, points, false, true);
        var dot = new Vector3[20];
        for (int i = 0; i < dot.Length; i++)
        { float angle = i * Mathf.PI * 2 / dot.Length; dot[i] = new Vector3(Mathf.Cos(angle) * 0.022f, 0.08f + Mathf.Sin(angle) * 0.022f, 0); }
        ObjectiveGlow.Line(transform, "Location pin center", ink, 0.008f, dot, false, true);
    }
    void LateUpdate()
    {
        transform.localPosition = anchor + Vector3.up * (Mathf.Sin(Time.time * 1.3f) * 0.012f);
        if (viewer) transform.rotation = Quaternion.LookRotation(viewer.transform.position - transform.position, Vector3.up);
    }
    void OnDestroy() { if (ink) Destroy(ink); if (shadow) Destroy(shadow); }
}

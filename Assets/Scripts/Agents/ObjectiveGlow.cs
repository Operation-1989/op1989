using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Soft additive halos and bright cores, readable with or without camera bloom.</summary>
public static class ObjectiveGlow
{
    public static readonly Color Amber = new Color(1, 0.66f, 0.2f);
    public static readonly Color Cyan = new Color(0.15f, 0.9f, 1);
    public static Material Material(Color color, bool halo)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = halo ? "Objective soft glow" : "Objective luminous core" };
        material.SetColor("_BaseColor", color * (halo ? 2.5f : 5));
        material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One); material.SetFloat("_ZWrite", 0);
        material.SetFloat("_Cull", (float)CullMode.Off); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("ShadowCaster", false);
        if (halo)
        {
            var texture = new Texture2D(2, 32, TextureFormat.RGBA32, false) { name = "Objective glow falloff", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
            {
                float d = Mathf.Abs((y + 0.5f) / 16f - 1);
                Color pixel = new Color(1, 1, 1, Mathf.Pow(1 - d, 2) * 0.26f);
                texture.SetPixel(0, y, pixel); texture.SetPixel(1, y, pixel);
            }
            texture.Apply(false, true); material.SetTexture("_BaseMap", texture);
        }
        return material;
    }
    public static LineRenderer Line(Transform parent, string name, Material material, float width, Vector3[] points, bool world = true, bool loop = false)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.useWorldSpace = world;
        line.positionCount = points.Length; line.SetPositions(points); line.loop = loop;
        line.startWidth = line.endWidth = width; line.numCapVertices = 4; line.numCornerVertices = 4;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        return line;
    }
    public static Vector3[] Circle(float radius, float height, int count = 96)
    {
        var points = new Vector3[count];
        for (int i = 0; i < count; i++) { float angle = i * Mathf.PI * 2 / count; points[i] = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius); }
        return points;
    }
    public static void Release(Material material)
    {
        if (!material) return;
        var texture = material.GetTexture("_BaseMap");
        if (texture && texture.name == "Objective glow falloff") Object.Destroy(texture);
        Object.Destroy(material);
    }
}

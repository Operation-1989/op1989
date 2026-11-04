using UnityEngine;

/// <summary>Heading tape with a fixed center index and a live objective bearing.</summary>
public sealed class ObjectiveCompassHud
{
    GUIStyle cardinal, degree, heading, distance, edge, instruction;
    Texture2D diamond;
    static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    void Styles()
    {
        if (cardinal != null) return;
        cardinal = MissionHudStyle.Label(MissionHudStyle.Semibold, 12, new Color(0.9f, 0.94f, 0.95f));
        degree = MissionHudStyle.Label(MissionHudStyle.Regular, 10, new Color(0.52f, 0.61f, 0.65f));
        heading = MissionHudStyle.Label(MissionHudStyle.Semibold, 10, new Color(0.85f, 0.9f, 0.92f));
        distance = MissionHudStyle.Label(MissionHudStyle.Regular, 10, new Color(0.77f, 0.84f, 0.86f));
        edge = MissionHudStyle.Label(MissionHudStyle.Semibold, 20, Color.white);
        instruction = MissionHudStyle.Label(MissionHudStyle.Regular, 11, new Color(0.76f, 0.82f, 0.84f));
        foreach (var style in new[] { cardinal, degree, heading, distance, edge, instruction })
        {
            style.alignment = TextAnchor.MiddleCenter; style.wordWrap = false;
            style.hover.textColor = style.active.textColor = style.focused.textColor = style.normal.textColor;
        }
    }
    public static float MarkerOffset(float relativeBearing) => Mathf.Clamp(relativeBearing / 75f, -1, 1);
    public void Draw(float screenWidth, float yaw, float objectiveBearing, float meters, Color accent, string action)
    {
        Styles();
        float width = Mathf.Min(480, screenWidth * 0.46f);
        var panel = new Rect((screenWidth - width) * 0.5f, 18, width, 102);
        MissionHudStyle.Fill(panel, new Color(0.015f, 0.025f, 0.03f, 0.84f));
        MissionHudStyle.Fill(new Rect(panel.x, panel.y, width, 1), new Color(0.7f, 0.82f, 0.87f, 0.18f));
        MissionHudStyle.Fill(new Rect(panel.x, panel.yMax - 1, width, 1), new Color(0.7f, 0.82f, 0.87f, 0.12f));
        GUI.Label(new Rect(panel.center.x - 30, panel.y + 2, 60, 17), $"{Mathf.RoundToInt(yaw) % 360:000}", heading);
        float half = (width - 44) * 0.5f;
        GUI.BeginGroup(new Rect(panel.x + 22, panel.y + 21, width - 44, 34));
        for (int bearing = 0; bearing < 360; bearing += 5)
        {
            float delta = Mathf.DeltaAngle(yaw, bearing);
            if (Mathf.Abs(delta) > 84) continue;
            float x = half + delta / 75f * half;
            bool major = bearing % 15 == 0;
            float fade = Mathf.Clamp01((half - Mathf.Abs(x - half)) / 35);
            MissionHudStyle.Fill(new Rect(x, major ? 23 : 27, 1, major ? 9 : 5), new Color(0.72f, 0.8f, 0.83f, fade * (major ? 0.8f : 0.3f)));
            if (!major) continue;
            var previous = GUI.color; GUI.color = new Color(1, 1, 1, fade);
            GUI.Label(new Rect(x - 23, 0, 46, 22), bearing % 45 == 0 ? Directions[bearing / 45] : bearing.ToString("000"), bearing % 45 == 0 ? cardinal : degree);
            GUI.color = previous;
        }
        GUI.EndGroup();
        MissionHudStyle.Fill(new Rect(panel.center.x - 0.5f, panel.y + 43, 1, 13), new Color(0.95f, 0.98f, 1, 0.65f));
        MissionHudStyle.Fill(new Rect(panel.x + 22, panel.y + 58, width - 44, 1), new Color(0.5f, 0.65f, 0.7f, 0.14f));
        float markerX = panel.center.x + MarkerOffset(objectiveBearing) * half;
        Diamond(new Vector2(markerX, panel.y + 59), 22, new Color(accent.r, accent.g, accent.b, 0.08f));
        Diamond(new Vector2(markerX, panel.y + 59), 14, new Color(accent.r, accent.g, accent.b, 0.22f));
        Diamond(new Vector2(markerX, panel.y + 59), 8, accent);
        if (Mathf.Abs(objectiveBearing) > 75)
        {
            var previous = GUI.color; GUI.color = accent;
            GUI.Label(new Rect(markerX - 10, panel.y + 33, 20, 24), objectiveBearing < 0 ? "‹" : "›", edge);
            GUI.color = previous;
        }
        GUI.Label(new Rect(panel.center.x - 42, panel.y + 64, 84, 16), $"{meters:0} m", distance);
        GUI.Label(new Rect(panel.x + 20, panel.y + 82, width - 40, 16), action, instruction);
    }
    void Diamond(Vector2 center, float size, Color color)
    {
        if (!diamond)
        {
            diamond = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Compass objective diamond", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                diamond.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(15.5f - Mathf.Abs(x - 15.5f) - Mathf.Abs(y - 15.5f))));
            diamond.Apply(false, true);
        }
        var previous = GUI.color; GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), diamond);
        GUI.color = previous;
    }
    public void Dispose() { if (diamond) Object.Destroy(diamond); }
}

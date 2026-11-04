using UnityEngine;

/// <summary>Shared typography, spacing and colours for mission and combat overlays.</summary>
public static class MissionHudStyle
{
    public static float Scale => Mathf.Max(.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
    public static Font Regular => Resources.Load<Font>("MissionUI/Inter-Regular");
    public static Font Semibold => Resources.Load<Font>("MissionUI/Inter-SemiBold");
    public static readonly Color Accent = new Color(.76f, .69f, .42f);

    public static GUIStyle Label(Font font, int size, Color color)
    {
        var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, wordWrap = true,
            padding = new RectOffset(), margin = new RectOffset() };
        foreach (var state in new[] { style.normal, style.hover, style.active, style.focused,
            style.onNormal, style.onHover, style.onActive, style.onFocused }) state.textColor = color;
        return style;
    }

    public static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    public static void Card(Rect rect)
    {
        Fill(new Rect(rect.x + 3, rect.y + 4, rect.width, rect.height), new Color(0, 0, 0, .22f));
        Fill(rect, new Color(.035f, .055f, .048f, .95f));
        Fill(new Rect(rect.x, rect.y, 3, rect.height), Accent);
    }
}

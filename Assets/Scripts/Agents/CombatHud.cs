using UnityEngine;

public sealed class CombatHud
{
    GUIStyle eyebrow, heading, body, hint, healthText, crosshair;
    Texture2D scopeMask;

    public void Dispose()
    {
        if (scopeMask) Object.Destroy(scopeMask);
    }

    void DrawScope(float width, float height)
    {
        if (!scopeMask)
        {
            const int size = 512;
            scopeMask = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "Scope mask", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * size * 0.5f);
                    pixels[y * size + x] = new Color(0, 0, 0, Mathf.Clamp01(radius - (size * 0.5f - 3)));
                }
            scopeMask.SetPixels32(pixels);
            scopeMask.Apply(false, true);
        }
        float diameter = Mathf.Min(width, height) * 0.88f;
        float cx = width * 0.5f, cy = height * 0.5f, radiusOnScreen = diameter * 0.5f;
        var lens = new Rect(cx - radiusOnScreen, cy - radiusOnScreen, diameter, diameter);
        MissionHudStyle.Fill(new Rect(0, 0, width, lens.y), Color.black);
        MissionHudStyle.Fill(new Rect(0, lens.yMax, width, height - lens.yMax), Color.black);
        MissionHudStyle.Fill(new Rect(0, lens.y, lens.x, diameter), Color.black);
        MissionHudStyle.Fill(new Rect(lens.xMax, lens.y, width - lens.xMax, diameter), Color.black);
        var ink = new Color(0.015f, 0.02f, 0.015f, 0.95f);
        MissionHudStyle.Fill(new Rect(lens.x, cy - 0.5f, diameter, 1), ink);
        MissionHudStyle.Fill(new Rect(cx - 0.5f, lens.y, 1, diameter), ink);
        for (int i = -3; i <= 3; i++)
        {
            if (i == 0) continue;
            float step = diameter * 0.08f * i;
            MissionHudStyle.Fill(new Rect(cx + step - 0.5f, cy - 4, 1, 8), ink);
            MissionHudStyle.Fill(new Rect(cx - 4, cy + step - 0.5f, 8, 1), ink);
        }
        MissionHudStyle.Fill(new Rect(cx - 1.5f, cy - 1.5f, 3, 3), MissionHudStyle.Accent);
        GUI.DrawTexture(lens, scopeMask);
    }

    void Styles()
    {
        if (body != null) return;
        eyebrow = MissionHudStyle.Label(MissionHudStyle.Regular, 11, new Color(.68f, .75f, .71f));
        heading = MissionHudStyle.Label(MissionHudStyle.Semibold, 21, new Color(.96f, .96f, .9f));
        body = MissionHudStyle.Label(MissionHudStyle.Regular, 16, new Color(.85f, .89f, .86f));
        hint = MissionHudStyle.Label(MissionHudStyle.Regular, 12, new Color(.65f, .73f, .69f));
        healthText = MissionHudStyle.Label(MissionHudStyle.Semibold, 18, new Color(.96f, .96f, .9f));
        healthText.alignment = TextAnchor.MiddleRight;
        crosshair = MissionHudStyle.Label(MissionHudStyle.Regular, 28, new Color(.96f, .96f, .9f, 1f));
        crosshair.alignment = TextAnchor.MiddleCenter;
    }

    public void Draw(CombatHealth health, float secondsLeft)
    {
        Styles();
        float scale = MissionHudStyle.Scale, width = Screen.width / scale, height = Screen.height / scale;
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
        var player = health.GetComponent<PlayerSoldierPresentation>();
        bool scoped = !health.Dead && player && player.isActiveAndEnabled && player.IsScoped;
        if (scoped) DrawScope(width, height);
        var card = new Rect(width - 344, height - 104, 320, 80);
        MissionHudStyle.Card(card);
        GUI.Label(new Rect(card.x + 20, card.y + 14, 200, 18), "ASSAULT RIFLE", eyebrow);
        GUI.Label(new Rect(card.x + 20, card.y + 36, 210, 24), "LMB Fire / RMB " + (player && player.toggleScope ? "Toggle scope" : "Hold scope"), hint);
        GUI.Label(new Rect(card.xMax - 96, card.y + 22, 76, 25), $"{health.Current:0} HP", healthText);
        var bar = new Rect(card.x + 20, card.yMax - 12, card.width - 40, 3);
        MissionHudStyle.Fill(bar, new Color(.17f, .23f, .20f));
        MissionHudStyle.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(health.Current / health.maximumHealth), bar.height), MissionHudStyle.Accent);
        if (health.Dead)
        {
            var death = new Rect((width - 440) / 2, (height - 204) / 2, 440, 204);
            MissionHudStyle.Card(death);
            GUI.Label(new Rect(death.x + 20, death.y + 18, 395, 18), "CHECKPOINT", eyebrow);
            GUI.Label(new Rect(death.x + 20, death.y + 44, 395, 30), "Regroup inside the gate", heading);
            GUI.Label(new Rect(death.x + 20, death.y + 88, 395, 52), "You were shot. You will return inside the main gate with full health.", body);
            GUI.Label(new Rect(death.x + 20, death.yMax - 35, 395, 22), $"Respawning in {Mathf.CeilToInt(secondsLeft)}s   /   R  Respawn now", hint);
        }
        else if (!scoped && Cursor.lockState == CursorLockMode.Locked)
            GUI.Label(new Rect(width / 2 - 18, height / 2 - 18, 36, 36), "+", crosshair);
        GUI.matrix = matrix;
    }
}

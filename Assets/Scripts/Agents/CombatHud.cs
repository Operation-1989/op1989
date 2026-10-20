using UnityEngine;

public sealed class CombatHud
{
    GUIStyle eyebrow, heading, body, hint, healthText, crosshair;

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
        var card = new Rect(width - 344, height - 104, 320, 80);
        MissionHudStyle.Card(card);
        GUI.Label(new Rect(card.x + 20, card.y + 14, 200, 18), "ASSAULT RIFLE", eyebrow);
        GUI.Label(new Rect(card.x + 20, card.y + 36, 180, 24), "Hold LMB to fire", body);
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
        else if (Cursor.lockState == CursorLockMode.Locked)
            GUI.Label(new Rect(width / 2 - 18, height / 2 - 18, 36, 36), "+", crosshair);
        GUI.matrix = matrix;
    }
}

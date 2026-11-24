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
        GUI.Label(new Rect(card.x + 20, card.y + 14, 200, 18), health.IsRegenerating ? "RECOVERING" : "ASSAULT RIFLE", eyebrow);
        GUI.Label(new Rect(card.x + 20, card.y + 36, 210, 24), "LMB Fire / RMB " + (player && player.toggleScope ? "Toggle scope" : "Hold scope"), hint);
        GUI.Label(new Rect(card.xMax - 96, card.y + 22, 76, 25), $"{health.Current:0} HP", healthText);
        var bar = new Rect(card.x + 20, card.yMax - 12, card.width - 40, 3);
        MissionHudStyle.Fill(bar, new Color(.17f, .23f, .20f));
        MissionHudStyle.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(health.Current / health.maximumHealth), bar.height), MissionHudStyle.Accent);
        if (health.Dead)
        {
            var checkpoint = health.GetComponent<PlayerCheckpointState>();
            bool buildingCheckpoint = checkpoint && checkpoint.HasCheckpoint;
            var death = new Rect((width - 440) / 2, (height - 204) / 2, 440, 204);
            MissionHudStyle.Card(death);
            GUI.Label(new Rect(death.x + 20, death.y + 18, 395, 18), "CHECKPOINT", eyebrow);
            GUI.Label(new Rect(death.x + 20, death.y + 44, 395, 30), buildingCheckpoint ? "Return to checkpoint" : "Regroup inside the gate", heading);
            GUI.Label(new Rect(death.x + 20, death.y + 88, 395, 52), buildingCheckpoint ?
                "You were shot. You will return to " + checkpoint.DisplayName + " with full health." :
                "You were shot. You will return inside the main gate with full health.", body);
            GUI.Label(new Rect(death.x + 20, death.yMax - 35, 395, 22), $"Respawning in {Mathf.CeilToInt(secondsLeft)}s   /   R  Respawn now", hint);
        }
        else if (!scoped && Cursor.lockState == CursorLockMode.Locked)
            GUI.Label(new Rect(width / 2 - 18, height / 2 - 18, 36, 36), "+", crosshair);
        GUI.matrix = matrix;
    }
}

/// <summary>Player-facing drone warning and a glowing ring around its screen position.</summary>
public sealed class DroneThreatHud
{
    static readonly Color Red = new Color(1f, .22f, .18f);
    Operation1989.AI.Drone.TacticalDroneController drone;
    DroneDamage damage;
    float nextSearch;
    GUIStyle title, instruction;
    Texture2D ring;

    bool FindDrone()
    {
        if (drone && drone.isActiveAndEnabled && damage && !damage.Destroyed) return true;
        if (Time.unscaledTime < nextSearch) return false;
        nextSearch = Time.unscaledTime + 1f;
        drone = null;
        damage = null;
        foreach (var candidate in Object.FindObjectsByType<Operation1989.AI.Drone.TacticalDroneController>())
        {
            var health = candidate.GetComponent<DroneDamage>();
            if (!candidate.isActiveAndEnabled || !health || health.Destroyed) continue;
            drone = candidate;
            damage = health;
            return true;
        }
        return false;
    }

    void Styles()
    {
        if (title != null) return;
        title = MissionHudStyle.Label(MissionHudStyle.Semibold, 16, Red);
        instruction = MissionHudStyle.Label(MissionHudStyle.Regular, 12, new Color(.94f, .91f, .89f));
        title.alignment = instruction.alignment = TextAnchor.MiddleCenter;
        const int size = 128;
        ring = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Drone identification ring", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * 64);
                float distance = Mathf.Abs(radius - 46);
                float stroke = Mathf.Clamp01(2.5f - distance);
                float glow = Mathf.Exp(-distance * distance / 40f) * .4f;
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Max(stroke, glow));
            }
        ring.SetPixels32(pixels);
        ring.Apply(false, true);
    }

    public void Draw(Camera camera, float width, float height)
    {
        if (!camera || !FindDrone()) return;
        Styles();
        var banner = new Rect((width - 600) * .5f, 132, 600, 64);
        MissionHudStyle.Card(banner);
        MissionHudStyle.Fill(new Rect(banner.x, banner.y, 3, banner.height), Red);
        GUI.Label(new Rect(banner.x + 16, banner.y + 8, banner.width - 32, 24), "SHOOT DOWN THE DRONE", title);
        GUI.Label(new Rect(banner.x + 16, banner.y + 33, banner.width - 32, 21),
            "It alerts the patrol jeep to intruders. Shoot the drone marked by the red circle.", instruction);

        var collider = drone.GetComponent<Collider>();
        Bounds bounds = collider ? collider.bounds : new Bounds(drone.transform.position, Vector3.one * 2);
        Vector3 view = camera.WorldToViewportPoint(bounds.center);
        if (view.z <= camera.nearClipPlane || view.x < 0 || view.x > 1 || view.y < 0 || view.y > 1) return;
        Vector2 center = new Vector2(view.x * width, (1f - view.y) * height);
        float radius = 0;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = Vector3.Scale(bounds.extents, new Vector3(
                (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
            Vector3 projected = camera.WorldToViewportPoint(bounds.center + offset);
            if (projected.z <= camera.nearClipPlane) continue;
            Vector2 point = new Vector2(projected.x * width, (1f - projected.y) * height);
            radius = Mathf.Max(radius, Vector2.Distance(center, point));
        }
        // Leave the target visible inside a hollow ring, with space for its soft glow.
        radius = Mathf.Clamp(radius + 6, 22, 96);
        float halfSize = radius * 64f / 46f;
        var color = GUI.color;
        GUI.color = Red;
        GUI.DrawTexture(new Rect(center.x - halfSize, center.y - halfSize, halfSize * 2, halfSize * 2), ring);
        GUI.color = color;
    }

    public void Dispose() { if (ring) Object.Destroy(ring); }
}

using UnityEngine;

/// <summary>Per-run player checkpoint; death/health restoration do not discard progress.</summary>
[DisallowMultipleComponent]
public sealed class PlayerCheckpointState : MonoBehaviour
{
    PlayerCheckpoint active;
    CombatHealth health;
    float notificationUntil;
    GUIStyle label, title;
    public bool HasCheckpoint => active && active.respawnPoint;
    public string DisplayName => HasCheckpoint ? active.checkpointName : "Inside the gate";
    public Vector3 RespawnPosition => active.respawnPoint.position;
    public Quaternion RespawnRotation => active.respawnPoint.rotation;
    public bool NotificationVisible => HasCheckpoint && health && !health.Dead && Time.unscaledTime < notificationUntil;

    void Awake() { health = GetComponent<CombatHealth>(); Clear(); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BeginPlaySession()
    {
        foreach (var state in FindObjectsByType<PlayerCheckpointState>()) state.Clear();
    }
    public void Clear() { active = null; notificationUntil = 0; }
    internal bool Activate(PlayerCheckpoint checkpoint)
    {
        if (!health) health = GetComponent<CombatHealth>();
        if (!health || !health.isPlayer || health.Dead || !checkpoint || !checkpoint.respawnPoint ||
            (HasCheckpoint && checkpoint.priority <= active.priority)) return false;
        active = checkpoint; notificationUntil = Time.unscaledTime + 3;
        return true;
    }
    void OnGUI()
    {
        if (!NotificationVisible) return;
        if (label == null)
        {
            label = MissionHudStyle.Label(MissionHudStyle.Regular, 11, MissionHudStyle.Accent);
            title = MissionHudStyle.Label(MissionHudStyle.Semibold, 17, Color.white);
        }
        float scale = MissionHudStyle.Scale;
        var matrix = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        var card = new Rect((Screen.width / scale - 320) * .5f, 132, 320, 66);
        MissionHudStyle.Card(card);
        GUI.Label(new Rect(card.x + 18, card.y + 11, 284, 18), "CHECKPOINT REACHED", label);
        GUI.Label(new Rect(card.x + 18, card.y + 31, 284, 26), DisplayName, title);
        GUI.matrix = matrix;
    }
}

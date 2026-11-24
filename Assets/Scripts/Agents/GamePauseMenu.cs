using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Scene-owned pause menu. Reloading the scene also resets every mission and agent.</summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class GamePauseMenu : MonoBehaviour
{
    static GamePauseMenu instance;
    static float pausedSeconds;
    static int consumedFrame = -1;
    bool paused, restarting;
    float pausedAt, previousTimeScale;
    bool previousAudioPause, previousCursorVisible;
    CursorLockMode previousCursorLock;
    GUIStyle heading, label, button;

    public static bool IsPaused => instance && instance.paused;
    public static bool BlocksInput => IsPaused || consumedFrame == Time.frameCount;
    // Preserve unscaled timers (respawning, notifications) while excluding time spent in this menu.
    public static float UnscaledTime => (IsPaused ? instance.pausedAt : Time.unscaledTime) - pausedSeconds;
    public string RestartScenePath => gameObject.scene.path;
    public bool CanRestart => !string.IsNullOrEmpty(RestartScenePath) && Application.CanStreamedLevelBeLoaded(RestartScenePath);
    static Rect PauseButtonRect => new Rect(16, 7, 116, 32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        if (instance) instance.Resume();
        instance = null; pausedSeconds = 0; consumedFrame = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InitializeSession()
    {
        SceneManager.sceneLoaded -= InstallInScene;
        SceneManager.sceneLoaded += InstallInScene;
        InstallInScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void InstallInScene(Scene scene, LoadSceneMode mode)
    {
        if (instance && instance.gameObject.scene == scene) return;
        foreach (var existing in FindObjectsByType<GamePauseMenu>())
            if (existing.gameObject.scene == scene)
            {
                existing.Resume(); instance = existing; return;
            }
        foreach (var player in FindObjectsByType<FirstPersonController>())
            if (player.gameObject.scene == scene)
            {
                var menu = new GameObject("Pause menu");
                SceneManager.MoveGameObjectToScene(menu, scene);
                menu.AddComponent<GamePauseMenu>();
                return;
            }
    }

    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    void Update()
    {
        if (restarting) return;
        if (Keyboard.current != null && (Keyboard.current.pKey.wasPressedThisFrame ||
            (paused && Keyboard.current.escapeKey.wasPressedThisFrame)))
        {
            if (paused) Resume(); else Pause();
            return;
        }
        // Handle this click before gameplay Update can recapture the cursor or fire a rifle.
        if (!paused && Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mouse = Mouse.current.position.ReadValue();
            mouse = new Vector2(mouse.x, Screen.height - mouse.y) / MissionHudStyle.Scale;
            if (PauseButtonRect.Contains(mouse)) Pause();
        }
    }

    public void Pause()
    {
        if (paused || !isActiveAndEnabled || (instance && instance != this)) return;
        instance = this;
        previousTimeScale = Time.timeScale;
        previousAudioPause = AudioListener.pause;
        previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible;
        pausedAt = Time.unscaledTime; paused = true; consumedFrame = Time.frameCount;
        Time.timeScale = 0;
        // Unity freezes the DSP clock as well, retaining scheduled cutscene audio positions.
        AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    public void Resume()
    {
        if (!paused) return;
        pausedSeconds += Mathf.Max(0, Time.unscaledTime - pausedAt);
        paused = false; consumedFrame = Time.frameCount;
        Time.timeScale = previousTimeScale;
        AudioListener.pause = previousAudioPause;
        Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible;
    }

    public bool RestartFromBeginning()
    {
        if (restarting || !CanRestart) return false;
        Pause(); restarting = true;
        try
        {
            // Stay paused while loading. OnDisable releases our global state before
            // the new scene starts its fresh briefing, truck intro and checkpoints.
            if (SceneManager.LoadSceneAsync(RestartScenePath, LoadSceneMode.Single) != null) return true;
        }
        catch (System.Exception error) { Debug.LogException(error, this); }
        restarting = false;
        return false;
    }

    void Styles()
    {
        if (button != null) return;
        heading = MissionHudStyle.Label(MissionHudStyle.Semibold, 28, Color.white);
        label = MissionHudStyle.Label(MissionHudStyle.Regular, 14, new Color(.85f, .89f, .86f));
        heading.alignment = label.alignment = TextAnchor.MiddleCenter;
        button = new GUIStyle(GUI.skin.button)
        {
            font = MissionHudStyle.Semibold, fontSize = 15,
            alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12, 12, 6, 6)
        };
    }

    void OnGUI()
    {
        Styles();
        var matrix = GUI.matrix; int depth = GUI.depth;
        var color = GUI.color; var contentColor = GUI.contentColor; bool enabled = GUI.enabled;
        GUI.depth = -10000; GUI.color = GUI.contentColor = Color.white; GUI.enabled = true;
        float scale = MissionHudStyle.Scale, width = Screen.width / scale, height = Screen.height / scale;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        if (!paused)
        {
            if (GUI.Button(PauseButtonRect, "PAUSE  [P]", button)) Pause();
        }
        else
        {
            MissionHudStyle.Fill(new Rect(0, 0, width, height), new Color(0, 0, 0, .72f));
            var card = new Rect((width - 420) * .5f, (height - 270) * .5f, 420, 270);
            MissionHudStyle.Card(card);
            GUI.Label(new Rect(card.x + 24, card.y + 24, 372, 42), restarting ? "RESTARTING..." : "PAUSED", heading);
            GUI.Label(new Rect(card.x + 24, card.y + 73, 372, 28), "OPERATION 1989", label);
            GUI.enabled = !restarting;
            if (GUI.Button(new Rect(card.x + 30, card.y + 116, 360, 42), "Resume", button)) Resume();
            GUI.enabled = !restarting && CanRestart;
            if (GUI.Button(new Rect(card.x + 30, card.y + 172, 360, 42), "Restart from Beginning", button)) RestartFromBeginning();
            GUI.enabled = true;
            GUI.Label(new Rect(card.x + 24, card.y + 232, 372, 24), "P / Esc  Resume", label);
            var input = Event.current;
            if (input.isMouse || input.isKey || input.type == EventType.ScrollWheel) input.Use();
        }
        GUI.matrix = matrix; GUI.depth = depth; GUI.color = color; GUI.contentColor = contentColor; GUI.enabled = enabled;
    }

    void OnDisable()
    {
        Resume();
        if (instance == this) instance = null;
    }
}

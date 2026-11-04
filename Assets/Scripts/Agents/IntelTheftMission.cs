using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>USB theft, alarm, and helicopter extraction for the main-building computer.</summary>
public sealed class IntelTheftMission : MonoBehaviour
{
    public enum Phase { FindComputer, Downloading, ReadyToRemove, Escape, Complete }
    public Transform player;
    public Camera playerCamera;
    public IntelTerminal terminal;
    public HelicopterExtractionZone extraction;
    public BaseTheftAlarm alarm;
    public IntroTruckSequence intro;
    [Min(0.1f)] public float downloadSeconds = 15f;
    [Min(0.5f)] public float interactionDistance = 2.8f;
    public Phase State { get; private set; }
    public float DownloadProgress => Mathf.Clamp01(downloadElapsed / Mathf.Max(0.1f, downloadSeconds));
    public bool HasIntel => State == Phase.Escape || State == Phase.Complete;
    public bool CanControl => player && movement && movement.isActiveAndEnabled && (!health || !health.Dead) &&
        Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0 && (!intro || !intro.MissionCapturesPointer);
    int terminalInputFrame = -1;
    public bool OwnsTerminalInput => terminalInputFrame == Time.frameCount || (CanControl &&
        (State == Phase.FindComputer || State == Phase.ReadyToRemove) && IsLookingAtTerminal());
    public string ObjectiveTitle => State == Phase.Downloading ? "Copy the secret" :
        State == Phase.ReadyToRemove ? "Remove the USB" : State == Phase.Escape ? "Evacuate" :
        State == Phase.Complete ? "Intel secured" : "Recover the secret";
    public string ObjectiveText => State == Phase.Downloading ? "The USB is connected. Wait for the download to finish." :
        State == Phase.ReadyToRemove ? "Return to the terminal and press F to remove the USB." :
        State == Phase.Escape ? "Follow the compass marker with the USB." :
        State == Phase.Complete ? "You escaped with the secret. Mission accomplished." :
        "Follow the compass marker and interact with the highlighted computer.";
    public string CompassInstruction => State == Phase.Downloading ? "Wait for the transfer" :
        State == Phase.ReadyToRemove ? "Remove the USB" : State == Phase.Escape ? "Reach extraction" : "Find the computer";
    public bool IsNearTerminal
    {
        get
        {
            if (!playerCamera || !terminal || !terminal.highlightTarget) return false;
            var bounds = terminal.highlightTarget.bounds;
            if (Vector3.Distance(playerCamera.transform.position, bounds.ClosestPoint(playerCamera.transform.position)) > interactionDistance) return false;
            Vector3 target = bounds.center + Vector3.up * Mathf.Min(0.3f, bounds.extents.y * 0.5f);
            Vector3 delta = target - playerCamera.transform.position;
            return RifleWeapon.Trace(playerCamera.transform.position, delta.normalized, delta.magnitude + 0.1f, player, out var hit) &&
                hit.collider.GetComponentInParent<IntelTerminal>() == terminal;
        }
    }
    public bool ShowTerminalPrompt => !HasIntel && (!health || !health.Dead) && (!intro || (intro.HandedOff && !intro.MissionCapturesPointer)) &&
        Cursor.lockState == CursorLockMode.Locked && IsNearTerminal;

    FirstPersonController movement;
    CombatHealth health;
    float downloadElapsed;
    GUIStyle title, text, small, marker;
    readonly ObjectiveCompassHud compass = new ObjectiveCompassHud();
    readonly TerminalInteractionPopup terminalPopup = new TerminalInteractionPopup();
    public float CompassHeading
    {
        get
        {
            if (!playerCamera) return 0;
            Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f && player) forward = player.forward;
            return Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360);
        }
    }
    public float ObjectiveDistance => !player || !terminal || !extraction ? 0 : Vector3.Distance(player.position,
        State == Phase.Escape ? extraction.transform.position : terminal.InteractionFrame.position);
    public float ObjectiveBearing
    {
        get
        {
            if (!player || !playerCamera || !terminal || !extraction) return 0;
            Vector3 target = State == Phase.Escape ? extraction.transform.position : terminal.InteractionFrame.position;
            Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = player.forward;
            return Vector3.SignedAngle(forward, Vector3.ProjectOnPlane(target - playerCamera.transform.position, Vector3.up), Vector3.up);
        }
    }

    void Awake()
    {
        if (player) { movement = player.GetComponent<FirstPersonController>(); health = player.GetComponent<CombatHealth>(); }
    }
    void Start() { terminalPopup.Initialize(transform); ResetForCheckpoint(); }
    void Update()
    {
        if (!player || !terminal || !extraction) { terminalPopup.Hide(); return; }
        if (health && health.Dead) { if (State != Phase.FindComputer) ResetForCheckpoint(); terminalPopup.Hide(); return; }
        if (State == Phase.Downloading)
        {
            downloadElapsed = Mathf.Min(downloadSeconds, downloadElapsed + Time.deltaTime);
            if (DownloadProgress >= 1) { State = Phase.ReadyToRemove; terminal.RefreshDisplay(); }
        }
        if (CanControl && IsLookingAtTerminal() && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            InteractWithTerminal();
        if (State == Phase.Escape && CanControl && extraction.Contains(player.position)) TryExtract();
        UpdateTerminalPrompt();
    }

    public bool IsLookingAtTerminal()
    {
        if (!playerCamera || !terminal) return false;
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        return RifleWeapon.Trace(ray.origin, ray.direction, interactionDistance, player, out var hit) &&
            hit.collider.GetComponentInParent<IntelTerminal>() == terminal;
    }
    public bool InteractWithTerminal()
    {
        // Keep range, cover, death, cursor, and cutscene checks here, not just in the input caller.
        if (!CanControl || !IsLookingAtTerminal()) return false;
        if (State == Phase.FindComputer)
        {
            terminalInputFrame = Time.frameCount;
            downloadElapsed = 0;
            State = Phase.Downloading;
            terminal.SetUsbInserted(true);
            terminal.RefreshDisplay();
            return true;
        }
        if (State != Phase.ReadyToRemove) return false;
        terminalInputFrame = Time.frameCount;
        State = Phase.Escape;
        terminal.SetUsbInserted(false);
        terminal.RefreshDisplay();
        if (alarm) alarm.SetActive(true);
        extraction.SetMarked(true);
        return true;
    }
    public bool TryExtract()
    {
        if (State != Phase.Escape || !CanControl || !extraction || !extraction.Contains(player.position)) return false;
        State = Phase.Complete;
        if (alarm) alarm.SetActive(false);
        extraction.SetMarked(false);
        movement.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        return true;
    }
    public void ResetForCheckpoint()
    {
        State = Phase.FindComputer;
        downloadElapsed = 0;
        terminalInputFrame = -1;
        terminalPopup.Hide();
        if (alarm) alarm.SetActive(false);
        if (terminal) { terminal.SetUsbInserted(false); terminal.RefreshDisplay(); }
        if (extraction) extraction.SetMarked(false);
    }
    void Replay()
    {
        ResetForCheckpoint();
        if (health) health.Restore();
        if (intro) intro.RespawnInsideGate();
        else { movement.enabled = true; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
    void Styles()
    {
        if (text != null) return;
        title = MissionHudStyle.Label(MissionHudStyle.Semibold, 19, new Color(0.96f, 0.96f, 0.9f));
        text = MissionHudStyle.Label(MissionHudStyle.Regular, 14, new Color(0.85f, 0.89f, 0.86f));
        small = MissionHudStyle.Label(MissionHudStyle.Regular, 11, MissionHudStyle.Accent);
        marker = MissionHudStyle.Label(MissionHudStyle.Semibold, 12, MissionHudStyle.Accent);
        marker.alignment = TextAnchor.MiddleCenter;
        foreach (var style in new[] { title, text, small, marker })
            style.hover.textColor = style.active.textColor = style.focused.textColor = style.normal.textColor;
    }
    void OnGUI()
    {
        if (!player || (health && health.Dead) || (intro && (!intro.HandedOff || !intro.Entered))) return;
        Styles();
        var previous = GUI.matrix;
        var previousColor = GUI.color;
        var previousContentColor = GUI.contentColor;
        bool previousEnabled = GUI.enabled;
        GUI.color = GUI.contentColor = Color.white;
        GUI.enabled = true;
        float scale = MissionHudStyle.Scale, width = Screen.width / scale, height = Screen.height / scale;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        if (State == Phase.Downloading && !ShowTerminalPrompt)
        {
            MissionHudStyle.Card(new Rect(24, 24, 310, 72));
            GUI.Label(new Rect(44, 39, 270, 28), $"Copying {DownloadProgress * 100:0}% / {Mathf.CeilToInt(downloadSeconds - downloadElapsed)}s", text);
            MissionHudStyle.Fill(new Rect(44, 77, 270, 3), new Color(0.12f, 0.17f, 0.14f));
            MissionHudStyle.Fill(new Rect(44, 77, 270 * DownloadProgress, 3), MissionHudStyle.Accent);
        }
        if (State == Phase.Complete)
        {
            var panel = new Rect((width - 460) * 0.5f, (height - 210) * 0.5f, 460, 210);
            MissionHudStyle.Card(panel);
            GUI.Label(new Rect(panel.x + 24, panel.y + 24, 410, 25), "MISSION ACCOMPLISHED", small);
            GUI.Label(new Rect(panel.x + 24, panel.y + 56, 410, 30), "Secret extracted", title);
            GUI.Label(new Rect(panel.x + 24, panel.y + 94, 410, 44), "The USB and its secret are safe aboard the helicopter.", text);
            if (GUI.Button(new Rect(panel.x + 24, panel.y + 154, 240, 32), "Replay from checkpoint")) Replay();
        }
        else
        {
            compass.Draw(width, CompassHeading, ObjectiveBearing, ObjectiveDistance,
                State == Phase.Escape ? ObjectiveGlow.Cyan : ObjectiveGlow.Amber, CompassInstruction);
        }
        GUI.matrix = previous;
        GUI.color = previousColor;
        GUI.contentColor = previousContentColor;
        GUI.enabled = previousEnabled;
    }
    void UpdateTerminalPrompt()
    {
        if (!ShowTerminalPrompt) { terminalPopup.Hide(); return; }
        bool aimed = IsLookingAtTerminal();
        string heading = State == Phase.Downloading ? "Copying the secret" : State == Phase.ReadyToRemove ? "Transfer complete" : "Steal the secret";
        string body = State == Phase.Downloading ? $"Keep the USB connected.\n{Mathf.CeilToInt(downloadSeconds - downloadElapsed)}s remaining" :
            State == Phase.ReadyToRemove ? (aimed ? "Press F to remove the USB.\nRemoving it will trigger the alarm." : "Aim at the computer, then press F to remove the USB.") :
            aimed ? "Press F to insert the USB.\nCopy the files, then remove it." : "Aim at the computer, then press F.\nInsert the USB to copy the secret.";
        terminalPopup.Show(transform, heading, body, State == Phase.Downloading ? DownloadProgress : -1);
    }
    void OnDisable() => terminalPopup.Hide();
    void OnDestroy() { compass.Dispose(); terminalPopup.Dispose(); }
}

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Compact mission reminder. Movement dismisses it; M or the button recalls it.</summary>
public sealed class IntroMissionHud
{
    bool dismissed, ownsCursor;
    Vector3 movementAnchor;
    int consumedFrame = -1;
    CursorLockMode previousLock;
    bool previousVisibility;
    GUIStyle eyebrow, heading, body, hint, button, close;
    public bool Expanded { get; private set; }
    public bool Visible => !dismissed || Expanded;
    float Scale => MissionHudStyle.Scale;
    Rect ButtonRect => new Rect(24, Screen.height / Scale - 56, 210, 34);
    Rect CardRect => new Rect(24, ButtonRect.y - (Expanded ? 276 : 206) - 10, 440, Expanded ? 276 : 206);

    public bool CapturesPointer
    {
        get
        {
            if (Expanded || consumedFrame == Time.frameCount) return true;
            if (Cursor.lockState == CursorLockMode.Locked || Mouse.current == null) return false;
            Vector2 mouse = Mouse.current.position.ReadValue();
            mouse = new Vector2(mouse.x, Screen.height - mouse.y) / Scale;
            return ButtonRect.Contains(mouse) || (Visible && CardRect.Contains(mouse));
        }
    }

    public void Reset(Vector3 position)
    {
        ReleaseCursor();
        Expanded = dismissed = false;
        movementAnchor = position;
        consumedFrame = -1;
    }

    public void Tick(IntroTruckSequence sequence)
    {
        if (!sequence.player) return;
        if (!sequence.HandedOff)
        {
            movementAnchor = sequence.player.position;
            return;
        }
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.mKey.wasPressedThisFrame) Toggle(sequence.player.position);
        else if (Expanded && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Dismiss();

        Vector3 delta = sequence.player.position - movementAnchor;
        delta.y = 0;
        if (delta.sqrMagnitude > .15f * .15f && Visible) Dismiss();
    }

    public void Toggle(Vector3 position)
    {
        consumedFrame = Time.frameCount;
        if (Expanded) { Dismiss(); return; }
        Expanded = true;
        movementAnchor = position;
        previousLock = Cursor.lockState;
        previousVisibility = Cursor.visible;
        ownsCursor = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Dismiss()
    {
        dismissed = true;
        Expanded = false;
        consumedFrame = Time.frameCount;
        bool resumeLook = ownsCursor;
        ReleaseCursor();
        if (resumeLook)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void ReleaseCursor()
    {
        if (!ownsCursor) return;
        Cursor.lockState = previousLock;
        Cursor.visible = previousVisibility;
        ownsCursor = false;
    }

    void Styles()
    {
        if (body != null) return;
        var regular = MissionHudStyle.Regular;
        var semibold = MissionHudStyle.Semibold;
        eyebrow = Label(regular, 11, new Color(.68f, .75f, .71f));
        heading = Label(semibold, 21, new Color(.96f, .96f, .9f));
        body = Label(regular, 16, new Color(.85f, .89f, .86f));
        hint = Label(regular, 12, new Color(.65f, .73f, .69f));
        button = new GUIStyle(GUI.skin.button) { font = semibold, fontSize = 13, alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(14, 12, 0, 0) };
        button.normal.background = button.hover.background = button.active.background = Texture2D.whiteTexture;
        button.normal.textColor = button.hover.textColor = button.active.textColor = Color.white;
        close = new GUIStyle(GUI.skin.button) { font = regular, fontSize = 18 };
    }

    static GUIStyle Label(Font font, int size, Color color)
    {
        return MissionHudStyle.Label(font, size, color);
    }

    static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    public void Draw(IntroTruckSequence sequence)
    {
        Styles();
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousBackground = GUI.backgroundColor;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * Scale);
        if (Visible)
        {
            Rect card = CardRect;
            MissionHudStyle.Card(card);
            string status = sequence.MissedEntry ? "ENTRY WINDOW MISSED" : sequence.Entered ? "NEXT OBJECTIVE" : "INFILTRATION  /  01";
            bool overview = !sequence.HandedOff && sequence.Elapsed < sequence.cutsceneDuration * .48f;
            string title = overview || Expanded ? "Recover the secret" : sequence.MissedEntry ? "Regroup and try again" :
                sequence.Entered ? "Reach the main building" : "Follow the supply truck";
            string objective = sequence.MissedEntry ? "The gate has closed. Press R to replay the approach." : sequence.Entered
                ? "Find the computer in the main building. Stay out of sight." : "Slip through the gate behind the truck. Keep out of sight.";
            if (sequence.Entered && sequence.intelMission && !overview)
            {
                title = sequence.intelMission.ObjectiveTitle;
                objective = sequence.intelMission.ObjectiveText;
            }
            string description = Expanded ? sequence.missionBriefing + "\n\n" + objective : overview ? sequence.missionBriefing : objective;
            if (sequence.Entered && sequence.intelMission && !overview) description = objective;
            GUI.Label(new Rect(card.x + 20, card.y + 18, 355, 18), overview ? sequence.missionTitle : status, eyebrow);
            GUI.Label(new Rect(card.x + 20, card.y + 43, 393, 31), title, heading);
            GUI.Label(new Rect(card.x + 20, card.y + 87, 393, card.height - 125), description, body);
            string footer = !sequence.HandedOff ? "Space  Skip intro and take control" :
                sequence.MissedEntry ? "R  Retry intro   /   M  Dismiss" : Expanded ? "M / Esc  Close   /   Move to dismiss" :
                sequence.Entered ? "Move to dismiss   /   M  Mission" :
                sequence.GateClosingRequested ? "Gate closing. Move through now.   /   M  Mission" :
                "Follow closely. The gate closes as the truck turns.";
            GUI.Label(new Rect(card.x + 20, card.yMax - 29, 396, 20), footer, hint);
            if (sequence.HandedOff && GUI.Button(new Rect(card.xMax - 35, card.y + 12, 24, 24), "×", close)) Dismiss();
        }
        if (sequence.HandedOff)
        {
            GUI.backgroundColor = new Color(.08f, .12f, .1f, .96f);
            string caption = sequence.MissedEntry ? "MISSION   [M]   ·   R to retry" : "MISSION   [M]";
            if (GUI.Button(ButtonRect, caption, button)) Toggle(sequence.player.position);
            GUI.Label(new Rect(ButtonRect.xMax + 12, ButtonRect.y + 9, 235, 20),
                Expanded ? "Move to return to action" : "Esc for cursor · Click to view", hint);
        }
        GUI.backgroundColor = previousBackground;
        GUI.matrix = previousMatrix;
    }
}

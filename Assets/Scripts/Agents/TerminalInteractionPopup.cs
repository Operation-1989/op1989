using UnityEngine;
using UnityEngine.UI;

/// <summary>Passive, centered interaction guidance; never captures input or unlocks the cursor.</summary>
public sealed class TerminalInteractionPopup
{
    GameObject root, panel;
    Text heading, body;
    Image progress;
    GameObject progressTrack;
    public void Initialize(Transform owner)
    {
        if (root) return;
        Build(owner); root.SetActive(false);
    }
    public void Show(Transform owner, string title, string message, float copied)
    {
        if (!root) Build(owner);
        root.SetActive(true); heading.text = title; body.text = message;
        progressTrack.SetActive(copied >= 0);
        progress.rectTransform.sizeDelta = new Vector2(300 * Mathf.Clamp01(copied), 2);
    }
    void Build(Transform owner)
    {
        root = new GameObject("Terminal interaction popup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(owner, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
        panel = new GameObject("Centered terminal panel", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(root.transform, false);
        var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(360, 110);
        var background = panel.GetComponent<Image>(); background.color = new Color(0.015f, 0.025f, 0.03f, 0.9f); background.raycastTarget = false;
        heading = Label("Interaction heading", -15, 25, 16, MissionHudStyle.Semibold, new Color(0.94f, 0.96f, 0.95f));
        body = Label("Interaction instructions", -43, 44, 12, MissionHudStyle.Regular, new Color(0.79f, 0.84f, 0.83f));
        progressTrack = new GameObject("Copy progress track", typeof(RectTransform), typeof(Image)); progressTrack.transform.SetParent(panel.transform, false);
        var trackRect = progressTrack.GetComponent<RectTransform>(); trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 0);
        trackRect.pivot = new Vector2(0.5f, 0); trackRect.anchoredPosition = new Vector2(0, 13); trackRect.sizeDelta = new Vector2(300, 2);
        var track = progressTrack.GetComponent<Image>(); track.color = new Color(0.14f, 0.2f, 0.2f); track.raycastTarget = false;
        var fill = new GameObject("Copy progress", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(progressTrack.transform, false);
        progress = fill.GetComponent<Image>(); progress.color = new Color(0.6f, 0.72f, 0.71f); progress.raycastTarget = false;
        progress.rectTransform.anchorMin = progress.rectTransform.anchorMax = progress.rectTransform.pivot = Vector2.zero;
        progress.rectTransform.anchoredPosition = Vector2.zero;
    }
    Text Label(string name, float y, float height, int size, Font font, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(panel.transform, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(320, height);
        var label = go.GetComponent<Text>(); label.font = font; label.fontSize = size; label.color = color;
        label.alignment = TextAnchor.MiddleCenter; label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow; label.raycastTarget = false;
        return label;
    }
    public void Hide() { if (root) root.SetActive(false); }
    public void Dispose() { if (root) Object.Destroy(root); }
}

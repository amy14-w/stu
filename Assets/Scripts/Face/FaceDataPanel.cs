using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Compact face-detection readout: a "Face" drop-down button in the top-right corner opens a small translucent
// panel with only the signals Stu uses (furrow, brow down, squint, smile) and the two timers that decide when
// he asks "Stuck on a concept?". Replaces the old FaceReceiver / StuckDetector text overlays.
// Created automatically in any scene that has face detection (a StuckDetector); no scene setup needed.
public class FaceDataPanel : MonoBehaviour
{
    const float RefreshSeconds = 0.1f;
    static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.55f);
    static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.15f);
    static readonly Color Accent = new Color(1f, 0.6f, 0.2f, 1f);   // Stu orange
    static readonly Color Alert = new Color(1f, 0.35f, 0.3f, 1f);
    static readonly Color Good = new Color(0.45f, 0.9f, 0.55f, 1f);
    static readonly Color Muted = new Color(1f, 1f, 1f, 0.6f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateIfNeeded()
    {
        if (FindFirstObjectByType<StuckDetector>() == null || FindFirstObjectByType<FaceDataPanel>() != null) return;
        new GameObject("FaceDataPanel").AddComponent<FaceDataPanel>();
    }

    class Row
    {
        public Text value;
        public RectTransform fill;
        public Image fillImage;
        public RectTransform marker;
    }

    FaceReceiver receiver;
    FaceSignals signals;
    StuckDetector detector;

    GameObject panel;
    Text toggleLabel, statusText, stateText;
    Row furrow, browDown, squint, smile, frownTimer, studyTimer;
    float nextRefresh;
    bool open;
    Font font;

    void Start()
    {
        detector = FindFirstObjectByType<StuckDetector>();
        if (detector == null) { Destroy(gameObject); return; }
        signals = detector.GetComponent<FaceSignals>();
        receiver = detector.GetComponent<FaceReceiver>();

        // This panel replaces the old on-screen text overlays.
        detector.showDebug = false;
        if (receiver != null) receiver.showDebug = false;

        BuildUI();
        SetOpen(false);
    }

    void Update()
    {
        if (!open || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshSeconds;
        Refresh();
    }

    void SetOpen(bool value)
    {
        open = value;
        panel.SetActive(open);
        toggleLabel.text = open ? "Face  ▴" : "Face  ▾";
        if (open) Refresh();
    }

    void Refresh()
    {
        // Status line
        bool tracking = receiver != null && receiver.Tracking;
        if (!tracking && receiver != null && receiver.autoStart == false)
            SetStatus("Face detection off (place Stu)", Muted);
        else if (signals == null || !signals.HasFace)
            SetStatus("No face", Muted);
        else if (!signals.Calibrated)
            SetStatus($"Calibrating  {signals.CalibrationProgress * 100f:0}%  (relax)", Accent);
        else
            SetStatus($"Tracking  {(receiver != null ? receiver.Hz : 0)} Hz", Good);

        // Stu's state
        stateText.text = detector.State switch
        {
            StuckState.MaybeStuck => "Stu: noticing a frown",
            StuckState.Asking => "Stu: asking if you want a hint",
            StuckState.Cooldown => $"Stu: waiting  {detector.CooldownLeft:0}s",
            _ => detector.enabled ? "Stu: watching" : "Stu: talking with you",
        };

        // Signals (rise above the student's neutral face, as the detector uses them)
        float threshold = Mathf.Max(0.01f, detector.furrowThreshold);
        SetRow(furrow, detector.FurrowScore / (threshold * 2f), $"{detector.FurrowScore:0.00}",
               detector.IsFrowning ? Alert : Accent, 0.5f);
        if (signals != null)
        {
            SetRow(browDown, Rise(signals.BrowDown, signals.NeutralBrowDown) * 4f, $"+{Rise(signals.BrowDown, signals.NeutralBrowDown):0.00}", Accent);
            SetRow(squint, Rise(signals.Squint, signals.NeutralSquint) * 3f, $"+{Rise(signals.Squint, signals.NeutralSquint):0.00}", Accent);
            SetRow(smile, signals.Smile, $"{signals.Smile:0.00}", Good);
        }

        // Timers that decide when Stu asks
        SetRow(frownTimer, detector.FrownTime / Mathf.Max(0.1f, detector.FrownNeeded),
               $"{detector.FrownTime:0.0} / {detector.FrownNeeded:0.#}s", Accent);
        SetRow(studyTimer, detector.OnPageTime / Mathf.Max(0.1f, detector.OnPageNeeded),
               $"{detector.OnPageTime:0} / {detector.OnPageNeeded:0}s", Accent);
    }

    static float Rise(float value, float neutral) => Mathf.Max(0f, value - neutral);

    void SetStatus(string text, Color color)
    {
        statusText.text = text;
        statusText.color = color;
    }

    static void SetRow(Row row, float fill01, string value, Color color, float markerAt = -1f)
    {
        fill01 = Mathf.Clamp01(fill01);
        row.fill.anchorMax = new Vector2(fill01, 1f);
        row.fillImage.color = color;
        row.value.text = value;
        if (row.marker != null) row.marker.anchorMin = row.marker.anchorMax = new Vector2(markerAt, 0.5f);
    }

    // ---------------------------------------------------------------- UI construction

    void BuildUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        gameObject.AddComponent<GraphicRaycaster>();

        const float width = 480f, margin = 24f;

        // Drop-down button (top right)
        var toggle = Box("FaceToggle", transform, PanelColor);
        Place(toggle, new Vector2(-margin, -margin), new Vector2(170f, 60f));
        toggle.gameObject.AddComponent<Button>().onClick.AddListener(() => SetOpen(!open));
        toggleLabel = Label(toggle, 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

        // Panel under the button
        var p = Box("FacePanel", transform, PanelColor);
        panel = p.gameObject;
        const float rowH = 46f, pad = 16f;
        float height = pad * 2f + rowH * 8f;
        Place(p, new Vector2(-margin, -margin - 60f - 8f), new Vector2(width, height));

        float y = -pad;
        statusText = Line(p, ref y, rowH, 26, FontStyle.Bold);
        stateText = Line(p, ref y, rowH, 24, FontStyle.Normal);
        furrow = BarRow(p, ref y, rowH, "Furrow", withMarker: true);
        browDown = BarRow(p, ref y, rowH, "Brow down");
        squint = BarRow(p, ref y, rowH, "Squint");
        smile = BarRow(p, ref y, rowH, "Smile");
        frownTimer = BarRow(p, ref y, rowH, "Frown time");
        studyTimer = BarRow(p, ref y, rowH, "Study time");
    }

    // Anchored to the top-right corner of the screen.
    static void Place(RectTransform rt, Vector2 offset, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
    }

    static RectTransform Box(string name, Transform parent, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.color = color;
        img.rectTransform.SetParent(parent, false);
        return img.rectTransform;
    }

    Text Label(RectTransform parent, int size, Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var t = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.rectTransform.SetParent(parent, false);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = anchor;
        t.raycastTarget = false;
        return t;
    }

    // Full-width text line inside the panel.
    Text Line(RectTransform panelRect, ref float y, float h, int size, FontStyle style)
    {
        var rt = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(panelRect, false);
        SetRowRect(rt, y, h);
        y -= h;
        return Label(rt, size, Color.white, TextAnchor.MiddleLeft, style);
    }

    // "Name   [=====     ]   value"
    Row BarRow(RectTransform panelRect, ref float y, float h, string name, bool withMarker = false)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(panelRect, false);
        SetRowRect(rt, y, h);
        y -= h;

        var nameText = Label(rt, 24, Muted, TextAnchor.MiddleLeft);
        nameText.rectTransform.anchorMax = new Vector2(0.34f, 1f);

        var track = Box("Track", rt, TrackColor);
        track.anchorMin = new Vector2(0.35f, 0.32f);
        track.anchorMax = new Vector2(0.74f, 0.68f);
        track.offsetMin = track.offsetMax = Vector2.zero;

        var fill = Box("Fill", track, Accent);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;

        RectTransform marker = null;
        if (withMarker)
        {
            marker = Box("Threshold", track, Color.white);
            marker.sizeDelta = new Vector2(3f, 26f);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        }

        var value = Label(rt, 24, Color.white, TextAnchor.MiddleRight);
        value.rectTransform.anchorMin = new Vector2(0.75f, 0f);

        return new Row { value = value, fill = fill, fillImage = fill.GetComponent<Image>(), marker = marker };
    }

    static void SetRowRect(RectTransform rt, float y, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(18f, y - h);
        rt.offsetMax = new Vector2(-18f, y);
    }
}

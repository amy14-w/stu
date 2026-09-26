using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Speech bubble that floats above Stu:
// - small "?" while StuckDetector is MaybeStuck (Stu noticed the frown),
// - "Stuck on a concept? Want a hint?" with Yes / Not now while Asking,
// - a short reply after the answer.
// Builds its own UI in code so no scene/prefab edits are needed.
[RequireComponent(typeof(StuckDetector))]
public class StuSpeechBubble : MonoBehaviour
{
    [Tooltip("Stu's root. Leave empty to find the placeholder Stu spawned by StuWindowDemo.")]
    public Transform stu;
    public string question = "Stuck on a concept?\nWant a hint?";
    public string yesReply = "Okay! Tell me what's tricky\nand we'll work through it.";
    public string notNowReply = "No problem, I'll be right here.";
    public float replySeconds = 3f;

    StuckDetector detector;
    Camera cam;
    Canvas canvas;
    RectTransform bubble, hintMark, buttonRow;
    Text bubbleText;
    float replyUntil;
    float nextFindTime;

    void Awake() => detector = GetComponent<StuckDetector>();

    void OnEnable()
    {
        detector.AskStarted += OnAsk;
        detector.Answered += OnAnswered;
    }

    void OnDisable()
    {
        detector.AskStarted -= OnAsk;
        detector.Answered -= OnAnswered;
        StuWindowDemo.BlockTaps = false;
    }

    void Start()
    {
        EnsureEventSystem();
        BuildUI();
        bubble.gameObject.SetActive(false);
        hintMark.gameObject.SetActive(false);
    }

    void OnAsk()
    {
        bubbleText.text = question;
        buttonRow.gameObject.SetActive(true);
        bubble.gameObject.SetActive(true);
        StuWindowDemo.BlockTaps = true; // so tapping a button doesn't also move Stu
    }

    void OnAnswered(bool yes)
    {
        bubbleText.text = yes ? yesReply : notNowReply;
        buttonRow.gameObject.SetActive(false);
        replyUntil = Time.time + replySeconds;
        StuWindowDemo.BlockTaps = false;
    }

    void LateUpdate()
    {
        if (canvas == null) return;
        if (stu == null && Time.time >= nextFindTime) FindStu();
        if (cam == null) cam = Camera.main;

        bool asking = detector.State == StuckState.Asking;
        bool replying = Time.time < replyUntil;
        bubble.gameObject.SetActive(asking || replying);
        hintMark.gameObject.SetActive(!asking && !replying && detector.State == StuckState.MaybeStuck);

        Vector2 anchor = StuHeadScreenPoint();
        if (bubble.gameObject.activeSelf) Place(bubble, anchor);
        if (hintMark.gameObject.activeSelf)
        {
            Place(hintMark, anchor);
            // Gentle pulse, grows as the frown timer fills up.
            float fill = Mathf.Clamp01(detector.FrownTime / Mathf.Max(0.1f, detector.FrownNeeded));
            hintMark.localScale = Vector3.one * (0.8f + 0.3f * fill + 0.05f * Mathf.Sin(Time.time * 6f));
        }
    }

    // ---------------------------------------------------------------- placement

    void FindStu()
    {
        nextFindTime = Time.time + 0.5f;
        var go = GameObject.Find("Stu (placeholder)");
        if (go == null) go = GameObject.Find("Stu");
        if (go != null) stu = go.transform;
    }

    Vector2 StuHeadScreenPoint()
    {
        var fallback = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
        if (stu == null || cam == null) return fallback;

        Vector3 top = stu.position;
        var renderers = stu.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            top = new Vector3(b.center.x, b.max.y, b.center.z);
        }
        Vector3 sp = cam.WorldToScreenPoint(top);
        return sp.z > 0f ? new Vector2(sp.x, sp.y + 20f) : fallback;
    }

    void Place(RectTransform rt, Vector2 screenPoint)
    {
        // Overlay canvas: world position == screen pixels. Pivot is bottom-center.
        float s = canvas.scaleFactor;
        float halfW = rt.sizeDelta.x * s * 0.5f, h = rt.sizeDelta.y * s, m = 16f;
        float x = Mathf.Clamp(screenPoint.x, halfW + m, Screen.width - halfW - m);
        float y = Mathf.Clamp(screenPoint.y, m, Screen.height - h - m);
        rt.position = new Vector3(x, y, 0f);
    }

    // ---------------------------------------------------------------- UI construction

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    void BuildUI()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        canvas = new GameObject("StuBubbleCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        // Main bubble
        bubble = Panel("Bubble", canvas.transform, new Vector2(640f, 280f), new Color(1f, 1f, 1f, 0.95f));
        bubbleText = Label("Text", bubble, font, 44, new Color(0.15f, 0.15f, 0.2f));
        var tr = bubbleText.rectTransform;
        tr.anchorMin = new Vector2(0f, 0.4f); tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(24f, 0f); tr.offsetMax = new Vector2(-24f, -16f);

        buttonRow = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
        buttonRow.SetParent(bubble, false);
        buttonRow.anchorMin = Vector2.zero; buttonRow.anchorMax = new Vector2(1f, 0.4f);
        buttonRow.offsetMin = new Vector2(24f, 20f); buttonRow.offsetMax = new Vector2(-24f, -4f);
        var row = buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 24f;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = true;

        MakeButton("Yes", buttonRow, font, new Color(0.25f, 0.65f, 0.35f), () => detector.Answer(true));
        MakeButton("Not now", buttonRow, font, new Color(0.55f, 0.55f, 0.6f), () => detector.Answer(false));

        // Small "?" shown while Stu is noticing a frown
        hintMark = Panel("NoticingMark", canvas.transform, new Vector2(90f, 90f), new Color(1f, 1f, 1f, 0.9f));
        var q = Label("Q", hintMark, font, 64, new Color(0.95f, 0.5f, 0.1f));
        q.text = "?";
        q.fontStyle = FontStyle.Bold;
        q.raycastTarget = false;
        hintMark.GetComponent<Image>().raycastTarget = false;
    }

    static RectTransform Panel(string name, Transform parent, Vector2 size, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.color = color;
        var rt = img.rectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;
        return rt;
    }

    static Text Label(string name, RectTransform parent, Font font, int size, Color color)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.rectTransform.SetParent(parent, false);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        return t;
    }

    static void MakeButton(string label, RectTransform parent, Font font, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var img = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
        img.color = color;
        img.rectTransform.SetParent(parent, false);
        img.GetComponent<Button>().onClick.AddListener(onClick);
        var t = Label("Label", img.rectTransform, font, 38, Color.white);
        t.text = label;
        t.raycastTarget = false;
    }
}

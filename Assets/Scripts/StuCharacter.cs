using UnityEngine;

// Makes Pip (S'dhari's model + PipAnimator + PipExpressions in Assets/Pip) react to face detection as Stu.
// Lives on the root of Assets/Prefabs/StuPip.prefab (built by Stu > Character > Set Up Pip).
//
//   student frowning (MaybeStuck)  -> think face
//   Stu asks "Stuck on a concept?" -> NeedHint
//   "Yes"                          -> Point
//   big smile held ~1 s            -> Cheer
//   face gone / looking away       -> Point (gentle "back to the page" nudge)
// With the AI tutor (StuConversation) in the scene:
//   "Yes, I want a hint"           -> starts the voice conversation
//   AI tools point / cheer / think -> Point / Cheer / think face
// The PipAnimator states set their own faces on enter (PipExpressionState). Stu always asks; it never
// labels the student.
public class StuCharacter : MonoBehaviour
{
    [Header("Model")]
    public Animator animator;
    public PipExpressions expressions;
    [Tooltip("Child holding the model; rotated by Model Yaw Offset if Pip doesn't face the student.")]
    public Transform model;
    public float modelYawOffset = 0f;

    [Header("Reactions")]
    public bool reactToFace = true;
    [Tooltip("Smoothed smile above this for Smile Hold Seconds -> Cheer.")]
    public float smileCheerThreshold = 0.5f;
    public float smileHoldSeconds = 1f;
    public float cheerCooldownSeconds = 20f;
    [Tooltip("No face / looking away this long -> Point nudge.")]
    public float distractedSeconds = 5f;
    public float nudgeCooldownSeconds = 30f;

    // PipAnimator triggers
    static readonly int NeedHintTrigger = Animator.StringToHash("NeedHint");
    static readonly int PointTrigger = Animator.StringToHash("Point");
    static readonly int CheerTrigger = Animator.StringToHash("Cheer");

    StuckDetector detector;
    FaceSignals signals;
    StuConversation conversation;
    StuckState lastState = StuckState.Watching;
    float smileTime, awayTime, nextCheer, nextNudge;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (expressions == null) expressions = GetComponentInChildren<PipExpressions>();
        if (animator != null) animator.applyRootMotion = false; // placement code owns the position
    }

    void OnDisable()
    {
        Subscribe(false);
        SubscribeConversation(false);
    }

    void Update()
    {
        if (model != null) model.localRotation = Quaternion.Euler(0f, modelYawOffset, 0f);
        if (conversation == null && StuConversation.Instance != null)
        {
            conversation = StuConversation.Instance;
            SubscribeConversation(true);
        }
        if (!reactToFace) return;

        if (detector == null)
        {
            detector = FindFirstObjectByType<StuckDetector>();
            if (detector != null)
            {
                signals = detector.GetComponent<FaceSignals>();
                Subscribe(true);
            }
        }
        if (detector == null) return;

        var state = detector.State;
        if (state != lastState)
        {
            if (state == StuckState.MaybeStuck && expressions != null) expressions.ShowThinkFace();
            if (state == StuckState.Asking) Trigger(NeedHintTrigger);
            lastState = state;
        }

        if (signals == null || state == StuckState.Asking) return;

        // Happy: sustained smile
        smileTime = signals.HasFace && signals.Smile > smileCheerThreshold ? smileTime + Time.deltaTime : 0f;
        if (smileTime >= smileHoldSeconds && Time.time >= nextCheer)
        {
            nextCheer = Time.time + cheerCooldownSeconds;
            Trigger(CheerTrigger);
        }

        // Distracted: no face, or eyes clearly off to the side
        bool away = !signals.HasFace || signals.LookOut > detector.lookAwayThreshold;
        awayTime = away ? awayTime + Time.deltaTime : 0f;
        if (awayTime >= distractedSeconds && Time.time >= nextNudge)
        {
            nextNudge = Time.time + nudgeCooldownSeconds;
            Trigger(PointTrigger);
        }
    }

    void OnAnswered(bool wantsHint)
    {
        if (wantsHint)
        {
            Trigger(PointTrigger);
            // Hand over to the AI tutor (uses the current problem's context if one was loaded).
            if (conversation != null && !conversation.IsInConversation) conversation.StartConversation(null);
        }
        else if (expressions != null) expressions.ShowBaseFace();
    }

    // AI tutor tool calls -> Pip
    void OnAIPoint() => Trigger(PointTrigger);
    void OnAICheer() => Trigger(CheerTrigger);
    void OnAIThink() { if (expressions != null) expressions.ShowThinkFace(); }

    void SubscribeConversation(bool on)
    {
        if (conversation == null) return;
        conversation.OnPoint -= OnAIPoint;
        conversation.OnCheer -= OnAICheer;
        conversation.OnThink -= OnAIThink;
        if (!on) return;
        conversation.OnPoint += OnAIPoint;
        conversation.OnCheer += OnAICheer;
        conversation.OnThink += OnAIThink;
    }

    public void Cheer() => Trigger(CheerTrigger);

    void Trigger(int trigger)
    {
        if (animator != null && animator.runtimeAnimatorController != null) animator.SetTrigger(trigger);
    }

    void Subscribe(bool on)
    {
        if (detector == null) return;
        detector.Answered -= OnAnswered;
        if (on) detector.Answered += OnAnswered;
    }
}

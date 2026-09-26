using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Decides when the student might be STUCK (eyes on the page + furrowed brows for a while) and asks.
// Stu never assumes: it only raises AskStarted; the student answers Yes / Not now via Answer().
// This is the stuck slice of StuMoodController (Step 3); the other moods come later.
public enum StuckState { Watching, MaybeStuck, Asking, Cooldown }

[RequireComponent(typeof(FaceSignals))]
public class StuckDetector : MonoBehaviour
{
    [Header("Demo mode (short timers for judging)")]
    public bool demoMode = true;

    [Header("Frown / furrow (tune on the phone with the debug overlay)")]
    [Tooltip("FurrowScore above this counts as frowning. Lower = more sensitive.")]
    public float furrowThreshold = 0.3f;
    [Tooltip("Each gain multiplies how far that value rises above the student's neutral face. 0 = ignore.")]
    public float browDownGain = 10f;
    public float squintGain = 3f;       // on the S22 below the face, a frown shows mostly as squint
    public float noseSneerGain = 4f;
    public float browInnerUpGain = 0f;
    public float mouthFrownGain = 0f;
    [Tooltip("Smiling also raises squint; above this smoothed smile, don't count it as a frown.")]
    public float smileSuppressThreshold = 0.5f;

    [Header("Eyes on page")]
    [Tooltip("Off: 'studying' = face present and not looking away. On: also needs eyes down (lookDown). " +
             "Eyes lift when frowning at a phone on a stand, so this is off by default.")]
    public bool requireEyesOnPage = false;
    public float lookDownThreshold = 0.3f;
    [Tooltip("Frowning while clearly looking to the side (above this) doesn't count as stuck.")]
    public float lookAwayThreshold = 0.5f;
    [Tooltip("Looking up above this counts as looking away.")]
    public float lookUpAwayThreshold = 0.4f;
    [Tooltip("How fast frown time drains when the frown stops (1 = same speed it builds).")]
    public float frownDrainRate = 0.5f;
    [Tooltip("Face missing this long = distracted, not stuck: timers reset.")]
    public float noFaceResetSeconds = 3f;

    [Header("Timers: normal / demo")]
    public float onPageSeconds = 60f;
    public float demoOnPageSeconds = 10f;
    public float frownSeconds = 8f;
    public float demoFrownSeconds = 1f;
    public float cooldownSeconds = 120f;
    public float demoCooldownSeconds = 20f;

    public bool showDebug = true;

    public StuckState State { get; private set; } = StuckState.Watching;
    public float OnPageTime { get; private set; }
    public float FrownTime { get; private set; }
    public float CooldownLeft { get; private set; }
    public bool IsFrowning { get; private set; }
    public bool IsOnPage { get; private set; }
    public float FurrowScore { get; private set; }

    public float OnPageNeeded => demoMode ? demoOnPageSeconds : onPageSeconds;
    public float FrownNeeded => demoMode ? demoFrownSeconds : frownSeconds;
    float CooldownNeeded => demoMode ? demoCooldownSeconds : cooldownSeconds;

    public event Action AskStarted;
    public event Action<bool> Answered; // true = "Yes", false = "Not now"

    FaceSignals signals;
    int maxTouches;

    void Awake() => signals = GetComponent<FaceSignals>();

    void Update()
    {
        HiddenGestures();

        float dt = Time.deltaTime;
        bool present = signals.HasFace;

        if (signals.NoFaceSeconds > noFaceResetSeconds)
        {
            OnPageTime = 0f;
            FrownTime = 0f;
        }

        // Gaze is noisy, so studying/frowning only need "not looking away", not strictly "eyes down".
        bool lookingAway = signals.LookOut > lookAwayThreshold || signals.LookUp > lookUpAwayThreshold;
        IsOnPage = present && !lookingAway && (!requireEyesOnPage || signals.LookDown > lookDownThreshold);
        FurrowScore = ComputeFurrow();
        IsFrowning = present && signals.Calibrated && !lookingAway && signals.Smile < smileSuppressThreshold
                     && FurrowScore > furrowThreshold;

        if (State == StuckState.Asking) return;

        if (IsOnPage) OnPageTime += dt;
        // Frown time builds while frowning and drains slowly when brows relax,
        // so a brief relaxed moment doesn't wipe it out.
        FrownTime = IsFrowning ? FrownTime + dt : Mathf.Max(0f, FrownTime - dt * frownDrainRate);

        if (State == StuckState.Cooldown)
        {
            CooldownLeft -= dt;
            if (CooldownLeft > 0f) return;
            State = StuckState.Watching;
        }

        State = FrownTime > 0.5f ? StuckState.MaybeStuck : StuckState.Watching;
        if (FrownTime >= FrownNeeded && OnPageTime >= OnPageNeeded) Ask();
    }

    // Back to a clean Watching state (used when Stu is unlocked / face detection pauses).
    public void ResetState()
    {
        State = StuckState.Watching;
        OnPageTime = 0f;
        FrownTime = 0f;
        CooldownLeft = 0f;
    }

    public void Ask()
    {
        if (State == StuckState.Asking) return;
        State = StuckState.Asking;
        AskStarted?.Invoke();
    }

    public void Answer(bool wantsHint)
    {
        if (State != StuckState.Asking) return;
        FrownTime = 0f;
        OnPageTime = 0f;
        CooldownLeft = CooldownNeeded; // don't re-ask right away either way
        State = StuckState.Cooldown;
        Answered?.Invoke(wantsHint);
    }

    // Strongest rise above neutral among the frown-related blendshapes, each scaled by its gain.
    float ComputeFurrow()
    {
        if (!signals.Calibrated) return 0f;
        float Rise(float v, float neutral, float gain) => gain * Mathf.Max(0f, v - neutral);
        return Mathf.Max(
            Mathf.Max(Rise(signals.BrowDown, signals.NeutralBrowDown, browDownGain),
                      Rise(signals.Squint, signals.NeutralSquint, squintGain)),
            Mathf.Max(Rise(signals.NoseSneer, signals.NeutralNoseSneer, noseSneerGain),
                      Mathf.Max(Rise(signals.BrowInnerUp, signals.NeutralBrowInnerUp, browInnerUpGain),
                                Rise(signals.MouthFrown, signals.NeutralMouthFrown, mouthFrownGain))));
    }

    // Hidden shortcuts, decided when all fingers lift (so a 4-finger tap doesn't also count as 3):
    // 3-finger tap = ask now (judging), 4-finger tap = recalibrate neutral face. Editor: H / C.
    void HiddenGestures()
    {
        var ts = Touchscreen.current;
        if (ts != null)
        {
            int count = 0;
            foreach (var t in ts.touches) if (t.press.isPressed) count++;
            if (count > 0) maxTouches = Mathf.Max(maxTouches, count);
            else if (maxTouches > 0)
            {
                if (maxTouches == 3) Ask();
                else if (maxTouches >= 4) signals.Recalibrate();
                maxTouches = 0;
            }
        }
        var kb = Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame) Ask();
        if (kb != null && kb.cKey.wasPressedThisFrame) signals.Recalibrate();
    }

    void OnGUI()
    {
        if (!showDebug) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        style.normal.textColor = new Color(1f, 0.6f, 1f);
        string text =
            $"STUCK {State}{(demoMode ? " (demo)" : "")}\n" +
            (signals.Calibrated
                ? $"furrow {FurrowScore:0.00}/{furrowThreshold:0.00} {(IsFrowning ? "FROWN" : "")}\n" +
                  $"  +brow {signals.BrowDown - signals.NeutralBrowDown:0.00} +squint {signals.Squint - signals.NeutralSquint:0.00} +sneer {signals.NoseSneer - signals.NeutralNoseSneer:0.00}\n"
                : $"calibrating neutral face {signals.CalibrationProgress * 100f:0}% (relax, look at page)\n") +
            $"lookDown(sm) {signals.LookDown:0.00}/{lookDownThreshold:0.00} {(IsOnPage ? "ON PAGE" : "")}\n" +
            $"frown {FrownTime:0.0}/{FrownNeeded:0}s  page {OnPageTime:0}/{OnPageNeeded:0}s" +
            (State == StuckState.Cooldown ? $"\ncooldown {CooldownLeft:0}s" : "");
        GUI.Label(new Rect(Screen.width * 0.62f, 330f, Screen.width * 0.36f, 200f), text, style);
    }
}

using UnityEngine;

// Smooths FaceReceiver's raw blendshapes (exponential moving average, ~0.5 s) and tracks "no face" time.
// Left/right pairs are combined so callers don't care about camera mirroring.
[RequireComponent(typeof(FaceReceiver))]
public class FaceSignals : MonoBehaviour
{
    [Tooltip("Time constant of the moving average, in seconds.")]
    public float smoothingSeconds = 0.5f;
    [Tooltip("If no frame arrives for this long, treat it as no face (tracker stopped or stalled).")]
    public float staleSeconds = 1f;
    [Tooltip("Seconds of face data averaged as the student's neutral face (at start, or after Recalibrate).")]
    public float calibrationSeconds = 3f;

    public bool HasFace { get; private set; }
    public float NoFaceSeconds { get; private set; }

    // Smoothed, 0..1
    public float BrowDown { get; private set; }     // avg L/R, furrowed brows
    public float BrowInnerUp { get; private set; }
    public float LookDown { get; private set; }     // avg L/R, eyes on the page
    public float LookUp { get; private set; }       // avg L/R
    public float LookOut { get; private set; }      // max L/R, looking to either side
    public float Blink { get; private set; }        // avg L/R
    public float Squint { get; private set; }       // avg L/R
    public float NoseSneer { get; private set; }    // avg L/R
    public float MouthFrown { get; private set; }   // avg L/R
    public float JawOpen { get; private set; }
    public float Smile { get; private set; }        // avg L/R

    // Neutral face (averaged during calibration). Frowning is measured as the rise above these,
    // because resting values differ a lot between people, lighting and camera angles.
    public bool Calibrated { get; private set; }
    public float CalibrationProgress => Mathf.Clamp01(calibTime / Mathf.Max(0.01f, calibrationSeconds));
    public float NeutralBrowDown { get; private set; }
    public float NeutralBrowInnerUp { get; private set; }
    public float NeutralSquint { get; private set; }
    public float NeutralNoseSneer { get; private set; }
    public float NeutralMouthFrown { get; private set; }

    FaceReceiver receiver;
    float lastFrameTime = -1f;
    float calibTime;
    int calibCount;
    float sumBrowDown, sumBrowInnerUp, sumSquint, sumSneer, sumMouthFrown;

    void Awake() => receiver = GetComponent<FaceReceiver>();
    void OnEnable() => receiver.FrameReceived += OnFrame;
    void OnDisable() => receiver.FrameReceived -= OnFrame;

    void OnFrame(FaceFrame f)
    {
        float now = Time.time;
        float dt = lastFrameTime < 0f ? 1f : now - lastFrameTime;
        lastFrameTime = now;

        HasFace = f.face;
        if (!f.face) return; // keep last values; callers gate on HasFace

        float browDown = (f.browDownL + f.browDownR) * 0.5f;
        float squint = (f.eyeSquintL + f.eyeSquintR) * 0.5f;
        float sneer = (f.noseSneerL + f.noseSneerR) * 0.5f;
        float mouthFrown = (f.mouthFrownL + f.mouthFrownR) * 0.5f;
        if (!Calibrated) Calibrate(browDown, f.browInnerUp, squint, sneer, mouthFrown, Mathf.Min(dt, 0.2f));

        float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, smoothingSeconds));
        BrowDown = Mathf.Lerp(BrowDown, browDown, a);
        BrowInnerUp = Mathf.Lerp(BrowInnerUp, f.browInnerUp, a);
        LookDown = Mathf.Lerp(LookDown, (f.eyeLookDownL + f.eyeLookDownR) * 0.5f, a);
        LookUp = Mathf.Lerp(LookUp, (f.eyeLookUpL + f.eyeLookUpR) * 0.5f, a);
        LookOut = Mathf.Lerp(LookOut, Mathf.Max(f.eyeLookOutL, f.eyeLookOutR), a);
        Blink = Mathf.Lerp(Blink, (f.eyeBlinkL + f.eyeBlinkR) * 0.5f, a);
        Squint = Mathf.Lerp(Squint, squint, a);
        NoseSneer = Mathf.Lerp(NoseSneer, sneer, a);
        MouthFrown = Mathf.Lerp(MouthFrown, mouthFrown, a);
        JawOpen = Mathf.Lerp(JawOpen, f.jawOpen, a);
        Smile = Mathf.Lerp(Smile, (f.mouthSmileL + f.mouthSmileR) * 0.5f, a);
    }

    // Call while the student looks at their work with a relaxed face.
    public void Recalibrate()
    {
        Calibrated = false;
        calibTime = 0f;
        calibCount = 0;
        sumBrowDown = sumBrowInnerUp = sumSquint = sumSneer = sumMouthFrown = 0f;
    }

    void Calibrate(float browDown, float browInnerUp, float squint, float sneer, float mouthFrown, float dt)
    {
        calibTime += dt;
        calibCount++;
        sumBrowDown += browDown; sumBrowInnerUp += browInnerUp; sumSquint += squint;
        sumSneer += sneer; sumMouthFrown += mouthFrown;
        if (calibTime < calibrationSeconds) return;
        NeutralBrowDown = sumBrowDown / calibCount;
        NeutralBrowInnerUp = sumBrowInnerUp / calibCount;
        NeutralSquint = sumSquint / calibCount;
        NeutralNoseSneer = sumSneer / calibCount;
        NeutralMouthFrown = sumMouthFrown / calibCount;
        Calibrated = true;
        Debug.Log($"[FaceSignals] neutral: brow {NeutralBrowDown:0.00} inner {NeutralBrowInnerUp:0.00} " +
                  $"squint {NeutralSquint:0.00} sneer {NeutralNoseSneer:0.00} mouthFrown {NeutralMouthFrown:0.00}");
    }

    void Update()
    {
        bool stale = lastFrameTime < 0f || Time.time - lastFrameTime > staleSeconds;
        if (stale) HasFace = false;
        NoFaceSeconds = HasFace ? 0f : NoFaceSeconds + Time.deltaTime;
    }
}

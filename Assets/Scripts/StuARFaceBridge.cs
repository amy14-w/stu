using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Connects Quinton's AR flow (SurfaceManager + StuSpawner + diagrams) with face detection and Pip's reactions.
// Used in Assets/Scenes/StuIntegrated.unity (built by Stu > Integrate > Create Integrated Scene).
//
// iPhone: ARKit tracks the desk with the back camera and the student's face with the front TrueDepth camera
// at the same time, so Stu stays anchored on the real desk while face detection runs.
// Face detection runs only while Stu is on a chosen surface:
//   Stu spawned  AND  surface set  AND  not changing surface (SurfaceManager turns the plane manager off)
//
// Android: face detection stays off in this scene (ARCore + the front-camera plugin together deadlocked the
// S22 camera service). Android face detection still works in StuWindowDemo.
public class StuARFaceBridge : MonoBehaviour
{
    [Header("AR (from the XR Origin)")]
    public ARPlaneManager planeManager;
    [Tooltip("ARKit world + user face tracking. Enabled on iPhone only.")]
    public ARFaceManager faceManager;

    [Header("Face detection")]
    public FaceReceiver faceReceiver;

    [Header("Performance")]
    public int targetFrameRate = 60;

    public bool StudyMode { get; private set; }

    static bool FaceSupported => Application.platform == RuntimePlatform.IPhonePlayer;

    StuCharacter character;
    StuckDetector detector;
    FaceSignals signals;
    bool calibratedOnce;

    void Awake()
    {
        Application.targetFrameRate = targetFrameRate;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.orientation = ScreenOrientation.LandscapeLeft; // phone lies sideways on its stand

        if (faceManager != null) faceManager.enabled = FaceSupported;
        var arkitSource = FindFirstObjectByType<ARKitFaceSource>();
        if (arkitSource != null) arkitSource.enabled = FaceSupported;
        if (faceReceiver != null) faceReceiver.autoStart = false; // starts once Stu is placed
    }

    void Start()
    {
        detector = FindFirstObjectByType<StuckDetector>();
        if (detector != null) signals = detector.GetComponent<FaceSignals>();
    }

    void Update()
    {
        if (character == null)
        {
            character = FindFirstObjectByType<StuCharacter>();
            if (character != null) character.reactToFace = StudyMode; // standby until face detection runs
        }
        if (!FaceSupported) return;

        bool surfaceSet = SurfaceManager.Instance != null && SurfaceManager.Instance.IsSurfaceInitialized();
        bool choosingSurface = planeManager != null && planeManager.enabled;
        bool stuPlaced = character != null && surfaceSet && !choosingSurface;

        if (stuPlaced && !StudyMode) EnterStudy();
        else if (!stuPlaced && StudyMode) ExitStudy();
    }

    void EnterStudy()
    {
        StudyMode = true;
        if (signals != null && !calibratedOnce) { signals.Recalibrate(); calibratedOnce = true; } // neutral face once
        if (detector != null) detector.ResetState();
        if (faceReceiver != null) faceReceiver.BeginTracking();
        if (character != null) character.reactToFace = true;
    }

    void ExitStudy()
    {
        StudyMode = false;
        if (character != null)
        {
            character.reactToFace = false;
            if (character.expressions != null) character.expressions.ShowBaseFace();
        }
        if (faceReceiver != null) faceReceiver.EndTracking();
        if (detector != null) detector.ResetState();
    }
}

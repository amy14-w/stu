using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// iPhone face source. ARKit runs the back camera for the AR view and, at the same time, tracks the student's
// face with the front TrueDepth camera ("world tracking + user face tracking", Face ID iPhones XS/XR and newer).
// It fills the same FaceFrame the Android plugin sends, so FaceSignals / StuckDetector / StuSpeechBubble work
// unchanged. Like Android, only blendshape numbers are used; no face images leave the device.
//
// Needs: ARFaceManager on the XR Origin, ARCameraManager facing World, and
// Project Settings > XR Plug-in Management > Apple ARKit > Face Tracking turned on.
[RequireComponent(typeof(FaceReceiver))]
public class ARKitFaceSource : MonoBehaviour
{
    public ARFaceManager faceManager;
    public ARCameraManager cameraManager;
    [Tooltip("Frames per second sent to FaceReceiver (Android sends ~12).")]
    public float publishHz = 15f;

    // ARKitBlendShapeLocation values. Unity.XR.ARKit only compiles for iOS, so the ids are mirrored here.
    const int BrowDownLeft = 0, BrowDownRight = 1, BrowInnerUp = 2;
    const int EyeBlinkLeft = 8, EyeBlinkRight = 9, EyeLookDownLeft = 10, EyeLookDownRight = 11;
    const int EyeLookOutLeft = 14, EyeLookOutRight = 15, EyeLookUpLeft = 16, EyeLookUpRight = 17;
    const int EyeSquintLeft = 18, EyeSquintRight = 19, JawOpen = 24, MouthFrownLeft = 29, MouthFrownRight = 30;
    const int MouthSmileLeft = 43, MouthSmileRight = 44, NoseSneerLeft = 49, NoseSneerRight = 50;

    FaceReceiver receiver;
    float nextPublish;
    string lastError;

    void Awake() => receiver = GetComponent<FaceReceiver>();

    void Update()
    {
        if (faceManager == null) faceManager = FindFirstObjectByType<ARFaceManager>();
        if (cameraManager == null) cameraManager = FindFirstObjectByType<ARCameraManager>();

        if (faceManager == null || !faceManager.enabled)
        {
            ReportError("no ARFaceManager in the scene");
            return;
        }
        if (ARSession.state < ARSessionState.SessionTracking) return; // still starting up
        if (faceManager.subsystem == null || !faceManager.subsystem.running)
        {
            ReportError("ARKit face tracking not running (needs a Face ID iPhone and ARKit Face Tracking enabled)");
            return;
        }
        if (cameraManager != null && cameraManager.currentFacingDirection == CameraFacingDirection.User)
            ReportError("ARKit switched to the front camera: world + face tracking unsupported on this device?");

        if (Time.unscaledTime < nextPublish) return;
        nextPublish = Time.unscaledTime + 1f / Mathf.Max(1f, publishHz);

        var frame = new FaceFrame { t = (long)(Time.realtimeSinceStartupAsDouble * 1000.0) };
        ARFace face = null;
        foreach (var f in faceManager.trackables)
        {
            if (f.trackingState == TrackingState.Tracking) { face = f; break; }
        }

        if (face != null)
        {
            var result = faceManager.TryGetBlendShapes(face, Allocator.Temp);
            if (result.status.IsSuccess() && result.value.IsCreated)
            {
                var shapes = result.value;
                frame.face = shapes.Length > 0;
                foreach (var s in shapes) Set(frame, s.blendShapeId, s.weight);
                shapes.Dispose();
            }
        }
        receiver.Publish(frame);
    }

    static void Set(FaceFrame f, int id, float w)
    {
        switch (id)
        {
            case BrowDownLeft: f.browDownL = w; break;
            case BrowDownRight: f.browDownR = w; break;
            case BrowInnerUp: f.browInnerUp = w; break;
            case EyeBlinkLeft: f.eyeBlinkL = w; break;
            case EyeBlinkRight: f.eyeBlinkR = w; break;
            case EyeLookDownLeft: f.eyeLookDownL = w; break;
            case EyeLookDownRight: f.eyeLookDownR = w; break;
            case EyeLookOutLeft: f.eyeLookOutL = w; break;
            case EyeLookOutRight: f.eyeLookOutR = w; break;
            case EyeLookUpLeft: f.eyeLookUpL = w; break;
            case EyeLookUpRight: f.eyeLookUpR = w; break;
            case EyeSquintLeft: f.eyeSquintL = w; break;
            case EyeSquintRight: f.eyeSquintR = w; break;
            case JawOpen: f.jawOpen = w; break;
            case MouthFrownLeft: f.mouthFrownL = w; break;
            case MouthFrownRight: f.mouthFrownR = w; break;
            case MouthSmileLeft: f.mouthSmileL = w; break;
            case MouthSmileRight: f.mouthSmileR = w; break;
            case NoseSneerLeft: f.noseSneerL = w; break;
            case NoseSneerRight: f.noseSneerR = w; break;
        }
    }

    void ReportError(string message)
    {
        if (message == lastError) return;
        lastError = message;
        receiver.OnFaceError(message);
    }
}

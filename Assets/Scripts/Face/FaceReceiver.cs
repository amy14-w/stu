using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// Stu Step 1 (Unity side): starts the native front-camera face tracker (Assets/Plugins/Android/StuFaceTracker.java)
// and receives its JSON. The GameObject MUST be named "FaceReceiver" because Java calls
// UnitySendMessage("FaceReceiver", "OnFace", json). Raw values only; smoothing/states belong in FaceSignals (Step 3).
[Serializable]
public class FaceFrame
{
    public bool face;
    public float browDownL, browDownR, browInnerUp;
    public float eyeLookDownL, eyeLookDownR, eyeLookUpL, eyeLookUpR, eyeLookOutL, eyeLookOutR;
    public float eyeBlinkL, eyeBlinkR, eyeSquintL, eyeSquintR;
    public float noseSneerL, noseSneerR, mouthFrownL, mouthFrownR;
    public float jawOpen, mouthSmileL, mouthSmileR;
    public long t;
}

public class FaceReceiver : MonoBehaviour
{
    [Tooltip("Start the front-camera tracker automatically (after camera permission is granted).")]
    public bool autoStart = true;
    [Tooltip("Rotation MediaPipe applies to front frames. -1 = automatic. Try 0/90/180/270 if face is never found.")]
    public int rotationOverride = -1;
    [Tooltip("Minimum ms between frames sent to MediaPipe (70 = ~14 Hz).")]
    public int minFrameIntervalMs = 70;
    [Tooltip("Extra wait (after the back camera is running, and after resume) before opening the front camera. " +
             "Opening both cameras at the same instant deadlocked the S22's camera service.")]
    public float startDelaySeconds = 2f;
    [Tooltip("Give up waiting for StuWindowDemo's back camera after this long and start anyway.")]
    public float backCameraWaitTimeout = 15f;
    public bool showDebug = true;

    public FaceFrame Latest { get; private set; } = new FaceFrame();
    public float LastFrameTime { get; private set; } = -1f;
    public int Hz { get; private set; }
    public string LastError { get; private set; } = "";
    public bool Running => tracker != null;

    public event Action<FaceFrame> FrameReceived;

    int frameCount;
    float hzTimer;
    bool wantRunning;   // true once Start() has permission; OnApplicationPause only resumes after that
    Coroutine pendingStart;
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject tracker;
#else
    object tracker;
#endif

    void Awake()
    {
        if (gameObject.name != "FaceReceiver")
        {
            Debug.LogWarning($"[FaceReceiver] Renaming '{gameObject.name}' to 'FaceReceiver' so native messages arrive.");
            gameObject.name = "FaceReceiver";
        }
    }

    IEnumerator Start()
    {
        if (!autoStart) yield break;
#if UNITY_ANDROID && !UNITY_EDITOR
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            // StuWindowDemo usually asks already; ask here too in case it runs alone.
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1f);
        }
#endif
        wantRunning = true;
        pendingStart = StartCoroutine(StartAfterDelay());
    }

    IEnumerator StartAfterDelay()
    {
        // Let the back camera finish opening first (only if StuWindowDemo is in the scene).
        if (FindFirstObjectByType<StuWindowDemo>() != null)
        {
            float waited = 0f;
            while (!StuWindowDemo.BackCameraReady && waited < backCameraWaitTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        yield return new WaitForSecondsRealtime(startDelaySeconds);
        pendingStart = null;
        StartTracker();
    }

    public void StartTracker()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tracker != null) return;
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            tracker = new AndroidJavaObject("com.stu.face.StuFaceTracker", activity, gameObject.name);
            tracker.Set("rotationOverride", rotationOverride);
            tracker.Set("minFrameIntervalMs", minFrameIntervalMs);
            tracker.Call("start");
            Debug.Log("[FaceReceiver] tracker started");
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Debug.LogError("[FaceReceiver] start failed: " + e);
            tracker = null;
        }
#else
        LastError = "face tracking only runs on an Android device";
#endif
    }

    public void StopTracker()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tracker == null) return;
        tracker.Call("stop");
        tracker.Dispose();
        tracker = null;
#endif
    }

    // Called from Java via UnitySendMessage.
    public void OnFace(string json)
    {
        try
        {
            var frame = JsonUtility.FromJson<FaceFrame>(json);
            Latest = frame;
            LastFrameTime = Time.time;
            frameCount++;
            FrameReceived?.Invoke(frame);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FaceReceiver] bad json: " + e.Message + " :: " + json);
        }
    }

    // Called from Java via UnitySendMessage.
    public void OnFaceError(string message)
    {
        LastError = message;
        Debug.LogWarning("[FaceReceiver] " + message);
    }

    void Update()
    {
        hzTimer += Time.unscaledDeltaTime;
        if (hzTimer >= 1f)
        {
            Hz = frameCount;
            frameCount = 0;
            hzTimer = 0f;
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (pendingStart != null) { StopCoroutine(pendingStart); pendingStart = null; }
        if (paused)
        {
            StopTracker();
            StuWindowDemo.BackCameraReady = false; // wait for a fresh back frame after resume
        }
        else if (wantRunning) pendingStart = StartCoroutine(StartAfterDelay());
    }

    void OnDestroy() => StopTracker();

    void OnGUI()
    {
        if (!showDebug) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        style.normal.textColor = Color.cyan;
        var f = Latest;
        float age = LastFrameTime < 0 ? -1f : Time.time - LastFrameTime;
        string text =
            $"FACE {(Running ? "on" : "off")} {Hz} Hz  age {age:0.0}s\n" +
            $"face={f.face}\n" +
            $"browDown {f.browDownL:0.00} {f.browDownR:0.00}  in {f.browInnerUp:0.00}\n" +
            $"squint {f.eyeSquintL:0.00} {f.eyeSquintR:0.00}  sneer {f.noseSneerL:0.00} {f.noseSneerR:0.00}\n" +
            $"lookDown {f.eyeLookDownL:0.00} {f.eyeLookDownR:0.00}\n" +
            $"lookUp   {f.eyeLookUpL:0.00} {f.eyeLookUpR:0.00}\n" +
            $"lookOut  {f.eyeLookOutL:0.00} {f.eyeLookOutR:0.00}\n" +
            $"blink    {f.eyeBlinkL:0.00} {f.eyeBlinkR:0.00}\n" +
            $"jaw {f.jawOpen:0.00}  smile {f.mouthSmileL:0.00} {f.mouthSmileR:0.00}\n" +
            (string.IsNullOrEmpty(LastError) ? "" : "ERR " + LastError);
        GUI.Label(new Rect(Screen.width * 0.62f, 20f, Screen.width * 0.36f, Screen.height - 40f), text, style);
    }
}

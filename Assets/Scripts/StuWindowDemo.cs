using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// STU WINDOW DEMO v2 (Galaxy S22 test)
// - Back camera fills the screen, placeholder Stu stands on the desk, front camera runs in the corner.
// - v2: camera test modes (Both / BackOnly / FrontOnly), camera order switch, on-screen device list,
//        and a material slot that fixes the pink/magenta Stu on the phone.
public class StuWindowDemo : MonoBehaviour
{
    public enum CameraTest { Both, BackOnly, FrontOnly }

    [Header("Camera test")]
    public CameraTest cameraTest = CameraTest.Both;
    [Tooltip("Which camera to open first when testing Both.")]
    public bool openBackFirst = true;

    [Header("Character")]
    [Tooltip("Optional: drag a character prefab here. Leave empty for the built-in placeholder Stu.")]
    public GameObject stuPrefab;
    [Tooltip("Drag any URP Lit material here. Without it, Stu shows up pink on the phone.")]
    public Material stuMaterial;
    public float stuHeight = 0.12f;

    [Header("Virtual camera (tune so Stu sits on the real desk)")]
    public float cameraHeight = 0.35f;
    public float cameraTilt = 25f;
    public float cameraFov = 60f;

    // Set by UI (e.g. StuSpeechBubble) while it needs taps, so a button press doesn't also move Stu.
    public static bool BlockTaps;
    // True once the back WebCamTexture has delivered a frame. FaceReceiver waits for this before opening
    // the front camera: opening both at the same instant deadlocked the S22's camera service.
    public static bool BackCameraReady;

    Camera cam;
    WebCamTexture backCam, frontCam;
    RawImage backImage, frontImage;
    AspectRatioFitter backFitter;
    Transform stu;
    Vector3 target = new Vector3(0f, 0f, 1f);
    readonly Plane desk = new Plane(Vector3.up, Vector3.zero);

    int backFrames, frontFrames, backFps, frontFps;
    float fpsTimer;
    string status = "Starting...";
    string deviceInfo = "";

    IEnumerator Start()
    {
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        SetupCamera();
        SetupCanvases();
        stu = CreateStu();
        stu.position = target;

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            while (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                yield return new WaitForSeconds(0.5f);
        }
#endif

        string backName = null, frontName = null;
        var devices = WebCamTexture.devices;
        for (int i = 0; i < devices.Length; i++)
        {
            var d = devices[i];
            deviceInfo += $"\n[{i}] {d.name} {(d.isFrontFacing ? "FRONT" : "back")}";
            if (d.isFrontFacing) { if (frontName == null) frontName = d.name; }
            else { if (backName == null) backName = d.name; }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        try {
            var cameraManager = AndroidApplication.currentContext.Call<AndroidJavaObject>("getSystemService", "camera");
            deviceInfo += "\nConcurrent pairs: " + cameraManager.Call<AndroidJavaObject>("getConcurrentCameraIds").Call<string>("toString");
        } catch (System.Exception e) { deviceInfo += "\nConcurrent check failed: " + e.Message; }
#endif

#if UNITY_EDITOR
        // Mac has one webcam: use it as the background so the Editor preview looks right.
        if (backName == null) { backName = frontName; frontName = null; }
#endif

        bool wantBack = cameraTest != CameraTest.FrontOnly && backName != null;
        bool wantFront = cameraTest != CameraTest.BackOnly && frontName != null;

        if (openBackFirst)
        {
            if (wantBack) StartBack(backName);
            if (wantBack && wantFront) yield return new WaitForSeconds(1.5f);
            if (wantFront) StartFront(frontName);
        }
        else
        {
            if (wantFront) StartFront(frontName);
            if (wantBack && wantFront) yield return new WaitForSeconds(1.5f);
            if (wantBack) StartBack(backName);
        }

        status = $"Mode: {cameraTest}, back first: {openBackFirst}. Tap the desk to move Stu";
    }

    void StartBack(string name)
    {
        backCam = new WebCamTexture(name, 1280, 720, 30);
        backImage.texture = backCam;
        backCam.Play();
    }

    void StartFront(string name)
    {
        frontCam = new WebCamTexture(name, 640, 480, 30);
        frontImage.texture = frontCam;
        frontImage.enabled = true;
        frontCam.Play();
    }

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.transform.SetPositionAndRotation(new Vector3(0f, cameraHeight, 0f), Quaternion.Euler(cameraTilt, 0f, 0f));
        cam.fieldOfView = cameraFov;
        cam.nearClipPlane = 0.01f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        if (FindFirstObjectByType<Light>() == null)
        {
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    void SetupCanvases()
    {
        var bgCanvas = new GameObject("BackCameraCanvas").AddComponent<Canvas>();
        bgCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        bgCanvas.worldCamera = cam;
        bgCanvas.planeDistance = 10f;

        backImage = new GameObject("BackFeed").AddComponent<RawImage>();
        backImage.transform.SetParent(bgCanvas.transform, false);
        var bgRect = backImage.rectTransform;
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
        backFitter = backImage.gameObject.AddComponent<AspectRatioFitter>();
        backFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

        var overlay = new GameObject("OverlayCanvas").AddComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;

        frontImage = new GameObject("FrontFeed").AddComponent<RawImage>();
        frontImage.transform.SetParent(overlay.transform, false);
        var frRect = frontImage.rectTransform;
        frRect.anchorMin = frRect.anchorMax = frRect.pivot = new Vector2(1f, 1f);
        frRect.anchoredPosition = new Vector2(-20f, -20f);
        frRect.sizeDelta = new Vector2(320f, 240f);
        frontImage.enabled = false;
    }

    Material MakeMat(Color c)
    {
        // Copying a material that's referenced in the scene keeps its shader in the build (no pink).
        Material m = stuMaterial != null
            ? new Material(stuMaterial)
            : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.color = c;
        return m;
    }

    Transform CreateStu()
    {
        if (stuPrefab != null) return Instantiate(stuPrefab).transform;

        var root = new GameObject("Stu (placeholder)").transform;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(1f, 0.6f, 0.2f));

        for (int side = -1; side <= 1; side += 2)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = side < 0 ? "EyeL" : "EyeR";
            eye.transform.SetParent(root, false);
            eye.transform.localPosition = new Vector3(0.2f * side, 1.5f, 0.42f);
            eye.transform.localScale = Vector3.one * 0.25f;
            eye.GetComponent<Renderer>().sharedMaterial = MakeMat(Color.white);

            var pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pupil.name = "Pupil";
            pupil.transform.SetParent(eye.transform, false);
            pupil.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            pupil.transform.localScale = Vector3.one * 0.5f;
            pupil.GetComponent<Renderer>().sharedMaterial = MakeMat(Color.black);
        }

        root.localScale = Vector3.one * (stuHeight / 2f);
        return root;
    }

    void Update()
    {
        CountFrames();
        FixFeedOrientation(backCam, backImage, false, backFitter);
        FixFeedOrientation(frontCam, frontImage, true, null);

        if (stu == null) return;

        if (!BlockTaps && TryGetTap(out Vector2 tap))
        {
            Ray ray = cam.ScreenPointToRay(tap);
            if (desk.Raycast(ray, out float dist)) target = ray.GetPoint(dist);
        }

        float bob = Mathf.Abs(Mathf.Sin(Time.time * 3f)) * stuHeight * 0.08f;
        stu.position = Vector3.Lerp(stu.position, target + Vector3.up * bob, 8f * Time.deltaTime);

        Vector3 look = cam.transform.position - stu.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.0001f)
            stu.rotation = Quaternion.Slerp(stu.rotation, Quaternion.LookRotation(look), 5f * Time.deltaTime);
    }

    static bool TryGetTap(out Vector2 pos)
    {
        pos = default;
        var ts = Touchscreen.current;
        if (ts != null && ts.primaryTouch.press.wasPressedThisFrame)
        {
            pos = ts.primaryTouch.position.ReadValue();
            return true;
        }
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            pos = mouse.position.ReadValue();
            return true;
        }
        return false;
    }

    static void FixFeedOrientation(WebCamTexture tex, RawImage img, bool mirror, AspectRatioFitter fitter)
    {
        if (tex == null || tex.width < 100) return;
        img.rectTransform.localEulerAngles = new Vector3(0f, 0f, -tex.videoRotationAngle);
        bool flipY = tex.videoVerticallyMirrored;
        img.uvRect = new Rect(mirror ? 1f : 0f, flipY ? 1f : 0f, mirror ? -1f : 1f, flipY ? -1f : 1f);
        if (fitter != null) fitter.aspectRatio = (float)tex.width / tex.height;
    }

    void CountFrames()
    {
        if (backCam != null && backCam.didUpdateThisFrame) { backFrames++; BackCameraReady = true; }
        if (frontCam != null && frontCam.didUpdateThisFrame) frontFrames++;
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= 1f)
        {
            backFps = backFrames; frontFps = frontFrames;
            backFrames = frontFrames = 0; fpsTimer = 0f;
        }
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        style.normal.textColor = Color.yellow;
        string backState = backCam == null ? "not opened" : $"playing={backCam.isPlaying} fps={backFps}";
        string frontState = frontCam == null ? "not opened" : $"playing={frontCam.isPlaying} fps={frontFps}";
        GUI.Label(new Rect(20f, 20f, Screen.width * 0.6f, Screen.height - 40f),
            $"BACK {backState}\nFRONT {frontState}\n{status}\nCameras:{deviceInfo}", style);
    }

    void OnDestroy()
    {
        BackCameraReady = false;
        if (backCam != null) backCam.Stop();
        if (frontCam != null) frontCam.Stop();
    }
}
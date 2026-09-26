using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// STU WINDOW DEMO (Galaxy S22 test)
// - Back camera fills the screen (the desk on the other side of the phone).
// - A placeholder Stu stands on that desk. Tap/click to move him. He bobs and faces you.
// - Front camera runs at the same time (small preview, top right) -> later feeds MediaPipe.
//
// Setup: new EMPTY scene (no AR Session / XR Origin), add an empty GameObject,
// attach this script, put the scene first in Build Profiles, Build And Run.
// Everything else (camera, canvases, light, placeholder character) is created by code.
public class StuWindowDemo : MonoBehaviour
{
    [Header("Character")]
    [Tooltip("Optional: drag a character prefab here. Leave empty for the built-in placeholder Stu.")]
    public GameObject stuPrefab;
    [Tooltip("Height of the placeholder Stu in scene units (meters).")]
    public float stuHeight = 0.12f;

    [Header("Virtual camera (tune so Stu sits on the real desk)")]
    public float cameraHeight = 0.35f;   // how high the phone is above the desk
    public float cameraTilt = 25f;       // how much the back camera looks down
    public float cameraFov = 60f;        // roughly the back camera's vertical FOV in landscape

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
        foreach (var d in WebCamTexture.devices)
        {
            if (d.isFrontFacing) { if (frontName == null) frontName = d.name; }
            else { if (backName == null) backName = d.name; }
        }

        // In the Mac Editor the built-in webcam usually shows up as "not front facing",
        // so it becomes the background. That's fine for testing Stu placement.
        #if UNITY_EDITOR
        if (backName == null) { backName = frontName; frontName = null; }
#endif
        if (backName != null)
        {
            backCam = new WebCamTexture(backName, 1280, 720, 30);
            backImage.texture = backCam;
            backCam.Play();
        }

        yield return new WaitForSeconds(1.5f); // let the first camera settle before opening the second

        if (frontName != null)
        {
            frontCam = new WebCamTexture(frontName, 640, 480, 30);
            frontImage.texture = frontCam;
            frontCam.Play();
            frontImage.enabled = true;
        }

        status = "Tap the desk to move Stu";
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
        // Back camera feed: rendered BEHIND the 3D scene so Stu draws on top of it.
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

        // Front camera preview: small, on top of everything (debug only; hide for the demo).
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

    Transform CreateStu()
    {
        if (stuPrefab != null) return Instantiate(stuPrefab).transform;

        // Placeholder Stu: orange capsule with googly eyes. Replace with the real model later.
        var root = new GameObject("Stu (placeholder)").transform;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); // height 2, radius 0.5
        body.name = "Body";
        body.transform.SetParent(root, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);       // feet on the desk
        body.GetComponent<Renderer>().material.color = new Color(1f, 0.6f, 0.2f);

        for (int side = -1; side <= 1; side += 2)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = side < 0 ? "EyeL" : "EyeR";
            eye.transform.SetParent(root, false);
            eye.transform.localPosition = new Vector3(0.2f * side, 1.5f, 0.42f);
            eye.transform.localScale = Vector3.one * 0.25f;

            var pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pupil.name = "Pupil";
            pupil.transform.SetParent(eye.transform, false);
            pupil.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            pupil.transform.localScale = Vector3.one * 0.5f;
            pupil.GetComponent<Renderer>().material.color = Color.black;
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

        // Tap (phone) or click (Editor) on the desk to move Stu there.
        if (TryGetTap(out Vector2 tap))
        {
            Ray ray = cam.ScreenPointToRay(tap);
            if (desk.Raycast(ray, out float dist)) target = ray.GetPoint(dist);
        }

        // Idle bob + walk toward the target + turn to face the student.
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
        if (tex == null || tex.width < 100) return; // not started yet
        img.rectTransform.localEulerAngles = new Vector3(0f, 0f, -tex.videoRotationAngle);
        bool flipY = tex.videoVerticallyMirrored;
        img.uvRect = new Rect(mirror ? 1f : 0f, flipY ? 1f : 0f, mirror ? -1f : 1f, flipY ? -1f : 1f);
        if (fitter != null) fitter.aspectRatio = (float)tex.width / tex.height;
    }

    void CountFrames()
    {
        if (backCam != null && backCam.didUpdateThisFrame) backFrames++;
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
        var style = new GUIStyle(GUI.skin.label) { fontSize = 32 };
        style.normal.textColor = Color.yellow;
        string verdict = backFps > 5 && frontFps > 5 ? "PASS: both cameras live"
                       : "Checking... FAIL if one fps stays 0";
        GUI.Label(new Rect(20f, 20f, Screen.width * 0.6f, 200f),
            $"BACK fps={backFps}  FRONT fps={frontFps}\n{verdict}\n{status}", style);
    }

    void OnDestroy()
    {
        if (backCam != null) backCam.Stop();
        if (frontCam != null) frontCam.Stop();
    }
}
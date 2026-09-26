using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// iPhone (ARKit) counterpart of StuWindowDemo's placement: the AR camera background shows the desk through
// the back camera, Stu is dropped on the first detected table plane in the middle of the view, and a tap
// moves Stu to another spot on the plane. If no plane is found (phone already still on its stand), Stu is
// placed a short distance in front of the camera instead.
public class StuARPlacement : MonoBehaviour
{
    [Tooltip("Optional: character prefab. Leave empty for the placeholder Stu.")]
    public GameObject stuPrefab;
    [Tooltip("Any URP Lit material (e.g. StuMat). Without it, Stu shows up pink on the phone.")]
    public Material stuMaterial;
    [Tooltip("Stu's height in meters on the real desk.")]
    public float stuHeight = 0.12f;
    [Tooltip("Place Stu in front of the camera if no desk plane is found by then.")]
    public float fallbackSeconds = 5f;
    public float fallbackDistance = 0.45f;
    public float fallbackBelowCamera = 0.2f;
    public bool showDebug = true;

    public ARRaycastManager raycastManager;

    static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    Camera cam;
    Transform stu;
    Vector3 target;
    bool placed;
    float startTime;
    string status = "Point the back camera at the desk...";

    void Start()
    {
        Screen.orientation = ScreenOrientation.LandscapeLeft; // phone lies sideways on its stand, like Android
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
        startTime = Time.time;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || ARSession.state < ARSessionState.SessionTracking) return;

        if (!placed)
        {
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.4f);
            if (RaycastDesk(center, out Pose pose)) Place(pose.position, "Stu is on the desk. Tap to move.");
            else if (Time.time - startTime > fallbackSeconds)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                Place(cam.transform.position + fwd * fallbackDistance + Vector3.down * fallbackBelowCamera,
                      "No desk found: Stu placed in front. Tap the desk to move.");
            }
            return;
        }

        if (!StuWindowDemo.BlockTaps && TryGetTap(out Vector2 tap) && RaycastDesk(tap, out Pose tapPose))
            target = tapPose.position;

        float bob = Mathf.Abs(Mathf.Sin(Time.time * 3f)) * stuHeight * 0.08f;
        stu.position = Vector3.Lerp(stu.position, target + Vector3.up * bob, 8f * Time.deltaTime);

        Vector3 look = cam.transform.position - stu.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.0001f)
            stu.rotation = Quaternion.Slerp(stu.rotation, Quaternion.LookRotation(look), 5f * Time.deltaTime);
    }

    void Place(Vector3 position, string message)
    {
        stu = stuPrefab != null ? Instantiate(stuPrefab).transform : StuPlaceholder.Create(stuMaterial, stuHeight);
        stu.position = target = position;
        placed = true;
        status = message;
    }

    bool RaycastDesk(Vector2 screenPoint, out Pose pose)
    {
        pose = default;
        if (raycastManager == null || !raycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon))
            return false;
        pose = hits[0].pose;
        return true;
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
        return false;
    }

    void OnGUI()
    {
        if (!showDebug) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        style.normal.textColor = Color.yellow;
        GUI.Label(new Rect(20f, 20f, Screen.width * 0.6f, 200f), $"AR {ARSession.state}\n{status}", style);
    }
}

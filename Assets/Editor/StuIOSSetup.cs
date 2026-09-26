using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// iPhone setup for Stu.
// - Stu > iOS > Create AR Scene: builds Assets/Scenes/StuARDemo.unity (AR Session, XR Origin with plane,
//   raycast and face managers, FaceReceiver + ARKitFaceSource + stuck prompt, Stu placement).
// - Switching the build platform puts the right scene first in the build list automatically:
//   iOS -> StuARDemo, Android -> StuWindowDemo. (Also available as Stu > Use Scene For Current Platform.)
public static class StuIOSSetup
{
    const string ARScene = "Assets/Scenes/StuARDemo.unity";
    const string AndroidScene = "Assets/Scenes/StuWindowDemo.unity";
    const string StuMaterial = "Assets/StuMat.mat";

    [MenuItem("Stu/iOS/Create AR Scene")]
    static void CreateARScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ARScene) &&
            !EditorUtility.DisplayDialog("Stu", $"{ARScene} already exists. Replace it?", "Replace", "Cancel"))
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Selection.activeObject = null; // AR Foundation's menu items parent new objects to the selection

        if (!EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session") ||
            !EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)"))
        {
            Debug.LogError("[Stu] Couldn't run AR Foundation's GameObject/XR menu items. Is AR Foundation installed?");
            return;
        }

        var origin = Object.FindFirstObjectByType<XROrigin>();
        if (origin == null)
        {
            Debug.LogError("[Stu] XR Origin wasn't created.");
            return;
        }
        // The template's XR Origin may already include some managers; reuse them.
        var planes = GetOrAdd<ARPlaneManager>(origin.gameObject);
        planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        var raycasts = GetOrAdd<ARRaycastManager>(origin.gameObject);
        var faces = GetOrAdd<ARFaceManager>(origin.gameObject);
        faces.requestedMaximumFaceCount = 1;
        var cameraManager = origin.Camera.GetComponent<ARCameraManager>();
        cameraManager.requestedFacingDirection = CameraFacingDirection.World; // back camera view + front face tracking

        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var receiver = new GameObject("FaceReceiver");
        receiver.AddComponent<FaceReceiver>();
        receiver.AddComponent<FaceSignals>();
        receiver.AddComponent<StuckDetector>();
        receiver.AddComponent<StuSpeechBubble>();
        var source = receiver.AddComponent<ARKitFaceSource>();
        source.faceManager = faces;
        source.cameraManager = cameraManager;

        var placement = new GameObject("Stu Placement").AddComponent<StuARPlacement>();
        placement.stuMaterial = AssetDatabase.LoadAssetAtPath<Material>(StuMaterial);
        placement.stuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/StuPip.prefab"); // null -> capsule
        placement.raycastManager = raycasts;
        if (placement.stuMaterial == null) Debug.LogWarning($"[Stu] {StuMaterial} not found: Stu may render pink.");

        EditorSceneManager.SaveScene(scene, ARScene);
        UseScenesFor(EditorUserBuildSettings.activeBuildTarget);
        Debug.Log($"[Stu] Created {ARScene}. Switch the build platform to iOS to build it.");
    }

    static T GetOrAdd<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

    [MenuItem("Stu/Use Scene For Current Platform")]
    static void UseScenesForCurrentPlatform() => UseScenesFor(EditorUserBuildSettings.activeBuildTarget);

    // Puts the platform's Stu scene first (enabled) and disables the other platform's Stu scene.
    static void UseScenesFor(BuildTarget target)
    {
        string want = target == BuildTarget.iOS ? ARScene : target == BuildTarget.Android ? AndroidScene : null;
        string other = want == ARScene ? AndroidScene : ARScene;
        if (want == null || !File.Exists(want)) return;

        var scenes = EditorBuildSettings.scenes.Where(s => s.path != want).ToList();
        foreach (var s in scenes)
            if (s.path == other) s.enabled = false;
        scenes.Insert(0, new EditorBuildSettingsScene(want, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[Stu] Build scene for {target}: {want}");
    }

    class PlatformSwitchHook : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;
        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => UseScenesFor(newTarget);
    }
}

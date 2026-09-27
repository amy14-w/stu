using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Stu > Integrate > Create Integrated Scene
// Copies the AR scene (ARAndAII if present, else Quinton) to Assets/Scenes/StuIntegrated.unity (the source
// scene is not modified) and adds:
//   - StuPipAR.prefab: StuPip (animated Pip + StuCharacter) + Quinton's Stu component and DiagramAnchor,
//     scaled to real size, used by StuSpawner.
//   - Face detection (FaceReceiver, FaceSignals, StuckDetector, StuSpeechBubble, ARKitFaceSource) and
//     StuARFaceBridge, which runs it only while Stu is placed on a chosen surface.
//   - ARFaceManager on the XR Origin for iPhone (disabled on Android at runtime).
//   - Change Surface / Test Problem / Spawn Stu buttons moved to the bottom right.
// Then makes StuIntegrated the build scene for Android and iOS.
public static class StuIntegrationSetup
{
    // ARAndAII = Quinton's AR scene + the AI tutor (StuConversation); falls back to Quinton's scene.
    const string AIScene = "Assets/Scenes/ARAndAII.unity";
    const string QuintonScene = "Assets/Scenes/Quinton.unity";
    static string SourceScene => File.Exists(AIScene) ? AIScene : QuintonScene;
    const string TargetScene = "Assets/Scenes/StuIntegrated.unity";
    const string PipPrefabPath = "Assets/Prefabs/StuPip.prefab";
    const string ARPrefabPath = "Assets/Prefabs/StuPipAR.prefab";
    const float StuHeightMeters = 0.13f;                         // about Quinton's Stu (MainCharacter at 0.065 scale)
    static readonly Vector3 DiagramOffset = new Vector3(0.2f, 0f, 0.2f); // same as Stu.prefab
    // Bottom-up order in the bottom-right corner
    static readonly string[] Buttons = { "SpawnStuButton", "ChangeSurfaceButton", "TestProblemButton" };

    [MenuItem("Stu/Integrate/Create Integrated Scene")]
    static void CreateIntegratedScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(SourceScene) || AssetDatabase.LoadAssetAtPath<GameObject>(PipPrefabPath) == null)
        {
            Debug.LogError($"[Stu] Need {SourceScene} and {PipPrefabPath} (Stu > Character > Set Up Pip).");
            return;
        }
        if (File.Exists(TargetScene))
        {
            if (!EditorUtility.DisplayDialog("Stu", $"{TargetScene} already exists. Rebuild it from {SourceScene}?", "Rebuild", "Cancel"))
                return;
            AssetDatabase.DeleteAsset(TargetScene);
        }

        var stuPrefab = BuildARPrefab();
        if (!AssetDatabase.CopyAsset(SourceScene, TargetScene))
        {
            Debug.LogError($"[Stu] Couldn't copy {SourceScene}.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

        // Stu = Pip
        var spawner = Object.FindFirstObjectByType<StuSpawner>();
        if (spawner != null) SetField(spawner, "stuPrefab", stuPrefab);
        else Debug.LogWarning("[Stu] No StuSpawner in the scene.");

        // AR pieces
        var origin = Object.FindFirstObjectByType<XROrigin>();
        var planeManager = Object.FindFirstObjectByType<ARPlaneManager>();
        var cameraManager = origin != null ? origin.Camera.GetComponent<ARCameraManager>() : null;
        ARFaceManager faces = null;
        if (origin != null)
        {
            if (!origin.TryGetComponent(out faces)) faces = origin.gameObject.AddComponent<ARFaceManager>();
            faces.requestedMaximumFaceCount = 1;
            faces.enabled = false; // StuARFaceBridge enables it on iPhone only
        }
        if (cameraManager != null) cameraManager.requestedFacingDirection = CameraFacingDirection.World;

        // Face detection
        var receiverGo = new GameObject("FaceReceiver");
        var receiver = receiverGo.AddComponent<FaceReceiver>();
        receiver.autoStart = false;
        receiverGo.AddComponent<FaceSignals>();
        receiverGo.AddComponent<StuckDetector>();
        receiverGo.AddComponent<StuSpeechBubble>();
        var arkitSource = receiverGo.AddComponent<ARKitFaceSource>();
        arkitSource.faceManager = faces;
        arkitSource.cameraManager = cameraManager;

        var bridge = new GameObject("StuARFaceBridge").AddComponent<StuARFaceBridge>();
        bridge.planeManager = planeManager;
        bridge.faceManager = faces;
        bridge.faceReceiver = receiver;

        MoveButtonsBottomRight();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        StuIOSSetup.UseScenesForActivePlatform();
        Debug.Log($"[Stu] Created {TargetScene} from {SourceScene} (+ Pip + face detection). It is now the build scene.");
    }

    // StuPip variant + Quinton's Stu component/DiagramAnchor, scaled to StuHeightMeters.
    static GameObject BuildARPrefab()
    {
        var pipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PipPrefabPath);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(pipPrefab);
        root.name = "StuPipAR";

        var model = root.GetComponent<StuCharacter>().model;
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0 && model != null)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            if (b.size.y > 0.0001f) model.localScale *= StuHeightMeters / b.size.y;
        }

        var anchor = new GameObject("DiagramAnchor").transform;
        anchor.SetParent(root.transform, false);
        var stu = root.AddComponent<Stu>();
        SetField(stu, "diagramAnchor", anchor);
        var so = new SerializedObject(stu);
        so.FindProperty("diagramOffset").vector3Value = DiagramOffset;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, ARPrefabPath); // saved as a variant of StuPip
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void MoveButtonsBottomRight()
    {
        for (int i = 0; i < Buttons.Length; i++)
        {
            var go = GameObject.Find(Buttons[i]);
            if (go == null) { Debug.LogWarning($"[Stu] Button {Buttons[i]} not found."); continue; }
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(360f, 100f);
            rt.anchoredPosition = new Vector2(-40f, 40f + i * 120f);
            EditorUtility.SetDirty(rt);
        }
    }

    static void SetField(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

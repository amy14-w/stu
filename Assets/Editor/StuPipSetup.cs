using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Stu > Character > Set Up Pip (prefab + scenes)
// Builds Assets/Prefabs/StuPip.prefab from S'dhari's Pip assets (pip.fbx + PipAnimator + PipExpressions +
// face materials), adds StuCharacter so Pip reacts to face detection, and sets it as the Stu prefab in
// StuWindowDemo (Android) and StuARDemo (iOS). The placeholder capsule stays the fallback when no prefab is set.
public static class StuPipSetup
{
    const string ModelPath = "Assets/Pip/Models/pip.fbx";
    const string ControllerPath = "Assets/Pip/Animations/PipAnimator.controller";
    const string PrefabPath = "Assets/Prefabs/StuPip.prefab";
    const string BaseFacePath = "Assets/Pip/Materials/pip_baseFace.mat";
    const string ThinkFacePath = "Assets/Pip/Materials/pip_thinkFace.mat";
    const string CheerFacePath = "Assets/Pip/Materials/pip_cheerFace.mat";
    const long FaceRendererFileId = -1539308780246984850; // pip.fbx renderer whose slot 0 shows the face (see Sdhari scene)
    static readonly string[] Scenes = { "Assets/Scenes/StuWindowDemo.unity", "Assets/Scenes/StuARDemo.unity" };

    [MenuItem("Stu/Character/Set Up Pip (prefab + scenes)")]
    static void SetUp()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (modelAsset == null || controller == null)
        {
            Debug.LogError($"[Stu] Need {ModelPath} and {ControllerPath} (pull main for S'dhari's Pip work).");
            return;
        }

        var prefab = BuildPrefab(modelAsset, controller);
        foreach (var scene in Scenes) UsePrefabInScene(scene, prefab);
        Debug.Log($"[Stu] Pip set up: {PrefabPath} is now Stu in {string.Join(", ", Scenes)}.");
    }

    static GameObject BuildPrefab(GameObject modelAsset, RuntimeAnimatorController controller)
    {
        var root = new GameObject("StuPip");
        var pip = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        pip.name = "Pip";
        pip.transform.SetParent(root.transform, false);

        var animator = pip.GetComponent<Animator>();
        if (animator == null) animator = pip.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Same face renderer + materials as S'dhari's Sdhari scene.
        var faceRenderer = FindFaceRenderer(pip);
        var baseFace = AssetDatabase.LoadAssetAtPath<Material>(BaseFacePath);
        if (faceRenderer != null && baseFace != null) faceRenderer.sharedMaterial = baseFace;

        // PipExpressions sits next to the Animator: PipExpressionState looks it up there.
        var expressions = pip.AddComponent<PipExpressions>();
        var so = new SerializedObject(expressions);
        so.FindProperty("faceRenderer").objectReferenceValue = faceRenderer;
        so.FindProperty("baseFace").objectReferenceValue = baseFace;
        so.FindProperty("thinkFace").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ThinkFacePath);
        so.FindProperty("cheerFace").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(CheerFacePath);
        so.ApplyModifiedPropertiesWithoutUndo();

        var character = root.AddComponent<StuCharacter>();
        character.animator = animator;
        character.expressions = expressions;
        character.model = pip.transform;

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Renderer FindFaceRenderer(GameObject pip)
    {
        var renderers = pip.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(r);
            if (source != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out _, out long id) &&
                id == FaceRendererFileId)
                return r;
        }
        if (renderers.Length > 0)
            Debug.LogWarning($"[Stu] Face renderer not matched; using {renderers[0].name}. Check PipExpressions on the prefab.");
        return renderers.Length > 0 ? renderers[0] : null;
    }

    static void UsePrefabInScene(string path, GameObject prefab)
    {
        if (!File.Exists(path)) return;
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        foreach (var demo in Object.FindObjectsByType<StuWindowDemo>(FindObjectsSortMode.None))
        {
            demo.stuPrefab = prefab;
            EditorUtility.SetDirty(demo);
        }
        foreach (var placement in Object.FindObjectsByType<StuARPlacement>(FindObjectsSortMode.None))
        {
            placement.stuPrefab = prefab;
            EditorUtility.SetDirty(placement);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}

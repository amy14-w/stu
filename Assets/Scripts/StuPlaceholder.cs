using UnityEngine;

// Builds the placeholder Stu (orange capsule + googly eyes) used by StuWindowDemo (Android) and StuARPlacement (iOS).
// The root is named "Stu (placeholder)" so StuSpeechBubble can find it.
public static class StuPlaceholder
{
    public const string Name = "Stu (placeholder)";

    // baseMaterial: any URP Lit material referenced by the scene. Copying it keeps its shader in the build;
    // without it Stu renders pink on device.
    public static Transform Create(Material baseMaterial, float height)
    {
        var root = new GameObject(Name).transform;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.GetComponent<Renderer>().sharedMaterial = MakeMat(baseMaterial, new Color(1f, 0.6f, 0.2f));

        for (int side = -1; side <= 1; side += 2)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = side < 0 ? "EyeL" : "EyeR";
            eye.transform.SetParent(root, false);
            eye.transform.localPosition = new Vector3(0.2f * side, 1.5f, 0.42f);
            eye.transform.localScale = Vector3.one * 0.25f;
            eye.GetComponent<Renderer>().sharedMaterial = MakeMat(baseMaterial, Color.white);

            var pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pupil.name = "Pupil";
            pupil.transform.SetParent(eye.transform, false);
            pupil.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            pupil.transform.localScale = Vector3.one * 0.5f;
            pupil.GetComponent<Renderer>().sharedMaterial = MakeMat(baseMaterial, Color.black);
        }

        // Colliders aren't needed and would block AR raycasts/taps.
        foreach (var c in root.GetComponentsInChildren<Collider>()) Object.Destroy(c);

        root.localScale = Vector3.one * (height / 2f);
        return root;
    }

    static Material MakeMat(Material baseMaterial, Color c)
    {
        Material m = baseMaterial != null
            ? new Material(baseMaterial)
            : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.color = c;
        return m;
    }
}

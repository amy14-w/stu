using UnityEngine;

public class PipExpressions : MonoBehaviour
{
    [Header("Face Renderer")]
    [SerializeField] private Renderer faceRenderer;

    [Header("Face Materials")]
    [SerializeField] private Material baseFace;
    [SerializeField] private Material thinkFace;
    [SerializeField] private Material cheerFace;

    public void ShowBaseFace()
    {
        faceRenderer.material = baseFace;
    }

    public void ShowThinkFace()
    {
        faceRenderer.material = thinkFace;
    }

    public void ShowCheerFace()
    {
        faceRenderer.material = cheerFace;
    }
}

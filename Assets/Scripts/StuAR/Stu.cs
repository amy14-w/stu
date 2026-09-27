using UnityEngine;

public class Stu : MonoBehaviour
{
    [SerializeField] Transform diagramAnchor;

    [SerializeField] Vector3 diagramOffset;

    public void Initialize()
    {
        diagramAnchor.parent = transform.parent;
        diagramAnchor.localScale = Vector3.one;
    }

    private void Update()
    {
        var target = Camera.main.transform.position;

        FaceTarget(target);

        diagramAnchor.position = transform.position + diagramOffset;
        diagramAnchor.rotation = Quaternion.identity;
    }

    private void FaceTarget(Vector3 target)
    {
        transform.LookAt(target);

        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
    }

    public Transform GetDiagramAnchor()
    {
        return diagramAnchor;
    }
}

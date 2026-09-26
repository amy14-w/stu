using UnityEngine;

public class Stu : MonoBehaviour
{

    private void Update()
    {
        var target = Camera.main.transform.position;

        //FaceTarget(target);
    }

    private void FaceTarget(Vector3 target)
    {
        transform.LookAt(target);

        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
    }
}

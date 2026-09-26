using System.Collections;
using UnityEngine;

public class Diagram : MonoBehaviour
{
    private DiagramData data;

    public void Despawn()
    {

    }

    private IEnumerator DespawnAnimation()
    {
        int frames = 60;

        for (int i = 1; i <= frames; i++)
        {
            float t = (float)i / frames;

            transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, t); 

            yield return new WaitForSeconds(1f / 60f);
        }

        Destroy(gameObject);
    }
}

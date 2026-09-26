using UnityEngine;

public class DiagramManager : MonoBehaviour // integrate with surface manager
{
    GameObject spawnedDiagram;

    public void TrySpawnDiagram(DiagramData data)
    {
        if (spawnedDiagram != null)
        {
            Diagram diagram;

            if (spawnedDiagram.TryGetComponent(out diagram))
            {
                diagram.Despawn();
            } else
            {
                Destroy(spawnedDiagram);
            }
        }



        spawnedDiagram = Instantiate(spawnedDiagram);
    }
}

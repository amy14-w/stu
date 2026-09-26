using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEngine;
using System.Collections.Generic;

public class DiagramManager : MonoBehaviour
{
    [SerializeField] StuSpawner stuSpawner;

    GameObject spawnedDiagram;

    public static DiagramManager Instance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        } else
        {
            Destroy(this);
        }
    }

    public async Task TrySpawnDiagram(DiagramData data)
    {
        await DespawnDiagram();

        Transform diagramAnchor = stuSpawner.GetStu().GetDiagramAnchor();

        spawnedDiagram = Instantiate(data.prefab, diagramAnchor);

        Transform surface = SurfaceManager.Instance.GetMainSurface();

        await spawnedDiagram.GetComponent<Diagram>().Spawn();
    }

    public async Task DespawnDiagram()
    {
        if (spawnedDiagram != null)
        {
            Diagram diagram;

            if (spawnedDiagram.TryGetComponent(out diagram))
            {
                await diagram.Despawn();
                spawnedDiagram = null;
            }
            else
            {
                Destroy(spawnedDiagram);
            }
        }
    }

    public async Task CompleteDiagramAction(string actionName)
    {
        await spawnedDiagram.GetComponent<Diagram>().CompleteAction(actionName);
    }

    /*public async Task ParseAndCompleteDiagramActions(string fullInput)
    {
        List<string> actionNames = new List<string>();

        // Matches literal "[A:" followed by anything captured in Group 1 until the closing "]"
        string pattern = @"\[A:(.*?)\]";

        MatchCollection matches = Regex.Matches(fullInput, pattern);

        foreach (Match match in matches)
        {
            // match.Groups[1] gets the captured ACTION NAME inside the brackets
            actionNames.Add(match.Groups[1].Value.Trim().ToUpper());
        }

        if (actionNames.Count > 0) 
        {
            await spawnedDiagram.GetComponent<Diagram>().CompleteActions(actionNames);
        }
    }*/

    public bool IsDiagramActive()
    {
        return spawnedDiagram != null;
    }
}

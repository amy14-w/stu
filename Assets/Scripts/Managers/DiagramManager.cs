using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEngine;
using System.Collections.Generic;
using Unity.Android.Gradle.Manifest;

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

    private void OnEnable()
    {
        StuConversation.Instance.OnShowDiagram += TrySpawnDiagram;
        StuConversation.Instance.OnHideDiagram += DespawnDiagram;

        StuConversation.Instance.OnPerformDiagramAction += CompleteDiagramAction;
    }

    public void TrySpawnDiagram()
    {
        var problem = ProblemsManager.Instance.GetCurrentProblem();

        if (problem != null)
        {
            var data = problem.diagram;

            DespawnDiagram();
            spawnedDiagram = null;

            Transform diagramAnchor = stuSpawner.GetStu().GetDiagramAnchor();

            spawnedDiagram = Instantiate(data.prefab, diagramAnchor);

            Transform surface = SurfaceManager.Instance.GetMainSurface();

            spawnedDiagram.GetComponent<Diagram>().Spawn();
        }
    }

    public void DespawnDiagram()
    {
        if (spawnedDiagram != null)
        {
            Diagram diagram;

            if (spawnedDiagram.TryGetComponent(out diagram))
            {
                diagram.Despawn();
                spawnedDiagram = null;
            }
            else
            {
                Destroy(spawnedDiagram);
            }
        }
    }

    public void CompleteDiagramAction(string actionName)
    {
        spawnedDiagram.GetComponent<Diagram>().CompleteAction(actionName);
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

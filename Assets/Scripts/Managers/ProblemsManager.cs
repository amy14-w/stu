using UnityEditor;
using UnityEngine;

public class ProblemsManager : MonoBehaviour
{
    [SerializeField] ProblemData[] problems;

    ProblemData currentProblem = null;

    public static ProblemsManager Instance;

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

    [ContextMenu("Start Convo")]
    public void ProblemScanDebug()
    {
        StuConversation.Instance.StartConversation(null);
    }

    private void OnEnable()
    {
        StuConversation.Instance.OnProblemNumberProvided += InitiateProblemSolving;
        StuConversation.Instance.OnConversationEnded += OnConversationOver;
    }

    private void OnDisable()
    {
        StuConversation.Instance.OnProblemNumberProvided -= InitiateProblemSolving;
        StuConversation.Instance.OnConversationEnded -= OnConversationOver;
    }


    private void OnConversationOver()
    {
        currentProblem = null;
        DiagramManager.Instance.DespawnDiagram();
    }

    private void InitiateProblemSolving(int num)
    {
        ProblemData problem = RetrieveProblem(num);

        if (problem != null) 
        {
            TryProblemChange(problem);
        }
    }


    public ProblemData RetrieveProblem(int number)
    {
        ProblemData foundProblem = null;

        foreach (ProblemData problem in problems) 
        {
            if (problem.number == number)
            {
                foundProblem = problem;
                break;
            }
        }

        if (foundProblem != null) 
        {
            return foundProblem;
        } else
        {
            Debug.LogError("Scanned key is not associated with a problem: " + number);
            return null;
        }
    }

    public void TryProblemChange(ProblemData newProblem)
    {
        if (currentProblem == null) 
        {
            currentProblem = newProblem;

            StuConversation.Instance.SetContext(currentProblem.problemText, currentProblem.diagram.description, currentProblem.answer, currentProblem.solutionSteps, currentProblem.diagram.actionTags.ToArray());
        }
    }

    public ProblemData GetCurrentProblem()
    {
        return currentProblem;
    }
}

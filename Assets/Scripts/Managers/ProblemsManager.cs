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

    public void ProblemScanDebug()
    {
        TryProblemChange(problems[0]);
    }

    public ProblemData RetrieveProblem(string key)
    {
        ProblemData foundProblem = null;

        foreach (ProblemData problem in problems) 
        {
            if (problem.key.Equals(key))
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
            Debug.LogError("Scanned key is not associated with a problem: " + key);
            return null;
        }
    }

    public void TryProblemChange(ProblemData newProblem)
    {
        if (currentProblem == null) 
        {
            currentProblem = newProblem;

            StuConversation.Instance.SetContext(currentProblem.problemText, currentProblem.diagram.description, currentProblem.answer, currentProblem.solutionSteps, currentProblem.diagram.actionTags.ToArray());

            StuConversation.Instance.StartConversation(null);
        }
    }

    public ProblemData GetCurrentProblem()
    {
        return currentProblem;
    }
}

using UnityEngine;

[CreateAssetMenu(fileName ="ProblemData", menuName ="ProblemData")]
public class ProblemData : ScriptableObject
{
    public string key;

    public string problemText;
    public string answer;

    public string solutionSteps;

    public DiagramData diagram;
}

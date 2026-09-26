using UnityEngine;

[CreateAssetMenu(fileName ="ProblemData", menuName ="DiagramData")]
public class ProblemData : ScriptableObject
{
    public string key;

    public string problemText;
    public string answer;

    public string solutionSteps;

    DiagramData diagram;
}

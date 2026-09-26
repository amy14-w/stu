using UnityEngine;

[CreateAssetMenu(fileName = "DiagramData", menuName = "DiagramData")]
public class DiagramData : ScriptableObject
{
    public GameObject prefab;

    public string description;

    public string[] actionTags;
}

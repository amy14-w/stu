using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DiagramData", menuName = "DiagramData")]
public class DiagramData : ScriptableObject
{
    public GameObject prefab;

    public string description;

    public List<string> actionTags;
}

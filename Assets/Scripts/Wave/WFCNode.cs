using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO.Enumeration;

[CreateAssetMenu(fileName = "WFCNode", menuName = "WFC/Node")]
[System.Serializable]

public class WFCNode : ScriptableObject
{

    public string Name;
    public GameObject Prefab;  //Object spawn
    public WFCConnection Top;
    public WFCConnection Bottom;
    public WFCConnection Left;
    public WFCConnection Right;




}

public class Temp_WFCConnection
{
}

[System.Serializable]
public class WFCConnection
{

    public List<WFCNode> CompatibleNodes = new List<WFCNode>();



}
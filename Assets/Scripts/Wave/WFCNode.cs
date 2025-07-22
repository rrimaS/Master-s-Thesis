using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "WFCNode", menuName = "WFC/Node")]
[System.Serializable]
public class WFCNode : ScriptableObject
{
    [Header("Node Info")]
    public string Name;
    public GameObject Prefab;  // Object to spawn
    
    [Header("Horizontal Connections (X,Z plane)")]
    public WFCConnection North;    // +Z direction (was "Top" in 2D)
    public WFCConnection South;    // -Z direction (was "Bottom" in 2D)
    public WFCConnection East;     // +X direction (was "Right" in 2D)
    public WFCConnection West;     // -X direction (was "Left" in 2D)
    
    [Header("Vertical Connections (Y axis)")]
    public WFCConnection Above;    // +Y direction (up)
    public WFCConnection Below;    // -Y direction (down)
    
    [Header("Node Properties")]
    [Tooltip("Can this node be placed at ground level?")]
    public bool canBeGroundLevel = true;
    
    [Tooltip("Can this node be placed in air/upper levels?")]
    public bool canBeAerial = true;
    
    [Tooltip("Weight for random selection (higher = more likely)")]
    [Range(0.1f, 10f)]
    public float weight = 1f;
}

[System.Serializable]
public class WFCConnection
{
    [Tooltip("Nodes that can connect to this side")]
    public List<WFCNode> CompatibleNodes = new List<WFCNode>();
    
    [Header("Connection Properties")]
    [Tooltip("Name/ID of this connection type (e.g., 'road', 'wall', 'open')")]
    public string connectionType = "default";
    
    [Tooltip("Visual representation of this connection in editor")]
    public Color debugColor = Color.white;
    
    // Helper method to check if a node is compatible
    public bool IsCompatibleWith(WFCNode node)
    {
        return CompatibleNodes.Contains(node);
    }
    
    // Helper method to add a node if not already present
    public void AddCompatibleNode(WFCNode node)
    {
        if (node != null && !CompatibleNodes.Contains(node))
        {
            CompatibleNodes.Add(node);
        }
    }
    
    // Helper method to remove a node
    public void RemoveCompatibleNode(WFCNode node)
    {
        if (CompatibleNodes.Contains(node))
        {
            CompatibleNodes.Remove(node);
        }
    }
}
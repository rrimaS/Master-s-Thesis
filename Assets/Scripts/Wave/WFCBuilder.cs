using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class NodeLimit
{
    [Tooltip("The node type to limit")]
    public WFCNode node;
    
    [Tooltip("Maximum number of this node type allowed in the scene")]
    public int maxCount = 10;
    
    [Tooltip("Description for this limit")]
    public string description = "";
}

public class WFCBuilder : MonoBehaviour
{
    [Header("Map Settings")]
    [SerializeField] private int Width = 10;
    [SerializeField] private int Height = 10;
    [SerializeField] private int Depth = 5; // New: Z-axis depth
    
    [Header("Animation Settings")]
    [SerializeField] private float spawnDelay = 0.1f;
    [SerializeField] private bool useWaveEffect = true;
    
    [Header("Positioning")]
    [SerializeField] private bool autoCalculateSpacing = true;
    [SerializeField] private Vector3 manualSpacing = Vector3.one;
    
    [Header("Generation Settings")]
    [SerializeField] private bool generate3D = true;
    [SerializeField] private bool startFromCenter = true;
    [SerializeField] private Vector3Int customStartPosition;
    
    [Header("Node Limits")]
    [SerializeField] private bool useNodeLimits = true;
    [SerializeField] private List<NodeLimit> nodeLimits = new List<NodeLimit>();
    
    [Header("Boundary Settings")]
    [SerializeField] private bool useBoundaries = true;
    [SerializeField] private WFCNode boundaryNode;
    [SerializeField] private bool boundaryOnXEdges = true;
    [SerializeField] private bool boundaryOnZEdges = true;
    [SerializeField] private bool boundaryOnYBottom = false;
    [SerializeField] private bool boundaryOnYTop = false;
    [SerializeField] private int boundaryThickness = 1;

    // 3D array with the collapsed tiles 
    private WFCNode[,,] _grid;
    private GameObject[,,] _spawnedObjects;
    
    // List of nodes 
    public List<WFCNode> Nodes = new List<WFCNode>();

    // Queue for positions that need collapsing
    private Queue<Vector3Int> _toCollapse = new Queue<Vector3Int>();
    
    // Node limit tracking
    private Dictionary<WFCNode, int> _nodeUsageCount = new Dictionary<WFCNode, int>();
    
    // Spacing between objects
    private Vector3 _actualSpacing = Vector3.one;

    // Array for checking neighbours (6 directions in 3D)
    private Vector3Int[] offsets = new Vector3Int[] {
        new Vector3Int(0, 0, 1),    // North (+Z)
        new Vector3Int(0, 0, -1),   // South (-Z)  
        new Vector3Int(1, 0, 0),    // East (+X)
        new Vector3Int(-1, 0, 0),   // West (-X)
        new Vector3Int(0, 1, 0),    // Above (+Y)
        new Vector3Int(0, -1, 0)    // Below (-Y)
    };

    private void Start()
    {
        _grid = new WFCNode[Width, Height, Depth];
        _spawnedObjects = new GameObject[Width, Height, Depth];
        
        CalculateSpacing();
        StartCoroutine(CollapseWorldCoroutine());
    }

    private void CalculateSpacing()
    {
        if (autoCalculateSpacing && Nodes.Count > 0 && Nodes[0] != null && Nodes[0].Prefab != null)
        {
            Renderer renderer = Nodes[0].Prefab.GetComponent<Renderer>();
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                _actualSpacing = new Vector3(bounds.size.x, bounds.size.y, bounds.size.z);
            }
            else
            {
                Renderer[] childRenderers = Nodes[0].Prefab.GetComponentsInChildren<Renderer>();
                if (childRenderers.Length > 0)
                {
                    Bounds combinedBounds = childRenderers[0].bounds;
                    foreach (Renderer childRenderer in childRenderers)
                    {
                        combinedBounds.Encapsulate(childRenderer.bounds);
                    }
                    _actualSpacing = new Vector3(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                }
                else
                {
                    _actualSpacing = Vector3.one;
                }
            }
        }
        else
        {
            _actualSpacing = manualSpacing;
        }
        
        Debug.Log($"Using spacing: {_actualSpacing}");
    }

    private IEnumerator CollapseWorldCoroutine()
    {
        _toCollapse.Clear();
        _nodeUsageCount.Clear(); // Reset usage tracking
        
        Vector3Int startPos;
        if (startFromCenter)
        {
            startPos = new Vector3Int(Width / 2, 0, Depth / 2); // Start at ground level in center
        }
        else
        {
            startPos = customStartPosition;
        }
        
        _toCollapse.Enqueue(startPos);

        while (_toCollapse.Count > 0)
        {
            Vector3Int currentPos = _toCollapse.Dequeue();
            int x = currentPos.x;
            int y = currentPos.y;
            int z = currentPos.z;

            // Skip if already collapsed
            if (_grid[x, y, z] != null)
            {
                continue;
            }

            // Check if this position should be forced to boundary node
            if (useBoundaries && IsBoundaryPosition(x, y, z))
            {
                if (boundaryNode != null)
                {
                    _grid[x, y, z] = boundaryNode;
                    TrackNodeUsage(boundaryNode);
                    SpawnNodeAtPosition(x, y, z);
                    
                    // Add neighbors to queue
                    AddNeighborsToQueue(currentPos);
                    
                    if (useWaveEffect && spawnDelay > 0)
                    {
                        yield return new WaitForSeconds(spawnDelay);
                    }
                    continue;
                }
            }

            // Get potential nodes based on position constraints
            List<WFCNode> potentialNodes = GetFilteredNodes(y);

            // Loop through each neighbour of this node
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3Int neighbour = currentPos + offsets[i];

                // Focus on the neighbours that are inside the grid
                if (IsInsideGrid(neighbour))
                {
                    WFCNode neighbourNode = _grid[neighbour.x, neighbour.y, neighbour.z];

                    // If the neighbour cell has been collapsed, factor it into potential nodes
                    if (neighbourNode != null)
                    {
                        switch (i)
                        {
                            case 0: // North (+Z)
                                WhittleNodes(potentialNodes, neighbourNode.South.CompatibleNodes);
                                break;
                            case 1: // South (-Z)
                                WhittleNodes(potentialNodes, neighbourNode.North.CompatibleNodes);
                                break;
                            case 2: // East (+X)
                                WhittleNodes(potentialNodes, neighbourNode.West.CompatibleNodes);
                                break;
                            case 3: // West (-X)
                                WhittleNodes(potentialNodes, neighbourNode.East.CompatibleNodes);
                                break;
                            case 4: // Above (+Y)
                                WhittleNodes(potentialNodes, neighbourNode.Below.CompatibleNodes);
                                break;
                            case 5: // Below (-Y)
                                WhittleNodes(potentialNodes, neighbourNode.Above.CompatibleNodes);
                                break;
                        }
                    }
                    // If neighbouring cell is null, add it to collapse queue
                    else
                    {
                        AddNeighborToQueue(neighbour);
                    }
                }
            }

            // Apply weighted selection for more variety
            WFCNode selectedNode = SelectWeightedNode(potentialNodes);
            
            if (selectedNode == null)
            {
                selectedNode = Nodes[0]; // Default/blank tile
                Debug.LogWarning($"No compatible nodes found for position ({x}, {y}, {z}). Using default tile.");
            }

            _grid[x, y, z] = selectedNode;

            // Track node usage for limits
            TrackNodeUsage(selectedNode);

            // Spawn the object with proper positioning
            SpawnNodeAtPosition(x, y, z);

            // Wait before processing next node for wave effect
            if (useWaveEffect && spawnDelay > 0)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }

        Debug.Log("3D World generation complete!");
        LogNodeUsageSummary();
    }

    private bool IsBoundaryPosition(int x, int y, int z)
    {
        bool isBoundary = false;
        
        // Check X boundaries (West/East edges)
        if (boundaryOnXEdges)
        {
            if (x < boundaryThickness || x >= Width - boundaryThickness)
            {
                isBoundary = true;
            }
        }
        
        // Check Z boundaries (North/South edges)
        if (boundaryOnZEdges)
        {
            if (z < boundaryThickness || z >= Depth - boundaryThickness)
            {
                isBoundary = true;
            }
        }
        
        // Check Y boundaries (Bottom/Top edges)
        if (boundaryOnYBottom)
        {
            if (y < boundaryThickness)
            {
                isBoundary = true;
            }
        }
        
        if (boundaryOnYTop)
        {
            if (y >= Height - boundaryThickness)
            {
                isBoundary = true;
            }
        }
        
        return isBoundary;
    }

    private void AddNeighborsToQueue(Vector3Int position)
    {
        for (int i = 0; i < offsets.Length; i++)
        {
            Vector3Int neighbour = position + offsets[i];
            AddNeighborToQueue(neighbour);
        }
    }

    private void AddNeighborToQueue(Vector3Int neighbour)
    {
        if (IsInsideGrid(neighbour) && !_toCollapse.Contains(neighbour) && _grid[neighbour.x, neighbour.y, neighbour.z] == null)
        {
            _toCollapse.Enqueue(neighbour);
        }
    }

    private List<WFCNode> GetFilteredNodes(int yLevel)
    {
        List<WFCNode> filteredNodes = new List<WFCNode>();
        
        foreach (WFCNode node in Nodes)
        {
            bool canPlace = false;
            
            if (yLevel == 0 && node.canBeGroundLevel) // Ground level
            {
                canPlace = true;
            }
            else if (yLevel > 0 && node.canBeAerial) // Upper levels
            {
                canPlace = true;
            }
            
            // Check node limits
            if (canPlace && useNodeLimits)
            {
                canPlace = IsNodeWithinLimit(node);
            }
            
            if (canPlace)
            {
                filteredNodes.Add(node);
            }
        }
        
        return filteredNodes.Count > 0 ? filteredNodes : new List<WFCNode>(Nodes);
    }

    private bool IsNodeWithinLimit(WFCNode node)
    {
        if (!useNodeLimits) return true;
        
        // Find limit for this node
        NodeLimit limit = nodeLimits.FirstOrDefault(nl => nl.node == node);
        if (limit == null) return true; // No limit set = unlimited
        
        // Check current usage
        int currentCount = _nodeUsageCount.ContainsKey(node) ? _nodeUsageCount[node] : 0;
        return currentCount < limit.maxCount;
    }

    private void TrackNodeUsage(WFCNode node)
    {
        if (!useNodeLimits || node == null) return;
        
        if (_nodeUsageCount.ContainsKey(node))
        {
            _nodeUsageCount[node]++;
        }
        else
        {
            _nodeUsageCount[node] = 1;
        }
    }

    private void LogNodeUsageSummary()
    {
        if (!useNodeLimits) return;
        
        Debug.Log("=== Node Usage Summary ===");
        foreach (NodeLimit limit in nodeLimits)
        {
            int used = _nodeUsageCount.ContainsKey(limit.node) ? _nodeUsageCount[limit.node] : 0;
            string status = used >= limit.maxCount ? " (LIMIT REACHED)" : "";
            Debug.Log($"{limit.node.Name}: {used}/{limit.maxCount}{status} - {limit.description}");
        }
    }

    private WFCNode SelectWeightedNode(List<WFCNode> potentialNodes)
    {
        if (potentialNodes.Count == 0) return null;
        if (potentialNodes.Count == 1) return potentialNodes[0];

        // Calculate total weight
        float totalWeight = 0f;
        foreach (WFCNode node in potentialNodes)
        {
            totalWeight += node.weight;
        }

        // Select random point in weight range
        float randomPoint = UnityEngine.Random.Range(0f, totalWeight);
        
        // Find the selected node
        float currentWeight = 0f;
        foreach (WFCNode node in potentialNodes)
        {
            currentWeight += node.weight;
            if (randomPoint <= currentWeight)
            {
                return node;
            }
        }

        // Fallback
        return potentialNodes[potentialNodes.Count - 1];
    }

    private void SpawnNodeAtPosition(int x, int y, int z)
    {
        if (_grid[x, y, z] == null || _grid[x, y, z].Prefab == null) return;

        Vector3 worldPosition = new Vector3(
            x * _actualSpacing.x, 
            y * _actualSpacing.y, 
            z * _actualSpacing.z
        );

        GameObject newNode = Instantiate(_grid[x, y, z].Prefab, worldPosition, Quaternion.identity);
        newNode.name = $"{_grid[x, y, z].Name}_({x},{y},{z})";
        newNode.transform.SetParent(this.transform);
        
        _spawnedObjects[x, y, z] = newNode;
    }

    private void WhittleNodes(List<WFCNode> potentialNodes, List<WFCNode> validNodes)
    {
        for (int i = potentialNodes.Count - 1; i >= 0; i--)
        {
            if (!validNodes.Contains(potentialNodes[i]))
            {
                potentialNodes.RemoveAt(i);
            }
        }
    }

    private bool IsInsideGrid(Vector3Int v3int)
    {
        return v3int.x >= 0 && v3int.x < Width && 
               v3int.y >= 0 && v3int.y < Height && 
               v3int.z >= 0 && v3int.z < Depth;
    }

    [ContextMenu("Regenerate World")]
    public void RegenerateWorld()
    {
        ClearWorld();
        StopAllCoroutines();
        StartCoroutine(CollapseWorldCoroutine());
    }

    [ContextMenu("Clear World")]
    public void ClearWorld()
    {
        if (_spawnedObjects != null)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int z = 0; z < Depth; z++)
                    {
                        if (_spawnedObjects[x, y, z] != null)
                        {
                            if (Application.isPlaying)
                                Destroy(_spawnedObjects[x, y, z]);
                            else
                                DestroyImmediate(_spawnedObjects[x, y, z]);
                        }
                    }
                }
            }
        }
        
        _grid = new WFCNode[Width, Height, Depth];
        _spawnedObjects = new GameObject[Width, Height, Depth];
        _nodeUsageCount.Clear(); // Reset limits tracking
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = new Vector3(
            (Width - 1) * _actualSpacing.x * 0.5f, 
            (Height - 1) * _actualSpacing.y * 0.5f, 
            (Depth - 1) * _actualSpacing.z * 0.5f
        );
        Vector3 size = new Vector3(
            Width * _actualSpacing.x, 
            Height * _actualSpacing.y, 
            Depth * _actualSpacing.z
        );
        Gizmos.DrawWireCube(center, size);
        
        // Draw start position
        Gizmos.color = Color.green;
        Vector3 startPos = startFromCenter ? 
            new Vector3(Width/2 * _actualSpacing.x, 0, Depth/2 * _actualSpacing.z) :
            new Vector3(customStartPosition.x * _actualSpacing.x, customStartPosition.y * _actualSpacing.y, customStartPosition.z * _actualSpacing.z);
        Gizmos.DrawSphere(startPos, 0.5f);
        
        // Draw boundary zones
        if (useBoundaries && boundaryNode != null)
        {
            Gizmos.color = Color.red;
            
            // X boundaries (West/East)
            if (boundaryOnXEdges)
            {
                for (int t = 0; t < boundaryThickness; t++)
                {
                    // West boundary
                    Vector3 westCenter = new Vector3(t * _actualSpacing.x, (Height-1) * _actualSpacing.y * 0.5f, (Depth-1) * _actualSpacing.z * 0.5f);
                    Vector3 westSize = new Vector3(_actualSpacing.x * 0.8f, Height * _actualSpacing.y, Depth * _actualSpacing.z);
                    Gizmos.DrawWireCube(westCenter, westSize);
                    
                    // East boundary
                    Vector3 eastCenter = new Vector3((Width-1-t) * _actualSpacing.x, (Height-1) * _actualSpacing.y * 0.5f, (Depth-1) * _actualSpacing.z * 0.5f);
                    Gizmos.DrawWireCube(eastCenter, westSize);
                }
            }
            
            // Z boundaries (North/South)
            if (boundaryOnZEdges)
            {
                for (int t = 0; t < boundaryThickness; t++)
                {
                    // North boundary
                    Vector3 northCenter = new Vector3((Width-1) * _actualSpacing.x * 0.5f, (Height-1) * _actualSpacing.y * 0.5f, (Depth-1-t) * _actualSpacing.z);
                    Vector3 northSize = new Vector3(Width * _actualSpacing.x, Height * _actualSpacing.y, _actualSpacing.z * 0.8f);
                    Gizmos.DrawWireCube(northCenter, northSize);
                    
                    // South boundary
                    Vector3 southCenter = new Vector3((Width-1) * _actualSpacing.x * 0.5f, (Height-1) * _actualSpacing.y * 0.5f, t * _actualSpacing.z);
                    Gizmos.DrawWireCube(southCenter, northSize);
                }
            }
        }
    }
}
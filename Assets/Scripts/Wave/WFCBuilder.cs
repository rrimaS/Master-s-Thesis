using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class WFCBuilder : MonoBehaviour
{
    [Header("Map Settings")]
    [SerializeField] private int Width = 10;
    [SerializeField] private int Height = 10;
    
    [Header("Animation Settings")]
    [SerializeField] private float spawnDelay = 0.1f;
    [SerializeField] private bool useWaveEffect = true;
    
    [Header("Positioning")]
    [SerializeField] private bool autoCalculateSpacing = true;
    [SerializeField] private Vector3 manualSpacing = Vector3.one;
    [SerializeField] private bool use3D = true; // Use X,Z instead of X,Y

    // 2D array with the collapsed tiles 
    private WFCNode[,] _grid;
    private GameObject[,] _spawnedObjects; // Track spawned objects
    
    // List of nodes 
    public List<WFCNode> Nodes = new List<WFCNode>();

    // Queue for positions that need collapsing (changed from List to Queue for wave effect)
    private Queue<Vector2Int> _toCollapse = new Queue<Vector2Int>();
    
    // Spacing between objects
    private Vector3 _actualSpacing = Vector3.one;

    // Array for checking neighbours 
    private Vector2Int[] offsets = new Vector2Int[] {
        new Vector2Int(0, 1),       //top
        new Vector2Int(0, -1),      //bottom
        new Vector2Int(1, 0),       //right
        new Vector2Int(-1, 0)       //left
    };

    private void Start()
    {
        _grid = new WFCNode[Width, Height];
        _spawnedObjects = new GameObject[Width, Height];
        
        CalculateSpacing();
        StartCoroutine(CollapseWorldCoroutine());
    }

    private void CalculateSpacing()
    {
        if (autoCalculateSpacing && Nodes.Count > 0 && Nodes[0] != null && Nodes[0].Prefab != null)
        {
            // Get the bounds of the first prefab to calculate spacing
            Renderer renderer = Nodes[0].Prefab.GetComponent<Renderer>();
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                _actualSpacing = new Vector3(bounds.size.x, bounds.size.y, bounds.size.z);
            }
            else
            {
                // Fallback: try to get bounds from child renderers
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
        _toCollapse.Enqueue(new Vector2Int(Width / 2, Height / 2));

        while (_toCollapse.Count > 0)
        {
            Vector2Int currentPos = _toCollapse.Dequeue();
            int x = currentPos.x;
            int y = currentPos.y;

            // Skip if already collapsed
            if (_grid[x, y] != null)
            {
                continue;
            }

            // Potential nodes include every possible node
            List<WFCNode> potentialNodes = new List<WFCNode>(Nodes);

            // Loop through each neighbour of this node
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2Int neighbour = new Vector2Int(x + offsets[i].x, y + offsets[i].y);

                // Focus on the neighbours that are inside the grid
                if (IsInsideGrid(neighbour))
                {
                    WFCNode neighbourNode = _grid[neighbour.x, neighbour.y];

                    // If the neighbour cell has been collapsed, factor it into potential nodes
                    if (neighbourNode != null)
                    {
                        switch (i)
                        {
                            case 0: // top
                                WhittleNodes(potentialNodes, neighbourNode.Bottom.CompatibleNodes);
                                break;
                            case 1: // bottom
                                WhittleNodes(potentialNodes, neighbourNode.Top.CompatibleNodes);
                                break;
                            case 2: // right
                                WhittleNodes(potentialNodes, neighbourNode.Left.CompatibleNodes);
                                break;
                            case 3: // left
                                WhittleNodes(potentialNodes, neighbourNode.Right.CompatibleNodes);
                                break;
                        }
                    }
                    // If neighbouring cell is null, add it to collapse queue
                    else
                    {
                        if (!_toCollapse.Contains(neighbour))
                        {
                            _toCollapse.Enqueue(neighbour);
                        }
                    }
                }
            }

            // Collapse this wave
            if (potentialNodes.Count < 1)
            {
                _grid[x, y] = Nodes[0]; // Default/blank tile
                Debug.LogWarning($"No compatible nodes found for position ({x}, {y}). Using default tile.");
            }
            else
            {
                _grid[x, y] = potentialNodes[UnityEngine.Random.Range(0, potentialNodes.Count)];
            }

            // Spawn the object with proper positioning
            SpawnNodeAtPosition(x, y);

            // Wait before processing next node for wave effect
            if (useWaveEffect && spawnDelay > 0)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }

        Debug.Log("World generation complete!");
    }

    private void SpawnNodeAtPosition(int x, int y)
    {
        if (_grid[x, y] == null || _grid[x, y].Prefab == null) return;

        Vector3 worldPosition;
        
        if (use3D)
        {
            // Use X,Z positioning for 3D world (Y stays at 0 or can be adjusted)
            worldPosition = new Vector3(
                x * _actualSpacing.x, 
                0f, // You can adjust this or make it configurable
                y * _actualSpacing.z
            );
        }
        else
        {
            // Traditional 2D X,Y positioning
            worldPosition = new Vector3(
                x * _actualSpacing.x, 
                y * _actualSpacing.y, 
                0f
            );
        }

        GameObject newNode = Instantiate(_grid[x, y].Prefab, worldPosition, Quaternion.identity);
        newNode.name = $"{_grid[x, y].Name}_({x},{y})";
        newNode.transform.SetParent(this.transform); // Organize under this object
        
        _spawnedObjects[x, y] = newNode;
    }

    // Compares a list of potential nodes to a list of valid nodes and removes all non-valid nodes
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

    private bool IsInsideGrid(Vector2Int v2int)
    {
        return v2int.x >= 0 && v2int.x < Width && v2int.y >= 0 && v2int.y < Height;
    }

    // Utility methods for runtime control
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
                    if (_spawnedObjects[x, y] != null)
                    {
                        if (Application.isPlaying)
                            Destroy(_spawnedObjects[x, y]);
                        else
                            DestroyImmediate(_spawnedObjects[x, y]);
                    }
                }
            }
        }
        
        _grid = new WFCNode[Width, Height];
        _spawnedObjects = new GameObject[Width, Height];
    }

    // Debug visualization in Scene view
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = use3D ? new Vector3((Width-1) * _actualSpacing.x * 0.5f, 0, (Height-1) * _actualSpacing.z * 0.5f)
                              : new Vector3((Width-1) * _actualSpacing.x * 0.5f, (Height-1) * _actualSpacing.y * 0.5f, 0);
        Vector3 size = use3D ? new Vector3(Width * _actualSpacing.x, 0.1f, Height * _actualSpacing.z)
                            : new Vector3(Width * _actualSpacing.x, Height * _actualSpacing.y, 0.1f);
        Gizmos.DrawWireCube(center, size);
    }
}
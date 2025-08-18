using UnityEngine;
using System.Collections.Generic;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class DLATerrainSettings
{
    [Header("DLA Parameters")]
    public int mapSize = 512;
    public int maxParticles = 5000;
    public int maxWalkSteps = 10000;
    public float stickiness = 1.0f;
    public int seedCount = 5;

    [Header("Seed Placement")]
    public bool centerStart = true;
    public int clusterRadius = 2; // Override automatic cluster radius calculation

    [Header("Terrain Processing")]
    public int smoothingPasses = 2;
    public float[] smoothingRadii = { 4f, 16f };
    public float[] smoothingWeights = { 0.8f, 0.4f };
    public float maxBlurDistance = 80f; // Maximum distance for height falloff
    public float falloffPower = 3.0f; // Controls how steep the falloff is (1.0 = linear, 2.0 = quadratic, 3.0+ = steep)
    public float heightMultiplier = 0.6f; // Overall height scaling (0.0-1.0)
    public float minimumHeight = 0.05f; // Minimum height at edges (0.0-0.2)
    
    [Header("Advanced Height Control")]
    public bool useCustomCurve = false;
    public AnimationCurve heightFalloffCurve; // Custom falloff curve

    [Header("Unity Terrain")]
    public float terrainHeight = 100f;
    public float terrainWidth = 500f;
    public float terrainLength = 500f;
    public Material terrainMaterial;

    [Header("Export Settings")]
    public bool autoExportHeightmap = true;
    public string heightmapFileName = "DLA_Heightmap";
    public enum HeightmapFormat { PNG, RAW }
    public HeightmapFormat exportFormat = HeightmapFormat.PNG;
}

public class DLATerrainGenerator : MonoBehaviour
{
    [SerializeField] private DLATerrainSettings settings = new DLATerrainSettings();
    [SerializeField] private bool generateOnStart = false;
    [SerializeField] private bool showDebugTexture = true;

    private Terrain terrain;
    private TerrainData terrainData;
    private float[,] heightMap;
    private bool[,] occupied;
    private List<Vector2Int> seeds;
    private List<Vector2Int> occupiedPositions;
    private Texture2D debugTexture;
    private bool isGenerating = false;
    
    // Distance-based height calculation fields
    private float maxDistanceFromSeed = 0f;
    private Vector2Int primarySeed;
    
    // Progress tracking variables
    private float generationProgress = 0f;
    private string currentPhase = "";
    private System.DateTime startTime;
    private int currentParticles = 0;
    private int currentSmoothingPass = 0;

    // Predefined move offsets for faster particle movement
    private static readonly Vector2Int[] moveOffsets = {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(-1, -1),
        new Vector2Int(1, -1), new Vector2Int(-1, 1)
    };

    void Start()
    {
        if (generateOnStart)
        {
            GenerateTerrain();
        }
    }

    public void GenerateTerrain()
    {
        if (!isGenerating)
        {
            StartCoroutine(GenerateTerrainCoroutine());
        }
        else
        {
            Debug.LogWarning("Terrain generation is already in progress!");
        }
    }

    private IEnumerator GenerateTerrainCoroutine()
    {
        isGenerating = true;
        startTime = System.DateTime.Now;
        generationProgress = 0f;
        
        // Phase 1: DLA Generation (70% of progress)
        currentPhase = "Generating DLA Pattern";
        Debug.Log("Starting DLA Terrain Generation...");
        yield return StartCoroutine(GenerateDLAPattern());

        // Phase 2: Processing (20% of progress)
        currentPhase = "Processing Heightmap";
        Debug.Log("Processing heightmap...");
        yield return StartCoroutine(ProcessHeightMap());

        // Phase 3: Terrain Creation (10% of progress)
        currentPhase = "Creating Unity Terrain";
        generationProgress = 0.9f;
        Debug.Log("Creating Unity terrain...");
        CreateUnityTerrain();

        if (showDebugTexture || settings.autoExportHeightmap)
        {
            CreateDebugTexture();
        }

        if (settings.autoExportHeightmap)
        {
            ExportHeightmap();
        }

        generationProgress = 1f;
        currentPhase = "Complete";
        
        System.TimeSpan totalTime = System.DateTime.Now - startTime;
        Debug.Log("DLA Terrain generation complete! Total time: " + totalTime.Minutes + "m " + totalTime.Seconds + "s");
        
        isGenerating = false;
    }

    private IEnumerator GenerateDLAPattern()
    {
        // Initialize arrays
        heightMap = new float[settings.mapSize, settings.mapSize];
        occupied = new bool[settings.mapSize, settings.mapSize];
        seeds = new List<Vector2Int>();
        occupiedPositions = new List<Vector2Int>();

        // Calculate maximum possible distance for normalization
        maxDistanceFromSeed = Vector2.Distance(Vector2.zero, new Vector2(settings.mapSize, settings.mapSize));

        if (settings.centerStart)
        {
            // Calculate center position
            Vector2Int center = new Vector2Int(settings.mapSize / 2, settings.mapSize / 2);
            primarySeed = center; // Store the primary seed
            
            // Option 1: Single seed at exact center
            if (settings.seedCount == 1)
            {
                seeds.Add(center);
                occupied[center.x, center.y] = true;
                heightMap[center.x, center.y] = 1.0f;
                occupiedPositions.Add(center);
            }
            else
            {
                // Option 2: Multiple seeds clustered around center
                // First seed at exact center
                seeds.Add(center);
                occupied[center.x, center.y] = true;
                heightMap[center.x, center.y] = 1.0f;
                occupiedPositions.Add(center);
                
                // Additional seeds in a small cluster around center
                int maxClusterRadius = settings.clusterRadius > 0 ? settings.clusterRadius : Mathf.Max(2, settings.mapSize / 64);
                
                for (int i = 1; i < settings.seedCount; i++)
                {
                    Vector2Int seed;
                    int attempts = 0;
                    
                    do
                    {
                        // Generate random offset within cluster radius
                        int offsetX = Random.Range(-maxClusterRadius, maxClusterRadius + 1);
                        int offsetY = Random.Range(-maxClusterRadius, maxClusterRadius + 1);
                        
                        seed = new Vector2Int(center.x + offsetX, center.y + offsetY);
                        attempts++;
                        
                        // Fallback to center if we can't find a valid position after many attempts
                        if (attempts > 50)
                        {
                            seed = center;
                            break;
                        }
                    }
                    while (!IsValidPosition(seed) || occupied[seed.x, seed.y]);
                    
                    seeds.Add(seed);
                    occupied[seed.x, seed.y] = true;
                    
                    // Just mark as occupied, height will be calculated later in post-processing
                    heightMap[seed.x, seed.y] = 1.0f;
                    occupiedPositions.Add(seed);
                }
            }
        }
        else
        {
            // Original random seed placement
            for (int i = 0; i < settings.seedCount; i++)
            {
                Vector2Int seed = new Vector2Int(
                    Random.Range(settings.mapSize / 4, 3 * settings.mapSize / 4),
                    Random.Range(settings.mapSize / 4, 3 * settings.mapSize / 4)
                );
                seeds.Add(seed);
                occupied[seed.x, seed.y] = true;
                
                if (i == 0)
                {
                    primarySeed = seed; // First seed is primary
                    heightMap[seed.x, seed.y] = 1.0f;
                }
                else
                {
                    // Just mark as occupied, height will be calculated later in post-processing
                    heightMap[seed.x, seed.y] = 1.0f;
                }
                
                occupiedPositions.Add(seed);
            }
        }

        int particlesGenerated = 0;
        int frameCounter = 0;
        currentParticles = 0;

        // Generate DLA pattern
        while (particlesGenerated < settings.maxParticles)
        {
            if (RunRandomWalk())
            {
                particlesGenerated++;
                currentParticles = particlesGenerated;
                
                // Update progress (DLA generation is 70% of total progress)
                generationProgress = (float)particlesGenerated / settings.maxParticles * 0.7f;
            }

            // Yield less frequently for better performance
            frameCounter++;
            if (frameCounter % 200 == 0)
            {
                yield return null;
            }
        }
    }

    // Simplified DLA generation - we only care about the pattern, not the heights during generation
    private bool RunRandomWalk()
    {
        // Start particle at random position on edge
        Vector2Int particle = GetRandomEdgePosition();

        for (int step = 0; step < settings.maxWalkSteps; step++)
        {
            // Check if particle is adjacent to occupied cell
            if (IsAdjacentToOccupied(particle))
            {
                if (Random.value <= settings.stickiness)
                {
                    // Stick the particle
                    if (IsValidPosition(particle))
                    {
                        occupied[particle.x, particle.y] = true;
                        heightMap[particle.x, particle.y] = 1.0f; // Just mark as occupied, height will be calculated later
                        occupiedPositions.Add(particle);

                        // Simple line drawing without height interpolation (we'll calculate heights later)
                        Vector2Int nearest = FindNearestOccupied(particle);
                        if (nearest != Vector2Int.zero)
                        {
                            DrawSimpleLine(particle, nearest);
                        }

                        return true;
                    }
                }
            }

            // Move particle randomly using predefined offsets (faster)
            particle = particle + moveOffsets[Random.Range(0, moveOffsets.Length)];

            // Check if particle has moved out of bounds or too far
            if (!IsValidPosition(particle) || IsOutOfWalkRadius(particle))
            {
                break;
            }
        }

        return false;
    }

    // Simplified line drawing - just mark the path, don't worry about heights
    private void DrawSimpleLine(Vector2Int start, Vector2Int end)
    {
        Vector2 current = start;
        Vector2 target = end;
        float distance = Vector2.Distance(start, end);
        
        if (distance <= 1) return; // Skip very short lines

        for (float t = 0; t <= 1; t += 1f / distance)
        {
            Vector2 point = Vector2.Lerp(current, target, t);

            // Add small random displacement
            point += new Vector2(
                Random.Range(-0.2f, 0.2f),
                Random.Range(-0.2f, 0.2f)
            );

            Vector2Int intPoint = new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));

            if (IsValidPosition(intPoint))
            {
                heightMap[intPoint.x, intPoint.y] = 1.0f; // Just mark as part of the structure
            }
        }
    }

    private Vector2Int GetRandomEdgePosition()
    {
        int side = Random.Range(0, 4);
        switch (side)
        {
            case 0: return new Vector2Int(Random.Range(0, settings.mapSize), 0); // Top
            case 1: return new Vector2Int(Random.Range(0, settings.mapSize), settings.mapSize - 1); // Bottom
            case 2: return new Vector2Int(0, Random.Range(0, settings.mapSize)); // Left
            default: return new Vector2Int(settings.mapSize - 1, Random.Range(0, settings.mapSize)); // Right
        }
    }

    private bool IsAdjacentToOccupied(Vector2Int pos)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                int x = pos.x + dx;
                int y = pos.y + dy;

                if (IsValidPosition(new Vector2Int(x, y)) && occupied[x, y])
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool IsValidPosition(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < settings.mapSize && pos.y >= 0 && pos.y < settings.mapSize;
    }

    private bool IsOutOfWalkRadius(Vector2Int pos)
    {
        Vector2 center = new Vector2(settings.mapSize / 2f, settings.mapSize / 2f);
        float distanceFromCenter = Vector2.Distance(pos, center);
        return distanceFromCenter > settings.mapSize * 0.6f;
    }

    private Vector2Int FindNearestOccupied(Vector2Int pos)
    {
        float minDistance = float.MaxValue;
        Vector2Int nearest = Vector2Int.zero;

        // Use the fast lookup list instead of scanning entire grid
        foreach (var occupiedPos in occupiedPositions)
        {
            float distance = (pos - occupiedPos).sqrMagnitude; // Use sqrMagnitude for faster computation
            if (distance < minDistance && distance > 0)
            {
                minDistance = distance;
                nearest = occupiedPos;
            }
        }

        return nearest;
    }

    private IEnumerator ProcessHeightMap()
    {
        // STEP 1: Convert DLA pattern to distance field for smooth terrain
        currentPhase = "Creating Distance Field";
        yield return StartCoroutine(CreateDistanceFieldHeightmap());
        
        // STEP 2: Apply additional smoothing if needed
        if (settings.smoothingPasses > 0)
        {
            currentPhase = "Final Smoothing";
            yield return StartCoroutine(ApplyFinalSmoothing());
        }
        
        // STEP 3: Normalize final heightmap
        NormalizeHeightMap();
    }

    private IEnumerator CreateDistanceFieldHeightmap()
    {
        // Store the original DLA pattern (only pixels that were actually generated)
        List<Vector2Int> dlaPoints = new List<Vector2Int>();
        
        // Collect all DLA structure points
        for (int x = 0; x < settings.mapSize; x++)
        {
            for (int y = 0; y < settings.mapSize; y++)
            {
                if (heightMap[x, y] > 0.05f) // Any pixel that was touched by DLA
                {
                    dlaPoints.Add(new Vector2Int(x, y));
                }
            }
        }
        
        // Create completely new heightmap based on distance from DLA points
        float[,] newHeightMap = new float[settings.mapSize, settings.mapSize];
        int processedPixels = 0;
        int totalPixels = settings.mapSize * settings.mapSize;
        
        for (int x = 0; x < settings.mapSize; x++)
        {
            for (int y = 0; y < settings.mapSize; y++)
            {
                // Find distance to nearest DLA point
                float minDistance = float.MaxValue;
                
                foreach (Vector2Int dlaPoint in dlaPoints)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(dlaPoint.x, dlaPoint.y));
                    minDistance = Mathf.Min(minDistance, distance);
                }
                
                // Convert distance to height using smooth falloff
                float height = CalculateSmoothHeight(minDistance);
                newHeightMap[x, y] = height;
                
                processedPixels++;
                
                // Update progress and yield occasionally
                if (processedPixels % 1000 == 0)
                {
                    generationProgress = 0.7f + (float)processedPixels / totalPixels * 0.15f;
                    yield return null;
                }
            }
        }
        
        heightMap = newHeightMap;
    }

    private float CalculateSmoothHeight(float distance)
    {
        if (distance <= 0) return 1.0f; // On the DLA structure itself
        
        // Normalize distance (0 = on structure, 1 = maximum distance)
        float normalizedDistance = Mathf.Clamp01(distance / settings.maxBlurDistance);
        
        // Apply falloff curve
        float height;
        if (settings.useCustomCurve && settings.heightFalloffCurve != null && settings.heightFalloffCurve.keys.Length > 0)
        {
            // Use custom animation curve if available and enabled
            height = settings.heightFalloffCurve.Evaluate(normalizedDistance);
        }
        else
        {
            // Enhanced mathematical falloff with multiple options
            
            // Option 1: Power-based falloff (more dramatic)
            float powerFalloff = Mathf.Pow(1f - normalizedDistance, settings.falloffPower);
            
            // Option 2: Exponential falloff (very dramatic fade)
            float expFalloff = Mathf.Exp(-normalizedDistance * 5f);
            
            // Option 3: Smooth step falloff (natural looking)
            float smoothStepFalloff = Mathf.SmoothStep(1f, 0f, normalizedDistance);
            
            // Combine for best results - using exponential for dramatic fade
            height = expFalloff * powerFalloff;
            
            // Apply height multiplier to reduce overall brightness
            height *= settings.heightMultiplier;
            
            // Ensure minimum height at edges
            height = Mathf.Max(height, settings.minimumHeight * (1f - normalizedDistance));
        }
        
        return Mathf.Clamp01(height);
    }

    private IEnumerator ApplyFinalSmoothing()
    {
        float[,] finalHeightMap = new float[settings.mapSize, settings.mapSize];

        for (int pass = 0; pass < settings.smoothingPasses; pass++)
        {
            currentSmoothingPass = pass + 1;
            
            // Update progress (final smoothing is 5% of total, from 85% to 90%)
            generationProgress = 0.85f + (float)pass / settings.smoothingPasses * 0.05f;
            
            float[,] smoothedMap = ApplyGaussianSmoothing(heightMap, settings.smoothingRadii[pass]);
            float weight = settings.smoothingWeights[pass];

            // Add weighted smoothed map to final result
            for (int x = 0; x < settings.mapSize; x++)
            {
                for (int y = 0; y < settings.mapSize; y++)
                {
                    finalHeightMap[x, y] += smoothedMap[x, y] * weight;
                }
            }

            yield return null;
        }

        heightMap = finalHeightMap;
    }
    
    private void NormalizeHeightMap()
    {
        // Find max height
        float maxHeight = 0f;
        for (int x = 0; x < settings.mapSize; x++)
        {
            for (int y = 0; y < settings.mapSize; y++)
            {
                maxHeight = Mathf.Max(maxHeight, heightMap[x, y]);
            }
        }

        // Apply contrast enhancement to make the fade more dramatic
        for (int x = 0; x < settings.mapSize; x++)
        {
            for (int y = 0; y < settings.mapSize; y++)
            {
                if (maxHeight > 0)
                {
                    // Normalize first
                    float normalizedHeight = heightMap[x, y] / maxHeight;
                    
                    // Apply contrast curve to enhance the fade effect
                    // This makes bright areas brighter and dark areas darker
                    float contrastedHeight = Mathf.Pow(normalizedHeight, 1.5f);
                    
                    // Apply additional fade based on distance from map center
                    Vector2 center = new Vector2(settings.mapSize / 2f, settings.mapSize / 2f);
                    Vector2 currentPos = new Vector2(x, y);
                    float distanceFromCenter = Vector2.Distance(currentPos, center);
                    float maxDistanceFromCenter = Vector2.Distance(Vector2.zero, center);
                    float centerFade = 1f - Mathf.Pow(distanceFromCenter / maxDistanceFromCenter, 2f);
                    
                    // Combine height with center fade
                    heightMap[x, y] = contrastedHeight * Mathf.Lerp(0.1f, 1f, centerFade);
                }
            }
        }
    }

    private float[,] ApplyGaussianSmoothing(float[,] input, float radius)
    {
        int size = settings.mapSize;
        float[,] temp = new float[size, size];
        float[,] output = new float[size, size];
        int kernelRadius = Mathf.CeilToInt(radius * 3);
        
        // Pre-compute 1D Gaussian kernel
        float[] kernel = new float[kernelRadius * 2 + 1];
        float sigma2 = radius * radius;
        float sum = 0f;
        
        for (int i = -kernelRadius; i <= kernelRadius; i++)
        {
            float weight = Mathf.Exp(-(i * i) / (2f * sigma2));
            kernel[i + kernelRadius] = weight;
            sum += weight;
        }
        
        // Normalize kernel
        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        // Separable Gaussian blur: Horizontal pass
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = 0f;
                for (int k = -kernelRadius; k <= kernelRadius; k++)
                {
                    int sampleX = Mathf.Clamp(x + k, 0, size - 1);
                    value += input[sampleX, y] * kernel[k + kernelRadius];
                }
                temp[x, y] = value;
            }
        }

        // Separable Gaussian blur: Vertical pass
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float value = 0f;
                for (int k = -kernelRadius; k <= kernelRadius; k++)
                {
                    int sampleY = Mathf.Clamp(y + k, 0, size - 1);
                    value += temp[x, sampleY] * kernel[k + kernelRadius];
                }
                output[x, y] = value;
            }
        }

        return output;
    }

    private void CreateUnityTerrain()
    {
        // Get or create terrain component
        terrain = GetComponent<Terrain>();
        if (terrain == null)
        {
            terrain = gameObject.AddComponent<Terrain>();
        }

        // Create terrain data using the correct method
        terrainData = new TerrainData();
        terrainData.heightmapResolution = settings.mapSize + 1;
        terrainData.size = new Vector3(settings.terrainWidth, settings.terrainHeight, settings.terrainLength);

        // Convert heightmap to Unity format (flipped and with border)
        float[,] unityHeights = new float[settings.mapSize + 1, settings.mapSize + 1];
        for (int x = 0; x < settings.mapSize + 1; x++)
        {
            for (int y = 0; y < settings.mapSize + 1; y++)
            {
                int sourceX = Mathf.Clamp(x, 0, settings.mapSize - 1);
                int sourceY = Mathf.Clamp(y, 0, settings.mapSize - 1);
                unityHeights[y, x] = heightMap[sourceX, sourceY]; // Note: Unity uses [y,x] format
            }
        }

        terrainData.SetHeights(0, 0, unityHeights);

        // Apply material if provided
        if (settings.terrainMaterial != null)
        {
            terrain.materialTemplate = settings.terrainMaterial;
        }

        terrain.terrainData = terrainData;

        // Add terrain collider
        TerrainCollider collider = GetComponent<TerrainCollider>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<TerrainCollider>();
        }
        collider.terrainData = terrainData;
    }

    public void ExportHeightmap()
    {
        if (heightMap == null)
        {
            Debug.LogError("No heightmap to export! Generate terrain first.");
            return;
        }

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = settings.heightmapFileName + "_" + timestamp;

        switch (settings.exportFormat)
        {
            case DLATerrainSettings.HeightmapFormat.PNG:
                ExportHeightmapAsPNG(fileName);
                break;
            case DLATerrainSettings.HeightmapFormat.RAW:
                ExportHeightmapAsRAW(fileName);
                break;
        }
    }

    private void ExportHeightmapAsPNG(string fileName)
    {
        Texture2D exportTexture = new Texture2D(settings.mapSize, settings.mapSize, TextureFormat.RGB24, false);

        for (int x = 0; x < settings.mapSize; x++)
        {
            for (int y = 0; y < settings.mapSize; y++)
            {
                float height = heightMap[x, y];
                Color color = new Color(height, height, height, 1f);
                exportTexture.SetPixel(x, y, color);
            }
        }

        exportTexture.Apply();

        byte[] pngData = exportTexture.EncodeToPNG();
        
        #if UNITY_EDITOR
        string path = Application.dataPath + "/" + fileName + ".png";
        System.IO.File.WriteAllBytes(path, pngData);
        AssetDatabase.Refresh();
        Debug.Log("Heightmap exported as PNG: " + path);
        #else
        string path = Application.persistentDataPath + "/" + fileName + ".png";
        System.IO.File.WriteAllBytes(path, pngData);
        Debug.Log("Heightmap exported as PNG: " + path);
        #endif

        DestroyImmediate(exportTexture);
    }

    private void ExportHeightmapAsRAW(string fileName)
    {
        // Export as 16-bit RAW format (compatible with most terrain tools)
        byte[] rawData = new byte[settings.mapSize * settings.mapSize * 2];
        int index = 0;

        for (int y = settings.mapSize - 1; y >= 0; y--) // Flip Y for standard RAW format
        {
            for (int x = 0; x < settings.mapSize; x++)
            {
                // Convert float height (0-1) to 16-bit value (0-65535)
                ushort heightValue = (ushort)(heightMap[x, y] * 65535f);
                
                // Write as little-endian 16-bit
                rawData[index++] = (byte)(heightValue & 0xFF);
                rawData[index++] = (byte)((heightValue >> 8) & 0xFF);
            }
        }

        #if UNITY_EDITOR
        string path = Application.dataPath + "/" + fileName + ".raw";
        System.IO.File.WriteAllBytes(path, rawData);
        AssetDatabase.Refresh();
        Debug.Log("Heightmap exported as RAW: " + path);
        Debug.Log("RAW Format: " + settings.mapSize + "x" + settings.mapSize + ", 16-bit, Little-Endian");
        #else
        string path = Application.persistentDataPath + "/" + fileName + ".raw";
        System.IO.File.WriteAllBytes(path, rawData);
        Debug.Log("Heightmap exported as RAW: " + path);
        Debug.Log("RAW Format: " + settings.mapSize + "x" + settings.mapSize + ", 16-bit, Little-Endian");
        #endif
    }

    private void CreateDebugTexture()
    {
        if (debugTexture != null)
        {
            DestroyImmediate(debugTexture);
        }
        
        debugTexture = new Texture2D(settings.mapSize, settings.mapSize);

        for (int x = 0; x < settings.mapSize; x++)
        {
            for (int y = 0; y < settings.mapSize; y++)
            {
                float height = heightMap[x, y];
                Color color = new Color(height, height, height, 1f);
                debugTexture.SetPixel(x, y, color);
            }
        }

        debugTexture.Apply();
    }

    void OnGUI()
    {
        if (showDebugTexture && debugTexture != null)
        {
            GUI.DrawTexture(new Rect(10, 10, 200, 200), debugTexture);
            GUI.Label(new Rect(10, 220, 200, 20), "DLA Heightmap Preview");
        }

        if (isGenerating)
        {
            DrawProgressBar();
        }
    }
    
    private void DrawProgressBar()
    {
        // Background box
        GUI.Box(new Rect(10, 250, 300, 80), "");
        
        // Title
        GUI.Label(new Rect(20, 260, 280, 20), "Generating DLA Terrain...", EditorStyles.boldLabel);
        
        // Current phase
        GUI.Label(new Rect(20, 280, 280, 20), "Phase: " + currentPhase);
        
        // Progress bar background
        GUI.Box(new Rect(20, 300, 260, 20), "");
        
        // Progress bar fill
        GUI.Box(new Rect(20, 300, 260 * generationProgress, 20), "", GUI.skin.button);
        
        // Progress percentage
        string progressText = Mathf.RoundToInt(generationProgress * 100) + "%";
        GUI.Label(new Rect(280, 300, 40, 20), progressText);
        
        // Detailed progress info
        if (currentPhase == "Generating DLA Pattern")
        {
            GUI.Label(new Rect(20, 320, 280, 20), "Particles: " + currentParticles + " / " + settings.maxParticles);
        }
        else if (currentPhase == "Processing Heightmap")
        {
            GUI.Label(new Rect(20, 320, 280, 20), "Smoothing Pass: " + currentSmoothingPass + " / " + settings.smoothingPasses);
        }
        
        // Time elapsed
        if (startTime != default(System.DateTime))
        {
            System.TimeSpan elapsed = System.DateTime.Now - startTime;
            string timeText = elapsed.Minutes + "m " + elapsed.Seconds + "s";
            GUI.Label(new Rect(200, 260, 100, 20), "Elapsed: " + timeText);
            
            // Time estimation
            if (generationProgress > 0.05f) // Only show estimate after 5% progress
            {
                double totalEstimatedSeconds = elapsed.TotalSeconds / generationProgress;
                double remainingSeconds = totalEstimatedSeconds - elapsed.TotalSeconds;
                
                if (remainingSeconds > 0)
                {
                    int remainingMinutes = (int)(remainingSeconds / 60);
                    int remainingSecondsOnly = (int)(remainingSeconds % 60);
                    string estimatedText = remainingMinutes + "m " + remainingSecondsOnly + "s remaining";
                    GUI.Label(new Rect(20, 340, 280, 20), estimatedText);
                }
            }
        }
    }

    // Public methods for the custom editor
    public bool IsGenerating()
    {
        return isGenerating;
    }

    public float[,] GetHeightMap()
    {
        return heightMap;
    }

    public int GetMapSize()
    {
        return settings.mapSize;
    }

    public bool HasTerrain()
    {
        return terrain != null && terrainData != null;
    }
    
    public float GetGenerationProgress()
    {
        return generationProgress;
    }
    
    public string GetCurrentPhase()
    {
        return currentPhase;
    }
    
    public string GetProgressDetails()
    {
        if (currentPhase == "Generating DLA Pattern")
        {
            return "Particles: " + currentParticles + " / " + settings.maxParticles;
        }
        else if (currentPhase == "Processing Heightmap")
        {
            return "Smoothing Pass: " + currentSmoothingPass + " / " + settings.smoothingPasses;
        }
        return "";
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(DLATerrainGenerator))]
public class DLATerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        DLATerrainGenerator generator = (DLATerrainGenerator)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Terrain Generation", EditorStyles.boldLabel);

        GUI.enabled = !generator.IsGenerating();
        if (GUILayout.Button("Generate DLA Terrain", GUILayout.Height(30)))
        {
            generator.GenerateTerrain();
        }
        GUI.enabled = true;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);

        if (GUILayout.Button("Export Heightmap"))
        {
            generator.ExportHeightmap();
        }

        if (generator.IsGenerating())
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Terrain generation in progress...", MessageType.Info);
            
            // Progress bar in inspector
            float progress = generator.GetGenerationProgress();
            EditorGUILayout.LabelField("Phase: " + generator.GetCurrentPhase());
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, 
                Mathf.RoundToInt(progress * 100) + "%");
            
            string details = generator.GetProgressDetails();
            if (!string.IsNullOrEmpty(details))
            {
                EditorGUILayout.LabelField(details);
            }
            
            // Force repaint to update progress
            Repaint();
        }

        // Show terrain info if available
        if (generator.GetHeightMap() != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Current Terrain Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Map Size: " + generator.GetMapSize() + "x" + generator.GetMapSize());
            EditorGUILayout.LabelField("Has Terrain: " + generator.HasTerrain());
        }
    }
}
#endif
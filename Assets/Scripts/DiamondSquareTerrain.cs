using UnityEngine;
using UnityEditor;
using UnityEngine.Profiling;

[System.Serializable]
public enum TerrainTypeD
{
    Custom,
    Flat,
    Mountains,
    Islands,
    Canyon
}

[System.Serializable]
public enum TerrainSize
{
    Size_129x129 = 7,    // 2^7 + 1 = 129
    Size_257x257 = 8,    // 2^8 + 1 = 257
    Size_513x513 = 9,    // 2^9 + 1 = 513
    Size_1025x1025 = 10, // 2^10 + 1 = 1025
    Size_2049x2049 = 11, // 2^11 + 1 = 2049
    Size_4097x4097 = 12  // 2^12 + 1 = 4097 (Unity max)
}

[System.Serializable]
public class DiamondSquareSettings
{
    [Header("Scene Type")]
    public TerrainTypeD terrainType = TerrainTypeD.Custom;
    
    [Header("Terrain Size")]
    [Tooltip("Select terrain resolution. Higher = more detail but slower generation")]
    public TerrainSize terrainSize = TerrainSize.Size_513x513;
    
    [Header("Advanced Size Control")]
    [Tooltip("Manual power control (overrides Terrain Size if > 0)")]
    [Range(0, 12)]
    public int customPower = 0; // 0 = use terrainSize enum
    
    [Header("Height Settings")]
    public float maxHeight = 50f;
    public float cornerHeight = 25f;

    [Header("Randomness")]
    public float initialRandomRange = 20f;
    [Range(0.1f, 1.0f)]
    public float roughness = 0.5f;

    [Header("Seeding")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Post-processing")]
    public bool smoothTerrain = false;
    [Range(1, 5)]
    public int smoothingPasses = 1;
    
    [Header("Scene-Specific Settings")]
    [Space]
    [Header("Islands Settings")]
    [Range(0.1f, 0.9f)]
    public float islandFalloffRadius = 0.7f;
    [Range(1.0f, 5.0f)]
    public float islandFalloffStrength = 2.0f;
    
    [Header("Canyon Settings")]
    [Range(0.1f, 0.5f)]
    public float canyonWidth = 0.2f;
    [Range(0.3f, 0.8f)]
    public float canyonDepth = 0.6f;
    public int canyonBranches = 2;
    
    [Header("Mountain Settings")]
    [Range(1, 5)]
    public int mountainPeaks = 3;
    [Range(0.3f, 0.9f)]
    public float peakHeight = 0.8f;
    
    // Helper method to get actual power value
    public int GetPower()
    {
        return customPower > 0 ? customPower : (int)terrainSize;
    }
    
    // Helper method to get actual size
    public int GetActualSize()
    {
        int power = GetPower();
        return (int)Mathf.Pow(2, power) + 1;
    }
}

[RequireComponent(typeof(Terrain))]
public class DiamondSquareTerrain : MonoBehaviour
{
    [SerializeField] public DiamondSquareSettings settings = new DiamondSquareSettings();

    private Terrain terrain;
    private TerrainData terrainData;
    private float[,] heightMap;
    private int size;

    void Start()
    {
        InitializeTerrain();
        GenerateTerrain();
    }

    void InitializeTerrain()
    {
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData ?? new TerrainData();
        terrain.terrainData = terrainData;

        // Use the new size calculation
        size = settings.GetActualSize();
        size = Mathf.Min(size, 4097); // Unity max resolution

        terrainData.heightmapResolution = size;
        terrainData.size = new Vector3(size, settings.maxHeight, size);

        heightMap = new float[size, size];
        
        Debug.Log($"Initialized terrain with size: {size}x{size} (power: {settings.GetPower()})");
    }

    public void GenerateTerrain()
    {
        if (settings.useRandomSeed)
            Random.InitState(System.Environment.TickCount);
        else
            Random.InitState(settings.seed);

        // Apply terrain type presets
        ApplyTerrainTypeSettings();
        
        InitializeCorners();
        DiamondSquareAlgorithm();

        // Apply scene-specific post-processing
        ApplyScenePostProcessing();

        if (settings.smoothTerrain)
            SmoothHeightMap();

        NormalizeHeightMap();
        ApplyHeightMapToTerrain();
    }

    void ApplyTerrainTypeSettings()
    {
        switch (settings.terrainType)
        {
            case TerrainTypeD.Flat:
                settings.initialRandomRange = 2f;
                settings.roughness = 0.2f;
                settings.cornerHeight = settings.maxHeight * 0.5f;
                break;
                
            case TerrainTypeD.Mountains:
                settings.initialRandomRange = settings.maxHeight * 0.8f;
                settings.roughness = 0.8f;
                settings.cornerHeight = settings.maxHeight * 0.3f;
                break;
                
            case TerrainTypeD.Islands:
                settings.initialRandomRange = settings.maxHeight * 0.4f;
                settings.roughness = 0.5f;
                settings.cornerHeight = 0f; // Water level at edges
                break;
                
            case TerrainTypeD.Canyon:
                settings.initialRandomRange = settings.maxHeight * 0.3f;
                settings.roughness = 0.4f;
                settings.cornerHeight = settings.maxHeight * 0.7f;
                break;
        }
    }

    void InitializeCorners()
    {
        float normalizedCornerHeight = settings.cornerHeight / settings.maxHeight;
        
        if (settings.terrainType == TerrainTypeD.Mountains)
        {
            // Random corner heights for mountains
            heightMap[0, 0] = normalizedCornerHeight + Random.Range(-0.2f, 0.2f);
            heightMap[0, size - 1] = normalizedCornerHeight + Random.Range(-0.2f, 0.2f);
            heightMap[size - 1, 0] = normalizedCornerHeight + Random.Range(-0.2f, 0.2f);
            heightMap[size - 1, size - 1] = normalizedCornerHeight + Random.Range(-0.2f, 0.2f);
        }
        else
        {
            heightMap[0, 0] = normalizedCornerHeight;
            heightMap[0, size - 1] = normalizedCornerHeight;
            heightMap[size - 1, 0] = normalizedCornerHeight;
            heightMap[size - 1, size - 1] = normalizedCornerHeight;
        }
    }

    void ApplyScenePostProcessing()
    {
        switch (settings.terrainType)
        {
            case TerrainTypeD.Islands:
                ApplyIslandMask();
                break;
                
            case TerrainTypeD.Canyon:
                ApplyCanyonCarving();
                break;
                
            case TerrainTypeD.Mountains:
                ApplyMountainPeaks();
                break;
                
            case TerrainTypeD.Flat:
                ApplyFlatteningFilter();
                break;
        }
    }

    void ApplyIslandMask()
    {
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxDistance = size * settings.islandFalloffRadius;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 point = new Vector2(x, y);
                float distance = Vector2.Distance(point, center);
                
                if (distance > maxDistance)
                {
                    float falloff = Mathf.Pow((distance - maxDistance) / (size * 0.5f - maxDistance), settings.islandFalloffStrength);
                    falloff = Mathf.Clamp01(falloff);
                    heightMap[x, y] = Mathf.Lerp(heightMap[x, y], 0f, falloff);
                }
            }
        }
    }

    void ApplyCanyonCarving()
    {
        // Main canyon path
        CarveCanyonPath(0.2f, 0.5f, 0.8f, 0.5f); // Horizontal main canyon
        
        // Additional branches
        for (int i = 0; i < settings.canyonBranches; i++)
        {
            float startX = Random.Range(0.3f, 0.7f);
            float startY = Random.Range(0.3f, 0.7f);
            float endX = Random.Range(0.3f, 0.7f);
            float endY = Random.Range(0.3f, 0.7f);
            
            CarveCanyonPath(startX, startY, endX, endY);
        }
    }

    void CarveCanyonPath(float startX, float startY, float endX, float endY)
    {
        int steps = size;
        float canyonWidthPixels = size * settings.canyonWidth;
        
        for (int step = 0; step < steps; step++)
        {
            float t = (float)step / steps;
            float x = Mathf.Lerp(startX * size, endX * size, t);
            float y = Mathf.Lerp(startY * size, endY * size, t);
            
            // Add some randomness to path
            x += Random.Range(-2f, 2f);
            y += Random.Range(-2f, 2f);
            
            int centerX = Mathf.RoundToInt(x);
            int centerY = Mathf.RoundToInt(y);
            
            // Carve canyon
            for (int dx = -(int)canyonWidthPixels; dx <= canyonWidthPixels; dx++)
            {
                for (int dy = -(int)canyonWidthPixels; dy <= canyonWidthPixels; dy++)
                {
                    int px = centerX + dx;
                    int py = centerY + dy;
                    
                    if (px >= 0 && px < size && py >= 0 && py < size)
                    {
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        if (distance <= canyonWidthPixels)
                        {
                            float depth = 1f - (distance / canyonWidthPixels);
                            float targetHeight = settings.canyonDepth * depth;
                            heightMap[px, py] = Mathf.Min(heightMap[px, py], targetHeight);
                        }
                    }
                }
            }
        }
    }

    void ApplyMountainPeaks()
    {
        for (int peak = 0; peak < settings.mountainPeaks; peak++)
        {
            int peakX = Random.Range(size / 4, 3 * size / 4);
            int peakY = Random.Range(size / 4, 3 * size / 4);
            float peakRadius = size * 0.15f;
            
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(peakX, peakY));
                    
                    if (distance < peakRadius)
                    {
                        float influence = 1f - (distance / peakRadius);
                        influence = Mathf.Pow(influence, 0.5f); // Smoother falloff
                        float peakAddition = settings.peakHeight * influence;
                        heightMap[x, y] = Mathf.Max(heightMap[x, y], heightMap[x, y] + peakAddition);
                    }
                }
            }
        }
    }

    void ApplyFlatteningFilter()
    {
        // Reduce extreme variations for flat terrain
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float currentHeight = heightMap[x, y];
                float flatHeight = 0.5f; // Middle height
                
                // Pull towards flat height but keep some variation
                heightMap[x, y] = Mathf.Lerp(currentHeight, flatHeight, 0.7f);
            }
        }
    }

    void DiamondSquareAlgorithm()
    {
        int stepSize = size - 1;
        float randomRange = settings.initialRandomRange / settings.maxHeight;

        while (stepSize > 1)
        {
            DiamondStep(stepSize, randomRange);
            SquareStep(stepSize, randomRange);

            stepSize /= 2;
            randomRange *= settings.roughness;
        }
    }

    void DiamondStep(int stepSize, float randomRange)
    {
        int halfStep = stepSize / 2;

        for (int x = 0; x < size - 1; x += stepSize)
        {
            for (int y = 0; y < size - 1; y += stepSize)
            {
                float a = heightMap[x, y];
                float b = heightMap[x + stepSize, y];
                float c = heightMap[x, y + stepSize];
                float d = heightMap[x + stepSize, y + stepSize];

                float average = (a + b + c + d) / 4f;
                float randomValue = Random.Range(-randomRange, randomRange);

                int cx = Mathf.Min(x + halfStep, size - 1);
                int cy = Mathf.Min(y + halfStep, size - 1);
                heightMap[cx, cy] = average + randomValue;
            }
        }
    }

    void SquareStep(int stepSize, float randomRange)
    {
        int halfStep = stepSize / 2;

        for (int x = 0; x < size; x += halfStep)
        {
            for (int y = (x + halfStep) % stepSize; y < size; y += stepSize)
            {
                float average = 0f;
                int count = 0;

                if (x - halfStep >= 0) { average += heightMap[x - halfStep, y]; count++; }
                if (x + halfStep < size) { average += heightMap[x + halfStep, y]; count++; }
                if (y - halfStep >= 0) { average += heightMap[x, y - halfStep]; count++; }
                if (y + halfStep < size) { average += heightMap[x, y + halfStep]; count++; }

                average /= count;
                float randomValue = Random.Range(-randomRange, randomRange);

                heightMap[x, y] = average + randomValue;
            }
        }
    }

    void SmoothHeightMap()
    {
        for (int pass = 0; pass < settings.smoothingPasses; pass++)
        {
            float[,] smoothedMap = new float[size, size];

            for (int x = 1; x < size - 1; x++)
            {
                for (int y = 1; y < size - 1; y++)
                {
                    float sum = 0f;
                    int count = 0;

                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            sum += heightMap[x + dx, y + dy];
                            count++;
                        }

                    smoothedMap[x, y] = sum / count;
                }
            }

            for (int x = 1; x < size - 1; x++)
                for (int y = 1; y < size - 1; y++)
                    heightMap[x, y] = smoothedMap[x, y];
        }
    }

    void NormalizeHeightMap()
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float val = heightMap[x, y];
                if (val < min) min = val;
                if (val > max) max = val;
            }

        float range = max - min;
        Debug.Log($"Before normalization: Min={min:F6}, Max={max:F6}, Range={range:F6}");
        
        if (range == 0f)
        {
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    heightMap[x, y] = 0f;
        }
        else
        {
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    heightMap[x, y] = Mathf.Clamp01((heightMap[x, y] - min) / range);
        }
        
        // Debug after normalization
        float newMin = float.MaxValue;
        float newMax = float.MinValue;
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float val = heightMap[x, y];
                if (val < newMin) newMin = val;
                if (val > newMax) newMax = val;
            }
        Debug.Log($"After normalization: Min={newMin:F6}, Max={newMax:F6}");
    }

    void ApplyHeightMapToTerrain()
    {
        terrainData.SetHeights(0, 0, heightMap);
    }

    public void RegenerateTerrain()
    {
        InitializeTerrain();
        GenerateTerrain();
    }

    public float[,] GetHeightMap()
    {
        return heightMap; // Return reference, not clone for stats checking
    }
    
    public float[,] GetHeightMapCopy()
    {
        return (float[,])heightMap?.Clone(); // Safe copy method
    }

    // Method for benchmarking
    public float GenerateTerrainTimed()
    {
        float startTime = Time.realtimeSinceStartup;
        GenerateTerrain();
        return (Time.realtimeSinceStartup - startTime) * 1000f; // Return milliseconds
    }
    
    // Method for comprehensive benchmarking with memory tracking
    public BenchmarkResult GenerateTerrainBenchmark()
    {
        // Force garbage collection before measurement
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        
        // Get initial memory
        long initialMemory = System.GC.GetTotalMemory(false);
        long initialUnityMemory = Profiler.GetTotalAllocatedMemoryLong();
        
        // Measure generation time
        float startTime = Time.realtimeSinceStartup;
        GenerateTerrain();
        float endTime = Time.realtimeSinceStartup;
        
        // Get final memory
        long finalMemory = System.GC.GetTotalMemory(false);
        long finalUnityMemory = Profiler.GetTotalAllocatedMemoryLong();
        
        // Calculate differences
        long memoryUsed = finalMemory - initialMemory;
        long unityMemoryUsed = finalUnityMemory - initialUnityMemory;
        float generationTime = (endTime - startTime) * 1000f;
        
        // Calculate theoretical memory usage
        int theoreticalMemory = size * size * sizeof(float); // HeightMap array
        
        return new BenchmarkResult
        {
            GenerationTimeMs = generationTime,
            MemoryUsedBytes = memoryUsed,
            UnityMemoryUsedBytes = unityMemoryUsed,
            TheoreticalMemoryBytes = theoreticalMemory,
            TerrainSize = size,
            PointCount = size * size,
            MemoryPerPoint = (float)memoryUsed / (size * size),
            MemoryEfficiency = (float)theoreticalMemory / Mathf.Max(1, memoryUsed)
        };
    }
    
    // FIXED: Bulk testing with memory tracking
    public void RunMemoryBenchmarkSuite(int iterations = 5)
    {
        TerrainTypeD[] terrainTypes = { TerrainTypeD.Flat, TerrainTypeD.Mountains, TerrainTypeD.Islands, TerrainTypeD.Canyon };
        int[] powers = { 7, 8, 9, 10 }; // Powers that give us 129, 257, 513, 1025
        
        Debug.Log("=== DIAMOND SQUARE MEMORY BENCHMARK SUITE ===");
        Debug.Log("Format: [Type] [Size] - Time: Xms, Memory: XMB, Unity Memory: XMB, Efficiency: X%");
        
        foreach (var terrainType in terrainTypes)
        {
            foreach (var powerValue in powers)
            {
                settings.terrainType = terrainType;
                settings.customPower = powerValue; // FIXED: Use customPower instead of power
                int targetSize = (int)Mathf.Pow(2, powerValue) + 1;
                
                float totalTime = 0f;
                long totalMemory = 0;
                long totalUnityMemory = 0;
                float totalEfficiency = 0f;
                
                for (int i = 0; i < iterations; i++)
                {
                    InitializeTerrain();
                    BenchmarkResult benchmarkResult = GenerateTerrainBenchmark();
                    
                    totalTime += benchmarkResult.GenerationTimeMs;
                    totalMemory += benchmarkResult.MemoryUsedBytes;
                    totalUnityMemory += benchmarkResult.UnityMemoryUsedBytes;
                    totalEfficiency += benchmarkResult.MemoryEfficiency;
                }
                
                // Calculate averages
                float avgTime = totalTime / iterations;
                float avgMemoryMB = (totalMemory / iterations) / (1024f * 1024f);
                float avgUnityMemoryMB = (totalUnityMemory / iterations) / (1024f * 1024f);
                float avgEfficiency = (totalEfficiency / iterations) * 100f;
                
                string resultString = $"{terrainType} {targetSize}x{targetSize} - Time: {avgTime:F1}ms, " +
                                     $"Memory: {avgMemoryMB:F2}MB, Unity Memory: {avgUnityMemoryMB:F2}MB, " +
                                     $"Efficiency: {avgEfficiency:F1}%";
                
                Debug.Log(resultString);
            }
        }
    }

    // ENHANCED: Method to get terrain statistics with highest point tracking
    public TerrainStats GetTerrainStats()
    {
        if (heightMap == null || size <= 0)
        {
            Debug.LogWarning("HeightMap is null or size is 0. Generate terrain first!");
            return new TerrainStats();
        }
        
        float min = float.MaxValue;
        float max = float.MinValue;
        float sum = 0f;
        float sumSquares = 0f;
        int totalPoints = size * size;
        
        // Track coordinates of highest and lowest points
        Vector2Int highestPoint = Vector2Int.zero;
        Vector2Int lowestPoint = Vector2Int.zero;
        
        // Debug info
        Debug.Log($"Analyzing heightmap of size {size}x{size} with {totalPoints} points");
        
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float height = heightMap[x, y];
                
                if (height > max)
                {
                    max = height;
                    highestPoint = new Vector2Int(x, y);
                }
                
                if (height < min)
                {
                    min = height;
                    lowestPoint = new Vector2Int(x, y);
                }
                
                sum += height;
                sumSquares += height * height;
            }
        }
        
        // Check if we got meaningful data
        if (min == float.MaxValue || max == float.MinValue)
        {
            Debug.LogWarning("Invalid height data detected!");
            return new TerrainStats();
        }
        
        float mean = sum / totalPoints;
        float variance = (sumSquares / totalPoints) - (mean * mean);
        float stdDev = Mathf.Sqrt(Mathf.Max(0f, variance)); // Prevent negative variance
        
        // Calculate actual world heights
        float highestWorldHeight = max * settings.maxHeight;
        float lowestWorldHeight = min * settings.maxHeight;
        
        Debug.Log($"Raw stats: Min={min:F6}, Max={max:F6}, Mean={mean:F6}, StdDev={stdDev:F6}");
        Debug.Log($"Highest Point: ({highestPoint.x}, {highestPoint.y}) at {highestWorldHeight:F2} world units");
        Debug.Log($"Lowest Point: ({lowestPoint.x}, {lowestPoint.y}) at {lowestWorldHeight:F2} world units");
        
        return new TerrainStats
        {
            MinHeight = min,
            MaxHeight = max,
            MeanHeight = mean,
            StandardDeviation = stdDev,
            HeightRange = max - min,
            TerrainSize = size,
            HighestPointCoordinates = highestPoint,
            LowestPointCoordinates = lowestPoint,
            HighestPointWorldHeight = highestWorldHeight,
            LowestPointWorldHeight = lowestWorldHeight
        };
    }

    // NEW: Get the highest point as a world position
    public Vector3 GetHighestPointWorldPosition()
    {
        TerrainStats stats = GetTerrainStats();
        
        // Convert grid coordinates to world position
        float worldX = transform.position.x + (stats.HighestPointCoordinates.x * terrainData.size.x / size);
        float worldY = transform.position.y + stats.HighestPointWorldHeight;
        float worldZ = transform.position.z + (stats.HighestPointCoordinates.y * terrainData.size.z / size);
        
        return new Vector3(worldX, worldY, worldZ);
    }

    // NEW: Get the lowest point as a world position
    public Vector3 GetLowestPointWorldPosition()
    {
        TerrainStats stats = GetTerrainStats();
        
        // Convert grid coordinates to world position
        float worldX = transform.position.x + (stats.LowestPointCoordinates.x * terrainData.size.x / size);
        float worldY = transform.position.y + stats.LowestPointWorldHeight;
        float worldZ = transform.position.z + (stats.LowestPointCoordinates.y * terrainData.size.z / size);
        
        return new Vector3(worldX, worldY, worldZ);
    }

    // NEW: Visual debugging method to mark highest/lowest points
    public void MarkExtremePoints()
    {
        Vector3 highestPos = GetHighestPointWorldPosition();
        Vector3 lowestPos = GetLowestPointWorldPosition();
        
        // Create debug spheres at runtime (or use Gizmos in editor)
        Debug.DrawRay(highestPos, Vector3.up * 10f, Color.green, 5f);
        Debug.DrawRay(lowestPos, Vector3.up * 10f, Color.red, 5f);
        
        Debug.Log($"Highest point (Green): {highestPos}");
        Debug.Log($"Lowest point (Red): {lowestPos}");
    }

    public Texture2D ExportHeightmapAsTexture()
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, false);

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float height = heightMap[x, y];
                Color color = new Color(height, height, height, 1f);
                texture.SetPixel(x, size - 1 - y, color);
            }

        texture.Apply();
        return texture;
    }

    // Bulk testing method for research
    public void RunBenchmarkSuite(int iterations = 10)
    {
        TerrainTypeD[] terrainTypes = { TerrainTypeD.Flat, TerrainTypeD.Mountains, TerrainTypeD.Islands, TerrainTypeD.Canyon };
        TerrainSize[] sizes = { TerrainSize.Size_129x129, TerrainSize.Size_257x257, TerrainSize.Size_513x513, TerrainSize.Size_1025x1025 };
        
        Debug.Log("=== DIAMOND SQUARE BENCHMARK SUITE ===");
        
        foreach (var terrainType in terrainTypes)
        {
            foreach (var size in sizes)
            {
                settings.terrainType = terrainType;
                settings.terrainSize = size;
                
                float totalTime = 0f;
                for (int i = 0; i < iterations; i++)
                {
                    InitializeTerrain();
                    totalTime += GenerateTerrainTimed();
                }
                
                float avgTime = totalTime / iterations;
                TerrainStats stats = GetTerrainStats();
                
                Debug.Log($"{terrainType} - {size}: {avgTime:F2}ms avg, StdDev: {stats.StandardDeviation:F3}, Range: {stats.HeightRange:F3}");
            }
        }
    }

    void OnValidate()
    {
        settings.customPower = Mathf.Clamp(settings.customPower, 0, 12);
        settings.maxHeight = Mathf.Max(0.1f, settings.maxHeight);
        settings.initialRandomRange = Mathf.Max(0f, settings.initialRandomRange);
        settings.roughness = Mathf.Clamp01(settings.roughness);
    }
}

// ENHANCED: TerrainStats struct with highest point information
[System.Serializable]
public struct TerrainStats
{
    public float MinHeight;
    public float MaxHeight;
    public float MeanHeight;
    public float StandardDeviation;
    public float HeightRange;
    public int TerrainSize;
    
    // New fields for highest point tracking
    public Vector2Int HighestPointCoordinates;
    public Vector2Int LowestPointCoordinates;
    public float HighestPointWorldHeight; // Actual world height in units
    public float LowestPointWorldHeight;  // Actual world height in units
    
    public override string ToString()
    {
        return $"=== TERRAIN STATISTICS ===\n" +
               $"Size: {TerrainSize}x{TerrainSize}\n" +
               $"Height Range (Normalized): {MinHeight:F4} - {MaxHeight:F4}\n" +
               $"Mean Height: {MeanHeight:F4}\n" +
               $"Standard Deviation: {StandardDeviation:F4}\n" +
               $"Height Variation: {HeightRange:F4}\n" +
               $"Highest Point: ({HighestPointCoordinates.x}, {HighestPointCoordinates.y}) at {HighestPointWorldHeight:F2} units\n" +
               $"Lowest Point: ({LowestPointCoordinates.x}, {LowestPointCoordinates.y}) at {LowestPointWorldHeight:F2} units";
    }
}

[System.Serializable]
public struct BenchmarkResult
{
    public float GenerationTimeMs;
    public long MemoryUsedBytes;
    public long UnityMemoryUsedBytes;
    public int TheoreticalMemoryBytes;
    public int TerrainSize;
    public int PointCount;
    public float MemoryPerPoint;
    public float MemoryEfficiency;
    
    public override string ToString()
    {
        return $"Time: {GenerationTimeMs:F2}ms, Memory: {MemoryUsedBytes / (1024f * 1024f):F2}MB, " +
               $"Unity Memory: {UnityMemoryUsedBytes / (1024f * 1024f):F2}MB, " +
               $"Size: {TerrainSize}x{TerrainSize}, Efficiency: {MemoryEfficiency * 100f:F1}%";
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(DiamondSquareTerrain))]
public class DiamondSquareTerrainEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        DiamondSquareTerrain terrainGenerator = (DiamondSquareTerrain)target;

        // Show current terrain info
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Current Terrain Info:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Size: {terrainGenerator.settings.GetActualSize()}x{terrainGenerator.settings.GetActualSize()}");
        EditorGUILayout.LabelField($"Power: {terrainGenerator.settings.GetPower()}");
        EditorGUILayout.LabelField($"Total Points: {terrainGenerator.settings.GetActualSize() * terrainGenerator.settings.GetActualSize():N0}");
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate Terrain"))
            terrainGenerator.RegenerateTerrain();

        if (GUILayout.Button("Benchmark Generation"))
        {
            float time = terrainGenerator.GenerateTerrainTimed();
            Debug.Log($"Terrain generation took: {time:F2} ms");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Memory Benchmark"))
        {
            BenchmarkResult result = terrainGenerator.GenerateTerrainBenchmark();
            Debug.Log($"=== MEMORY BENCHMARK RESULT ===\n{result}");
        }

        if (GUILayout.Button("Full Benchmark Suite"))
        {
            terrainGenerator.RunMemoryBenchmarkSuite();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        // ENHANCED: Show Terrain Stats button with highest point info
        if (GUILayout.Button("Show Terrain Stats"))
        {
            // Ensure terrain is generated first
            if (terrainGenerator.GetHeightMap() == null)
            {
                Debug.LogWarning("No terrain generated yet. Generating now...");
                terrainGenerator.RegenerateTerrain();
            }
            
            TerrainStats stats = terrainGenerator.GetTerrainStats();
            
            if (stats.TerrainSize > 0)
            {
                string statsText = stats.ToString() + $"\nTerrain Type: {terrainGenerator.settings.terrainType}";
                
                Debug.Log(statsText);
                
                // Also show in editor dialog
                EditorUtility.DisplayDialog("Terrain Statistics", statsText, "OK");
            }
            else
            {
                Debug.LogError("Failed to generate terrain statistics!");
            }
        }

        if (GUILayout.Button("Run Benchmark Suite"))
        {
            terrainGenerator.RunBenchmarkSuite();
        }
        EditorGUILayout.EndHorizontal();

        // NEW: Mark Highest/Lowest Points button
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Mark Highest/Lowest Points"))
        {
            if (terrainGenerator.GetHeightMap() == null)
            {
                Debug.LogWarning("No terrain generated yet. Generating now...");
                terrainGenerator.RegenerateTerrain();
            }
            
            terrainGenerator.MarkExtremePoints();
            
            // Optional: Create temporary GameObjects as markers
            TerrainStats stats = terrainGenerator.GetTerrainStats();
            Vector3 highestPos = terrainGenerator.GetHighestPointWorldPosition();
            Vector3 lowestPos = terrainGenerator.GetLowestPointWorldPosition();
            
            // Clean up old markers
            GameObject oldHighMarker = GameObject.Find("Highest Point Marker");
            GameObject oldLowMarker = GameObject.Find("Lowest Point Marker");
            if (oldHighMarker) DestroyImmediate(oldHighMarker);
            if (oldLowMarker) DestroyImmediate(oldLowMarker);
            
            // Create new marker objects
            GameObject highMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            highMarker.name = "Highest Point Marker";
            highMarker.transform.position = highestPos;
            highMarker.transform.localScale = Vector3.one * 5f;
            highMarker.GetComponent<Renderer>().material.color = Color.green;
            
            GameObject lowMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lowMarker.name = "Lowest Point Marker";
            lowMarker.transform.position = lowestPos;
            lowMarker.transform.localScale = Vector3.one * 5f;
            lowMarker.GetComponent<Renderer>().material.color = Color.red;
            
            Debug.Log($"Created markers at highest and lowest points");
            Debug.Log($"Highest: {highestPos} (Grid: {stats.HighestPointCoordinates})");
            Debug.Log($"Lowest: {lowestPos} (Grid: {stats.LowestPointCoordinates})");
        }

        if (GUILayout.Button("Export Heightmap"))
        {
            Texture2D heightmapTexture = terrainGenerator.ExportHeightmapAsTexture();
            string path = EditorUtility.SaveFilePanel("Save Heightmap", "Assets", $"heightmap_{terrainGenerator.settings.GetActualSize()}x{terrainGenerator.settings.GetActualSize()}.png", "png");

            if (!string.IsNullOrEmpty(path))
            {
                byte[] bytes = heightmapTexture.EncodeToPNG();
                System.IO.File.WriteAllBytes(path, bytes);
                AssetDatabase.Refresh();
                Debug.Log("Heightmap saved to: " + path);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Terrain Sizes:\n" +
            "• 129x129: Small, fast generation (~16K points)\n" +
            "• 257x257: Medium detail (~66K points)\n" +
            "• 513x513: High detail (~263K points)\n" +
            "• 1025x1025: Very high detail (~1M points)\n" +
            "• 2049x2049: Extreme detail (~4M points)\n" +
            "• 4097x4097: Maximum detail (~16M points)\n\n" +
            "Scene Types:\n" +
            "• Flat: Low variation, gentle rolling hills\n" +
            "• Mountains: High peaks, dramatic elevation changes\n" +
            "• Islands: Land masses surrounded by water\n" +
            "• Canyon: Deep carved valleys and ravines\n" +
            "• Custom: Manual parameter control\n\n" +
            "NEW: Stats now show highest/lowest points with coordinates!",
            MessageType.Info
        );
    }
}
#endif
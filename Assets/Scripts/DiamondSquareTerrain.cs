using UnityEngine;
using UnityEditor;

[System.Serializable]
public class DiamondSquareSettings
{
    [Header("Terrain Size")]
    [Tooltip("Power of 2 for terrain resolution (3 = 9x9, 4 = 17x17, etc.)")]
    [Range(3, 10)]
    public int power = 7; // 129x129

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
}

[RequireComponent(typeof(Terrain))]
public class DiamondSquareTerrain : MonoBehaviour
{
    [SerializeField] private DiamondSquareSettings settings = new DiamondSquareSettings();

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

        // Calculate size = 2^n + 1
        size = (int)Mathf.Pow(2, settings.power) + 1;
        size = Mathf.Min(size, 4097); // Unity max resolution

        terrainData.heightmapResolution = size;
        terrainData.size = new Vector3(size, settings.maxHeight, size);

        heightMap = new float[size, size];
    }

    public void GenerateTerrain()
    {
        if (settings.useRandomSeed)
            Random.InitState(System.Environment.TickCount);
        else
            Random.InitState(settings.seed);

        InitializeCorners();
        DiamondSquareAlgorithm();

        if (settings.smoothTerrain)
            SmoothHeightMap();

        NormalizeHeightMap();
        ApplyHeightMapToTerrain();
    }

    void InitializeCorners()
    {
        float normalizedCornerHeight = settings.cornerHeight / settings.maxHeight;
        heightMap[0, 0] = normalizedCornerHeight;
        heightMap[0, size - 1] = normalizedCornerHeight;
        heightMap[size - 1, 0] = normalizedCornerHeight;
        heightMap[size - 1, size - 1] = normalizedCornerHeight;
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
        return (float[,])heightMap.Clone();
    }

    public Texture2D ExportHeightmapAsTexture()
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, false);

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float height = heightMap[x, y];
                Color color = new Color(height, height, height, 1f);
                texture.SetPixel(x, size - 1 - y, color); // Flip Y for correct orientation
            }

        texture.Apply();
        return texture;
    }

    void OnValidate()
    {
        settings.power = Mathf.Clamp(settings.power, 3, 10);
        settings.maxHeight = Mathf.Max(0.1f, settings.maxHeight);
        settings.initialRandomRange = Mathf.Max(0f, settings.initialRandomRange);
        settings.roughness = Mathf.Clamp01(settings.roughness);
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

        if (GUILayout.Button("Generate New Terrain"))
            terrainGenerator.RegenerateTerrain();

        if (GUILayout.Button("Export Heightmap as Texture"))
        {
            Texture2D heightmapTexture = terrainGenerator.ExportHeightmapAsTexture();
            string path = EditorUtility.SaveFilePanel("Save Heightmap Texture", "Assets", "heightmap.png", "png");

            if (!string.IsNullOrEmpty(path))
            {
                byte[] bytes = heightmapTexture.EncodeToPNG();
                System.IO.File.WriteAllBytes(path, bytes);
                AssetDatabase.Refresh();
                Debug.Log("Heightmap texture saved to: " + path);
            }
        }

        EditorGUILayout.HelpBox(
            "Power determines resolution:\n" +
            "3 = 9x9, 4 = 17x17, 5 = 33x33, 6 = 65x65\n" +
            "7 = 129x129, 8 = 257x257, 9 = 513x513, 10 = 1025x1025",
            MessageType.Info
        );
    }
}
#endif

using System.Collections.Generic;
using UnityEngine;

public enum HeatmapDetectionMode
{
    ImageProcessing,
    CarTransform
}

public class ImageBasedHeatmapManager : MonoBehaviour
{
    [Header("Detection Mode")]
    public HeatmapDetectionMode detectionMode = HeatmapDetectionMode.CarTransform;

    [Header("Image Processing Detection")]
    public Camera detectionCamera;
    public RenderTexture detectionRenderTexture;

    [Header("Heatmap Display")]
    public Renderer heatmapRenderer;

    [Header("Heatmap Texture Settings")]
    public int heatmapWidth = 128;
    public int heatmapHeight = 128;

    [Header("World Area")]
    public float worldMinX = -180f;
    public float worldMaxX = 180f;
    public float worldMinZ = -150f;
    public float worldMaxZ = 150f;

    [Header("Coordinate Correction")]
    public bool flipHeatmapX = true;
    public bool flipHeatmapZ = false;

    [Header("Image Processing Settings")]
    public float brightnessThreshold = 0.1f;
    public int pixelStep = 4;
    public int minBlobPixelCount = 6;
    public int maxDetectedCars = 50;

    [Header("Heat Settings")]
    public float updateInterval = 0.25f;
    public float heatAddAmount = 0.08f;
    public float heatDecay = 0.985f;
    public int heatRadius = 12;

    [Header("Display Alpha")]
    public float emptyAlpha = 0.05f;
    public float minHeatAlpha = 0.20f;
    public float maxHeatAlpha = 0.80f;

    [Header("Heatmap Plane Object")]
    public GameObject heatmapPlaneObject;
    public bool showHeatmapOnlyInPlayMode = true;

    [Header("Debug")]
    public bool showDebugLog = false;
    public bool addTestHeatOnStart = false;

    private Texture2D captureTexture;
    private Texture2D heatmapTexture;
    private float[,] heatValues;
    private float updateTimer;

    private void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ConfigureWebGlNormalMapOnly();
        enabled = false;
        return;
#endif

        if (showHeatmapOnlyInPlayMode && heatmapPlaneObject != null)
        {
            heatmapPlaneObject.SetActive(true);
        }

        Initialize();

        if (addTestHeatOnStart)
        {
            AddTestHeatCenter();
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// WebGL is used as the frontend viewer. In that build we show only the
    /// normal parking-lot camera and skip the heatmap rendering pipeline.
    /// Unity Editor Play Mode is intentionally unaffected.
    /// </summary>
    private void ConfigureWebGlNormalMapOnly()
    {
        if (heatmapPlaneObject != null)
        {
            heatmapPlaneObject.SetActive(false);
        }

        if (detectionCamera != null)
        {
            detectionCamera.enabled = false;
        }

        GameObject heatmapViewObject = GameObject.Find("HeatmapViewCamera");
        if (heatmapViewObject != null)
        {
            Camera heatmapViewCamera = heatmapViewObject.GetComponent<Camera>();
            if (heatmapViewCamera != null)
            {
                heatmapViewCamera.enabled = false;
            }
        }

        Camera normalCamera = Camera.main;
        if (normalCamera != null)
        {
            normalCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }
    }
#endif

    private void Update()
    {
        updateTimer += Time.deltaTime;

        if (updateTimer < updateInterval)
        {
            return;
        }

        updateTimer = 0f;

        if (detectionMode == HeatmapDetectionMode.ImageProcessing)
        {
            CaptureDetectionImage();
            ProcessDetectionImage();
        }
        else
        {
            ProcessCarTransforms();
        }

        DecayHeatValues();
        UpdateHeatmapTexture();
    }

    private void Initialize()
    {
        heatValues = new float[heatmapWidth, heatmapHeight];

        heatmapTexture = new Texture2D(
            heatmapWidth,
            heatmapHeight,
            TextureFormat.RGBA32,
            false
        );

        heatmapTexture.wrapMode = TextureWrapMode.Clamp;
        heatmapTexture.filterMode = FilterMode.Bilinear;

        ClearHeatmapTexture();
        SetHeatmapTextureToMaterial();

        if (detectionRenderTexture != null)
        {
            captureTexture = new Texture2D(
                detectionRenderTexture.width,
                detectionRenderTexture.height,
                TextureFormat.RGBA32,
                false
            );
        }
    }

    private void SetHeatmapTextureToMaterial()
    {
        if (heatmapRenderer == null)
        {
            Debug.LogWarning($"{name}: heatmapRenderer が設定されていません。");
            return;
        }

        Material material = heatmapRenderer.material;

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", heatmapTexture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", heatmapTexture);
        }
    }

    private void CaptureDetectionImage()
    {
        if (detectionCamera == null || detectionRenderTexture == null)
        {
            return;
        }

        if (captureTexture == null ||
            captureTexture.width != detectionRenderTexture.width ||
            captureTexture.height != detectionRenderTexture.height)
        {
            captureTexture = new Texture2D(
                detectionRenderTexture.width,
                detectionRenderTexture.height,
                TextureFormat.RGBA32,
                false
            );
        }

        RenderTexture currentRenderTexture = RenderTexture.active;

        detectionCamera.targetTexture = detectionRenderTexture;
        detectionCamera.Render();

        RenderTexture.active = detectionRenderTexture;

        captureTexture.ReadPixels(
            new Rect(0, 0, detectionRenderTexture.width, detectionRenderTexture.height),
            0,
            0
        );

        captureTexture.Apply();

        RenderTexture.active = currentRenderTexture;
    }

    private void ProcessDetectionImage()
    {
        if (captureTexture == null)
        {
            return;
        }

        int width = captureTexture.width;
        int height = captureTexture.height;

        bool[,] detectedPixels = new bool[width, height];
        bool[,] visited = new bool[width, height];

        int brightPixelCount = 0;

        for (int y = 0; y < height; y += pixelStep)
        {
            for (int x = 0; x < width; x += pixelStep)
            {
                Color color = captureTexture.GetPixel(x, y);
                float brightness = color.grayscale;

                if (brightness < brightnessThreshold)
                {
                    continue;
                }

                detectedPixels[x, y] = true;
                brightPixelCount++;
            }
        }

        int detectedCarCount = 0;

        for (int y = 0; y < height; y += pixelStep)
        {
            for (int x = 0; x < width; x += pixelStep)
            {
                if (!detectedPixels[x, y] || visited[x, y])
                {
                    continue;
                }

                BlobInfo blob = FindBlob(x, y, detectedPixels, visited, width, height);

                if (blob.pixelCount < minBlobPixelCount)
                {
                    continue;
                }

                int centerPixelX = Mathf.RoundToInt(blob.sumX / blob.pixelCount);
                int centerPixelY = Mathf.RoundToInt(blob.sumY / blob.pixelCount);

                if (TryConvertPixelToWorldPosition(centerPixelX, centerPixelY, out Vector3 worldPosition))
                {
                    AddSmoothHeatAtWorldPosition(worldPosition);
                    detectedCarCount++;
                }

                if (detectedCarCount >= maxDetectedCars)
                {
                    break;
                }
            }

            if (detectedCarCount >= maxDetectedCars)
            {
                break;
            }
        }

        if (showDebugLog)
        {
            Debug.Log($"ImageProcessing検出: 明るいピクセル数 {brightPixelCount}, 検出数 {detectedCarCount}");
        }
    }

    private BlobInfo FindBlob(
        int startX,
        int startY,
        bool[,] detectedPixels,
        bool[,] visited,
        int width,
        int height
    )
    {
        BlobInfo blob = new BlobInfo();

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            blob.pixelCount++;
            blob.sumX += current.x;
            blob.sumY += current.y;

            for (int offsetY = -pixelStep; offsetY <= pixelStep; offsetY += pixelStep)
            {
                for (int offsetX = -pixelStep; offsetX <= pixelStep; offsetX += pixelStep)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    int nextX = current.x + offsetX;
                    int nextY = current.y + offsetY;

                    if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                    {
                        continue;
                    }

                    if (visited[nextX, nextY] || !detectedPixels[nextX, nextY])
                    {
                        continue;
                    }

                    visited[nextX, nextY] = true;
                    queue.Enqueue(new Vector2Int(nextX, nextY));
                }
            }
        }

        return blob;
    }

    private bool TryConvertPixelToWorldPosition(
        int pixelX,
        int pixelY,
        out Vector3 worldPosition
    )
    {
        worldPosition = Vector3.zero;

        if (detectionCamera == null || captureTexture == null)
        {
            return false;
        }

        float screenX = pixelX;
        float screenY = pixelY;

        Ray ray = detectionCamera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));

        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (!groundPlane.Raycast(ray, out float enter))
        {
            return false;
        }

        worldPosition = ray.GetPoint(enter);
        return true;
    }

    private void ProcessCarTransforms()
    {
#if UNITY_2023_1_OR_NEWER
        Car[] cars = FindObjectsByType<Car>(FindObjectsSortMode.None);
#else
        Car[] cars = FindObjectsOfType<Car>();
#endif

        int detectedCarCount = 0;

        foreach (Car car in cars)
        {
            if (car == null)
            {
                continue;
            }

            AddSmoothHeatAtWorldPosition(car.transform.position);
            detectedCarCount++;
        }

        if (showDebugLog)
        {
            Debug.Log($"CarTransform検出: {detectedCarCount}台");
        }
    }

    private void AddSmoothHeatAtWorldPosition(Vector3 worldPosition)
    {
        if (!TryConvertWorldToHeatmapPosition(worldPosition, out int centerX, out int centerY))
        {
            return;
        }

        int radius = Mathf.Max(1, heatRadius);

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int targetX = centerX + x;
                int targetY = centerY + y;

                if (targetX < 0 || targetX >= heatmapWidth ||
                    targetY < 0 || targetY >= heatmapHeight)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(x * x + y * y);

                if (distance > radius)
                {
                    continue;
                }

                float normalizedDistance = distance / radius;
                float power = Mathf.Exp(-normalizedDistance * normalizedDistance * 4f);

                heatValues[targetX, targetY] += heatAddAmount * power;
                heatValues[targetX, targetY] = Mathf.Clamp01(heatValues[targetX, targetY]);
            }
        }
    }

    private bool TryConvertWorldToHeatmapPosition(
        Vector3 worldPosition,
        out int heatmapX,
        out int heatmapY
    )
    {
        heatmapX = 0;
        heatmapY = 0;

        if (worldMaxX <= worldMinX || worldMaxZ <= worldMinZ)
        {
            return false;
        }

        float normalizedX = Mathf.InverseLerp(worldMinX, worldMaxX, worldPosition.x);
        float normalizedZ = Mathf.InverseLerp(worldMinZ, worldMaxZ, worldPosition.z);

        if (flipHeatmapX)
        {
            normalizedX = 1f - normalizedX;
        }

        if (flipHeatmapZ)
        {
            normalizedZ = 1f - normalizedZ;
        }

        if (normalizedX < 0f || normalizedX > 1f ||
            normalizedZ < 0f || normalizedZ > 1f)
        {
            return false;
        }

        heatmapX = Mathf.RoundToInt(normalizedX * (heatmapWidth - 1));
        heatmapY = Mathf.RoundToInt(normalizedZ * (heatmapHeight - 1));

        return true;
    }

    private void DecayHeatValues()
    {
        for (int y = 0; y < heatmapHeight; y++)
        {
            for (int x = 0; x < heatmapWidth; x++)
            {
                heatValues[x, y] *= heatDecay;

                if (heatValues[x, y] < 0.001f)
                {
                    heatValues[x, y] = 0f;
                }
            }
        }
    }

    private void UpdateHeatmapTexture()
    {
        for (int y = 0; y < heatmapHeight; y++)
        {
            for (int x = 0; x < heatmapWidth; x++)
            {
                float value = Mathf.Clamp01(heatValues[x, y]);
                Color color = GetHeatColor(value);

                heatmapTexture.SetPixel(x, y, color);
            }
        }

        heatmapTexture.Apply();
    }

    private Color GetHeatColor(float value)
    {
        if (value <= 0.001f)
        {
            return new Color(0f, 0.2f, 1f, emptyAlpha);
        }

        Color color;

        if (value < 0.33f)
        {
            float t = value / 0.33f;
            color = Color.Lerp(Color.blue, Color.cyan, t);
        }
        else if (value < 0.66f)
        {
            float t = (value - 0.33f) / 0.33f;
            color = Color.Lerp(Color.cyan, Color.yellow, t);
        }
        else
        {
            float t = (value - 0.66f) / 0.34f;
            color = Color.Lerp(Color.yellow, Color.red, t);
        }

        color.a = Mathf.Lerp(minHeatAlpha, maxHeatAlpha, value);
        return color;
    }

    private void ClearHeatmapTexture()
    {
        if (heatmapTexture == null)
        {
            return;
        }

        Color clearColor = new Color(0f, 0.2f, 1f, emptyAlpha);

        for (int y = 0; y < heatmapHeight; y++)
        {
            for (int x = 0; x < heatmapWidth; x++)
            {
                heatmapTexture.SetPixel(x, y, clearColor);
            }
        }

        heatmapTexture.Apply();
    }

    [ContextMenu("Clear Heatmap")]
    public void ClearHeatmap()
    {
        if (heatValues == null)
        {
            heatValues = new float[heatmapWidth, heatmapHeight];
        }

        for (int y = 0; y < heatmapHeight; y++)
        {
            for (int x = 0; x < heatmapWidth; x++)
            {
                heatValues[x, y] = 0f;
            }
        }

        ClearHeatmapTexture();
    }

    [ContextMenu("Add Test Heat Center")]
    public void AddTestHeatCenter()
    {
        Vector3 center = new Vector3(
            (worldMinX + worldMaxX) * 0.5f,
            0f,
            (worldMinZ + worldMaxZ) * 0.5f
        );

        AddSmoothHeatAtWorldPosition(center);
        UpdateHeatmapTexture();
    }

    private struct BlobInfo
    {
        public int pixelCount;
        public float sumX;
        public float sumY;
    }
}
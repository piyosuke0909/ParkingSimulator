using System.Collections.Generic;
using UnityEngine;

public class ImageBasedHeatmapManager : MonoBehaviour
{
    [Header("Editor Visibility")]
    public GameObject heatmapPlaneObject;
    public bool showHeatmapOnlyInPlayMode = true;

    [Header("Detection Camera")]
    public Camera detectionCamera;
    public RenderTexture detectionRenderTexture;

    [Header("Heatmap Output")]
    public Renderer heatmapRenderer;
    public int heatmapWidth = 128;
    public int heatmapHeight = 128;

    [Header("World Area")]
    public float worldMinX = -180f;
    public float worldMaxX = 180f;
    public float worldMinZ = -150f;
    public float worldMaxZ = 150f;

    [Header("Coordinate Fix")]
    public bool flipHeatmapX = true;
    public bool flipHeatmapZ = false;

    [Header("Image Processing")]
    [Range(0f, 1f)]
    public float brightnessThreshold = 0.1f;

    [Tooltip("車として扱う最小ピクセル数。小さすぎる白点ノイズを無視します。")]
    public int minBlobPixelCount = 6;

    [Tooltip("1回の更新で検出する最大車両数。多すぎる誤検出を防ぎます。")]
    public int maxDetectedCars = 50;

    [Tooltip("画像処理時に何ピクセルおきに確認するか。小さいほど精度が高いが重くなります。")]
    public int pixelStep = 4;

    [Tooltip("ヒートマップを何秒ごとに更新するか。")]
    public float updateInterval = 0.25f;

    [Header("Heat Settings")]
    [Tooltip("1回の検出で加算する熱量。")]
    public float heatAddAmount = 0.12f;

    [Tooltip("熱の減衰率。1に近いほど長く残ります。")]
    [Range(0.8f, 1f)]
    public float heatDecay = 0.985f;

    [Tooltip("熱が広がる半径。大きいほど広くぼかされます。")]
    public int heatRadius = 12;

    [Header("Debug")]
    public bool showDebugLog = false;
    public bool addTestHeatOnStart = false;

    private Texture2D captureTexture;
    private Texture2D heatmapTexture;
    private float[,] heatValues;

    private float timer;
    private bool initialized;

    private void Start()
    {
        if (heatmapPlaneObject != null && showHeatmapOnlyInPlayMode)
        {
            heatmapPlaneObject.SetActive(true);
        }

        Initialize();

        if (addTestHeatOnStart)
        {
            AddSmoothHeatAtWorldPosition(new Vector3(0f, 0f, 0f));
            AddSmoothHeatAtWorldPosition(new Vector3(60f, 0f, -40f));
            AddSmoothHeatAtWorldPosition(new Vector3(-60f, 0f, 40f));
            UpdateHeatmapTexture();
        }
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        if (captureTexture == null || heatmapTexture == null || heatValues == null)
        {
            initialized = false;
            Initialize();

            if (!initialized)
            {
                return;
            }
        }

        timer += Time.deltaTime;

        if (timer < updateInterval)
        {
            return;
        }

        timer = 0f;

        CaptureDetectionImage();
        DecayHeatValues();
        ProcessDetectionImage();
        UpdateHeatmapTexture();
    }

    private void Initialize()
    {
        initialized = false;

        if (detectionCamera == null)
        {
            Debug.LogError("Detection Camera が設定されていません。");
            return;
        }

        if (detectionRenderTexture == null)
        {
            Debug.LogError("Detection Render Texture が設定されていません。");
            return;
        }

        if (heatmapRenderer == null)
        {
            Debug.LogError("Heatmap Renderer が設定されていません。");
            return;
        }

        captureTexture = new Texture2D(
            detectionRenderTexture.width,
            detectionRenderTexture.height,
            TextureFormat.RGB24,
            false
        );

        heatmapTexture = new Texture2D(
            heatmapWidth,
            heatmapHeight,
            TextureFormat.RGBA32,
            false
        );

        heatValues = new float[heatmapWidth, heatmapHeight];

        heatmapTexture.wrapMode = TextureWrapMode.Clamp;
        heatmapTexture.filterMode = FilterMode.Bilinear;

        SetHeatmapTextureToMaterial();

        ClearHeatmapTexture();

        initialized = true;

        if (showDebugLog)
        {
            Debug.Log("ImageBasedHeatmapManager 初期化完了");
        }
    }

    private void SetHeatmapTextureToMaterial()
    {
        if (heatmapRenderer == null || heatmapTexture == null)
        {
            return;
        }

        Material material = heatmapRenderer.material;
        if (material == null)
        {
            return;
        }

        material.mainTexture = heatmapTexture;

        // URP Lit Shader 用
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", heatmapTexture);
        }

        // Built-in Standard Shader 用
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", heatmapTexture);
        }
    }

    private void CaptureDetectionImage()
    {
        if (captureTexture == null || detectionRenderTexture == null)
        {
            return;
        }

        RenderTexture previous = RenderTexture.active;

        RenderTexture.active = detectionRenderTexture;

        captureTexture.ReadPixels(
            new Rect(0, 0, detectionRenderTexture.width, detectionRenderTexture.height),
            0,
            0
        );

        captureTexture.Apply();

        RenderTexture.active = previous;
    }

    private void ProcessDetectionImage()
    {
        int width = captureTexture.width;
        int height = captureTexture.height;

        bool[,] detectedPixels = new bool[width, height];
        bool[,] visited = new bool[width, height];

        int brightPixelCount = 0;

        // まず、車と思われる明るいピクセルを記録する
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

        // 白いピクセルの塊を探す
        for (int y = 0; y < height; y += pixelStep)
        {
            for (int x = 0; x < width; x += pixelStep)
            {
                if (!detectedPixels[x, y])
                {
                    continue;
                }

                if (visited[x, y])
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

                    if (showDebugLog)
                    {
                        Debug.Log($"車両検出 {detectedCarCount}: Pixel({centerPixelX}, {centerPixelY}) → World {worldPosition}");
                    }
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
            Debug.Log($"明るいピクセル数: {brightPixelCount} / 検出車両数: {detectedCarCount}");
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

            // 周囲8方向を確認する
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

                    if (visited[nextX, nextY])
                    {
                        continue;
                    }

                    if (!detectedPixels[nextX, nextY])
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

    private bool TryConvertPixelToWorldPosition(int pixelX, int pixelY, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        Ray ray = detectionCamera.ScreenPointToRay(new Vector3(pixelX, pixelY, 0f));

        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float enter))
        {
            worldPosition = ray.GetPoint(enter);
            return true;
        }

        return false;
    }

    private void AddSmoothHeatAtWorldPosition(Vector3 worldPosition)
    {
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

        int centerX = Mathf.RoundToInt(normalizedX * (heatmapWidth - 1));
        int centerY = Mathf.RoundToInt(normalizedZ * (heatmapHeight - 1));

        for (int y = -heatRadius; y <= heatRadius; y++)
        {
            for (int x = -heatRadius; x <= heatRadius; x++)
            {
                int targetX = centerX + x;
                int targetY = centerY + y;

                if (targetX < 0 || targetX >= heatmapWidth || targetY < 0 || targetY >= heatmapHeight)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(x * x + y * y);

                if (distance > heatRadius)
                {
                    continue;
                }

                float normalizedDistance = distance / heatRadius;

                // ガウス風の滑らかな広がり
                float power = Mathf.Exp(-normalizedDistance * normalizedDistance * 4f);

                heatValues[targetX, targetY] += heatAddAmount * power;
                heatValues[targetX, targetY] = Mathf.Clamp01(heatValues[targetX, targetY]);
            }
        }
    }

    private void DecayHeatValues()
    {
        if (heatValues == null)
        {
            return;
        }

        for (int y = 0; y < heatmapHeight; y++)
        {
            for (int x = 0; x < heatmapWidth; x++)
            {
                heatValues[x, y] *= heatDecay;

                if (heatValues[x, y] < 0.005f)
                {
                    heatValues[x, y] = 0f;
                }
            }
        }
    }

    private void UpdateHeatmapTexture()
    {
        if (heatmapTexture == null || heatValues == null)
        {
            return;
        }

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
        value = Mathf.Clamp01(value);

        // 何も検出されていない場所も、薄い青で表示
        if (value <= 0.001f)
        {
            return new Color(0f, 0.2f, 1f, 0.01f);
        }

        Color c1 = new Color(0f, 0.25f, 1f, 1f);   // blue
        Color c2 = new Color(0f, 1f, 1f, 1f);      // cyan
        Color c3 = new Color(0f, 1f, 0.2f, 1f);    // green
        Color c4 = new Color(1f, 1f, 0f, 1f);      // yellow
        Color c5 = new Color(1f, 0.45f, 0f, 1f);   // orange
        Color c6 = new Color(1f, 0f, 0f, 1f);      // red

        Color color;

        if (value < 0.2f)
        {
            color = Color.Lerp(c1, c2, value / 0.2f);
        }
        else if (value < 0.4f)
        {
            color = Color.Lerp(c2, c3, (value - 0.2f) / 0.2f);
        }
        else if (value < 0.6f)
        {
            color = Color.Lerp(c3, c4, (value - 0.4f) / 0.2f);
        }
        else if (value < 0.8f)
        {
            color = Color.Lerp(c4, c5, (value - 0.6f) / 0.2f);
        }
        else
        {
            color = Color.Lerp(c5, c6, (value - 0.8f) / 0.2f);
        }

        // 値が高いほど濃くする
        color.a = Mathf.Lerp(0.08f, 0.75f, value);

        return color;
    }

    private void ClearHeatmapTexture()
    {
        for (int y = 0; y < heatmapHeight; y++)
        {
            for (int x = 0; x < heatmapWidth; x++)
            {
                heatValues[x, y] = 0f;
                heatmapTexture.SetPixel(x, y, new Color(0f, 0.15f, 1f, 0.05f));
            }
        }

        heatmapTexture.Apply();
    }

    [ContextMenu("Clear Heatmap")]
    public void ClearHeatmap()
    {
        if (!initialized)
        {
            return;
        }

        ClearHeatmapTexture();

        if (showDebugLog)
        {
            Debug.Log("ヒートマップをリセットしました。");
        }
    }

    [ContextMenu("Add Test Heat Center")]
    public void AddTestHeatCenter()
    {
        if (!initialized)
        {
            Initialize();
        }

        AddSmoothHeatAtWorldPosition(Vector3.zero);
        UpdateHeatmapTexture();

        if (showDebugLog)
        {
            Debug.Log("中央にテスト用ヒートを追加しました。");
        }
    }

    private struct BlobInfo
    {
        public int pixelCount;
        public float sumX;
        public float sumY;
    }

}
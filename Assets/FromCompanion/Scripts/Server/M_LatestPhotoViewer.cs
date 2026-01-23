using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class M_LatestPhotoViewer : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("RawImage in the UI that shows the latest uploaded photo.")]
    public RawImage targetImage;

    [Tooltip("Optional UI text element that shows the latest file name.")]
    public TMP_Text infoText;

    [Header("Auto Setup & Debug")]
    [Tooltip("If true and targetImage is not set, auto-find a RawImage in children.")]
    public bool autoFindRawImage = true;

    [Tooltip("If true, log extra details about texture + UI rect + file header.")]
    public bool verboseLogging = true;

    [Header("Retry Settings")]
    [Tooltip("Number of retries if LoadImage fails (to tolerate race conditions).")]
    public int maxLoadRetries = 3;

    [Tooltip("Delay between retries in seconds.")]
    public float retryDelaySeconds = 0.05f;

    [Header("Display / Fitting")]
    [Tooltip("If true, scale the RawImage to fit within the viewport while preserving aspect ratio.")]
    public bool scaleToFit = true;

    [Tooltip("If assigned, we will fit inside this rect. If null, we fit inside targetImage's parent rect.")]
    public RectTransform fitViewport;

    [Tooltip("If true, we will center the RawImage in the viewport.")]
    public bool centerInViewport = true;

    [Tooltip("If true, we allow upscaling small images to fill the viewport. If false, small images won't be enlarged.")]
    public bool allowUpscale = true;

    [Tooltip("Optional padding (pixels) inside the viewport when fitting.")]
    public Vector2 fitPadding = Vector2.zero;

    // Internal state
    private Texture2D currentTexture;
    private string lastDisplayedPath;
    private string currentlyLoadingPath;
    private Coroutine loadCoroutine;

    private void Awake()
    {
        if (targetImage == null && autoFindRawImage)
        {
            targetImage = GetComponentInChildren<RawImage>(true);
            if (targetImage != null)
                Debug.Log($"[M_LatestPhotoViewer] Auto-assigned RawImage: {targetImage.name}");
            else
                Debug.LogWarning("[M_LatestPhotoViewer] No RawImage found in children and targetImage not assigned.");
        }
    }

    private void Start()
    {
        // 🔴 HARD TEST: prove the RawImage renders *anything* at all.
        if (targetImage != null)
        {
            Texture2D testTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            testTex.SetPixel(0, 0, Color.red);
            testTex.SetPixel(1, 0, Color.red);
            testTex.SetPixel(0, 1, Color.red);
            testTex.SetPixel(1, 1, Color.red);
            testTex.Apply();

            targetImage.texture = testTex;
            targetImage.color = Color.white;

            currentTexture = testTex;

            // IMPORTANT: do NOT SetNativeSize() here; fit it instead
            ApplyFitAndCenter(testTex.width, testTex.height);

            Debug.Log("[M_LatestPhotoViewer] Test red texture applied in Start(). " +
                      "If you don't see a red square, the issue is UI layout, not image loading.");
        }
        else
        {
            Debug.LogWarning("[M_LatestPhotoViewer] No targetImage in Start().");
        }
    }

    private void OnDisable()
    {
        if (currentTexture != null)
        {
            Destroy(currentTexture);
            currentTexture = null;
        }

        if (loadCoroutine != null)
        {
            StopCoroutine(loadCoroutine);
            loadCoroutine = null;
        }
    }

    private void Update()
    {
        // Optional debug: press SPACE to reapply red test square in Editor
        if (Input.GetKeyDown(KeyCode.Space) && targetImage != null)
        {
            Debug.Log("[M_LatestPhotoViewer] Space pressed: reapplying red test texture.");
            Texture2D testTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            testTex.SetPixel(0, 0, Color.red);
            testTex.SetPixel(1, 0, Color.red);
            testTex.SetPixel(0, 1, Color.red);
            testTex.SetPixel(1, 1, Color.red);
            testTex.Apply();

            if (currentTexture != null)
                Destroy(currentTexture);

            currentTexture = testTex;
            targetImage.texture = currentTexture;
            targetImage.color = Color.white;

            ApplyFitAndCenter(testTex.width, testTex.height);
        }

        // 👀 Poll the last saved photo path from the HTTP server
        string latestPath = M_SimpleHttpServer.LastSavedPhotoPath;

        if (!string.IsNullOrEmpty(latestPath) && latestPath != lastDisplayedPath)
        {
            Debug.Log($"[M_LatestPhotoViewer] Detected new photo path: {latestPath}");
            lastDisplayedPath = latestPath;

            // Avoid spamming multiple coroutines for the same path
            if (currentlyLoadingPath != latestPath)
            {
                if (loadCoroutine != null)
                    StopCoroutine(loadCoroutine);

                loadCoroutine = StartCoroutine(LoadAndDisplayWithRetry(latestPath));
            }
        }
    }

    /// <summary>
    /// Fit/center the RawImage rect to the viewport while preserving aspect ratio.
    /// </summary>
    private void ApplyFitAndCenter(int texW, int texH)
    {
        if (targetImage == null)
            return;

        RectTransform imgRT = targetImage.rectTransform;

        RectTransform viewport = fitViewport;
        if (viewport == null)
            viewport = imgRT.parent as RectTransform;

        if (viewport == null)
        {
            if (verboseLogging)
                Debug.LogWarning("[M_LatestPhotoViewer] No viewport (fitViewport or parent). Skipping fit/center.");
            return;
        }

        // Ensure we have up-to-date layout rects
        Canvas.ForceUpdateCanvases();

        Rect vr = viewport.rect;

        float availW = Mathf.Max(1f, vr.width - fitPadding.x * 2f);
        float availH = Mathf.Max(1f, vr.height - fitPadding.y * 2f);

        // Preserve aspect ratio
        float texAspect = (texH == 0) ? 1f : (float)texW / texH;
        float viewAspect = availW / availH;

        float fitW, fitH;

        if (!scaleToFit)
        {
            // Use native size but still allow centering
            fitW = texW;
            fitH = texH;

            if (!allowUpscale)
            {
                // no-op here, because native is native; allowUpscale matters mainly in scaleToFit mode
            }
        }
        else
        {
            // Fit inside availW x availH
            if (texAspect >= viewAspect)
            {
                // limited by width
                fitW = availW;
                fitH = fitW / texAspect;
            }
            else
            {
                // limited by height
                fitH = availH;
                fitW = fitH * texAspect;
            }

            if (!allowUpscale)
            {
                // Don’t enlarge beyond native size
                fitW = Mathf.Min(fitW, texW);
                fitH = Mathf.Min(fitH, texH);
            }
        }

        // Center + size
        // Make it easy: anchor the image to the center of the viewport, then set sizeDelta.
        if (centerInViewport)
        {
            imgRT.anchorMin = new Vector2(0.5f, 0.5f);
            imgRT.anchorMax = new Vector2(0.5f, 0.5f);
            imgRT.pivot = new Vector2(0.5f, 0.5f);
            imgRT.anchoredPosition = Vector2.zero;
        }

        imgRT.sizeDelta = new Vector2(fitW, fitH);

        if (verboseLogging)
        {
            Debug.Log($"[M_LatestPhotoViewer] FitAndCenter: tex={texW}x{texH}, viewport={vr.width}x{vr.height}, " +
                      $"avail={availW}x{availH}, result={fitW}x{fitH}, allowUpscale={allowUpscale}");
        }
    }

    /// <summary>
    /// Coroutine that attempts to load the image multiple times
    /// to tolerate timing issues between file write and read.
    /// </summary>
    private IEnumerator LoadAndDisplayWithRetry(string path)
    {
        currentlyLoadingPath = path;

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[M_LatestPhotoViewer] Received empty path.");
            currentlyLoadingPath = null;
            yield break;
        }

        if (!File.Exists(path))
        {
            Debug.LogWarning("[M_LatestPhotoViewer] File does not exist: " + path);
            currentlyLoadingPath = null;
            yield break;
        }

        int attempts = 0;
        bool success = false;
        int maxAttempts = Mathf.Max(1, maxLoadRetries);

        while (attempts < maxAttempts && !success)
        {
            attempts++;

            byte[] bytes = null;
            bool readFailed = false;

            // ---- Attempt reading file bytes (no yields here) ----
            try
            {
                bytes = File.ReadAllBytes(path);
                Debug.Log($"[M_LatestPhotoViewer] Attempt {attempts}: Read {bytes.Length} bytes from {path}");

                if (verboseLogging && bytes.Length >= 3)
                {
                    string headerHex = $"{bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2}";
                    Debug.Log($"[M_LatestPhotoViewer] File header (first 3 bytes): {headerHex}");
                }
            }
            catch (System.Exception ex)
            {
                readFailed = true;
                Debug.LogError($"[M_LatestPhotoViewer] Exception while reading image (attempt {attempts}): {ex}");
            }

            if (!readFailed && bytes != null)
            {
                // ---- Attempt Texture2D.LoadImage (no yields here) ----
                Texture2D tempTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool loaded = false;

                try
                {
                    loaded = tempTex.LoadImage(bytes);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[M_LatestPhotoViewer] Exception during LoadImage (attempt {attempts}): {ex}");
                }

                Debug.Log($"[M_LatestPhotoViewer] Attempt {attempts}: LoadImage result={loaded}, tex={tempTex.width}x{tempTex.height}");

                if (loaded)
                {
                    // SUCCESS → swap texture safely
                    if (currentTexture != null)
                        Destroy(currentTexture);

                    currentTexture = tempTex;

                    if (targetImage != null)
                    {
                        targetImage.texture = currentTexture;
                        targetImage.color = Color.white;

                        ApplyFitAndCenter(currentTexture.width, currentTexture.height);

                        if (verboseLogging)
                        {
                            Rect r = targetImage.rectTransform.rect;
                            Debug.Log($"[M_LatestPhotoViewer] Applied texture to RawImage '{targetImage.name}', rect={r.width}x{r.height}");
                        }
                    }
                    else
                    {
                        Debug.LogError("[M_LatestPhotoViewer] targetImage is NULL. Cannot display texture.");
                    }

                    if (infoText != null)
                        infoText.text = $"Received: {Path.GetFileName(path)}";

                    Debug.Log("[M_LatestPhotoViewer] Displayed image: " + path);
                    success = true;
                }
                else
                {
                    // Failed to decode; clean up temp texture
                    Destroy(tempTex);
                }
            }

            // ---- Only yield OUTSIDE try/catch ----
            if (!success && attempts < maxAttempts)
            {
                Debug.LogWarning("[M_LatestPhotoViewer] LoadImage failed. Retrying...");
                yield return new WaitForSeconds(retryDelaySeconds);
            }
        }

        if (!success)
        {
            Debug.LogError($"[M_LatestPhotoViewer] Failed to load image after {attempts} attempts: {path}");
        }

        currentlyLoadingPath = null;
        loadCoroutine = null;
    }
}
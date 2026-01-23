using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Client-side photo picker + uploader.
/// - Optionally runs UDP discovery to find the headset/server (required for Quest reliability).
/// - Lets the user pick an image from gallery (NativeGallery).
/// - Downscales + encodes to JPG.
/// - Uploads to http://{discoveredIp}:{discoveredPort}/upload-photo via M_RestClient.
/// - Updates an optional local preview RawImage + filename label.
/// </summary>
public class M_PhotoUploader : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Client-side discovery component for finding the server automatically (UDP beacons).")]
    public M_NetworkDiscoveryClient discoveryClient;

    [Header("UI (Optional Preview)")]
    [Tooltip("RawImage that shows a preview of the selected (and resized) image on the client.")]
    public RawImage previewImage;

    [Tooltip("Optional label to show the selected file name.")]
    public TMP_Text fileNameText;

    [Header("Image Resize & Compression")]
    [Tooltip("Maximum width/height in pixels for the uploaded image. Larger images will be downscaled by NativeGallery.")]
    public int maxUploadDimension = 1024;

    [Range(1, 100)]
    [Tooltip("JPEG quality for upload (1 = worst/smallest, 100 = best/largest).")]
    public int jpegQuality = 70;

    [Tooltip("If true, logs original vs encoded byte sizes.")]
    public bool logSizeDetails = true;

    [Header("Discovery Behavior")]
    [Tooltip("If true, run UDP discovery before uploading when no server is set.")]
    public bool requireDiscoveryBeforeUpload = true;

    [Tooltip("If true, always run discovery before each upload (more robust on networks where IP can change).")]
    public bool alwaysRediscoverBeforeUpload = false;

    private M_RestClient restClient;

    private string currentSelectedPath;
    private Texture2D currentPreviewTexture;

    private void Awake()
    {
        restClient = M_RestClient.Instance;
    }

    /// <summary>
    /// Called by UI button.
    /// - If server URL is missing (or alwaysRediscoverBeforeUpload), attempt discovery.
    /// - Always lets user pick a photo (even if discovery fails), but may skip upload if no server set.
    /// </summary>
    public void OnPickAndUploadButton()
    {
        if (restClient == null)
            restClient = M_RestClient.Instance;

        if (restClient == null)
        {
            Debug.LogError("[M_PhotoUploader] RestClient instance not found.");
            return;
        }

        Debug.Log("[M_PhotoUploader] Pick+Upload pressed.");

        bool needDiscovery =
            alwaysRediscoverBeforeUpload ||
            string.IsNullOrEmpty(restClient.ServerBaseUrl);

        if (needDiscovery && discoveryClient != null)
            StartCoroutine(DiscoverThenPickAndUpload());
        else
            PickPhotoAndUpload();
    }

    private IEnumerator DiscoverThenPickAndUpload()
    {
        Debug.Log("[M_PhotoUploader] Starting discovery...");

        bool discovered = false;

        if (discoveryClient != null)
        {
            yield return StartCoroutine(discoveryClient.DiscoverServerCoroutine());

            if (!string.IsNullOrEmpty(discoveryClient.discoveredIp) &&
                discoveryClient.discoveredPort > 0)
            {
                // CRITICAL: Set server using UDP sender endpoint IP/port from discovery client
                restClient.SetServer(discoveryClient.discoveredIp, discoveryClient.discoveredPort);
                discovered = true;

                Debug.Log($"[M_PhotoUploader] Discovery OK -> {discoveryClient.discoveredIp}:{discoveryClient.discoveredPort}");
            }
        }

        if (!discovered)
        {
            Debug.LogWarning("[M_PhotoUploader] Discovery failed.");
        }

        // Always allow picking a photo.
        PickPhotoAndUpload();
    }

    private void PickPhotoAndUpload()
    {
        Debug.Log("[M_PhotoUploader] Opening gallery...");

        if (NativeGallery.IsMediaPickerBusy())
        {
            Debug.LogWarning("[M_PhotoUploader] Media picker is busy; ignoring.");
            return;
        }

        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("[M_PhotoUploader] User cancelled image selection.");
                return;
            }

            currentSelectedPath = path;
            Debug.Log("[M_PhotoUploader] Selected: " + path);

            // ---- Load + downscale via NativeGallery (keep readable for EncodeToJPG) ----
            int maxSize = Mathf.Max(256, maxUploadDimension);

            Texture2D tex = NativeGallery.LoadImageAtPath(
                path,
                maxSize,
                markTextureNonReadable: false,
                generateMipmaps: false
            );

            if (tex == null)
            {
                Debug.LogWarning("[M_PhotoUploader] NativeGallery.LoadImageAtPath returned null. Falling back to File.ReadAllBytes.");

                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(bytes, markNonReadable: false))
                    {
                        Debug.LogError("[M_PhotoUploader] Fallback Texture2D.LoadImage failed.");
                        Destroy(tex);
                        return;
                    }

                    Debug.Log($"[M_PhotoUploader] Fallback load OK. tex={tex.width}x{tex.height}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[M_PhotoUploader] Fallback load exception: " + ex);
                    return;
                }
            }

            Debug.Log($"[M_PhotoUploader] Loaded tex={tex.width}x{tex.height}");

            // ---- Update local preview UI ----
            UpdateClientPreviewUI(path, tex);

            // ---- Gate upload on discovery if required ----
            if (string.IsNullOrEmpty(restClient.ServerBaseUrl))
            {
                if (requireDiscoveryBeforeUpload)
                {
                    Debug.LogError("[M_PhotoUploader] No server discovered; upload skipped (requireDiscoveryBeforeUpload=true).");
                    return;
                }
                else
                {
                    Debug.LogWarning("[M_PhotoUploader] No server URL; attempting upload anyway will fail.");
                }
            }

            // ---- Encode to JPEG ----
            int q = Mathf.Clamp(jpegQuality, 1, 100);
            byte[] jpgBytes;

            try
            {
                jpgBytes = tex.EncodeToJPG(q);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[M_PhotoUploader] EncodeToJPG failed: " + ex);
                return;
            }

            if (logSizeDetails)
            {
                Debug.Log($"[M_PhotoUploader] Encoded JPEG (q={q}) size={jpgBytes.Length} bytes.");
            }

            // ---- Upload ----
            StartCoroutine(restClient.UploadPhoto(jpgBytes));

        }, "Select a photo to upload", "image/*");
    }

    /// <summary>
    /// Updates filename label and RawImage preview.
    /// Keeps the Texture2D alive to avoid preview disappearing.
    /// </summary>
    private void UpdateClientPreviewUI(string path, Texture2D previewTex)
    {
        if (fileNameText != null)
        {
            fileNameText.text = $"Selected: {Path.GetFileName(path)}";
        }

        if (previewImage != null && previewTex != null)
        {
            if (currentPreviewTexture != null && currentPreviewTexture != previewTex)
                Destroy(currentPreviewTexture);

            currentPreviewTexture = previewTex;

            previewImage.texture = currentPreviewTexture;
            previewImage.color = Color.white;

            // If you prefer fixed rect sizing, remove this.
            previewImage.SetNativeSize();

            if (logSizeDetails)
                Debug.Log($"[M_PhotoUploader] Preview updated: {currentPreviewTexture.width}x{currentPreviewTexture.height}");
        }
    }

    private void OnDestroy()
    {
        if (currentPreviewTexture != null)
        {
            Destroy(currentPreviewTexture);
            currentPreviewTexture = null;
        }
    }
}
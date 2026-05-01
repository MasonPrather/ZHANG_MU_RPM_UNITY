using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Displays a selected Quest-local photo on either:
/// - a UI RawImage
/// - a Renderer material slot
///
/// Replaces the currently displayed texture each time a new item is selected.
/// </summary>
public class M_QuestPhotoDisplay : MonoBehaviour
{
    [Header("UI Target (Optional)")]
    [Tooltip("Optional RawImage for showing the selected photo in UI.")]
    public RawImage targetRawImage;

    [Tooltip("Optional bounds rect used to fit the RawImage within a panel. Defaults to the RawImage's parent.")]
    public RectTransform rawImageFitBounds;

    [Header("Renderer Target (Optional)")]
    [Tooltip("Optional Renderer for showing the selected photo on a material.")]
    public Renderer targetRenderer;

    [Tooltip("Material slot index on the target renderer.")]
    public int materialIndex = 0;

    [Tooltip("Optional albedo property name override.")]
    public string albedoProperty = "_BaseMap";

    [Header("Info UI (Optional)")]
    [Tooltip("Optional label for showing the selected file name.")]
    public TMP_Text fileNameText;

    [Tooltip("Optional label for showing status / debug info.")]
    public TMP_Text statusText;

    [Header("Display Behavior")]
    [Tooltip("If greater than 0, selected photos will be downscaled to this maximum dimension before display.")]
    public int maxDisplayDimension = 2048;

    [Tooltip("If true, keep the displayed texture readable.")]
    public bool keepTextureReadable = false;

    [Tooltip("If true, set RawImage to native size after applying texture.")]
    public bool useNativeSizeForRawImage = false;

    [Tooltip("If true, fit the RawImage inside the panel bounds while preserving aspect ratio.")]
    public bool fitRawImageToBounds = true;

    [Tooltip("If true, scale the RawImage to exactly match the usable bounds height after margins are applied. This keeps the viewer at a fixed display height under the header area.")]
    public bool matchRawImageToBoundsHeight = true;

    [Tooltip("If true, height-fit images are clamped to the bounds width so very wide uploads stay inside the panel.")]
    public bool clampHeightFitToBoundsWidth = true;

    [Tooltip("If true, recalculate the RawImage size on the next frame after Unity layout has settled.")]
    public bool refreshRawImageLayoutAfterFrame = true;

    [Tooltip("Margins inside the bounds rect in the order Left, Right, Top, Bottom.")]
    public Vector4 rawImageFitMargins = Vector4.zero;

    [Tooltip("Optional fallback texture shown when nothing is selected.")]
    public Texture fallbackTexture;

    [Header("Debug")]
    [Tooltip("If true, log detailed display information.")]
    public bool verboseLogging = true;

    private Texture2D _currentTexture;
    private Material _runtimeMaterial;
    private string _currentFileName;
    private Coroutine _layoutRefreshCoroutine;

    private void Start()
    {
        ApplyFallback();
    }

    /// <summary>
    /// Clears the current photo and restores fallback state.
    /// </summary>
    public void ClearDisplay()
    {
        ReleaseCurrentTexture();
        _currentFileName = string.Empty;
        ApplyFallback();

        if (fileNameText != null)
            fileNameText.text = "No photo selected";

        if (statusText != null)
            statusText.text = "Idle";

        if (verboseLogging)
            Debug.Log("[M_QuestPhotoDisplay] Display cleared.");
    }

    /// <summary>
    /// Loads and displays a full-size image using the provided Android bridge.
    /// </summary>
    public void DisplayPhoto(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        if (bridge == null)
        {
            Debug.LogError("[M_QuestPhotoDisplay] DisplayPhoto failed: bridge is null.");
            return;
        }

        if (item == null || string.IsNullOrEmpty(item.filePath))
        {
            Debug.LogWarning("[M_QuestPhotoDisplay] DisplayPhoto failed: invalid item/path.");
            return;
        }

        StartCoroutine(DisplayPhotoCoroutine(bridge, item));
    }

    /// <summary>
    /// Applies a texture that was already loaded elsewhere, such as a synchronized network upload.
    /// </summary>
    public void DisplayTexture(Texture2D texture, string fileName = "")
    {
        if (texture == null)
        {
            Debug.LogWarning("[M_QuestPhotoDisplay] DisplayTexture failed: texture is null.");
            return;
        }

        ReleaseCurrentTexture();
        _currentTexture = texture;
        _currentFileName = fileName ?? string.Empty;

        ApplyTexture(_currentTexture);

        if (fileNameText != null)
            fileNameText.text = string.IsNullOrWhiteSpace(_currentFileName) ? "Uploaded photo" : _currentFileName;

        if (statusText != null)
            statusText.text = $"{_currentTexture.width} x {_currentTexture.height}";

        if (verboseLogging)
            Debug.Log($"[M_QuestPhotoDisplay] Displayed provided texture: {_currentFileName} ({_currentTexture.width}x{_currentTexture.height})");
    }

    private IEnumerator DisplayPhotoCoroutine(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        if (statusText != null)
            statusText.text = "Loading photo...";

        yield return null;

        Texture2D tex = bridge.LoadFullTexture(item, maxDisplayDimension, markNonReadable: !keepTextureReadable);

        if (tex == null)
        {
            if (statusText != null)
                statusText.text = "Failed to load photo";

            Debug.LogWarning($"[M_QuestPhotoDisplay] Failed to load selected photo: {item.fileName}");
            yield break;
        }

        DisplayTexture(tex, item.fileName);
    }

    private void ApplyFallback()
    {
        if (targetRawImage != null)
        {
            targetRawImage.texture = fallbackTexture;
            targetRawImage.color = fallbackTexture != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        if (targetRenderer != null)
        {
            Material mat = GetOrCreateRuntimeMaterial();
            if (mat != null)
            {
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", fallbackTexture);

                if (!string.IsNullOrEmpty(albedoProperty) && mat.HasProperty(albedoProperty))
                    mat.SetTexture(albedoProperty, fallbackTexture);
            }
        }
    }

    private void ApplyTexture(Texture texture)
    {
        if (targetRawImage != null)
        {
            targetRawImage.texture = texture;
            targetRawImage.color = Color.white;

            if (fitRawImageToBounds)
            {
                UpdateRawImageLayout(texture);

                if (refreshRawImageLayoutAfterFrame && isActiveAndEnabled)
                {
                    if (_layoutRefreshCoroutine != null)
                        StopCoroutine(_layoutRefreshCoroutine);

                    _layoutRefreshCoroutine = StartCoroutine(RefreshRawImageLayoutNextFrame(texture));
                }
            }
            else if (useNativeSizeForRawImage)
            {
                targetRawImage.SetNativeSize();
            }
        }

        if (targetRenderer != null)
        {
            Material mat = GetOrCreateRuntimeMaterial();
            if (mat != null)
            {
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", texture);

                if (!string.IsNullOrEmpty(albedoProperty) && mat.HasProperty(albedoProperty))
                    mat.SetTexture(albedoProperty, texture);

                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", Color.white);
            }
        }
    }

    private Material GetOrCreateRuntimeMaterial()
    {
        if (targetRenderer == null)
            return null;

        Material[] mats = targetRenderer.materials;
        if (mats == null || mats.Length == 0)
        {
            Debug.LogWarning("[M_QuestPhotoDisplay] Target renderer has no materials.");
            return null;
        }

        int slot = Mathf.Clamp(materialIndex, 0, mats.Length - 1);

        if (_runtimeMaterial == null)
        {
            _runtimeMaterial = new Material(mats[slot]);
            _runtimeMaterial.name = mats[slot].name + "_Runtime";

            mats[slot] = _runtimeMaterial;
            targetRenderer.materials = mats;
        }

        return _runtimeMaterial;
    }

    private void ReleaseCurrentTexture()
    {
        if (_currentTexture != null)
        {
            Destroy(_currentTexture);
            _currentTexture = null;
        }
    }

    private void UpdateRawImageLayout(Texture texture)
    {
        if (targetRawImage == null || texture == null)
            return;

        RectTransform rawRect = targetRawImage.rectTransform;
        if (rawRect == null)
            return;

        RectTransform boundsRect = rawImageFitBounds != null ? rawImageFitBounds : rawRect.parent as RectTransform;
        if (boundsRect == null)
        {
            if (useNativeSizeForRawImage)
                targetRawImage.SetNativeSize();
            return;
        }

        float availableWidth = Mathf.Max(1f, boundsRect.rect.width - rawImageFitMargins.x - rawImageFitMargins.y);
        float availableHeight = Mathf.Max(1f, boundsRect.rect.height - rawImageFitMargins.z - rawImageFitMargins.w);
        float textureWidth = Mathf.Max(1f, texture.width);
        float textureHeight = Mathf.Max(1f, texture.height);
        float heightScale = availableHeight / textureHeight;
        float fitScale = Mathf.Min(availableWidth / textureWidth, heightScale);
        float scale = matchRawImageToBoundsHeight ? heightScale : fitScale;

        if (matchRawImageToBoundsHeight && clampHeightFitToBoundsWidth && textureWidth * scale > availableWidth)
            scale = fitScale;

        rawRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textureWidth * scale);
        rawRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textureHeight * scale);
        rawRect.anchoredPosition = new Vector2(
            (rawImageFitMargins.x - rawImageFitMargins.y) * 0.5f,
            (rawImageFitMargins.w - rawImageFitMargins.z) * 0.5f);

        if (verboseLogging)
        {
            Debug.Log($"[M_QuestPhotoDisplay] RawImage layout -> targetHeight={availableHeight:F1}, appliedSize={textureWidth * scale:F1}x{textureHeight * scale:F1}, fixedHeight={matchRawImageToBoundsHeight}");
        }
    }

    private IEnumerator RefreshRawImageLayoutNextFrame(Texture texture)
    {
        yield return null;

        Canvas.ForceUpdateCanvases();

        if (targetRawImage != null && targetRawImage.texture == texture)
            UpdateRawImageLayout(texture);

        _layoutRefreshCoroutine = null;
    }

    private void OnDestroy()
    {
        if (_layoutRefreshCoroutine != null)
        {
            StopCoroutine(_layoutRefreshCoroutine);
            _layoutRefreshCoroutine = null;
        }

        ReleaseCurrentTexture();

        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }
}

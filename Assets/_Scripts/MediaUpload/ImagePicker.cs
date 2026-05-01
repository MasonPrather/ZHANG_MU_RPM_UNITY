using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// User-driven image picker for Quest/Android via NativeGallery.
/// Opens the OS gallery picker, loads the selected image as a readable Texture2D,
/// applies it locally, and prepares encoded bytes for a future upload/sync layer.
/// </summary>
public class ImagePicker : MonoBehaviour
{
    public enum UploadEncoding
    {
        PNG,
        JPG
    }

    [Serializable]
    public sealed class TextureEvent : UnityEvent<Texture2D>
    {
    }

    [Serializable]
    public sealed class StringEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public sealed class PreparedImage
    {
        public string sourcePath;
        public string fileName;
        public string mimeType;
        public string sourceMimeType;
        public string cachePath;
        public int width;
        public int height;
        public byte[] bytes;
    }

    [Header("Display Targets")]
    [Tooltip("Optional renderer that receives the selected image.")]
    public Renderer targetRenderer;

    [Tooltip("Optional RawImage preview that receives the selected image.")]
    public RawImage previewImage;

    [Tooltip("Optional project display helper. If assigned, it owns the displayed texture lifecycle.")]
    public M_QuestPhotoDisplay photoDisplay;

    [Tooltip("Preferred texture property for renderer materials.")]
    public string textureProperty = "_BaseMap";

    [Tooltip("If true, clone the renderer material before applying user-selected media.")]
    public bool useRuntimeMaterialInstance = true;

    [Header("Picker")]
    [Tooltip("Maximum width/height loaded from the picked image. Larger images are downscaled by NativeGallery.")]
    public int maxImageDimension = 2048;

    [Tooltip("Android picker title.")]
    public string pickerTitle = "Select an image";

    [Tooltip("Android MIME filter.")]
    public string mimeFilter = "image/*";

    [Tooltip("Generate mipmaps for the preview/display texture.")]
    public bool generateMipmaps = false;

    [Header("Upload Preparation")]
    [Tooltip("Encoding used for the upload/sync payload.")]
    public UploadEncoding uploadEncoding = UploadEncoding.PNG;

    [Range(1, 100)]
    [Tooltip("JPEG quality when Upload Encoding is JPG.")]
    public int jpegQuality = 85;

    [Tooltip("If true, write the encoded payload to app-owned persistent storage.")]
    public bool cachePreparedImage = true;

    [Tooltip("Folder under persistentDataPath used for cached selected images.")]
    public string cacheFolderName = "PickedImages";

    [Header("Optional UI")]
    public TMP_Text statusText;
    public TMP_Text fileNameText;

    [Header("Events")]
    public TextureEvent textureLoaded = new TextureEvent();
    public StringEvent statusChanged = new StringEvent();
    public StringEvent uploadPayloadPrepared = new StringEvent();

    public PreparedImage CurrentImage { get; private set; }
    public Texture2D CurrentTexture { get; private set; }
    public event Action<PreparedImage> ImagePrepared;

    private Material runtimeMaterial;
    private bool pickerInProgress;
    private bool currentTextureOwnedByPhotoDisplay;

    public void PickImage()
    {
        if (pickerInProgress || NativeGallery.IsMediaPickerBusy())
        {
            SetStatus("Image picker is already open.");
            return;
        }

        pickerInProgress = true;
        SetStatus("Opening image picker...");

        NativeGallery.GetImageFromGallery((path) =>
        {
            pickerInProgress = false;

            if (string.IsNullOrEmpty(path))
            {
                SetStatus("No image selected.");
                return;
            }

            LoadSelectedImage(path);
        }, pickerTitle, string.IsNullOrWhiteSpace(mimeFilter) ? "image/*" : mimeFilter);
    }

    public bool TryGetPreparedImage(out PreparedImage image)
    {
        image = CurrentImage;
        return image != null && image.bytes != null && image.bytes.Length > 0;
    }

    /// <summary>
    /// UI hook for a separate confirmation/upload button. It republishes the prepared
    /// payload to code subscribers without implementing a backend or NGO upload here.
    /// </summary>
    public void PrepareCurrentImageForUpload()
    {
        if (!TryGetPreparedImage(out PreparedImage image))
        {
            SetStatus("Select an image before uploading.");
            return;
        }

        ImagePrepared?.Invoke(image);
        uploadPayloadPrepared?.Invoke(image.cachePath);
        SetStatus($"Prepared {image.fileName} for upload ({FormatByteCount(image.bytes.Length)}).");
    }

    public void Clear()
    {
        ReleaseCurrentTexture();
        CurrentImage = null;

        if (photoDisplay != null)
            photoDisplay.ClearDisplay();

        if (previewImage != null)
        {
            previewImage.texture = null;
            previewImage.color = new Color(1f, 1f, 1f, 0f);
        }

        if (fileNameText != null)
            fileNameText.text = string.Empty;

        SetStatus("No image selected.");
    }

    private void LoadSelectedImage(string path)
    {
        NativeGallery.ImageProperties properties = GetImageProperties(path);
        int maxSize = Mathf.Max(256, maxImageDimension);
        Texture2D texture;

        try
        {
            texture = NativeGallery.LoadImageAtPath(
                path,
                maxSize,
                markTextureNonReadable: false,
                generateMipmaps: generateMipmaps
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ImagePicker] Failed to load selected image at '{path}': {ex}");
            SetStatus("Failed to load selected image.");
            return;
        }

        if (texture == null)
        {
            Debug.LogError($"[ImagePicker] NativeGallery.LoadImageAtPath returned null for '{path}'.");
            SetStatus("Failed to load selected image.");
            return;
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 4;

        ReplaceCurrentTexture(texture);

        string displayName = BuildDisplayName(path);
        ApplyTexture(texture, displayName);
        HandleUpload(texture, path, displayName, properties.mimeType);
    }

    private NativeGallery.ImageProperties GetImageProperties(string path)
    {
        try
        {
            return NativeGallery.GetImageProperties(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ImagePicker] Could not read image properties for '{path}': {ex.Message}");
            return default;
        }
    }

    private void ApplyTexture(Texture2D texture, string displayName)
    {
        if (photoDisplay != null)
        {
            photoDisplay.DisplayTexture(texture, displayName);
            currentTextureOwnedByPhotoDisplay = true;
        }

        if (previewImage != null)
        {
            previewImage.texture = texture;
            previewImage.color = Color.white;
        }

        if (targetRenderer != null)
            ApplyRendererTexture(texture);

        if (fileNameText != null)
            fileNameText.text = displayName;

        textureLoaded?.Invoke(texture);
    }

    private void ApplyRendererTexture(Texture texture)
    {
        Material material = GetTargetMaterial();
        if (material == null)
            return;

        if (!string.IsNullOrWhiteSpace(textureProperty) && material.HasProperty(textureProperty))
            material.SetTexture(textureProperty, texture);

        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);

        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", texture);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
    }

    private Material GetTargetMaterial()
    {
        if (targetRenderer == null)
            return null;

        if (!useRuntimeMaterialInstance)
            return targetRenderer.material;

        if (runtimeMaterial == null)
        {
            Material sourceMaterial = targetRenderer.sharedMaterial != null
                ? targetRenderer.sharedMaterial
                : targetRenderer.material;

            if (sourceMaterial == null)
            {
                Debug.LogWarning("[ImagePicker] Target renderer has no material.");
                return null;
            }

            runtimeMaterial = new Material(sourceMaterial)
            {
                name = sourceMaterial.name + "_ImagePickerRuntime"
            };

            targetRenderer.material = runtimeMaterial;
        }

        return runtimeMaterial;
    }

    private void HandleUpload(Texture2D texture, string sourcePath, string displayName, string sourceMimeType)
    {
        byte[] encodedBytes;
        string mimeType;
        string extension;

        try
        {
            if (uploadEncoding == UploadEncoding.JPG)
            {
                encodedBytes = ImageConversion.EncodeToJPG(texture, Mathf.Clamp(jpegQuality, 1, 100));
                mimeType = "image/jpeg";
                extension = ".jpg";
            }
            else
            {
                encodedBytes = ImageConversion.EncodeToPNG(texture);
                mimeType = "image/png";
                extension = ".png";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ImagePicker] Failed to encode selected image for upload: {ex}");
            SetStatus("Image loaded, but upload encoding failed.");
            return;
        }

        if (encodedBytes == null || encodedBytes.Length == 0)
        {
            Debug.LogError("[ImagePicker] Encoded image payload was empty.");
            SetStatus("Image loaded, but upload payload is empty.");
            return;
        }

        string uploadFileName = BuildUploadFileName(displayName, extension);
        string cachePath = cachePreparedImage ? CacheImage(uploadFileName, encodedBytes) : string.Empty;

        CurrentImage = new PreparedImage
        {
            sourcePath = sourcePath,
            fileName = uploadFileName,
            mimeType = mimeType,
            sourceMimeType = sourceMimeType,
            cachePath = cachePath,
            width = texture.width,
            height = texture.height,
            bytes = encodedBytes
        };

        ImagePrepared?.Invoke(CurrentImage);
        uploadPayloadPrepared?.Invoke(cachePath);

        string sizeLabel = FormatByteCount(encodedBytes.Length);
        SetStatus($"Image ready: {texture.width} x {texture.height}, {sizeLabel}");
    }

    private string CacheImage(string fileName, byte[] encodedBytes)
    {
        try
        {
            string folderName = string.IsNullOrWhiteSpace(cacheFolderName) ? "PickedImages" : cacheFolderName.Trim();
            string cacheDirectory = Path.Combine(Application.persistentDataPath, folderName);
            Directory.CreateDirectory(cacheDirectory);

            string cachePath = CreateUniqueCachePath(cacheDirectory, fileName);
            File.WriteAllBytes(cachePath, encodedBytes);
            return cachePath;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ImagePicker] Failed to cache selected image: {ex.Message}");
            return string.Empty;
        }
    }

    private string CreateUniqueCachePath(string directory, string fileName)
    {
        string safeFileName = SanitizeFileName(fileName);
        string baseName = Path.GetFileNameWithoutExtension(safeFileName);
        string extension = Path.GetExtension(safeFileName);

        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "picked-image";

        if (string.IsNullOrWhiteSpace(extension))
            extension = uploadEncoding == UploadEncoding.JPG ? ".jpg" : ".png";

        string candidate = Path.Combine(directory, baseName + extension);
        int suffix = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}-{suffix}{extension}");
            suffix++;
        }

        return candidate;
    }

    private string BuildDisplayName(string path)
    {
        string fileName = string.Empty;

        if (!string.IsNullOrWhiteSpace(path))
            fileName = Path.GetFileName(path);

        return string.IsNullOrWhiteSpace(fileName) ? "picked-image" : fileName;
    }

    private string BuildUploadFileName(string displayName, string extension)
    {
        string safeDisplayName = SanitizeFileName(displayName);
        string baseName = Path.GetFileNameWithoutExtension(safeDisplayName);

        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "picked-image";

        return baseName + extension;
    }

    private string SanitizeFileName(string value)
    {
        string safeValue = string.IsNullOrWhiteSpace(value) ? "picked-image" : value.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();

        for (int i = 0; i < invalidChars.Length; i++)
            safeValue = safeValue.Replace(invalidChars[i], '_');

        return safeValue;
    }

    private string FormatByteCount(int byteCount)
    {
        const float kib = 1024f;
        const float mib = kib * 1024f;

        if (byteCount >= mib)
            return $"{byteCount / mib:0.0} MB";

        if (byteCount >= kib)
            return $"{byteCount / kib:0.0} KB";

        return $"{byteCount} bytes";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        statusChanged?.Invoke(message);
        Debug.Log("[ImagePicker] " + message);
    }

    private void ReplaceCurrentTexture(Texture2D texture)
    {
        ReleaseCurrentTexture();
        CurrentTexture = texture;
        currentTextureOwnedByPhotoDisplay = false;
    }

    private void ReleaseCurrentTexture()
    {
        if (CurrentTexture != null && !currentTextureOwnedByPhotoDisplay)
            Destroy(CurrentTexture);

        CurrentTexture = null;
        currentTextureOwnedByPhotoDisplay = false;
    }

    private void OnDestroy()
    {
        ReleaseCurrentTexture();

        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }
}

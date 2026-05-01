using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;

/// <summary>
/// Bridges phone uploads saved by M_SimpleHttpServer into the MediaUpload display.
/// </summary>
public class M_PhoneUploadToDisplay : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Existing MediaUpload display target. If empty, the first active M_QuestPhotoDisplay is used.")]
    public M_QuestPhotoDisplay photoDisplay;

    [Tooltip("If true, find the MediaUpload display at runtime when no reference is assigned.")]
    public bool autoFindPhotoDisplay = true;

    [Tooltip("Optional status label. If empty, the display status text is used.")]
    public TMP_Text statusText;

    [Tooltip("If true, find LatestPhotoInfo or the display status label at runtime when no reference is assigned.")]
    public bool autoFindStatusText = true;

    [Tooltip("Existing gallery controller. If assigned, phone uploads are added to the browseable gallery list.")]
    public M_QuestGalleryController galleryController;

    [Tooltip("If true, find the gallery controller at runtime when no reference is assigned.")]
    public bool autoFindGalleryController = true;

    [Tooltip("If true, refresh the gallery browser when a phone upload arrives.")]
    public bool refreshGalleryOnPhoneUpload = true;

    [Header("Network Sync")]
    [Tooltip("Existing shared-media network bridge. If empty, it is found at runtime.")]
    public M_NetworkedPhotoSync networkedPhotoSync;

    [Tooltip("If true, find the shared-media network bridge at runtime when no reference is assigned.")]
    public bool autoFindNetworkedPhotoSync = true;

    [Tooltip("If true, phone uploads are broadcast to connected Unity clients after local display.")]
    public bool broadcastUploadsToConnectedUsers = true;

    [Header("Loading")]
    [Tooltip("Number of short retries if the server reports a path before the file is readable.")]
    public int maxLoadRetries = 5;

    [Tooltip("Delay between file-read retries.")]
    public float retryDelaySeconds = 0.05f;

    [Tooltip("If true, keep uploaded textures CPU-readable after loading.")]
    public bool keepTextureReadable = false;

    [Header("Debug")]
    [Tooltip("If true, log when phone uploads are loaded into the scene.")]
    public bool verboseLogging = true;

    private string _lastDisplayedPath;
    private Coroutine _loadCoroutine;

    private void Start()
    {
        ResolveReferences();
    }

    private void Update()
    {
        string path = M_SimpleHttpServer.LastSavedPhotoPath;
        if (string.IsNullOrEmpty(path) || string.Equals(path, _lastDisplayedPath, StringComparison.Ordinal))
            return;

        if (_loadCoroutine != null)
            StopCoroutine(_loadCoroutine);

        _loadCoroutine = StartCoroutine(LoadAndDisplay(path));
    }

    private IEnumerator LoadAndDisplay(string path)
    {
        _lastDisplayedPath = path;
        ResolveReferences();

        if (statusText != null)
            statusText.text = "Loading phone photo...";

        byte[] bytes = null;
        Exception readException = null;

        int attempts = Mathf.Max(0, maxLoadRetries) + 1;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            readException = null;

            try
            {
                if (File.Exists(path))
                    bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                readException = ex;
            }

            if (bytes != null && bytes.Length > 0)
                break;

            if (attempt < attempts - 1)
                yield return new WaitForSeconds(Mathf.Max(0.01f, retryDelaySeconds));
        }

        if (bytes == null || bytes.Length == 0)
        {
            if (statusText != null)
                statusText.text = "Phone photo could not be loaded.";

            if (readException != null)
                Debug.LogWarning($"[M_PhoneUploadToDisplay] Failed to read upload '{path}': {readException.Message}");
            else
                Debug.LogWarning($"[M_PhoneUploadToDisplay] Uploaded file was missing or empty: {path}");

            _loadCoroutine = null;
            yield break;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool loaded = false;
        Exception loadException = null;

        try
        {
            loaded = texture.LoadImage(bytes, !keepTextureReadable);
        }
        catch (Exception ex)
        {
            loadException = ex;
        }

        if (!loaded)
        {
            Destroy(texture);

            if (statusText != null)
                statusText.text = "Phone upload was not a valid image.";

            if (loadException != null)
                Debug.LogWarning($"[M_PhoneUploadToDisplay] Failed to decode upload '{path}': {loadException.Message}");
            else
                Debug.LogWarning($"[M_PhoneUploadToDisplay] Failed to decode upload '{path}'.");

            _loadCoroutine = null;
            yield break;
        }

        texture.name = Path.GetFileNameWithoutExtension(path);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Trilinear;

        if (photoDisplay != null)
        {
            string fileName = Path.GetFileName(path);
            photoDisplay.DisplayTexture(texture, fileName);
            RefreshGalleryForUpload(path);

            if (broadcastUploadsToConnectedUsers)
                BroadcastPhoneUpload(fileName, bytes);

            if (verboseLogging)
                Debug.Log($"[M_PhoneUploadToDisplay] Displayed phone upload: {path}");
        }
        else
        {
            Destroy(texture);

            if (statusText != null)
                statusText.text = "Phone upload saved, but no display target was found.";

            Debug.LogWarning("[M_PhoneUploadToDisplay] No M_QuestPhotoDisplay found in the scene.");
        }

        _loadCoroutine = null;
    }

    private void ResolveReferences()
    {
        if (photoDisplay == null && autoFindPhotoDisplay)
            photoDisplay = UnityEngine.Object.FindObjectOfType<M_QuestPhotoDisplay>();

        if (statusText == null && autoFindStatusText)
        {
            if (photoDisplay != null && photoDisplay.statusText != null)
                statusText = photoDisplay.statusText;
            else
                statusText = FindTextByName("LatestPhotoInfo");
        }

        if (networkedPhotoSync == null && autoFindNetworkedPhotoSync)
        {
            if (photoDisplay != null)
                networkedPhotoSync = photoDisplay.GetComponent<M_NetworkedPhotoSync>();

            if (networkedPhotoSync == null)
                networkedPhotoSync = UnityEngine.Object.FindObjectOfType<M_NetworkedPhotoSync>();
        }

        if (galleryController == null && autoFindGalleryController)
            galleryController = UnityEngine.Object.FindObjectOfType<M_QuestGalleryController>();
    }

    private void BroadcastPhoneUpload(string fileName, byte[] bytes)
    {
        ResolveReferences();

        if (networkedPhotoSync == null)
        {
            Debug.LogWarning("[M_PhoneUploadToDisplay] Phone upload displayed locally, but no M_NetworkedPhotoSync was found for broadcast.");
            return;
        }

        networkedPhotoSync.BroadcastImageBytes(fileName, bytes);
    }

    private void RefreshGalleryForUpload(string path)
    {
        ResolveReferences();

        if (!refreshGalleryOnPhoneUpload || galleryController == null)
            return;

        galleryController.NotifyExternalImageImported(path, "Phone Upload");
    }

    private static TMP_Text FindTextByName(string namePart)
    {
        if (string.IsNullOrEmpty(namePart))
            return null;

        TMP_Text[] texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
                continue;

            if (text.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                return text;
        }

        return null;
    }
}

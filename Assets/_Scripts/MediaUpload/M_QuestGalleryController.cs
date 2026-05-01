using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main controller for the media upload UI.
/// Responsibilities:
/// - Route user-driven imports through the NativeGallery-backed ImagePicker.
/// - Keep the older tile browser available for manual refresh/debug workflows.
/// - Route selected image to the large display
/// </summary>
public class M_QuestGalleryController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Legacy Android/Quest helper used only by the manual gallery browser fallback.")]
    public M_QuestGalleryAndroidBridge androidBridge;

    [Tooltip("Parent transform that receives spawned tile prefabs.")]
    public Transform tileParent;

    [Tooltip("Tile prefab used for each gallery image.")]
    public M_QuestGalleryTile tilePrefab;

    [Tooltip("Photo display target for the selected image.")]
    public M_QuestPhotoDisplay photoDisplay;

    [Tooltip("NativeGallery-backed picker used for user-driven imports. Preferred on Quest/Android.")]
    public ImagePicker imagePicker;

    [Header("UI (Optional)")]
    [Tooltip("Optional status label.")]
    public TMP_Text statusText;

    [Tooltip("Optional empty-state label.")]
    public TMP_Text emptyStateText;

    [Header("Load Behavior")]
    [Tooltip("If true, load the legacy tile browser automatically on Start. Keep this off for picker-first Quest media access.")]
    public bool autoLoadOnStart = false;

    [Tooltip("Number of items to instantiate immediately.")]
    public int initialVisibleCount = 24;

    [Tooltip("Additional items to add when Load More is called.")]
    public int loadMoreCount = 24;

    [Tooltip("If true, auto-select the first gallery item after load.")]
    public bool autoSelectFirstItem = true;

    [Tooltip("If true, selecting a tile immediately uploads it to the display.")]
    public bool uploadOnSelection = false;

    [Tooltip("If true, load thumbnails progressively over multiple frames.")]
    public bool progressiveThumbnailLoading = true;

    [Header("Imports")]
    [Tooltip("If true, poll Android for newly shared images that were sent directly into this app.")]
    public bool pollPendingSharedImports = true;

    [Tooltip("Seconds between checks for newly shared images on Android.")]
    public float pendingSharedImportPollInterval = 1f;

    [Tooltip("If true, the managed device picker allows selecting multiple images in one sync/import pass.")]
    public bool allowBatchImportFromDevice = true;

    [Tooltip("Maximum number of images to allow in one device sync/import selection.")]
    public int maxBatchImportSelectionCount = 24;

    [Tooltip("If true, images selected through the NativeGallery picker are broadcast to connected Unity clients.")]
    public bool broadcastNativeGalleryImports = true;

    [Header("Debug")]
    [Tooltip("If true, log controller events.")]
    public bool verboseLogging = true;

    private List<M_QuestGalleryAndroidBridge.GalleryItem> _items = new List<M_QuestGalleryAndroidBridge.GalleryItem>();
    private List<M_QuestGalleryTile> _spawnedTiles = new List<M_QuestGalleryTile>();

    private int _visibleCount = 0;
    private M_QuestGalleryTile _selectedTile;
    private M_QuestGalleryAndroidBridge.GalleryItem _selectedItem;
    private ScrollRect _scrollRect;
    private M_NetworkedPhotoSync _networkedPhotoSync;
    private Coroutine _pendingSharedImportPollRoutine;
    private bool _isLoadingGallery;
    private bool _reloadQueued;
    private bool _queuedPreserveCurrentDisplay;
    private bool _queuedRequestMediaPermission;
    private string _queuedPreferredSelectionIdentity;

    private void Awake()
    {
        ConfigureScrollViewport();
        ResolveRuntimeReferences();
        SubscribeImagePicker();
    }

    private void Start()
    {
        if (autoLoadOnStart)
            QueueGalleryReload();

        EnsurePendingSharedImportPollRunning();
    }

    private void OnEnable()
    {
        SubscribeImagePicker();
        EnsurePendingSharedImportPollRunning();
    }

    private void OnDisable()
    {
        UnsubscribeImagePicker();

        if (_pendingSharedImportPollRoutine != null)
        {
            StopCoroutine(_pendingSharedImportPollRoutine);
            _pendingSharedImportPollRoutine = null;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            TryProcessPendingSharedImports();
    }

    private void EnsurePendingSharedImportPollRunning()
    {
        if (!pollPendingSharedImports || _pendingSharedImportPollRoutine != null)
            return;

        _pendingSharedImportPollRoutine = StartCoroutine(PollPendingSharedImportsCoroutine());
    }

    /// <summary>
    /// UI hook for a Refresh button.
    /// </summary>
    public void RefreshGallery()
    {
        QueueGalleryReload(requestMediaPermission: true);
    }

    /// <summary>
    /// UI hook for an Import/Sync button. Uses NativeGallery when ImagePicker is present.
    /// </summary>
    public void ImportImageFromDevice()
    {
        if (imagePicker != null)
        {
            imagePicker.PickImage();
            return;
        }

        if (androidBridge == null)
        {
            Debug.LogWarning("[M_QuestGalleryController] ImportImageFromDevice failed: androidBridge is not assigned.");
            return;
        }

        int maxSelectionCount = Mathf.Max(1, maxBatchImportSelectionCount);
        bool allowMultiple = allowBatchImportFromDevice && maxSelectionCount > 1;

        if (verboseLogging)
            Debug.Log($"[M_QuestGalleryController] ImportImageFromDevice requested. allowMultiple={allowMultiple}, maxSelectionCount={maxSelectionCount}");

        if (!androidBridge.LaunchManagedImagePicker(allowMultiple, maxSelectionCount))
        {
            SetStatus("Image picker unavailable");
            return;
        }

        SetStatus(allowMultiple ? "Choose photos to sync" : "Choose an image to import");
    }

    /// <summary>
    /// Optional UI hook alias for flows that want to label the action as a sync.
    /// </summary>
    public void SyncImagesFromDevice()
    {
        ImportImageFromDevice();
    }

    public void NotifyExternalImageImported(string filePath, string sourceLabel = "Imported")
    {
        string preferredIdentity = null;
        ResolveRuntimeReferences();

        if (androidBridge != null && !string.IsNullOrWhiteSpace(filePath))
        {
            M_QuestGalleryAndroidBridge.GalleryItem item = androidBridge.CreateGalleryItemFromFilePath(filePath, sourceLabel);
            if (item != null)
                preferredIdentity = androidBridge.GetGalleryItemIdentity(item);
        }

        QueueGalleryReload(preferredIdentity, preserveCurrentDisplay: true, requestMediaPermission: false);
    }

    private void SubscribeImagePicker()
    {
        ResolveRuntimeReferences();

        if (imagePicker == null)
            return;

        imagePicker.ImagePrepared -= HandleImagePickerPrepared;
        imagePicker.ImagePrepared += HandleImagePickerPrepared;
    }

    private void UnsubscribeImagePicker()
    {
        if (imagePicker != null)
            imagePicker.ImagePrepared -= HandleImagePickerPrepared;
    }

    private void HandleImagePickerPrepared(ImagePicker.PreparedImage image)
    {
        ResolveRuntimeReferences();
        ClearSelection();

        if (!broadcastNativeGalleryImports)
            return;

        if (_networkedPhotoSync == null)
        {
            Debug.LogWarning("[M_QuestGalleryController] NativeGallery image was prepared, but no M_NetworkedPhotoSync was available.");
            return;
        }

        _networkedPhotoSync.BroadcastPreparedImage(image);
    }

    /// <summary>
    /// UI hook for a Load More button.
    /// </summary>
    public void LoadMore()
    {
        if (_items == null || _items.Count == 0)
            return;
        int oldCount = _visibleCount;
        _visibleCount = Mathf.Min(_visibleCount + Mathf.Max(1, loadMoreCount), _items.Count);

        StartCoroutine(SpawnRangeCoroutine(oldCount, _visibleCount));

        if (verboseLogging)
            Debug.Log($"[M_QuestGalleryController] LoadMore -> {_visibleCount}/{_items.Count}");
    }

    /// <summary>
    /// Called by M_QuestGalleryTile when a tile is pressed.
    /// </summary>
    public void OnTileSelected(M_QuestGalleryTile tile, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        if (_selectedTile != null)
            _selectedTile.SetSelected(false);

        _selectedTile = tile;
        _selectedItem = item;

        if (_selectedTile != null)
            _selectedTile.SetSelected(true);

        if (uploadOnSelection)
            UploadSelected();

        if (statusText != null && item != null)
            statusText.text = uploadOnSelection ? $"Uploaded: {item.fileName}" : $"Selected: {item.fileName}";

        if (verboseLogging && item != null)
            Debug.Log($"[M_QuestGalleryController] Selected -> {item.fileName}");
    }

    /// <summary>
    /// UI hook for a Share/Upload button. Shares the currently selected tile to the display.
    /// </summary>
    public void UploadSelected()
    {
        ResolveRuntimeReferences();

        if (_selectedItem == null)
        {
            if (TrySharePreparedPickerImage())
                return;

            SetStatus("Select an image first");
            return;
        }

        if (photoDisplay == null)
        {
            Debug.LogWarning("[M_QuestGalleryController] UploadSelected failed: photoDisplay is not assigned.");
            return;
        }

        if (androidBridge == null)
        {
            Debug.LogWarning("[M_QuestGalleryController] UploadSelected failed: androidBridge is not assigned.");
            return;
        }

        if (_networkedPhotoSync != null)
            _networkedPhotoSync.UploadSelectedItem(androidBridge, _selectedItem);
        else
            photoDisplay.DisplayPhoto(androidBridge, _selectedItem);

        SetStatus($"Shared: {_selectedItem.fileName}");
    }

    /// <summary>
    /// Clearer UI hook name for Share buttons. Kept separate from UploadSelected so old scenes still work.
    /// </summary>
    public void ShareSelectedImage()
    {
        UploadSelected();
    }

    private void QueueGalleryReload(
        string preferredSelectionIdentity = null,
        bool preserveCurrentDisplay = false,
        bool requestMediaPermission = true)
    {
        if (_isLoadingGallery)
        {
            _reloadQueued = true;
            _queuedPreserveCurrentDisplay |= preserveCurrentDisplay;
            _queuedRequestMediaPermission |= requestMediaPermission;

            if (!string.IsNullOrWhiteSpace(preferredSelectionIdentity))
                _queuedPreferredSelectionIdentity = preferredSelectionIdentity;

            return;
        }

        _isLoadingGallery = true;
        StartCoroutine(LoadGalleryCoroutine(preferredSelectionIdentity, preserveCurrentDisplay, requestMediaPermission));
    }

    private IEnumerator LoadGalleryCoroutine(
        string preferredSelectionIdentity = null,
        bool preserveCurrentDisplay = false,
        bool requestMediaPermission = true)
    {
        try
        {
            if (androidBridge == null)
            {
                Debug.LogError("[M_QuestGalleryController] Android bridge is not assigned.");
                yield break;
            }

            if (tileParent == null || tilePrefab == null)
            {
                Debug.LogError("[M_QuestGalleryController] Tile parent or tile prefab is not assigned.");
                yield break;
            }

            SetStatus(requestMediaPermission ? "Checking media access..." : "Scanning imported photos...");
            SetEmptyState(false);

            ClearTiles();
            _items.Clear();
            _visibleCount = 0;
            _selectedTile = null;
            _selectedItem = null;

            if (photoDisplay != null && !preserveCurrentDisplay)
                photoDisplay.ClearDisplay();

            if (requestMediaPermission)
                yield return StartCoroutine(androidBridge.RequestMediaPermissionCoroutine());

            bool hasMediaPermission = androidBridge.HasMediaPermission();
            SetStatus(hasMediaPermission ? "Scanning gallery..." : "Scanning imported photos...");
            yield return null;

            _items = androidBridge.GetGalleryItems();
            _items = FilterUniqueItems(_items);

            if (_items == null || _items.Count == 0)
            {
                SetStatus(hasMediaPermission ? "No images found" : "No imported photos found");
                SetEmptyState(true, hasMediaPermission
                    ? "No images were found in Quest gallery sources."
                    : "No phone uploads or app imports were found yet.");
                yield break;
            }
            _visibleCount = Mathf.Min(Mathf.Max(1, initialVisibleCount), _items.Count);

            SetStatus($"Found {_items.Count} image(s)");
            SetEmptyState(false);

            yield return StartCoroutine(SpawnRangeCoroutine(0, _visibleCount));

            int preferredSelectionIndex = FindItemIndexByIdentity(preferredSelectionIdentity);
            if (preferredSelectionIndex >= 0)
            {
                if (preferredSelectionIndex >= _visibleCount)
                {
                    int previousVisibleCount = _visibleCount;
                    _visibleCount = preferredSelectionIndex + 1;
                    yield return StartCoroutine(SpawnRangeCoroutine(previousVisibleCount, _visibleCount));
                }

                OnTileSelected(_spawnedTiles[preferredSelectionIndex], _items[preferredSelectionIndex]);
                SetStatus($"Imported: {_items[preferredSelectionIndex].fileName}");
                yield break;
            }

            if (autoSelectFirstItem && _spawnedTiles.Count > 0 && _items.Count > 0)
            {
                OnTileSelected(_spawnedTiles[0], _items[0]);
            }
        }
        finally
        {
            _isLoadingGallery = false;

            if (_reloadQueued)
            {
                bool preserveQueuedDisplay = _queuedPreserveCurrentDisplay;
                bool requestQueuedPermission = _queuedRequestMediaPermission;
                string queuedPreferredIdentity = _queuedPreferredSelectionIdentity;

                _reloadQueued = false;
                _queuedPreserveCurrentDisplay = false;
                _queuedRequestMediaPermission = false;
                _queuedPreferredSelectionIdentity = null;

                StartCoroutine(LoadGalleryCoroutine(queuedPreferredIdentity, preserveQueuedDisplay, requestQueuedPermission));
            }
        }
    }

    private IEnumerator SpawnRangeCoroutine(int startIndex, int endExclusive)
    {
        for (int i = startIndex; i < endExclusive; i++)
        {
            if (i < 0 || i >= _items.Count)
                continue;

            M_QuestGalleryAndroidBridge.GalleryItem item = _items[i];
            M_QuestGalleryTile tile = Instantiate(tilePrefab, tileParent);
            tile.Setup(this, item);

            _spawnedTiles.Add(tile);

            Texture2D thumb = androidBridge.LoadThumbnailTexture(item);
            tile.SetThumbnail(thumb);

            if (progressiveThumbnailLoading)
                yield return null;
        }

        SetStatus($"Showing {_spawnedTiles.Count} / {_items.Count}");
    }

    private void ClearTiles()
    {
        for (int i = 0; i < _spawnedTiles.Count; i++)
        {
            if (_spawnedTiles[i] != null)
                Destroy(_spawnedTiles[i].gameObject);
        }

        _spawnedTiles.Clear();
    }

    private void ClearSelection()
    {
        if (_selectedTile != null)
            _selectedTile.SetSelected(false);

        _selectedTile = null;
        _selectedItem = null;
    }

    private IEnumerator PollPendingSharedImportsCoroutine()
    {
        float waitDuration = Mathf.Max(0.25f, pendingSharedImportPollInterval);
        WaitForSecondsRealtime waitInstruction = new WaitForSecondsRealtime(waitDuration);

        while (true)
        {
            TryProcessPendingSharedImports();
            yield return waitInstruction;
        }
    }

    private void TryProcessPendingSharedImports()
    {
        ResolveRuntimeReferences();

        if (androidBridge == null)
            return;

        List<M_QuestGalleryAndroidBridge.GalleryItem> importedItems = androidBridge.ConsumePendingSharedImports();
        if (importedItems == null || importedItems.Count == 0)
            return;

        string preferredIdentity = androidBridge.GetGalleryItemIdentity(importedItems[0]);
        string statusMessage = importedItems.Count == 1
            ? $"Imported shared image: {importedItems[0].fileName}"
            : $"Imported {importedItems.Count} shared images";

        SetStatus(statusMessage);
        QueueGalleryReload(preferredIdentity, preserveCurrentDisplay: true, requestMediaPermission: false);
    }

    private int FindItemIndexByIdentity(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity) || androidBridge == null || _items == null)
            return -1;

        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(androidBridge.GetGalleryItemIdentity(_items[i]), identity, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private List<M_QuestGalleryAndroidBridge.GalleryItem> FilterUniqueItems(List<M_QuestGalleryAndroidBridge.GalleryItem> items)
    {
        if (items == null || items.Count <= 1 || androidBridge == null)
            return items;

        HashSet<string> seenIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<M_QuestGalleryAndroidBridge.GalleryItem> uniqueItems = new List<M_QuestGalleryAndroidBridge.GalleryItem>(items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            M_QuestGalleryAndroidBridge.GalleryItem item = items[i];
            if (item == null)
                continue;

            string identity = androidBridge.GetGalleryItemIdentity(item);
            if (string.IsNullOrWhiteSpace(identity) || !seenIdentities.Add(identity))
                continue;

            uniqueItems.Add(item);
        }

        if (verboseLogging && uniqueItems.Count != items.Count)
            Debug.Log($"[M_QuestGalleryController] Filtered {items.Count - uniqueItems.Count} duplicate gallery item(s) before spawning tiles.");

        return uniqueItems;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        if (verboseLogging)
            Debug.Log("[M_QuestGalleryController] " + message);
    }

    private void SetEmptyState(bool visible, string message = "")
    {
        if (emptyStateText != null)
        {
            emptyStateText.gameObject.SetActive(visible);
            emptyStateText.text = message;
        }
    }

    private void ResolveRuntimeReferences()
    {
        if (androidBridge == null)
            androidBridge = GetComponent<M_QuestGalleryAndroidBridge>();

        if (imagePicker == null)
            imagePicker = GetComponent<ImagePicker>();

        if (photoDisplay == null)
        {
            photoDisplay = GetComponent<M_QuestPhotoDisplay>();

            if (photoDisplay == null)
                photoDisplay = UnityEngine.Object.FindObjectOfType<M_QuestPhotoDisplay>();
        }

        if (_networkedPhotoSync == null)
            _networkedPhotoSync = GetComponent<M_NetworkedPhotoSync>();

        if (_networkedPhotoSync == null)
            _networkedPhotoSync = gameObject.AddComponent<M_NetworkedPhotoSync>();

        if (_networkedPhotoSync != null)
            _networkedPhotoSync.Initialize(photoDisplay);
    }

    private bool TrySharePreparedPickerImage()
    {
        if (imagePicker == null || !imagePicker.TryGetPreparedImage(out ImagePicker.PreparedImage preparedImage))
            return false;

        if (_networkedPhotoSync == null)
            ResolveRuntimeReferences();

        if (_networkedPhotoSync == null)
        {
            Debug.LogWarning("[M_QuestGalleryController] Prepared picker image could not be shared because no M_NetworkedPhotoSync was available.");
            return false;
        }

        _networkedPhotoSync.BroadcastPreparedImage(preparedImage);
        SetStatus($"Shared: {preparedImage.fileName}");
        return true;
    }

    private void ConfigureScrollViewport()
    {
        if (tileParent == null)
            return;

        RectTransform contentRect = tileParent as RectTransform;
        if (contentRect == null)
            return;

        _scrollRect = tileParent.GetComponentInParent<ScrollRect>();
        if (_scrollRect == null)
            return;

        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;

        if (_scrollRect.viewport != null)
        {
            if (_scrollRect.viewport.GetComponent<RectMask2D>() == null)
                _scrollRect.viewport.gameObject.AddComponent<RectMask2D>();
        }

        if (contentRect.GetComponent<ContentSizeFitter>() == null)
        {
            var fitter = contentRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// Quest / Android-side helper for:
/// - Requesting media/storage permission.
/// - Querying Android MediaStore for gallery-visible images.
/// - Finding common gallery folders on device storage as a fallback.
/// - Scanning for supported image files.
/// - Loading thumbnails and full-size textures from file paths or content URIs.
/// </summary>
public class M_QuestGalleryAndroidBridge : MonoBehaviour
{
    private static readonly string[] BuiltInSupportedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif" };
    private static readonly string[] AndroidBitmapFallbackExtensions = { ".heic", ".heif", ".webp", ".bmp" };
    private static readonly string[] HorizonProbeKeywords = { "horizon", "com.oculus.horizon", "com.meta.horizon", "com.facebook.horizon", "meta horizon" };

    [Serializable]
    public class GalleryItem
    {
        public string filePath;
        public string fileName;
        public string contentUri;
        public string relativePath;
        public string mimeType;
        public string source;
        public long lastWriteTicks;
        public long fileSizeBytes;
    }

    [Header("Scan Settings")]
    [Tooltip("If true, recursively scan subfolders inside each root folder.")]
    public bool recursiveScan = true;

    [Tooltip("If true, skip Android MediaStore and only use filesystem folder scanning.")]
    public bool disableMediaStoreQuery = false;

    [Tooltip("Maximum number of image files to return after sorting.")]
    public int maxItems = 200;

    [Tooltip("If true, sort newest files first.")]
    public bool newestFirst = true;

    [Tooltip("Supported file extensions.")]
    public string[] supportedExtensions = new string[] { ".jpg", ".jpeg", ".png", ".webp" };

    [Tooltip("Folder names to skip during recursive scanning.")]
    public string[] skippedFolderNames = new string[] { ".thumbnails", "Android", "obb", "data", "cache" };

    [Header("Thumbnail Settings")]
    [Tooltip("Maximum width/height for generated tile thumbnails.")]
    public int thumbnailMaxDimension = 256;

    [Tooltip("If true, generated thumbnails remain readable.")]
    public bool thumbnailsReadable = false;

    [Header("Imports")]
    [Tooltip("App-owned subfolder used for images imported from Android shares and the device picker.")]
    public string importedMediaFolderName = "ImportedSharedMedia";

    [Tooltip("App-owned subfolder used by the no-install phone upload web page.")]
    public string phoneUploadFolderName = "Uploads";

    [Header("Debug")]
    [Tooltip("If true, log detailed scan / load information.")]
    public bool verboseLogging = true;

    [Tooltip("If true, log every supported image that gets added.")]
    public bool logEachAddedImage = false;

    [Tooltip("If true, emit a compact scan summary with likely Horizon/Meta matches after each gallery refresh.")]
    public bool logGalleryDiagnostics = true;

    [Tooltip("Maximum number of sample items to include in diagnostic summaries.")]
    public int diagnosticItemSampleCount = 12;

    [Header("Deep Probe")]
    [Tooltip("If true, perform a bounded deep filesystem probe under shared storage roots to discover Horizon-related folders we do not already know about.")]
    public bool runExpandedFilesystemProbe = true;

    [Tooltip("Maximum folder depth for the expanded filesystem probe.")]
    public int expandedProbeMaxDepth = 5;

    [Tooltip("Maximum number of directories to inspect during the expanded filesystem probe.")]
    public int expandedProbeMaxDirectories = 1200;

    [Tooltip("Maximum number of keyword-matching directories to report and scan during the expanded filesystem probe.")]
    public int expandedProbeMaxMatches = 40;

    private struct ProbeDirectoryState
    {
        public string path;
        public int depth;

        public ProbeDirectoryState(string path, int depth)
        {
            this.path = path;
            this.depth = depth;
        }
    }

    public bool HasMediaPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool hasReadExternal = Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead);
        bool hasReadMediaImages = Permission.HasUserAuthorizedPermission("android.permission.READ_MEDIA_IMAGES");

        if (verboseLogging)
            Debug.Log($"[M_QuestGalleryAndroidBridge] HasMediaPermission -> ExternalStorageRead={hasReadExternal}, READ_MEDIA_IMAGES={hasReadMediaImages}");

        return hasReadExternal || hasReadMediaImages;
#else
        return true;
#endif
    }

    public IEnumerator RequestMediaPermissionCoroutine()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (HasMediaPermission())
            yield break;

        if (verboseLogging)
            Debug.Log("[M_QuestGalleryAndroidBridge] Requesting media permission...");

        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            Permission.RequestUserPermission(Permission.ExternalStorageRead);

        if (!Permission.HasUserAuthorizedPermission("android.permission.READ_MEDIA_IMAGES"))
            Permission.RequestUserPermission("android.permission.READ_MEDIA_IMAGES");

        float timeout = 5f;
        float timer = 0f;

        while (!HasMediaPermission() && timer < timeout)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        if (verboseLogging)
            Debug.Log($"[M_QuestGalleryAndroidBridge] Permission request complete. HasMediaPermission={HasMediaPermission()}");
#else
        yield break;
#endif
    }

    /// <summary>
    /// Scans Quest-accessible folders for supported image files.
    /// </summary>
    public List<GalleryItem> GetGalleryItems()
    {
        List<GalleryItem> items = new List<GalleryItem>();
        HashSet<string> seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int mediaStoreCount = 0;
        Dictionary<string, int> rootAddedCounts = logGalleryDiagnostics ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : null;
        bool canScanSharedStorage = HasMediaPermission();

        if (!disableMediaStoreQuery && canScanSharedStorage)
            mediaStoreCount = QueryMediaStoreImages(items, seenItems);

        List<string> roots = GetCandidateImageFolders(canScanSharedStorage);

        if (verboseLogging)
        {
            Debug.Log($"[M_QuestGalleryAndroidBridge] Candidate folder count: {roots.Count}");
            for (int i = 0; i < roots.Count; i++)
                Debug.Log($"[M_QuestGalleryAndroidBridge] Candidate[{i}] = {roots[i]}");
        }

        foreach (string root in roots)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            if (!Directory.Exists(root))
            {
                if (verboseLogging)
                    Debug.Log($"[M_QuestGalleryAndroidBridge] Skipping missing folder: {root}");
                continue;
            }

            if (verboseLogging)
                Debug.Log($"[M_QuestGalleryAndroidBridge] Scanning folder: {root}");

            try
            {
                int beforeCount = items.Count;
                ScanFolderRecursive(root, items, seenItems);

                if (rootAddedCounts != null)
                    rootAddedCounts[root] = Mathf.Max(0, items.Count - beforeCount);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Scan exception for '{root}': {ex.Message}");
            }
        }

        if (runExpandedFilesystemProbe && canScanSharedStorage)
            RunExpandedFilesystemProbe(items, seenItems);

        SortGalleryItems(items);

        if (maxItems > 0 && items.Count > maxItems)
        {
            if (verboseLogging)
                Debug.Log($"[M_QuestGalleryAndroidBridge] Truncating image list from {items.Count} to maxItems={maxItems}.");

            items = items.GetRange(0, maxItems);
        }

        if (verboseLogging)
            Debug.Log($"[M_QuestGalleryAndroidBridge] Found {items.Count} image(s). MediaStore={mediaStoreCount}, Filesystem={Mathf.Max(0, items.Count - mediaStoreCount)}");

        if (logGalleryDiagnostics)
            LogGalleryDiagnostics(items, mediaStoreCount, roots, rootAddedCounts);

        return items;
    }

    public Texture2D LoadThumbnailTexture(GalleryItem item)
    {
        return LoadTextureFromGalleryItem(item, thumbnailMaxDimension, thumbnailsReadable);
    }

    public Texture2D LoadThumbnailTexture(string filePath)
    {
        GalleryItem item = new GalleryItem
        {
            filePath = filePath,
            fileName = Path.GetFileName(filePath),
            source = "Filesystem"
        };

        return LoadThumbnailTexture(item);
    }

    public Texture2D LoadFullTexture(GalleryItem item, int maxDimension = 0, bool markNonReadable = false)
    {
        return LoadTextureFromGalleryItem(item, maxDimension, !markNonReadable);
    }

    public Texture2D LoadFullTexture(string filePath, int maxDimension = 0, bool markNonReadable = false)
    {
        GalleryItem item = new GalleryItem
        {
            filePath = filePath,
            fileName = Path.GetFileName(filePath),
            source = "Filesystem"
        };

        return LoadFullTexture(item, maxDimension, markNonReadable);
    }

    public void SortGalleryItems(List<GalleryItem> items)
    {
        if (items == null)
            return;

        items.Sort((a, b) =>
        {
            if (newestFirst)
                return b.lastWriteTicks.CompareTo(a.lastWriteTicks);

            return a.lastWriteTicks.CompareTo(b.lastWriteTicks);
        });
    }

    public string GetManagedImportFolderPath()
    {
        string importPath = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass shareReceiver = new AndroidJavaClass("com.multiuserfullbody.mediaupload.MediaShareReceiverActivity"))
                importPath = NormalizePath(shareReceiver.CallStatic<string>("getImportDirectoryPath"));
        }
        catch (Exception ex)
        {
            if (verboseLogging)
                Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to query the managed import directory from Android: {ex.Message}");
        }
#endif

        if (string.IsNullOrWhiteSpace(importPath))
        {
            string folderName = string.IsNullOrWhiteSpace(importedMediaFolderName) ? "ImportedSharedMedia" : importedMediaFolderName.Trim();
            string rootPath = string.IsNullOrWhiteSpace(Application.persistentDataPath) ? Application.temporaryCachePath : Application.persistentDataPath;
            importPath = NormalizePath(Path.Combine(rootPath, folderName));
        }

        if (!Directory.Exists(importPath))
            Directory.CreateDirectory(importPath);

        return importPath;
    }

    public string GetPhoneUploadFolderPath()
    {
        string folderName = string.IsNullOrWhiteSpace(phoneUploadFolderName) ? "Uploads" : phoneUploadFolderName.Trim();
        string rootPath = string.IsNullOrWhiteSpace(Application.persistentDataPath) ? Application.temporaryCachePath : Application.persistentDataPath;
        string uploadPath = NormalizePath(Path.Combine(rootPath, folderName));

        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        return uploadPath;
    }

    public GalleryItem ImportImageIntoManagedStorage(string sourcePath, string sourceLabel = "Imported")
    {
        string normalizedSourcePath = NormalizePath(sourcePath);
        if (string.IsNullOrWhiteSpace(normalizedSourcePath) || !File.Exists(normalizedSourcePath))
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] ImportImageIntoManagedStorage failed: source file is missing '{sourcePath}'.");
            return null;
        }

        if (!IsSupportedImageFile(normalizedSourcePath))
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] ImportImageIntoManagedStorage skipped unsupported image '{sourcePath}'.");
            return null;
        }

        string managedImportFolder = GetManagedImportFolderPath();
        string managedPath = normalizedSourcePath.StartsWith(managedImportFolder, StringComparison.OrdinalIgnoreCase)
            ? normalizedSourcePath
            : CreateUniqueImportPath(managedImportFolder, Path.GetFileName(normalizedSourcePath));

        try
        {
            if (!string.Equals(normalizedSourcePath, managedPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(normalizedSourcePath, managedPath, overwrite: false);
                File.SetLastWriteTimeUtc(managedPath, File.GetLastWriteTimeUtc(normalizedSourcePath));
            }

            GalleryItem item = CreateGalleryItemFromFilePath(managedPath, sourceLabel);
            if (item != null && verboseLogging)
                Debug.Log($"[M_QuestGalleryAndroidBridge] Imported image into managed storage: {DescribeItem(item)}");

            return item;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to import '{sourcePath}' into managed storage: {ex.Message}");
            return null;
        }
    }

    public GalleryItem CreateGalleryItemFromFilePath(string filePath, string sourceOverride = null)
    {
        string normalizedPath = NormalizePath(filePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
            return null;

        try
        {
            FileInfo fileInfo = new FileInfo(normalizedPath);
            return new GalleryItem
            {
                filePath = normalizedPath,
                fileName = fileInfo.Name,
                source = string.IsNullOrWhiteSpace(sourceOverride) ? "Filesystem" : sourceOverride,
                lastWriteTicks = fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0,
                fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                mimeType = GuessMimeTypeFromFileExtension(fileInfo.Extension)
            };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to create a gallery item for '{filePath}': {ex.Message}");
            return null;
        }
    }

    public List<GalleryItem> ConsumePendingSharedImports()
    {
        List<GalleryItem> importedItems = new List<GalleryItem>();

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass shareReceiver = new AndroidJavaClass("com.multiuserfullbody.mediaupload.MediaShareReceiverActivity"))
            {
                string[] pendingPaths = shareReceiver.CallStatic<string[]>("consumePendingImportedPaths");
                if (pendingPaths == null || pendingPaths.Length == 0)
                    return importedItems;

                for (int i = 0; i < pendingPaths.Length; i++)
                {
                    GalleryItem item = CreateGalleryItemFromFilePath(pendingPaths[i], "Android Share");
                    if (item != null)
                        importedItems.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to consume pending shared imports: {ex.Message}");
        }
#endif

        if (verboseLogging && importedItems.Count > 0)
            Debug.Log($"[M_QuestGalleryAndroidBridge] Consumed {importedItems.Count} pending Android shared import(s).");

        return importedItems;
    }

    public bool LaunchManagedImagePicker()
    {
        return LaunchManagedImagePicker(false, 1);
    }

    public bool LaunchManagedImagePicker(bool allowMultiple, int maxSelectionCount = 1)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (verboseLogging)
                Debug.Log($"[M_QuestGalleryAndroidBridge] LaunchManagedImagePicker -> allowMultiple={allowMultiple}, maxSelectionCount={Mathf.Max(1, maxSelectionCount)}");

            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaClass pickerActivity = new AndroidJavaClass("com.multiuserfullbody.mediaupload.MediaImportPickerActivity"))
            {
                pickerActivity.CallStatic("launch", activity, allowMultiple, Mathf.Max(1, maxSelectionCount));
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to launch the managed image picker: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    private void ScanFolderRecursive(string root, List<GalleryItem> items, HashSet<string> seenItems)
    {
        Stack<string> folders = new Stack<string>();
        folders.Push(root);

        while (folders.Count > 0)
        {
            string current = folders.Pop();

            if (ShouldSkipFolder(current))
            {
                if (verboseLogging)
                    Debug.Log($"[M_QuestGalleryAndroidBridge] Skipping folder by rule: {current}");
                continue;
            }

            string[] files = null;
            try
            {
                files = Directory.GetFiles(current, "*.*", SearchOption.TopDirectoryOnly);
                if (verboseLogging)
                    Debug.Log($"[M_QuestGalleryAndroidBridge] Raw file count in '{current}' = {files.Length}");
            }
            catch (Exception ex)
            {
                if (verboseLogging)
                    Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Could not read files in '{current}': {ex.Message}");
            }

            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];

                    if (!IsSupportedImageFile(file))
                        continue;

                    try
                    {
                        FileInfo fi = new FileInfo(file);

                        GalleryItem item = new GalleryItem
                        {
                            filePath = file,
                            fileName = fi.Name,
                            source = "Filesystem",
                            lastWriteTicks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0,
                            fileSizeBytes = fi.Exists ? fi.Length : 0
                        };

                        if (!TryAddGalleryItem(items, seenItems, item))
                            continue;

                        if (logEachAddedImage)
                            Debug.Log($"[M_QuestGalleryAndroidBridge] Added image: {file}");
                    }
                    catch (Exception ex)
                    {
                        if (verboseLogging)
                            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to inspect file '{file}': {ex.Message}");
                    }
                }
            }

            if (!recursiveScan)
                continue;

            string[] subdirs = null;
            try
            {
                subdirs = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                if (verboseLogging)
                    Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Could not read subdirs in '{current}': {ex.Message}");
            }

            if (subdirs != null)
            {
                for (int i = 0; i < subdirs.Length; i++)
                    folders.Push(subdirs[i]);
            }
        }
    }

    private Texture2D LoadTextureFromGalleryItem(GalleryItem item, int maxDimension, bool keepReadable)
    {
        if (item == null)
        {
            Debug.LogWarning("[M_QuestGalleryAndroidBridge] LoadTextureFromGalleryItem called with null item.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(item.filePath) && string.IsNullOrWhiteSpace(item.contentUri))
        {
            Debug.LogWarning("[M_QuestGalleryAndroidBridge] Gallery item has neither filePath nor contentUri.");
            return null;
        }

        bool preferBitmapFallback = RequiresAndroidBitmapFallback(item.filePath, item.mimeType);
        if (!preferBitmapFallback)
        {
            byte[] rawBytes = TryReadRawImageBytes(item);
            Texture2D directTexture = LoadTextureFromEncodedBytes(rawBytes, item, maxDimension, keepReadable, "raw bytes");
            if (directTexture != null)
                return directTexture;
        }

        byte[] transcodedBytes = TryReadTranscodedImageBytes(item);
        Texture2D transcodedTexture = LoadTextureFromEncodedBytes(transcodedBytes, item, maxDimension, keepReadable, "Android bitmap fallback");
        if (transcodedTexture != null)
            return transcodedTexture;

        if (preferBitmapFallback)
        {
            byte[] rawBytes = TryReadRawImageBytes(item);
            Texture2D directTexture = LoadTextureFromEncodedBytes(rawBytes, item, maxDimension, keepReadable, "late raw fallback");
            if (directTexture != null)
                return directTexture;
        }

        Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to load texture for '{DescribeItem(item)}'.");
        return null;
    }

    private bool IsSupportedImageFile(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return false;

        string[] effectiveExtensions = GetEffectiveSupportedExtensions();

        for (int i = 0; i < effectiveExtensions.Length; i++)
        {
            if (string.Equals(ext, effectiveExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool ShouldSkipFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return true;

        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(folderName))
            return false;

        for (int i = 0; i < skippedFolderNames.Length; i++)
        {
            if (string.Equals(folderName, skippedFolderNames[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private int QueryMediaStoreImages(List<GalleryItem> items, HashSet<string> seenItems)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        int addedCount = 0;

        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (AndroidJavaClass mediaClass = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
            using (AndroidJavaObject externalContentUri = mediaClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            {
                string[] projection =
                {
                    "_id",
                    "_display_name",
                    "date_modified",
                    "_size",
                    "_data",
                    "relative_path",
                    "mime_type"
                };

                string sortOrder = newestFirst ? "date_modified DESC" : "date_modified ASC";

                using (AndroidJavaObject cursor = resolver.Call<AndroidJavaObject>("query", externalContentUri, projection, null, null, sortOrder))
                {
                    if (cursor == null)
                    {
                        if (verboseLogging)
                            Debug.LogWarning("[M_QuestGalleryAndroidBridge] MediaStore query returned a null cursor.");

                        return 0;
                    }

                    int idIndex = cursor.Call<int>("getColumnIndex", "_id");
                    int fileNameIndex = cursor.Call<int>("getColumnIndex", "_display_name");
                    int dateModifiedIndex = cursor.Call<int>("getColumnIndex", "date_modified");
                    int sizeIndex = cursor.Call<int>("getColumnIndex", "_size");
                    int dataIndex = cursor.Call<int>("getColumnIndex", "_data");
                    int relativePathIndex = cursor.Call<int>("getColumnIndex", "relative_path");
                    int mimeTypeIndex = cursor.Call<int>("getColumnIndex", "mime_type");

                    while (cursor.Call<bool>("moveToNext"))
                    {
                        long mediaId = GetCursorLong(cursor, idIndex);
                        if (mediaId <= 0)
                            continue;

                        string fileName = GetCursorString(cursor, fileNameIndex);
                        string mimeType = GetCursorString(cursor, mimeTypeIndex);

                        if (!IsSupportedMediaStoreItem(fileName, mimeType))
                            continue;

                        string filePath = NormalizePath(GetCursorString(cursor, dataIndex));
                        string relativePath = NormalizePath(GetCursorString(cursor, relativePathIndex));
                        string contentUriString;

                        using (AndroidJavaClass contentUris = new AndroidJavaClass("android.content.ContentUris"))
                        using (AndroidJavaObject itemUri = contentUris.CallStatic<AndroidJavaObject>("withAppendedId", externalContentUri, mediaId))
                        {
                            contentUriString = itemUri != null ? itemUri.Call<string>("toString") : null;
                        }

                        GalleryItem item = new GalleryItem
                        {
                            filePath = filePath,
                            fileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(filePath) : fileName,
                            contentUri = contentUriString,
                            relativePath = relativePath,
                            mimeType = mimeType,
                            source = "MediaStore",
                            lastWriteTicks = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0L, GetCursorLong(cursor, dateModifiedIndex))).UtcTicks,
                            fileSizeBytes = Math.Max(0L, GetCursorLong(cursor, sizeIndex))
                        };

                        if (!TryAddGalleryItem(items, seenItems, item))
                            continue;

                        addedCount++;

                        if (logEachAddedImage)
                            Debug.Log($"[M_QuestGalleryAndroidBridge] Added MediaStore image: {DescribeItem(item)}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] MediaStore query failed: {ex.Message}");
        }

        if (verboseLogging)
            Debug.Log($"[M_QuestGalleryAndroidBridge] MediaStore returned {addedCount} image(s).");

        return addedCount;
#else
        return 0;
#endif
    }

    private List<string> GetCandidateImageFolders(bool includeSharedStorage)
    {
        List<string> folders = new List<string>();

        TryAddFolder(folders, GetPhoneUploadFolderPath());
        TryAddFolder(folders, GetManagedImportFolderPath());

        if (!includeSharedStorage)
            return folders;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass environment = new AndroidJavaClass("android.os.Environment"))
            {
                string dcim = environment.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", environment.GetStatic<string>("DIRECTORY_DCIM"))
                    ?.Call<string>("getAbsolutePath");

                string pictures = environment.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", environment.GetStatic<string>("DIRECTORY_PICTURES"))
                    ?.Call<string>("getAbsolutePath");

                string downloads = environment.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", environment.GetStatic<string>("DIRECTORY_DOWNLOADS"))
                    ?.Call<string>("getAbsolutePath");

                TryAddFolder(folders, dcim);
                TryAddFolder(folders, pictures);
                TryAddFolder(folders, downloads);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Android folder query failed: {ex.Message}");
        }

        // Generic shared-media locations.
        TryAddFolder(folders, "/storage/emulated/0/DCIM");
        TryAddFolder(folders, "/storage/emulated/0/DCIM/Camera");
        TryAddFolder(folders, "/storage/emulated/0/DCIM/Screenshots");
        TryAddFolder(folders, "/storage/emulated/0/Screenshots");
        TryAddFolder(folders, "/storage/emulated/0/Pictures");
        TryAddFolder(folders, "/storage/emulated/0/Pictures/Screenshots");
        TryAddFolder(folders, "/storage/emulated/0/Pictures/Oculus");
        TryAddFolder(folders, "/storage/emulated/0/Pictures/Meta Quest");
        TryAddFolder(folders, "/storage/emulated/0/Pictures/Meta Horizon");
        TryAddFolder(folders, "/storage/emulated/0/Pictures/Meta Horizon/Shared");
        TryAddFolder(folders, "/storage/emulated/0/Download");
        TryAddFolder(folders, "/storage/emulated/0/Android/media");
        TryAddFolder(folders, "/storage/emulated/0/Android/media/com.oculus.horizon");
        TryAddFolder(folders, "/storage/emulated/0/Android/media/com.oculus.horizon/files");
        TryAddFolder(folders, "/storage/emulated/0/Android/media/com.meta.horizon");
        TryAddFolder(folders, "/storage/emulated/0/Android/media/com.meta.horizon/files");
        TryAddFolder(folders, "/storage/emulated/0/Android/media/com.facebook.horizon");
        TryAddFolder(folders, "/storage/emulated/0/Android/media/com.facebook.horizon/files");
        TryAddFolder(folders, "/storage/emulated/0/Android/data/com.oculus.horizon");
        TryAddFolder(folders, "/storage/emulated/0/Android/data/com.oculus.horizon/files");
        TryAddFolder(folders, "/storage/emulated/0/Android/data/com.meta.horizon");
        TryAddFolder(folders, "/storage/emulated/0/Android/data/com.meta.horizon/files");
        TryAddFolder(folders, "/storage/emulated/0/Android/data/com.facebook.horizon");
        TryAddFolder(folders, "/storage/emulated/0/Android/data/com.facebook.horizon/files");

        // Quest / Oculus-specific likely locations.
        TryAddFolder(folders, "/storage/emulated/0/Oculus");
        TryAddFolder(folders, "/storage/emulated/0/Oculus/Screenshots");
        TryAddFolder(folders, "/storage/emulated/0/Oculus/Media");
        TryAddFolder(folders, "/storage/emulated/0/Oculus/VideoShots");
        TryAddFolder(folders, "/storage/emulated/0/Pictures/Horizon");
        TryAddFolder(folders, "/sdcard/Oculus");
        TryAddFolder(folders, "/sdcard/Oculus/Screenshots");
        TryAddFolder(folders, "/sdcard/Oculus/Media");
        TryAddFolder(folders, "/sdcard/Oculus/VideoShots");
        TryAddFolder(folders, "/sdcard/DCIM");
        TryAddFolder(folders, "/sdcard/DCIM/Screenshots");
        TryAddFolder(folders, "/sdcard/Pictures");
        TryAddFolder(folders, "/sdcard/Pictures/Screenshots");
        TryAddFolder(folders, "/sdcard/Download");
        TryAddFolder(folders, "/sdcard/Android/media");
        TryAddFolder(folders, "/sdcard/Android/media/com.oculus.horizon");
        TryAddFolder(folders, "/sdcard/Android/media/com.oculus.horizon/files");
        TryAddFolder(folders, "/sdcard/Android/media/com.meta.horizon");
        TryAddFolder(folders, "/sdcard/Android/media/com.meta.horizon/files");
        TryAddFolder(folders, "/sdcard/Android/media/com.facebook.horizon");
        TryAddFolder(folders, "/sdcard/Android/media/com.facebook.horizon/files");
        TryAddFolder(folders, "/sdcard/Android/data/com.oculus.horizon");
        TryAddFolder(folders, "/sdcard/Android/data/com.oculus.horizon/files");
        TryAddFolder(folders, "/sdcard/Android/data/com.meta.horizon");
        TryAddFolder(folders, "/sdcard/Android/data/com.meta.horizon/files");
        TryAddFolder(folders, "/sdcard/Android/data/com.facebook.horizon");
        TryAddFolder(folders, "/sdcard/Android/data/com.facebook.horizon/files");
        TryAddFolder(folders, "/sdcard/Pictures/Horizon");
#else
        TryAddFolder(folders, Path.Combine(Application.dataPath, ".."));
        TryAddFolder(folders, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        TryAddFolder(folders, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
#endif

        return folders;
    }

    private void TryAddFolder(List<string> folders, string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        path = NormalizeComparablePath(path);

        if (!folders.Contains(path))
            folders.Add(path);
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        path = path.Replace('\\', '/');

        while (path.Contains("//"))
            path = path.Replace("//", "/");

        return path.TrimEnd('/');
    }

    private string NormalizeComparablePath(string path)
    {
        path = NormalizePath(path);
        if (string.IsNullOrEmpty(path))
            return path;

        const string canonicalSharedRoot = "/storage/emulated/0";
        string[] sharedStorageAliases =
        {
            "/sdcard",
            "/mnt/sdcard",
            "/storage/self/primary"
        };

        for (int i = 0; i < sharedStorageAliases.Length; i++)
        {
            string alias = sharedStorageAliases[i];
            if (string.Equals(path, alias, StringComparison.OrdinalIgnoreCase))
                return canonicalSharedRoot;

            if (path.StartsWith(alias + "/", StringComparison.OrdinalIgnoreCase))
                return canonicalSharedRoot + path.Substring(alias.Length);
        }

        return path;
    }

    private string[] GetEffectiveSupportedExtensions()
    {
        HashSet<string> extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddExtensions(extensions, BuiltInSupportedExtensions);
        AddExtensions(extensions, supportedExtensions);

        string[] effectiveExtensions = new string[extensions.Count];
        extensions.CopyTo(effectiveExtensions);
        return effectiveExtensions;
    }

    private void AddExtensions(HashSet<string> extensions, string[] values)
    {
        if (extensions == null || values == null)
            return;

        for (int i = 0; i < values.Length; i++)
        {
            string ext = values[i];
            if (string.IsNullOrWhiteSpace(ext))
                continue;

            if (!ext.StartsWith(".", StringComparison.Ordinal))
                ext = "." + ext;

            extensions.Add(ext);
        }
    }

    private bool IsSupportedMediaStoreItem(string fileName, string mimeType)
    {
        if (IsSupportedImageFile(fileName))
            return true;

        return !string.IsNullOrWhiteSpace(mimeType) && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAddGalleryItem(List<GalleryItem> items, HashSet<string> seenItems, GalleryItem item)
    {
        if (item == null)
            return false;

        string identity = GetGalleryItemIdentity(item);
        if (string.IsNullOrWhiteSpace(identity))
            return false;

        if (!seenItems.Add(identity))
            return false;

        items.Add(item);
        return true;
    }

    public string GetGalleryItemIdentity(GalleryItem item)
    {
        if (item == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(item.filePath))
            return "path:" + NormalizeComparablePath(item.filePath);

        if (!string.IsNullOrWhiteSpace(item.contentUri))
            return "uri:" + item.contentUri.Trim();

        return "meta:" + (item.fileName ?? string.Empty) + "|" + item.fileSizeBytes + "|" + item.lastWriteTicks;
    }

    private string CreateUniqueImportPath(string folderPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return string.Empty;

        string sanitizedFileName = string.IsNullOrWhiteSpace(fileName) ? "imported-image.png" : fileName.Trim();
        string baseName = Path.GetFileNameWithoutExtension(sanitizedFileName);
        string extension = Path.GetExtension(sanitizedFileName);

        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "imported-image";

        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        string candidatePath = NormalizePath(Path.Combine(folderPath, baseName + extension));
        int suffix = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = NormalizePath(Path.Combine(folderPath, $"{baseName}-{suffix}{extension}"));
            suffix++;
        }

        return candidatePath;
    }

    private string DescribeItem(GalleryItem item)
    {
        if (item == null)
            return "(null)";

        if (!string.IsNullOrWhiteSpace(item.filePath))
            return item.filePath;

        if (!string.IsNullOrWhiteSpace(item.contentUri))
            return item.contentUri;

        return item.fileName ?? "(unnamed)";
    }

    private void LogGalleryDiagnostics(List<GalleryItem> items, int mediaStoreCount, List<string> roots, Dictionary<string, int> rootAddedCounts)
    {
        int totalItems = items != null ? items.Count : 0;
        int filesystemCount = Mathf.Max(0, totalItems - mediaStoreCount);
        int sampleLimit = Mathf.Max(1, diagnosticItemSampleCount);

        Debug.Log($"[M_QuestGalleryAndroidBridge] Diagnostics summary: total={totalItems}, mediaStore={mediaStoreCount}, filesystem={filesystemCount}, candidateRoots={(roots != null ? roots.Count : 0)}");

        if (roots != null && rootAddedCounts != null)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                bool isLikelyHorizonRoot = IsLikelyHorizonText(root);
                int addedCount = rootAddedCounts.TryGetValue(root, out int count) ? count : 0;

                if (addedCount > 0 || isLikelyHorizonRoot)
                    Debug.Log($"[M_QuestGalleryAndroidBridge] Diagnostics root: {root} -> +{addedCount} item(s)");
            }
        }

        List<GalleryItem> likelyHorizonItems = new List<GalleryItem>();
        List<GalleryItem> recentSamples = new List<GalleryItem>();

        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                GalleryItem item = items[i];
                if (item == null)
                    continue;

                if (recentSamples.Count < sampleLimit)
                    recentSamples.Add(item);

                if (IsLikelyHorizonItem(item))
                    likelyHorizonItems.Add(item);
            }
        }

        if (likelyHorizonItems.Count == 0)
        {
            Debug.Log("[M_QuestGalleryAndroidBridge] Diagnostics found no likely Horizon/Meta gallery entries.");
        }
        else
        {
            Debug.Log($"[M_QuestGalleryAndroidBridge] Diagnostics found {likelyHorizonItems.Count} likely Horizon/Meta gallery entr{(likelyHorizonItems.Count == 1 ? "y" : "ies")}.");

            int highlightLimit = Mathf.Min(sampleLimit, likelyHorizonItems.Count);
            for (int i = 0; i < highlightLimit; i++)
                Debug.Log($"[M_QuestGalleryAndroidBridge] Diagnostics highlight[{i}] {FormatDiagnosticItem(likelyHorizonItems[i])}");
        }

        int recentLimit = Mathf.Min(sampleLimit, recentSamples.Count);
        for (int i = 0; i < recentLimit; i++)
            Debug.Log($"[M_QuestGalleryAndroidBridge] Diagnostics sample[{i}] {FormatDiagnosticItem(recentSamples[i])}");
    }

    private bool IsLikelyHorizonItem(GalleryItem item)
    {
        if (item == null)
            return false;

        return IsLikelyHorizonText(item.filePath) ||
               IsLikelyHorizonText(item.relativePath) ||
               IsLikelyHorizonText(item.contentUri) ||
               IsLikelyHorizonText(item.fileName) ||
               IsLikelyHorizonText(item.mimeType);
    }

    private bool IsLikelyHorizonText(string value)
    {
        return ContainsAnyKeyword(value, HorizonProbeKeywords);
    }

    private string FormatDiagnosticItem(GalleryItem item)
    {
        if (item == null)
            return "(null)";

        return $"source={item.source ?? "Unknown"}, file={item.fileName ?? "(unnamed)"}, relative={item.relativePath ?? "-"}, path={item.filePath ?? "-"}, uri={item.contentUri ?? "-"}, mime={item.mimeType ?? "-"}";
    }

    private void RunExpandedFilesystemProbe(List<GalleryItem> items, HashSet<string> seenItems)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (items == null || seenItems == null)
            return;

        int maxDepth = Mathf.Max(1, expandedProbeMaxDepth);
        int maxDirectories = Mathf.Max(1, expandedProbeMaxDirectories);
        int maxMatches = Mathf.Max(1, expandedProbeMaxMatches);

        Queue<ProbeDirectoryState> queue = new Queue<ProbeDirectoryState>();
        HashSet<string> visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> matchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int startingItemCount = items.Count;
        int visitedCount = 0;

        EnqueueProbeRoot(queue, "/storage/emulated/0");
        EnqueueProbeRoot(queue, "/sdcard");

        while (queue.Count > 0 && visitedCount < maxDirectories && matchedDirectories.Count < maxMatches)
        {
            ProbeDirectoryState current = queue.Dequeue();
            string currentPath = NormalizePath(current.path);

            if (string.IsNullOrWhiteSpace(currentPath))
                continue;

            if (!visitedDirectories.Add(currentPath))
                continue;

            if (!Directory.Exists(currentPath))
                continue;

            visitedCount++;

            if (PathContainsProbeKeyword(currentPath) && matchedDirectories.Add(currentPath))
            {
                int beforeCount = items.Count;

                try
                {
                    ScanFolderRecursive(currentPath, items, seenItems);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Deep probe scan exception for '{currentPath}': {ex.Message}");
                }

                Debug.Log($"[M_QuestGalleryAndroidBridge] Deep probe matched directory: {currentPath} -> +{Mathf.Max(0, items.Count - beforeCount)} image(s)");
            }

            if (current.depth >= maxDepth)
                continue;

            string[] subdirs = null;
            try
            {
                subdirs = Directory.GetDirectories(currentPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                if (verboseLogging || logGalleryDiagnostics)
                    Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Deep probe could not read subdirs in '{currentPath}': {ex.Message}");
            }

            if (subdirs == null)
                continue;

            for (int i = 0; i < subdirs.Length; i++)
            {
                string subdir = subdirs[i];
                if (ShouldSkipProbeFolder(subdir))
                    continue;

                queue.Enqueue(new ProbeDirectoryState(subdir, current.depth + 1));
            }
        }

        Debug.Log($"[M_QuestGalleryAndroidBridge] Deep probe summary: visited={visitedCount}, matched={matchedDirectories.Count}, added={Mathf.Max(0, items.Count - startingItemCount)}, depth={maxDepth}");
#endif
    }

    private void EnqueueProbeRoot(Queue<ProbeDirectoryState> queue, string rootPath)
    {
        if (queue == null || string.IsNullOrWhiteSpace(rootPath))
            return;

        queue.Enqueue(new ProbeDirectoryState(rootPath, 0));
    }

    private bool ShouldSkipProbeFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return true;

        string normalizedPath = NormalizePath(folderPath);
        string folderName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(folderName))
            return false;

        if (folderName.StartsWith(".", StringComparison.Ordinal) &&
            !string.Equals(folderName, ".nomedia", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(folderName, ".thumbnails", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(folderName, "obb", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(folderName, "cache", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(folderName, "tmp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedPath.IndexOf("/Android/obb", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private bool PathContainsProbeKeyword(string value)
    {
        return ContainsAnyKeyword(value, HorizonProbeKeywords);
    }

    private bool ContainsAnyKeyword(string value, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(value) || keywords == null)
            return false;

        for (int i = 0; i < keywords.Length; i++)
        {
            string keyword = keywords[i];
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            if (value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private string GuessMimeTypeFromFileExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        switch (extension.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                return "image/jpeg";
            case ".png":
                return "image/png";
            case ".webp":
                return "image/webp";
            case ".heic":
                return "image/heic";
            case ".heif":
                return "image/heif";
            case ".bmp":
                return "image/bmp";
            default:
                return null;
        }
    }

    private bool RequiresAndroidBitmapFallback(string filePath, string mimeType)
    {
        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            if (mimeType.IndexOf("heic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mimeType.IndexOf("heif", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mimeType.IndexOf("webp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mimeType.IndexOf("bmp", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        string ext = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(ext))
            return false;

        for (int i = 0; i < AndroidBitmapFallbackExtensions.Length; i++)
        {
            if (string.Equals(ext, AndroidBitmapFallbackExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private byte[] TryReadRawImageBytes(GalleryItem item)
    {
        if (item == null)
            return null;

        byte[] bytes = TryReadFileBytes(item.filePath);
        if (bytes != null && bytes.Length > 0)
            return bytes;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(item.contentUri))
            return TryReadBytesFromContentUri(item.contentUri);
#endif

        return null;
    }

    private byte[] TryReadFileBytes(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            return File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            if (verboseLogging)
                Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to read file bytes from '{filePath}': {ex.Message}");

            return null;
        }
    }

    private Texture2D LoadTextureFromEncodedBytes(byte[] bytes, GalleryItem item, int maxDimension, bool keepReadable, string loadMode)
    {
        if (bytes == null || bytes.Length == 0)
            return null;

        try
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            bool loaded = tex.LoadImage(bytes, markNonReadable: false);
            if (!loaded)
            {
                Destroy(tex);

                if (verboseLogging)
                    Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Texture2D.LoadImage failed for '{DescribeItem(item)}' via {loadMode}.");

                return null;
            }

            if (maxDimension > 0 && (tex.width > maxDimension || tex.height > maxDimension))
            {
                Texture2D resized = ResizeTexture(tex, maxDimension, keepReadable);

                if (resized != null)
                {
                    Destroy(tex);
                    tex = resized;
                }
            }
            else if (!keepReadable)
            {
                Texture2D nonReadableCopy = MakeTextureNonReadableCopy(tex);
                if (nonReadableCopy != null)
                {
                    Destroy(tex);
                    tex = nonReadableCopy;
                }
            }

            ConfigureDisplayTextureSampling(tex);

            if (verboseLogging)
                Debug.Log($"[M_QuestGalleryAndroidBridge] Loaded texture via {loadMode}: {DescribeItem(item)} ({tex.width}x{tex.height})");

            return tex;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_QuestGalleryAndroidBridge] Texture decode exception for '{DescribeItem(item)}' via {loadMode}: {ex}");
            return null;
        }
    }

    private string GetCursorString(AndroidJavaObject cursor, int columnIndex)
    {
        if (cursor == null || columnIndex < 0)
            return null;

        return cursor.Call<string>("getString", columnIndex);
    }

    private long GetCursorLong(AndroidJavaObject cursor, int columnIndex)
    {
        if (cursor == null || columnIndex < 0)
            return 0L;

        return cursor.Call<long>("getLong", columnIndex);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private byte[] TryReadBytesFromContentUri(string contentUri)
    {
        if (string.IsNullOrWhiteSpace(contentUri))
            return null;

        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
            using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", contentUri))
            using (AndroidJavaObject inputStream = resolver.Call<AndroidJavaObject>("openInputStream", uri))
            {
                return ReadAllBytesFromInputStream(inputStream);
            }
        }
        catch (Exception ex)
        {
            if (verboseLogging)
                Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Failed to read content URI '{contentUri}': {ex.Message}");

            return null;
        }
    }

    private byte[] ReadAllBytesFromInputStream(AndroidJavaObject inputStream)
    {
        if (inputStream == null)
            return null;

        try
        {
            using (AndroidJavaObject byteArrayOutputStream = new AndroidJavaObject("java.io.ByteArrayOutputStream"))
            {
                byte[] buffer = new byte[16 * 1024];
                int bytesRead;

                while ((bytesRead = inputStream.Call<int>("read", buffer)) != -1)
                    byteArrayOutputStream.Call("write", buffer, 0, bytesRead);

                return byteArrayOutputStream.Call<byte[]>("toByteArray");
            }
        }
        finally
        {
            inputStream.Call("close");
        }
    }

    private byte[] TryReadTranscodedImageBytes(GalleryItem item)
    {
        if (item == null)
            return null;

        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory"))
            using (AndroidJavaClass compressFormatClass = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat"))
            {
                AndroidJavaObject bitmap = null;

                try
                {
                    if (!string.IsNullOrWhiteSpace(item.contentUri))
                    {
                        using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
                        using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", item.contentUri))
                        using (AndroidJavaObject inputStream = resolver.Call<AndroidJavaObject>("openInputStream", uri))
                        {
                            if (inputStream != null)
                                bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeStream", inputStream);
                        }
                    }

                    if (bitmap == null && !string.IsNullOrWhiteSpace(item.filePath))
                        bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeFile", item.filePath);

                    if (bitmap == null)
                        return null;

                    using (AndroidJavaObject byteArrayOutputStream = new AndroidJavaObject("java.io.ByteArrayOutputStream"))
                    using (AndroidJavaObject pngFormat = compressFormatClass.GetStatic<AndroidJavaObject>("PNG"))
                    {
                        bool compressed = bitmap.Call<bool>("compress", pngFormat, 100, byteArrayOutputStream);
                        if (!compressed)
                            return null;

                        return byteArrayOutputStream.Call<byte[]>("toByteArray");
                    }
                }
                finally
                {
                    if (bitmap != null)
                    {
                        bitmap.Call("recycle");
                        bitmap.Dispose();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (verboseLogging)
                Debug.LogWarning($"[M_QuestGalleryAndroidBridge] Android bitmap fallback failed for '{DescribeItem(item)}': {ex.Message}");

            return null;
        }
    }
#else
    private byte[] TryReadTranscodedImageBytes(GalleryItem item)
    {
        return null;
    }
#endif

    private Texture2D ResizeTexture(Texture2D source, int maxDimension, bool keepReadable)
    {
        if (source == null)
            return null;

        int srcW = source.width;
        int srcH = source.height;

        float scale = Mathf.Min((float)maxDimension / srcW, (float)maxDimension / srcH);
        scale = Mathf.Min(scale, 1f);

        int dstW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
        int dstH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));

        RenderTexture rt = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D resized = new Texture2D(dstW, dstH, TextureFormat.RGBA32, true);
        resized.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
        resized.Apply(updateMipmaps: true, makeNoLongerReadable: !keepReadable);
        ConfigureDisplayTextureSampling(resized);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return resized;
    }

    private Texture2D MakeTextureNonReadableCopy(Texture2D source)
    {
        if (source == null)
            return null;

        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        ConfigureDisplayTextureSampling(copy);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return copy;
    }

    private void ConfigureDisplayTextureSampling(Texture texture)
    {
        if (texture == null)
            return;

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 4;
    }
}

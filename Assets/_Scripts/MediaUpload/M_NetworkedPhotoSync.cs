using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Bridges local media imports and phone uploads with the active multiplayer player object.
/// Local uploads are displayed immediately, then forwarded through XRINetworkPlayer so
/// every connected client applies the same image to their own shared photo panel.
/// </summary>
public class M_NetworkedPhotoSync : MonoBehaviour
{
    [SerializeField] private M_QuestPhotoDisplay photoDisplay;
    [SerializeField, Range(1, 100)] private int jpegQuality = 75;
    [SerializeField] private float localPlayerLookupTimeout = 3f;
    [SerializeField] private float duplicateBroadcastWindowSeconds = 2f;
    [SerializeField] private bool verboseLogging = true;

    private int _lastBroadcastSignature;
    private float _lastBroadcastTime = -999f;
    private Type _networkPlayerType;
    private EventInfo _sharedMediaReceivedEvent;
    private Delegate _sharedMediaReceivedHandler;

    public void Initialize(M_QuestPhotoDisplay display)
    {
        if (photoDisplay == null)
            photoDisplay = display;
    }

    private void Awake()
    {
        if (photoDisplay == null)
            photoDisplay = GetComponent<M_QuestPhotoDisplay>();
    }

    private void OnEnable()
    {
        SubscribeSharedMediaReceived();
    }

    private void OnDisable()
    {
        UnsubscribeSharedMediaReceived();
    }

    public void UploadSelectedItem(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        StartCoroutine(UploadSelectedItemCoroutine(bridge, item));
    }

    public void BroadcastPreparedImage(ImagePicker.PreparedImage image)
    {
        if (image == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] BroadcastPreparedImage ignored null image.");
            return;
        }

        BroadcastImageBytes(image.fileName, image.bytes);
    }

    public void BroadcastTexture(Texture2D texture, string fileName)
    {
        StartCoroutine(BroadcastTextureCoroutine(texture, fileName));
    }

    public void BroadcastImageBytes(string fileName, byte[] encodedBytes)
    {
        StartCoroutine(BroadcastImageBytesCoroutine(fileName, encodedBytes));
    }

    private object ResolveLocalPlayer()
    {
        if (!TryResolveNetworkPlayerType())
            return null;

        object localPlayer = GetStaticMemberValue(_networkPlayerType, "LocalPlayer");
        if (localPlayer != null)
            return localPlayer;

        UnityEngine.Object[] players = UnityEngine.Object.FindObjectsOfType(_networkPlayerType);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && GetBoolMemberValue(players[i], "IsLocalPlayer", false))
                return players[i];
        }

        return null;
    }

    private IEnumerator UploadSelectedItemCoroutine(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        if (bridge == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] UploadSelectedItem failed: bridge is null.");
            yield break;
        }

        if (item == null || (string.IsNullOrWhiteSpace(item.filePath) && string.IsNullOrWhiteSpace(item.contentUri)))
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] UploadSelectedItem failed: item source is invalid.");
            yield break;
        }

        yield return null;

        int maxDimension = photoDisplay != null && photoDisplay.maxDisplayDimension > 0 ? photoDisplay.maxDisplayDimension : 2048;
        Texture2D localTexture = bridge.LoadFullTexture(item, maxDimension, markNonReadable: false);

        if (localTexture == null)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to load '{item.fileName}' for upload.");
            yield break;
        }

        bool displayOwnsTexture = false;
        if (photoDisplay != null)
        {
            photoDisplay.DisplayTexture(localTexture, item.fileName);
            displayOwnsTexture = true;
        }

        byte[] encodedBytes = ImageConversion.EncodeToJPG(localTexture, Mathf.Clamp(jpegQuality, 1, 100));
        if (encodedBytes == null || encodedBytes.Length == 0)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to encode '{item.fileName}' for synchronization.");

            if (!displayOwnsTexture)
                Destroy(localTexture);

            yield break;
        }

        yield return BroadcastImageBytesCoroutine(item.fileName, encodedBytes);

        if (!displayOwnsTexture)
            Destroy(localTexture);
    }

    private IEnumerator BroadcastTextureCoroutine(Texture2D texture, string fileName)
    {
        if (texture == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] BroadcastTexture ignored null texture.");
            yield break;
        }

        byte[] encodedBytes = null;

        try
        {
            encodedBytes = ImageConversion.EncodeToJPG(texture, Mathf.Clamp(jpegQuality, 1, 100));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to encode texture '{fileName}' for synchronization: {ex.Message}");
        }

        yield return BroadcastImageBytesCoroutine(fileName, encodedBytes);
    }

    private IEnumerator BroadcastImageBytesCoroutine(string fileName, byte[] encodedBytes)
    {
        if (encodedBytes == null || encodedBytes.Length == 0)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] BroadcastImageBytes ignored an empty payload.");
            yield break;
        }

        string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "Uploaded photo" : fileName;
        if (IsDuplicateRecentBroadcast(safeFileName, encodedBytes))
        {
            if (verboseLogging)
                Debug.Log($"[M_NetworkedPhotoSync] Skipped duplicate shared media broadcast: {safeFileName}");

            yield break;
        }

        object localPlayer = null;
        float playerLookupTimer = 0f;
        float timeout = Mathf.Max(0.1f, localPlayerLookupTimeout);

        while (playerLookupTimer < timeout)
        {
            localPlayer = ResolveLocalPlayer();
            if (IsReadyLocalPlayer(localPlayer))
                break;

            playerLookupTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!IsReadyLocalPlayer(localPlayer))
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] Shared image applied locally, but no spawned local network player was available for broadcast.");
            yield break;
        }

        MarkBroadcastPayload(safeFileName, encodedBytes);

        if (verboseLogging)
            Debug.Log($"[M_NetworkedPhotoSync] Broadcasting '{safeFileName}' ({encodedBytes.Length} bytes).");

        InvokeBroadcastSharedMedia(localPlayer, safeFileName, encodedBytes);
    }

    private void HandleSharedMediaReceived(string fileName, byte[] encodedBytes)
    {
        if (!isActiveAndEnabled || photoDisplay == null || encodedBytes == null || encodedBytes.Length == 0)
            return;

        Texture2D syncedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, true);

        if (!ImageConversion.LoadImage(syncedTexture, encodedBytes, markNonReadable: false))
        {
            Destroy(syncedTexture);
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to decode synchronized image '{fileName}'.");
            return;
        }

        syncedTexture.wrapMode = TextureWrapMode.Clamp;
        syncedTexture.filterMode = FilterMode.Trilinear;
        syncedTexture.anisoLevel = 4;

        if (!photoDisplay.keepTextureReadable)
            syncedTexture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

        if (verboseLogging)
            Debug.Log($"[M_NetworkedPhotoSync] Applied synchronized image '{fileName}' ({syncedTexture.width}x{syncedTexture.height}).");

        photoDisplay.DisplayTexture(syncedTexture, fileName);
    }

    private bool IsDuplicateRecentBroadcast(string fileName, byte[] bytes)
    {
        if (duplicateBroadcastWindowSeconds <= 0f)
            return false;

        int signature = ComputePayloadSignature(fileName, bytes);
        return signature == _lastBroadcastSignature &&
               Time.unscaledTime - _lastBroadcastTime <= duplicateBroadcastWindowSeconds;
    }

    private void MarkBroadcastPayload(string fileName, byte[] bytes)
    {
        _lastBroadcastSignature = ComputePayloadSignature(fileName, bytes);
        _lastBroadcastTime = Time.unscaledTime;
    }

    private static int ComputePayloadSignature(string fileName, byte[] bytes)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (fileName != null ? fileName.GetHashCode() : 0);
            hash = hash * 31 + (bytes != null ? bytes.Length : 0);

            if (bytes == null || bytes.Length == 0)
                return hash;

            int sampleCount = Mathf.Min(32, bytes.Length);
            for (int i = 0; i < sampleCount; i++)
                hash = hash * 31 + bytes[i];

            int start = Mathf.Max(sampleCount, bytes.Length - sampleCount);
            for (int i = start; i < bytes.Length; i++)
                hash = hash * 31 + bytes[i];

            return hash;
        }
    }

    private void SubscribeSharedMediaReceived()
    {
        if (!TryResolveNetworkPlayerType() || _sharedMediaReceivedHandler != null)
            return;

        _sharedMediaReceivedEvent = _networkPlayerType.GetEvent("onSharedMediaReceived", BindingFlags.Public | BindingFlags.Static);
        if (_sharedMediaReceivedEvent == null)
            return;

        MethodInfo handlerMethod = GetType().GetMethod(nameof(HandleSharedMediaReceived), BindingFlags.Instance | BindingFlags.NonPublic);
        if (handlerMethod == null)
            return;

        try
        {
            _sharedMediaReceivedHandler = Delegate.CreateDelegate(_sharedMediaReceivedEvent.EventHandlerType, this, handlerMethod);
            _sharedMediaReceivedEvent.AddEventHandler(null, _sharedMediaReceivedHandler);
        }
        catch (Exception ex)
        {
            _sharedMediaReceivedHandler = null;
            if (verboseLogging)
                Debug.LogWarning($"[M_NetworkedPhotoSync] Could not subscribe to shared media events: {ex.Message}");
        }
    }

    private void UnsubscribeSharedMediaReceived()
    {
        if (_sharedMediaReceivedEvent == null || _sharedMediaReceivedHandler == null)
            return;

        try
        {
            _sharedMediaReceivedEvent.RemoveEventHandler(null, _sharedMediaReceivedHandler);
        }
        catch (Exception ex)
        {
            if (verboseLogging)
                Debug.LogWarning($"[M_NetworkedPhotoSync] Could not unsubscribe from shared media events: {ex.Message}");
        }
        finally
        {
            _sharedMediaReceivedEvent = null;
            _sharedMediaReceivedHandler = null;
        }
    }

    private bool TryResolveNetworkPlayerType()
    {
        if (_networkPlayerType != null)
            return true;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            _networkPlayerType = assemblies[i].GetType("XRMultiplayer.XRINetworkPlayer");
            if (_networkPlayerType != null)
                return true;
        }

        return false;
    }

    private bool IsReadyLocalPlayer(object localPlayer)
    {
        return localPlayer != null &&
               GetBoolMemberValue(localPlayer, "IsSpawned", false) &&
               GetBoolMemberValue(localPlayer, "IsOwner", false);
    }

    private void InvokeBroadcastSharedMedia(object localPlayer, string fileName, byte[] encodedBytes)
    {
        MethodInfo method = localPlayer.GetType().GetMethod("BroadcastSharedMedia", BindingFlags.Public | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] Local network player has no BroadcastSharedMedia method.");
            return;
        }

        try
        {
            method.Invoke(localPlayer, new object[] { fileName, encodedBytes });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] BroadcastSharedMedia failed: {ex.Message}");
        }
    }

    private static object GetStaticMemberValue(Type type, string memberName)
    {
        if (type == null || string.IsNullOrWhiteSpace(memberName))
            return null;

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
        if (property != null)
            return property.GetValue(null, null);

        FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
        return field != null ? field.GetValue(null) : null;
    }

    private static bool GetBoolMemberValue(object target, string memberName, bool fallback)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return fallback;

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null && property.PropertyType == typeof(bool))
            return (bool)property.GetValue(target, null);

        FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(target);

        return fallback;
    }
}

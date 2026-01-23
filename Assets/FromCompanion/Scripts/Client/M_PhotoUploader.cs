using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Client-side picker/uploader. Uses UDP discovery to set the server, then uploads image bytes.
/// </summary>
public class M_PhotoUploader : MonoBehaviour
{
    [Header("References")]
    public M_NetworkDiscoveryClient discoveryClient;

    [Header("UI (Optional Preview)")]
    public RawImage previewImage;
    public TMP_Text fileNameText;

    [Header("Image Resize & Compression")]
    public int maxUploadDimension = 1024;

    [Range(1, 100)]
    public int jpegQuality = 85;

    private M_RestClient _restClient;

    private void Awake()
    {
        _restClient = M_RestClient.Instance;
    }

    private IEnumerator Start()
    {
        // If discovery client exists, try to discover on startup.
        if (discoveryClient != null)
        {
            yield return StartCoroutine(discoveryClient.DiscoverServerCoroutine());
            if (!string.IsNullOrEmpty(discoveryClient.discoveredIp) && discoveryClient.discoveredPort > 0)
                _restClient.SetServer(discoveryClient.discoveredIp, discoveryClient.discoveredPort);
        }
    }

    /// <summary>
    /// Call this with the JPEG bytes you want to upload.
    /// (If you're using NativeGallery, call this after you have downsizedBytes.)
    /// </summary>
    public void UploadBytes(byte[] downsizedBytes)
    {
        if (downsizedBytes == null || downsizedBytes.Length == 0)
        {
            Debug.LogError("[M_PhotoUploader] UploadBytes called with empty data.");
            return;
        }

        // Refresh server if discovery just happened
        if (discoveryClient != null &&
            !string.IsNullOrEmpty(discoveryClient.discoveredIp) &&
            discoveryClient.discoveredPort > 0)
        {
            _restClient.SetServer(discoveryClient.discoveredIp, discoveryClient.discoveredPort);
        }

        if (string.IsNullOrEmpty(_restClient.ServerBaseUrl))
        {
            Debug.LogError("[M_PhotoUploader] No server discovered (ServerBaseUrl empty). Upload aborted.");
            return;
        }

        StartCoroutine(_restClient.UploadPhoto(downsizedBytes));
    }
}
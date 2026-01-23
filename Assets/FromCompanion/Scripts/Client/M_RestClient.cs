using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class M_RestClient : MonoBehaviour
{
    private static M_RestClient _instance;
    public static M_RestClient Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<M_RestClient>();
                if (_instance == null)
                {
                    var go = new GameObject("M_RestClient");
                    _instance = go.AddComponent<M_RestClient>();
                    DontDestroyOnLoad(go);
                }
                Debug.Log("[M_RestClient] Instance found/created.");
            }
            return _instance;
        }
    }

    [Header("Server Settings")]
    [SerializeField] private string serverIp = "10.0.0.68";  // or whatever your Mac IP is
    [SerializeField] private int serverPort = 8080;

    public string ServerBaseUrl => $"http://{serverIp}:{serverPort}";

    public void SetServer(string ip, int port)
    {
        serverIp = ip;
        serverPort = port;
        Debug.Log($"[M_RestClient] Server set to {ServerBaseUrl}");
    }

    /// <summary>
    /// Uploads raw JPEG bytes directly as the HTTP request body.
    /// NO form, NO multipart, just the image bytes.
    /// </summary>
    public IEnumerator UploadPhoto(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogError("[M_RestClient] UploadPhoto called with empty imageBytes.");
            yield break;
        }

        string url = $"{ServerBaseUrl}/upload-photo";
        Debug.Log($"[M_RestClient] UploadPhoto started. Url={url}, bytes={imageBytes.Length}");

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            // RAW BODY
            request.uploadHandler = new UploadHandlerRaw(imageBytes);
            request.downloadHandler = new DownloadHandlerBuffer();

            // Tell server what this is
            request.SetRequestHeader("Content-Type", "image/jpeg");

#if UNITY_2020_2_OR_NEWER
            request.timeout = 15; // seconds
#endif

            var op = request.SendWebRequest();
            yield return op;

#if UNITY_2020_2_OR_NEWER
            bool hasError = request.result != UnityWebRequest.Result.Success;
#else
            bool hasError = request.isNetworkError || request.isHttpError;
#endif

            if (hasError)
            {
                Debug.LogError($"[M_RestClient] Request result={request.result}, responseCode={request.responseCode}, error={request.error}");
                Debug.LogError($"[M_RestClient] Upload failed. Error={request.error}, ResponseCode={request.responseCode}, Body={request.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"[M_RestClient] Upload successful. ResponseCode={request.responseCode}, Body={request.downloadHandler.text}");
            }
        }
    }
}
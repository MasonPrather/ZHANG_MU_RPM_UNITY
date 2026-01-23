using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple REST client for posting image bytes to the discovered server.
/// Upload endpoint: POST /upload-photo (raw jpg bytes)
/// </summary>
public class M_RestClient : MonoBehaviour
{
    private static M_RestClient _instance;
    public static M_RestClient Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = FindObjectOfType<M_RestClient>();
            if (_instance == null)
            {
                var go = new GameObject("M_RestClient");
                _instance = go.AddComponent<M_RestClient>();
                DontDestroyOnLoad(go);
            }

            return _instance;
        }
    }

    public string ServerBaseUrl { get; private set; }

    public void SetServer(string ip, int port)
    {
        if (string.IsNullOrEmpty(ip) || port <= 0)
        {
            Debug.LogError("[M_RestClient] SetServer called with invalid ip/port.");
            return;
        }

        ServerBaseUrl = $"http://{ip}:{port}";
        Debug.Log($"[M_RestClient] ServerBaseUrl set to {ServerBaseUrl}");
    }

    public IEnumerator UploadPhoto(byte[] jpgBytes)
    {
        if (jpgBytes == null || jpgBytes.Length == 0)
        {
            Debug.LogError("[M_RestClient] UploadPhoto called with empty bytes.");
            yield break;
        }

        if (string.IsNullOrEmpty(ServerBaseUrl))
        {
            Debug.LogError("[M_RestClient] No ServerBaseUrl set; cannot upload.");
            yield break;
        }

        string url = $"{ServerBaseUrl}/upload-photo";

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(jpgBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "image/jpeg");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool hasError = req.result != UnityWebRequest.Result.Success;
#else
            bool hasError = req.isNetworkError || req.isHttpError;
#endif

            if (hasError)
            {
                Debug.LogError($"[M_RestClient] Upload failed. url={url} code={req.responseCode} err={req.error}");
                if (req.downloadHandler != null)
                    Debug.LogError($"[M_RestClient] Body={req.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"[M_RestClient] Upload OK. code={req.responseCode}");
            }
        }
    }
}
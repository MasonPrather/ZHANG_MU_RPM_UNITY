using System.IO;
using UnityEngine;

/// <summary>
/// Starts the local HTTP upload server and UDP discovery beacon server.
/// Uploads are saved under Application.persistentDataPath/Uploads (Quest-safe).
/// </summary>
public class M_ServerBootstrap : MonoBehaviour
{
    [Header("Ports")]
    public int httpPort = 8080;
    public int discoveryPort = 7777;

    private M_SimpleHttpServer _httpServer;

    private void Start()
    {
        string uploadRoot = Path.Combine(Application.persistentDataPath, "Uploads");
        Directory.CreateDirectory(uploadRoot);

        Debug.Log($"[ServerBootstrap] UploadRoot={uploadRoot}");
        Debug.Log($"[ServerBootstrap] Starting HTTP server on port {httpPort}");

        _httpServer = new M_SimpleHttpServer(httpPort, uploadRoot);
        _httpServer.Start();

        Debug.Log($"[ServerBootstrap] Starting Discovery server on UDP port {discoveryPort} (httpPort={httpPort})");

        var discovery = gameObject.AddComponent<M_NetworkDiscoveryServer>();
        discovery.discoveryPort = discoveryPort;
        discovery.httpPort = httpPort;
    }

    private void OnDisable()
    {
        // Quest can disable objects during lifecycle events; be safe.
        _httpServer?.Stop();
        _httpServer = null;
    }

    private void OnDestroy()
    {
        _httpServer?.Stop();
        _httpServer = null;
    }
}
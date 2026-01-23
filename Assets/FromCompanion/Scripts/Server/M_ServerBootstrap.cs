using System.IO;
using UnityEngine;

/// <summary>
/// Starts the local HTTP upload server and the UDP discovery server.
/// Uploads go to Application.persistentDataPath/Uploads (Quest-safe).
/// </summary>
public class M_ServerBootstrap : MonoBehaviour
{
    public int httpPort = 8080;
    public int discoveryPort = 7777;

    private M_SimpleHttpServer _httpServer;

    private void Start()
    {
        string uploadRoot = Path.Combine(Application.persistentDataPath, "Uploads");
        Directory.CreateDirectory(uploadRoot);

        Debug.Log($"[M_ServerBootstrap] Upload root: {uploadRoot}");

        Debug.Log($"[M_ServerBootstrap] Starting HTTP server on port {httpPort}");
        _httpServer = new M_SimpleHttpServer(httpPort, uploadRoot);
        _httpServer.Start();

        Debug.Log($"[M_ServerBootstrap] Starting Discovery server on port {discoveryPort}");
        var ds = gameObject.AddComponent<M_NetworkDiscoveryServer>();
        ds.discoveryPort = discoveryPort;
        ds.httpPort = httpPort;
    }

    private void OnDestroy()
    {
        _httpServer?.Stop();
        _httpServer = null;
    }
}
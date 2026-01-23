using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// Listens for UDP beacons and stores the discovered server IP + httpPort.
/// IMPORTANT: The correct HTTP target IP is the UDP sender endpoint IP (sender.Address),
/// not anything embedded in the message.
/// </summary>
public class M_NetworkDiscoveryClient : MonoBehaviour
{
    [Header("Discovery Settings")]
    public int discoveryPort = 7777;
    public float timeoutSeconds = 5f;

    [Header("Result (Read-Only)")]
    [HideInInspector] public string discoveredIp;
    [HideInInspector] public int discoveredPort;

    private UdpClient _udp;
    private IPEndPoint _any;

    private void OnEnable()
    {
        StartDiscovery();
    }

    private void OnDisable()
    {
        StopDiscovery();
    }

    public void StartDiscovery()
    {
        StopDiscovery();

        try
        {
            _any = new IPEndPoint(IPAddress.Any, 0);

            // Bind to ALL interfaces on the discovery port
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, discoveryPort));
            _udp.EnableBroadcast = true;
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            Debug.Log($"[M_NetworkDiscoveryClient] Listening for beacons on UDP {discoveryPort}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_NetworkDiscoveryClient] Failed to bind UDP {discoveryPort}: {ex}");
            _udp = null;
        }
    }

    public void StopDiscovery()
    {
        try { _udp?.Close(); } catch { }
        _udp = null;
    }

    public IEnumerator DiscoverServerCoroutine()
    {
        discoveredIp = null;
        discoveredPort = 0;

        float start = Time.unscaledTime;

        while (Time.unscaledTime - start < timeoutSeconds)
        {
            if (_udp == null)
                yield break;

            while (_udp.Available > 0)
            {
                byte[] bytes = null;
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);

                try
                {
                    bytes = _udp.Receive(ref sender);
                }
                catch
                {
                    bytes = null;
                }

                if (bytes == null || bytes.Length == 0)
                    continue;

                string msg = Encoding.UTF8.GetString(bytes).Trim();
                if (!msg.StartsWith("PHOTO_SERVER", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Parse httpPort from the beacon
                int httpPort = 0;
                var parts = msg.Split('|');
                foreach (var p in parts)
                {
                    if (p.StartsWith("httpPort=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(p.Substring("httpPort=".Length), out httpPort);
                    }
                }

                if (httpPort <= 0)
                    continue;

                // CRITICAL: Use sender endpoint IP
                discoveredIp = sender.Address.ToString();
                discoveredPort = httpPort;

                Debug.Log($"[M_NetworkDiscoveryClient] Found server at {discoveredIp}:{discoveredPort} (from UDP sender endpoint)");
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("[M_NetworkDiscoveryClient] Discovery timed out. No server found.");
    }
}
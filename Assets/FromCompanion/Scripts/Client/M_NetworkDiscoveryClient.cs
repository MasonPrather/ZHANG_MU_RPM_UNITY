using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using UnityEngine;

public class M_NetworkDiscoveryClient : MonoBehaviour
{
    [Header("Discovery Settings")]
    public int discoveryPort = 7777;
    public float timeoutSeconds = 5f;

    [HideInInspector] public string discoveredIp;
    [HideInInspector] public int discoveredPort;

    public IEnumerator DiscoverServerCoroutine()
    {
        discoveredIp = null;
        discoveredPort = 0;

        using (UdpClient client = new UdpClient())
        {
            client.EnableBroadcast = true;

            byte[] requestBytes = Encoding.UTF8.GetBytes("VR_DISCOVERY_REQUEST");
            IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);

            try
            {
                client.Send(requestBytes, requestBytes.Length, broadcastEndpoint);
                Debug.Log("[M_NetworkDiscoveryClient] Sent discovery broadcast");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[M_NetworkDiscoveryClient] Failed to send broadcast: {ex}");
                yield break;
            }

            float startTime = Time.time;

            while (Time.time - startTime < timeoutSeconds)
            {
                if (client.Available > 0)
                {
                    IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] respBytes = client.Receive(ref serverEndpoint);
                    string resp = Encoding.UTF8.GetString(respBytes).Trim();

                    if (resp.StartsWith("VR_SERVER_RESPONSE|"))
                    {
                        string[] parts = resp.Split('|');
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
                        {
                            discoveredIp = serverEndpoint.Address.ToString();
                            discoveredPort = port;
                            Debug.Log($"[M_NetworkDiscoveryClient] Found server at {discoveredIp}:{discoveredPort}");
                            yield break;
                        }
                    }
                }

                yield return null;
            }

            Debug.LogWarning("[M_NetworkDiscoveryClient] Discovery timed out. No server found.");
        }
    }
}
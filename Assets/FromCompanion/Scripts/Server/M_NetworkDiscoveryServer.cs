using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Net.NetworkInformation;

public class M_NetworkDiscoveryServer : MonoBehaviour
{
    [Header("Ports")]
    [Tooltip("UDP discovery port that clients will listen on")]
    public int discoveryPort = 7777;

    [Tooltip("HTTP port your M_SimpleHttpServer is listening on")]
    public int httpPort = 8080;

    private UdpClient udp;
    private Thread thread;
    private bool running;

    private void Start()
    {
        try
        {
            udp = new UdpClient();
            udp.EnableBroadcast = true;

            running = true;
            thread = new Thread(BroadcastLoop)
            {
                IsBackground = true
            };
            thread.Start();

            Debug.Log($"[M_NetworkDiscoveryServer] Broadcasting PHOTO_SERVER beacons on UDP port {discoveryPort}, httpPort={httpPort}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_NetworkDiscoveryServer] Failed to start: {ex}");
        }
    }

    private void OnDestroy()
    {
        running = false;

        try
        {
            udp?.Close();
        }
        catch { }
    }

    private void BroadcastLoop()
    {
        IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);

        while (running)
        {
            try
            {
                string localIP = GetLocalIPv4();
                if (!string.IsNullOrEmpty(localIP))
                {
                    // Payload the client expects:
                    // "PHOTO_SERVER|<ip>|<httpPort>"
                    string msg = $"PHOTO_SERVER|{localIP}|{httpPort}";
                    byte[] bytes = Encoding.UTF8.GetBytes(msg);
                    udp.Send(bytes, bytes.Length, broadcastEndpoint);
                    // Debug.Log($"[M_NetworkDiscoveryServer] Broadcast: {msg}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[M_NetworkDiscoveryServer] BroadcastLoop error: {ex}");
            }

            Thread.Sleep(1000); // 1 second
        }
    }

    private string GetLocalIPv4()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_NetworkDiscoveryServer] GetLocalIPv4 failed: {ex}");
        }

        return null;
    }
}
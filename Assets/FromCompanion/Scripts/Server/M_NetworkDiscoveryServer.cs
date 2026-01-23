using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// Broadcasts discovery beacons so clients can find the HTTP upload server.
/// Sends to 255.255.255.255 AND subnet broadcast addresses (more robust on real networks).
/// Beacon format: "PHOTO_SERVER|httpPort=8080"
/// </summary>
public class M_NetworkDiscoveryServer : MonoBehaviour
{
    [Header("Ports")]
    public int discoveryPort = 7777;
    public int httpPort = 8080;

    [Header("Broadcast")]
    [Tooltip("Seconds between beacons.")]
    public float beaconIntervalSeconds = 1.0f;

    private UdpClient _udp;
    private float _nextSendTime;

    private void OnEnable()
    {
        try
        {
            _udp = new UdpClient();
            _udp.EnableBroadcast = true;
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            Debug.Log($"[M_NetworkDiscoveryServer] Broadcasting PHOTO_SERVER beacons on UDP port {discoveryPort}, httpPort={httpPort}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_NetworkDiscoveryServer] Failed to init UDP: {ex}");
        }
    }

    private void OnDisable()
    {
        try { _udp?.Close(); } catch { }
        _udp = null;
    }

    private void Update()
    {
        if (_udp == null) return;
        if (Time.unscaledTime < _nextSendTime) return;

        _nextSendTime = Time.unscaledTime + Mathf.Max(0.05f, beaconIntervalSeconds);

        string msg = $"PHOTO_SERVER|httpPort={httpPort}";
        byte[] data = Encoding.UTF8.GetBytes(msg);

        // 1) Global broadcast
        SendTo(new IPEndPoint(IPAddress.Broadcast, discoveryPort), data);

        // 2) Subnet broadcasts (often required on managed networks/routers)
        foreach (var ep in GetSubnetBroadcastEndpoints(discoveryPort))
            SendTo(ep, data);
    }

    private void SendTo(IPEndPoint ep, byte[] data)
    {
        try
        {
            _udp.Send(data, data.Length, ep);
        }
        catch
        {
            // Swallow per-send errors; some endpoints may fail depending on routing.
        }
    }

    private static IEnumerable<IPEndPoint> GetSubnetBroadcastEndpoints(int port)
    {
        var list = new List<IPEndPoint>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                // Skip loopback/tunnels
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(ua.Address))
                        continue;

                    if (ua.IPv4Mask == null)
                        continue;

                    var bcast = ComputeBroadcastAddress(ua.Address, ua.IPv4Mask);
                    if (bcast != null)
                        list.Add(new IPEndPoint(bcast, port));
                }
            }
        }
        catch
        {
            // ignore
        }

        // Deduplicate
        var seen = new HashSet<string>();
        foreach (var ep in list)
        {
            string key = ep.Address.ToString();
            if (seen.Add(key))
                yield return ep;
        }
    }

    private static IPAddress ComputeBroadcastAddress(IPAddress ip, IPAddress mask)
    {
        byte[] ipBytes = ip.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        if (ipBytes.Length != 4 || maskBytes.Length != 4) return null;

        byte[] bcast = new byte[4];
        for (int i = 0; i < 4; i++)
            bcast[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));

        return new IPAddress(bcast);
    }
}
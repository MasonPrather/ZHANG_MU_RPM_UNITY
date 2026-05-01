using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class M_QuestLanAdvertiserUdp : MonoBehaviour
{
    public M_QuestSignalingHostTcp signalingHost;

    public int broadcastPort = 7777;
    public float broadcastInterval = 1.0f;

    public string beaconPrefix = "MURPM";
    public string deviceName = "QuestHost";

    private UdpClient _udp;
    private float _nextSend;

    private void OnEnable()
    {
        _udp = new UdpClient();
        _udp.EnableBroadcast = true;
        _nextSend = Time.time + 0.25f;
    }

    private void OnDisable()
    {
        try { _udp?.Close(); } catch { }
        _udp = null;
    }

    private void Update()
    {
        if (_udp == null || signalingHost == null) return;
        if (Time.time < _nextSend) return;

        _nextSend = Time.time + broadcastInterval;

        string payload = $"{beaconPrefix}|sig={signalingHost.port}|name={deviceName}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);

        try
        {
            var ep = new IPEndPoint(IPAddress.Broadcast, broadcastPort);
            _udp.Send(bytes, bytes.Length, ep);
        }
        catch { }
    }
}

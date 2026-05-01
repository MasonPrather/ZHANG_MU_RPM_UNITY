using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class M_QuestSignalingHostTcp : MonoBehaviour
{
    public int port = 29000;
    public float helloTimeoutSeconds = 5f;

    public M_PairingCodeProvider codeProvider;

    public string HostIp { get; private set; } = "0.0.0.0";
    public bool IsClientConnected => _client != null && _client.Connected;

    public event Action OnClientConnected;
    public event Action<string> OnClientRejected;
    public event Action OnClientDisconnected;
    public event Action<string> OnJsonReceived;

    private TcpListener _listener;
    private TcpClient _client;
    private NetworkStream _stream;
    private Thread _thread;
    private volatile bool _running;

    private readonly object _sendLock = new object();
    private static readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    [Serializable]
    private class HelloMsg
    {
        public string type;
        public string code;
        public string client;
    }

    private void Start()
    {
        HostIp = M_AndroidIpUtil.GetLocalWifiIp();
        StartServer();
    }

    private void Update()
    {
        while (_main.TryDequeue(out var a))
            a?.Invoke();
    }

    private void OnDestroy()
    {
        StopServer();
    }

    public void StartServer()
    {
        if (_running) return;

        _running = true;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _thread = new Thread(ServerLoop) { IsBackground = true };
        _thread.Start();
    }

    public void StopServer()
    {
        _running = false;

        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        try { _listener?.Stop(); } catch { }

        _stream = null;
        _client = null;
        _listener = null;

        try { _thread?.Join(200); } catch { }
        _thread = null;
    }

    public void SendJson(string json)
    {
        if (!IsClientConnected || _stream == null) return;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] len = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));

            lock (_sendLock)
            {
                _stream.Write(len, 0, len.Length);
                _stream.Write(payload, 0, payload.Length);
                _stream.Flush();
            }
        }
        catch { }
    }

    private void ServerLoop()
    {
        while (_running)
        {
            TcpClient pending = null;

            try
            {
                pending = _listener.AcceptTcpClient();
                pending.NoDelay = true;

                ForceDisconnectActiveClient();

                _client = pending;
                _stream = _client.GetStream();
                _stream.ReadTimeout = Mathf.RoundToInt(helloTimeoutSeconds * 1000f);

                string helloJson = ReadFrame(_stream);
                if (!ValidateHello(helloJson, codeProvider != null ? codeProvider.PairingCode : null))
                {
                    SendImmediateRejectAndClose("bad_code_or_hello");
                    _main.Enqueue(() => OnClientRejected?.Invoke("Bad pairing code or invalid HELLO."));
                    continue;
                }

                _stream.ReadTimeout = Timeout.Infinite;

                SendJson("{\"type\":\"ack\"}");
                _main.Enqueue(() => OnClientConnected?.Invoke());

                while (_running && IsClientConnected)
                {
                    string json = ReadFrame(_stream);
                    if (json == null) break;

                    string captured = json;
                    _main.Enqueue(() => OnJsonReceived?.Invoke(captured));
                }

                CleanupActiveClient();
                _main.Enqueue(() => OnClientDisconnected?.Invoke());
            }
            catch
            {
                try { pending?.Close(); } catch { }
                Thread.Sleep(25);
            }
        }
    }

    private void SendImmediateRejectAndClose(string reason)
    {
        if (_stream == null) return;
        string msg = $"{{\"type\":\"reject\",\"reason\":\"{reason}\"}}";
        byte[] payload = Encoding.UTF8.GetBytes(msg);
        byte[] len = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
        _stream.Write(len, 0, len.Length);
        _stream.Write(payload, 0, payload.Length);
        _stream.Flush();
        CleanupActiveClient();
    }

    private void ForceDisconnectActiveClient()
    {
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        CleanupActiveClient();
    }

    private void CleanupActiveClient()
    {
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }

    private static string ReadFrame(NetworkStream stream)
    {
        byte[] lenBuf = ReadExact(stream, 4);
        if (lenBuf == null) return null;

        int len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf, 0));
        if (len <= 0 || len > (4 * 1024 * 1024)) return null;

        byte[] payload = ReadExact(stream, len);
        if (payload == null) return null;

        return Encoding.UTF8.GetString(payload);
    }

    private static byte[] ReadExact(NetworkStream stream, int bytes)
    {
        byte[] buf = new byte[bytes];
        int read = 0;
        while (read < bytes)
        {
            int r = stream.Read(buf, read, bytes - read);
            if (r <= 0) return null;
            read += r;
        }
        return buf;
    }

    private static bool ValidateHello(string helloJson, string expectedCode)
    {
        if (string.IsNullOrEmpty(helloJson) || string.IsNullOrEmpty(expectedCode))
            return false;

        try
        {
            var h = JsonUtility.FromJson<HelloMsg>(helloJson);
            if (h == null) return false;
            if (h.type != "hello") return false;
            return string.Equals(h.code, expectedCode, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Minimal HTTP server:
/// - GET /ping -> {"status":"pong"}
/// - POST /upload-photo (raw body) -> saves jpg and updates LastSavedPhotoPath.
/// </summary>
public class M_SimpleHttpServer
{
    public static string LastSavedPhotoPath;

    private readonly int _port;
    private readonly string _uploadRoot;

    private TcpListener _listener;
    private Thread _thread;
    private volatile bool _running;

    public M_SimpleHttpServer(int port, string uploadRoot)
    {
        _port = port;
        _uploadRoot = uploadRoot;

        try
        {
            Directory.CreateDirectory(_uploadRoot);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_SimpleHttpServer] Failed to create upload root '{_uploadRoot}': {ex}");
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _thread = new Thread(ListenLoop) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;

        try { _listener?.Stop(); } catch { }

        try
        {
            if (_thread != null && _thread.IsAlive)
                _thread.Join(500);
        }
        catch { }
    }

    private void ListenLoop()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            Debug.Log($"[M_SimpleHttpServer] Listening on 0.0.0.0:{_port}");

            while (_running)
            {
                if (!_listener.Pending())
                {
                    Thread.Sleep(10);
                    continue;
                }

                var client = _listener.AcceptTcpClient();
                client.NoDelay = true;
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_SimpleHttpServer] ListenLoop exception: {ex}");
        }
    }

    private void HandleClient(object state)
    {
        using (TcpClient client = (TcpClient)state)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                string remoteIp = remote != null ? remote.Address.ToString() : "UNKNOWN";

                byte[] headerBytes = ReadHeaders(stream, out int headerEndIndex);
                if (headerBytes == null || headerEndIndex < 0)
                {
                    Send(stream, 400, "{\"error\":\"bad_headers\"}");
                    return;
                }

                string headerText = Encoding.ASCII.GetString(headerBytes, 0, headerEndIndex);
                string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);

                if (lines.Length == 0)
                {
                    Send(stream, 400, "{\"error\":\"bad_request\"}");
                    return;
                }

                string[] req = lines[0].Split(' ');
                string method = req.Length > 0 ? req[0] : "";
                string path = req.Length > 1 ? req[1] : "/";

                int contentLength = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(lines[i].Substring("Content-Length:".Length).Trim(), out contentLength);
                        break;
                    }
                }

                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && path.Equals("/ping", StringComparison.OrdinalIgnoreCase))
                {
                    Send(stream, 200, "{\"status\":\"pong\"}");
                    return;
                }

                if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase) || !path.Equals("/upload-photo", StringComparison.OrdinalIgnoreCase))
                {
                    Send(stream, 404, "{\"error\":\"not_found\"}");
                    return;
                }

                if (contentLength <= 0)
                {
                    Send(stream, 400, "{\"error\":\"invalid_content_length\"}");
                    return;
                }

                Debug.Log($"[M_SimpleHttpServer] Request from {remoteIp}: POST /upload-photo, Content-Length={contentLength}");

                byte[] body = ReadBody(stream, headerBytes, headerEndIndex, contentLength);
                if (body == null)
                {
                    Send(stream, 400, "{\"error\":\"incomplete_body\"}");
                    return;
                }

                string saved = SavePhoto(body);
                if (string.IsNullOrEmpty(saved))
                {
                    Send(stream, 500, "{\"error\":\"save_failed\"}");
                    return;
                }

                Send(stream, 200, "{\"status\":\"ok\"}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[M_SimpleHttpServer] HandleClient exception: {ex}");
                try { Send(stream, 500, "{\"error\":\"server_error\"}"); } catch { }
            }
        }
    }

    private static byte[] ReadHeaders(NetworkStream stream, out int headerEndIndex)
    {
        headerEndIndex = -1;

        using (MemoryStream ms = new MemoryStream())
        {
            byte[] buf = new byte[4096];
            const int maxHeaderSize = 64 * 1024;

            while (true)
            {
                int read = stream.Read(buf, 0, buf.Length);
                if (read <= 0) return null;

                ms.Write(buf, 0, read);
                if (ms.Length > maxHeaderSize) return null;

                byte[] data = ms.GetBuffer();
                int len = (int)ms.Length;

                for (int i = 3; i < len; i++)
                {
                    if (data[i - 3] == (byte)'\r' &&
                        data[i - 2] == (byte)'\n' &&
                        data[i - 1] == (byte)'\r' &&
                        data[i] == (byte)'\n')
                    {
                        headerEndIndex = i - 3;
                        byte[] outBytes = new byte[len];
                        Buffer.BlockCopy(data, 0, outBytes, 0, len);
                        return outBytes;
                    }
                }
            }
        }
    }

    private static byte[] ReadBody(NetworkStream stream, byte[] headerBytes, int headerEndIndex, int contentLength)
    {
        int headerTotalLen = headerEndIndex + 4; // "\r\n\r\n"
        int already = headerBytes.Length - headerTotalLen;
        if (already < 0) already = 0;

        byte[] body = new byte[contentLength];
        int total = 0;

        if (already > 0)
        {
            int toCopy = Math.Min(already, contentLength);
            Buffer.BlockCopy(headerBytes, headerTotalLen, body, 0, toCopy);
            total += toCopy;
        }

        while (total < contentLength)
        {
            int read = stream.Read(body, total, contentLength - total);
            if (read <= 0) return null;
            total += read;
        }

        return body;
    }

    private string SavePhoto(byte[] data)
    {
        try
        {
            string fileName = $"photo_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.jpg";
            string fullPath = Path.Combine(_uploadRoot, fileName);

            File.WriteAllBytes(fullPath, data);
            LastSavedPhotoPath = fullPath;

            Debug.Log($"[M_SimpleHttpServer] Saved photo: {fullPath} (bytes={data.Length})");
            return fullPath;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_SimpleHttpServer] SavePhoto failed: {ex}");
            return null;
        }
    }

    private static void Send(NetworkStream stream, int status, string body)
    {
        string statusText = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            _ => "Internal Server Error"
        };

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string header =
            $"HTTP/1.1 {status} {statusText}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
    }
}
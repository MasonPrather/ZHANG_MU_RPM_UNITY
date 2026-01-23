using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Minimal HTTP server for receiving a raw image body via:
///   POST /upload-photo
/// Saves the body to disk and updates LastSavedPhotoPath for viewers to poll.
/// </summary>
public class M_SimpleHttpServer
{
    public static string LastSavedPhotoPath;

    private readonly int _port;
    private readonly string _uploadRoot;

    private TcpListener _listener;
    private Thread _listenerThread;
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
            Debug.LogError($"[HttpServer] Failed to create upload root '{_uploadRoot}': {ex}");
        }
    }

    public void Start()
    {
        if (_running) return;

        _running = true;
        _listenerThread = new Thread(ListenLoop) { IsBackground = true };
        _listenerThread.Start();
    }

    public void Stop()
    {
        _running = false;

        try { _listener?.Stop(); }
        catch { /* ignore */ }

        try
        {
            if (_listenerThread != null && _listenerThread.IsAlive)
                _listenerThread.Join(500);
        }
        catch { /* ignore */ }
    }

    private void ListenLoop()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            Debug.Log($"[HttpServer] Listening on 0.0.0.0:{_port}");

            while (_running)
            {
                if (!_listener.Pending())
                {
                    Thread.Sleep(10);
                    continue;
                }

                TcpClient client = _listener.AcceptTcpClient();
                client.NoDelay = true;

                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }
        catch (SocketException ex)
        {
            Debug.LogError($"[HttpServer] SocketException: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HttpServer] Exception: {ex}");
        }
    }

    private void HandleClient(object state)
    {
        using (TcpClient client = (TcpClient)state)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
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

                // Request line: METHOD PATH HTTP/1.1
                string[] req = lines[0].Split(' ');
                string method = req.Length > 0 ? req[0] : "";
                string path = req.Length > 1 ? req[1] : "/";

                int contentLength = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
                        break;
                    }
                }

                if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                    !path.Equals("/upload-photo", StringComparison.OrdinalIgnoreCase))
                {
                    Send(stream, 404, "{\"error\":\"not_found\"}");
                    return;
                }

                if (contentLength <= 0)
                {
                    Send(stream, 400, "{\"error\":\"invalid_content_length\"}");
                    return;
                }

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
                Debug.LogError($"[HttpServer] HandleClient exception: {ex}");
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

            Debug.Log($"[HttpServer] Saved photo: {fullPath} (bytes={data.Length})");
            return fullPath;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HttpServer] SavePhoto failed: {ex}");
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
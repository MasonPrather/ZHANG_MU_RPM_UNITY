using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Minimal local HTTP server for phone-to-Quest photo upload.
/// - GET / -> phone-friendly upload page.
/// - GET /ping -> {"status":"pong"}.
/// - POST /upload-photo -> accepts raw image bytes or multipart form uploads.
/// Uploaded files are saved under the configured upload root and exposed through LastSavedPhotoPath.
/// </summary>
public class M_SimpleHttpServer
{
    public static string LastSavedPhotoPath;

    private const int DefaultMaxUploadBytes = 64 * 1024 * 1024;

    private readonly int _port;
    private readonly string _uploadRoot;
    private readonly string _pairingCode;
    private readonly int _maxUploadBytes;
    private readonly bool _allowLegacyRawUploadsWithoutCode;

    private TcpListener _listener;
    private Thread _thread;
    private volatile bool _running;

    private sealed class UploadedFile
    {
        public string FileName;
        public string ContentType;
        public byte[] Data;
    }

    public M_SimpleHttpServer(int port, string uploadRoot)
        : this(port, uploadRoot, null, DefaultMaxUploadBytes, true)
    {
    }

    public M_SimpleHttpServer(int port, string uploadRoot, string pairingCode, int maxUploadBytes)
        : this(port, uploadRoot, pairingCode, maxUploadBytes, true)
    {
    }

    public M_SimpleHttpServer(int port, string uploadRoot, string pairingCode, int maxUploadBytes, bool allowLegacyRawUploadsWithoutCode)
    {
        _port = port;
        _uploadRoot = uploadRoot;
        _pairingCode = string.IsNullOrWhiteSpace(pairingCode) ? string.Empty : pairingCode.Trim();
        _maxUploadBytes = Mathf.Max(1024 * 1024, maxUploadBytes);
        _allowLegacyRawUploadsWithoutCode = allowLegacyRawUploadsWithoutCode;

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
        if (_running)
            return;

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

                TcpClient client = _listener.AcceptTcpClient();
                client.NoDelay = true;
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 30000;
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }
        catch (SocketException ex)
        {
            if (_running)
                Debug.LogError($"[M_SimpleHttpServer] ListenLoop socket exception: {ex}");
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
                IPEndPoint remote = client.Client.RemoteEndPoint as IPEndPoint;
                string remoteIp = remote != null ? remote.Address.ToString() : "UNKNOWN";

                byte[] requestBytes = ReadHeaders(stream, out int headerEndIndex);
                if (requestBytes == null || headerEndIndex < 0)
                {
                    SendJson(stream, 400, "{\"error\":\"bad_headers\"}");
                    return;
                }

                string headerText = Encoding.ASCII.GetString(requestBytes, 0, headerEndIndex);
                string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0)
                {
                    SendJson(stream, 400, "{\"error\":\"bad_request\"}");
                    return;
                }

                string[] req = lines[0].Split(' ');
                string method = req.Length > 0 ? req[0] : string.Empty;
                string target = req.Length > 1 ? req[1] : "/";

                Dictionary<string, string> headers = ParseHeaders(lines);
                Dictionary<string, string> query = ParseQuery(target);
                string path = GetRequestPath(target);
                int contentLength = GetContentLength(headers);

                if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    SendBytes(stream, 204, "text/plain; charset=utf-8", Array.Empty<byte>());
                    return;
                }

                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    HandleGet(stream, path);
                    return;
                }

                if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                    !path.Equals("/upload-photo", StringComparison.OrdinalIgnoreCase))
                {
                    SendJson(stream, 404, "{\"error\":\"not_found\"}");
                    return;
                }

                if (contentLength <= 0)
                {
                    SendJson(stream, 400, "{\"error\":\"invalid_content_length\"}");
                    return;
                }

                if (contentLength > _maxUploadBytes)
                {
                    SendJson(stream, 413, "{\"error\":\"upload_too_large\"}");
                    return;
                }

                Debug.Log($"[M_SimpleHttpServer] Request from {remoteIp}: POST /upload-photo, Content-Length={contentLength}");

                byte[] body = ReadBody(stream, requestBytes, headerEndIndex, contentLength);
                if (body == null)
                {
                    SendJson(stream, 400, "{\"error\":\"incomplete_body\"}");
                    return;
                }

                string contentType = GetHeader(headers, "Content-Type");
                UploadFiles(stream, body, contentType, headers, query);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[M_SimpleHttpServer] HandleClient exception: {ex}");
                try { SendJson(stream, 500, "{\"error\":\"server_error\"}"); } catch { }
            }
        }
    }

    private void HandleGet(NetworkStream stream, string path)
    {
        if (path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/upload", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            SendHtml(stream, 200, BuildUploadPageHtml());
            return;
        }

        if (path.Equals("/ping", StringComparison.OrdinalIgnoreCase))
        {
            SendJson(stream, 200, "{\"status\":\"pong\"}");
            return;
        }

        if (path.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            string latest = string.IsNullOrEmpty(LastSavedPhotoPath) ? string.Empty : EscapeJson(LastSavedPhotoPath);
            SendJson(stream, 200, "{\"status\":\"ok\",\"latestPath\":\"" + latest + "\"}");
            return;
        }

        if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            SendBytes(stream, 204, "image/x-icon", Array.Empty<byte>());
            return;
        }

        SendJson(stream, 404, "{\"error\":\"not_found\"}");
    }

    private void UploadFiles(
        NetworkStream stream,
        byte[] body,
        string contentType,
        Dictionary<string, string> headers,
        Dictionary<string, string> query)
    {
        List<UploadedFile> files = new List<UploadedFile>();
        Dictionary<string, string> formFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        bool isMultipartUpload = IsMultipart(contentType);

        if (isMultipartUpload)
        {
            string boundary = GetMultipartBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                SendJson(stream, 400, "{\"error\":\"missing_boundary\"}");
                return;
            }

            if (!TryParseMultipart(body, boundary, files, formFields))
            {
                SendJson(stream, 400, "{\"error\":\"bad_multipart\"}");
                return;
            }
        }
        else
        {
            files.Add(new UploadedFile
            {
                FileName = GetUploadedFileName(headers, query),
                ContentType = contentType,
                Data = body
            });
        }

        string submittedCode = GetSubmittedCode(headers, query, formFields);
        bool allowNoCodeLegacyUpload = _allowLegacyRawUploadsWithoutCode &&
                                       !isMultipartUpload &&
                                       string.IsNullOrWhiteSpace(submittedCode);

        if (!allowNoCodeLegacyUpload && !IsPairingCodeValid(submittedCode))
        {
            SendJson(stream, 403, "{\"error\":\"bad_code\"}");
            return;
        }

        if (files.Count == 0)
        {
            SendJson(stream, 400, "{\"error\":\"no_files\"}");
            return;
        }

        int savedCount = 0;
        string lastSaved = string.Empty;

        for (int i = 0; i < files.Count; i++)
        {
            UploadedFile file = files[i];
            if (file == null || file.Data == null || file.Data.Length == 0)
                continue;

            string saved = SavePhoto(file.Data, file.FileName, file.ContentType);
            if (string.IsNullOrEmpty(saved))
                continue;

            savedCount++;
            lastSaved = saved;
        }

        if (savedCount == 0)
        {
            SendJson(stream, 500, "{\"error\":\"save_failed\"}");
            return;
        }

        SendJson(stream, 200, "{\"status\":\"ok\",\"count\":" + savedCount.ToString(CultureInfo.InvariantCulture) +
                              ",\"latestPath\":\"" + EscapeJson(lastSaved) + "\"}");
    }

    private bool IsPairingCodeValid(string submittedCode)
    {
        if (string.IsNullOrEmpty(_pairingCode))
            return true;

        return string.Equals(_pairingCode, (submittedCode ?? string.Empty).Trim(), StringComparison.Ordinal);
    }

    private string GetSubmittedCode(
        Dictionary<string, string> headers,
        Dictionary<string, string> query,
        Dictionary<string, string> formFields)
    {
        string value = GetHeader(headers, "X-Pairing-Code");
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        if (query != null && query.TryGetValue("code", out value))
            return value;

        if (formFields != null && formFields.TryGetValue("code", out value))
            return value;

        return string.Empty;
    }

    private static bool IsMultipart(string contentType)
    {
        return !string.IsNullOrEmpty(contentType) &&
               contentType.IndexOf("multipart/form-data", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetMultipartBoundary(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return string.Empty;

        string[] pieces = contentType.Split(';');
        for (int i = 0; i < pieces.Length; i++)
        {
            string piece = pieces[i].Trim();
            if (!piece.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                continue;

            string boundary = piece.Substring("boundary=".Length).Trim();
            if (boundary.Length >= 2 && boundary[0] == '"' && boundary[boundary.Length - 1] == '"')
                boundary = boundary.Substring(1, boundary.Length - 2);

            return boundary;
        }

        return string.Empty;
    }

    private static bool TryParseMultipart(
        byte[] body,
        string boundary,
        List<UploadedFile> files,
        Dictionary<string, string> fields)
    {
        byte[] boundaryBytes = Encoding.ASCII.GetBytes("--" + boundary);
        byte[] headerTerminator = { 13, 10, 13, 10 };

        int position = FindBytes(body, boundaryBytes, 0);
        if (position < 0)
            return false;

        while (position >= 0 && position < body.Length)
        {
            position += boundaryBytes.Length;

            if (position + 1 < body.Length && body[position] == '-' && body[position + 1] == '-')
                break;

            if (position + 1 < body.Length && body[position] == 13 && body[position + 1] == 10)
                position += 2;

            int headersEnd = FindBytes(body, headerTerminator, position);
            if (headersEnd < 0)
                return false;

            string partHeaderText = Encoding.UTF8.GetString(body, position, headersEnd - position);
            Dictionary<string, string> partHeaders = ParseHeaderBlock(partHeaderText);
            string disposition = GetHeader(partHeaders, "Content-Disposition");
            string name = GetDispositionValue(disposition, "name");
            string fileName = GetDispositionValue(disposition, "filename");
            string partContentType = GetHeader(partHeaders, "Content-Type");

            int contentStart = headersEnd + headerTerminator.Length;
            int nextBoundary = FindBytes(body, boundaryBytes, contentStart);
            if (nextBoundary < 0)
                return false;

            int contentEnd = nextBoundary;
            if (contentEnd >= contentStart + 2 && body[contentEnd - 2] == 13 && body[contentEnd - 1] == 10)
                contentEnd -= 2;

            int contentLength = Mathf.Max(0, contentEnd - contentStart);
            byte[] content = new byte[contentLength];
            if (contentLength > 0)
                Buffer.BlockCopy(body, contentStart, content, 0, contentLength);

            if (!string.IsNullOrEmpty(fileName))
            {
                files.Add(new UploadedFile
                {
                    FileName = fileName,
                    ContentType = partContentType,
                    Data = content
                });
            }
            else if (!string.IsNullOrEmpty(name))
            {
                fields[name] = Encoding.UTF8.GetString(content).Trim();
            }

            position = nextBoundary;
        }

        return true;
    }

    private static string GetDispositionValue(string disposition, string key)
    {
        if (string.IsNullOrEmpty(disposition) || string.IsNullOrEmpty(key))
            return string.Empty;

        string[] pieces = disposition.Split(';');
        for (int i = 0; i < pieces.Length; i++)
        {
            string piece = pieces[i].Trim();
            if (!piece.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                continue;

            string value = piece.Substring(key.Length + 1).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2);

            return value;
        }

        return string.Empty;
    }

    private static int FindBytes(byte[] data, byte[] pattern, int startIndex)
    {
        if (data == null || pattern == null || pattern.Length == 0)
            return -1;

        int max = data.Length - pattern.Length;
        for (int i = Mathf.Max(0, startIndex); i <= max; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] == pattern[j])
                    continue;

                match = false;
                break;
            }

            if (match)
                return i;
        }

        return -1;
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
                if (read <= 0)
                    return null;

                ms.Write(buf, 0, read);
                if (ms.Length > maxHeaderSize)
                    return null;

                byte[] data = ms.GetBuffer();
                int len = (int)ms.Length;

                for (int i = 3; i < len; i++)
                {
                    if (data[i - 3] == 13 &&
                        data[i - 2] == 10 &&
                        data[i - 1] == 13 &&
                        data[i] == 10)
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
        int headerTotalLen = headerEndIndex + 4;
        int already = headerBytes.Length - headerTotalLen;
        if (already < 0)
            already = 0;

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
            if (read <= 0)
                return null;

            total += read;
        }

        return body;
    }

    private string SavePhoto(byte[] data, string originalFileName, string contentType)
    {
        try
        {
            string extension = ResolveImageExtension(data, originalFileName, contentType);
            string baseName = Path.GetFileNameWithoutExtension(SanitizeFileName(originalFileName));
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "phone_photo";

            string fileName = $"{baseName}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}{extension}";
            string fullPath = Path.Combine(_uploadRoot, fileName);

            File.WriteAllBytes(fullPath, data);
            LastSavedPhotoPath = fullPath;

            Debug.Log($"[M_SimpleHttpServer] Saved photo: {fullPath} (bytes={data.Length}, contentType={contentType ?? "unknown"})");
            return fullPath;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[M_SimpleHttpServer] SavePhoto failed: {ex}");
            return null;
        }
    }

    private static string ResolveImageExtension(byte[] data, string originalFileName, string contentType)
    {
        if (IsJpeg(data))
            return ".jpg";

        if (IsPng(data))
            return ".png";

        if (IsWebP(data))
            return ".webp";

        string ext = Path.GetExtension(originalFileName);
        if (!string.IsNullOrWhiteSpace(ext))
        {
            ext = ext.ToLowerInvariant();
            if (ext == ".jpeg")
                return ".jpg";

            if (ext == ".jpg" || ext == ".png" || ext == ".webp" || ext == ".gif" || ext == ".heic" || ext == ".heif")
                return ext;
        }

        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0)
                return ".png";

            if (contentType.IndexOf("webp", StringComparison.OrdinalIgnoreCase) >= 0)
                return ".webp";

            if (contentType.IndexOf("heic", StringComparison.OrdinalIgnoreCase) >= 0)
                return ".heic";
        }

        return ".jpg";
    }

    private static bool IsJpeg(byte[] data)
    {
        return data != null && data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
    }

    private static bool IsPng(byte[] data)
    {
        return data != null && data.Length >= 8 &&
               data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
               data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
    }

    private static bool IsWebP(byte[] data)
    {
        return data != null && data.Length >= 12 &&
               data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
               data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50;
    }

    private static string GetUploadedFileName(Dictionary<string, string> headers, Dictionary<string, string> query)
    {
        string value = GetHeader(headers, "X-File-Name");
        if (!string.IsNullOrWhiteSpace(value))
            return Uri.UnescapeDataString(value.Trim());

        if (query != null && query.TryGetValue("filename", out value) && !string.IsNullOrWhiteSpace(value))
            return value;

        return "phone_photo.jpg";
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "phone_photo";

        string safe = Path.GetFileName(value.Trim());
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            safe = safe.Replace(invalid[i], '_');

        return string.IsNullOrWhiteSpace(safe) ? "phone_photo" : safe;
    }

    private static Dictionary<string, string> ParseHeaders(string[] lines)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            string name = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            headers[name] = value;
        }

        return headers;
    }

    private static Dictionary<string, string> ParseHeaderBlock(string headerText)
    {
        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
        }

        return headers;
    }

    private static string GetHeader(Dictionary<string, string> headers, string name)
    {
        if (headers != null && headers.TryGetValue(name, out string value))
            return value;

        return string.Empty;
    }

    private static int GetContentLength(Dictionary<string, string> headers)
    {
        string value = GetHeader(headers, "Content-Length");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
    }

    private static string GetRequestPath(string target)
    {
        if (string.IsNullOrEmpty(target))
            return "/";

        int queryIndex = target.IndexOf('?');
        string path = queryIndex >= 0 ? target.Substring(0, queryIndex) : target;
        return string.IsNullOrEmpty(path) ? "/" : path;
    }

    private static Dictionary<string, string> ParseQuery(string target)
    {
        Dictionary<string, string> query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(target))
            return query;

        int queryIndex = target.IndexOf('?');
        if (queryIndex < 0 || queryIndex >= target.Length - 1)
            return query;

        string queryString = target.Substring(queryIndex + 1);
        string[] pairs = queryString.Split('&');
        for (int i = 0; i < pairs.Length; i++)
        {
            if (string.IsNullOrEmpty(pairs[i]))
                continue;

            int equals = pairs[i].IndexOf('=');
            string rawName = equals >= 0 ? pairs[i].Substring(0, equals) : pairs[i];
            string rawValue = equals >= 0 ? pairs[i].Substring(equals + 1) : string.Empty;

            string name = UrlDecode(rawName);
            if (string.IsNullOrEmpty(name))
                continue;

            query[name] = UrlDecode(rawValue);
        }

        return query;
    }

    private static string UrlDecode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return Uri.UnescapeDataString(value.Replace("+", " "));
    }

    private static void SendJson(NetworkStream stream, int status, string body)
    {
        SendBytes(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(body ?? "{}"));
    }

    private static void SendHtml(NetworkStream stream, int status, string body)
    {
        SendBytes(stream, status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(body ?? string.Empty));
    }

    private static void SendBytes(NetworkStream stream, int status, string contentType, byte[] bodyBytes)
    {
        string statusText;
        switch (status)
        {
            case 200:
                statusText = "OK";
                break;
            case 204:
                statusText = "No Content";
                break;
            case 400:
                statusText = "Bad Request";
                break;
            case 403:
                statusText = "Forbidden";
                break;
            case 404:
                statusText = "Not Found";
                break;
            case 413:
                statusText = "Payload Too Large";
                break;
            default:
                statusText = "Internal Server Error";
                break;
        }

        byte[] safeBody = bodyBytes ?? Array.Empty<byte>();
        string header =
            $"HTTP/1.1 {status} {statusText}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {safeBody.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Content-Type, X-File-Name, X-Pairing-Code\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);

        if (safeBody.Length > 0)
            stream.Write(safeBody, 0, safeBody.Length);
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private string BuildUploadPageHtml()
    {
        string requiresCode = string.IsNullOrEmpty(_pairingCode) ? "false" : "true";

        return @"<!doctype html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Photo Upload</title>
<style>
body{margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#f7f7f4;color:#161616}
main{max-width:560px;margin:0 auto;padding:28px 20px 40px}
h1{font-size:28px;line-height:1.1;margin:0 0 10px}
p{font-size:16px;line-height:1.45;margin:0 0 18px;color:#4c4c4c}
label{display:block;font-weight:700;margin:18px 0 8px}
input{box-sizing:border-box;width:100%;font-size:18px;border:1px solid #b9b9b2;border-radius:8px;padding:13px;background:white}
input[type=file]{padding:12px}
button{width:100%;margin-top:22px;border:0;border-radius:8px;background:#1f6feb;color:white;font-size:18px;font-weight:700;padding:15px}
button:disabled{background:#9aa9bd}
.status{margin-top:18px;padding:14px;border-radius:8px;background:#fff;border:1px solid #ddd;min-height:22px}
.hint{font-size:14px;color:#666;margin-top:12px}
</style>
</head>
<body>
<main>
<h1>Add photos</h1>
<p>Choose photos from this phone. They will appear in the headset after upload.</p>
<label for='code'>Code from headset</label>
<input id='code' inputmode='numeric' autocomplete='one-time-code' placeholder='Enter code'>
<label for='photos'>Photos</label>
<input id='photos' type='file' accept='image/*' multiple>
<button id='upload'>Upload photos</button>
<div id='status' class='status'>Ready.</div>
<div class='hint'>Phone and headset must be on the same Wi-Fi network.</div>
</main>
<script>
const requiresCode = " + requiresCode + @";
const maxDim = 2048;
const quality = 0.86;
const code = document.getElementById('code');
const photos = document.getElementById('photos');
const button = document.getElementById('upload');
const statusBox = document.getElementById('status');
const params = new URLSearchParams(location.search);
if (params.get('code')) code.value = params.get('code');
function setStatus(text){ statusBox.textContent = text; }
function safeName(name){ return (name || 'phone-photo').replace(/\.[^.]+$/, '').replace(/[^a-z0-9._-]+/gi, '_') + '.jpg'; }
function imageToJpeg(file){
  return new Promise((resolve,reject)=>{
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(url);
      const scale = Math.min(1, maxDim / Math.max(img.naturalWidth || img.width, img.naturalHeight || img.height));
      const w = Math.max(1, Math.round((img.naturalWidth || img.width) * scale));
      const h = Math.max(1, Math.round((img.naturalHeight || img.height) * scale));
      const canvas = document.createElement('canvas');
      canvas.width = w;
      canvas.height = h;
      const ctx = canvas.getContext('2d');
      ctx.drawImage(img, 0, 0, w, h);
      canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('Could not encode image')), 'image/jpeg', quality);
    };
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('Could not read image')); };
    img.src = url;
  });
}
async function uploadBlob(blob, fileName){
  const res = await fetch('/upload-photo', {
    method: 'POST',
    headers: {
      'Content-Type': 'image/jpeg',
      'X-File-Name': fileName,
      'X-Pairing-Code': code.value.trim()
    },
    body: blob
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}
async function uploadFallback(file){
  const form = new FormData();
  form.append('code', code.value.trim());
  form.append('photo', file, file.name || 'phone-photo');
  const res = await fetch('/upload-photo', { method: 'POST', body: form });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}
button.addEventListener('click', async () => {
  const selected = Array.from(photos.files || []);
  if (requiresCode && !code.value.trim()) { setStatus('Enter the code from the headset.'); return; }
  if (!selected.length) { setStatus('Choose at least one photo.'); return; }
  button.disabled = true;
  let uploaded = 0;
  try {
    for (const file of selected) {
      setStatus('Preparing ' + file.name + '...');
      try {
        const jpeg = await imageToJpeg(file);
        setStatus('Uploading ' + file.name + '...');
        await uploadBlob(jpeg, safeName(file.name));
      } catch (e) {
        setStatus('Uploading original ' + file.name + '...');
        await uploadFallback(file);
      }
      uploaded++;
      setStatus('Uploaded ' + uploaded + ' of ' + selected.length + '.');
    }
    setStatus('Done. You can return to the headset.');
  } catch (e) {
    setStatus('Upload failed. Check the code and Wi-Fi, then try again.');
  } finally {
    button.disabled = false;
  }
});
</script>
</body>
</html>";
    }
}

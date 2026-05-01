using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Starts the local phone upload server and the UDP discovery server.
/// Uploads go to Application.persistentDataPath/Uploads (Quest-safe).
/// </summary>
public class M_ServerBootstrap : MonoBehaviour
{
    public event Action<M_ServerBootstrap> InstructionsPublished;

    public string EffectivePairingCode => _effectivePairingCode;
    public string PublishedInstructions => _publishedInstructions;
    public string PrimaryUploadUrl { get; private set; }

    public string[] PublishedUploadUrls
    {
        get
        {
            string[] copy = new string[_publishedUploadUrls.Length];
            Array.Copy(_publishedUploadUrls, copy, _publishedUploadUrls.Length);
            return copy;
        }
    }

    [Header("Ports")]
    public int httpPort = 8080;
    public int discoveryPort = 7777;

    [Header("Phone Upload")]
    [Tooltip("Maximum accepted HTTP request size in megabytes.")]
    public int maxUploadMegabytes = 64;

    [Tooltip("If true, phones must enter the short code shown in the headset before uploading.")]
    public bool requirePairingCode = true;

    [Tooltip("If true, existing Unity companion clients can keep posting raw image bytes without a code.")]
    public bool allowLegacyRawUploadsWithoutCode = true;

    [Tooltip("Optional fixed code for testing. Leave empty to generate a fresh code each run.")]
    public string pairingCode;

    [Header("UI (Optional)")]
    [Tooltip("Text shown in VR with the phone URL and pairing code.")]
    public TMP_Text phoneUploadInstructionsText;

    [Tooltip("Optional extra status label.")]
    public TMP_Text serverStatusText;

    [Tooltip("If true, use a scene TMP text named LatestPhotoInfo when no text fields are assigned.")]
    public bool autoFindInstructionsText = true;

    [Tooltip("If true, keep the phone URL/code visible even if another script tries to reuse the same label.")]
    public bool keepInstructionsTextStatic = true;

    [Tooltip("Maximum phone URLs to show in VR. More than one helps when Android/Unity reports a non-Wi-Fi interface first.")]
    public int maxDisplayedUrls = 3;

    private M_SimpleHttpServer _httpServer;
    private string _effectivePairingCode;
    private string _publishedInstructions;
    private string[] _publishedUploadUrls = new string[0];

    private sealed class NetworkAddressInfo
    {
        public string address;
        public string interfaceName;
        public NetworkInterfaceType interfaceType;
        public int score;
    }

    private void Start()
    {
        string uploadRoot = Path.Combine(Application.persistentDataPath, "Uploads");
        Directory.CreateDirectory(uploadRoot);

        Debug.Log($"[M_ServerBootstrap] Upload root: {uploadRoot}");

        _effectivePairingCode = requirePairingCode ? ResolvePairingCode() : string.Empty;
        int maxUploadBytes = Mathf.Max(1, maxUploadMegabytes) * 1024 * 1024;

        Debug.Log($"[M_ServerBootstrap] Starting HTTP server on port {httpPort}");
        _httpServer = new M_SimpleHttpServer(httpPort, uploadRoot, _effectivePairingCode, maxUploadBytes, allowLegacyRawUploadsWithoutCode);
        _httpServer.Start();

        Debug.Log($"[M_ServerBootstrap] Starting Discovery server on port {discoveryPort}");
        var ds = gameObject.AddComponent<M_NetworkDiscoveryServer>();
        ds.discoveryPort = discoveryPort;
        ds.httpPort = httpPort;

        ResolveInstructionTextReferences();
        PublishPhoneUploadInstructions();
    }

    private void OnDestroy()
    {
        _httpServer?.Stop();
        _httpServer = null;
    }

    private void LateUpdate()
    {
        if (!keepInstructionsTextStatic || string.IsNullOrEmpty(_publishedInstructions))
            return;

        RestoreInstructionsText(phoneUploadInstructionsText);

        if (serverStatusText != phoneUploadInstructionsText)
            RestoreInstructionsText(serverStatusText);
    }

    private string ResolvePairingCode()
    {
        string trimmed = string.IsNullOrWhiteSpace(pairingCode) ? string.Empty : pairingCode.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            return trimmed;

        return UnityEngine.Random.Range(100000, 999999).ToString();
    }

    private void PublishPhoneUploadInstructions()
    {
        List<NetworkAddressInfo> addresses = GetLocalIPv4Addresses();
        _publishedUploadUrls = BuildUploadUrls(addresses);
        PrimaryUploadUrl = _publishedUploadUrls.Length > 0 ? _publishedUploadUrls[0] : $"http://<quest-ip>:{httpPort}";

        string instructions = BuildPhoneUploadInstructions(addresses);

        _publishedInstructions = instructions;

        if (phoneUploadInstructionsText != null)
            phoneUploadInstructionsText.text = instructions;

        if (serverStatusText != null && serverStatusText != phoneUploadInstructionsText)
            serverStatusText.text = instructions;

        if (addresses.Count == 0)
        {
            Debug.LogWarning("[M_ServerBootstrap] Could not find a local IPv4 address. Check Wi-Fi/network connection.");
            Debug.Log($"[M_ServerBootstrap] Phone upload URL: http://<quest-ip>:{httpPort}");
        }
        else
        {
            for (int i = 0; i < addresses.Count; i++)
            {
                NetworkAddressInfo address = addresses[i];
                Debug.Log($"[M_ServerBootstrap] Phone upload URL: http://{address.address}:{httpPort} ({address.interfaceName}, {address.interfaceType}, score={address.score})");
            }
        }

        if (!string.IsNullOrEmpty(_effectivePairingCode))
            Debug.Log($"[M_ServerBootstrap] Phone upload code: {_effectivePairingCode}");

        InstructionsPublished?.Invoke(this);
    }

    private void RestoreInstructionsText(TMP_Text text)
    {
        if (text != null && text.text != _publishedInstructions)
            text.text = _publishedInstructions;
    }

    private string BuildPhoneUploadInstructions(List<NetworkAddressInfo> addresses)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Open on phone:");

        if (_publishedUploadUrls.Length == 0)
        {
            sb.AppendLine($"http://<quest-ip>:{httpPort}");
        }
        else
        {
            int count = Mathf.Max(1, maxDisplayedUrls);
            count = Math.Min(count, _publishedUploadUrls.Length);

            for (int i = 0; i < count; i++)
                sb.AppendLine(_publishedUploadUrls[i]);
        }

        if (!string.IsNullOrEmpty(_effectivePairingCode))
            sb.AppendLine($"Code: {_effectivePairingCode}");

        sb.Append("Same Wi-Fi. If one URL fails, try the next.");
        return sb.ToString();
    }

    private string[] BuildUploadUrls(List<NetworkAddressInfo> addresses)
    {
        if (addresses == null || addresses.Count == 0)
            return new string[0];

        string[] urls = new string[addresses.Count];
        for (int i = 0; i < addresses.Count; i++)
            urls[i] = $"http://{addresses[i].address}:{httpPort}";

        return urls;
    }

    private void ResolveInstructionTextReferences()
    {
        if (!autoFindInstructionsText)
            return;

        if (phoneUploadInstructionsText != null && serverStatusText != null)
            return;

        TMP_Text candidate = FindTextByName("PhoneUpload") ?? FindTextByName("LatestPhotoInfo");
        if (candidate == null)
            return;

        if (phoneUploadInstructionsText == null)
            phoneUploadInstructionsText = candidate;

        if (serverStatusText == null)
            serverStatusText = phoneUploadInstructionsText;
    }

    private static TMP_Text FindTextByName(string namePart)
    {
        if (string.IsNullOrEmpty(namePart))
            return null;

        TMP_Text[] texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
                continue;

            if (text.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                return text;
        }

        return null;
    }

    private static List<NetworkAddressInfo> GetLocalIPv4Addresses()
    {
        List<NetworkAddressInfo> addresses = new List<NetworkAddressInfo>();

        try
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                NetworkInterface networkInterface = interfaces[i];
                if (networkInterface == null || networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                {
                    if (address == null || address.Address == null)
                        continue;

                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(address.Address))
                        continue;

                    if (IsLinkLocalIPv4(address.Address))
                        continue;

                    string value = address.Address.ToString();
                    AddAddress(addresses, value, networkInterface.Name, networkInterface.NetworkInterfaceType, ScoreAddress(address.Address, networkInterface));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_ServerBootstrap] Failed to enumerate local IP addresses: {ex.Message}");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        AddAndroidWifiAddress(addresses);
#endif

        addresses.Sort((a, b) => b.score.CompareTo(a.score));
        return addresses;
    }

    private static void AddAddress(
        List<NetworkAddressInfo> addresses,
        string address,
        string interfaceName,
        NetworkInterfaceType interfaceType,
        int score)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        for (int i = 0; i < addresses.Count; i++)
        {
            if (addresses[i].address == address)
            {
                if (score > addresses[i].score)
                {
                    addresses[i].interfaceName = interfaceName;
                    addresses[i].interfaceType = interfaceType;
                    addresses[i].score = score;
                }

                return;
            }
        }

        addresses.Add(new NetworkAddressInfo
        {
            address = address,
            interfaceName = string.IsNullOrWhiteSpace(interfaceName) ? "unknown" : interfaceName,
            interfaceType = interfaceType,
            score = score
        });
    }

    private static int ScoreAddress(IPAddress address, NetworkInterface networkInterface)
    {
        int score = 0;
        string name = ((networkInterface.Name ?? string.Empty) + " " + (networkInterface.Description ?? string.Empty)).ToLowerInvariant();

        if (IsPrivateIPv4(address))
            score += 100;

        if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            score += 100;

        if (name.Contains("wlan") || name.Contains("wifi") || name.Contains("wi-fi") || name.Contains("en0"))
            score += 80;

        if (name.Contains("bridge") ||
            name.Contains("utun") ||
            name.Contains("awdl") ||
            name.Contains("llw") ||
            name.Contains("docker") ||
            name.Contains("veth") ||
            name.Contains("vmnet") ||
            name.Contains("virtualbox") ||
            name.Contains("tap") ||
            name.Contains("tun"))
        {
            score -= 150;
        }

        return score;
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }

    private static bool IsLinkLocalIPv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void AddAndroidWifiAddress(List<NetworkAddressInfo> addresses)
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext"))
            using (AndroidJavaObject wifiManager = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
            using (AndroidJavaObject wifiInfo = wifiManager.Call<AndroidJavaObject>("getConnectionInfo"))
            {
                int rawIp = wifiInfo.Call<int>("getIpAddress");
                if (rawIp == 0)
                    return;

                string address = string.Format(
                    "{0}.{1}.{2}.{3}",
                    rawIp & 0xff,
                    (rawIp >> 8) & 0xff,
                    (rawIp >> 16) & 0xff,
                    (rawIp >> 24) & 0xff);

                AddAddress(addresses, address, "android-wifi", NetworkInterfaceType.Wireless80211, 500);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_ServerBootstrap] Android Wi-Fi IP lookup failed: {ex.Message}");
        }
    }
#endif
}

using UnityEngine;

public static class M_AndroidIpUtil
{
#if UNITY_ANDROID && !UNITY_EDITOR
    public static string GetLocalWifiIp()
    {
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
            using var wifiInfo = wifiManager.Call<AndroidJavaObject>("getConnectionInfo");
            int ipInt = wifiInfo.Call<int>("getIpAddress");

            return string.Format("{0}.{1}.{2}.{3}",
                ipInt & 0xff,
                (ipInt >> 8) & 0xff,
                (ipInt >> 16) & 0xff,
                (ipInt >> 24) & 0xff
            );
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[M_AndroidIpUtil] Failed to fetch WiFi IP: {e.Message}");
            return "0.0.0.0";
        }
    }
#else
    public static string GetLocalWifiIp() => "0.0.0.0";
#endif
}

using UnityEngine.XR.OpenXR;

namespace UnityEngine.XR.Templates.VRMultiplayer
{
    public enum XRPlatformType
    {
        Quest,
        AndroidXR,
        Other,
        All
    }

    public class XRPlatformUnderstanding
    {
        const string RUNTIME_NAME_META = "Oculus";
        const string RUNTIME_NAME_ANDROID = "Android XR";

        /// <summary>
        /// The current platform based on the active XRSessionSubsystem.
        /// </summary>
        public static XRPlatformType CurrentPlatform
        {
            get
            {
                if (!k_Initialized)
                {
                    k_CurrentPlatform = GetCurrentXRPlatform();
                    k_Initialized = true;
                }
                return k_CurrentPlatform;
            }
        }

        static XRPlatformType k_CurrentPlatform = XRPlatformType.All;

        static bool k_Initialized = false;

        /// <summary>
        /// Returns the current platform based on the active XRSessionSubsystem.
        /// </summary>
        /// <returns>The current platform based on the active XRSessionSubsystem.</returns>
        static XRPlatformType GetCurrentXRPlatform()
        {
            // If we have already initialized, just return the current platform
            if (k_Initialized)
                return k_CurrentPlatform;

            var openXRRuntimeName = OpenXRRuntime.name;
            switch (openXRRuntimeName)
            {
                case RUNTIME_NAME_META:
                    Debug.Log("Meta runtime detected.");
                    k_CurrentPlatform = XRPlatformType.Quest;
                    break;
                case RUNTIME_NAME_ANDROID:
                    Debug.Log("Android XR runtime detected.");
                    k_CurrentPlatform = XRPlatformType.AndroidXR;
                    break;
                default:
                    Debug.Log($"Unknown OpenXR runtime detected: {openXRRuntimeName}");
                    k_CurrentPlatform = XRPlatformType.Other;
                    break;
            }

            k_Initialized = true;
            return k_CurrentPlatform;
        }
    }
}

using Unity.Netcode;
using XRMultiplayer;

namespace UnityEngine.XR.Templates.VRMultiplayer
{
    public class NetworkedPlatformSpecificVisuals : NetworkBehaviour
    {
        [SerializeField] Renderer m_HMDRenderer;
        [SerializeField] Color m_QuestColor = Color.blue;
        [SerializeField] Color m_AndroidColor = Color.green;

        [SerializeField] Color m_OtherColor = Color.red;

        XRINetworkPlayer m_NetworkPlayer;
        void Start()
        {
            if (!TryGetComponent(out m_NetworkPlayer))
            {
                Debug.LogError("NetworkedPlatformSpecificVisuals requires an XRINetworkPlayer component.");
                return;
            }

            UpdatePlatformVisuals(-1, m_NetworkPlayer.platformType.Value);
            m_NetworkPlayer.platformType.OnValueChanged += UpdatePlatformVisuals;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (m_NetworkPlayer != null)
                m_NetworkPlayer.platformType.OnValueChanged -= UpdatePlatformVisuals;
        }

        void UpdatePlatformVisuals(int oldtype, int platformType)
        {
            // Set up the visuals based on the platform type
            switch ((XRPlatformType)platformType)
            {
                case XRPlatformType.Quest:
                    m_HMDRenderer.material.SetColor("_BaseColor", m_QuestColor);
                    break;
                case XRPlatformType.AndroidXR:
                    m_HMDRenderer.material.SetColor("_BaseColor", m_AndroidColor);
                    break;
                case XRPlatformType.Other:
                    m_HMDRenderer.material.SetColor("_BaseColor", m_OtherColor);
                    break;
                default:
                    // Set up default visuals
                    break;
            }
        }
    }
}

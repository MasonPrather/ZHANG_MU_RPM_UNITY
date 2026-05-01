using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Content.Interaction;

namespace XRMultiplayer
{
    [RequireComponent(typeof(XRLever))]
    public class NetworkXRLever : NetworkBehaviour
    {
        /// <summary>
        /// The networked knob value.
        /// </summary>
        NetworkVariable<bool> m_NetworkedLeverValue = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        XRLever m_XRLever;

        void Awake()
        {
            // Get associated components
            if (!TryGetComponent(out m_XRLever))
            {
                Utils.Log("Missing Components! Disabling Now.", 2);
                enabled = false;
                return;
            }
        }

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_XRLever.onLeverActivate.AddListener(LeverChanged);
            m_XRLever.onLeverDeactivate.AddListener(LeverChanged);

            if (IsOwner)
            {
                m_NetworkedLeverValue.Value = m_XRLever.value;
            }
            else
            {
                m_XRLever.value = m_NetworkedLeverValue.Value;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            m_XRLever.onLeverActivate.RemoveListener(LeverChanged);
            m_XRLever.onLeverDeactivate.RemoveListener(LeverChanged);
        }

        void LeverChanged()
        {
            LeverChangedOwnerRpc(m_XRLever.value, NetworkManager.Singleton.LocalClientId);
        }

        [Rpc(SendTo.Owner)]
        void LeverChangedOwnerRpc(bool newValue, ulong clientId)
        {
            m_NetworkedLeverValue.Value = newValue;
            LeverChangedRpc(newValue, clientId);
        }

        [Rpc(SendTo.Everyone)]
        void LeverChangedRpc(bool newValue, ulong clientId)
        {
            if (clientId != NetworkManager.Singleton.LocalClientId)
            {
                m_XRLever.onLeverActivate.RemoveListener(LeverChanged);
                m_XRLever.onLeverDeactivate.RemoveListener(LeverChanged);
                m_XRLever.value = newValue;
                m_XRLever.onLeverActivate.AddListener(LeverChanged);
                m_XRLever.onLeverDeactivate.AddListener(LeverChanged);
            }
        }
    }
}

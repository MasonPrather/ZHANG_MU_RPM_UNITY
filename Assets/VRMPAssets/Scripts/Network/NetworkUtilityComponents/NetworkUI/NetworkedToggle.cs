using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

namespace XRMultiplayer
{
    /// <summary>
    /// Simple implementation of a Networked Toggle.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class NetworkedToggle : NetworkBehaviour
    {

        [SerializeField, Tooltip("Broadcast the value of the dropdown to all clients when a new client joins.")]
        bool m_BroadcastValueOnJoin = false;

        /// <summary>
        /// Networked variable to sync the state of the toggle on new clients joining.
        /// </summary>
        NetworkVariable<bool> m_NetworkToggleValue;

        /// <summary>
        /// Toggle associated with this component.
        /// </summary>
        Toggle m_Toggle;

        ///<inheritdoc/>
        private void Awake()
        {
            m_Toggle = GetComponent<Toggle>();
            m_NetworkToggleValue = new NetworkVariable<bool>(m_Toggle.isOn, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
            m_Toggle.onValueChanged.AddListener(UpdateToggleValue);
        }

        ///<inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (m_BroadcastValueOnJoin)
            {
                // Sync the value of the dropdown.
                m_Toggle.isOn = m_NetworkToggleValue.Value;
            }
            else
            {
                m_Toggle.SetIsOnWithoutNotify(m_NetworkToggleValue.Value);
            }

        }

        /// <summary>
        /// Called when the Toggle is updated by the local user.
        /// </summary>
        /// <param name="value">Value of the toggle.</param>
        void UpdateToggleValue(bool value)
        {
            UpdateToggleRpc(value);
        }

        /// <summary>
        /// Called from the local user to the Server when the local user has updated the toggle.
        /// </summary>
        /// <param name="value">Value of the toggle.</param>
        [Rpc(SendTo.NotMe)]
        void UpdateToggleRpc(bool value)
        {
            m_Toggle.onValueChanged.RemoveListener(UpdateToggleValue);
            m_Toggle.isOn = value;
            m_Toggle.onValueChanged.AddListener(UpdateToggleValue);
        }
    }
}

using UnityEngine;
using Unity.Netcode;
using TMPro;

namespace XRMultiplayer
{
    /// <summary>
    /// Simple implementation of a Networked Dropdown.
    /// </summary>
    [RequireComponent(typeof(TMP_Dropdown))]
    public class NetworkedDropdown : NetworkBehaviour
    {
        [SerializeField, Tooltip("Broadcast the value of the dropdown to all clients when a new client joins.")]
        bool m_BroadcastValueOnJoin = false;

        /// <summary>
        /// Networked Variable to sync the state of the dropdown on new clients joining.
        /// </summary>
        NetworkVariable<int> m_CurrentDropdownNetworkValue;

        /// <summary>
        /// Dropdown associated with this component.
        /// </summary>
        TMP_Dropdown m_Dropdown;

        ///<inheritdoc/>
        private void Awake()
        {
            m_Dropdown = GetComponent<TMP_Dropdown>();
            m_CurrentDropdownNetworkValue = new NetworkVariable<int>(m_Dropdown.value, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
            m_Dropdown.onValueChanged.AddListener(UpdateDropdown);
        }

        ///<inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (m_BroadcastValueOnJoin)
            {
                // Sync the value of the dropdown.
                m_Dropdown.value = m_CurrentDropdownNetworkValue.Value;
            }
            else
            {
                m_Dropdown.SetValueWithoutNotify(m_CurrentDropdownNetworkValue.Value);
            }
        }

        /// <summary>
        /// Called when the Dropdown is updated by the local user.
        /// </summary>
        /// <param name="dropdownValue">Value of the dropdown.</param>
        void UpdateDropdown(int dropdownValue)
        {
            UpdateDropdownRpc(dropdownValue);
        }

        /// <summary>
        /// Called from the local user to update the slider.
        /// </summary>
        /// <param name="dropdownValue">Value of the dropdown.</param>
        [Rpc(SendTo.NotMe)]
        void UpdateDropdownRpc(int dropdownValue)
        {
            //Remove listener here before updating value to prevent continuous looping
            m_Dropdown.onValueChanged.RemoveListener(UpdateDropdown);
            m_Dropdown.value = dropdownValue;
            m_Dropdown.onValueChanged.AddListener(UpdateDropdown);
        }
    }
}

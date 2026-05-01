using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

namespace XRMultiplayer
{
    /// <summary>
    /// Simple implementation of a Networked button.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class NetworkedButton : NetworkBehaviour
    {
        /// <summary>
        /// Button associated with this component.
        /// </summary>
        Button m_Button;

        ///<inheritdoc/>
        private void Awake()
        {
            m_Button = GetComponent<Button>();
            m_Button.onClick.AddListener(ButtonClicked);
        }

        /// <summary>
        /// Called when the button is clicked by the Local user.
        /// </summary>
        void ButtonClicked()
        {
            ClickButtonRpc();
        }

        /// <summary>
        /// Called from the local user to the Server when the local user has clicked the button.
        /// </summary>
        /// <param name="clientId">Local user Id.</param>
        [Rpc(SendTo.NotMe)]
        void ClickButtonRpc()
        {
            m_Button.onClick.RemoveListener(ButtonClicked);
            m_Button.onClick.Invoke();
            m_Button.onClick.AddListener(ButtonClicked);
        }
    }
}

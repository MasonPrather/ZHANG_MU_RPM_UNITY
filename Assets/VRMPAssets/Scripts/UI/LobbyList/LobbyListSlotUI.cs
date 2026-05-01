using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace XRMultiplayer
{
    public class LobbyListSlotUI : MonoBehaviour
    {
        [SerializeField] TMP_Text m_RoomNameText;
        [SerializeField] TMP_Text m_PlayerCountText;
        [SerializeField] Button m_JoinButton;
        [SerializeField] GameObject m_FullImage;
        [SerializeField] TMP_Text m_StatusText;
        [SerializeField] GameObject m_JoinImage;

        LobbyUI m_LobbyListUI;
        ISessionInfo m_Session;

        bool m_NonJoinable = false;

        public void CreateSessionUI(ISessionInfo session, LobbyUI lobbyListUI)
        {
            m_NonJoinable = false;
            m_Session = session;
            m_LobbyListUI = lobbyListUI;
            m_JoinButton.onClick.AddListener(JoinRoom);
            m_RoomNameText.text = session.Name;
            m_PlayerCountText.text = $"{session.MaxPlayers - session.AvailableSlots}/{session.MaxPlayers}";

            m_FullImage.SetActive(false);
            m_JoinImage.SetActive(false);
        }

        public void CreateNonJoinableLobbyUI(ISessionInfo session, LobbyUI lobbyListUI, string statusText)
        {
            m_NonJoinable = true;
            m_JoinButton.interactable = false;
            m_Session = session;
            m_LobbyListUI = lobbyListUI;
            m_RoomNameText.text = session.Name;
            m_StatusText.text = statusText;
            m_FullImage.SetActive(true);
            m_JoinImage.SetActive(false);
        }

        public void ToggleHover(bool toggle)
        {
            if (m_NonJoinable) return;
            if (toggle)
            {
                if (m_Session.AvailableSlots <= 0)
                {
                    m_JoinImage.SetActive(false);
                    m_FullImage.SetActive(true);
                    m_JoinButton.interactable = false;
                }
                else
                {
                    m_JoinImage.SetActive(true);
                    m_FullImage.SetActive(false);
                    m_JoinButton.interactable = true;
                }
            }
            else
            {
                m_FullImage.SetActive(false);
                m_JoinImage.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            m_JoinButton.onClick.RemoveListener(JoinRoom);
        }

        void JoinRoom()
        {
            m_LobbyListUI.JoinLobby(m_Session);
        }
    }
}

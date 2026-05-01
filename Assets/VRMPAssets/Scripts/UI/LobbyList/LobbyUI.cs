using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Services.Vivox;
using Unity.Services.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace XRMultiplayer
{
    public class LobbyUI : MonoBehaviour
    {
        /// <summary>
        ///  The timeout for direct join in seconds. This is the time we wait for a direct join to complete before giving up.
        ///  This is used to prevent the client from waiting indefinitely if the server is not responding.
        /// </summary>
        const float k_DirectJoinTimeout = 4.5f;

        enum ConnectionSubPanel
        {
            LobbyPanel = 0,
            CreationPanel = 1,
            ConnectionPanel = 2,
            ConnectionSuccessPanel = 3,
            ConnectionFailurePanel = 4,
            NoConnectionPanel = 5
        }

        [Header("Lobby List")]
        [SerializeField]
        Transform m_LobbyListParent;

        [SerializeField]
        GameObject m_LobbyListPrefab;

        [SerializeField]
        GameObject m_SessionPanelObject;

        [SerializeField]
        GameObject m_LocalPanelObject;

        [SerializeField]
        Button m_RefreshButton;

        [SerializeField]
        Image m_CooldownImage;

        [SerializeField]
        float m_AutoRefreshTime = 5.0f;

        [SerializeField]
        float m_RefreshCooldownTime = .5f;

        [Header("Connection Texts")]
        [SerializeField]
        TMP_Text m_ConnectionUpdatedText;

        [SerializeField]
        TMP_Text m_ConnectionSuccessText;

        [SerializeField]
        TMP_Text m_ConnectionFailedText;

        [Header("Room Creation")]
        [SerializeField]
        TMP_InputField m_RoomNameText;

        [SerializeField]
        Toggle m_PrivacyToggle;

        [SerializeField]
        GameObject[] m_ConnectionSubPanels;

        VoiceChatManager m_VoiceChatManager;

        Coroutine m_UpdateLobbiesRoutine;
        Coroutine m_CooldownFillRoutine;

        bool m_Private = false;
        int m_PlayerCount;

        private void Awake()
        {
            m_VoiceChatManager = FindFirstObjectByType<VoiceChatManager>();
            SessionManager.status.Subscribe(ConnectedUpdated);
            m_CooldownImage.enabled = false;
        }

        private void Start()
        {
            m_PrivacyToggle.onValueChanged.AddListener(TogglePrivacy);

            bool isLocal = XRINetworkGameManager.CurrentSessionType != SessionType.DistributedAuthority;
            m_SessionPanelObject.SetActive(!isLocal);
            m_LocalPanelObject.SetActive(isLocal);
            m_PlayerCount = XRINetworkGameManager.maxPlayers / 2;
            XRINetworkGameManager.Instance.OnConnectionFailedAction += FailedToConnect;
            XRINetworkGameManager.Instance.OnConnectionUpdated += ConnectedUpdated;

            foreach (Transform t in m_LobbyListParent)
            {
                Destroy(t.gameObject);
            }
        }

        void OnEnable()
        {
            ToggleConnectionSubPanel(ConnectionSubPanel.LobbyPanel);
        }

        private void OnDisable()
        {
            HideLobbies();
        }

        private void OnDestroy()
        {
            XRINetworkGameManager.Instance.OnConnectionFailedAction -= FailedToConnect;
            XRINetworkGameManager.Instance.OnConnectionUpdated -= ConnectedUpdated;

            SessionManager.status.Unsubscribe(ConnectedUpdated);
        }

        public void CreateLobby()
        {
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
            if (string.IsNullOrEmpty(m_RoomNameText.text) || m_RoomNameText.text == "<Room Name>")
            {
                m_RoomNameText.text = $"{XRINetworkGameManager.LocalPlayerName.Value}'s Room";
            }
            XRINetworkGameManager.Instance.CreateNewLobby(m_RoomNameText.text, m_Private, m_PlayerCount);
            m_ConnectionSuccessText.text = $"Joining {m_RoomNameText.text}";
        }

        public void CancelConnection()
        {
            XRINetworkGameManager.Instance.CancelMatchmaking();
        }

        /// <summary>
        /// Set the room name
        /// </summary>
        /// <param name="roomName">The name of the room</param>
        /// <remarks> This function is called from <see cref="XRIKeyboardDisplay"/>
        public void SetRoomName(string roomName)
        {
            if (!string.IsNullOrEmpty(roomName))
            {
                m_RoomNameText.text = roomName;
            }
        }

        /// <summary>
        /// Join a room by code
        /// </summary>
        /// <param name="roomCode">The room code to join</param>
        /// <remarks> This function is called from <see cref="XRIKeyboardDisplay"/>
        public void EnterRoomCode(string roomCode)
        {
            ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionPanel);
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
            XRINetworkGameManager.Instance.JoinLobbyByCode(roomCode.ToUpper());
            m_ConnectionSuccessText.text = $"Joining Room: {roomCode.ToUpper()}";
        }

        public void JoinLobby(ISessionInfo Session)
        {
            ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionPanel);
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
            XRINetworkGameManager.Instance.JoinLobbySpecific(Session);
            m_ConnectionSuccessText.text = $"Joining {Session.Name}";
        }

        public void QuickJoinLobby()
        {
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
            XRINetworkGameManager.Instance.QuickJoinLobby();
            m_ConnectionSuccessText.text = "Joining Random";
        }

        public void SetVoiceChatAudidibleDistance(int audibleDistance)
        {
            if (audibleDistance <= m_VoiceChatManager.ConversationalDistance)
            {
                audibleDistance = m_VoiceChatManager.ConversationalDistance + 1;
            }
            m_VoiceChatManager.AudibleDistance = audibleDistance;
        }

        public void SetVoiceChatConversationalDistance(int conversationalDistance)
        {
            m_VoiceChatManager.ConversationalDistance = conversationalDistance;
        }

        public void SetVoiceChatAudioFadeIntensity(float fadeIntensity)
        {
            m_VoiceChatManager.AudioFadeIntensity = fadeIntensity;
        }

        public void SetVoiceChatAudioFadeModel(int fadeModel)
        {
            m_VoiceChatManager.AudioFadeModel = (AudioFadeModel)fadeModel;
        }

        public void TogglePrivacy(bool toggle)
        {
            m_Private = toggle;
        }

        void ToggleConnectionSubPanel(ConnectionSubPanel panel)
        {
            ToggleConnectionSubPanel((int)panel);
        }

        public void ToggleConnectionSubPanel(int panelId)
        {
            for (int i = 0; i < m_ConnectionSubPanels.Length; i++)
            {
                m_ConnectionSubPanels[i].SetActive(i == panelId);
            }


            if (panelId == 0)
            {
                ShowLobbies();
            }
            else
            {
                HideLobbies();
            }
        }

        void OnConnected(bool connected)
        {
            if (connected)
            {
                ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionSuccessPanel);
                XRINetworkGameManager.Connected.Unsubscribe(OnConnected);
            }
        }

        void ConnectedUpdated(string update)
        {
            m_ConnectionUpdatedText.text = $"<b>Status:</b> {update}";
        }

        public void FailedToConnect(string reason)
        {
            ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionFailurePanel);
            m_ConnectionFailedText.text = $"<b>Error:</b> {reason}";
        }

        public void HideLobbies()
        {
            EnableRefresh();
            if (m_UpdateLobbiesRoutine != null) StopCoroutine(m_UpdateLobbiesRoutine);
        }

        public void ShowLobbies()
        {
            UpdateLobbyDisplay();
            if (m_UpdateLobbiesRoutine != null) StopCoroutine(m_UpdateLobbiesRoutine);
            m_UpdateLobbiesRoutine = StartCoroutine(UpdateAvailableLobbies());
        }

        IEnumerator UpdateAvailableLobbies()
        {
            while (true)
            {
                yield return new WaitForSeconds(m_AutoRefreshTime);
                UpdateLobbyDisplay();
            }
        }

        void EnableRefresh()
        {
            m_CooldownImage.enabled = false;
            m_RefreshButton.interactable = true;
        }

        IEnumerator UpdateButtonCooldown()
        {
            m_RefreshButton.interactable = false;

            m_CooldownImage.enabled = true;
            for (float i = 0; i < m_RefreshCooldownTime; i += Time.deltaTime)
            {
                m_CooldownImage.fillAmount = Mathf.Clamp01(i / m_RefreshCooldownTime);
                yield return null;
            }
            EnableRefresh();
        }

        async void UpdateLobbyDisplay()
        {
            if (m_CooldownImage.enabled || (int)XRINetworkGameManager.CurrentConnectionState.Value < 2) return;
            if (m_CooldownFillRoutine != null) StopCoroutine(m_CooldownFillRoutine);
            m_CooldownFillRoutine = StartCoroutine(UpdateButtonCooldown());

            await System.Threading.Tasks.Task.Yield(); // Wait for the end of the frame
            if (XRINetworkGameManager.CurrentSessionType == SessionType.LocalOnly)
                return;

            QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(SessionManager.GetQuickJoinFilterOptions());

            foreach (Transform t in m_LobbyListParent)
            {
                Destroy(t.gameObject);
            }

            if (results != null && results.Sessions.Count > 0)
            {
                foreach (var session in results.Sessions)
                {
                    if (SessionManager.CheckForSessionFilter(session))
                        continue;

                    if (SessionManager.CheckForIncompatibilityFilter(session))
                    {
                        LobbyListSlotUI newLobbyUI = Instantiate(m_LobbyListPrefab, m_LobbyListParent).GetComponent<LobbyListSlotUI>();
                        newLobbyUI.CreateNonJoinableLobbyUI(session, this, "Version Conflict");
                        continue;
                    }

                    if (SessionManager.CanJoinLobby(session))
                    {
                        LobbyListSlotUI newLobbyUI = Instantiate(m_LobbyListPrefab, m_LobbyListParent).GetComponent<LobbyListSlotUI>();
                        newLobbyUI.CreateSessionUI(session, this);
                    }
                }
            }
        }

        public void HostLocalRoom()
        {
            if (XRINetworkGameManager.Instance.HostLocalConnection())
            {
                ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionSuccessPanel);
            }
            else
            {
                Utils.LogError($"Failed to host local room:");
                m_ConnectionFailedText.text = $"<b>Error:</b> Room already exists or could not be created";
                ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionFailurePanel);
            }
        }

        public void JoinLocalRoom()
        {
            if (XRINetworkGameManager.Instance.JoinLocalConnection())
            {
                StartCoroutine(CheckForFailedConnection());
                ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionPanel);
            }
            else
            {
                FailedToJoinLocal();
            }
        }

        void FailedToJoinLocal()
        {
            Utils.LogError($"Failed to join local room:");
            m_ConnectionFailedText.text = $"<b>Error:</b> Room does not exist or could not be joined";
            ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionFailurePanel);
        }

        IEnumerator CheckForFailedConnection()
        {
            yield return new WaitForSeconds(k_DirectJoinTimeout);
            if (!NetworkManager.Singleton.IsConnectedClient)
            {
                NetworkManager.Singleton.Shutdown();
                FailedToJoinLocal();
            }
            else
            {
                ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionSuccessPanel);
            }
        }

        /// <summary>
        /// Called from the UI to set the IP address for joining a direct connection.
        /// </summary>
        /// <param name="address">IP address or DNS</param>
        public void SetIP(string address)
        {
            SetIPAsync(address);
        }

        /// <summary>
        ///  Asynchronously sets the IP address for joining a direct connection.
        ///  This method validates the IP address format and resolves it to ensure it's valid.
        /// </summary>
        /// <param name="address">IP address or DNS</param>
        public virtual async void SetIPAsync(string address)
        {
            // Validate the IP address format
            var hostEntry = await System.Net.Dns.GetHostEntryAsync(address);
            if (hostEntry == null || hostEntry.AddressList.Length == 0)
            {
                Utils.LogError($"Failed to resolve IP address: {address}");
                m_ConnectionFailedText.text = $"<b>Error:</b> Invalid IP address";
                ToggleConnectionSubPanel(ConnectionSubPanel.ConnectionFailurePanel);
                return;
            }

            // If multiple addresses are found, log a warning as we only use the first one
            if (hostEntry.AddressList.Length > 1)
                Utils.LogWarning($"Multiple IP addresses found for {address}. Using the first one: {hostEntry.AddressList[0]}");

            var ipAddress = hostEntry.AddressList[0].ToString();
            var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            transport.SetConnectionData(ipAddress, transport.ConnectionData.Port); // Assuming default port is 7777, change as needed
        }

        /// <summary>
        /// Called from the UI buttons to update the max players allowed per room.
        /// </summary>
        /// <param name="count">Amount of players to allow.</param>
        public void UpdatePlayerCount(int count)
        {
            m_PlayerCount = Mathf.Clamp(count, 1, XRINetworkGameManager.maxPlayers);
        }
    }
}

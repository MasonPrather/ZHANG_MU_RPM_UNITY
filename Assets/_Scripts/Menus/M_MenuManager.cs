using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using XRMultiplayer;

namespace XRMultiplayer
{
    public class M_MenuManager : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private GameObject lobbyPanel;

        [Header("Lobby UI")]
        [SerializeField] private TMP_Text loadingText;
        [SerializeField] private TMP_Text lobbyPlayerListText;
        [SerializeField] private TMP_Text lobbyStatusText;
        [SerializeField] private Button startGameButton;

        [Header("Settings")]
        [SerializeField] private string roomName = "VRRoom";
        [SerializeField] private InputActionProperty menuButtonAction;

        private GameObject lobbyMenu;
        private bool host = false;
        private bool connectionHandled = false;

        private void Start()
        {
            ShowMainMenu();

            if (menuButtonAction != null)
            {
                menuButtonAction.action.Enable();
                menuButtonAction.action.performed += OnMenuButtonPressed;
            }
        }

        private void OnDestroy()
        {
            if (menuButtonAction != null)
            {
                menuButtonAction.action.performed -= OnMenuButtonPressed;
            }

            XRINetworkGameManager.Connected.Unsubscribe(OnConnected);
        }

        private void OnMenuButtonPressed(InputAction.CallbackContext context)
        {
            if (mainMenuPanel == null) return;

            bool isActive = mainMenuPanel.activeSelf;
            mainMenuPanel.SetActive(!isActive);

            if (!isActive)
                loadingPanel.SetActive(false);
        }

        public void OnHostGameClicked()
        {
            if (XRINetworkGameManager.Instance == null) return;

            Debug.Log("[M_MenuManager] Hosting lobby...");
            host = true;
            connectionHandled = false;

            ShowLoading("Creating lobby and hosting...");

            if (XRINetworkGameManager.Connected.Value)
                OnConnected(true);
            else
                XRINetworkGameManager.Connected.Subscribe(OnConnected);

            try
            {
                XRINetworkGameManager.Instance.CreateNewLobby(roomName, false, XRINetworkGameManager.maxPlayers / 2);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[M_MenuManager] Failed to host: " + ex.Message);
                ShowLoading("Failed to host lobby.");
            }
        }

        public void OnJoinGameClicked()
        {
            if (XRINetworkGameManager.Instance == null) return;

            Debug.Log("[M_MenuManager] Joining lobby...");
            host = false;
            connectionHandled = false;

            ShowLoading("Searching for available lobbies...");

            if (XRINetworkGameManager.Connected.Value)
                OnConnected(true);
            else
                XRINetworkGameManager.Connected.Subscribe(OnConnected);

            try
            {
                XRINetworkGameManager.Instance.QuickJoinLobby();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[M_MenuManager] Failed to join: " + ex.Message);
                ShowLoading("Failed to join lobby.");
            }
        }

        private void OnConnected(bool connected)
        {
            if (connectionHandled) return;
            connectionHandled = true;

            XRINetworkGameManager.Connected.Unsubscribe(OnConnected);

            if (connected)
            {
                Debug.Log("[M_MenuManager] Successfully connected!");

                ShowLoading(host ? "Lobby hosted!" : "Lobby joined!");

                // Show lobby UI
                Invoke(nameof(ShowLobby), 1.25f);
            }
            else
            {
                Debug.LogWarning("[M_MenuManager] Connection failed.");
                ShowLoading("Connection failed.");
            }
        }

        private void ShowMainMenu()
        {
            mainMenuPanel.SetActive(true);
            loadingPanel.SetActive(false);
            lobbyPanel.SetActive(false);
        }

        private void ShowLoading(string message)
        {
            mainMenuPanel.SetActive(false);
            loadingPanel.SetActive(true);
            lobbyPanel.SetActive(false);
            loadingText.text = message;
        }

        private void ShowLobby()
        {
            mainMenuPanel.SetActive(false);
            loadingPanel.SetActive(false);
            lobbyPanel.SetActive(true);

            UpdateLobbyUI();

            startGameButton.gameObject.SetActive(XRINetworkGameManager.Instance.IsHost);
        }

        private void UpdateLobbyUI()
        {
            if (!Unity.Netcode.NetworkManager.Singleton.IsConnectedClient) return;

            int connected = Unity.Netcode.NetworkManager.Singleton.ConnectedClientsList.Count;
            int maxPlayers = XRINetworkGameManager.maxPlayers;

            lobbyPlayerListText.text = $"Players Connected:\n{connected}/{maxPlayers}";
            lobbyStatusText.text = "Connected!";
        }

        public void OnStartGameClicked()
        {
            Debug.Log("[M_MenuManager] Start Game button clicked.");
            // TODO: Add scene loading or game start logic here
        }
    }
}
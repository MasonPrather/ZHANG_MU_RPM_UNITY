using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.XR.CoreUtils.Bindings;
using Unity.XR.CoreUtils.Bindings.Variables;
using System;

namespace XRMultiplayer
{
    public class M_MenuManager : MonoBehaviour
    {
        [Header("Panels")]
        public GameObject mainMenuPanel;
        public GameObject loadingPanel;
        public GameObject lobbyPanel;

        [Header("Lobby UI")]
        public TMP_Text loadingText;
        public TMP_Text lobbyPlayerListText;
        public TMP_Text lobbyStatusText;
        public Button startGameButton;

        private LobbyManager lobbyManager => XRINetworkGameManager.Instance.lobbyManager;
        private IEventBinding statusObserver;

        private bool hasConnected = false;
        private bool awaitingConnection = false;

        private void Start()
        {
            ShowMainMenu();

            // Subscribe to lobby failure callback
            lobbyManager.OnLobbyFailed += HandleLobbyFailed;

            // Observe status BindableVariable
            statusObserver = LobbyManager.status.Subscribe(OnLobbyStatusChanged);
            statusObserver.Bind();

            // Listen for player join/leave
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        public void OnHostGameClicked()
        {
            ShowLoading("Creating lobby and hosting game...");
            awaitingConnection = true;
            _ = lobbyManager.CreateLobby();
        }

        public void OnJoinGameClicked()
        {
            ShowLoading("Searching for lobbies...");
            awaitingConnection = true;
            _ = lobbyManager.QuickJoinLobby();
        }

        public void OnStartGameClicked()
        {
            Debug.Log("Start Game button clicked.");
            // Insert your scene transition or network game start logic here
        }

        private void OnLobbyStatusChanged(string status)
        {
            if (status.ToLower().Contains("connected to lobby") && awaitingConnection && !hasConnected)
            {
                hasConnected = true;
                awaitingConnection = false;
                ShowLobby();
                UpdateLobbyUI();
            }
        }

        private void HandleLobbyFailed(string reason)
        {
            Debug.LogWarning($"[MenuManager] Lobby connection failed: {reason}");
            ShowMainMenu();
            loadingText.text = $"Failed to connect:\n{reason}";
            awaitingConnection = false;
        }

        public void ShowMainMenu()
        {
            mainMenuPanel.SetActive(true);
            loadingPanel.SetActive(false);
            lobbyPanel.SetActive(false);
        }

        public void ShowLoading(string message)
        {
            mainMenuPanel.SetActive(false);
            loadingPanel.SetActive(true);
            lobbyPanel.SetActive(false);
            loadingText.text = message;
        }

        public void ShowLobby()
        {
            mainMenuPanel.SetActive(false);
            loadingPanel.SetActive(false);
            lobbyPanel.SetActive(true);

            startGameButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);
        }

        public void UpdateLobbyUI()
        {
            if (!NetworkManager.Singleton.IsConnectedClient) return;

            int connected = NetworkManager.Singleton.ConnectedClientsList.Count;
            int maxPlayers = XRINetworkGameManager.maxPlayers;

            lobbyPlayerListText.text = $"Players Connected: {connected}/{maxPlayers}";
            lobbyStatusText.text = "Connected!";
        }

        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[MenuManager] Client connected: {clientId}");
            UpdateLobbyUI();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"[MenuManager] Client disconnected: {clientId}");
            UpdateLobbyUI();
        }

        private void OnDestroy()
        {
            statusObserver?.Unbind();
            if (lobbyManager != null)
                lobbyManager.OnLobbyFailed -= HandleLobbyFailed;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }
    }
}
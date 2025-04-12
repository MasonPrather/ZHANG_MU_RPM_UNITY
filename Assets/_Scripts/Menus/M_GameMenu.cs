using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRMultiplayer;
using UnityEngine.InputSystem;

public class M_GameMenu : MonoBehaviour
{
    [Header("UI Variables")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private GameObject lobbyMenuPrefab;
    [SerializeField] private TMP_Text statusText;

    [Header("Other Variables")]
    [SerializeField] private string roomName;
    [SerializeField] private InputActionProperty menuButtonAction;
    private GameObject lobbyMenu;
    private int playerMaxCount;
    private bool host;

    private void Start()
    {
        mainPanel.SetActive(true);
        loadingPanel.SetActive(false);
        playerMaxCount = XRINetworkGameManager.maxPlayers / 2;
        menuButtonAction.action.Enable();
        menuButtonAction.action.performed += OnMenuButtonPressed;
    }

    private void OnDestroy()
    {
        menuButtonAction.action.performed -= OnMenuButtonPressed;
    }

    private void OnMenuButtonPressed(InputAction.CallbackContext context)
    {
        if (mainPanel != null)
        {
            bool isActive = mainPanel.activeSelf;
            mainPanel.SetActive(!isActive);

            if (!isActive)
            {
                loadingPanel.SetActive(false);
            }
        }
    }

    public void HostLobby()
    {
        Debug.Log("[M_GameMenu] Hosting lobby...");
        host = true;

        Debug.Log($"[M_GameMenu] Connected.Value before subscribing: {XRINetworkGameManager.Connected.Value}");

        if (XRINetworkGameManager.Connected.Value)
        {
            Debug.Log("[M_GameMenu] Already connected, calling OnConnected(true)...");
            OnConnected(true);
        }
        else
        {
            Debug.Log("[M_GameMenu] Not connected yet, subscribing to changes...");
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
        }

        // Update UI
        mainPanel.SetActive(false);
        loadingPanel.SetActive(true);
        statusText.text = "Hosting lobby...";

        try
        {
            XRINetworkGameManager.Instance.CreateNewLobby(roomName, false, playerMaxCount);

            // Add a delay to check if it updates after some time
            Invoke(nameof(CheckConnectionStatus), 3f);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[M_GameMenu] Failed to create lobby: " + ex.Message);
        }
    }

    // Manually check after 3 seconds if the value changed
    private void CheckConnectionStatus()
    {
        Debug.Log($"[M_GameMenu] Checking connection status after delay: {XRINetworkGameManager.Connected.Value}");

        if (XRINetworkGameManager.Connected.Value)
        {
            OnConnected(true);
        }
    }

    public void QuickJoinLobby()
    {
        Debug.Log("[M_GameMenu] Quick joining lobby...");

        // **Check current connection state**
        if (XRINetworkGameManager.Connected.Value)
        {
            OnConnected(true);
        }
        else
        {
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
        }

        // Update UI
        mainPanel.SetActive(false);
        loadingPanel.SetActive(true);
        statusText.text = "Attempting to join lobby...";

        try
        {
            XRINetworkGameManager.Instance.QuickJoinLobby();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[M_GameMenu] Quick join failed: " + ex.Message);
        }
    }

    private void OnConnected(bool connected)
    {
        Debug.Log("[M_GameMenu] OnConnected triggered!");

        if (connected)
        {
            Debug.Log("[M_GameMenu] Connection successful!");
            loadingPanel.SetActive(false);

            if (host)
            {
                lobbyMenu = Instantiate(lobbyMenuPrefab, transform.parent);
                lobbyMenu.SetActive(true);
            }

            // Unsubscribe after successful connection
            XRINetworkGameManager.Connected.Unsubscribe(OnConnected);
        }
        else
        {
            Debug.LogWarning("[M_GameMenu] Connection failed.");
        }
    }
}
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

    [Header("Settings")]
    [SerializeField] private string roomName;
    [SerializeField] private InputActionProperty menuButtonAction;

    private GameObject lobbyMenu;
    private bool host;
    private bool connectionHandled;

    private void Start()
    {
        mainPanel.SetActive(true);
        loadingPanel.SetActive(false);

        menuButtonAction.action.Enable();
        menuButtonAction.action.performed += OnMenuButtonPressed;
    }

    private void OnDestroy()
    {
        menuButtonAction.action.performed -= OnMenuButtonPressed;
    }

    private void OnMenuButtonPressed(InputAction.CallbackContext context)
    {
        if (mainPanel == null) return;

        bool isActive = mainPanel.activeSelf;
        mainPanel.SetActive(!isActive);

        if (!isActive)
        {
            loadingPanel.SetActive(false);
        }
    }

    public void HostLobby()
    {
        if (XRINetworkGameManager.Instance == null) return;

        Debug.Log("[M_GameMenu] Hosting lobby...");
        host = true;
        connectionHandled = false;

        ShowLoading("Hosting lobby...");

        if (XRINetworkGameManager.Connected.Value)
        {
            OnConnected(true); // Already connected
        }
        else
        {
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
        }

        try
        {
            XRINetworkGameManager.Instance.CreateNewLobby(roomName, false, XRINetworkGameManager.maxPlayers / 2);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[M_GameMenu] Failed to create lobby: " + ex.Message);
            ShowLoading("Failed to host lobby.");
        }
    }

    public void QuickJoinLobby()
    {
        if (XRINetworkGameManager.Instance == null) return;

        Debug.Log("[M_GameMenu] Quick joining lobby...");
        host = false;
        connectionHandled = false;

        ShowLoading("Attempting to join lobby...");

        if (XRINetworkGameManager.Connected.Value)
        {
            OnConnected(true);
        }
        else
        {
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
        }

        try
        {
            XRINetworkGameManager.Instance.QuickJoinLobby();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[M_GameMenu] Quick join failed: " + ex.Message);
            ShowLoading("Failed to join lobby.");
        }
    }

    private void OnConnected(bool connected)
    {
        if (connectionHandled) return;
        connectionHandled = true;

        Debug.Log("[M_GameMenu] OnConnected triggered!");
        XRINetworkGameManager.Connected.Unsubscribe(OnConnected);

        if (connected)
        {
            Debug.Log("[M_GameMenu] Connection successful!");

            // Show success message
            ShowLoading(host ? "Lobby hosted!" : "Joined lobby!");

            // Spawn lobby UI if host
            if (host && lobbyMenu == null)
            {
                lobbyMenu = Instantiate(lobbyMenuPrefab, transform.parent);
                lobbyMenu.SetActive(true);
            }

            // Delay hiding loading screen for UX clarity
            Invoke(nameof(HideLoading), 1.25f);
        }
        else
        {
            Debug.LogWarning("[M_GameMenu] Connection failed.");
            ShowLoading("Connection failed.");
        }
    }

    private void ShowLoading(string message)
    {
        mainPanel.SetActive(false);
        loadingPanel.SetActive(true);
        statusText.text = message;
    }

    private void HideLoading()
    {
        loadingPanel.SetActive(false);
    }
}
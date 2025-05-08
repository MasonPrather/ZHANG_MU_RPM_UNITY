using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRMultiplayer;

public class M_GameMenu : MonoBehaviour
{
    [Header("UI Variables")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private GameObject lobbyMenuPrefab;
    [SerializeField] private TMP_Text statusText;

    [Header("Settings")]
    [SerializeField] private string roomName;

    [Header("Input Settings")]
    [SerializeField] private OVRInput.Button toggleMenuButton = OVRInput.Button.Start; // Can use .Start, .One, .Two, etc.

    private GameObject lobbyMenu;
    private bool host;
    private bool connectionHandled;

    private void Start()
    {
        mainPanel.SetActive(true);
        loadingPanel.SetActive(false);
    }

    private void Update()
    {
        if (OVRInput.GetDown(toggleMenuButton))
        {
            ToggleMenu();
        }
    }

    private void ToggleMenu()
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
            OnConnected(true);
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

            ShowLoading(host ? "Lobby hosted!" : "Joined lobby!");

            if (host && lobbyMenu == null)
            {
                lobbyMenu = Instantiate(lobbyMenuPrefab, transform.parent);
                lobbyMenu.SetActive(true);
            }

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
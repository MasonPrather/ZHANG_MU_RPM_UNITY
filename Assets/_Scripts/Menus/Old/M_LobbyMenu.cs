using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public class M_LobbyMenu : NetworkBehaviour
{
    [Header("UI Elements")]
    public GameObject lobbyMenuPanel;  // The second menu panel
    public TMP_Text lobbyStatusText;   // Displays number of players
    public TMP_Text hostNameText;      // Displays host player's name
    public TMP_Text playerNameText;    // Displays joining player's name
    public Button startGameButton;     // Button to start game

    [Header("Audio")]
    public AudioSource startGameSound; // Sound effect when game starts

    private NetworkVariable<int> playerCount = new NetworkVariable<int>(0);
    private NetworkVariable<string> hostPlayerName = new NetworkVariable<string>();
    private NetworkVariable<string> joinPlayerName = new NetworkVariable<string>();

    // Static reference to track the existing instance
    private static M_LobbyMenu existingInstance;

    private void Awake()
    {
        if (existingInstance != null)
        {
            Debug.LogWarning("Duplicate LobbyMenu detected. Destroying this instance.");
            Destroy(gameObject);
            return;
        }

        existingInstance = this;
        DontDestroyOnLoad(gameObject); // Optional, keeps the menu persistent if needed
    }

    private void Start()
    {
        startGameButton.gameObject.SetActive(false); // Hide "Start Game" button initially
    }

    public override void OnNetworkSpawn()
    {
        if (IsClient)
        {
            playerCount.OnValueChanged += UpdateLobbyStatus;
            hostPlayerName.OnValueChanged += UpdateHostNameUI;
            joinPlayerName.OnValueChanged += UpdateJoinNameUI;
        }

        if (IsHost)
        {
            hostPlayerName.Value = "VP16"; // Assign default name to host
            playerCount.Value++;
        }
    }

    private void UpdateLobbyStatus(int oldCount, int newCount)
    {
        lobbyStatusText.text = $"Players Connected: {newCount}/2";

        if (newCount == 2 && IsHost)
        {
            startGameButton.gameObject.SetActive(true); // Show "Start Game" button only for host
        }
    }

    private void UpdateHostNameUI(string previous, string current)
    {
        hostNameText.text = current.ToString();
    }

    private void UpdateJoinNameUI(string previous, string current)
    {
        playerNameText.text = current.ToString();
    }

    [ServerRpc(RequireOwnership = false)]
    public void AssignJoiningPlayerServerRpc()
    {
        if (playerCount.Value < 2)
        {
            playerCount.Value++;
            joinPlayerName.Value = "Ara C"; // Assign name to joining player
        }
    }

    public void StartGame()
    {
        if (IsHost)
        {
            StartGameClientRpc();
        }
    }

    [ClientRpc]
    private void StartGameClientRpc()
    {
        startGameSound.Play(); // Play sound for all players
        StartCoroutine(TransitionToGame());
    }

    private IEnumerator TransitionToGame()
    {
        yield return new WaitForSeconds(1.5f); // Short delay before hiding menu
        lobbyMenuPanel.SetActive(false); // Hide lobby UI
        // Load game scene here if needed
    }

    private void OnDestroy()
    {
        if (existingInstance == this)
        {
            existingInstance = null;
        }
    }
}
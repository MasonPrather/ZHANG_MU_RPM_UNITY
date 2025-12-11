/*
 * OneClickMatchMenu.cs
 * - Host/Join with lightweight UI flow guards.
 */

using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meta.XR.MultiplayerBlocks.Shared
{
    public class M_MenuManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CustomMatchmaking matchmaking;

        [Header("UI/Flow")]
        [SerializeField] private string gameplayScene = "Game";
        [SerializeField, Min(1f)] private float joinTimeoutSec = 8f;

        [Header("Panels")]
        [SerializeField] private GameObject MainPanel;
        [SerializeField] private GameObject HostingPanel;        // “Hosting Lobby…”
        [SerializeField] private GameObject JoiningPanel;        // “Joining Lobby…”
        [SerializeField] private GameObject JoinFailedPanel;     // “No public games found…”
        [SerializeField] private GameObject LobbyHostedPanel;    // success toast (host)
        [SerializeField] private GameObject LobbyJoinedPanel;    // success toast (join)
        [SerializeField, Min(0.5f)] private float successToastSeconds = 3f;

        [Header("Room Defaults (applied when Hosting)")]
        [SerializeField] private string lobbyName = "myLobby"; // groups rooms
        [SerializeField, Range(2, 32)] private int maxPlayers = 8;
        [SerializeField] private bool isPrivate = false;       // keep false for auto-join
        [SerializeField] private bool passwordProtect = false; // keep false for one-click

        private void Reset()
        {
            matchmaking = GetComponent<CustomMatchmaking>();
        }

        private void Start()
        {
            if (M_VivoxManager.Instance != null)
            {
                M_VivoxManager.Instance.VivoxReady += OnVivoxReady;
                M_VivoxManager.Instance.ChannelJoined += OnVoiceChannelJoined;
                M_VivoxManager.Instance.ChannelLeft += OnVoiceChannelLeft;
            }
        }

        private void Awake()
        {
            if (matchmaking == null)
                matchmaking = GetComponent<CustomMatchmaking>();

            // Optional feedback hooks
            if (matchmaking != null)
            {
                matchmaking.onRoomCreationFinished.AddListener(OnRoomCreated);
                matchmaking.onRoomJoinFinished.AddListener(OnRoomJoined);
                matchmaking.onRoomLeaveFinished.AddListener(OnRoomLeft);
            }

            // Default to main on boot
            ShowOnly(MainPanel);
        }

        private void OnDestroy()
        {
            if (M_VivoxManager.Instance != null)
            {
                M_VivoxManager.Instance.VivoxReady -= OnVivoxReady;
                M_VivoxManager.Instance.ChannelJoined -= OnVoiceChannelJoined;
                M_VivoxManager.Instance.ChannelLeft -= OnVoiceChannelLeft;
            }
        }

        // -------------------- UI Buttons --------------------

        public async void OnClick_Host()
        {
            if (matchmaking == null) { Debug.LogError("CustomMatchmaking ref missing."); return; }

            // Guard UI
            ShowOnly(HostingPanel);

            try
            {
                matchmaking.LobbyName = string.IsNullOrWhiteSpace(lobbyName) ? "myLobby" : lobbyName;
                matchmaking.MaxPlayersPerRoom = maxPlayers;
                matchmaking.IsPrivate = isPrivate;
                matchmaking.IsPasswordProtected = passwordProtect;

                var result = await matchmaking.CreateRoom();
                if (!result.IsSuccess)
                {
                    Debug.LogWarning($"Host failed: {result.ErrorMessage}");
                    // Bounce back to main so user can try again
                    ShowOnly(MainPanel);
                    return;
                }

                // Success: quick toast, then clear UI, then load scene
                yieldToastThenHide(LobbyHostedPanel, successToastSeconds);
                await LoadGameplayAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"Host exception: {e.Message}");
                ShowOnly(MainPanel);
            }
        }

        public async void OnClick_Join()
        {
            if (matchmaking == null) { Debug.LogError("CustomMatchmaking ref missing."); return; }

            // Guard UI
            ShowOnly(JoiningPanel);

            try
            {
                var lobby = string.IsNullOrWhiteSpace(lobbyName) ? matchmaking.LobbyName : lobbyName;
                if (string.IsNullOrWhiteSpace(lobby)) lobby = "myLobby";

                var joinTask = matchmaking.JoinOpenRoom(lobby);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(joinTimeoutSec));

                var completed = await Task.WhenAny(joinTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    Debug.Log("[Join] Timeout. No open rooms (or network slow). Staying in menu.");
                    ShowOnly(JoinFailedPanel);
                    return;
                }

                var result = await joinTask;
                if (!result.IsSuccess)
                {
                    Debug.LogWarning($"[Join] Failed: {result.ErrorMessage}");
                    ShowOnly(JoinFailedPanel);
                    return;
                }

                // Success: quick toast, then clear UI, then load scene
                yieldToastThenHide(LobbyJoinedPanel, successToastSeconds);
                await LoadGameplayAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Join exception: {e.Message}");
                ShowOnly(JoinFailedPanel);
            }
        }

        // Button on JoinFailedPanel -> back to main
        public void OnClick_ReturnToMain()
        {
            M_VivoxManager.Instance?.LeaveChannel();
            ShowOnly(MainPanel);
        }

        // -------------------- Scene / UI helpers --------------------

        private async Task LoadGameplayAsync()
        {
            if (SceneManager.GetActiveScene().name != gameplayScene)
                await SceneManager.LoadSceneAsync(gameplayScene);
        }

        private void ShowNoLobbiesFound()
        {
            ShowOnly(JoinFailedPanel);
        }

        private void ShowOnly(GameObject target)
        {
            // Disable all known panels, then enable the one we want (if provided)
            SetActiveSafe(MainPanel, false);
            SetActiveSafe(HostingPanel, false);
            SetActiveSafe(JoiningPanel, false);
            SetActiveSafe(JoinFailedPanel, false);
            SetActiveSafe(LobbyHostedPanel, false);
            SetActiveSafe(LobbyJoinedPanel, false);

            SetActiveSafe(target, true);
        }

        private void HideAllPanels()
        {
            ShowOnly(null);
        }

        private void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private void yieldToastThenHide(GameObject toastPanel, float seconds)
        {
            if (toastPanel == null) { HideAllPanels(); return; }
            ShowOnly(toastPanel);
            StartCoroutine(AutoHideToast(seconds));
        }

        private IEnumerator AutoHideToast(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            HideAllPanels(); // leaves no UI visible
        }

        // -------------------- Optional event hooks --------------------

        private void OnRoomCreated(CustomMatchmaking.RoomOperationResult result)
        {
            if (result.IsSuccess)
            {
                Debug.Log($"Room created. Token={result.RoomToken}");
                StartCoroutine(JoinVivoxWhenReady(GetLobbyName()));
            }
        }

        private void OnRoomJoined(CustomMatchmaking.RoomOperationResult result)
        {
            if (result.IsSuccess)
            {
                Debug.Log($"Joined room. Token={result.RoomToken}");
                StartCoroutine(JoinVivoxWhenReady(GetLobbyName()));
            }
        }

        // --- Where to LEAVE voice ---

        private void OnRoomLeft()
        {
            Debug.Log("Left room.");
            M_VivoxManager.Instance?.LeaveChannel();
        }

        // --- Helpers ---

        private IEnumerator JoinVivoxWhenReady(string channel)
        {
            yield return new WaitUntil(() =>
                M_VivoxManager.Instance != null && M_VivoxManager.Instance.IsReady
            );
            M_VivoxManager.Instance.JoinChannel(channel);
        }

        private string GetLobbyName()
        {
            var name = string.IsNullOrWhiteSpace(lobbyName) ? matchmaking?.LobbyName : lobbyName;
            return string.IsNullOrWhiteSpace(name) ? "myLobby" : name;
        }

        // --- Optional: Vivox event logs ---

        private void OnVivoxReady() => Debug.Log("[Vivox] Ready");
        private void OnVoiceChannelJoined(string chan) => Debug.Log($"[Vivox] Joined {chan}");
        private void OnVoiceChannelLeft(string chan) => Debug.Log($"[Vivox] Left {chan}");
    }
}
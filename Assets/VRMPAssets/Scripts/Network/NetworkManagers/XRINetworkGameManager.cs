using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.XR.CoreUtils.Bindings.Variables;
using UnityEngine;
using UnityEditor;
using Unity.Services.Multiplayer;
using Unity.Netcode.Transports.UTP;
using System.Net.Sockets;
using System.Net;

#if UNITY_EDITOR && HAS_MPPM

#endif

namespace XRMultiplayer
{
    /// <summary>
    /// Manages the high level connection for a networked game session.
    /// </summary>
    [RequireComponent(typeof(SessionManager)), RequireComponent(typeof(AuthenticationManager))]
    public class XRINetworkGameManager : MonoBehaviour
    {
        /// <summary>
        /// Determines the current state of the networked game connection.
        /// </summary>
        ///<remarks>
        /// None: No connection state.
        /// Authenticating: Currently authenticating.
        /// Authenticated: Authenticated.
        /// Connecting: Currently connecting to a lobby.
        /// Connected: Connected to a lobby.
        /// </remarks>
        public enum ConnectionState
        {
            None,
            Authenticating,
            Authenticated,
            Connecting,
            Connected
        }

        /// <summary>
        /// Max amount of players allowed when creating a new room.
        /// </summary>
        public const int maxPlayers = 20;

        /// <summary>
        /// Singleton Reference for access to this manager.
        /// </summary>
        public static XRINetworkGameManager Instance => s_Instance;
        static XRINetworkGameManager s_Instance;

        /// <summary>
        /// OwnerClientId that gets set for the local player when connecting to a game.
        /// </summary>
        public static ulong LocalId;

        /// <summary>
        /// Authentication Id that gets passed once Authenticated.
        /// </summary>
        public static string AuthenicationId;

        /// <summary>
        /// Internal Room Code set by Lobby.
        /// </summary>
        public static string ConnectedRoomCode;

        /// <summary>
        /// Current connected region set by Lobby and Relay.
        /// </summary>
        public static string ConnectedRoomRegion;

        /// <summary>
        /// Bindable Variable that gets updated when changing the the currently connected room.
        /// </summary>
        public static BindableVariable<string> ConnectedRoomName = new("");

        /// <summary>
        /// Bindable Variable that gets updated when the local player changes name.
        /// </summary>
        public static BindableVariable<string> LocalPlayerName = new("Player");

        /// <summary>
        /// Bindable Variable that gets updated when the local player changes color.
        /// </summary>
        public static BindableVariable<Color> LocalPlayerColor = new(Color.white);

        /// <summary>
        /// Bindable Variable that gets updated when a player connects or disconnects from a networked game.
        /// </summary>
        public static IReadOnlyBindableVariable<bool> Connected
        {
            get => m_Connected;
        }
        static BindableVariable<bool> m_Connected = new BindableVariable<bool>(false);

        /// <summary>
        /// Bindable Variable that gets updated throughout the authentication and connection process.
        /// See <see cref="ConnectionState"/>
        /// </summary>
        public static IReadOnlyBindableVariable<ConnectionState> CurrentConnectionState
        {
            get => m_ConnectionState;
        }
        static BindableEnum<ConnectionState> m_ConnectionState = new BindableEnum<ConnectionState>(ConnectionState.None);

        /// <summary>
        /// Returns the current session type.
        /// </summary>
        /// <remarks>
        /// NOTE: This is only available after Instance is set (Awake).
        /// This will check <see cref="NetworkReachability.NotReachable"/>'s state and override the session type to <see cref="SessionType.LocalOnly"/>  if no internet connection is available.
        /// </remarks>
        /// <returns>
        /// The current session type, either <see cref="SessionType.DistributedAuthority"/> or <see cref="SessionType.LocalOnly"/>.
        /// </returns>
        public static SessionType CurrentSessionType
        {
            get
            {
                SessionType defaultSessionType;
                try
                {
                    defaultSessionType = Instance.sessionManager.sessionType;

                    // If the session type is Distributed Authority and there is no internet connection, fallback to LocalOnly.
                    if (defaultSessionType == SessionType.DistributedAuthority && Application.internetReachability == NetworkReachability.NotReachable)
                        defaultSessionType = SessionType.LocalOnly;
                }
                catch (Exception ex)
                {
                    // If Instance is not set, log the error and return the default session type as local.
                    Utils.Log($"{k_DebugPrepend}Error getting CurrentSessionType: {ex.Message}", 1);
                    defaultSessionType = SessionType.LocalOnly;
                }

                return defaultSessionType;
            }
        }

        /// <summary>
        /// Auto connects to the player to a networked game session once they connect to a lobby.
        /// Uncheck if you want to handle joining a networked session separately.
        /// </summary>
        public bool autoConnectOnLobbyJoin { get => m_AutoConnectOnLobbyJoin; }
        [SerializeField] bool m_AutoConnectOnLobbyJoin = true;

        /// <summary>
        /// Flag for updating positional voice chat.
        /// </summary>
        /// <remarks>
        /// This will be removed in the future with the Vivox v16 update.
        /// </remarks>
        public bool positionalVoiceChat = false;

        /// <summary>
        /// Action for when a player connects or disconnects.
        /// </summary>
        public Action<ulong, bool> OnPlayerStateChanged;

        /// <summary>
        /// Action for when connection status is updated.
        /// </summary>
        public Action<string> OnConnectionUpdated;

        /// <summary>
        /// Action for when connection fails.
        /// </summary>
        public Action<string> OnConnectionFailedAction;

        public Action<ulong> OnSessionOwnerPromoted;

        /// <summary>
        /// Lobby Manager handles the Lobby and Relay work between players.
        /// </summary>
        public SessionManager sessionManager => m_SessionManager;
        SessionManager m_SessionManager;

        /// <summary>
        /// Lobby Manager handles the Lobby and Relay work between players.
        /// </summary>
        public AuthenticationManager authenticationManager => m_AuthenticationManager;
        AuthenticationManager m_AuthenticationManager;

        /// <summary>
        /// List that handles all current players by ID.
        /// Useful for getting specific players.
        /// See <see cref="TryGetPlayerByID"/>
        /// </summary>
        readonly List<ulong> m_CurrentPlayerIDs = new();

        /// <summary>
        /// Flagged whenever the application is in the process of shutting down.
        /// </summary>
        bool m_IsShuttingDown = false;

        const string k_DebugPrepend = "<color=#FAC00C>[Network Game Manager]</color> ";

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected virtual async void Awake()
        {
            // Check for existing singleton reference. If once already exists early out.
            if (s_Instance != null)
            {
                Utils.Log($"{k_DebugPrepend}Duplicate XRINetworkGameManager found, destroying.", 2);
                Destroy(gameObject);
                return;
            }
            s_Instance = this;

            // Check for Lobby Manager, if none exist, early out.
            if (TryGetComponent(out m_SessionManager) && TryGetComponent(out m_AuthenticationManager))
            {
                m_SessionManager.OnSessionFailed += ConnectionFailed;
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Missing Managers, Disabling Component", 2);
                enabled = false;
                return;
            }

#if UNITY_EDITOR

            bool skipCloudCheck = false;
# if HAS_MPPM
            if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            {
                skipCloudCheck = true;
            }
# endif
            // Check if the project is linked to Unity Cloud and that it's not a MPPM Clone.
            if (!CloudProjectSettings.projectBound && !skipCloudCheck)
            {
                Utils.Log($"{k_DebugPrepend}Project has not been linked to Unity Cloud." +
                               "\nThe VR Multiplayer Template utilizes Unity Gaming Services and must be linked to Unity Cloud." +
                               "\nGo to <b>Settings -> Project Settings -> Services</b> and link your project.", 2);
            }
#endif

            // Initialize bindable variables.
            m_Connected.Value = false;
            // Update connection state.
            m_ConnectionState.Value = ConnectionState.Authenticating;

            // If using Distributed Authority, wait for Authentication to complete.
            if (CurrentSessionType == SessionType.DistributedAuthority)
            {
                bool signedIn = await m_AuthenticationManager.Authenticate();
                if (!signedIn)
                {
                    Utils.Log($"{k_DebugPrepend}Failed to Authenticate.", 1);
                    ConnectionFailed("Failed to Authenticate.");
                    PlayerHudNotification.Instance.ShowText($"Failed to Authenticate.");
                    return;
                }
            }

            m_ConnectionState.Value = ConnectionState.Authenticated;
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected virtual void Start()
        {
            NetworkManager.Singleton.OnClientStopped += LocalClientStopped;
            NetworkManager.Singleton.OnSessionOwnerPromoted += SessionOwnerPromoted;
        }

        void SessionOwnerPromoted(ulong sessionOwnerId)
        {
            OnSessionOwnerPromoted?.Invoke(sessionOwnerId);
            if (TryGetPlayerByID(sessionOwnerId, out XRINetworkPlayer player))
            {
                PlayerHudNotification.Instance.ShowText($"<b>Status:</b> {player.playerName} now the Host.");
            }
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        public void OnDestroy()
        {
            ShutDown();
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        private void OnApplicationQuit()
        {
            ShutDown();
        }

        async void ShutDown()
        {
            if (m_IsShuttingDown) return;
            m_IsShuttingDown = true;

            // Remove callbacks
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientStopped -= LocalClientStopped;
            }

            await m_SessionManager.LeaveSession();
        }

        public bool IsAuthenticated()
        {
            return m_SessionManager.sessionType != SessionType.DistributedAuthority || AuthenticationManager.IsAuthenticated();
        }
        /// <summary>
        /// Called from XRINetworkPlayer once they have spawned.
        /// </summary>
        /// <param name="localPlayerId">Sets based on <see cref="NetworkObject.OwnerClientId"/> from the local player</param>
        public virtual void OnLocalClientStarted(ulong localPlayerId)
        {
            LocalId = localPlayerId;
            m_ConnectionState.Value = ConnectionState.Connected;
            PlayerHudNotification.Instance.ShowText($"<b>Status:</b> Connected");
            Utils.Log($"{k_DebugPrepend}Local Player Started with ID: {localPlayerId}", 0);
        }

        /// <summary>
        /// Called when disconnected from any networked game.
        /// </summary>
        /// <param name="id">
        /// Local player id.
        /// </param>
        protected virtual void LocalClientStopped(bool id)
        {
            m_Connected.Value = false;
            m_CurrentPlayerIDs.Clear();
            PlayerHudNotification.Instance.ShowText($"<b>Status:</b> Disconnected");
            // Check if authenticated on disconnect.
            if (IsAuthenticated())
            {
                m_ConnectionState.Value = ConnectionState.Authenticated;
            }
            else
            {
                m_ConnectionState.Value = ConnectionState.None;
            }
        }

        /// <summary>
        /// Finds all <see cref="XRINetworkPlayer"/>'s existing in the scene and gets the <see cref="XRINetworkPlayer"/>
        /// based on <see cref="NetworkObject.OwnerClientId"/> for that player.
        /// </summary>
        /// <param name="id">
        /// <see cref="NetworkObject.OwnerClientId"/> of the player.
        /// </param>
        /// <param name="player">
        /// Out <see cref="XRINetworkPlayer"/>.
        /// </param>
        /// <returns>
        /// Returns true based on whether or not a player with that Id exists.
        /// </returns>
        public virtual bool TryGetPlayerByID(ulong id, out XRINetworkPlayer player)
        {
            // Find all existing players in scene. This is a workaround until NGO exposes client side player list (2.x I believe - JG).
            XRINetworkPlayer[] allPlayers = FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None);

            //Loops through existing players and returns true if player with id is found.
            foreach (XRINetworkPlayer p in allPlayers)
            {
                if (p.NetworkObject.OwnerClientId == id)
                {
                    player = p;
                    return true;
                }
            }
            player = null;
            return false;
        }

        [ContextMenu("Show All NetworkClients")]
        void ShowAllNetworkClients()
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClients)
            {
                Debug.Log($"Client: {client.Key}, {client.Value.PlayerObject.name}");
            }
        }

        /// <summary>
        /// This function will set the player ID in the list <see cref="m_CurrentPlayerIDs"/> and
        /// invokes the callback <see cref="OnPlayerStateChanged"/>.
        /// </summary>
        /// <param name="playerID"><see cref="NetworkObject.OwnerClientId"/> of the joined player.</param>
        /// <remarks>Called from <see cref="XRINetworkPlayer.CompleteSetup"/>.</remarks>
        public virtual void PlayerJoined(ulong playerID)
        {
            // If playerID is not already registered, then add.
            if (!m_CurrentPlayerIDs.Contains(playerID))
            {
                m_CurrentPlayerIDs.Add(playerID);
                OnPlayerStateChanged?.Invoke(playerID, true);
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Trying to Add a player ID [{playerID}] that already exists", 1);
            }
        }

        /// <summary>
        /// Called from <see cref="XRINetworkPlayer.OnDestroy"/>.
        /// </summary>
        /// <param name="playerID"><see cref="NetworkObject.OwnerClientId"/> of the player who left.</param>
        public virtual void PlayerLeft(ulong playerID)
        {
            // Check to make sure player has been registerd.
            if (m_CurrentPlayerIDs.Contains(playerID))
            {
                m_CurrentPlayerIDs.Remove(playerID);
                OnPlayerStateChanged?.Invoke(playerID, false);
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Trying to remove a player ID [{playerID}] that doesn't exist", 1);
            }
        }

        /// <summary>
        /// Called whenever there is a problem with connecting to game or lobby.
        /// </summary>
        /// <param name="reason">Failure message.</param>
        public virtual void ConnectionFailed(string reason)
        {
            OnConnectionFailedAction?.Invoke(reason);
            m_ConnectionState.Value = IsAuthenticated() ? ConnectionState.Authenticated : ConnectionState.None;
        }

        /// <summary>
        /// Called whenever there is an update to connection status.
        /// </summary>
        /// <param name="update">Status update message.</param>
        public virtual void ConnectionUpdated(string update)
        {
            OnConnectionUpdated?.Invoke(update);
        }

        /// <summary>
        /// Joins a random lobby. If no lobbies exist, it will create a new one.
        /// </summary>
        public virtual async void QuickJoinLobby()
        {
            Utils.Log($"{k_DebugPrepend}Joining Lobby by Quick Join.");
            if (await AbleToConnect())
            {
                ConnectToSession(await m_SessionManager.QuickJoinLobby());
            }
        }

        /// <summary>
        /// Called when trying to join a Lobby by Room Code.
        /// </summary>
        /// <param name="lobby">Lobby to join.</param>
        public virtual async void JoinLobbyByCode(string code)
        {
            Utils.Log($"{k_DebugPrepend}Joining Lobby by room code: {code}.");
            if (await AbleToConnect())
            {
                ConnectToSession(await m_SessionManager.JoinLobby(roomCode: code));
            }
        }

        /// <summary>
        /// Called when trying to join a specific Lobby.
        /// </summary>
        /// <param name="session">Lobby to join.</param>
        public virtual async void JoinLobbySpecific(ISessionInfo session)
        {
            Utils.Log($"{k_DebugPrepend}Joining specific Lobby: {session.Name}.");
            if (await AbleToConnect())
            {
                ConnectToSession(await m_SessionManager.JoinLobby(sessionInfo: session));
            }
        }

        /// <summary>
        /// Creates a new Lobby.
        /// </summary>
        /// <param name="roomName">Name of the lobby.</param>
        /// <param name="isPrivate">Whether or not the lobby is private.</param>
        /// <param name="playerCount">Maximum allowed players.</param>
        public virtual async void CreateNewLobby(string roomName = null, bool isPrivate = false, int playerCount = maxPlayers)
        {
            Utils.Log($"{k_DebugPrepend}Creating New Lobby: {roomName}.");
            if (await AbleToConnect())
            {
                ConnectToSession(await m_SessionManager.CreateSession(roomName, isPrivate, playerCount));
            }
        }

        /// <summary>
        /// Checks if a we are currently able to connect to a lobby.
        /// If already connected it will disconnect in attempt to "Hot Join" a new lobby.
        /// </summary>
        /// <returns>Whether or not we are able to connect.</returns>
        protected virtual async Task<bool> AbleToConnect()
        {
            // If in the process of trying to connect, send failure message and return false.
            if (m_ConnectionState.Value == ConnectionState.Connecting)
            {
                string failureMessage = "Connection attempt still in progress.";
                Utils.Log($"{k_DebugPrepend}{failureMessage}", 1);
                ConnectionFailed(failureMessage);
                return false;
            }

            // If already connected to a lobby, disconnect in attempt to "Hot Join".
            if (Connected.Value || m_ConnectionState.Value == ConnectionState.Connected)
            {
                Utils.Log($"{k_DebugPrepend}Already Connected to a Lobby. Disconnecting.", 0);
                await DisconnectAsync();

                // Small wait while everything finishes disconnecting.
                // This isn't technically needed, but makes the flow feel better.
                await Task.Delay(100);
            }

            m_ConnectionState.Value = ConnectionState.Connecting;
            return true;
        }

        /// <summary>
        /// Connect to a lobby.
        /// </summary>
        /// <param name="session">Lobby to connect to.</param>
        protected virtual void ConnectToSession(ISession session)
        {
            // Send failure message if we can't connect.
            if (session == null)
            {
                FailedToConnect();
            }
            else
            {
                ConnectedRoomCode = session.Code;
                ConnectedRoomName.Value = session.Name;
                m_Connected.Value = true;
            }
        }

        /// <summary>
        /// Generic failure message.
        /// </summary>
        protected virtual void FailedToConnect(string reason = null)
        {
            string failureMessage = "Failed to connect to lobby.";
            if (reason != null)
            {
                failureMessage = $"{reason}";
            }
            Utils.Log($"{k_DebugPrepend}{failureMessage}", 1);
        }

        /// <summary>
        /// Cancel current matchmaking.
        /// Called from the Lobby UI.
        /// </summary>
        public virtual async void CancelMatchmaking()
        {
            if (IsAuthenticated())
            {
                m_ConnectionState.Value = ConnectionState.Authenticated;
            }

            await m_SessionManager.LeaveSession();
        }

        /// <summary>
        /// High Level Disconnect call.
        /// </summary>
        public virtual async void Disconnect()
        {
            if (CurrentSessionType == SessionType.DistributedAuthority)
                await DisconnectAsync();
            else
                LeaveLocalConnection();
        }

        /// <summary>
        /// Awaitable Disconnect call, used for Hot Joining.
        /// </summary>
        /// <returns></returns>
        public virtual async Task DisconnectAsync()
        {
            await m_SessionManager.LeaveSession();
            m_Connected.Value = false;
            if (IsAuthenticated())
            {
                m_ConnectionState.Value = ConnectionState.Authenticated;
            }
            else
            {
                m_ConnectionState.Value = ConnectionState.None;
            }
            Utils.Log($"{k_DebugPrepend}Disconnected from Game.");
        }

        /// <summary>
        /// Hosts a local connection.
        /// This will use the local IP address of the device to connect.
        /// </summary>
        public virtual bool HostLocalConnection()
        {
            string localIP = GetLocalIPAddress();

            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;

            transport.ConnectionData.Address = localIP;
            ConnectedRoomName.Value = "Local Room";
            ConnectedRoomCode = localIP;
            return NetworkManager.Singleton.StartHost();
        }

        /// <summary>
        /// Joins a local connection as a client.
        /// This will use the the IP address the user manually sets in the UnityTransport.
        /// </summary>
        public virtual bool JoinLocalConnection()
        {
            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
            ConnectedRoomName.Value = "Local Room";
            ConnectedRoomCode = transport.ConnectionData.Address;
            return NetworkManager.Singleton.StartClient();
        }

        /// <summary>
        /// Leaves the local connection, either as a host or client.
        /// </summary>
        public virtual void LeaveLocalConnection()
        {
            NetworkManager.Singleton.Shutdown();
        }


        /// <summary>
        /// Gets the local IP address.
        /// </summary>
        /// <returns>Returns string of local IP address.</returns>
        /// <remarks>This may not work in all environments, especially if the device has multiple network interfaces.</remarks>
        public virtual string GetLocalIPAddress()
        {
            string localIP = "127.0.0.1";
            try
            {
                string host = "8.8.8.8"; // Google's public DNS server, used to determine the local IP address.
                int port = 65530; // Arbitrary port number, not used for actual communication.

                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect(host, port);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    localIP = endPoint.Address.ToString();
                }
            }
            catch (Exception e)
            {
                Utils.Log($"{k_DebugPrepend}Failed to get local IP: {e.Message}", 1);
            }
            return localIP;
        }
    }
}
